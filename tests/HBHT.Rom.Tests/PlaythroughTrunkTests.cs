using HBHT.Core;
using HBHT.Core.Playthrough;
using HBHT.Plugins;
using HBHT.Plugins.Saves;

namespace HBHT.Rom.Tests;

/// <summary>
/// The trunk of flag coverage (docs/systems/playthrough.md, "Flag coverage") on NA 1.0's data: a short story run with a save before
/// every step, the targets live from each, and each given to one save in its window.
/// </summary>
public sealed class PlaythroughTrunkTests(Na10Import fixture, ITestOutputHelper output) : IDisposable
{
    private Na10Import.Loaded Data => fixture.Data;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hbht-playthrough-trunk-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void AShortTrunkSavesBeforeEveryStepCoversWhatItSetsAndGivesEveryLiveTargetOneSaveInItsWindow()
    {
        var model = QuestModel.Build(Data.Set);
        var graph = new WorldGraph(Data.Set, Path.Combine(Path.GetTempPath(), "hbht-playthrough-cache"));
        var folder = Path.Combine(_root, "trunk");

        // The story run to the first member joining: a handful of steps from a new game.
        var manifest = Trunk.Run(PlaythroughHost.NewGame(Data.Set), model, graph, folder, new MemberGoal(), progress: output.WriteLine);
        Assert.True(manifest.Reached, manifest.Why);
        output.WriteLine($"{manifest.Steps} steps, {manifest.Saves.Count} saves, {manifest.Seen.Count} flags seen");

        // A save and a manifest row before every step (the player has the controls before each), in order, each loading.
        Assert.Equal(Enumerable.Range(1, manifest.Steps), manifest.Saves.Select(s => s.Step));
        var read = Trunk.Read(folder);
        Assert.Equal(manifest.Saves, read.Saves, (a, b) => a.Step == b.Step && a.Save == b.Save && a.Action == b.Action && a.Where == b.Where);
        Assert.Equal(manifest.Seen, read.Seen);
        foreach (var save in manifest.Saves)
        {
            Assert.NotNull(save.Where);
            Assert.NotEmpty(save.Party);
            Assert.Empty(GameState.Load(Data.Set, SaveFile.Read(Path.Combine(folder, save.Save), Data.Set)).Problems);
        }
        // The opening's flags are seen before the first step (3, its done flag); the rest by step, with the save before it.
        Assert.Contains(manifest.Seen, x => x.Flag == Ids.Flag(3).ToString() && x.Step == 0 && x.Save is null);
        Assert.All(manifest.Seen.Where(x => x.Step > 0), x => Assert.Equal(manifest.Saves.Single(s => s.Step == x.Step).Save, x.Save));

        // The looks from the start and each save, side by side.
        var start = GameState.NewGame(Data.Set);
        var targets = CoverGoal.StaticTargets(model, PlanState.From(model, start));
        var games = new List<(int, Func<GameState>)> { (0, () => GameState.NewGame(Data.Set)) };
        games.AddRange(manifest.Saves.Select(s => (s.Step, (Func<GameState>)(() => GameState.Load(Data.Set, SaveFile.Read(Path.Combine(folder, s.Save), Data.Set))))));
        var looks = Trunk.Look(model, graph, games, targets, threads: 4);
        Assert.Equal(games.Select(g => g.Item1), looks.Select(l => l.Step));
        foreach (var look in looks)
            output.WriteLine($"step {look.Step}: {look.Live.Count} live, {look.Rounds} rounds, {look.Seconds:0.0} s");

        // Every target the trunk saw set is covered; every other target live from a save goes to exactly one save in its window; the
        // rest to the start's job or nowhere.
        var covered = manifest.Seen.Select(x => RecordId.Parse(x.Flag)).ToHashSet();
        var assignment = Trunk.Assign(targets, looks.Skip(1).ToList(), looks[0], covered);
        Assert.NotEmpty(assignment.Covered);
        Assert.Equal(targets.Count, assignment.Chosen.Count + assignment.FromStart.Count + assignment.Nowhere.Count + assignment.Covered.Count);
        foreach (var target in targets.Where(t => !covered.Contains(t)))
        {
            var window = looks.Skip(1).Where(l => l.Live.ContainsKey(target)).Select(l => l.Step).ToList();
            if (window.Count == 0)
                Assert.False(assignment.Chosen.ContainsKey(target));
            else
                Assert.Contains(assignment.Chosen[target], window);
        }
        var byStep = assignment.ByStep();
        output.WriteLine($"{assignment.Covered.Count} covered by the trunk, {assignment.Chosen.Count} given to saves " +
            $"({string.Join(", ", byStep.Select(p => $"t{p.Key:000} {p.Value.Count}"))}), {assignment.FromStart.Count} to the start, {assignment.Nowhere.Count} nowhere");

        // As a tree: a job per save with targets, and the trunk's own result, whose flags the ledger counts.
        var queue = new CoverQueue(_root);
        var jobs = Trunk.Jobs(manifest, assignment, "trunk", null);
        queue.Init(new CoverRoot([.. targets.Select(t => t.ToString())], null, 10, 3, DateTimeOffset.UtcNow, "trunk"), jobs);
        queue.WriteResult(Trunk.Result(manifest, assignment, "trunk", jobs));
        Assert.Equal(byStep.Keys.Select(s => $"t{s:000}").Concat(assignment.FromStart.Count > 0 ? ["t000"] : []).Order(StringComparer.Ordinal),
            queue.All().Select(j => j.Job.Id).Order(StringComparer.Ordinal));
        var ledger = queue.Ledger();
        Assert.All(assignment.Covered, t => Assert.Equal("trunk", ledger[t.ToString()].Job));
        foreach (var job in jobs.Where(j => j.Save is not null))
            Assert.True(File.Exists(queue.Resolve(job.Save!)), job.Save);
        // What the trunk covered, no job is after.
        Assert.DoesNotContain(jobs.SelectMany(j => j.Targets), t => ledger.ContainsKey(t));
    }
}
