using HBHT.Core.Playthrough;

namespace HBHT.Core.Tests;

/// <summary>
/// The trunk's assignment (<see cref="Trunk.Choose"/>, <see cref="Trunk.Assign"/>): each target goes to the latest trunk save of those
/// within the slack of its nearest, and a target with no window goes to the start's job or nowhere.
/// </summary>
public sealed class TrunkAssignmentTests
{
    private static TrunkLook Look(int step, params (int Flag, float Far)[] live) =>
        new(step, live.ToDictionary(l => Ids.Flag(l.Flag), l => l.Far), 1, 0);

    [Fact]
    public void TheLatestSaveWithinFiftyOfTheNearestIsChosen()
    {
        // Nearest at step 3 (20); steps 5 and 7 are within 50 of it, step 9 isn't.
        Assert.Equal(7, Trunk.Choose([(1, 400), (3, 20), (5, 60), (7, 69), (9, 71)]));
    }

    [Fact]
    public void FarOffTheSlackIsHalfAsFarAgainAsTheNearest()
    {
        // Nearest 1,000: within 1,500 rather than 1,050.
        Assert.Equal(6, Trunk.Choose([(2, 1_000), (4, 1_200), (6, 1_490), (8, 1_510)]));
        // Near, 50 is the more.
        Assert.Equal(4, Trunk.Choose([(2, 10), (4, 59), (6, 61)]));
    }

    [Fact]
    public void AWindowWithOneSaveTakesItAndNoneTakesNothing()
    {
        Assert.Equal(12, Trunk.Choose([(12, 5_000)]));
        Assert.Null(Trunk.Choose([]));
    }

    [Fact]
    public void AnotherMapSetsDistanceIsTheSameFromEverySaveSoTheLatestIsTaken()
    {
        // A target in a map set the trunk never stands in is 10,000 and a bit from every save: the latest in its window, where the party
        // is furthest on.
        Assert.Equal(30, Trunk.Choose([(10, 10_012), (20, 10_012), (30, 10_012)]));
        // One save in its map set wins over the rest.
        Assert.Equal(20, Trunk.Choose([(10, 10_012), (20, 300), (30, 10_012)]));
    }

    [Fact]
    public void EveryTargetGoesToOneSaveInItsWindowTheStartsJobOrNowhere()
    {
        var saves = new[]
        {
            Look(1, (10, 5), (11, 300)),
            Look(2, (10, 30), (11, 250), (12, 40)),
            Look(4, (11, 100), (12, 400)),
        };
        // 13 is live only from the start, 14 from nowhere, 15 the trunk set itself.
        var start = Look(0, (10, 5), (11, 300), (12, 40), (13, 70), (15, 0));
        var targets = new[] { 10, 11, 12, 13, 14, 15 }.Select(Ids.Flag).ToList();
        var assignment = Trunk.Assign(targets, saves, start, new HashSet<HBHT.Plugins.RecordId> { Ids.Flag(15) });

        Assert.Equal(2, assignment.Chosen[Ids.Flag(10)]);
        Assert.Equal(4, assignment.Chosen[Ids.Flag(11)]);
        Assert.Equal(2, assignment.Chosen[Ids.Flag(12)]);
        Assert.Equal([Ids.Flag(13)], assignment.FromStart);
        Assert.Equal([Ids.Flag(14)], assignment.Nowhere);
        Assert.Equal([Ids.Flag(15)], assignment.Covered);
        // Each chosen save is in the target's window.
        foreach (var (target, step) in assignment.Chosen)
            Assert.Contains(saves, s => s.Step == step && s.Live.ContainsKey(target));
        Assert.Equal([2, 4], assignment.ByStep().Keys);
    }
}
