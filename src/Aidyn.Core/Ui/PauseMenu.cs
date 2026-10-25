using Aidyn.Core.Audio;
using Aidyn.Core.Characters;
using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// The pause menu, which Start opens over the game (<c>BigAssMenu</c>): the System tab (<see cref="SystemMenu"/>), the Party tab
/// (<see cref="PartyMenu"/>, and the windows in its place, <see cref="IPartyWindow"/>) and the World tab (<see cref="WorldMenu"/>)
/// side by side on the scroll (<see cref="PauseScroll"/>), with the Party tab's barter state, the loot window a container opens
/// (<see cref="OpenLoot"/>) and a shop a script opens (<see cref="OpenShop"/>), its training state (<see cref="OpenTraining"/>)
/// and the post-battle windows (<see cref="OpenPostBattle"/>). This is the menu's state and rules: which tab shows and how far
/// the tabs have slid, what each press does, when it closes and how (<see cref="PauseExit"/>), the windows' swap after a battle,
/// a volume stepping while the stick is held, and what the save screen asks. Its sounds and the scroll's steps go to its host
/// as cues (<see cref="IPauseMenuHost"/>, ADR 0019), and the runtime draws what it reads here, in the original's layout (ADR 0014).
/// </summary>
/// <remarks>
/// <para>
/// Opening, the world freezes and darkens to half over 13 frames, and then the scroll unrolls with the tabs between its
/// rollers; closing, it rolls up and the world brightens again. The menu takes input only while the scroll is open
/// (<see cref="Taking"/>). Its frame is a tick (<see cref="Cadence.MenuFrameTicks"/>).
/// </para>
/// <para>
/// Z or L, and R, change the tab, which slides over 32 frames, as the original's moves 10 pixels a frame across its 320, with
/// the scroll; presses wait while it slides. There's no wrap. The System tab can be left only from its list, as the original's
/// can. Each opening starts the tabs afresh, on the inventory's top row, but remembers which tab was open
/// (<c>gGlobals.pauseMenuSection</c>, <see cref="Remembered"/>) and the member last shown (<see cref="Member"/>).
/// </para>
/// </remarks>
public sealed class PauseMenu
{
    /// <summary>How far a tab slides a tick: the original's 10 pixels a frame of its 320.</summary>
    public const float SlidePerTick = 10f / 320 / Cadence.MenuFrameTicks;

    /// <summary>The fades' step a frame as the post-battle window swaps for the loot (<c>PauseMenu.CloseFade</c>, <c>OpenFade</c>).</summary>
    public const int WindowFadeStep = 60;

    private readonly GameState _game;
    private readonly Settings _settings;
    private readonly string _savesFolder;
    private readonly Func<(ChunkRef? Place, bool Outdoors)> _where;
    private readonly IPauseMenuHost _host;
    private readonly StickRepeat _stick;
    private readonly FrameGate _frame = new(Cadence.MenuFrameTicks);
    private TextLayout? _mainFont;
    /// <summary>In combat, whether it has the Party tab and Flee (<see cref="OpenCombat"/>); null in trek.</summary>
    private (bool PartyTab, bool CanFlee)? _combat;
    /// <summary>How the menu ends once the scroll has rolled up (<c>PauseMenu.Exit</c>).</summary>
    private PauseExit _exit = PauseExit.Close;
    /// <summary>How the menu ended, once it has closed, for <see cref="Step"/> to return.</summary>
    private PauseExit? _ended;
    /// <summary>The battle won whose post-battle windows are up (game mode 6), or null.</summary>
    private BattleSpoils? _spoils;
    /// <summary>How far the windows' swap has gone: the post-battle window fading out (1), then the loot window in (2); 0 for none.</summary>
    private int _swap;
    /// <summary>How opaque the window's lists and parts are as they swap, 0 to 255, by 60 a frame.</summary>
    private int _windowAlpha = 255;
    /// <summary>
    /// The sounds the window plays as the original builds it, once the world has darkened (<c>PauseMenu.Open</c> from
    /// <c>PauseMenu.Frame</c>): the loot window's coins and the post-battle window's level-up.
    /// </summary>
    private readonly List<OneShot> _onBuilt = [];

    /// <param name="game">The game the menu shows and acts on.</param>
    /// <param name="settings">The player's settings, which the Options page changes.</param>
    /// <param name="savesFolder">Where the save and load screens list saves.</param>
    /// <param name="where">The chunk the player is in, and whether it's outdoors, which the World tab's map and Camp read.</param>
    /// <param name="host">What plays the menu's sounds, steps its scroll and writes saves.</param>
    /// <param name="stick">The held directions, which a volume and the journal step by, and which opening and closing forget.</param>
    public PauseMenu(GameState game, Settings settings, string savesFolder, Func<(ChunkRef? Place, bool Outdoors)> where, IPauseMenuHost host,
        StickRepeat stick)
    {
        _game = game;
        _settings = settings;
        _savesFolder = savesFolder;
        _where = where;
        _host = host;
        _stick = stick;
    }

    public bool IsOpen { get; private set; }

    /// <summary>The tab showing, or being slid to.</summary>
    public PauseTab Tab { get; private set; }

    /// <summary>Where the tabs are: the tab shown, or between two as they slide, in tabs.</summary>
    public float Position { get; private set; }

    /// <summary>Whether the tabs are sliding to <see cref="Tab"/>.</summary>
    public bool Sliding => MathF.Abs(Position - (float)Tab) > 1e-5f;

    /// <summary>Whether the tabs can't be changed: in barter and training, and after a battle, the Party tab shows alone.</summary>
    public bool TabsLocked { get; private set; }

    /// <summary>The rightmost tab: the World tab in trek, in combat the Party tab from a turn's pause and else the System tab.</summary>
    public PauseTab LastTab => _combat switch
    {
        null => PauseTab.World,
        { PartyTab: true } => PauseTab.Party,
        _ => PauseTab.System,
    };

    /// <summary>The tab the menu last showed, which a default opening goes back to (<c>gGlobals.pauseMenuSection</c>).</summary>
    public PauseTab Remembered { get; private set; } = PauseTab.System;

    /// <summary>
    /// The party slot of the member every Party window opens on, the Party tab, barter, training and the post-battle windows alike
    /// (<c>PauseMenu.Member</c>, <c>0x800DE9F0</c> in 1.0, a byte <c>DollMenu::InitMenu</c> builds them on): the one last shown, as
    /// C-left and C-right change it in any of them (<c>PartyMenu.ChangeMember</c>, <c>0x8003054C</c>), or the member whose turn a
    /// battle paused on (<c>Combat.StatePause</c>, <c>0x800226D0</c>). An empty slot steps back to the one before with a member, which
    /// is kept (<c>0x80030128</c>). Nothing else sets it, so it outlasts trek and combat, and it isn't saved. The original's
    /// outlasts a save loaded too; the port's goes with the session, which loading builds again, as <see cref="Remembered"/> does.
    /// </summary>
    public int Member => IsOpen ? Window.Menu.Member : _member;

    private int _member;

    /// <summary>The scroll's opening and closing, the world's tint and where the tabs are cut.</summary>
    public PauseScroll Scroll { get; private set; } = new(0);

    /// <summary>What the Party tab shows and takes the presses: the tab itself, or the barter, training or post-battle window. Set from the first opening.</summary>
    public IPartyWindow Window { get; private set; } = null!;

    /// <summary>The Party tab's own state, which the windows are built on. Set from the first opening.</summary>
    public PartyMenu Party { get; private set; } = null!;

    /// <summary>The System tab. Set from the first opening.</summary>
    public SystemMenu System { get; private set; } = null!;

    /// <summary>The World tab. Set from the first opening.</summary>
    public WorldMenu World { get; private set; } = null!;

    /// <summary>How opaque the Party tab's window's own parts and lists are, 0 to 1, as the post-battle windows swap.</summary>
    public float WindowAlpha => _windowAlpha / 255f;

    /// <summary>Whether the window is the Party tab itself, whose list plays the cursor's sound itself too.</summary>
    public bool OwnTab => ReferenceEquals(Window, Window.Menu);

    /// <summary>Whether the menu takes input: once the scroll has unrolled, and until it rolls up.</summary>
    public bool Taking => Scroll.Phase == PauseScrollPhase.Open;

    /// <summary>Whether the post-battle window is fading out for the loot window, or the loot window in: nothing takes a press.</summary>
    public bool Swapping => _swap != 0;

    /// <summary>Whether a held direction repeats in the menu now (<see cref="Repeat"/>): not while it opens, closes or slides.</summary>
    public bool Repeats => IsOpen && Taking && !Sliding;

    /// <summary>Whether the name entry is up to take the keyboard (<see cref="TypeName"/>).</summary>
    public bool NameEntryOpen => IsOpen && Tab == PauseTab.System && !Sliding && System.Rename is not null;

    /// <summary>Opens the menu afresh on the party as it is now, on the tab given or else the one it last showed.</summary>
    public void Open(PauseTab? tab = null)
    {
        _combat = null;
        Open(tab ?? Remembered, tabsLocked: false);
    }

    /// <summary>
    /// Opens combat's pause menu (game mode 3, <c>BigAssMenu</c>): from a turn (combat's state 14), the System and Party tabs,
    /// opening on the Party tab, the System tab listing Options, and Flee while the party can flee (<see cref="Battle.CanFlee"/>);
    /// from any other state (27), the System tab alone, with Options.
    /// </summary>
    /// <param name="rules">The Party tab's rules in the battle's pause: whose turn it is, and what a flask or a weapon change does.</param>
    public void OpenCombat(bool partyTab, bool canFlee, CombatPause? rules = null)
    {
        _combat = (partyTab, canFlee);
        Open(partyTab ? PauseTab.Party : PauseTab.System, tabsLocked: false, rules: rules);
    }

    /// <summary>Opens the menu on the Theatre's page, as it reopens after a cinematic the Theatre played (<c>Theatre.Reopen</c>).</summary>
    public void OpenTheatre()
    {
        Open(PauseTab.System);
        System.ShowTheatre(fade: false);
    }

    /// <summary>
    /// Opens the menu in its barter state on loot (<c>build_loot_menu</c>), whose gold the party already has. Taking and dropping act
    /// on the game; the pile is lost once it closes. The coins sound a second after the window is built
    /// (<c>BarterMenu.BarterMenu</c>, <c>0x8003CB68</c>), once the world has darkened.
    /// </summary>
    public void OpenLoot(ItemPile loot, int gold)
    {
        _combat = null;
        Open(PauseTab.Party, tabsLocked: true, brightens: false);
        Window = BarterMenu.ForLoot(_game.Plugins, _game.Party, loot, gold, _game.Flags, _game.Random.World, Member);
        if (gold > 0)
            _onBuilt.Add(CoinsSound);
    }

    /// <summary>Opens the menu in its barter state on a shop (<c>InitShop</c>), which <see cref="Shop.Open"/> has opened on the game's party.</summary>
    public void OpenShop(Shop shop)
    {
        _combat = null;
        Open(PauseTab.Party, tabsLocked: true, brightens: false);
        Window = BarterMenu.ForShop(_game.Plugins, shop, _game.Random.World, Member);
    }

    /// <summary>Opens the menu in its training state at a trainer (<c>init_skill_trainer</c>). Training acts on the game.</summary>
    public void OpenTraining(Trainer trainer)
    {
        _combat = null;
        Open(PauseTab.Party, tabsLocked: true);
        Window = TrainingMenu.For(_game.Plugins, _game.Party, trainer, _game.Flags, _game.Random.World, Member);
    }

    /// <summary>
    /// Opens the post-battle windows over the black the battle left (game mode 6): the Party scroll unrolls on the post-battle window
    /// (pause state 5, <see cref="PostBattleMenu"/>), and the level-up sound plays a second after it's built, once the world has
    /// darkened, if anyone gained a level (<c>PartyMenu.ListsCreate</c>, <c>0x80030C50</c>). B or Start
    /// fades its lists out and the loot window in on the same scroll (state 6, <see cref="BarterMenu.ForLoot"/>, the gold already
    /// given), whose close rolls the scroll up and snaps the world bright again.
    /// </summary>
    public void OpenPostBattle(BattleSpoils spoils)
    {
        _combat = null;
        Open(PauseTab.Party, tabsLocked: true, brightens: false);
        _spoils = spoils;
        _swap = 0;
        _windowAlpha = 255;
        Window = PostBattleMenu.For(_game.Plugins, _game.Party, spoils, _game.Random.World, Member);
        if (spoils.Levelled.Count > 0)
            _onBuilt.Add(new OneShot(_game.Sounds.Menu.LevelUp, OneShot.LongTicks, OneShot.MenuTicks, TimesSoundVolume: true));
    }

    /// <param name="brightens">Whether the world brightens again as it closes; the loot window and shops snap back.</param>
    private void Open(PauseTab tab, bool tabsLocked, bool brightens = true, CombatPause? rules = null)
    {
        Scroll = new PauseScroll((int)tab, brightens, holds: tabsLocked);
        _exit = PauseExit.Close;
        _ended = null;
        _frame.Reset();
        _spoils = null;
        _swap = 0;
        _windowAlpha = 255;
        _onBuilt.Clear();
        // Skill checks and potions in trek draw from the world's stream.
        // The tab opens on the member last shown, or in a battle's pause the member whose turn it is (Combat.StatePause).
        if (rules?.Member is { } current && _game.Party.Slots.ToList().IndexOf(current) is >= 0 and var slot)
            _member = slot;
        var party = Party = PartyMenu.From(_game.Plugins, _game.Party, _member, random: _game.Random.World);
        // An empty slot steps back to a member's, which is kept (DollMenu::InitMenu).
        _member = party.Member;
        party.Combat = rules;
        Window = party;
        System = new SystemMenu(_game.Plugins, _settings, _game.Party, _savesFolder, isSet: _game.Flags.Get, combat: _combat?.CanFlee,
            screen: () => _host.Screen);
        var (place, outdoors) = _where();
        World = new WorldMenu(_game.Plugins, _game, _mainFont ??= new TextLayout(_game.Plugins.Get<FontRecord>(RecordId.Decimal(RecordTypes.Font, 0))),
            place, outdoors);
        Tab = tab;
        Position = (float)tab;
        TabsLocked = tabsLocked;
        _stick.Clear();
        IsOpen = true;
    }

    /// <summary>The coins, a second after a window is built on gold, at the sound volume as their gain.</summary>
    private OneShot CoinsSound => new(_game.Sounds.GoldGained, OneShot.LongTicks, OneShot.MenuTicks, TimesSoundVolume: true);

    /// <summary>A menu sound (<c>Audio.PlaySound</c> at gain 1), for so many ticks.</summary>
    private void Sound(Func<MenuSounds, RecordId> pick, int ticks)
    {
        _host.Cue(new MenuSoundCue(new OneShot(pick(_game.Sounds.Menu), ticks)));
    }

    /// <summary>The scroll unrolls as the menu opens, or rolls up as it closes (<c>BigAssMenu</c>, <c>PauseMenu.SlideOut</c>).</summary>
    private void SlideSound(bool opening) =>
        Sound((opening ? Tab != PauseTab.World : Tab == PauseTab.World) ? m => m.TabBack : m => m.TabForward, OneShot.MenuTicks);

    /// <summary>
    /// A tick of the menu, which whoever runs the game calls every tick it's open: a frame of the scroll, the tabs' slide, the
    /// windows' swap, a volume held, what the save screen asks and the World tab. Returns how the menu ended if it closed this
    /// tick (the original's exit code, <c>PauseMenu.Exit</c>, <c>0x80048e64</c>; ADR 0019), and otherwise null.
    /// </summary>
    /// <param name="scrollFrame">The scroll actor's clock in the state it's in, from 1, before this frame's step; without an actor, past any state's end.</param>
    /// <param name="scrollLength">The length of the state the actor is in, in frames.</param>
    public PauseExit? Step(int scrollFrame, int scrollLength)
    {
        if (IsOpen && _frame.Tick())
            StepFrame(scrollFrame, scrollLength);
        var ended = _ended;
        _ended = null;
        return ended;
    }

    private void StepFrame(int scrollFrame, int scrollLength)
    {
        StepScroll(scrollFrame, scrollLength);
        if (!IsOpen)
            return;
        if (_swap != 0)
            StepSwap();
        if (Sliding)
        {
            var to = (float)Tab;
            Position = Position < to ? MathF.Min(Position + SlidePerTick, to) : MathF.Max(Position - SlidePerTick, to);
        }
        // A volume steps every tick the stick is held, from the first.
        if (Tab == PauseTab.System && !Sliding && Taking && System.OnVolume)
        {
            var by = (_stick.Held(MenuAction.Right) ? 1 : 0) - (_stick.Held(MenuAction.Left) ? 1 : 0);
            if (by != 0)
                System.Sideways(by);
        }
        if (System.Saves is { Request: not SaveRequest.None } saves)
            CarryOut(saves);
        // The tab showing, or being slid to, steps: its popups' pulses and fades, its slides and screens.
        if (Tab == PauseTab.Party)
            Window.Step();
        else if (Tab == PauseTab.System)
            System.Step();
        var showing = Tab == PauseTab.World;
        World.Step(showing, Sliding, showing && _stick.Held(MenuAction.Up), showing && _stick.Held(MenuAction.Down));
    }

    /// <summary>
    /// One frame of the opening or closing: the world's tint and the scroll's step, cued to its actor while it moves
    /// (<see cref="ScrollPlayCue"/>). Closes the menu once it has rolled up and the world is bright.
    /// </summary>
    private void StepScroll(int frame, int length)
    {
        var wasDarkening = Scroll.Phase == PauseScrollPhase.Darkening;
        var cue = Scroll.Step(frame, length);
        if (Scroll.StepsScroll)
            _host.Cue(new ScrollPlayCue(cue));
        if (wasDarkening && Scroll.ShowsMenu)
        {
            SlideSound(opening: true);
            foreach (var sound in _onBuilt)
                _host.Cue(new MenuSoundCue(sound));
            _onBuilt.Clear();
        }
        if (Scroll.Phase == PauseScrollPhase.Closed)
            CloseNow();
    }

    /// <summary>
    /// A frame of the windows' swap: the post-battle window's lists and parchment fade out 60 a frame to 60 or less
    /// (<c>PauseMenu.CloseFade</c>, <c>0x8002F734</c>), the header staying; then the loot window is built at once and fades in 60 a
    /// frame (<c>PauseMenu.OpenFade</c>, <c>0x8002F3DC</c>), its coins sounding if the battle gave gold.
    /// </summary>
    private void StepSwap()
    {
        if (_swap == 1)
        {
            _windowAlpha -= WindowFadeStep;
            if (_windowAlpha <= WindowFadeStep)
            {
                Window = BarterMenu.ForLoot(_game.Plugins, _game.Party, _spoils!.Loot, _spoils.Gold, _game.Flags, _game.Random.World, Member);
                if (_spoils.Gold > 0)
                    _host.Cue(new MenuSoundCue(CoinsSound));
                _windowAlpha = WindowFadeStep;
                _swap = 2;
            }
        }
        else if (_swap == 2)
        {
            _windowAlpha = Math.Min(255, _windowAlpha + WindowFadeStep);
            if (_windowAlpha == 255)
                _swap = 0;
        }
    }

    /// <summary>Does what the save or load screen asks: the host writes the game, or the menu closes to load a save.</summary>
    private void CarryOut(SaveMenu saves)
    {
        var target = saves.Target!;
        if (saves.Request == SaveRequest.Load)
        {
            _exit = PauseExit.LoadFrom(target);
            CloseNow();
            return;
        }
        if (_host.Save(target) is { } error)
            saves.Failed(error);
        else
            saves.Saved();
    }

    /// <summary>Rolls the scroll up and closes the menu, which then ends as <paramref name="exit"/> says (<c>PauseMenu.Exit</c>).</summary>
    private void Close(PauseExit exit)
    {
        if (Scroll.Phase is PauseScrollPhase.Rolling or PauseScrollPhase.Brightening)
            return;
        _exit = exit;
        if (Scroll.Phase != PauseScrollPhase.Open)
        {
            CloseNow();
            return;
        }
        SlideSound(opening: false);
        // The scroll takes its closing state at once (SlideOut).
        _host.Cue(new ScrollPlayCue(Scroll.Close((int)Tab)));
        Window.ReleaseDetails();
        _stick.Clear();
    }

    /// <summary>
    /// Closes the menu at once with no ending to carry out, as the session does when it has carried one out itself, or when
    /// whoever drew the menu has rolled it up on its own.
    /// </summary>
    public void Discard()
    {
        CloseNow();
        _ended = null;
    }

    /// <summary>Closes the menu at once, as loading a save does, ending as it was asked to (<see cref="PauseExit.Close"/> unless otherwise).</summary>
    public void CloseNow()
    {
        if (!IsOpen)
            return;
        if (Scroll.Phase is not (PauseScrollPhase.Rolling or PauseScrollPhase.Brightening or PauseScrollPhase.Closed))
            SlideSound(opening: false);
        IsOpen = false;
        if (!TabsLocked && _combat is null)
            Remembered = Tab;
        // Every Party window keeps the member it last showed, a battle's pause and barter too.
        _member = Window.Menu.Member;
        _spoils = null;
        _swap = 0;
        _windowAlpha = 255;
        _ended = _exit;
    }

    /// <summary>
    /// A press or release while the menu is open, which takes every one: B and Start close a popup, a screen, or else the
    /// menu; Z and R change the tab; the rest go to the tab showing. Nothing reaches the menu while it opens or closes, nor
    /// while the post-battle windows swap, nor while the tabs slide, and those presses make no sound; nor do the presses a
    /// save's slide drops. True if the menu took it, so nothing else should.
    /// </summary>
    /// <param name="keyboard">
    /// Whether a keyboard key pressed it: with the name entry up, the keyboard types into it (<see cref="TypeName"/>), and its
    /// other keys make no sound.
    /// </param>
    public bool Press(MenuAction action, bool keyboard = false)
    {
        if (!IsOpen || action == MenuAction.None)
            return false;
        if (!Taking || _swap != 0)
            return true;
        var sliding = Sliding || System.Saves is { Sliding: true };
        var taken = HandleInput(action);
        if (taken && !sliding)
            PressSound(action, keyboard);
        return taken;
    }

    private bool HandleInput(MenuAction action)
    {
        if (action is MenuAction.Start or MenuAction.Back)
        {
            if (Sliding)
                return true;
            var start = action == MenuAction.Start;
            var held = Tab switch
            {
                PauseTab.System => start ? System.Start() : System.Back(),
                PauseTab.World => World.Back(),
                _ => Window.Back(),
            };
            if (!held && Tab == PauseTab.Party && Window is PostBattleMenu)
            {
                // The post-battle window closes into the loot (PauseMenu.ReadFrame's state 5), the scroll staying open.
                SlideSound(opening: false);
                _swap = 1;
                Window.ReleaseDetails();
            }
            else if (!held)
            {
                Close(PauseExit.Close);
            }
            return true;
        }
        if (action is MenuAction.TabPrevious or MenuAction.TabNext)
        {
            ChangeTab(action == MenuAction.TabPrevious ? -1 : 1);
            return true;
        }
        if (Sliding)
            return true;
        return Tab switch
        {
            PauseTab.System => HandleSystem(action),
            PauseTab.World => HandleWorld(action),
            _ => HandleParty(action),
        };
    }

    /// <summary>
    /// The Party tab's presses, to whichever window is up. A window that closes itself rolls the menu up as B on it would: the
    /// loot window's "Yes, leave them." (<c>BarterMenu.LeaveItems</c>, <c>0x8003d5f4</c>, the choice's callback) calls
    /// <c>PauseMenu.CloseToTrek</c> (<c>0x80048f40</c>), as B on an empty pile does, after a battle as after a container.
    /// </summary>
    private bool HandleParty(MenuAction action)
    {
        var taken = Window.Press(action);
        if (Window.Closed)
            Close(PauseExit.Close);
        return taken;
    }

    /// <summary>The System tab's presses: A takes the row, which may roll the menu up, and the stick moves the cursor, steps an option or a save, or moves over the name entry's keys.</summary>
    private bool HandleSystem(MenuAction action)
    {
        switch (action)
        {
            case MenuAction.Accept:
                // Help, Credits and a seen cinematic roll the menu up as closing does, and start once it has (PauseMenu.Exit's
                // codes 3, 5 and 2).
                if (System.Accept() is { } exit)
                    Close(exit);
                return true;
            case MenuAction.Left or MenuAction.Right:
                // A volume is stepped every tick it's held, in Step.
                if (!System.OnVolume)
                    System.Sideways(action.Horizontal());
                return true;
            case MenuAction.Up or MenuAction.Down:
                System.MoveCursor(action.Vertical());
                return true;
            case MenuAction.Appraise:
                return System.Clear();
            default:
                return false;
        }
    }

    /// <summary>The World tab's presses: up and down move between Camp and Journal, or scroll the journal; A chooses, or closes the journal or the message.</summary>
    private bool HandleWorld(MenuAction action)
    {
        switch (action)
        {
            case MenuAction.Accept:
                // Camp rolls the menu up, and the party camps once it has.
                if (World.Accept() is { } exit)
                    Close(exit);
                return true;
            case MenuAction.Up or MenuAction.Down:
                Vertical(World, action.Vertical());
                return true;
            default:
                return false;
        }
    }

    private static void Vertical(WorldMenu world, int by)
    {
        if (world.Journal is { } journal)
        {
            if (by < 0)
                journal.Up();
            else
                journal.Down();
        }
        else
        {
            world.MoveCursor(by);
        }
    }

    /// <summary>Whether the tab showing can be left: the System tab from its list, the Party tab with no popup up, the World tab with no message or journal.</summary>
    private bool CanLeave => Tab switch
    {
        PauseTab.System => System.OnList,
        PauseTab.World => World.Message is null && World.Journal is null,
        _ => !Window.HasPopup,
    };

    /// <summary>Z or R: slides to the tab that way, if there is one and the tab showing can be left.</summary>
    private void ChangeTab(int by)
    {
        var to = (PauseTab)((int)Tab + by);
        if (TabsLocked || Sliding || !Taking || to < PauseTab.System || to > LastTab || !CanLeave)
            return;
        Tab = to;
        Sound(by < 0 ? m => m.TabBack : m => m.TabForward, 2 * OneShot.MenuTicks);
        Window.ReleaseDetails();
    }

    /// <summary>
    /// The sounds the menu makes for a press it took, whatever the page did with it (<c>BigAssMenu</c>'s callbacks): the
    /// cursor for up and down, A, B and Start, the member for C-left and C-right, and the page's cursor for the Party
    /// tab's lists and the World tab. The details slide in and out with C-down. Tabs, opening and closing play their own.
    /// </summary>
    private void PressSound(MenuAction action, bool keyboard)
    {
        var party = Tab == PauseTab.Party && OwnTab;
        if (Tab == PauseTab.System && System.Rename is not null && keyboard)
            return;
        switch (action)
        {
            case MenuAction.Accept:
                Sound(m => m.Accept, OneShot.MenuTicks);
                break;
            case MenuAction.Back or MenuAction.Start:
                Sound(m => m.Back, OneShot.MenuTicks);
                break;
            case MenuAction.Details:
                Sound(m => m.TabBack, OneShot.MenuTicks);
                break;
            case MenuAction.DetailsRelease:
                Sound(m => m.TabForward, OneShot.MenuTicks);
                break;
            case MenuAction.MemberLeft or MenuAction.MemberRight when Tab == PauseTab.Party && Window.ChangesMember && _game.Party.Members.Count() > 1:
                Sound(m => m.Member, OneShot.MenuTicks);
                break;
            case MenuAction.Up or MenuAction.Down:
                CursorSound();
                break;
            case MenuAction.Left or MenuAction.Right when party || (Tab == PauseTab.System && !System.OnVolume):
                Sound(m => m.PageCursor, OneShot.ShortTicks);
                break;
        }
    }

    /// <summary>
    /// Up or down: the menu's cursor sound, and the World tab's own besides. The Party tab's lists play the cursor's
    /// themselves too, so with no popup up it plays twice at once, louder, as the user confirmed. The journal takes the
    /// presses itself, and scrolls silently.
    /// </summary>
    private void CursorSound()
    {
        if (Tab == PauseTab.World && World.Journal is not null)
            return;
        Sound(m => m.Cursor, OneShot.ShortTicks);
        if (Tab == PauseTab.Party && OwnTab && !Window.HasPopup)
            Sound(m => m.Cursor, OneShot.ShortTicks);
        if (Tab == PauseTab.World)
            Sound(m => m.PageCursor, OneShot.ShortTicks);
    }

    /// <summary>
    /// A direction held repeats (<see cref="StickRepeat"/>, while <see cref="Repeats"/>): the cursor moves on, and on the System
    /// tab, an option or the name entry's key too; left and right repeat nowhere else.
    /// </summary>
    public void Repeat(MenuAction direction)
    {
        if (!Repeats || !direction.IsDirection())
            return;
        var by = direction is MenuAction.Up or MenuAction.Left ? -1 : 1;
        var vertical = direction is MenuAction.Up or MenuAction.Down;
        if (vertical)
            CursorSound();
        if (Tab == PauseTab.System)
        {
            if (vertical)
                System.MoveCursor(by);
            else if (!System.OnVolume)
                System.Sideways(by);
            return;
        }
        if (!vertical)
            return;
        if (Tab == PauseTab.World)
        {
            Vertical(World, by);
            return;
        }
        Window.MoveCursor(by);
    }

    // The keyboard in the name entry, before any key reaches the menu's actions: a printable key types its character, Backspace
    // rubs out, Enter confirms and Escape cancels. Each is true if the name entry was up to take it.

    /// <summary>Types a character into the name entry (<see cref="NameEntry.Type"/>). False with no entry up, or a character it has no key for.</summary>
    public bool TypeName(char c)
    {
        if (!NameEntryOpen || !System.Rename!.Type(c))
            return false;
        System.Settle();
        return true;
    }

    /// <summary>Rubs the last letter out.</summary>
    public bool RubOutName() => NameKey(entry => entry.RubOut());

    /// <summary>Confirms the name, as OK does.</summary>
    public bool ConfirmName() => NameKey(entry => entry.Confirm());

    /// <summary>Cancels the name entry.</summary>
    public bool CancelName() => NameKey(entry => entry.Back());

    private bool NameKey(Action<NameEntry> act)
    {
        if (!NameEntryOpen)
            return false;
        act(System.Rename!);
        System.Settle();
        return true;
    }
}
