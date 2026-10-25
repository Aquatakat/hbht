using HBHT.Core.Characters;
using CharacterParty = HBHT.Core.Characters.Party;
using HBHT.Core.Items;
using HBHT.Core.Ui;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

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
        var party = ItemData.NewParty();
        Assert.True(party.Add(ItemData.Alaron()));
        return new PartyMenu(party, new MenuText([]), _ => null, _ => null);
    }

    private static void Frames(PartyMenu menu, int frames)
    {
        for (var tick = 0; tick < frames * Cadence.MenuFrameTicks; tick++)
            menu.Step();
    }

    /// <summary>The cursor's colour starts at its low colour and moves two steps a frame, as the original's lists step it twice.</summary>
    [Fact]
    public void ThePulseStartsAtItsLowColourAndMovesTwoStepsAFrame()
    {
        var menu = Menu();
        Assert.Equal(PartyMenu.HighlightFrom, menu.HighlightNow);

        Frames(menu, 1);

        // R 68 + 4 * 2, G 42 + 4 * 2, B 34 + 7 * 2: (83, 99 and 157) / 20 rounded toward zero, two steps.
        Assert.Equal(new Rgba(76, 50, 48, 255), menu.HighlightNow);
    }

    /// <summary>
    /// The pulse shows only even steps: it peaks at 18, as the frame's second step takes 19 back down, and comes back to
    /// its low colour 19 frames after it left.
    /// </summary>
    [Fact]
    public void ThePulsePeaksAtStep18AndTakes19FramesACycle()
    {
        var menu = Menu();

        Frames(menu, 9);
        Assert.Equal(new Rgba(140, 114, 160, 255), menu.HighlightNow);
        Frames(menu, 1);
        Assert.Equal(new Rgba(140, 114, 160, 255), menu.HighlightNow);
        Frames(menu, 9);
        Assert.Equal(PartyMenu.HighlightFrom, menu.HighlightNow);
        Frames(menu, 1);
        Assert.Equal(new Rgba(76, 50, 48, 255), menu.HighlightNow);
    }
}

public class PartyMenuMotionTests
{
    private static PartyMenu Menu(int members = 3)
    {
        var party = ItemData.NewParty();
        for (var n = 0; n < members; n++)
            Assert.True(party.Add(PotionData.Member($"Member {n}", n)));
        return new PartyMenu(party, new MenuText([]), _ => null, _ => null);
    }

    private static void Frames(PartyMenu menu, int frames)
    {
        for (var tick = 0; tick < frames * Cadence.MenuFrameTicks; tick++)
            menu.Step();
    }

    /// <summary>The menu's curve eases in and out: none of the way at first, half at the middle, all of it at the end.</summary>
    [Fact]
    public void TheTweensCurveStartsAndEndsSlowly()
    {
        Assert.Equal(0, MenuTween.Ease(0, 20), 5);
        Assert.Equal(0.5f, MenuTween.Ease(10, 20), 5);
        Assert.Equal(1, MenuTween.Ease(20, 20), 5);
        Assert.True(MenuTween.Ease(1, 20) < 0.01f);
        // Truncated each step, as the original's shorts and bytes are.
        Assert.Equal(228, MenuTween.Value(160, 300, MenuTween.Ease(10, 20) - 0.01f));
    }

    /// <summary>A member change moves the header over 15 frames, and holds off another until the tween has finished, a frame later.</summary>
    [Fact]
    public void AMemberChangeTakesFifteenFramesAndHoldsOffAnother()
    {
        var menu = Menu();
        var first = menu.Shown;

        menu.ChangeMember(1);
        Assert.Same(first, menu.Previous);
        Assert.Equal([0, 1, 2], menu.PreviousOrder);
        Assert.Equal([1, 2, 0], menu.PortraitOrder);
        Assert.Equal(0, menu.MemberChange);

        Frames(menu, PartyMenu.MemberFrames);
        Assert.Equal(1, menu.MemberChange, 5);
        menu.ChangeMember(1);
        Assert.Equal(1, menu.Member);

        Frames(menu, 1);
        Assert.Null(menu.Previous);
        menu.ChangeMember(1);
        Assert.Equal(2, menu.Member);
    }

    /// <summary>The next list slides the lists right and the one before slides them left, and no other change starts while they slide.</summary>
    [Fact]
    public void TheNextListSlidesRightAndHoldsOffChanges()
    {
        var menu = Menu();

        menu.ChangeList(1);
        Assert.Equal(PartyList.Spells, menu.List);
        Assert.Equal((PartyList.Inventory, 1), (menu.Slide!.From, menu.Slide.Direction));
        menu.ChangeList(1);
        menu.ChangeMember(1);
        Assert.Equal((PartyList.Spells, 0), (menu.List, menu.Member));

        Frames(menu, PartyMenu.ListFrames + 1);
        Assert.Null(menu.Slide);
        menu.ChangeList(-1);
        Assert.Equal((PartyList.Spells, -1), (menu.Slide!.From, menu.Slide.Direction));
        Assert.Equal(PartyList.Inventory, menu.List);
    }

    /// <summary>The list can change while the header does, as the original's guard on lists looks only at the lists.</summary>
    [Fact]
    public void TheListCanChangeWhileTheMemberDoes()
    {
        var menu = Menu();

        menu.ChangeMember(1);
        menu.ChangeList(1);

        Assert.Equal(PartyList.Spells, menu.List);
    }

    /// <summary>
    /// Both tweens take a step each frame, go the eased share of the way and finish a frame after they reach the end:
    /// the member change's header and the list slide, frame by frame.
    /// </summary>
    [Fact]
    public void TheTweensStepTheirEasedShareEachFrameAndFinishOneFrameLater()
    {
        var menu = Menu();
        menu.ChangeMember(1);
        menu.ChangeList(1);
        for (var frame = 0; frame <= PartyMenu.ListFrames + 2; frame++)
        {
            var member = frame <= PartyMenu.MemberFrames;
            var list = frame <= PartyMenu.ListFrames;
            Assert.Equal((member, list), (menu.Previous is not null, menu.Slide is not null));
            Assert.Equal(member ? MenuTween.Ease(frame, PartyMenu.MemberFrames) : 1, menu.MemberChange);
            Assert.Equal(list ? MenuTween.Ease(frame, PartyMenu.ListFrames) : 1, menu.ListChange);
            Assert.Equal(member || list, menu.Changing);
            Frames(menu, 1);
        }
    }
}
