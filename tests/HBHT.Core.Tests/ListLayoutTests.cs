using HBHT.Core.Characters;
using HBHT.Core.Items;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using CharacterParty = HBHT.Core.Characters.Party;

namespace HBHT.Core.Tests;

/// <summary>
/// The menus' list layouts (<see cref="ListLayout"/>): the original's fixed bounds and row pitch give how many rows each list
/// shows, the menus build their lists with that, and so they scroll and are hit with no view drawn.
/// </summary>
public class ListLayoutTests(BarterPlugins fixture) : IClassFixture<BarterPlugins>
{
    private static readonly MenuText Text = new([.. Enumerable.Range(0, 600).Select(i => $"{i}|%s|%s|%d")]);

    private static PartyMenu Menu(CharacterParty party) => new(party, Text, _ => null, _ => null) { Random = new ScriptedRandom() };

    /// <summary>A party carrying twelve kinds of item: more rows than the inventory shows.</summary>
    private static PartyMenu FullInventory()
    {
        var more = Enumerable.Range(100, 12).Select(n => ItemData.NewItem(n)).ToArray();
        var party = TradeData.PartyOf(TradeData.Catalog(20, more), null, TradeData.Member());
        foreach (var item in more)
            party.Inventory!.Add(item.Id);
        return Menu(party);
    }

    /// <summary>The lists are 104 tall: eight rows of 13, and the stats nine of 11.</summary>
    [Fact]
    public void TheTabsListsShowTheRowsTheirBoundsHold()
    {
        var menu = FullInventory();

        Assert.Equal(8, menu.Inventory.Visible);
        Assert.Equal(9, menu.Stats.Visible);
        Assert.Equal(8, menu.Skills.Visible);
        Assert.Equal(8, menu.Spells.Visible);
        Assert.Equal((8, 13), (PartyMenu.Layout(PartyList.Skills).Visible, PartyMenu.Layout(PartyList.Skills).RowHeight));
        Assert.Equal((9, 11), (PartyMenu.Layout(PartyList.Stats).Visible, PartyMenu.Layout(PartyList.Stats).RowHeight));
    }

    /// <summary>With no view drawn, a long list scrolls to keep the cursor in sight and wraps to the last page going up.</summary>
    [Fact]
    public void AListScrollsHeadlessNowItsVisibleCountIsKnownInTheCore()
    {
        var menu = FullInventory();
        var rows = menu.Inventory.Rows.Count;
        Assert.Equal(12, rows);
        Assert.False(menu.Inventory.MoreAbove);
        Assert.True(menu.Inventory.MoreBelow);

        for (var i = 0; i < 8; i++)
            menu.MoveCursor(1);
        Assert.Equal((8, 1), (menu.Inventory.Cursor, menu.Inventory.First));
        Assert.True(menu.Inventory.MoreAbove);

        menu.MoveCursor(-9);
        Assert.Equal((rows - 1, rows - 8), (menu.Inventory.Cursor, menu.Inventory.First));
        Assert.False(menu.Inventory.MoreBelow);
    }

    /// <summary>The pile's and the trainer's lists are 78 tall: six rows of 13.</summary>
    [Fact]
    public void ThePileAndTheTrainersListShowSixRows()
    {
        var more = Enumerable.Range(100, 8).Select(n => ItemData.NewItem(n)).ToArray();
        var catalog = TradeData.Catalog(8, more);
        var flags = ItemData.Flags();
        var party = TradeData.PartyOf(catalog, flags, TradeData.Member());
        var loot = new ItemPile(catalog);
        foreach (var item in more)
            loot.Add(item.Id, 1);
        var barter = BarterMenu.ForLoot(fixture.Plugins, party, loot, 0, flags);

        Assert.Equal(6, barter.Pile.Visible);
        Assert.Equal(6, BarterMenu.PileLayout.Visible);
        Assert.Equal(6, TrainingMenu.Layout.Visible);
        Assert.Equal(8, barter.Party.Inventory.Visible);

        barter.MoveCursor(5);
        Assert.Equal((5, 0), (barter.Pile.Cursor, barter.Pile.First));
        barter.MoveCursor(1);
        Assert.Equal((6, 1), (barter.Pile.Cursor, barter.Pile.First));
    }

    /// <summary>The crafting popup's list is 122 tall for the Alchemist and 117 for the rest, in 11s, or 28s for members.</summary>
    [Theory]
    [InlineData(CraftingTask.Alchemy, 11, 11)]
    [InlineData(CraftingTask.Armor, 10, 11)]
    [InlineData(CraftingTask.HealerTasks, 10, 11)]
    [InlineData(CraftingTask.LayOnHands, 4, 28)]
    [InlineData(CraftingTask.Restore, 4, 28)]
    [InlineData(CraftingTask.RestoreStat, 10, 11)]
    public void ACraftingPopupShowsTheRowsItsBoundsHold(CraftingTask task, int visible, int pitch)
    {
        var crafting = new CraftingMenu(task, "Title", [new CraftingRow("Row")], user: 0);

        Assert.Equal((visible, pitch), (crafting.Rows.Visible, crafting.Layout.RowHeight));
        Assert.Equal(CraftingMenu.ListLeft, crafting.Layout.Bounds.Left);
        Assert.Equal(CraftingMenu.TitleTop + CraftingMenu.TextRow, crafting.Layout.Bounds.Top);
    }

    /// <summary>A point in the bounds is the row it falls in, counted from the first in view, and nothing past the rows or outside.</summary>
    [Fact]
    public void AMousePointIsTheRowUnderItCountedFromTheFirstInView()
    {
        var layout = PartyMenu.Layout(PartyList.Inventory);
        var list = layout.List(Enumerable.Range(0, 12).ToList());
        var bounds = layout.Bounds;

        Assert.Equal(0, layout.RowAt(list, bounds.Left, bounds.Top));
        Assert.Equal(1, layout.RowAt(list, bounds.Left + 5, bounds.Top + 13));
        Assert.Equal(7, layout.RowAt(list, bounds.Right - 1, bounds.Bottom - 1));
        Assert.Null(layout.RowAt(list, bounds.Left - 1, bounds.Top));
        Assert.Null(layout.RowAt(list, bounds.Right, bounds.Top));
        Assert.Null(layout.RowAt(list, bounds.Left, bounds.Bottom));

        list.Point(11);
        Assert.Equal(4, list.First);
        Assert.Equal(4, layout.RowAt(list, bounds.Left, bounds.Top));
        Assert.Equal(11, layout.RowAt(list, bounds.Left, bounds.Top + 7 * 13));

        var short_ = layout.List(Enumerable.Range(0, 3).ToList());
        Assert.Null(layout.RowAt(short_, bounds.Left, bounds.Top + 3 * 13));
    }

    /// <summary>The arrows are 15 in from each side and 13 above the top, and a list slid across takes them with it.</summary>
    [Fact]
    public void TheArrowsSitInFromTheSidesAndAboveTheTopAndMoveWithTheList()
    {
        var layout = PartyMenu.Layout(PartyList.Inventory);

        Assert.Equal((175, 88), layout.UpArrow);
        Assert.Equal((285, 88), layout.DownArrow);

        var slid = layout.Shifted(-20);
        Assert.Equal(new ScreenRect(140, 101, 280, 205), slid.Bounds);
        Assert.Equal((155, 88), slid.UpArrow);
        Assert.Equal(layout.RowHeight, slid.RowHeight);
    }
}
