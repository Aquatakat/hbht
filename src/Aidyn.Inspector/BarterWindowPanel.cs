using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins;

namespace Aidyn.Inspector;

/// <summary>
/// The pause menu's barter state, drawn by the runtime's view against the editor's game: the loot window
/// on a roll, or a shop's window. What's taken, bought, dropped or sold stays in the game, to save for
/// the runtime. The loot table's page and a container's on the map show the loot window, and a shop's
/// page its window.
/// </summary>
public partial class BarterWindowPanel : MenuWindowPanel
{
    private readonly BarterMenu _barter;

    /// <param name="intro">What's open, and against which game.</param>
    public BarterWindowPanel(PluginSet plugins, BarterMenu barter, string intro) : base(plugins, barter)
    {
        _barter = barter;
        var shop = barter.Shop is not null;
        Describe(intro + " The stick's left and right reach the list on that side: the pile is always left of the party's, "
            + "as in the original. "
            + (shop ? "A buys one or sells, C-up appraises the stock's price, B leaves." : "A takes or drops, B leaves."));
        AddButton("◀", "The stick left: the list on the left.", () => Press(MenuAction.Left));
        AddButton("▶", "The stick right: the list on the right.", () => Press(MenuAction.Right));
        AddButton("▲", "The stick up.", () => Press(MenuAction.Up));
        AddButton("▼", "The stick down.", () => Press(MenuAction.Down));
        if (shop)
            AddButton("A", "Buy one of the stock's row, sell the party's, choose in a popup, or close a message.", Accept);
        else
            AddButton("A", "Take the pile's row, drop the party's, choose in a popup, or close a message.", Accept);
        if (shop)
            AddButton("B", "Close a message, or leave the shop.", Back);
        else
            AddButton("B", "Close a message, or leave the window, which asks first if items are left.", Back);
        if (shop)
            AddButton("C-up", "Appraise the stock's row: how the party judges its price.", () => Press(MenuAction.Appraise));
        AddDetails("Show what C-down shows for the active row, as it does while held.");
        Presses(("a", Accept), ("b", Back), ("cup", () => Press(MenuAction.Appraise)), ("up", () => Press(MenuAction.Up)), ("down", () => Press(MenuAction.Down)),
            ("left", () => Press(MenuAction.Left)), ("right", () => Press(MenuAction.Right)), ("details", ToggleDetails));
        Finish("with room, the pile goes right of the party's list.", closes: true);
    }

    /// <summary>
    /// The loot window on a roll, whose gold goes to the game's party as the window opens, as a container
    /// gives it.
    /// </summary>
    /// <param name="what">What was rolled, such as a loot table's name.</param>
    public static BarterWindowPanel ForLoot(PluginSet plugins, EditorGame game, ItemPile loot, int gold, string what)
    {
        var party = game.Game.Party;
        party.Gold += (uint)gold;
        var barter = BarterMenu.ForLoot(plugins, party, loot, gold, game.Game.Flags, game.Game.Random.World);
        return new BarterWindowPanel(plugins, barter,
            $"{what}, in the loot window against {Against(game)}: its gold is the party's already, and what's taken or dropped stays in the game.");
    }

    /// <summary>A shop's window on the game's party, which opening it costs the barterer's Stamina.</summary>
    public static BarterWindowPanel ForShop(PluginSet plugins, EditorGame game, Shop shop) =>
        new(plugins, BarterMenu.ForShop(plugins, shop, game.Game.Random.World),
            $"The shop's window against {Against(game)}: what's bought or sold stays in the game.");

    private static string Against(EditorGame game) => game.IsNewGame ? "a new game" : game.Name;

    private void Accept()
    {
        Press(MenuAction.Accept);
        if (_barter.Closed)
            Leave(Closed("The party left the rest."));
    }

    private void Back()
    {
        if (!_barter.Back())
            Leave(Closed("The window closed."));
    }

    private string Closed(string how) => $"{how} In the game, the menu closes and trek goes on. "
        + (_barter.Shop is null ? "Roll again for another." : "Open the shop again for another.");
}
