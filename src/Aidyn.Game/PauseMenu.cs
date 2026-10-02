using Aidyn.Core;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The pause menu, which Start opens over the game (<c>BigAssMenu</c>). So far it's the Party tab
/// alone (<see cref="PartyTabView"/>), on a <see cref="UiScreen"/> over the world, and its barter
/// state, the loot window, which a container opens (<see cref="OpenLoot"/>).
/// </summary>
/// <remarks>
/// <para>
/// The controls follow the original's: C-left and C-right change the member, the stick's left and
/// right change the list and its up and down move the cursor, and C-down shows the details while
/// held. On a PC, Q and E or the shoulders change the member, the movement keys, arrows, stick or
/// D-pad do the rest, and Tab or the top face button shows the details. A, Space or the bottom face
/// button, uses the cursor's row or chooses in a popup. B, Backspace, Start or Escape closes a popup,
/// or the menu if none is open. The mouse moves the cursor, as in the editor.
/// </para>
/// <para>
/// Each opening starts the tab afresh, on the first member, the inventory and the top row, as the
/// original's does, so a player's presses to reach something are the same every time. The original
/// remembers only which tab was open, which waits for the other tabs.
/// </para>
/// <para>
/// In the loot window, the stick's left and right go to the list that way: the pile is left of the
/// party's list in the classic layout, as the original's is, and right of it in the wide one. C-left
/// and C-right do nothing there, as in the original. B or Start closes it, unless items are left
/// (<see cref="BarterMenu.Back"/>), and so does choosing to leave them.
/// </para>
/// </remarks>
public partial class PauseMenu : CanvasLayer
{
    /// <summary>Above the lens flare and the debug overlay.</summary>
    public const int MenuLayer = 10;

    /// <summary>
    /// How long the stick held up or down waits before the cursor moves on by itself, and how often it
    /// does then, in seconds: the port's choice, as a held key repeats.
    /// </summary>
    private const double RepeatDelay = 0.4, RepeatEvery = 0.1;

    private static readonly string[] CursorActions = ["move_forward", "move_back"];

    private readonly PluginSet _plugins;
    private readonly GameState _game;
    private readonly UiScreen _screen = new() { ForwardsMouse = true };
    /// <summary>The stick's directions held past the deadzone, and how long until each repeats.</summary>
    private readonly Dictionary<string, double> _stick = [];
    private PartyTabView? _view;

    public PauseMenu(PluginSet plugins, GameState game)
    {
        _plugins = plugins;
        _game = game;
        Name = "Pause menu";
        Layer = MenuLayer;
        Visible = false;
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);
    }

    public bool IsOpen => Visible;

    /// <summary>The menu closed.</summary>
    public event Action? Closed;

    /// <summary>The UI screen, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    /// <summary>Opens the menu afresh on the party as it is now.</summary>
    public void Open()
    {
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
        Start();
    }

    /// <summary>
    /// Opens the menu in its barter state on loot (<c>build_loot_menu</c>), whose gold the party already
    /// has. Taking and dropping act on the game; the pile is lost once it closes.
    /// </summary>
    public void OpenLoot(ItemPile loot, int gold)
    {
        Open();
        _view!.Barter = BarterMenu.ForLoot(_plugins, _game.Party, loot, gold, _game.Flags, _game.Random.World);
    }

    private void Start()
    {
        _view!.DetailsOpen = false;
        _view.ProcessMode = ProcessModeEnum.Inherit;
        _stick.Clear();
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
        _view?.ProcessMode = ProcessModeEnum.Disabled;
        Closed?.Invoke();
    }

    /// <summary>
    /// B, or Start: closes a popup, and false if none was open, so the caller closes the menu. The loot
    /// window asks before leaving items.
    /// </summary>
    public bool Back()
    {
        if (_view is null)
            return false;
        var held = _view.Barter is { } barter ? barter.Back() : _view.Menu.Back();
        _view.QueueRedraw();
        return held;
    }

    /// <summary>Carries out a press or release while the menu is open. True if the menu took it.</summary>
    public bool Handle(InputEvent e)
    {
        if (_view is null)
            return false;
        if (_view.Barter is { } barter)
            return HandleBarter(e, barter);
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

    // The barter state's presses: the stick's left and right reach the list on that side.
    private bool HandleBarter(InputEvent e, BarterMenu barter)
    {
        if (e.IsActionPressed("menu_details") && barter.CloseMessage())
        {
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

    private void Side(BarterMenu barter, bool right)
    {
        if (right == _view!.PileOnRight)
            barter.ShowPile();
        else
            barter.ShowParty();
    }

    /// <summary>
    /// Whether an event presses a movement action. A key repeats as it's held. The stick sends an event
    /// whenever it moves, each past the deadzone reading as pressed, so it counts only as it crosses
    /// the deadzone; held up or down, it repeats in <see cref="_Process"/>.
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
        if (!IsOpen || _view is null)
            return;
        foreach (var action in CursorActions)
        {
            if (!_stick.TryGetValue(action, out var left))
                continue;
            left -= delta;
            if (left <= 0)
            {
                var by = action == "move_forward" ? -1 : 1;
                if (_view.Barter is { } barter)
                    barter.MoveCursor(by);
                else
                    _view.Menu.MoveCursor(by);
                _view.QueueRedraw();
                left += RepeatEvery;
            }
            _stick[action] = left;
        }
    }
}
