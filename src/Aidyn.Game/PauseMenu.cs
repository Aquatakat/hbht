using Aidyn.Core;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>The pause menu's tabs, left to right (<c>PauseMenuSection</c>). World isn't ported yet.</summary>
public enum PauseTab
{
    System,
    Party,
}

/// <summary>
/// The pause menu, which Start opens over the game (<c>BigAssMenu</c>): the System tab (<see cref="SystemTabView"/>)
/// and the Party tab (<see cref="PartyTabView"/>) side by side on a <see cref="UiScreen"/> over the world, and the
/// Party tab's barter state: the loot window, which a container opens (<see cref="OpenLoot"/>), and a shop, which
/// a script opens (<see cref="OpenShop"/>); and its training state, which a script opens at a trainer
/// (<see cref="OpenTraining"/>).
/// </summary>
/// <remarks>
/// <para>
/// Z or L, and R, change the tab, which slides a screen's width over 32 frames, as the original's moves 10 pixels a
/// frame across its 320; presses wait while it slides. There's no wrap. The System tab can be left only from its
/// list, as the original's can. On a PC, Z and X or the triggers change the tab.
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

    /// <summary>
    /// How long the stick held waits before the cursor moves on by itself, and how often it does then, in seconds:
    /// the port's choice, as a held key repeats.
    /// </summary>
    private const double RepeatDelay = 0.4, RepeatEvery = 0.1;

    /// <summary>How far a tab slides a tick: the original's 10 pixels a frame of its 320.</summary>
    private const float SlidePerTick = 10f / 320 / Cadence.FrameTicks;

    private static readonly string[] StickActions = ["move_forward", "move_back", "move_left", "move_right"];

    /// <summary>The tab the menu last showed, which a default opening goes back to (<c>gGlobals.pauseMenuSection</c>).</summary>
    private static PauseTab _lastTab = PauseTab.System;

    private readonly PluginSet _plugins;
    private readonly GameState _game;
    private readonly Settings _settings;
    private readonly UiScreen _screen = new() { ForwardsMouse = true };
    /// <summary>The stick's directions held past the deadzone, and how long until each repeats.</summary>
    private readonly Dictionary<string, double> _stick = [];
    private PartyTabView? _view;
    private SystemTabView? _system;
    /// <summary>Where the tabs are: the tab shown, or between two as they slide.</summary>
    private float _position;
    /// <summary>The world as it was drawn when the menu opened, which a save's screenshot is cut from.</summary>
    private Image? _snapshot;
    private bool _tabsLocked;

    public PauseMenu(PluginSet plugins, GameState game, Settings settings)
    {
        _plugins = plugins;
        _game = game;
        _settings = settings;
        Name = "Pause menu";
        Layer = MenuLayer;
        Visible = false;
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);
    }

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

    /// <summary>The UI screen, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    private bool Sliding => !Mathf.IsEqualApprox(_position, (float)Tab);

    /// <summary>Opens the menu afresh on the party as it is now, on the tab it last showed.</summary>
    public void Open() => Open(_lastTab, tabsLocked: false);

    private void Open(PauseTab tab, bool tabsLocked)
    {
        // The world as it was before the menu covered it; a save's screenshot is cut from it.
        _snapshot = GetViewport().GetTexture().GetImage();
        // Skill checks and potions in trek draw from the world's stream.
        var menu = PartyMenu.From(_plugins, _game.Party, random: _game.Random.World);
        if (_view is null)
        {
            _view = new PartyTabView(_plugins, menu);
            _screen.Canvas.AddChild(_view);
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
            _screen.Canvas.AddChild(_system);
        }
        else
        {
            _system.Menu = system;
        }
        Tab = tab;
        _position = (float)tab;
        _tabsLocked = tabsLocked;
        Place();
        _view.DetailsOpen = false;
        _stick.Clear();
        Visible = true;
        Activate();
    }

    /// <summary>
    /// Opens the menu in its barter state on loot (<c>build_loot_menu</c>), whose gold the party already has.
    /// Taking and dropping act on the game; the pile is lost once it closes.
    /// </summary>
    public void OpenLoot(ItemPile loot, int gold)
    {
        Open(PauseTab.Party, tabsLocked: true);
        _view!.Barter = BarterMenu.ForLoot(_plugins, _game.Party, loot, gold, _game.Flags, _game.Random.World);
    }

    /// <summary>
    /// Opens the menu in its barter state on a shop (<c>InitShop</c>), which <see cref="Shop.Open"/> has opened on the
    /// game's party. Buying and selling act on the game.
    /// </summary>
    public void OpenShop(Shop shop)
    {
        Open(PauseTab.Party, tabsLocked: true);
        _view!.Barter = BarterMenu.ForShop(_plugins, shop, _game.Random.World);
    }

    /// <summary>Opens the menu in its training state at a trainer (<c>init_skill_trainer</c>). Training acts on the game.</summary>
    public void OpenTraining(Core.Characters.Trainer trainer)
    {
        Open(PauseTab.Party, tabsLocked: true);
        _view!.Training = TrainingMenu.For(_plugins, _game.Party, trainer, _game.Flags, _game.Random.World);
    }

    public void Close()
    {
        Visible = false;
        if (!_tabsLocked)
            _lastTab = Tab;
        _snapshot = null;
        _view?.ProcessMode = ProcessModeEnum.Disabled;
        _system?.ProcessMode = ProcessModeEnum.Disabled;
        Closed?.Invoke();
    }

    /// <summary>Only the tab showing steps, and takes the mouse.</summary>
    private void Activate()
    {
        _view!.ProcessMode = Tab == PauseTab.Party ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _system!.ProcessMode = Tab == PauseTab.System ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _view.MouseFilter = Tab == PauseTab.Party ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
        _system.MouseFilter = Tab == PauseTab.System ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
    }

    /// <summary>Lays the tabs side by side, a screen's width apart, with the one at <see cref="_position"/> showing.</summary>
    private void Place()
    {
        var width = _screen.Canvas.GetViewportRect().Size.X;
        _system!.Position = new Vector2(Mathf.Round(((int)PauseTab.System - _position) * width), 0);
        _view!.Position = new Vector2(Mathf.Round(((int)PauseTab.Party - _position) * width), 0);
        _system.Visible = Math.Abs((int)PauseTab.System - _position) < 1;
        _view.Visible = Math.Abs((int)PauseTab.Party - _position) < 1;
    }

    /// <summary>Z or R: slides to the tab that way, if there is one and the tab showing can be left.</summary>
    private void ChangeTab(int by)
    {
        if (_tabsLocked || Sliding || !Enum.IsDefined((PauseTab)((int)Tab + by)))
            return;
        if (Tab == PauseTab.System && !_system!.Menu.OnList)
            return;
        if (Tab == PauseTab.Party && (_view!.Menu.Message is not null || _view.Menu.Crafting is not null))
            return;
        Tab += by;
        _view!.DetailsOpen = false;
        Activate();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsOpen || _system is null)
            return;
        if (Sliding)
        {
            _position = Mathf.MoveToward(_position, (float)Tab, SlidePerTick);
            Place();
        }
        else
        {
            Place();
        }
        if (_system.Menu.Saves is { Request: not SaveRequest.None } saves)
            CarryOut(saves);
    }

    /// <summary>Does what the save or load screen asks: writes the game, or loads a save.</summary>
    private void CarryOut(SaveMenu saves)
    {
        var target = saves.Target!;
        if (saves.Request == SaveRequest.Load)
        {
            if (LoadGame is { } load)
            {
                Close();
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
            var held = Tab == PauseTab.System ? start ? _system.Menu.Start() : _system.Menu.Back() : Back();
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
        else if (e.IsActionPressed("menu_member_previous"))
            menu.ChangeMember(-1);
        else if (e.IsActionPressed("menu_member_next"))
            menu.ChangeMember(1);
        else if (Pressed(e, "move_left") || e.IsActionPressed("menu_left"))
            menu.ChangeList(-1);
        else if (Pressed(e, "move_right") || e.IsActionPressed("menu_right"))
            menu.ChangeList(1);
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
    }

    // The System tab's presses: the stick moves the cursor, steps an option or a save, or moves over the name entry's keys.
    private bool HandleSystem(InputEvent e, SystemMenu system)
    {
        if (e.IsActionPressed("menu_accept"))
            system.Accept();
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
            return e is InputEventJoypadMotion || e.IsActionPressed("menu_member_previous") || e.IsActionPressed("menu_member_next");
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
        else if (e.IsActionPressed("menu_member_previous"))
            training.ChangeMember(-1);
        else if (e.IsActionPressed("menu_member_next"))
            training.ChangeMember(1);
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

    private void Side(BarterMenu barter, bool right)
    {
        if (right == _view!.PileOnRight)
            barter.ShowPile();
        else
            barter.ShowParty();
    }

    /// <summary>
    /// Whether an event presses an action. A key repeats as it's held. The stick and the triggers send an event
    /// whenever they move, each past the deadzone reading as pressed, so they count only as they cross the deadzone;
    /// the stick, held, repeats in <see cref="_Process"/>.
    /// </summary>
    private bool Pressed(InputEvent e, string action)
    {
        if (e is not InputEventJoypadMotion)
            return e.IsActionPressed(action, allowEcho: true);
        if (!e.IsAction(action))
            return false;
        if (!e.IsActionPressed(action))
        {
            _stick.Remove(action);
            return false;
        }
        return _stick.TryAdd(action, RepeatDelay);
    }

    public override void _Process(double delta)
    {
        if (!IsOpen || _view is null || _system is null || Sliding)
            return;
        foreach (var action in StickActions)
        {
            if (!_stick.TryGetValue(action, out var left))
                continue;
            left -= delta;
            if (left <= 0)
            {
                Repeat(action);
                left += RepeatEvery;
            }
            _stick[action] = left;
        }
    }

    /// <summary>The stick held: the cursor moves on, and on the System tab, an option or the name entry's key too.</summary>
    private void Repeat(string action)
    {
        var by = action is "move_forward" or "move_left" ? -1 : 1;
        var vertical = action is "move_forward" or "move_back";
        if (Tab == PauseTab.System)
        {
            if (vertical)
                _system!.Menu.MoveCursor(by);
            else
                _system!.Menu.Sideways(by);
            _system.QueueRedraw();
            return;
        }
        if (!vertical)
            return;
        if (_view!.Barter is { } barter)
            barter.MoveCursor(by);
        else if (_view.Training is { } training)
            training.MoveCursor(by);
        else
            _view.Menu.MoveCursor(by);
        _view.QueueRedraw();
    }
}
