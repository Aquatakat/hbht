using Aidyn.Core;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Game;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// The loot window on a roll, drawn by the runtime's view in the pause menu's barter state, against the
/// editor's game: the gold goes to its party as the window opens, as a container gives it, and what's
/// taken or dropped stays in the game, to save for the runtime. The loot table's page and a container's
/// on the map show it.
/// </summary>
public partial class LootWindowPanel : VBoxContainer
{
    private readonly UiScreen _screen = new()
    {
        CustomMinimumSize = new Vector2(UiScreen.Width, UiScreen.Height), Backdrop = new Color(0.22f, 0.25f, 0.3f), ForwardsMouse = true,
    };
    private readonly UiScreenOptions _options;
    private readonly PartyTabView _view;
    private readonly Label _state = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly HFlowContainer _bar = new();
    private readonly CheckBox _details = new() { Text = "Details (C-down)", TooltipText = "Show what C-down shows for the active row, as it does while held." };

    /// <param name="what">What was rolled, such as a loot table's name.</param>
    public LootWindowPanel(PluginSet plugins, EditorGame game, ItemPile loot, int gold, string what)
    {
        AddThemeConstantOverride("separation", 6);
        var party = game.Game.Party;
        party.Gold += (uint)gold;
        var barter = BarterMenu.ForLoot(plugins, party, loot, gold, game.Game.Flags, game.Game.Random.World);
        _view = new PartyTabView(plugins, barter.Party) { Barter = barter };
        _screen.Canvas.AddChild(_view);

        AddChild(new Label
        {
            Text = $"{what}, in the loot window against {(game.IsNewGame ? "a new game" : game.Name)}: its gold is the party's already, "
                + "and what's taken or dropped stays in the game. The stick's left and right reach the list on that side: the pile is "
                + "left of the party's in the classic layout and right of it in the wide one. A takes or drops, B leaves.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var bar = _bar;
        bar.AddThemeConstantOverride("h_separation", 6);
        bar.AddChild(Button("◀", "The stick left: the list on the left.", () => Side(right: false)));
        bar.AddChild(Button("▶", "The stick right: the list on the right.", () => Side(right: true)));
        bar.AddChild(Button("▲", "The stick up.", () => Barter.MoveCursor(-1)));
        bar.AddChild(Button("▼", "The stick down.", () => Barter.MoveCursor(1)));
        bar.AddChild(Button("A", "Take the pile's row, drop the party's, choose in a popup, or close a message.", Accept));
        bar.AddChild(Button("B", "Close a message, or leave the window, which asks first if items are left.", Back));
        _details.Toggled += on =>
        {
            if (!on || !Barter.CloseMessage())
                _view.DetailsOpen = on;
        };
        bar.AddChild(_details);
        AddChild(bar);
        AddChild(_options = new UiScreenOptions(_screen, "with room, the pile goes right of the party's list."));
        AddChild(_screen);
        AddChild(_state);
    }

    private BarterMenu Barter => _view.Barter!;

    /// <summary>Sets how the screen is shown (<c>--zoom</c>, <c>--ui-filter</c>, <c>--aspect</c>, <c>--ui-lines</c>).</summary>
    public void SetScreen(int? zoom, UiFilter? filter, bool? wide, int? lines) => _options.Set(zoom, filter, wide, lines);

    /// <summary>Presses A, B, the stick or C-down in turn (<c>--press</c>), for screenshots.</summary>
    public void Press(IEnumerable<string> buttons)
    {
        foreach (var button in buttons)
        {
            switch (button.Trim().ToLowerInvariant())
            {
                case "a": Accept(); break;
                case "b": Back(); break;
                case "up": Barter.MoveCursor(-1); break;
                case "down": Barter.MoveCursor(1); break;
                case "left": Side(right: false); break;
                case "right": Side(right: true); break;
                default: GD.PrintErr($"--press: \"{button}\" isn't a, b, up, down, left or right."); break;
            }
        }
        _view.QueueRedraw();
    }

    private void Side(bool right)
    {
        if (right == _view.PileOnRight)
            Barter.ShowPile();
        else
            Barter.ShowParty();
    }

    private void Accept()
    {
        Barter.Accept();
        if (Barter.Closed)
            Leave("The party left the rest.");
    }

    private void Back()
    {
        if (!Barter.Back())
            Leave("The window closed.");
    }

    // The original's window is gone once it closes; here it stays, to look at, with nothing more to do.
    private void Leave(string how)
    {
        _state.Text = $"{how} In the game, the menu closes and trek goes on. Roll again for another.";
        foreach (var button in _bar.GetChildren().OfType<BaseButton>())
            button.Disabled = true;
    }

    private Button Button(string text, string tooltip, Action act)
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += () =>
        {
            act();
            _view.QueueRedraw();
        };
        return button;
    }
}
