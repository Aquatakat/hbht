using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

public class TextLayoutTests
{
    private static readonly Rgba White = new(200, 200, 200, 255);
    private static readonly ScreenRect Screen = new(0, 0, 320, 240);

    /// <summary>
    /// Cells 8 × 9, four to a column and two columns. ' ' is 3 wide, 'a' 5, 'b' 6, 'c' 4 and 'd' 8, and
    /// '\u007F' is 7 wide and has a colour. '#' has no glyph.
    /// </summary>
    internal static FontRecord Font(bool buttonColors = true) => new(
        RecordId.Decimal(RecordTypes.Font, 0), RecordId.Hex(RecordTypes.Image, 0x1C), 4, 2, 8, 9,
        [3, 5, 6, 7, 1, 4, 1, 8],
        new Dictionary<string, int> { [" "] = 0, ["a"] = 1, ["b"] = 2, ["\u007F"] = 3, ["c"] = 5, ["d"] = 7 },
        buttonColors ? new Dictionary<string, Rgba> { ["\u007F"] = new(0, 0, 164, 255) } : null);

    private static readonly TextLayout Layout = new(Font());

    private static List<(int X, int Y)> Positions(TextRun run) => [.. run.Glyphs.Select(g => (g.X, g.Y))];

    [Fact]
    public void LinesAreTheCellHeightPlusTwoApart()
    {
        Assert.Equal(11, Layout.LineHeight());
        Assert.Equal(20, Layout.LineHeight(2f));
        Assert.Equal(15, Layout.LineHeight(1.5f));
    }

    [Fact]
    public void AdvanceIsTheGlyphsWidthAndUndrawnCharactersTakeNoRoom()
    {
        Assert.Equal([5, 6, 3, 7], "ab \u007F".Select(c => Layout.Advance(c)));
        Assert.Equal([0, 0, 0, 0], "#\n\té".Select(c => Layout.Advance(c)));
        Assert.Equal(1, Layout.Glyph('a'));
        Assert.Null(Layout.Glyph('#'));
        Assert.Null(Layout.Glyph('\n'));
    }

    [Fact]
    public void AdvanceScalesAndTruncates()
    {
        Assert.Equal(7, Layout.Advance('a', 1.5f));
        Assert.Equal(9, Layout.Advance('b', 1.5f));
        Assert.Equal(4, Layout.Advance(' ', 1.5f));
        Assert.Equal(12, Layout.Advance('b', 2f));
    }

    [Fact]
    public void WidthAddsTheAdvancesOfADrawnCharacters()
    {
        Assert.Equal(0, Layout.Width(""));
        Assert.Equal(25, Layout.Width("ab a#\nb"));
        Assert.Equal(7 + 9 + 4 + 7, Layout.Width("ab a", 1.5f));
    }

    [Fact]
    public void TextWrapsOnlyAtSpaces()
    {
        // "aaa" is 15 wide and "bbb" 18, so with the space in between the second word ends at 36.
        Assert.Equal(22, Layout.Height("aaa bbb", 0, 20));
        Assert.Equal(22, Layout.Height("aaa bbb", 0, 35));
        Assert.Equal(11, Layout.Height("aaa bbb", 0, 36));
        Assert.Equal(11, Layout.Height("aaa bbb", 0, 320));
    }

    [Fact]
    public void AWordThatEndsAtTheEdgeFits()
    {
        // "aaa bb" ends at 15 + 3 + 12 = 30.
        Assert.Equal(11, Layout.Height("aaa bb", 0, 30));
        Assert.Equal(22, Layout.Height("aaa bb", 0, 29));
    }

    [Fact]
    public void ASpaceAtTheStartOfALineTakesNoRoom()
    {
        var run = Layout.Print(" a", 10, 0, Screen, White);

        Assert.Equal([(10, 0)], Positions(run));
        Assert.Equal(11, run.Height);
    }

    [Fact]
    public void TheSpaceAWrapBreaksAtIsDropped()
    {
        var run = Layout.Print("aaa bbb", 0, 0, new ScreenRect(0, 0, 20, 240), White);

        Assert.Equal([(0, 0), (5, 0), (10, 0), (0, 11), (6, 11), (12, 11)], Positions(run));
        Assert.Equal(22, run.Height);
    }

    [Fact]
    public void AWordLongerThanTheLineOverflowsInsteadOfBreaking()
    {
        Assert.Equal(11, Layout.Height("aaaaaa", 0, 20));

        // Six a's are 30 wide. Inside the bounds they're cut off at 20: the fifth starts there and is dropped.
        var run = Layout.Print("aaaaaa", 0, 0, new ScreenRect(0, 0, 20, 240), White);

        Assert.Equal([(0, 0), (5, 0), (10, 0), (15, 0)], Positions(run));
        Assert.Equal(11, run.Height);
    }

    [Fact]
    public void ANewlineBreaksTheLine()
    {
        var run = Layout.Print("a\nb", 4, 10, Screen, White);

        Assert.Equal([(4, 10), (4, 21)], Positions(run));
        Assert.Equal(22, run.Height);
        Assert.Equal(22, Layout.Height("a\nb", 0, 320));
        // A trailing newline starts a line.
        Assert.Equal(22, Layout.Height("a\n", 0, 320));
    }

    [Fact]
    public void UndrawnCharactersTakeNoRoomAndAreSkipped()
    {
        var run = Layout.Print("a#b\t", 0, 0, Screen, White);

        Assert.Equal([(0, 0), (5, 0)], Positions(run));
        Assert.Equal(11, Layout.Height("#", 0, 320));
    }

    [Fact]
    public void HeightIsZeroForEmptyTextOrNoRoom()
    {
        Assert.Equal(0, Layout.Height("", 0, 320));
        Assert.Equal(0, Layout.Height("a", 50, 50));
        Assert.Equal(0, Layout.Height("a", 50, 10));
    }

    [Fact]
    public void PrintingNothingStillTakesALine()
    {
        var run = Layout.Print("", 0, 30, Screen, White);

        Assert.Empty(run.Glyphs);
        Assert.Equal(11, run.Height);
    }

    [Fact]
    public void PrintingInBoundsWithNoAreaDrawsNothing()
    {
        foreach (var bounds in new[] { new ScreenRect(10, 0, 10, 240), new ScreenRect(0, 20, 320, 20) })
        {
            var run = Layout.Print("a", 0, 0, bounds, White);
            Assert.Empty(run.Glyphs);
            Assert.Equal(0, run.Height);
        }
    }

    [Fact]
    public void GlyphsPointAtTheirCellsColumnByColumn()
    {
        var run = Layout.Print("abcd", 10, 20, Screen, White);

        Assert.Equal(
        [
            new PlacedGlyph(10, 20, 0, 9, 5, 9, White),
            new PlacedGlyph(15, 20, 0, 18, 6, 9, White),
            // Glyph 5 is column 1, row 1. Glyph 7 is column 1, row 3.
            new PlacedGlyph(21, 20, 8, 9, 4, 9, White),
            new PlacedGlyph(25, 20, 8, 27, 8, 9, White),
        ], run.Glyphs);
        Assert.Equal(11, run.Height);
    }

    [Fact]
    public void LinesAreTheScaledLineHeightApart()
    {
        var run = Layout.Print("ab\na", 0, 0, Screen, White, 1.5f, 1.5f);

        // a's advance of 7.5 truncates to 7.
        Assert.Equal([(0, 0), (7, 0), (0, 15)], Positions(run));
        Assert.Equal(30, run.Height);
        Assert.Equal(30, Layout.Height("ab\na", 0, 320, 1.5f, 1.5f));
    }

    [Fact]
    public void AGlyphOnTheLeftEdgeIsCut()
    {
        var run = Layout.Print("a", 10, 20, new ScreenRect(12, 0, 320, 240), White);

        Assert.Equal([new PlacedGlyph(12, 20, 2, 9, 3, 9, White)], run.Glyphs);
    }

    [Fact]
    public void AGlyphOnTheRightEdgeIsCut()
    {
        var run = Layout.Print("a", 10, 20, new ScreenRect(0, 0, 13, 240), White);

        Assert.Equal([new PlacedGlyph(10, 20, 0, 9, 3, 9, White)], run.Glyphs);
    }

    [Fact]
    public void AGlyphOnTheBottomEdgeIsCut()
    {
        var run = Layout.Print("a", 10, 20, new ScreenRect(0, 0, 320, 25), White);

        Assert.Equal([new PlacedGlyph(10, 20, 0, 9, 5, 5, White)], run.Glyphs);
    }

    [Fact]
    public void AGlyphOnTheTopEdgeIsCut()
    {
        var run = Layout.Print("a", 10, 20, new ScreenRect(0, 22, 320, 240), White);

        Assert.Equal([new PlacedGlyph(10, 22, 0, 11, 5, 7, White)], run.Glyphs);
    }

    [Fact]
    public void AGlyphOutsideTheBoundsIsDropped()
    {
        Assert.Empty(Layout.Print("a", 10, 20, new ScreenRect(15, 0, 320, 240), White).Glyphs);
        Assert.Empty(Layout.Print("a", 10, 20, new ScreenRect(0, 0, 10, 240), White).Glyphs);
        Assert.Empty(Layout.Print("a", 10, 20, new ScreenRect(0, 29, 320, 240), White).Glyphs);
        Assert.Empty(Layout.Print("a", 10, 20, new ScreenRect(0, 0, 320, 20), White).Glyphs);
    }

    [Fact]
    public void AButtonIsDrawnInItsOwnColourWithTheTextsAlpha()
    {
        var text = new Rgba(200, 200, 200, 128);

        var run = Layout.Print("a\u007Fa", 0, 0, Screen, text);

        Assert.Equal([text, new Rgba(0, 0, 164, 128), text], run.Glyphs.Select(g => g.Color));
        Assert.Equal([0, 5, 12], run.Glyphs.Select(g => g.X));
    }

    [Fact]
    public void AButtonKeepsTheTextsColourWhenTheFontHasNone()
    {
        var run = new TextLayout(Font(buttonColors: false)).Print("\u007F", 0, 0, Screen, White);

        Assert.Equal([White], run.Glyphs.Select(g => g.Color));
    }
}
