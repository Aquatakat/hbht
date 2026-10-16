using Aidyn.Core;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>The pause menu's tabs, left to right (<c>PauseMenuSection</c>).</summary>
public enum PauseTab
{
    System,
    Party,
    World,
}

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
/// Opening, the world as it was drawn freezes and darkens to half over 13 frames, and then the 3D scroll
/// (<see cref="PauseScrollView"/>) unrolls with the tabs between its rollers; closing, it rolls up and the world
/// brightens again (<see cref="PauseScroll"/>). The menu takes input only while it's open.
/// </para>
/// <para>
/// Z or L, and R, change the tab, which slides over 32 frames, as the original's moves 10 pixels a frame across its
/// 320, with the scroll; presses wait while it slides. There's no wrap. The System tab can be left only from its list, as the
/// original's can. On a PC, Z and X or the triggers change the tab.
/// </para>
/// <para>
/// The tabs are one scroll, as their images are drawn. In the classic layout they're 320 apart, so a screen wider
/// than the frame shows the edges of the tabs either side, their widgets faded into the parchment, which clears as a
/// tab slides into the middle. In the wide layout each tab fills the screen, and they're a screen's width apart.
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
/// In the loot window, the stick's left and right go to the list that way: the pile is left of the party's list in
/// the classic layout, as the original's is, and right of it in the wide one. C-left and C-right do nothing there,
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

    private const PauseTab LastTab = PauseTab.World;

    /// <summary>The tab the menu last showed, which a default opening goes back to (<c>gGlobals.pauseMenuSection</c>).</summary>
    private static PauseTab _lastTab = PauseTab.System;

    private readonly PluginSet _plugins;
    private readonly GameState _game;
    private readonly Settings _settings;
    private readonly UiScreen _screen = new() { ForwardsMouse = true };
    private readonly MenuStick _stick;
    private readonly TextLayout _mainFont;
    /// <summary>The world as it was when the menu opened, darkened behind it (<c>GetSnapShot</c>, <c>PauseMenu.DrawBackdrop</c>).</summary>
    private readonly TextureRect _backdrop = new()
    {
        Name = "Backdrop",
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    /// <summary>The tabs, cut to what the scroll has unrolled.</summary>
    private readonly Control _tabs = new() { Name = "Tabs", ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly Models _models;
    private readonly PauseScrollView? _scrollView;
    /// <summary>The World tab's journal, over the tabs and the scroll, which fade out under it.</summary>
    private readonly JournalView _journal;
    private PauseScroll _scroll = new(0);
    private Action? _afterClosing;
    private int _ticks;
    private PartyTabView? _view;
    private SystemTabView? _system;
    private WorldTabView? _world;
    /// <summary>Where the tabs are: the tab shown, or between two as they slide.</summary>
    private float _position;
    /// <summary>The world as it was drawn when the menu opened, which a save's screenshot is cut from.</summary>
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
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_backdrop);
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

    /// <summary>The menu closed.</summary>
    public event Action? Closed;

    /// <summary>
    /// Writes the game to a save file, with its screenshot from the world as the menu opened it; returns why it
    /// couldn't, or null.
    /// </summary>
    public Func<string, Image?, string?>? SaveGame { get; set; }

    /// <summary>Loads the save file, ending this game.</summary>
    public Action<string>? LoadGame { get; set; }

    /// <summary>The chunk the player is in, and whether it's outdoors, which the World tab's map and Camp read.</summary>
    public Func<(Plugins.Records.ChunkRef? Place, bool Outdoors)>? Where { get; set; }

    /// <summary>Camp was chosen on the World tab where the party can camp; the menu has closed.</summary>
    public event Action? CampChosen;

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
    public void Open(PauseTab? tab = null) => Open(tab ?? _lastTab, tabsLocked: false);

    /// <param name="brightens">Whether the world brightens again as it closes; the loot window and shops snap back.</param>
    private void Open(PauseTab tab, bool tabsLocked, bool brightens = true)
    {
        // The world as it was before the menu covered it, which it darkens behind it; a save's screenshot is cut from it.
        _snapshot = GetViewport().GetTexture().GetImage();
        _backdrop.Texture = ImageTexture.CreateFromImage(_snapshot);
        _scroll = new PauseScroll((int)tab, brightens, holds: tabsLocked);
        _scrollView?.Restart();
        _scrollView?.Rendering = true;
        _afterClosing = null;
        _ticks = 0;
        // Skill checks and potions in trek draw from the world's stream.
        var menu = PartyMenu.From(_plugins, _game.Party, random: _game.Random.World);
        if (_view is null)
        {
            _view = new PartyTabView(_plugins, menu);
            AddTab(_view);
        }
        else
        {
            _view.Menu = menu;
        }
        _view.Barter = null;
        _view.Training = null;
        var system = new SystemMenu(_plugins, _settings, _game.Party, DataPaths.Saves);
        if (_system is null)
        {
            _system = new SystemTabView(_plugins, system);
            AddTab(_system);
        }
        else
        {
            _system.Menu = system;
        }
        var (place, outdoors) = Where?.Invoke() ?? (null, false);
        var world = new WorldMenu(_plugins, _game, _mainFont, place, outdoors);
        if (_world is null)
        {
            _world = new WorldTabView(_plugins, world);
            AddTab(_world);
        }
        else
        {
            _world.Menu = world;
        }
        Tab = tab;
        _position = (float)tab;
        _tabsLocked = tabsLocked;
        Place();
        _view.DetailsOpen = false;
        _stick.Clear();
        Visible = true;
        Activate();
        ShowScroll();
    }

    /// <summary>A tab, laid out by <see cref="Place"/> at the screen's size rather than its parent's.</summary>
    private void AddTab(Control tab)
    {
        _tabs.AddChild(tab);
        tab.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
    }

    /// <summary>
    /// Opens the menu in its barter state on loot (<c>build_loot_menu</c>), whose gold the party already has.
    /// Taking and dropping act on the game; the pile is lost once it closes.
    /// </summary>
    public void OpenLoot(ItemPile loot, int gold)
    {
        Open(PauseTab.Party, tabsLocked: true, brightens: false);
        _view!.Barter = BarterMenu.ForLoot(_plugins, _game.Party, loot, gold, _game.Flags, _game.Random.World);
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
        _view!.Barter = BarterMenu.ForShop(_plugins, shop, _game.Random.World);
    }

    /// <summary>Opens the menu in its training state at a trainer (<c>init_skill_trainer</c>). Training acts on the game.</summary>
    public void OpenTraining(Core.Characters.Trainer trainer)
    {
        Open(PauseTab.Party, tabsLocked: true);
        _view!.Training = TrainingMenu.For(_plugins, _game.Party, trainer, _game.Flags, _game.Random.World);
    }

    /// <summary>Rolls the scroll up and closes the menu, and then does <paramref name="then"/>.</summary>
    public void Close(Action? then = null)
    {
        if (_scroll.Phase is PauseScrollPhase.Rolling or PauseScrollPhase.Brightening)
            return;
        if (_scroll.Phase != PauseScrollPhase.Open)
        {
            CloseNow();
            then?.Invoke();
            return;
        }
        SlideSound(opening: false);
        _scroll.Close((int)Tab);
        // The scroll takes its closing state at once (SlideOut).
        _scrollView?.Play(_scroll.TakeCue());
        _afterClosing = then;
        _view!.DetailsOpen = false;
        _stick.Clear();
        ShowScroll();
    }

    /// <summary>Closes the menu at once, as loading a save does.</summary>
    public void CloseNow()
    {
        if (!IsOpen)
            return;
        if (_scroll.Phase is not (PauseScrollPhase.Rolling or PauseScrollPhase.Brightening or PauseScrollPhase.Closed))
            SlideSound(opening: false);
        Visible = false;
        _scrollView?.Rendering = false;
        if (!_tabsLocked)
            _lastTab = Tab;
        _snapshot = null;
        _backdrop.Texture = null;
        _journal.Journal = null;
        _view?.ProcessMode = ProcessModeEnum.Disabled;
        _system?.ProcessMode = ProcessModeEnum.Disabled;
        Closed?.Invoke();
        var then = _afterClosing;
        _afterClosing = null;
        then?.Invoke();
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
        var tint = _scroll.Tint / 255f;
        _backdrop.SelfModulate = new Color(tint, tint, tint);
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
        _view!.ProcessMode = Tab == PauseTab.Party ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _system!.ProcessMode = Tab == PauseTab.System ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _view.MouseFilter = Tab == PauseTab.Party ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
        _system.MouseFilter = Tab == PauseTab.System ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
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
        foreach (var (control, tab) in new (Control, PauseTab)[] { (_system!, PauseTab.System), (_view!, PauseTab.Party), (_world!, PauseTab.World) })
        {
            var offset = ((int)tab - _position) * UiScreen.Width;
            control.Position = new Vector2(Mathf.Round(offset) - left, 0);
            control.Size = screen;
            control.Visible = Math.Abs(offset) < (screen.X + UiScreen.Width) / 2 && (!_tabsLocked || tab == PauseTab.Party);
            var veil = Math.Min(1, Math.Abs((int)tab - _position)) * NeighbourVeil;
            if (control is PartyTabView party)
                party.Veil = veil;
            else if (control is SystemTabView system)
                system.Veil = veil;
            else if (control is WorldTabView world)
                world.Veil = veil;
        }
    }

    /// <summary>Z or R: slides to the tab that way, if there is one and the tab showing can be left.</summary>
    private void ChangeTab(int by)
    {
        var to = (PauseTab)((int)Tab + by);
        if (_tabsLocked || Sliding || !Taking || to < PauseTab.System || to > LastTab)
            return;
        if (Tab == PauseTab.System && !_system!.Menu.OnList)
            return;
        if (Tab == PauseTab.Party && (_view!.Menu.Message is not null || _view.Menu.Crafting is not null))
            return;
        if (Tab == PauseTab.World && (_world!.Menu.Message is not null || _world.Menu.Journal is not null))
            return;
        Tab = to;
        Sound(by < 0 ? m => m.TabBack : m => m.TabForward, 2 * Core.Audio.OneShot.MenuTicks);
        _view!.DetailsOpen = false;
        Activate();
        Redraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsOpen || _system is null)
            return;
        // The menu's frames.
        if (++_ticks % Cadence.MenuFrameTicks != 0)
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
        var sliding = Sliding;
        var taken = HandleInput(e);
        if (taken && !sliding && _view is not null)
            PressSound(e);
        return taken;
    }

    /// <summary>
    /// The sounds the menu makes for a press it took, whatever the page did with it (<c>BigAssMenu</c>'s callbacks): the
    /// cursor for up and down, A, B and Start, the member for C-left and C-right, and the page's cursor for the Party
    /// tab's lists and the World tab. The details slide in and out with C-down. Tabs, opening and closing play their own.
    /// </summary>
    private void PressSound(InputEvent e)
    {
        const int Short = Core.Audio.OneShot.ShortTicks, Menu = Core.Audio.OneShot.MenuTicks;
        var party = Tab == PauseTab.Party && _view!.Barter is null && _view.Training is null;
        if (Tab == PauseTab.System && _system!.Menu.Rename is not null && e is InputEventKey)
            return;
        if (e.IsActionPressed("menu_accept"))
            Sound(m => m.Accept, Menu);
        else if (e.IsActionPressed("menu_back") || e.IsActionPressed("pause"))
            Sound(m => m.Back, Menu);
        else if (e.IsActionPressed("menu_details") || e.IsActionReleased("menu_details"))
            Sound(e.IsPressed() ? m => m.TabBack : m => m.TabForward, Menu);
        else if ((e.IsActionPressed("menu_member_left") || e.IsActionPressed("menu_member_right")) && Tab == PauseTab.Party
            && _view!.Barter is null && _game.Party.Members.Count() > 1)
            Sound(m => m.Member, Menu);
        else if (Pressed(e, "move_forward") || Pressed(e, "move_back") || e.IsActionPressed("menu_up", allowEcho: true)
            || e.IsActionPressed("menu_down", allowEcho: true))
            CursorSound();
        else if ((party || (Tab == PauseTab.System && !_system!.Menu.OnVolume))
            && (Pressed(e, "move_left") || Pressed(e, "move_right") || e.IsActionPressed("menu_left") || e.IsActionPressed("menu_right")))
            Sound(m => m.PageCursor, Short);
    }

    /// <summary>Up or down: the menu's cursor sound, and the World tab's own besides.</summary>
    private void CursorSound()
    {
        Sound(m => m.Cursor, Core.Audio.OneShot.ShortTicks);
        if (Tab == PauseTab.World)
            Sound(m => m.PageCursor, Core.Audio.OneShot.ShortTicks);
    }

    private bool HandleInput(InputEvent e)
    {
        if (_view is null || _system is null)
            return false;
        if (Tab == PauseTab.System && !Sliding && _system.Menu.Rename is { } rename && TypeName(e, rename))
        {
            _system.Menu.Settle();
            _system.QueueRedraw();
            return true;
        }
        var start = e.IsActionPressed("pause");
        if (start || e.IsActionPressed("menu_back"))
        {
            if (Sliding)
                return true;
            var held = Tab switch
            {
                PauseTab.System => start ? _system.Menu.Start() : _system.Menu.Back(),
                PauseTab.World => _world!.Menu.Back(),
                _ => Back(),
            };
            if (!held)
                Close();
            Redraw();
            return true;
        }
        if (Pressed(e, "menu_tab_previous"))
        {
            ChangeTab(-1);
            return true;
        }
        if (Pressed(e, "menu_tab_next"))
        {
            ChangeTab(1);
            return true;
        }
        if (Sliding)
            return e is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion;
        if (Tab == PauseTab.System)
            return HandleSystem(e, _system.Menu);
        if (Tab == PauseTab.World)
            return HandleWorld(e, _world!.Menu);
        if (_view.Barter is { } barter)
            return HandleBarter(e, barter);
        if (_view.Training is { } training)
            return HandleTraining(e, training);
        var menu = _view.Menu;
        if (e.IsActionPressed("menu_details") && menu.Message is not null)
            menu.CloseMessage();
        else if (e.IsActionPressed("menu_details") || e.IsActionReleased("menu_details"))
            _view.DetailsOpen = e.IsPressed();
        else if (e.IsActionPressed("menu_accept"))
            menu.Accept();
        // C-left shows the next member and C-right the one before; the stick's left the next list and its right the
        // one before, as the original's do.
        else if (e.IsActionPressed("menu_member_left"))
            menu.ChangeMember(1);
        else if (e.IsActionPressed("menu_member_right"))
            menu.ChangeMember(-1);
        else if (Pressed(e, "move_left") || e.IsActionPressed("menu_left"))
            menu.ChangeList(1);
        else if (Pressed(e, "move_right") || e.IsActionPressed("menu_right"))
            menu.ChangeList(-1);
        else if (Pressed(e, "move_forward") || e.IsActionPressed("menu_up", allowEcho: true))
            menu.MoveCursor(-1);
        else if (Pressed(e, "move_back") || e.IsActionPressed("menu_down", allowEcho: true))
            menu.MoveCursor(1);
        else
            return e is InputEventJoypadMotion;
        _view.QueueRedraw();
        return true;
    }

    /// <summary>
    /// B on the Party tab: closes a popup, and false if none was open, so the menu closes. The loot window asks
    /// before leaving items.
    /// </summary>
    private bool Back()
    {
        var view = _view!;
        return view.Barter is { } barter ? barter.Back() : view.Training is { } training ? training.Back() : view.Menu.Back();
    }

    private void Redraw()
    {
        _view?.QueueRedraw();
        _system?.QueueRedraw();
        _world?.QueueRedraw();
    }

    /// <summary>Whether the stick, the movement keys or the D-pad hold a direction.</summary>
    private static bool Held(string move) => MenuStick.Held(move) || MenuStick.Held(MenuStick.Dpad(move));

    // The System tab's presses: the stick moves the cursor, steps an option or a save, or moves over the name entry's keys.
    private bool HandleSystem(InputEvent e, SystemMenu system)
    {
        if (e.IsActionPressed("menu_accept"))
            system.Accept();
        else if (system.OnVolume && (e.IsAction("move_left") || e.IsAction("move_right") || e.IsAction("menu_left") || e.IsAction("menu_right")))
            return true; // Stepped every tick it's held, in _PhysicsProcess.
        else if (Pressed(e, "move_left") || e.IsActionPressed("menu_left", allowEcho: true))
            system.Sideways(-1);
        else if (Pressed(e, "move_right") || e.IsActionPressed("menu_right", allowEcho: true))
            system.Sideways(1);
        else if (Pressed(e, "move_forward") || e.IsActionPressed("menu_up", allowEcho: true))
            system.MoveCursor(-1);
        else if (Pressed(e, "move_back") || e.IsActionPressed("menu_down", allowEcho: true))
            system.MoveCursor(1);
        else
            return e is InputEventJoypadMotion;
        _system!.QueueRedraw();
        return true;
    }

    // The World tab's presses: up and down move between Camp and Journal, or scroll the journal; A chooses, or closes
    // the journal or the message.
    private bool HandleWorld(InputEvent e, WorldMenu world)
    {
        if (e.IsActionPressed("menu_accept"))
        {
            world.Accept();
            if (world.CampChosen)
            {
                Close(() => CampChosen?.Invoke());
                return true;
            }
        }
        else if (Pressed(e, "move_forward") || e.IsActionPressed("menu_up", allowEcho: true))
        {
            Vertical(world, -1);
        }
        else if (Pressed(e, "move_back") || e.IsActionPressed("menu_down", allowEcho: true))
        {
            Vertical(world, 1);
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

    // The barter state's presses: the stick's left and right reach the list on that side.
    private bool HandleBarter(InputEvent e, BarterMenu barter)
    {
        if (e.IsActionPressed("menu_details") && barter.CloseMessage())
        {
        }
        else if (e.IsActionPressed("menu_appraise"))
        {
            barter.Appraise();
        }
        else if (e.IsActionPressed("menu_details") || e.IsActionReleased("menu_details"))
        {
            _view!.DetailsOpen = e.IsPressed();
        }
        else if (e.IsActionPressed("menu_accept"))
        {
            barter.Accept();
            if (barter.Closed)
            {
                Close();
                return true;
            }
        }
        else if (Pressed(e, "move_left") || e.IsActionPressed("menu_left"))
        {
            Side(barter, right: false);
        }
        else if (Pressed(e, "move_right") || e.IsActionPressed("menu_right"))
        {
            Side(barter, right: true);
        }
        else if (Pressed(e, "move_forward") || e.IsActionPressed("menu_up", allowEcho: true))
        {
            barter.MoveCursor(-1);
        }
        else if (Pressed(e, "move_back") || e.IsActionPressed("menu_down", allowEcho: true))
        {
            barter.MoveCursor(1);
        }
        else
        {
            // C-left and C-right don't change the member here.
            return e is InputEventJoypadMotion || e.IsActionPressed("menu_member_left") || e.IsActionPressed("menu_member_right");
        }
        _view!.QueueRedraw();
        return true;
    }

    // The training state's presses.
    private bool HandleTraining(InputEvent e, TrainingMenu training)
    {
        if (e.IsActionPressed("menu_details") && training.CloseMessage())
        {
        }
        else if (e.IsActionPressed("menu_details") || e.IsActionReleased("menu_details"))
            _view!.DetailsOpen = e.IsPressed();
        else if (e.IsActionPressed("menu_accept"))
            training.Accept();
        else if (e.IsActionPressed("menu_member_left"))
            training.ChangeMember(1);
        else if (e.IsActionPressed("menu_member_right"))
            training.ChangeMember(-1);
        else if (Pressed(e, "move_left") || e.IsActionPressed("menu_left") || Pressed(e, "move_right") || e.IsActionPressed("menu_right"))
            training.Switch();
        else if (Pressed(e, "move_forward") || e.IsActionPressed("menu_up", allowEcho: true))
            training.MoveCursor(-1);
        else if (Pressed(e, "move_back") || e.IsActionPressed("menu_down", allowEcho: true))
            training.MoveCursor(1);
        else
            return e is InputEventJoypadMotion;
        _view!.QueueRedraw();
        return true;
    }

    /// <summary>The stick's left reaches the pile, and its right the party's list, as they're laid out.</summary>
    private static void Side(BarterMenu barter, bool right)
    {
        if (right)
            barter.ShowParty();
        else
            barter.ShowPile();
    }

    private bool Pressed(InputEvent e, string action) => _stick.Pressed(e, action);

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
        if (_view!.Barter is { } barter)
            barter.MoveCursor(by);
        else if (_view.Training is { } training)
            training.MoveCursor(by);
        else
            _view.Menu.MoveCursor(by);
        _view.QueueRedraw();
    }
}
