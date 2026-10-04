using Aidyn.Core.Ui;

namespace Aidyn.Core.Tests;

/// <summary>The journal's list (<c>Journal</c>, <c>WidgetScrollList</c>), with the test font's 11-pixel lines.</summary>
public class JournalMenuTests
{
    private static readonly TextLayout Font = new(TextLayoutTests.Font());

    /// <summary>One-line entries, 15 apart with the gap, between the blank lines at either end.</summary>
    private static JournalMenu Journal(int entries, int newest = -1) =>
        new(Font, [.. Enumerable.Range(0, entries).Select(i => ("a", i == newest))], "none");

    private static void Ticks(JournalMenu journal, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            journal.Step();
    }

    [Fact]
    public void TheEntriesGoBetweenBlankLinesFourApart()
    {
        var journal = Journal(3);
        Assert.Equal(["  ", "a", "a", "a", "  "], journal.Items.Select(i => i.Text));
        Assert.Equal([0, 15, 30, 45, 60], journal.Items.Select(i => i.Top));
        Assert.Equal(71, journal.ListHeight);
    }

    [Fact]
    public void AnEmptyJournalSaysSo()
    {
        Assert.Equal(["  ", "none", "  "], Journal(0).Items.Select(i => i.Text));
    }

    [Fact]
    public void ALongJournalOpensAtTheFootAndAShortOneAtTheTop()
    {
        Assert.Equal(0, Journal(3).Scroll);
        // 22 items: the last blank's foot at 326, brought up to 190.
        var journal = Journal(20, newest: 19);
        Assert.Equal((20, -136), (journal.Highlight, journal.Scroll));
        Assert.True(journal.Items[20].Newest);
    }

    [Fact]
    public void UpBringsTheFirstEntryAboveTheViewDownToItsTopAndWaitsForTheListToStop()
    {
        var journal = Journal(20, newest: 19);
        // Item 9's top is at 30 + 135 − 136 = 29, a pixel above the view.
        journal.Up();
        Assert.Equal(9, journal.Highlight);
        journal.Up();
        Assert.Equal(9, journal.Highlight);
        Ticks(journal, 1);
        Assert.Equal(-135, journal.Scroll);
        journal.Up();
        Assert.Equal(8, journal.Highlight);
        // 15 pixels at 5 a tick.
        Ticks(journal, 2);
        Assert.Equal(-125, journal.Scroll);
        Ticks(journal, 1);
        Assert.Equal(-120, journal.Scroll);
    }

    [Fact]
    public void DownBringsTheNextEntryBelowTheViewUpToItsFootAndStopsAtTheEnd()
    {
        var journal = Journal(20);
        Assert.Equal(-136, journal.Scroll);
        journal.Down();
        Ticks(journal, 1);
        Assert.Equal(-136, journal.Scroll);

        while (journal.Highlight > 0)
        {
            journal.Up();
            Ticks(journal, 40);
        }
        Assert.Equal(0, journal.Scroll);
        // Item 12's foot is at 30 + 180 + 11 = 221, a pixel below.
        journal.Down();
        Assert.Equal(12, journal.Highlight);
        Ticks(journal, 1);
        Assert.Equal(-1, journal.Scroll);
    }

    [Fact]
    public void AHeldDirectionKeepsScrollingWithNoWait()
    {
        var journal = Journal(20);
        for (var i = 0; i < 40; i++)
            journal.Step(upHeld: true);
        Assert.Equal(0, journal.Scroll);
    }

    [Fact]
    public void TheBookFadesSixtyATickAndTheArrowsShowWhereThereIsMore()
    {
        var journal = Journal(20);
        var alphas = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            journal.Step();
            alphas.Add(journal.Alpha);
        }
        Assert.Equal([60, 120, 180, 240, 255], alphas);
        // At the foot: more above, none below.
        Assert.Equal((150, 0), (journal.UpArrow, journal.DownArrow));
        Ticks(journal, 4);
        Assert.Equal(255, journal.UpArrow);

        journal.Close();
        Ticks(journal, 4);
        Assert.Equal(15, journal.Alpha);
        Assert.False(journal.Closed);
        Ticks(journal, 1);
        Assert.True(journal.Closed);
    }
}
