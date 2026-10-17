using Aidyn.Core.Scripts;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

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
    public void TheRolledScrollWrapsAt240()
    {
        var layout = Page(4).Show(Node("a", "b"));

        Assert.Equal(240, layout.Text.Bounds.Right);
        Assert.Equal(240, layout.Choices[0].Bounds.Right);
        Assert.Equal(240, BackdropStyle.For(9).Right);
    }

    [Fact]
    public void UpTo8ChoicesShowAndTheWindowFollowsTheCursor()
    {
        var page = Page();
        page.Show(Node("a", [.. Enumerable.Range(0, 10).Select(i => $"c{i}")]));
        Assert.Equal(8, page.Layout!.Choices.Count);

        page.MoveCursor(8);
        Assert.Equal((8, 1, "c1"), (page.Highlight, page.Layout.First, page.Layout.Choices[0].Text));
        Assert.Equal(55, page.Layout.Choices[0].Y);

        page.MoveCursor(5);
        Assert.Equal((9, 2), (page.Highlight, page.Layout.First));
        page.MoveCursor(-9);
        Assert.Equal((0, 0), (page.Highlight, page.Layout.First));
    }

    [Fact]
    public void ANewNodePutsTheCursorOnItsFirstChoice()
    {
        var page = Page();
        page.Show(Node("a", "b", "c"));
        page.MoveCursor(1);

        page.Show(Node("d", "e", "f"));

        Assert.Equal(0, page.Highlight);
        Assert.Equal("d", page.Layout!.Text.Text);
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
    public void EachPictureHasItsImageAndTheRestShowTheBook()
    {
        Assert.Equal(MenuImages.Book, BackdropStyle.For(0).Picture);
        Assert.Equal(MenuImages.Book, BackdropStyle.For(14).Picture);
        Assert.Equal(MenuImages.Book, BackdropStyle.For(null).Picture);
        int[] pictures = [1, 2, 3, 4, 9, 10, 11, 12];
        int[] images = [0x3606, 0x3603, 0x3605, 0x3608, 0x3608, 0x3602, 0x3607, 0x3604];
        Assert.Equal(images.Select(i => RecordId.Hex(RecordTypes.Image, i)), pictures.Select(p => BackdropStyle.For(p).Picture));
        Assert.Equal([10, 11, 12], pictures.Where(p => BackdropStyle.For(p).Shadow is not null));
    }
}
