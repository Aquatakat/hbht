using HBHT.Core.Playthrough;

namespace HBHT.Rom.Tests;

/// <summary>
/// Playthrough runs side by side in one process (docs/systems/playthrough.md, "The run"): they share the plugins, the quest model and
/// the world graph, and each plays as it would alone.
/// </summary>
public sealed class PlaythroughSideBySideTests(Na10Import fixture, ITestOutputHelper output) : IDisposable
{
    private readonly string _saves = Path.Combine(Path.GetTempPath(), "hbht-playthrough-side-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_saves))
            Directory.Delete(_saves, recursive: true);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void RunsOnThreadsSharingTheModelAndTheGraphEachPlayAsOneAloneDoes()
    {
        var set = fixture.Data.Set;
        var model = QuestModel.Build(set);
        var graph = new WorldGraph(set, Path.Combine(Path.GetTempPath(), "hbht-playthrough-cache"));
        RunReport Play(int copy) => new Playthrough(PlaythroughHost.NewGame(set), model, graph, new MemberGoal(),
            new RunOptions(MostTicks: 200_000, MostFailures: 4, Saves: Path.Combine(_saves, $"copy-{copy}"))).Run();

        var alone = Play(0);
        Assert.True(alone.Summary!.Reached, alone.Summary.Why);
        // The shared model's own reach is the planner's default: a run plays on a model of its own.
        Assert.Same(Everywhere.Instance, model.Reach);

        var reports = new RunReport[4];
        var threads = Enumerable.Range(0, reports.Length).Select(i => new Thread(() => reports[i] = Play(i + 1), 16 << 20)).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        string Played(RunReport r) => string.Join("\n", r.Steps.Select(s => $"{s.Step} {s.Action}: {s.Outcome}, {s.Ticks} ticks from {s.AtTick}"))
            + $"\n{r.Summary!.Steps} steps, {r.Summary.Ticks} ticks, {r.Summary.Battles} battles; "
            + string.Join("; ", r.Milestones.Select(m => $"{m.Kind} {m.What} at {m.Tick}"));
        output.WriteLine(Played(alone));
        foreach (var report in reports)
            Assert.Equal(Played(alone), Played(report));
    }
}
