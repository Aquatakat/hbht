using System.Numerics;
using Aidyn.Core.Audio;
using Aidyn.Core.Cameras;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>What the title shows: its cards, or the title itself over the fly-throughs.</summary>
public enum TitlePhase
{
    /// <summary>The intro cards over black (<see cref="IntroCards"/>).</summary>
    Cards,

    /// <summary>The logo and PRESS START, or the menu, over the Overworld's fly-throughs (game mode 0xF).</summary>
    Title,

    /// <summary>New Game or a save loaded: the screen fades out, and the title ends (<see cref="TitleScreen.Exit"/>).</summary>
    Leaving,
}

/// <summary>
/// The title screen (<c>Title.Frame</c>, <c>0x8001b154</c>, then game mode 0xF, <c>Title.ModeFrame</c>, <c>0x800551c0</c>): the intro
/// cards, then the Overworld's fly-throughs (<see cref="TitleRecord"/>) behind the logo and PRESS START, which A, B or Start turn into
/// the menu (<c>IntroMenu</c>), the pause menu's System tab with New Game, Load Game and Options (<see cref="SystemMenu"/>). New Game
/// asks Alaron's name and then the title fades out into the game; loading a save fades out into it. The session steps it and builds
/// each fly-through's scenery (<see cref="GameSession.Title"/>); the runtime draws what it holds and moves the camera along the
/// fly-through, telling it where the camera is (<see cref="Look"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The fly-throughs</b> (<c>Title.StartScenery</c>, <c>0x80054d70</c>; <c>Title.SceneryFrame</c>, <c>0x80054f70</c>): each is a camera
/// animation over the Overworld's chunks round its row and column, at a tick a tick. The first fades in from black over 15 ticks; 100
/// ticks before its animation ends it fades out over 100, and the next builds and fades in over 100, after the last the first again.
/// The place carries on from one title to the next (<c>Title.FlycamPlace</c>, <c>0x800e0a54</c>, which nothing resets), and passes
/// only once one has ended, so the title shows the place it last left. The clock stands at a new game's noon, with the weather clear.
/// </para>
/// <para>
/// <b>The logo</b> (<c>IntroMenu.ShowPressStart</c>, <c>0x800432b4</c>): its shadow, Borg8 <c>0x0104</c>, at (40, 20), the logo,
/// <c>0x0103</c>, over it at (54, 20), and PRESS START, <c>0x0102</c>, at (123, 205), fade in by 20 a tick. PRESS START blinks
/// (<c>IntroMenu.Blink</c>, <c>0x80042e5c</c>): hidden for 101 ticks, then shown and hidden 31 ticks each.
/// </para>
/// <para>
/// <b>The menu</b> comes up over the logo, the logo fading out by 20 a tick as the list fades in, and takes no press until both have
/// (<c>IntroMenu.Input</c>, <c>0x80043e44</c>). B on the list does nothing; Start chooses as A does. Each page is drawn on a feathered
/// black box (<c>IntroMenu_ShadowBG</c>, <c>0x80042eb0</c>), and its pages are the System tab's, in its layout (the user's choice).
/// </para>
/// <para>
/// <b>Leaving</b>: New Game's name confirmed (state 6) or a save chosen to load fades the screen out by 0.05 a tick, 20 ticks, and then
/// the title ends: the game begins with the opening cinematic (<c>Cinematic.Load(0, 1, 1)</c> at <c>0x80055434</c>), or the save
/// loads. <c>Title.Leave</c> (<c>0x80055718</c>) stops the title's music.
/// </para>
/// </remarks>
public sealed class TitleScreen
{
    /// <summary>The logo's and the list's fades, a tick (<c>IntroMenu.Build</c>'s speed byte, 0x14).</summary>
    public const int LogoFade = 20;

    /// <summary>The first fly-through fades in over 15 ticks, the rest over 100, and each fades out over its last 100.</summary>
    public const int FirstFadeIn = 15, SceneryFade = 100;

    /// <summary>The screen's fade out as the title ends: 0.05 a tick (the float at <c>0x800d2190</c>), 20 ticks.</summary>
    public const int LeaveTicks = 20;

    /// <summary>The logo's shadow, the logo and PRESS START (<c>IntroMenu.ShowPressStart</c>).</summary>
    public static readonly RecordId LogoShadow = Image(0x0104), Logo = Image(0x0103), PressStart = Image(0x0102);

    /// <summary>Where the logo's shadow, the logo and PRESS START are drawn in the 320 × 240 frame.</summary>
    public static readonly (int X, int Y) LogoShadowAt = (40, 20), LogoAt = (54, 20), PressStartAt = (123, 205);

    /// <summary>PRESS START's blink (<c>IntroMenu.Blink</c>): it counts to 101 and goes back to 70, turning on or off each time.</summary>
    public const int BlinkTop = 101, BlinkBack = 70;

    /// <summary>The map set the fly-throughs fly over: the Overworld.</summary>
    public static readonly RecordId Overworld = RecordId.Decimal(RecordTypes.MapSet, 0);

    private readonly GameState _game;
    private readonly Func<SystemMenu> _menu;
    private readonly Action<OneShot> _sound;
    private readonly TitleRecord? _record;
    private int _blink;
    private int _sceneryFadeIn = FirstFadeIn;
    private bool _sceneryEnding;
    /// <summary>Whether the first fly-through starts on the next tick: the first after the cards, or at once without them.</summary>
    private bool _startPending;
    /// <summary>The ticks the screen has faded out for as the title ends.</summary>
    private int _left;

    /// <param name="game">The new game the title would begin, whose party's Alaron New Game names.</param>
    /// <param name="menu">Makes the title's menu (<see cref="SystemMenu"/> with <c>title</c>), as Start brings it up.</param>
    /// <param name="sound">Plays a menu sound.</param>
    /// <param name="cards">Whether the intro cards play first; whether they can be skipped.</param>
    /// <param name="place">The fly-through the title starts on, which carries on from the last title.</param>
    public TitleScreen(GameState game, Func<SystemMenu> menu, Action<OneShot> sound, bool cards, bool skippable, int place)
    {
        _game = game;
        _menu = menu;
        _sound = sound;
        _record = game.Plugins.TrySingleton<TitleRecord>();
        Place = _record is { Flycams.Count: > 0 } r ? Math.Clamp(place, 0, r.Flycams.Count - 1) : 0;
        Cards = cards ? new IntroCards(skippable) : null;
        Phase = cards ? TitlePhase.Cards : TitlePhase.Title;
        _startPending = !cards;
    }

    public TitlePhase Phase { get; private set; }

    /// <summary>The intro cards, while they play.</summary>
    public IntroCards? Cards { get; private set; }

    /// <summary>The fly-through's place in the title record, which carries on to the next title.</summary>
    public int Place { get; private set; }

    /// <summary>The fly-through playing, once the cards have ended, or null if the plugins have none.</summary>
    public TitleFlycam? Flycam => Phase != TitlePhase.Cards && _record is { Flycams.Count: > 0 } r ? r.Flycams[Place] : null;

    /// <summary>The fly-through's time, from 1: the tick of its animation that's shown.</summary>
    public int Time { get; private set; }

    /// <summary>
    /// Whether the fly-through changed this tick: the first after the cards, or the next after one has faded out. The session builds its
    /// scenery and the runtime its view.
    /// </summary>
    public bool FlycamStarted { get; private set; }

    /// <summary>How bright the fly-through is, 0 to 1, as it fades in from black and out to it.</summary>
    public float SceneryBrightness { get; private set; }

    /// <summary>How bright everything is, 0 to 1: the whole screen fades out as the title ends.</summary>
    public float Brightness { get; private set; } = 1;

    /// <summary>The logo's and PRESS START's alpha, 0 to 255.</summary>
    public int LogoAlpha { get; private set; }

    /// <summary>Whether PRESS START shows, blinking.</summary>
    public bool PressStartShown { get; private set; }

    /// <summary>The menu, once Start has brought it up.</summary>
    public SystemMenu? Menu { get; private set; }

    /// <summary>
    /// The camera the fly-through's scenery is seen through, which the runtime moves along its animation: the chunks load about it and
    /// its sounds are heard from it.
    /// </summary>
    public FollowCamera Camera { get; } = new(Vector3.Zero, Vector2.UnitX);

    /// <summary>How the title ended, once it has faded out: a new game with Alaron's name, or a save to load. Null until then.</summary>
    public PauseExit? Exit { get; private set; }

    /// <summary>How the title ended, once: the session carries it out, and the title stays black until whoever loads the save does.</summary>
    public PauseExit? TakeExit()
    {
        var exit = Exit;
        Exit = null;
        return exit;
    }

    /// <summary>How it's to end as it fades out.</summary>
    private PauseExit? _leaving;

    /// <summary>Whether the menu's name entry is up to take the keyboard.</summary>
    public bool NameEntryOpen => Phase == TitlePhase.Title && Menu is { Rename: not null, Ended: null };

    /// <summary>Whether a direction held repeats: with the menu up and settled.</summary>
    public bool Repeats => Phase == TitlePhase.Title && Menu is { Fading: false } && LogoAlpha == 0;

    /// <summary>The runtime has posed the fly-through's camera: where it is and what it looks at.</summary>
    public void Look(Vector3 position, Vector3 aim)
    {
        Camera.PlaceAt(position);
        Camera.Aim = Camera.AimGoal = aim;
        var flat = new Vector2(aim.X - position.X, aim.Z - position.Z);
        if (flat.LengthSquared() > 1e-6f)
            Camera.LookXZ = Vector2.Normalize(flat);
    }

    /// <summary>A tick: the cards, or the fly-through, the logo's fade and blink, the menu and its held volume, and the fade out.</summary>
    /// <param name="held">Whether a direction is held, for a volume, which steps every tick.</param>
    public void Step(Func<MenuAction, bool> held)
    {
        FlycamStarted = false;
        if (Phase == TitlePhase.Cards)
        {
            Cards!.Step();
            if (!Cards.Ended)
                return;
            Cards = null;
            Phase = TitlePhase.Title;
            _startPending = true;
        }
        if (_startPending)
        {
            _startPending = false;
            StartFlycam(advance: false);
        }
        else
        {
            StepScenery();
        }
        if (Phase == TitlePhase.Leaving)
        {
            _left = Math.Min(LeaveTicks, _left + 1);
            Brightness = 1 - (float)_left / LeaveTicks;
            if (_left == LeaveTicks)
            {
                Exit = _leaving;
                _leaving = null;
            }
            return;
        }
        if (Menu is null)
        {
            LogoAlpha = WidgetFade.In(LogoAlpha, LogoFade);
            // The blink runs only while PRESS START is up.
            if (++_blink >= BlinkTop)
            {
                _blink = BlinkBack;
                PressStartShown = !PressStartShown;
            }
        }
        else
        {
            LogoAlpha = WidgetFade.Out(LogoAlpha, LogoFade);
            if (!Menu.Fading && LogoAlpha == 0 && Menu.OnVolume)
            {
                var by = (held(MenuAction.Right) ? 1 : 0) - (held(MenuAction.Left) ? 1 : 0);
                if (by != 0)
                    Menu.Sideways(by);
            }
            Menu.Step();
            if (Menu.Saves is { Request: SaveRequest.Load, Target: { } path })
                Leave(PauseExit.LoadFrom(path));
            else if (Menu.Ended is { } ended)
                Leave(ended);
        }
    }

    /// <summary>
    /// A tick of the fly-through: its animation a tick on, and 100 ticks before its end it fades to black, after which the next
    /// starts and fades in.
    /// </summary>
    private void StepScenery()
    {
        if (Flycam is not { } flycam)
            return;
        Time++;
        if (!_sceneryEnding)
        {
            SceneryBrightness = MathF.Min(1, SceneryBrightness + 1f / _sceneryFadeIn);
            if (Time >= flycam.Length - SceneryFade - 1 && SceneryBrightness >= 1)
                _sceneryEnding = true;
            return;
        }
        SceneryBrightness = MathF.Max(0, SceneryBrightness - 1f / SceneryFade);
        if (SceneryBrightness <= 0)
            StartFlycam(advance: true);
    }

    /// <summary>A fly-through starts, the next if one has ended, passing over one that needs the Expansion Pak only without it.</summary>
    private void StartFlycam(bool advance)
    {
        if (_record is not { Flycams.Count: > 0 } record)
            return;
        if (advance)
        {
            Place = (Place + 1) % record.Flycams.Count;
            _sceneryFadeIn = SceneryFade;
        }
        Time = 1;
        SceneryBrightness = 0;
        _sceneryEnding = false;
        FlycamStarted = true;
    }

    /// <summary>
    /// A press: the cards' skip; A, B or Start on PRESS START, which brings the menu up; or the menu's, once it has settled. Returns
    /// whether it was taken.
    /// </summary>
    /// <param name="keyboard">Whether a keyboard key pressed it, which makes no sound in the name entry, as it types there.</param>
    public bool Press(MenuAction action, bool keyboard = false)
    {
        switch (Phase)
        {
            case TitlePhase.Cards:
                return Cards!.Press(action);
            case TitlePhase.Leaving:
                return true;
        }
        if (Menu is null)
        {
            if (LogoAlpha < 255 || action is not (MenuAction.Accept or MenuAction.Back or MenuAction.Start or MenuAction.Up or MenuAction.Down))
                return action != MenuAction.None;
            Sound(action, keyboard);
            if (action is MenuAction.Up or MenuAction.Down)
                return true;
            // IntroMenu.OnA, OnB and OnStart all bring the list up (IntroMenu.ShowList, 0x80043598).
            Menu = _menu();
            Menu.FadeIn();
            return true;
        }
        if (Menu.Fading || LogoAlpha > 0 || Menu.Ended is not null)
            return true;
        Sound(action, keyboard);
        switch (action)
        {
            case MenuAction.Accept:
                Menu.Accept();
                break;
            case MenuAction.Back:
                Menu.Back();
                break;
            case MenuAction.Start:
                Menu.Start();
                break;
            case MenuAction.Left or MenuAction.Right:
                if (!Menu.OnVolume)
                    Menu.Sideways(action.Horizontal());
                break;
            case MenuAction.Up or MenuAction.Down:
                Menu.MoveCursor(action.Vertical());
                break;
            case MenuAction.Appraise:
                return Menu.Clear();
            default:
                return false;
        }
        return true;
    }

    /// <summary>A direction held repeats (<see cref="StickRepeat"/>, while <see cref="Repeats"/>): the cursor moves on, and an option or a key.</summary>
    public void Repeat(MenuAction direction)
    {
        if (!Repeats || !direction.IsDirection())
            return;
        var by = direction is MenuAction.Up or MenuAction.Left ? -1 : 1;
        if (direction is MenuAction.Up or MenuAction.Down)
        {
            Sound(direction, keyboard: false);
            Menu!.MoveCursor(by);
        }
        else if (!Menu!.OnVolume)
        {
            Menu.Sideways(by);
        }
    }

    /// <summary>
    /// The title menu's sounds (<c>IntroMenu.OnUp</c> to <c>OnA</c>, <c>0x80044020</c>–<c>0x800442b4</c>): the cursor for up and down,
    /// A for A and Start, B for B, and the System tab's for the stick's left and right; but A and Start make none on the load screen,
    /// and the keyboard none in the name entry.
    /// </summary>
    private void Sound(MenuAction action, bool keyboard)
    {
        if (keyboard && NameEntryOpen)
            return;
        var menu = _game.Sounds.Menu;
        var saves = Menu?.Page == SystemPage.Saves;
        switch (action)
        {
            case MenuAction.Up or MenuAction.Down:
                _sound(new OneShot(menu.Cursor, OneShot.ShortTicks));
                break;
            case MenuAction.Accept or MenuAction.Start when !saves:
                _sound(new OneShot(menu.Accept, OneShot.MenuTicks));
                break;
            case MenuAction.Back:
                _sound(new OneShot(menu.Back, OneShot.MenuTicks));
                break;
            case MenuAction.Left or MenuAction.Right when Menu is { OnVolume: false }:
                _sound(new OneShot(menu.PageCursor, OneShot.ShortTicks));
                break;
        }
    }

    /// <summary>The title fades out, and then ends as <paramref name="exit"/> says.</summary>
    private void Leave(PauseExit exit)
    {
        _leaving = exit;
        Phase = TitlePhase.Leaving;
    }

    private static RecordId Image(int borg8) => RecordId.Hex(RecordTypes.Image, borg8);
}
