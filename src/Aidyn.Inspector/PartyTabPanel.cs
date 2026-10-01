using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Game;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// The pause menu's Party tab, drawn by the runtime's view with the editor's game's party: a loaded
/// checkpoint's or save's, or a new game's Alaron. It's the inventory record's preview, since the tab is where
/// the party's items show.
/// </summary>
public partial class PartyTabPanel : VBoxContainer
{
    private readonly UiScreen _screen = new()
    {
        CustomMinimumSize = new Vector2(UiScreen.Width, UiScreen.Height), Backdrop = new Color(0.22f, 0.25f, 0.3f), ForwardsMouse = true,
    };
    private readonly UiScreenOptions _options;
    private readonly PartyTabView _view;
    private readonly CheckBox _details = new() { Text = "Details (C-down)", TooltipText = "Show what C-down shows for the cursor's row, as it does while held." };

    public PartyTabPanel(PluginCatalog catalog, EditorGame? game, CharacterRules rules, ItemCatalog items)
    {
        AddThemeConstantOverride("separation", 6);
        var party = game?.Game.Party ?? EditorCharacters.CopyParty(catalog.Plugins, rules, null, items);
        _view = new PartyTabView(catalog.Plugins, PartyMenu.From(catalog.Plugins, party));
        _screen.Canvas.AddChild(_view);

        AddChild(new Label
        {
            Text = $"{(game is { IsNewGame: false } ? $"{game.Name}'s party" : "A new game's party")} in the pause menu's Party tab. "
                + "Hover a row or scroll to move the cursor. Load a checkpoint or a save on a flag page to show its party.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var bar = new HFlowContainer();
        bar.AddThemeConstantOverride("h_separation", 6);
        bar.AddChild(Button("◀ Member", "The member before (C-left).", () => _view.Menu.ChangeMember(-1)));
        bar.AddChild(Button("Member ▶", "The next member (C-right).", () => _view.Menu.ChangeMember(1)));
        bar.AddChild(Button("◀ List", "The list before (the stick left).", () => _view.Menu.ChangeList(-1)));
        bar.AddChild(Button("List ▶", "The next list (the stick right).", () => _view.Menu.ChangeList(1)));
        _details.Toggled += on => _view.DetailsOpen = on;
        bar.AddChild(_details);
        AddChild(bar);
        AddChild(_options = new UiScreenOptions(_screen, "on a wider one the tab stays at the centre, or with room, opens out."));
        AddChild(_screen);
    }

    /// <summary>Sets how the screen is shown (<c>--zoom</c>, <c>--ui-filter</c>, <c>--aspect</c>, <c>--ui-lines</c>).</summary>
    public void SetScreen(int? zoom, UiFilter? filter, bool? wide, int? lines) => _options.Set(zoom, filter, wide, lines);

    /// <summary>
    /// Shows a member, a list, a row and the details (<c>--member</c>, <c>--list</c>, <c>--row</c>,
    /// <c>--details</c>), for screenshots.
    /// </summary>
    public void Show(int? member, PartyList? list, int? row, bool? details)
    {
        var menu = _view.Menu;
        if (member is { } m)
        {
            for (var i = 0; i < Party.MaxSize && menu.Member != m; i++)
                menu.ChangeMember(1);
        }
        if (list is { } l)
            menu.ShowList(l);
        if (row is { } r)
            menu.Point(r);
        if (details is { } d)
            _details.ButtonPressed = d;
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
