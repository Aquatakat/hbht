using HBHT.Core.Scripts;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

public class BackdropPageTests
{
    private static BackdropPage Page(int? picture = 0) => new(new TextLayout(TextLayoutTests.Font()), BackdropStyle.For(picture));

    private static string Lines(int count) => string.Join('\n', Enumerable.Repeat("a", count));

    private static ScriptPage Node(string text, params string[] choices) =>
        new(new ScriptLine(0, null, text), [.. choices.Select((c, i) => new ScriptLine(i + 1, null, c))]);

    [Fact]
    public void TheTextStartsAt64And30AndWrapsAt256AboveY205()
    {
        var layout = Page().Show(Node("a"));

        Assert.Equal(new TextBlock("a", 64, 30, new ScreenRect(64, 30, 256, 205)), layout.Text);
        Assert.Empty(layout.Choices);
    }

    [Fact]
    public void ChoicesAreNeverAboveY55()
    {
        // One line is 11 tall, and 4 below it is 45.
        var layout = Page().Show(Node("a", "b", "c"));

        Assert.Equal(new TextBlock("b", 75, 55, new ScreenRect(75, 55, 256, 205)), layout.Choices[0]);
        Assert.Equal(new TextBlock("c", 75, 70, new ScreenRect(75, 55, 256, 205)), layout.Choices[1]);
    }

    [Fact]
    public void ChoicesAre4BelowATallText()
    {
        // Five lines are 55 tall: 30 + 55 + 4 = 89. A choice of two lines is 22, and 4 between.
        var layout = Page().Show(Node(Lines(5), Lines(2), "c"));

        Assert.Equal((89, 115), (layout.Choices[0].Y, layout.Choices[1].Y));
    }

    [Fact]
    public void TheRolledScrollNarrowsOnlyItsTextAndOnlyAsPicture4()
    {
        var layout = Page(4).Show(Node("a", "b"));

        Assert.Equal(240, layout.Text.Bounds.Right);
        Assert.Equal(256, layout.Choices[0].Bounds.Right);
        Assert.Equal(256, BackdropStyle.For(9).TextRight);
    }

    [Fact]
    public void TheListSlidesToKeepTheCursorsChoiceInsideTheMenu()
    {
        // A choice of three lines is 33 tall, 37 with the gap. The one-line text pushes the list down 10, to 55.
        var page = Page();
        page.Show(Node("a", [.. Enumerable.Range(0, 8).Select(_ => Lines(3))]));
        Assert.Equal(55, page.Layout!.Choices[0].Y);

        // The fifth's top is at 55 + 4 × 37 = 203, its foot 31 past 205: the list slides up 31, cutting the first.
        page.MoveCursor(4);
        Assert.Equal((24, 172), (page.Layout.Choices[0].Y, page.Layout.Choices[4].Y));
        Assert.Equal(new ScreenRect(75, 55, 256, 205), page.Layout.Choices[0].Bounds);

        page.MoveCursor(-4);
        Assert.Equal(55, page.Layout.Choices[0].Y);
    }

    [Fact]
    public void TheCursorWrapsAndANewNodePutsItOnTheFirstChoice()
    {
        var page = Page();
        page.Show(Node("a", "b", "c", "d"));
        page.MoveCursor(-1);
        Assert.Equal(2, page.Highlight);
        page.MoveCursor(1);
        Assert.Equal(0, page.Highlight);
        page.MoveCursor(1);

        page.Show(Node("e", "f", "g"));

        Assert.Equal(0, page.Highlight);
        Assert.Equal("e", page.Layout!.Text.Text);
    }

    [Fact]
    public void TheSlideIsKeptFromNodeToNode()
    {
        // As the original never resets it: after a one-line text's 10, five lines' choices are 14 below, not 4.
        var page = Page();
        page.Show(Node("a", "b"));

        var layout = page.Show(Node(Lines(5), "c"));

        Assert.Equal(99, layout.Choices[0].Y);
    }

    [Fact]
    public void ThePageFadesInAndOutAtTheJournals60AFrame()
    {
        var page = Page();
        page.Show(Node("a"));
        var alphas = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            page.Step();
            alphas.Add(page.Alpha);
        }
        Assert.Equal([60, 120, 180, 240, 255], alphas);

        page.Close();
        for (var i = 0; i < 4; i++)
            page.Step();
        Assert.False(page.Closed);
        page.Step();
        Assert.True(page.Closed);
    }

    [Fact]
    public void TheCursorsChoicePulsesAStepATick()
    {
        // The book's pulse, from (100, 100, 100, 100) toward white, 7 a step: 19 steps up and 19 back, 38 ticks.
        var page = Page();
        page.Show(Node("a", "b"));
        page.Step();
        Assert.Equal(new Rgba(107, 107, 107, 107), page.HighlightNow);
        for (var i = 1; i < 19; i++)
            page.Step();
        Assert.Equal(new Rgba(233, 233, 233, 233), page.HighlightNow);
        for (var i = 0; i < 19; i++)
            page.Step();
        Assert.Equal(MenuPulse.ScrollFrom, page.HighlightNow);
    }

    [Fact]
    public void EachPictureHasItsImageAndTheRestShowTheBook()
    {
        Assert.Equal(MenuImages.Book, BackdropStyle.For(0).Picture);
        Assert.Equal(MenuImages.Book, BackdropStyle.For(14).Picture);
        Assert.Equal(MenuImages.Book, BackdropStyle.For(null).Picture);
        int[] pictures = [1, 2, 3, 4, 9, 10, 11, 12];
        int[] images = [0x3606, 0x3603, 0x3605, 0x3608, 0x3608, 0x3602, 0x3607, 0x3604];
        Assert.Equal(images.Select(i => RecordId.Hex(RecordTypes.Image, i)), pictures.Select(p => BackdropStyle.For(p).Picture));
        Assert.Equal([10, 11, 12], pictures.Where(p => BackdropStyle.For(p).Shadow is not null));
        Assert.Equal([new Rgba(0, 0, 0, 255)], new[] { 1, 2, 12 }.Select(p => BackdropStyle.For(p).PulseFrom).Distinct());
        Assert.Equal(MenuPulse.ScrollFrom, BackdropStyle.For(3).PulseFrom);
    }
}
