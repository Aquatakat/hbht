using HBHT.Core.Playthrough;

namespace HBHT.Core.Tests;

/// <summary>
/// The cover targets settled as not the bot's (docs/systems/playthrough.md, "The user's review of what's left"): the table <c>cover
/// status</c> reports them apart by.
/// </summary>
public sealed class NotTheBotsTests
{
    [Fact]
    public void TheTableHoldsEachFlagOnceByItsKind()
    {
        Assert.Equal(NotTheBots.All.Count, NotTheBots.All.Select(n => n.Flag).Distinct().Count());
        Assert.All(NotTheBots.All, n => Assert.False(string.IsNullOrWhiteSpace(n.Why)));
        Assert.Equal(NotTheBotsKind.Overestimate, NotTheBots.Of(Ids.Flag(5409))!.Kind);
        Assert.Equal(NotTheBotsKind.Original, NotTheBots.Of(Ids.Flag(600))!.Kind);
        Assert.Equal(NotTheBotsKind.SetAside, NotTheBots.Of(Ids.Flag(3143))!.Kind);
        // Keelin's and Godric and Becan's are the bot's: the user expects them reachable.
        Assert.Null(NotTheBots.Of(Ids.Flag(486)));
        Assert.Null(NotTheBots.Of(Ids.Flag(468)));
        Assert.Equal(20, NotTheBots.All.Count(n => n.Kind == NotTheBotsKind.Overestimate));
        Assert.Equal(13, NotTheBots.All.Count(n => n.Kind == NotTheBotsKind.Original));
        Assert.Equal(2, NotTheBots.All.Count(n => n.Kind == NotTheBotsKind.SetAside));
    }
}
