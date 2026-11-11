using HBHT.Core.Playthrough;

namespace HBHT.Core.Tests;

/// <summary>The cover tree's queue on disk (<see cref="CoverQueue"/>): claims, dead claims coming back, and the ledger as the results' union.</summary>
public sealed class CoverQueueTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hbht-cover-queue-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private CoverQueue NewTree(params string[] targets)
    {
        var queue = new CoverQueue(_root);
        queue.Init(new CoverRoot(targets, null, 60, 3, DateTimeOffset.UtcNow));
        return queue;
    }

    /// <summary>A claimant on this machine whose process isn't running: no process has ID 0 with this start.</summary>
    private static CoverQueue.Claimant Dead => new(Environment.MachineName, int.MaxValue - 1, 1);

    private static CoverResult Result(string job, params string[] seen) => new(job, "done", 1, 1, 1,
        [.. seen.Select((f, i) => new CoverSeenEntry(f, i + 1, 10 * i, null))], [], [], DateTimeOffset.UtcNow);

    [Fact]
    public void ARootStartsWithOnePendingJobAndRefusesASecondInit()
    {
        var queue = NewTree("aidyn:flag/10", "aidyn:flag/11");
        var (job, status, _) = Assert.Single(queue.All());
        Assert.Equal("0", job.Id);
        Assert.Equal(JobStatus.Pending, status);
        Assert.Equal(["aidyn:flag/10", "aidyn:flag/11"], job.Targets);
        Assert.Throws<IOException>(() => queue.Init(new CoverRoot([], null, 1, 1, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void AJobIsClaimedOnceAndTheShallowestFirst()
    {
        var queue = NewTree("aidyn:flag/10");
        queue.Add(new CoverJob("0.1", "0", "saves/0/a.sav", ["aidyn:flag/11"], 1));
        var mine = CoverQueue.Claimant.Mine();
        var first = queue.Claim(mine);
        Assert.Equal("0", first?.Id);
        Assert.Equal(JobStatus.Claimed, queue.Find("0"));
        Assert.Equal("0.1", queue.Claim(mine)?.Id);
        Assert.Null(queue.Claim(mine));
        // Adding a job of a name already in the queue does nothing.
        Assert.False(queue.Add(new CoverJob("0", null, null, [], 0)));
    }

    [Fact]
    public void AClaimWhoseProcessDiedGoesBackToPendingButALiveOneStays()
    {
        var queue = NewTree("aidyn:flag/10");
        queue.Add(new CoverJob("0.1", "0", null, ["aidyn:flag/11"], 1));
        Assert.Equal("0", queue.Claim(CoverQueue.Claimant.Mine())?.Id);
        Assert.Equal("0.1", queue.Claim(Dead)?.Id);
        var claimed = queue.All().Where(j => j.Status == JobStatus.Claimed).ToList();
        Assert.Equal(2, claimed.Count);
        Assert.True(claimed.Single(j => j.Job.Id == "0").Claim!.Value.Alive());
        Assert.False(claimed.Single(j => j.Job.Id == "0.1").Claim!.Value.Alive());

        Assert.Equal(1, queue.Recover());
        Assert.Equal(JobStatus.Claimed, queue.Find("0"));
        Assert.Equal(JobStatus.Pending, queue.Find("0.1"));
        // The next claim takes it again.
        Assert.Equal("0.1", queue.Claim(CoverQueue.Claimant.Mine())?.Id);
    }

    [Fact]
    public void AClaimOnAnotherMachineIsTakenToBeAlive()
    {
        var queue = NewTree("aidyn:flag/10");
        Assert.NotNull(queue.Claim(new CoverQueue.Claimant("SOMEWHERE-ELSE", 1, 1)));
        Assert.Equal(0, queue.Recover());
        Assert.Null(queue.Claim(CoverQueue.Claimant.Mine()));
    }

    [Fact]
    public void CompletingAJobWritesItsResultAddsWhatItMadeAndMovesItToDone()
    {
        var queue = NewTree("aidyn:flag/10", "aidyn:flag/11", "aidyn:flag/12");
        var me = CoverQueue.Claimant.Mine();
        var root = queue.Claim(me)!;
        queue.Complete(root, Result("0", "aidyn:flag/10", "aidyn:flag/500"), [new CoverJob("0.1", "0", "saves/0/b.sav", ["aidyn:flag/12"], 1, 4, "open x")], me);
        Assert.Equal(JobStatus.Done, queue.Find("0"));
        Assert.Equal(JobStatus.Pending, queue.Find("0.1"));

        var branch = queue.Claim(me)!;
        Assert.Equal(4, branch.Step);
        queue.Complete(branch, Result("0.1", "aidyn:flag/12", "aidyn:flag/10"), [], me);

        // The ledger is the union of the results, each flag's first sighting kept.
        var ledger = queue.Ledger();
        Assert.Equal(["aidyn:flag/10", "aidyn:flag/12", "aidyn:flag/500"], ledger.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("0", ledger["aidyn:flag/10"].Job);
        Assert.Equal("0.1", ledger["aidyn:flag/12"].Job);
        // What another job saw, a job's open targets leave out.
        Assert.Equal(["aidyn:flag/11"], CoverWorker.Open(new CoverJob("x", null, null, ["aidyn:flag/10", "aidyn:flag/11"], 0), ledger).Select(f => f.ToString()));
    }

    [Fact]
    public void SeveralClaimersAtOnceNeverClaimOneJobTwice()
    {
        var queue = NewTree("aidyn:flag/10");
        for (var i = 1; i <= 40; i++)
            queue.Add(new CoverJob($"0.{i}", "0", null, [], 1));
        var claimed = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 8, w =>
        {
            var claimant = new CoverQueue.Claimant("SOMEWHERE-ELSE", 1000 + w, 1);
            while (new CoverQueue(_root).Claim(claimant) is { } job)
                claimed.Add(job.Id);
        });
        Assert.Equal(41, claimed.Count);
        Assert.Equal(41, claimed.Distinct().Count());
    }

    [Fact]
    public void ASavesPathIsKeptRelativeToTheRootWhereItsUnderIt()
    {
        var queue = NewTree();
        Assert.Equal("saves/0/01-start.sav", queue.Relative(Path.Combine(_root, "saves", "0", "01-start.sav")));
        Assert.Equal(Path.Combine(_root, "saves/0/01-start.sav"), queue.Resolve("saves/0/01-start.sav"));
        var outside = Path.GetFullPath(Path.Combine(_root, "..", "elsewhere.sav"));
        Assert.Equal(outside, queue.Relative(outside));
    }
}
