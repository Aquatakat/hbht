using Aidyn.Core.Ui;

namespace Aidyn.Core.Tests;

/// <summary>
/// Combat's held menus laid out as 1.0's <c>ChoicePopup</c> lays them (<c>CreateList</c>, <c>WidgetPulseMenu.Step</c>, <c>Layout</c>,
/// <c>ScrollArrows.Step</c>), checked against the user's captures of Alaron's turn in the rats' battle: <c>roms/combat-cdown.png</c>
/// (Perform, three spells with their schools' icons and four <c>vs.</c> spells without, the last below the list) and
/// <c>roms/combat-cup.png</c> (four potions and Whitefire).
/// </summary>
public sealed class CombatMenuTests
{
    private static CombatMenu Menu(bool items, int rows) =>
        new(items, [.. Enumerable.Range(0, rows).Select(i => new CombatMenuRow(CombatMenuChoice.Spell, $"row {i}", null, CombatMenu.SpellColor))], null);

    /// <summary>Lays the menu out and steps it a tick, as the battle and its view do, so many times.</summary>
    private static CombatMenuLayout Run(CombatMenu menu, IReadOnlyList<int> heights, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            menu.Layout(heights);
            menu.Tick();
        }
        return menu.Layout(heights);
    }

    [Fact]
    public void CDownsRowsAreFourteenApartAndTheEighthScrolls()
    {
        // Main-font text (11 high) after the lute's 10: the text at the row's top, each row 11 high and 3 under it.
        var menu = Menu(items: false, rows: 8);
        var height = menu.RowHeight(iconHeight: 10, textHeight: 11);
        Assert.Equal((11, 0), (height, menu.TextTop(11)));
        var heights = Enumerable.Repeat(height, 8).ToList();
        var layout = menu.Layout(heights);

        // 8 × 14 = 112 is over 95, so the list is 95 high from y 73 (the capture's rows' text tops are 73, 87, …, 157) and the eighth
        // row, at 171, is below its clip.
        Assert.Equal(new ScreenRect(99, 73, 219, 168), layout.List);
        Assert.Equal([0, 14, 28, 42, 56, 70, 84, 98], layout.RowTops);
        Assert.Equal(new ScreenRect(100, 73, 221, 168), layout.Panel);
        Assert.Equal(new ScreenRect(99, 73, 222, 169), layout.Clip);

        // The down arrow fades in from 0 as the menu opens, 20 a tick, to 255 on the thirteenth.
        Assert.Equal((0, 0), (layout.UpArrow, layout.DownArrow));
        Assert.Equal((0, 240), (Run(menu, heights, 12).UpArrow, menu.DownArrowAlpha));
        Assert.Equal((0, 255), (Run(menu, heights, 1).UpArrow, menu.DownArrowAlpha));
    }

    [Fact]
    public void CUpsRowsAreNineteenApartAndFiveFillTheList()
    {
        // The flask's 16 square: text 3 down (half the icon less half the text), rows 16 high and 3 under each.
        var menu = Menu(items: true, rows: 5);
        var height = menu.RowHeight(iconHeight: 16, textHeight: 11);
        Assert.Equal((16, 3), (height, menu.TextTop(11)));
        var heights = Enumerable.Repeat(height, 5).ToList();

        // 5 × 19 = 95, the list's most, with the last row's gap: no arrow, and the rows' text at 76, 95, 114, 133 and 152.
        var layout = Run(menu, heights, 20);
        Assert.Equal(new ScreenRect(99, 73, 219, 168), layout.List);
        Assert.Equal([0, 19, 38, 57, 76], layout.RowTops);
        Assert.Equal((0, 0), (layout.UpArrow, layout.DownArrow));
    }

    [Fact]
    public void AShortListCountsItsLastGap()
    {
        // Three of C-down's rows: 42 high with the gap under the third, centred on 120.
        var layout = Menu(items: false, rows: 3).Layout([11, 11, 11]);
        Assert.Equal(new ScreenRect(99, 99, 219, 141), layout.List);
    }

    [Fact]
    public void TheLastRowScrollsToTheListsFootWithItsGapBelow()
    {
        var menu = Menu(items: false, rows: 8);
        var heights = Enumerable.Repeat(11, 8).ToList();
        Run(menu, heights, 13);
        menu.Move(-1);
        // The eighth row's foot, 98 + 11, at the list's 95: the goal 14, reached 10 a tick. Its gap stays below the clip, and the down
        // arrow, which looks at the row's foot, fades out.
        Assert.Equal((240, 35), (Run(menu, heights, 13).UpArrow, menu.DownArrowAlpha));
        var layout = Run(menu, heights, 2);
        Assert.Equal(14, layout.Scroll);
        Assert.Equal((255, 0), (layout.UpArrow, layout.DownArrow));
    }
}
