using Aidyn.Core.Characters;
using CharacterParty = Aidyn.Core.Characters.Party;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class MenuListTests
{
    private static MenuList<int> List(int rows, int visible = int.MaxValue) =>
        new([.. Enumerable.Range(0, rows)]) { Visible = visible };

    /// <summary>Moving past the last row goes to the first, and back from the first goes to the last.</summary>
    [Fact]
    public void TheCursorWrapsFromTheLastRowToTheFirstAndBack()
    {
        var list = List(5);

        list.Move(-1);
        Assert.Equal(4, list.Cursor);
        list.Move(1);
        Assert.Equal(0, list.Cursor);
        list.Move(7);
        Assert.Equal(2, list.Cursor);
        list.Move(-3);
        Assert.Equal(4, list.Cursor);
    }

    /// <summary>The list scrolls just far enough to keep the cursor in view going down.</summary>
    [Fact]
    public void MovingDownPastTheLastVisibleRowScrollsByOne()
    {
        var list = List(12, visible: 8);

        list.Move(7);
        Assert.Equal((7, 0), (list.Cursor, list.First));
        list.Move(1);
        Assert.Equal((8, 1), (list.Cursor, list.First));
        list.Move(1);
        Assert.Equal((9, 2), (list.Cursor, list.First));
    }

    /// <summary>Moving up from the top wraps to the last row, with the last page showing.</summary>
    [Fact]
    public void MovingUpFromTheTopWrapsToTheLastRowAndTheLastPage()
    {
        var list = List(12, visible: 8);

        list.Move(-1);

        Assert.Equal((11, 4), (list.Cursor, list.First));
        list.Move(1);
        Assert.Equal((0, 0), (list.Cursor, list.First));
    }

    /// <summary>Moving up above the first visible row scrolls back by one.</summary>
    [Fact]
    public void MovingUpAboveTheFirstVisibleRowScrollsBack()
    {
        var list = List(12, visible: 8);
        list.Point(11);

        list.Move(-7);
        Assert.Equal((4, 4), (list.Cursor, list.First));
        list.Move(-1);
        Assert.Equal((3, 3), (list.Cursor, list.First));
    }

    /// <summary>The arrows show while rows are hidden above or below.</summary>
    [Fact]
    public void MoreAboveAndMoreBelowSayWhichWaysRowsAreHidden()
    {
        var list = List(12, visible: 8);

        Assert.False(list.MoreAbove);
        Assert.True(list.MoreBelow);
        list.Point(11);
        Assert.True(list.MoreAbove);
        Assert.False(list.MoreBelow);
        list.Point(2);
        Assert.True(list.MoreAbove);
        Assert.True(list.MoreBelow);
        Assert.False(List(8, visible: 8).MoreBelow);
        Assert.False(List(3).MoreAbove);
        Assert.False(List(3).MoreBelow);
    }

    /// <summary>Pointing puts the cursor on a row, held to the rows there are, and scrolls to it.</summary>
    [Fact]
    public void PointPutsTheCursorOnARowHeldToTheListsEnds()
    {
        var list = List(12, visible: 8);

        list.Point(9);
        Assert.Equal((9, 2), (list.Cursor, list.First));
        list.Point(40);
        Assert.Equal((11, 4), (list.Cursor, list.First));
        list.Point(-3);
        Assert.Equal((0, 0), (list.Cursor, list.First));
    }

    /// <summary>Replacing with fewer rows puts a cursor past the end on the last row.</summary>
    [Fact]
    public void ReplacingWithFewerRowsClampsTheCursorToTheLastRow()
    {
        var list = List(12, visible: 8);
        list.Point(10);

        list.Replace([.. Enumerable.Range(0, 5)], resetPastEnd: false);

        Assert.Equal((4, 0), (list.Cursor, list.First));
        Assert.Equal(4, list.Current);
    }

    /// <summary>Replacing with fewer rows and resetting past the end puts the cursor and scroll back at the top.</summary>
    [Fact]
    public void ReplacingWithFewerRowsCanResetTheCursorAndScrollToTheTop()
    {
        var list = List(12, visible: 8);
        list.Point(10);

        list.Replace([.. Enumerable.Range(0, 5)], resetPastEnd: true);

        Assert.Equal((0, 0), (list.Cursor, list.First));
    }

    /// <summary>A cursor still among the new rows stays where it was, whether or not past the end resets.</summary>
    [Fact]
    public void ReplacingKeepsACursorThatIsStillAmongTheRows()
    {
        var list = List(12, visible: 8);
        list.Point(3);

        list.Replace([.. Enumerable.Range(0, 9)], resetPastEnd: true);

        Assert.Equal(3, list.Cursor);
        list.Replace([.. Enumerable.Range(0, 9)], resetPastEnd: false);
        Assert.Equal(3, list.Cursor);
    }

    /// <summary>An empty list has no current row and ignores the cursor's moves.</summary>
    [Fact]
    public void AnEmptyListHasNoCurrentRowAndDoesNotMove()
    {
        var list = List(0, visible: 8);

        list.Move(1);
        list.Point(3);

        Assert.Equal(0, list.Cursor);
        Assert.Equal(0, list.Current);
        Assert.False(list.MoreAbove);
        Assert.False(list.MoreBelow);
    }

    /// <summary>Showing more rows at once scrolls back up so the page is full.</summary>
    [Fact]
    public void ShowingMoreRowsScrollsBackSoThePageIsFull()
    {
        var list = List(12, visible: 4);
        list.Point(11);
        Assert.Equal(8, list.First);

        list.Visible = 8;

        Assert.Equal(4, list.First);
    }
}

public class InventoryOrderTests
{
    private static Item Named(int n, ItemKind kind, string name) => Item.From(ItemData.NewItem(n, kind) with { Name = name });

    /// <summary>Items come in the order of the kinds list, whatever order they're given in, with potions first and misc last.</summary>
    [Fact]
    public void KindsComeInTheListsOrder()
    {
        var kinds = InventoryOrder.Kinds;
        Assert.Equal(ItemKind.Potion, kinds[0]);
        Assert.Equal(ItemKind.Weapon, kinds[1]);
        Assert.Equal(ItemKind.Wand, kinds[2]);
        Assert.Equal(ItemKind.Scroll, kinds[3]);
        Assert.Equal(ItemKind.Misc, kinds[^1]);

        var items = kinds.Select((k, i) => Named(i, k, "A")).ToList();
        var shuffled = items.OrderBy(i => (int)i.Kind * 7919 % 31).ToList();
        shuffled.Sort(InventoryOrder.Compare);

        Assert.Equal(kinds, shuffled.Select(i => i.Kind));
    }

    /// <summary>Potions come before weapons, weapons before wands, wands before scrolls, and misc last.</summary>
    [Fact]
    public void PotionsPrecedeWeaponsWhichPrecedeWandsWhichPrecedeScrollsAndMiscIsLast()
    {
        Assert.True(InventoryOrder.Compare(Named(1, ItemKind.Potion, "Z"), Named(2, ItemKind.Weapon, "A")) < 0);
        Assert.True(InventoryOrder.Compare(Named(1, ItemKind.Weapon, "Z"), Named(2, ItemKind.Wand, "A")) < 0);
        Assert.True(InventoryOrder.Compare(Named(1, ItemKind.Wand, "Z"), Named(2, ItemKind.Scroll, "A")) < 0);
        Assert.True(InventoryOrder.Compare(Named(1, ItemKind.Key, "Z"), Named(2, ItemKind.Misc, "A")) < 0);
        Assert.True(InventoryOrder.Compare(Named(1, ItemKind.Misc, "A"), Named(2, ItemKind.Potion, "Z")) > 0);
    }

    /// <summary>Within a kind, names compare ordinally, so capitals come before lower case.</summary>
    [Fact]
    public void WithinAKindNamesAreInByteOrder()
    {
        Assert.True(InventoryOrder.Compare(Named(1, ItemKind.Weapon, "Apple"), Named(2, ItemKind.Weapon, "Zebra")) < 0);
        Assert.True(InventoryOrder.Compare(Named(1, ItemKind.Weapon, "Zebra"), Named(2, ItemKind.Weapon, "apple")) < 0);
        Assert.True(InventoryOrder.Compare(Named(1, ItemKind.Weapon, "apple"), Named(2, ItemKind.Weapon, "Zebra")) > 0);
        Assert.Equal(0, InventoryOrder.Compare(Named(1, ItemKind.Weapon, "Same"), Named(2, ItemKind.Weapon, "Same")));
    }
}

public class PartyMenuPulseTests
{
    private static PartyMenu Menu()
    {
        var party = new CharacterParty();
        Assert.True(party.Add(ItemData.Alaron()));
        return new PartyMenu(party, new MenuText([]), _ => null, _ => null);
    }

    private static void Frames(PartyMenu menu, int frames)
    {
        for (var tick = 0; tick < frames * Cadence.FrameTicks; tick++)
            menu.Step();
    }

    /// <summary>The cursor's colour starts at its low colour and moves only when a whole frame has passed.</summary>
    [Fact]
    public void ThePulseStartsAtItsLowColourAndMovesOnlyOnAFrame()
    {
        var menu = Menu();
        Assert.Equal(PartyMenu.HighlightFrom, menu.HighlightNow);

        for (var tick = 0; tick < Cadence.FrameTicks - 1; tick++)
            menu.Step();
        Assert.Equal(PartyMenu.HighlightFrom, menu.HighlightNow);
        menu.Step();
        Assert.Equal(new Rgba(72, 46, 41, 255), menu.HighlightNow);
    }

    /// <summary>After 19 frames each channel has gone 19 whole steps of a 20th of the way, rounded toward zero.</summary>
    [Fact]
    public void After19FramesEachChannelHasMovedNineteenWholeSteps()
    {
        var menu = Menu();

        Frames(menu, 19);

        // R 68 + 4 * 19, G 42 + 4 * 19, B 34 + 7 * 19: (83, 99 and 157) / 20 rounded toward zero.
        Assert.Equal(new Rgba(144, 118, 167, 255), menu.HighlightNow);
    }

    /// <summary>The pulse turns at the top and goes back down to its low colour, then up again.</summary>
    [Fact]
    public void ThePulseGoesBackDownAfterReachingItsTop()
    {
        var menu = Menu();

        Frames(menu, 20);
        Assert.Equal(new Rgba(140, 114, 160, 255), menu.HighlightNow);
        Frames(menu, 18);
        Assert.Equal(PartyMenu.HighlightFrom, menu.HighlightNow);
        Frames(menu, 1);
        Assert.Equal(new Rgba(72, 46, 41, 255), menu.HighlightNow);
    }
}
