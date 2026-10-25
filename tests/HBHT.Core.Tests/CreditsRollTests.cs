using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>The credits (<c>Credits.Step</c> and its states, 1.0 <c>0x8002c6d8</c>), at a tick a frame.</summary>
public class CreditsRollTests
{
    private const int Line = 11, Screen = 240;

    /// <summary>Two cards shown alone, of one line and of three, then three that scroll, the last with a blank line before a name.</summary>
    private static readonly CreditsRecord Credits = new(RecordId.Decimal(RecordTypes.Credits, 0),
    [
        new("H2O Credits", []),
        new("Project Directors", ["Christopher Bailey", "David Dubord"]),
        new("Designers", ["David Dubord", "Andrew Laing"]),
        new("THQ Credits", []),
        new("Special Thanks", ["Herman Ma", "\nDavid Pridie (1971-2001)"]),
    ], Shown: 2, End: "The End");

    private static int StepUntil(CreditsRoll roll, Func<CreditsRoll, bool> done, int limit = 100_000)
    {
        for (var ticks = 0; ticks < limit; ticks++)
        {
            if (done(roll))
                return ticks;
            roll.Step();
        }
        throw new InvalidOperationException("It never happened.");
    }

    [Fact]
    public void ACardShownAloneIsCentredAndFadesInHoldsAndFadesOut()
    {
        var roll = new CreditsRoll(Credits, Line, Screen);

        Assert.Empty(roll.Lines);
        // After the widget's opening and the card's set-up, it's half its height and the screen's up: 115 for a line.
        var first = StepUntil(roll, r => r.Lines.Any());
        Assert.Equal(CreditsRoll.Opening + 2, first);
        Assert.Equal(new CreditsLine("H2O Credits", 115, CreditsRoll.Heading with { A = 4 }), roll.Lines.Single());
        // 62 ticks rising by 4 to 252, the 63rd at full, then the hold.
        Assert.Equal(63, StepUntil(roll, r => r.Lines.Single().Color.A == 255));
        Assert.Equal(CreditsRoll.CardHold, StepUntil(roll, r => r.Lines.Single().Color.A < 255));
        // 63 ticks falling, and the next card is set up on the tick after the last.
        Assert.Equal(63, StepUntil(roll, r => !r.Lines.Any()));
        Assert.Equal(2, StepUntil(roll, r => r.Lines.Any()));
        // Three lines, 33 tall: (33 + 240) / 2 up, the heading yellow over the names in white.
        Assert.Equal([("Project Directors", 104, CreditsRoll.Heading), ("Christopher Bailey", 115, CreditsRoll.Name), ("David Dubord", 126, CreditsRoll.Name)],
            roll.Lines.Select(l => (l.Text, l.Y, l.Color with { A = 255 })));
    }

    [Fact]
    public void TheRestScrollHalfAPixelATickEachJoining32AboveTheFoot()
    {
        var roll = new CreditsRoll(Credits, Line, Screen);
        StepUntil(roll, r => r.Lines.Any(l => l.Text == "Project Directors"));
        StepUntil(roll, r => !r.Lines.Any());

        // The scroll moves on its second tick, and every other one after.
        var start = StepUntil(roll, r => r.Lines.Any());
        Assert.Equal(2, start);
        Assert.Equal(new CreditsLine("Designers", Screen - 1, CreditsRoll.Heading), roll.Lines.Single());
        roll.Step();
        Assert.Equal(Screen - 1, roll.Lines.Single().Y);
        roll.Step();
        Assert.Equal(Screen - 2, roll.Lines.Single().Y);

        // The next joins once the one before's foot is 32 above the screen's, and the gap stays.
        StepUntil(roll, r => r.Lines.Any(l => l.Text == "THQ Credits"));
        var designers = roll.Lines.First(l => l.Text == "Designers").Y;
        Assert.Equal(Screen - 1, roll.Lines.First(l => l.Text == "THQ Credits").Y);
        Assert.Equal(Screen - 1 - 33 - CreditsRoll.ScrollGap, designers);
        for (var i = 0; i < 300; i++)
            roll.Step();
        Assert.Equal(CreditsRoll.ScrollGap, roll.Lines.First(l => l.Text == "THQ Credits").Y - roll.Lines.First(l => l.Text == "Designers").Y - 33);

        // A newline in a name leaves a blank line before it: the name is drawn on its string's second line.
        StepUntil(roll, r => r.Lines.Any(l => l.Text.StartsWith('\n')));
        var thanks = roll.Lines.Where(l => l.Y >= roll.Lines.First(t => t.Text == "Special Thanks").Y).ToList();
        Assert.Equal(["Special Thanks", "Herman Ma", "\nDavid Pridie (1971-2001)"], thanks.Select(l => l.Text));
        Assert.Equal(thanks[0].Y + 2 * Line, thanks[2].Y);

        // They end when the last has gone off the top, with nothing after.
        StepUntil(roll, r => r.Ended);
        Assert.Empty(roll.Lines);
    }

    [Fact]
    public void ABOrStartEndsThemButNotTheEndings()
    {
        var roll = new CreditsRoll(Credits, Line, Screen);
        StepUntil(roll, r => r.Lines.Any());
        roll.Skip();
        Assert.True(roll.Ended);
        Assert.Empty(roll.Lines);

        var ending = new CreditsRoll(Credits, Line, Screen, ending: true);
        StepUntil(ending, r => r.Lines.Any());
        ending.Skip();
        Assert.False(ending.Ended);
    }

    [Fact]
    public void TheEndingsEndWithTheEndInTheMiddle()
    {
        var roll = new CreditsRoll(Credits, Line, Screen, ending: true);
        StepUntil(roll, r => r.Lines.Any(l => l.Text == "Special Thanks"));
        StepUntil(roll, r => r.Lines.Any(l => l.Text == "The End"));

        Assert.Equal(new CreditsLine("The End", 114, CreditsRoll.EndColor with { A = 2 }), roll.Lines.Single());
        // 127 ticks rising by 2 to 254, the 128th at full, the hold, then 127 ticks falling.
        Assert.Equal(127, StepUntil(roll, r => r.Lines.Single().Color.A == 255));
        Assert.Equal(CreditsRoll.EndHold, StepUntil(roll, r => r.Lines.Single().Color.A < 255));
        Assert.Equal(127, StepUntil(roll, r => r.Ended));
    }
}
