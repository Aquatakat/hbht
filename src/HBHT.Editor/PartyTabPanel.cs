using HBHT.Core.Characters;
using HBHT.Core.Items;
using HBHT.Core.Ui;
using HBHT.Plugins;
using Godot;

namespace HBHT.Editor;

/// <summary>
/// The pause menu's Party tab, drawn by the runtime's view with the editor's game's party: a loaded
/// checkpoint's or save's, or a new game's Alaron. It's the inventory record's preview, since the tab is where
/// the party's items show. A and B act on the editor's game, so what's equipped, drunk, read or crafted
/// stays in it, to save for the runtime.
/// </summary>
public partial class PartyTabPanel : MenuWindowPanel
{
    private readonly PartyMenu _menu;

    public PartyTabPanel(PluginCatalog catalog, EditorGame? game, CharacterRules rules, ItemCatalog items)
        : this(catalog, game, PartyMenu.From(catalog.Plugins,
            game?.Game.Party ?? EditorCharacters.CopyParty(catalog.Plugins, rules, null, items), random: game?.Game.Random.World))
    {
    }

    private PartyTabPanel(PluginCatalog catalog, EditorGame? game, PartyMenu menu) : base(catalog.Plugins, menu)
    {
        _menu = menu;
        Describe($"{(game is { IsNewGame: false } ? $"{game.Name}'s party" : "A new game's party")} in the pause menu's Party tab. "
            + "Hover a row or scroll to move the cursor, and A to use it: equip, take off, drink, read, or a skill's popup. "
            + "Load a checkpoint or a save on a flag page to show its party.");
        AddButton("◀ Member", "C-left: the next member.", () => Press(MenuAction.MemberLeft));
        AddButton("Member ▶", "C-right: the member before.", () => Press(MenuAction.MemberRight));
        AddButton("◀ List", "The stick left: the next list.", () => Press(MenuAction.Left));
        AddButton("List ▶", "The stick right: the list before.", () => Press(MenuAction.Right));
        AddButton("A", "Use the cursor's row, choose in a popup, or close a message.", () => Press(MenuAction.Accept));
        AddButton("B", "Close the popup or the message.", () => _menu.Back());
        AddDetails("Show what C-down shows for the cursor's row, as it does while held.");
        Presses(("a", () => Press(MenuAction.Accept)), ("b", () => _menu.Back()), ("up", () => Press(MenuAction.Up)), ("down", () => Press(MenuAction.Down)),
            ("left", () => Press(MenuAction.Left)), ("right", () => Press(MenuAction.Right)), ("cleft", () => Press(MenuAction.MemberLeft)),
            ("cright", () => Press(MenuAction.MemberRight)), ("details", ToggleDetails));
        Finish("on a wider one the tab stays at the centre, or with room, opens out.", closes: false);
    }

    /// <summary>
    /// Shows a member, a list, a row and the details (<c>--member</c>, <c>--list</c>, <c>--row</c>,
    /// <c>--details</c>), for screenshots.
    /// </summary>
    public void Show(int? member, PartyList? list, int? row, bool? details)
    {
        if (member is { } m)
        {
            for (var i = 0; i < Party.MaxSize && _menu.Member != m; i++)
                _menu.ChangeMember(1);
        }
        if (list is { } l)
            _menu.ShowList(l);
        if (row is { } r)
            _menu.Point(r);
        if (details is { } d)
            DetailsBox!.ButtonPressed = d;
    }
}
