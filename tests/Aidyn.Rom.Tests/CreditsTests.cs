using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Import;

namespace Aidyn.Rom.Tests;

public class CreditsTests
{
    /// <summary>
    /// Every version cuts its credits into the same 33 cards, the first 12 shown one at a time; the debug build's last
    /// card, Special Thanks, is three names short.
    /// </summary>
    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void CreditsAreCutIntoCards(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var table = CreditsTable.Read(rom);
        var credits = BaseImporter.BuildCredits(table, GameDb.Read(rom));

        Assert.Equal([0, 1, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 29, 36, 38, 44, 52, 58, 60, 68, 76, 77, 79, 82, 84, 86, 89, 92, 96, 111, 112, 116],
            table.Starts);
        Assert.Equal(12, credits.Shown);
        Assert.Equal(33, credits.Cards.Count);
        Assert.Equal(("H2O Credits", 0), (credits.Cards[0].Heading, credits.Cards[0].Names.Count));
        Assert.Equal(["Christopher Bailey", "David Dubord"], credits.Cards[1].Names);
        // The first to scroll, and the two headings with no names under them.
        Assert.Equal("Designers", credits.Cards[credits.Shown].Heading);
        Assert.Equal(("THQ Credits", "Studio X"), (credits.Cards[21].Heading, credits.Cards[30].Heading));
        Assert.Empty(credits.Cards[21].Names);
        Assert.Equal("Special Thanks", credits.Cards[^1].Heading);
        Assert.Equal(id == RomVersionId.Debug ? 12 : 15, credits.Cards[^1].Names.Count);
        Assert.Equal("The End", credits.End);
        if (id != RomVersionId.Debug)
        {
            // Three names have a newline before them, which leaves a blank line.
            Assert.Equal(3, credits.Cards[^1].Names.Count(n => n.StartsWith('\n')));
            Assert.Equal("\nDavid Pridie (1971-2001)", credits.Cards[^1].Names[^1]);
        }
    }
}
