using Aidyn.Core.Characters;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The pause menu, which Start opens over the game (<c>BigAssMenu</c>). So far it's the Party tab
/// alone (<see cref="PartyTabView"/>), read-only, on a <see cref="UiScreen"/> over the world.
/// </summary>
/// <remarks>
/// The controls follow the original's: C-left and C-right change the member, the stick's left and
/// right change the list and its up and down move the cursor, and C-down shows the details while
/// held. On a PC, Q and E or the shoulders change the member, the movement keys, arrows, stick or
/// D-pad do the rest, and Tab or the top face button shows the details. Start, Escape or B closes it.
/// The mouse moves the cursor, as in the editor.
/// </remarks>
public partial class PauseMenu : CanvasLayer
{
    /// <summary>Above the lens flare and the debug overlay.</summary>
    public const int MenuLayer = 10;

    private readonly PluginSet _plugins;
    private readonly Party _party;
    private readonly UiScreen _screen = new() { ForwardsMouse = true };
    private PartyTabView? _view;

    public PauseMenu(PluginSet plugins, Party party)
    {
        _plugins = plugins;
        _party = party;
        Name = "Pause menu";
        Layer = MenuLayer;
        Visible = false;
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);
    }

    public bool IsOpen => Visible;

    /// <summary>The UI screen, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    /// <summary>Opens the menu on the party as it is now.</summary>
    public void Open()
    {
        if (_view is null)
        {
            _view = new PartyTabView(_plugins, PartyMenu.From(_plugins, _party));
            _screen.Canvas.AddChild(_view);
        }
        else
        {
            _view.Menu.Refresh();
        }
        _view.DetailsOpen = false;
        _view.ProcessMode = ProcessModeEnum.Inherit;
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
        _view?.ProcessMode = ProcessModeEnum.Disabled;
    }

    /// <summary>Carries out a press or release while the menu is open. True if the menu took it.</summary>
    public bool Handle(InputEvent e)
    {
        if (_view is null)
            return false;
        var menu = _view.Menu;
        if (e.IsActionPressed("menu_details") || e.IsActionReleased("menu_details"))
            _view.DetailsOpen = e.IsPressed();
        else if (e.IsActionPressed("menu_member_previous"))
            menu.ChangeMember(-1);
        else if (e.IsActionPressed("menu_member_next"))
            menu.ChangeMember(1);
        else if (e.IsActionPressed("move_left") || e.IsActionPressed("menu_left"))
            menu.ChangeList(-1);
        else if (e.IsActionPressed("move_right") || e.IsActionPressed("menu_right"))
            menu.ChangeList(1);
        else if (e.IsActionPressed("move_forward", allowEcho: true) || e.IsActionPressed("menu_up", allowEcho: true))
            menu.MoveCursor(-1);
        else if (e.IsActionPressed("move_back", allowEcho: true) || e.IsActionPressed("menu_down", allowEcho: true))
            menu.MoveCursor(1);
        else
            return false;
        _view.QueueRedraw();
        return true;
    }
}
