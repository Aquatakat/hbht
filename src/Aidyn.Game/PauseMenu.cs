using Aidyn.Core;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The pause menu, which Start opens over the game (<c>BigAssMenu</c>): the System tab (<see cref="SystemTabView"/>),
/// the Party tab (<see cref="PartyTabView"/>) and the World tab (<see cref="WorldTabView"/>) side by side on a
/// <see cref="UiScreen"/> over the world, and the
/// Party tab's barter state: the loot window, which a container opens (<see cref="OpenLoot"/>), and a shop, which
/// a script opens (<see cref="OpenShop"/>); and its training state, which a script opens at a trainer
/// (<see cref="OpenTraining"/>).
/// </summary>
/// <remarks>
/// <para>
/// Opening, the world freezes and darkens to half over 13 frames, and then the 3D scroll
/// (<see cref="PauseScrollView"/>) unrolls with the tabs between its rollers; closing, it rolls up and the world
/// brightens again (<see cref="PauseScroll"/>). The menu takes input only while it's open.
/// </para>
/// <para>
/// The world behind it isn't a picture: the game stops trek and its scenery's animations, and goes on drawing the
/// world and the HUD as they stand, under a black layer as dark as the tint, which darkens them as multiplying by
/// the tint would. So the window's size and the menu size setting redraw them as they change, as they would in
/// trek.
/// </para>
/// <para>
/// Z or L, and R, change the tab, which slides over 32 frames, as the original's moves 10 pixels a frame across its
/// 320, with the scroll; presses wait while it slides. There's no wrap. The System tab can be left only from its list, as the
/// original's can. On a PC, Z and X or the triggers change the tab.
/// </para>
/// <para>
/// The tabs are one scroll, as their images are drawn, 320 apart, so a screen wider than the frame shows the
/// edges of the tabs either side, their widgets faded into the parchment, which clears as a tab slides into the
/// middle (ADR 0014).
/// </para>
/// <para>
/// The menu remembers which tab was open, as the original does, and nothing else: a new game's first opening is on
/// the System tab. Barter and training open on the Party tab, and can't change it.
/// </para>
/// <para>
/// The Party tab's controls follow the original's: C-left and C-right change the member, the stick's left and
/// right change the list and its up and down move the cursor, and C-down shows the details while held. On a PC, Q
/// and E or the shoulders change the member, the movement keys, arrows, stick or D-pad do the rest, and Tab or the
/// top face button shows the details. A, Space or the bottom face button, uses the cursor's row or chooses in a
/// popup. B, Backspace, Start or Escape closes a popup, or the menu if none is open. The mouse moves the cursor, as
/// in the editor. Each opening starts the tab afresh, on the first member, the inventory and the top row, so a
/// player's presses to reach something are the same every time.
/// </para>
/// <para>
/// In the loot window, the stick's left and right go to the list that way: the pile is always left of the party's
/// list, as the original's is. C-left and C-right do nothing there,
/// as in the original. B or Start closes it, unless items are left (<see cref="BarterMenu.Back"/>), and so does
/// choosing to leave them. A shop's is the same, but B or Start closes it at once, and C-up, R or the left face
/// button, appraises the stock's row. In training, the stick's up and down move the trainer's cursor, its left and
/// right change both lists between skills and spells, and C-left and C-right change the member.
/// </para>
/// </remarks>
public partial class PauseMenu : CanvasLayer
{
    /// <summary>Above the lens flare and the debug overlay.</summary>
    public const int MenuLayer = 10;

    /// <summary>How far a tab slides a tick: the original's 10 pixels a frame of its 320.</summary>
    private const float SlidePerTick = 10f / 320 / Cadence.MenuFrameTicks;

    /// <summary>How far the widgets of a tab beside the one showing are faded into its parchment.</summary>
    private const float NeighbourVeil = 0.6f;

    /// <summary>The rightmost tab: the World tab in trek, in combat the Party tab from a turn's pause and else the System tab.</summary>
    private PauseTab LastTab => _combat switch
    {
        null => PauseTab.World,
        { PartyTab: true } => PauseTab.Party,
        _ => PauseTab.System,
    };

    /// <summary>In combat, whether it has the Party tab and Flee (<see cref="OpenCombat"/>); null in trek.</summary>
    private (bool PartyTab, bool CanFlee)? _combat;

    /// <summary>The tab the menu last showed, which a default opening goes back to (<c>gGlobals.pauseMenuSection</c>).</summary>
    private static PauseTab _lastTab = PauseTab.System;

    private readonly PluginSet _plugins;
    private readonly GameState _game;
    private readonly Settings _settings;
    private readonly UiScreen _screen = new() { ForwardsMouse = true };
    private readonly MenuStick _stick;
    private readonly TextLayout _mainFont;
    /// <summary>
    /// What darkens the world behind the menu, still as the game stops it (<c>GetSnapShot</c>,
    /// <c>PauseMenu.DrawBackdrop</c>): black at one less the tint, over everything under the menu.
    /// </summary>
    private readonly ColorRect _darkness = new()
    {
        Name = "Darkness",
        Color = Colors.Transparent,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    /// <summary>The tabs, cut to what the scroll has unrolled.</summary>
    private readonly Control _tabs = new() { Name = "Tabs", ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly Models _models;
    private readonly PauseScrollView? _scrollView;
    /// <summary>The World tab's journal, over the tabs and the scroll, which fade out under it.</summary>
    private readonly JournalView _journal;
    private PauseScroll _scroll = new(0);
    /// <summary>How the menu ends once the scroll has rolled up (<c>PauseMenu.Exit</c>).</summary>
    private PauseExit _exit = PauseExit.Close;
    /// <summary>How the menu ended, once it has closed, for <see cref="Step"/> to return.</summary>
    private PauseExit? _ended;
    private readonly FrameGate _frame = new(Cadence.MenuFrameTicks);
    private PartyTabView? _view;
    private SystemTabView? _system;
    private WorldTabView? _world;
    /// <summary>Where the tabs are: the tab shown, or between two as they slide.</summary>
    private float _position;
    /// <summary>The frame on screen when the menu opened, which a save's screenshot is cut from.</summary>
    private Image? _snapshot;
    private bool _tabsLocked;

    public PauseMenu(PluginSet plugins, GameState game, Settings settings, MenuStick stick)
    {
        _stick = stick;
        _plugins = plugins;
        _mainFont = new TextLayout(plugins.Get<Plugins.Records.FontRecord>(RecordId.Decimal(RecordTypes.Font, 0)));
        _game = game;
        _settings = settings;
        Name = "Pause menu";
        Layer = MenuLayer;
        Visible = false;
        _models = new Models(plugins);
        _darkness.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_darkness);
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);
        _screen.Canvas.AddChild(_tabs);
        _scrollView = PauseScrollView.Create(plugins, _models);
        if (_scrollView is not null)
        {
            _scrollView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _screen.Canvas.AddChild(_scrollView);
        }
        _journal = new JournalView(plugins);
        _journal.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.Canvas.AddChild(_journal);
    }

    public override void _ExitTree() => _models.Dispose();

    public bool IsOpen => Visible;

    /// <summary>The tab showing, or being slid to.</summary>
    public PauseTab Tab { get; private set; }

    /// <summary>The menu opened, on a tab or in its barter or training state.</summary>
    public event Action? Opened;

    /// <summary>
    /// Writes the game to a save file, with its screenshot from the world as the menu opened it; returns why it
    /// couldn't, or null.
    /// </summary>
    public Func<string, Image?, string?>? SaveGame { get; set; }

    /// <summary>Loads the save file, ending this game.</summary>
    public Action<string>? LoadGame { get; set; }

    /// <summary>The chunk the player is in, and whether it's outdoors, which the World tab's map and Camp read.</summary>
    public Func<(Plugins.Records.ChunkRef? Place, bool Outdoors)>? Where { get; set; }

    /// <summary>Opens the menu on the Theatre's page, as it reopens after a cinematic the Theatre played (<c>Theatre.Reopen</c>).</summary>
    public void OpenTheatre()
    {
        Open(PauseTab.System);
        _system!.Menu.ShowTheatre(fade: false);
    }

    /// <summary>What plays the menu's sounds.</summary>
    public Core.Audio.GameAudio? Audio { get; set; }

    /// <summary>A menu sound (<c>Audio.PlaySound</c> at gain 1), for so many ticks.</summary>
    private void Sound(Func<Plugins.Records.MenuSounds, RecordId> pick, int ticks)
    {
        if (_game.Sounds is { } sounds)
            Audio?.Play(pick(sounds.Menu), ticks);
    }

    /// <summary>The scroll unrolls as the menu opens, or rolls up as it closes (<c>BigAssMenu</c>, <c>PauseMenu.SlideOut</c>).</summary>
    private void SlideSound(bool opening) =>
        Sound((opening ? Tab != PauseTab.World : Tab == PauseTab.World) ? m => m.TabBack : m => m.TabForward, Core.Audio.OneShot.MenuTicks);

    /// <summary>The UI screen, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    private bool Sliding => !Mathf.IsEqualApprox(_position, (float)Tab);

    /// <summary>Opens the menu afresh on the party as it is now, on the tab given or else the one it last showed.</summary>
    public void Open(PauseTab? tab = null)
    {
        _combat = null;
        Open(tab ?? _lastTab, tabsLocked: false);
    }

    /// <summary>
    /// Opens combat's pause menu (game mode 3, <c>BigAssMenu</c>): from a turn (combat's state 14), the System and Party
    /// tabs, opening on the Party tab, the System tab listing Options, and Flee while the party can flee
    /// (<see cref="Core.Combat.Battle.CanFlee"/>); from any other state (27), the System tab alone, with Options.
    /// </summary>
    public void OpenCombat(bool partyTab, bool canFlee)
    {
        _combat = (partyTab, canFlee);
        Open(partyTab ? PauseTab.Party : PauseTab.System, tabsLocked: false);
    }

    /// <param name="brightens">Whether the world brightens again as it closes; the loot window and shops snap back.</param>
    private void Open(PauseTab tab, bool tabsLocked, bool brightens = true)
    {
        // The frame before the menu covers it, which a save's screenshot is cut from.
        _snapshot = GetViewport().GetTexture().GetImage();
        _scroll = new PauseScroll((int)tab, brightens, holds: tabsLocked);
        _scrollView?.Restart();
        _scrollView?.Rendering = true;
        _exit = PauseExit.Close;
        _ended = null;
        _frame.Reset();
        // Skill checks and potions in trek draw from the world's stream.
        _view = Show(_view, PartyMenu.From(_plugins, _game.Party, random: _game.Random.World), (p, m) => new PartyTabView(p, m));
        _system = Show(_system, new SystemMenu(_plugins, _settings, _game.Party, DataPaths.Saves, isSet: _game.Flags.Get, combat: _combat?.CanFlee),
            (p, m) => new SystemTabView(p, m));
        var (place, outdoors) = Where?.Invoke() ?? (null, false);
        _world = Show(_world, new WorldMenu(_plugins, _game, _mainFont, place, outdoors), (p, m) => new WorldTabView(p, m));
        Tab = tab;
        _position = (float)tab;
        _tabsLocked = tabsLocked;
        Place();
        _view.DetailsOpen = false;
        _stick.Clear();
        Visible = true;
        Activate();
        ShowScroll();
        Opened?.Invoke();
    }

    /// <summary>
    /// A tab's view on the model this opening built: made the first time, laid out by <see cref="Place"/> at the screen's
    /// size rather than its parent's, and after that given the new model.
    /// </summary>
    private T Show<T, TModel>(T? view, TModel model, Func<PluginSet, TModel, T> make) where T : Control, IPauseTab<TModel>
    {
        if (view is not null)
        {
            ((IPauseTab<TModel>)view).Model = model;
            return view;
        }
        view = make(_plugins, model);
        _tabs.AddChild(view);
        view.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        return view;
    }

    /// <summary>The tabs' views, left to right, by <see cref="PauseTab"/>.</summary>
    private (Control View, PauseTab Tab)[] Tabs => [(_system!, PauseTab.System), (_view!, PauseTab.Party), (_world!, PauseTab.World)];

    /// <summary>
    /// Opens the menu in its barter state on loot (<c>build_loot_menu</c>), whose gold the party already has.
    /// Taking and dropping act on the game; the pile is lost once it closes.
    /// </summary>
    public void OpenLoot(ItemPile loot, int gold)
    {
        Open(PauseTab.Party, tabsLocked: true, brightens: false);
        _view!.Window = BarterMenu.ForLoot(_plugins, _game.Party, loot, gold, _game.Flags, _game.Random.World);
        // The coins, a second after the window opens, at the sound volume as their gain.
        if (gold > 0 && _game.Sounds is { } sounds)
            Audio?.Play(new Core.Audio.OneShot(sounds.GoldGained, Core.Audio.OneShot.LongTicks, Core.Audio.OneShot.MenuTicks, TimesSoundVolume: true));
    }

    /// <summary>
    /// Opens the menu in its barter state on a shop (<c>InitShop</c>), which <see cref="Shop.Open"/> has opened on the
    /// game's party. Buying and selling act on the game.
    /// </summary>
    public void OpenShop(Shop shop)
    {
        Open(PauseTab.Party, tabsLocked: true, brightens: false);
        _view!.Window = BarterMenu.ForShop(_plugins, shop, _game.Random.World);
    }

    /// <summary>Opens the menu in its training state at a trainer (<c>init_skill_trainer</c>). Training acts on the game.</summary>
    public void OpenTraining(Core.Characters.Trainer trainer)
    {
        Open(PauseTab.Party, tabsLocked: true);
        _view!.Window = TrainingMenu.For(_plugins, _game.Party, trainer, _game.Flags, _game.Random.World);
    }

    /// <summary>Rolls the scroll up and closes the menu, which then ends as <paramref name="exit"/> says (<c>PauseMenu.Exit</c>).</summary>
    private void Close(PauseExit exit)
    {
        if (_scroll.Phase is PauseScrollPhase.Rolling or PauseScrollPhase.Brightening)
            return;
        _exit = exit;
        if (_scroll.Phase != PauseScrollPhase.Open)
        {
            CloseNow();
            return;
        }
        SlideSound(opening: false);
        _scroll.Close((int)Tab);
        // The scroll takes its closing state at once (SlideOut).
        _scrollView?.Play(_scroll.TakeCue());
        _view!.DetailsOpen = false;
        _stick.Clear();
        ShowScroll();
    }

    /// <summary>Closes the menu at once, as loading a save does, ending as it was asked to (<see cref="PauseExit.Close"/> unless otherwise).</summary>
    public void CloseNow()
    {
        if (!IsOpen)
            return;
        if (_scroll.Phase is not (PauseScrollPhase.Rolling or PauseScrollPhase.Brightening or PauseScrollPhase.Closed))
            SlideSound(opening: false);
        Visible = false;
        _scrollView?.Rendering = false;
        if (!_tabsLocked && _combat is null)
            _lastTab = Tab;
        _snapshot = null;
        _journal.Journal = null;
        _view?.ProcessMode = ProcessModeEnum.Disabled;
        _system?.ProcessMode = ProcessModeEnum.Disabled;
        _ended = _exit;
    }

    /// <summary>Whether the menu takes input: once the scroll has unrolled, and until it rolls up.</summary>
    private bool Taking => _scroll.Phase == PauseScrollPhase.Open;

    /// <summary>
    /// One frame of the opening or closing: the world's tint, the scroll's step and where the tabs show. Closes the
    /// menu once it has rolled up and the world is bright.
    /// </summary>
    private void StepScroll()
    {
        var wasDarkening = _scroll.Phase == PauseScrollPhase.Darkening;
        // Without the scroll's actor, its animations take no time.
        if (_scrollView is { } view)
            _scroll.Step(view.Frame, view.Length);
        else
            _scroll.Step(1000, 1);
        if (_scroll.StepsScroll)
            _scrollView?.Play(_scroll.TakeCue());
        if (wasDarkening && _scroll.ShowsMenu)
            SlideSound(opening: true);
        if (_scroll.Phase == PauseScrollPhase.Closed)
        {
            CloseNow();
            return;
        }
        ShowScroll();
    }

    /// <summary>
    /// Shows the opening or closing as it is: the world's tint and whether, and how opaque, the tabs and scroll are
    /// drawn. They fade out as the journal fades in over them, and back as it fades out.
    /// </summary>
    private void ShowScroll()
    {
        _darkness.Color = new Color(0, 0, 0, 1 - _scroll.Tint / 255f);
        var alpha = _scroll.Alpha * (1 - _journal.Shown);
        _tabs.Visible = _scroll.ShowsMenu && alpha > 0;
        _tabs.Modulate = new Color(1, 1, 1, alpha);
        if (_scrollView is { } view)
        {
            view.Visible = _scroll.ShowsMenu && alpha > 0;
            view.Alpha = alpha;
        }
        Place();
    }

    /// <summary>Only the tab showing steps, and takes the mouse.</summary>
    private void Activate()
    {
        foreach (var (view, tab) in Tabs)
            ((IPauseTab)view).Activate(Tab == tab);
    }

    /// <summary>
    /// Lays the tabs side by side, 320 apart, with the one at <see cref="_position"/> showing at the screen's centre
    /// and those beside it at the screen's edges, faded, and cuts them to what the scroll has unrolled: within the
    /// original's clip, and between the scroll's rollers, so that its parchment never shows past them. Barter and
    /// training show the Party tab alone. The scroll moves with them.
    /// </summary>
    private void Place()
    {
        var screen = _screen.Canvas.GetViewportRect().Size;
        // The frame's left edge, as the tabs centre it.
        var frameLeft = ((int)screen.X - UiScreen.Width) / 2;
        var (left, right) = _scroll.Clip is var (l, r)
            ? (l <= 0 ? float.NegativeInfinity : frameLeft + l, r >= UiScreen.Width ? float.PositiveInfinity : frameLeft + r)
            : (float.NegativeInfinity, float.PositiveInfinity);
        if (_scrollView is { } view)
        {
            view.X = _position <= 1
                ? Mathf.Lerp(PauseScroll.TabX[0], PauseScroll.TabX[1], _position)
                : Mathf.Lerp(PauseScroll.TabX[1], PauseScroll.TabX[2], _position - 1);
            var (systemEnd, worldEnd) = view.Arrange();
            left = Math.Max(left, Mathf.Round(systemEnd));
            right = Math.Min(right, Mathf.Round(worldEnd));
        }
        left = Math.Clamp(left, 0, screen.X);
        right = Math.Clamp(right, 0, screen.X);
        _tabs.Position = new Vector2(left, 0);
        _tabs.Size = new Vector2(Math.Max(right - left, 0), screen.Y);
        foreach (var (control, tab) in Tabs)
        {
            var offset = ((int)tab - _position) * UiScreen.Width;
            control.Position = new Vector2(Mathf.Round(offset) - left, 0);
            control.Size = screen;
            control.Visible = Math.Abs(offset) < (screen.X + UiScreen.Width) / 2 && (!_tabsLocked || tab == PauseTab.Party) && tab <= LastTab;
            ((IPauseTab)control).Veil = Math.Min(1, Math.Abs((int)tab - _position)) * NeighbourVeil;
        }
    }

    /// <summary>Z or R: slides to the tab that way, if there is one and the tab showing can be left.</summary>
    private void ChangeTab(int by)
    {
        var to = (PauseTab)((int)Tab + by);
        if (_tabsLocked || Sliding || !Taking || to < PauseTab.System || to > LastTab)
            return;
        if (!((IPauseTab)Tabs[(int)Tab].View).CanLeave)
            return;
        Tab = to;
        Sound(by < 0 ? m => m.TabBack : m => m.TabForward, 2 * Core.Audio.OneShot.MenuTicks);
        _view!.DetailsOpen = false;
        Activate();
        Redraw();
    }

    /// <summary>
    /// A tick of the menu, which whoever runs the game calls every tick: while it's open, a frame of the scroll, the
    /// tabs' slide, a volume held, what the save screen asks and the World tab. Returns how the menu ended if it closed
    /// this tick (the original's exit code, <c>PauseMenu.Exit</c>, <c>0x80048e64</c>; ADR 0019), and otherwise null.
    /// </summary>
    public PauseExit? Step()
    {
        StepFrame();
        var ended = _ended;
        _ended = null;
        return ended;
    }

    private void StepFrame()
    {
        if (!IsOpen || _system is null)
            return;
        // The menu's frames.
        if (!_frame.Tick())
            return;
        StepScroll();
        if (!IsOpen)
            return;
        if (Sliding)
        {
            _position = Mathf.MoveToward(_position, (float)Tab, SlidePerTick);
            Redraw();
        }
        Place();
        // A volume steps every tick the stick is held, from the first.
        if (Tab == PauseTab.System && !Sliding && Taking && _system.Menu.OnVolume)
        {
            var by = (Held("move_right") ? 1 : 0) - (Held("move_left") ? 1 : 0);
            if (by != 0)
            {
                _system.Menu.Sideways(by);
                _system.QueueRedraw();
            }
        }
        if (_system.Menu.Saves is { Request: not SaveRequest.None } saves)
            CarryOut(saves);
        if (_world is { } world)
        {
            var showing = Tab == PauseTab.World;
            world.Active = showing;
            world.Menu.Step(showing, Sliding, showing && Held("move_forward"), showing && Held("move_back"));
            world.QueueRedraw();
            if (world.Menu.Journal is not null || _journal.Journal is not null)
            {
                _journal.Journal = world.Menu.Journal;
                _journal.QueueRedraw();
                ShowScroll();
            }
        }
    }

    /// <summary>Does what the save or load screen asks: writes the game, or loads a save.</summary>
    private void CarryOut(SaveMenu saves)
    {
        var target = saves.Target!;
        if (saves.Request == SaveRequest.Load)
        {
            if (LoadGame is { } load)
            {
                _exit = PauseExit.Load;
                CloseNow();
                load(target);
            }
            else
            {
                saves.Failed("Loading isn't possible here.");
            }
            return;
        }
        if (SaveGame?.Invoke(target, _snapshot) is { } error)
            saves.Failed(error);
        else
            saves.Saved();
    }

    /// <summary>
    /// Carries out a press or release while the menu is open: B and Start close a popup, a screen, or else the
    /// menu. True if the menu took it.
    /// </summary>
    public bool Handle(InputEvent e)
    {
        // Nothing reaches the menu while it opens or closes.
        if (!Taking)
            return e is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion or InputEventAction;
        // Nor do the presses a save's slide drops make a sound.
        var sliding = Sliding || _system?.Menu.Saves is { Sliding: true };
        var action = _stick.Translate(e);
        var taken = HandleInput(e, action);
        if (taken && !sliding && _view is not null)
            PressSound(e, action);
        return taken;
    }

    /// <summary>
    /// The sounds the menu makes for a press it took, whatever the page did with it (<c>BigAssMenu</c>'s callbacks): the
    /// cursor for up and down, A, B and Start, the member for C-left and C-right, and the page's cursor for the Party
    /// tab's lists and the World tab. The details slide in and out with C-down. Tabs, opening and closing play their own.
    /// </summary>
    private void PressSound(InputEvent e, MenuAction action)
    {
        const int Short = Core.Audio.OneShot.ShortTicks, Menu = Core.Audio.OneShot.MenuTicks;
        var party = Tab == PauseTab.Party && _view!.OwnTab;
        if (Tab == PauseTab.System && _system!.Menu.Rename is not null && e is InputEventKey)
            return;
        if (action == MenuAction.Accept)
            Sound(m => m.Accept, Menu);
        else if (action is MenuAction.Back or MenuAction.Start)
            Sound(m => m.Back, Menu);
        else if (action is MenuAction.Details or MenuAction.DetailsRelease)
            Sound(e.IsPressed() ? m => m.TabBack : m => m.TabForward, Menu);
        else if (action is MenuAction.MemberLeft or MenuAction.MemberRight && Tab == PauseTab.Party
            && _view!.Window.ChangesMember && _game.Party.Members.Count() > 1)
            Sound(m => m.Member, Menu);
        else if (action is MenuAction.Up or MenuAction.Down)
            CursorSound();
        else if ((party || (Tab == PauseTab.System && !_system!.Menu.OnVolume)) && action is MenuAction.Left or MenuAction.Right)
            Sound(m => m.PageCursor, Short);
    }

    /// <summary>
    /// Up or down: the menu's cursor sound, and the World tab's own besides. The Party tab's lists play the cursor's
    /// themselves too, so with no popup up it plays twice at once, louder, as the user confirmed. The journal takes the
    /// presses itself, and scrolls silently.
    /// </summary>
    private void CursorSound()
    {
        if (Tab == PauseTab.World && _world?.Menu.Journal is not null)
            return;
        Sound(m => m.Cursor, Core.Audio.OneShot.ShortTicks);
        if (Tab == PauseTab.Party && _view is { OwnTab: true, Window.HasPopup: false })
            Sound(m => m.Cursor, Core.Audio.OneShot.ShortTicks);
        if (Tab == PauseTab.World)
            Sound(m => m.PageCursor, Core.Audio.OneShot.ShortTicks);
    }

    private bool HandleInput(InputEvent e, MenuAction action)
    {
        if (_view is null || _system is null)
            return false;
        if (Tab == PauseTab.System && !Sliding && _system.Menu.Rename is { } rename && TypeName(e, rename))
        {
            _system.Menu.Settle();
            _system.QueueRedraw();
            return true;
        }
        if (action is MenuAction.Start or MenuAction.Back)
        {
            var start = action == MenuAction.Start;
            if (Sliding)
                return true;
            var held = Tab switch
            {
                PauseTab.System => start ? _system.Menu.Start() : _system.Menu.Back(),
                PauseTab.World => _world!.Menu.Back(),
                _ => Back(),
            };
            if (!held)
                Close(PauseExit.Close);
            Redraw();
            return true;
        }
        if (action is MenuAction.TabPrevious or MenuAction.TabNext)
        {
            ChangeTab(action == MenuAction.TabPrevious ? -1 : 1);
            return true;
        }
        if (Sliding)
            return e is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion;
        if (Tab == PauseTab.System)
            return HandleSystem(e, action, _system.Menu);
        if (Tab == PauseTab.World)
            return HandleWorld(e, action, _world!.Menu);
        return HandleParty(e, action, _view.Window);
    }

    /// <summary>
    /// The Party tab's presses, whichever window it shows: C-down's details, A, C-left and C-right, the stick's left and right,
    /// and its up and down, each as the window takes it (<see cref="IPartyWindow"/>), and C-up, which the shop's window has a use for.
    /// </summary>
    private bool HandleParty(InputEvent e, MenuAction action, IPartyWindow window)
    {
        if (action == MenuAction.Details && window.CloseMessage())
        {
        }
        else if (action == MenuAction.Appraise && window.Appraise())
        {
        }
        else if (action is MenuAction.Details or MenuAction.DetailsRelease)
        {
            _view!.DetailsOpen = e.IsPressed();
        }
        else if (action == MenuAction.Accept)
        {
            window.Accept();
            if (window.Closed)
            {
                Close(PauseExit.Close);
                return true;
            }
        }
        // C-left shows the next member and C-right the one before; the stick's left the next list and its right the
        // one before, as the original's do.
        else if (action == MenuAction.MemberLeft)
        {
            window.ChangeMember(1);
        }
        else if (action == MenuAction.MemberRight)
        {
            window.ChangeMember(-1);
        }
        else if (action is MenuAction.Left or MenuAction.Right)
        {
            window.Sideways(MenuStick.Horizontal(action));
        }
        else if (action is MenuAction.Up or MenuAction.Down)
        {
            window.MoveCursor(MenuStick.Vertical(action));
        }
        else
        {
            return e is InputEventJoypadMotion;
        }
        _view!.QueueRedraw();
        return true;
    }

    /// <summary>
    /// B on the Party tab: closes a popup, and false if none was open, so the menu closes. The loot window asks
    /// before leaving items.
    /// </summary>
    private bool Back() => _view!.Window.Back();

    private void Redraw()
    {
        _view?.QueueRedraw();
        _system?.QueueRedraw();
        _world?.QueueRedraw();
    }

    /// <summary>Whether the stick, the movement keys or the D-pad hold a direction.</summary>
    private static bool Held(string move) => MenuStick.Held(move) || MenuStick.Held(MenuStick.Dpad(move));

    // The System tab's presses: the stick moves the cursor, steps an option or a save, or moves over the name entry's keys.
    private bool HandleSystem(InputEvent e, MenuAction action, SystemMenu system)
    {
        if (action == MenuAction.Accept)
        {
            // Help, Credits and a seen cinematic roll the menu up as closing does, and start once it has (PauseMenu.Exit's
            // codes 3, 5 and 2).
            if (system.Accept() is { } exit)
            {
                Close(exit);
                return true;
            }
        }
        else if (system.OnVolume && (e.IsAction("move_left") || e.IsAction("move_right") || e.IsAction("menu_left") || e.IsAction("menu_right")))
            return true; // Stepped every tick it's held, in _PhysicsProcess.
        else if (action is MenuAction.Left or MenuAction.Right)
            system.Sideways(MenuStick.Horizontal(action));
        else if (action is MenuAction.Up or MenuAction.Down)
            system.MoveCursor(MenuStick.Vertical(action));
        else
            return e is InputEventJoypadMotion;
        _system!.QueueRedraw();
        return true;
    }

    // The World tab's presses: up and down move between Camp and Journal, or scroll the journal; A chooses, or closes
    // the journal or the message.
    private bool HandleWorld(InputEvent e, MenuAction action, WorldMenu world)
    {
        if (action == MenuAction.Accept)
        {
            // Camp rolls the menu up, and the party camps once it has.
            if (world.Accept() is { } exit)
            {
                Close(exit);
                return true;
            }
        }
        else if (action is MenuAction.Up or MenuAction.Down)
        {
            Vertical(world, MenuStick.Vertical(action));
        }
        else
        {
            return e is InputEventJoypadMotion;
        }
        _world!.QueueRedraw();
        return true;
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

    /// <summary>
    /// The keyboard in the name entry: a printable key types its character, Backspace rubs out, Enter confirms
    /// and Escape cancels, before any of them reach the menu's actions.
    /// </summary>
    private static bool TypeName(InputEvent e, NameEntry entry)
    {
        if (e is not InputEventKey { Pressed: true } key)
            return false;
        switch (key.Keycode)
        {
            case Key.Backspace:
                entry.RubOut();
                return true;
            case Key.Enter or Key.KpEnter:
                entry.Confirm();
                return true;
            case Key.Escape:
                entry.Back();
                return true;
        }
        return key.Unicode is > 31 and < 127 && entry.Type((char)key.Unicode);
    }

    public override void _Process(double delta)
    {
        if (!IsOpen || _view is null || _system is null || Sliding || !Taking)
            return;
        _stick.Step(delta, Repeat);
    }

    /// <summary>The stick held: the cursor moves on, and on the System tab, an option or the name entry's key too.</summary>
    private void Repeat(string action)
    {
        var by = action is "move_forward" or "move_left" ? -1 : 1;
        var vertical = action is "move_forward" or "move_back";
        if (vertical)
            CursorSound();
        if (Tab == PauseTab.System)
        {
            if (vertical)
                _system!.Menu.MoveCursor(by);
            else if (!_system!.Menu.OnVolume)
                _system.Menu.Sideways(by);
            _system.QueueRedraw();
            return;
        }
        if (!vertical)
            return;
        if (Tab == PauseTab.World)
        {
            Vertical(_world!.Menu, by);
            _world.QueueRedraw();
            return;
        }
        _view!.Window.MoveCursor(by);
        _view.QueueRedraw();
    }
}
