using HBHT.Core.Playthrough;
using HBHT.Plugins;

namespace HBHT.Core.Tests;

/// <summary>
/// Party jobs' starts (<see cref="PartyJobs.Starts"/>, <see cref="PartyJobs.Brought"/>): a target goes to the trunk saves it needs someone
/// from, weighed by its distance and the members' distances to join, best first; members the trunk brings in itself aren't asked for.
/// </summary>
public sealed class PartyJobsTests
{
    private static readonly RecordId Becan = Ids.Entity(0x0242);
    private static readonly RecordId Godric = Ids.Entity(0x0248);
    private static readonly RecordId Rheda = Ids.Entity(0x024C);

    private static TrunkLook Look(int step, float far, IReadOnlyList<RecordId> needs, params (RecordId Member, float Far)[] joins) =>
        new(step, new Dictionary<RecordId, float> { [Ids.Flag(10)] = far }, 1, 0)
        {
            Needs = needs.Count == 0 ? new Dictionary<RecordId, IReadOnlyList<RecordId>>() : new Dictionary<RecordId, IReadOnlyList<RecordId>> { [Ids.Flag(10)] = needs },
            Joins = joins.ToDictionary(j => j.Member, j => j.Far),
        };

    [Fact]
    public void ATargetsStartsAreTheSavesItNeedsSomeoneFromNearestFirstWithTheLatestWithinTheSlack()
    {
        List<TrunkLook> looks =
        [
            Look(2, 40, [Becan], (Becan, 10)),
            Look(4, 30, [Becan], (Becan, 50)),
            // Nothing needed from here: no party job starts here.
            Look(6, 5, []),
            // Godric can't join from here.
            Look(8, 5, [Godric]),
            Look(9, 300, [Becan], (Becan, 400)),
        ];
        var starts = PartyJobs.Starts(Ids.Flag(10), looks);
        // 50 and 80: both within 50 of the nearest, so the later first; then the far one.
        Assert.Equal([4, 2, 9], starts.Select(s => s.Step));
        Assert.All(starts, s => Assert.Equal([Becan], s.Members));
        Assert.Equal(80, starts[0].Far);

        // A start tried before is left out.
        Assert.Equal([2, 9], PartyJobs.Starts(Ids.Flag(10), looks, new HashSet<string> { starts[0].Key }).Select(s => s.Step));
        // A target live from nowhere it needs someone from has none.
        Assert.Empty(PartyJobs.Starts(Ids.Flag(11), looks));
    }

    [Fact]
    public void ATargetNeedingOnlyMembersTheTrunkBringsInLaterHasNoStartThere()
    {
        var manifest = new TrunkManifest("the ending", true, null, 3, 0, 0,
        [
            new TrunkSave(1, "a", 0, null, [new MemberLevel(GameState.Alaron.ToString(), "Alaron", 1, 0)], "t001.sav"),
            new TrunkSave(2, "b", 0, null, [new MemberLevel(GameState.Alaron.ToString(), "Alaron", 1, 0), new MemberLevel(Rheda.ToString(), "Rheda", 1, 0)], "t002.sav"),
            new TrunkSave(3, "c", 0, null, [new MemberLevel(GameState.Alaron.ToString(), "Alaron", 1, 0)], "t003.sav"),
        ], [], DateTimeOffset.UnixEpoch);
        var brought = PartyJobs.Brought(manifest);
        Assert.Equal([Rheda], brought[1]);
        Assert.Empty(brought[2]);
        Assert.Empty(brought[3]);

        // Rheda alone from step 1 is the story's own order: she joins there as the trunk has her. From step 3, where the trunk never
        // brings her back, she's a party; and with Godric, whom the trunk never brings, so is the pair from step 1.
        List<TrunkLook> alone = [Look(1, 10, [Rheda], (Rheda, 5)), Look(3, 10, [Rheda], (Rheda, 5))];
        Assert.Equal([3], PartyJobs.Starts(Ids.Flag(10), alone, brought: brought).Select(s => s.Step));
        List<TrunkLook> pair = [Look(1, 10, [Rheda, Godric], (Rheda, 5), (Godric, 20)), Look(3, 10, [Rheda], (Rheda, 5))];
        var starts = PartyJobs.Starts(Ids.Flag(10), pair, brought: brought);
        Assert.Equal([(3, (IReadOnlyList<RecordId>)[Rheda]), (1, [Rheda, Godric])], starts.Select(s => (s.Step, s.Members)), Comparer);
        Assert.Equal(35, starts[1].Far);
    }

    private static readonly IEqualityComparer<(int, IReadOnlyList<RecordId>)> Comparer =
        EqualityComparer<(int, IReadOnlyList<RecordId>)>.Create((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2), x => x.Item1);

    [Fact]
    public void APartyJobIsNamedByItsRoundSaveAndMembersAndCarriesItsParty()
    {
        var start = new PartyStart(7, [Becan, Godric], 12);
        Assert.Equal("p1-t007-0242_0248", PartyJobs.JobId(1, start));
        var manifest = new TrunkManifest("the ending", true, null, 7, 0, 0,
            [new TrunkSave(7, "talk to someone", 0, null, [], "saves/steps/t007.sav")], [], DateTimeOffset.UnixEpoch);
        var jobs = PartyJobs.Jobs(manifest, new Dictionary<RecordId, PartyStart> { [Ids.Flag(10)] = start, [Ids.Flag(11)] = start with { } }, "trunk", 1);
        var job = Assert.Single(jobs);
        Assert.Equal("trunk/saves/steps/t007.sav", job.Save);
        Assert.Equal([Ids.Flag(10).ToString(), Ids.Flag(11).ToString()], job.Targets);
        Assert.Equal([Becan.ToString(), Godric.ToString()], job.Party);
        Assert.Equal(0, job.Depth);
    }
}
