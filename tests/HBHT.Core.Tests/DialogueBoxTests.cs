using HBHT.Core.Scripts;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

public class DialogueBoxTests
{
    private static readonly RecordId Player = RecordId.Hex(RecordTypes.Entity, 0x023F);
    private static readonly RecordId Trahern = RecordId.Hex(RecordTypes.Entity, 0x0239);
    private static readonly RecordId Brenna = RecordId.Hex(RecordTypes.Entity, 0x0240);

    private static DialogueBox Box() => new(new TextLayout(TextLayoutTests.Font()), 52, 52, Player);

    private static ScriptLine Line(string text, RecordId? speaker) => new(0, speaker, text);

    private static string Lines(int count) => string.Join('\n', Enumerable.Repeat("a", count));

    /// <summary>Choices from one speaker under a line from another.</summary>
    private static ScriptPage Page(string line, params string[] choices) =>
        new(Line(line, Trahern), [.. choices.Select(c => Line(c, Brenna))]);

    [Fact]
    public void ALineSitsAtTheBottomOfTheScreen()
    {
        var layout = Box().Show(new ScriptPage(Line("a", Trahern), []));

        // One line is 11 tall, which would put the box's top at 215: it's never shorter than the border. The band ends at the overscan bar, 228.
        Assert.Equal((180, 228, Trahern), (layout.Top, layout.Bottom, layout.Speaker));
        Assert.Equal(new TextBlock("a", 78, 184, new ScreenRect(78, 184, 294, 230)), layout.Line);
        Assert.Equal((20, 180, 26, 186), (layout.BorderX, layout.BorderY, layout.PortraitX, layout.PortraitY));
        Assert.Empty(layout.Choices);
        Assert.Equal(0, layout.Highlight);
    }

    [Fact]
    public void ABoxGrowsUpwardToFitALongLine()
    {
        // Six lines are 66 tall: 232 - 2 - 66 - 4 = 160.
        var layout = Box().Show(new ScriptPage(Line(Lines(6), Trahern), []));

        Assert.Equal((160, 228), (layout.Top, layout.Bottom));
        Assert.Equal(new TextBlock(Lines(6), 78, 164, new ScreenRect(78, 164, 294, 230)), layout.Line);
        Assert.Equal((20, 160, 26, 166), (layout.BorderX, layout.BorderY, layout.PortraitX, layout.PortraitY));
    }

    [Fact]
    public void ALineTooTallForTheScreenSitsAtTheBase()
    {
        // 22 lines are 242 tall, which wraps in the original's u16s.
        var layout = Box().Show(new ScriptPage(Line(Lines(22), Trahern), []));

        Assert.Equal(180, layout.Top);
        Assert.Equal(new TextBlock(Lines(22), 78, 184, new ScreenRect(78, 184, 294, 230)), layout.Line);
        Assert.Equal(180, layout.BorderY);
    }

    [Fact]
    public void ALineWithNoSpeakerNamesNoOne()
    {
        Assert.Null(Box().Show(new ScriptPage(Line("a", null), [])).Speaker);
    }

    [Fact]
    public void ChoicesFromAnotherSpeakerFillTheBoxUnderTheLine()
    {
        var box = Box();
        var page = Page("a\na\na", "a", "b");

        Assert.False(box.ShowsLineBeforeChoices);
        var layout = box.Show(page);

        // The choices end at 180 - 4 + 52 = 228: two lines of 11 and 4 between them start at 202.
        Assert.Equal(
        [
            new TextBlock("a", 88, 202, new ScreenRect(88, 202, 294, 228)),
            new TextBlock("b", 88, 217, new ScreenRect(88, 202, 294, 228)),
        ], layout.Choices);
        // The line's text ends 2 above them. Three lines are 33 tall: 200 - 33 - 4 = 163.
        Assert.Equal(new TextBlock("a\na\na", 78, 167, new ScreenRect(78, 167, 294, 200)), layout.Line);
        Assert.Equal((163, 228, Trahern), (layout.Top, layout.Bottom, layout.Speaker));
        Assert.Equal((20, 163, 26, 169), (layout.BorderX, layout.BorderY, layout.PortraitX, layout.PortraitY));
    }

    [Fact]
    public void AShortLineAboveChoicesStaysAtTheBaseAndEndsAtTheirTop()
    {
        var layout = Box().Show(Page("a", "a", "b"));

        Assert.Equal(new TextBlock("a", 78, 184, new ScreenRect(78, 184, 294, 200)), layout.Line);
        Assert.Equal(180, layout.Top);
    }

    [Fact]
    public void AChoiceOfSeveralLinesTakesThemAll()
    {
        // The first is two lines: 22 + 4 + 11 = 37, from 228 - 37 = 191.
        var layout = Box().Show(Page("a", "a\na", "b"));

        Assert.Equal([191, 217], layout.Choices.Select(c => c.Y));
        Assert.All(layout.Choices, c => Assert.Equal(new ScreenRect(88, 191, 294, 228), c.Bounds));
    }

    [Fact]
    public void ALineBeforeItsOwnSpeakersChoicesShowsAlone()
    {
        var box = Box();
        var page = new ScriptPage(Line(Lines(6), Trahern), [Line("a", Trahern), Line("b", Trahern)]);

        Assert.False(box.ShowsLineBeforeChoices);
        var alone = box.Show(page);

        Assert.True(box.ShowsLineBeforeChoices);
        Assert.Empty(alone.Choices);
        Assert.Equal(new TextBlock(Lines(6), 78, 164, new ScreenRect(78, 164, 294, 230)), alone.Line);
        Assert.Equal((160, 228, Trahern), (alone.Top, alone.Bottom, alone.Speaker));

        var choices = box.ShowChoices();

        Assert.False(box.ShowsLineBeforeChoices);
        Assert.Null(choices.Line);
        Assert.Equal(Player, choices.Speaker);
        Assert.Equal([202, 217], choices.Choices.Select(c => c.Y));
        // The box keeps its base of four lines over the two choices (the user, in 1.0).
        Assert.Equal(180, choices.Top);
        Assert.Equal(228, choices.Bottom);
        // The portrait and its border stay where the line put them.
        Assert.Equal((20, 160, 26, 166), (choices.BorderX, choices.BorderY, choices.PortraitX, choices.PortraitY));
        Assert.Same(choices, box.Layout);
    }

    [Fact]
    public void ShowingANewPageStartsOverAtTheLine()
    {
        var box = Box();
        box.Show(new ScriptPage(Line(Lines(6), Trahern), [Line("a", Trahern)]));
        Assert.Equal(160, box.ShowChoices().BorderY);

        var layout = box.Show(new ScriptPage(Line("a", Trahern), []));

        Assert.Equal(180, layout.BorderY);
        Assert.NotNull(layout.Line);
        Assert.Equal(Trahern, layout.Speaker);
    }

    [Fact]
    public void ShowChoicesNeedsChoicesWaitingBehindTheLine()
    {
        var box = Box();
        Assert.Throws<InvalidOperationException>(() => box.ShowChoices());

        box.Show(new ScriptPage(Line("a", Trahern), []));
        Assert.Throws<InvalidOperationException>(() => box.ShowChoices());

        box.Show(Page("a", "a"));
        Assert.Throws<InvalidOperationException>(() => box.ShowChoices());

        box.Show(new ScriptPage(Line("a", Trahern), [Line("a", Trahern)]));
        box.ShowChoices();
        Assert.Throws<InvalidOperationException>(() => box.ShowChoices());
    }

    [Fact]
    public void TheCursorStopsAtTheFirstAndLastChoice()
    {
        var box = Box();
        box.Show(Page("a", "a", "b", "c"));

        box.MoveCursor(-1);
        Assert.Equal((0, 0), (box.Highlight, box.Layout!.Highlight));
        box.MoveCursor(1);
        Assert.Equal((1, 1), (box.Highlight, box.Layout!.Highlight));
        box.MoveCursor(5);
        Assert.Equal((2, 2), (box.Highlight, box.Layout!.Highlight));
        box.MoveCursor(-9);
        Assert.Equal((0, 0), (box.Highlight, box.Layout!.Highlight));
    }

    [Fact]
    public void TheCursorDoesNothingWithoutChoices()
    {
        var box = Box();
        box.MoveCursor(1);
        Assert.Equal(0, box.Highlight);

        box.Show(new ScriptPage(Line("a", Trahern), [Line("a", Trahern), Line("b", Trahern)]));
        box.MoveCursor(1);
        Assert.Equal(0, box.Highlight);

        box.ShowChoices();
        box.MoveCursor(1);
        Assert.Equal(1, box.Highlight);
        Assert.Equal(1, box.Layout!.Highlight);
    }

    [Fact]
    public void ANewPageResetsTheCursorAndPulse()
    {
        var box = Box();
        box.Show(Page("a", "a", "b"));
        box.MoveCursor(1);
        StepFrames(box, 5);

        box.Show(Page("a", "a", "b"));

        Assert.Equal(0, box.Highlight);
        Assert.Equal(DialogueBox.ChoiceColor, box.HighlightNow);
    }

    private static void StepFrames(DialogueBox box, int frames)
    {
        for (var i = 0; i < frames * Cadence.FrameTicks; i++)
            box.Step();
    }

    [Fact]
    public void ThePulseAdvancesOnceAFrame()
    {
        var box = Box();
        Assert.Equal(new Rgba(200, 180, 100, 255), box.HighlightNow);

        for (var i = 0; i < Cadence.FrameTicks - 1; i++)
            box.Step();
        Assert.Equal(DialogueBox.ChoiceColor, box.HighlightNow);

        box.Step();
        // Each channel moves by (to - from) / 24 toward zero: -150/24 = -6, -135/24 = -5, -75/24 = -3, -192/24 = -8.
        Assert.Equal(new Rgba(194, 175, 97, 247), box.HighlightNow);
    }

    [Fact]
    public void ThePulseTurnsAroundAfter23FramesAndAgainAtZero()
    {
        var box = Box();

        StepFrames(box, 23);
        Assert.Equal(new Rgba(200 - 6 * 23, 180 - 5 * 23, 100 - 3 * 23, 255 - 8 * 23), box.HighlightNow);

        StepFrames(box, 1);
        Assert.Equal(new Rgba(200 - 6 * 22, 180 - 5 * 22, 100 - 3 * 22, 255 - 8 * 22), box.HighlightNow);

        StepFrames(box, 22);
        Assert.Equal(DialogueBox.ChoiceColor, box.HighlightNow);

        StepFrames(box, 1);
        Assert.Equal(new Rgba(194, 175, 97, 247), box.HighlightNow);
    }

    [Fact]
    public void ThePanelIsInsideTheOverscanBarsAndFeatheredWhereTheyWere()
    {
        var layout = Box().Show(new ScriptPage(Line("a", Trahern), []));

        Assert.Equal(new FeatheredPanel(new ScreenRect(16, 180, 304, 228), 10, 12, 16, 16), layout.Panel);
    }

    [Fact]
    public void OnAWiderOrTallerScreenTheBoxKeepsItsSizeAtTheBottomCentre()
    {
        var box = Box();
        var page = Page("a", "a", "b");
        var small = box.Show(page);

        // A 16:9 screen with twice the original's lines: the frame is at (266, 240).
        box.Resize(852, 480);
        var large = box.Layout!;

        Assert.Equal((266, 240), box.Frame);
        Assert.Equal(small.Panel.Offset(266, 240), large.Panel);
        Assert.Equal((small.BorderX + 266, small.BorderY + 240), (large.BorderX, large.BorderY));
        Assert.Equal(small.Line!.X + 266, large.Line!.X);
        Assert.Equal(small.Line.Bounds.Right - small.Line.Bounds.Left, large.Line.Bounds.Right - large.Line.Bounds.Left);
        Assert.Equal(small.Choices.Select(c => c.Y + 240), large.Choices.Select(c => c.Y));
    }
}
