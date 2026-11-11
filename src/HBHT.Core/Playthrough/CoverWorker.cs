using System.Diagnostics;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Core.Playthrough;

/// <summary>
/// Plays a cover tree's jobs (<see cref="CoverQueue"/>): each from its save, over the targets no job has seen set yet, as a run with a
/// <see cref="CoverGoal"/>. A job's branches become pending jobs; a job stopped by the worker's own time, with targets still live, is
/// carried on from a save of where it stopped.
/// </summary>
public sealed class CoverWorker(CoverQueue queue, QuestModel model, WorldGraph graph)
{
    public CoverQueue Queue { get; } = queue;

    public Action<string>? Progress { get; set; }

    /// <summary>How each job's run plays, before the queue's own saves and wall time are put on it.</summary>
    public RunOptions Options { get; set; } = RunOptions.Default;

    /// <summary>The targets of a job that no job has seen set.</summary>
    public static List<RecordId> Open(CoverJob job, IReadOnlyDictionary<string, (string Job, CoverSeenEntry Seen)> ledger) =>
        [.. job.Targets.Where(t => !ledger.ContainsKey(t)).Select(RecordId.Parse)];

    /// <summary>
    /// Plays one claimed job within <paramref name="most"/> of wall time (the job's own cap if less), and completes it in the queue.
    /// </summary>
    public CoverResult Work(CoverJob job, TimeSpan? most = null)
    {
        var root = Queue.ReadRoot();
        var clock = Stopwatch.StartNew();
        var cap = TimeSpan.FromMinutes(root.JobMinutes);
        var cutByWorker = most is { } m && m < cap;
        var wall = cutByWorker ? most!.Value : cap;
        var ledger = Queue.Ledger();
        var targets = Open(job, ledger);
        var plugins = model.Plugins;
        var made = new List<CoverJob>();
        if (targets.Count == 0)
        {
            var empty = new CoverResult(job.Id, "nothing left: every target was seen by another job", 0, 0, 0, [], [], [], DateTimeOffset.UtcNow);
            Queue.Complete(job, empty, made);
            return empty;
        }
        if (job.Save is { } s && !File.Exists(Queue.Resolve(s)))
        {
            var missing = new CoverResult(job.Id, $"its save {s} is missing", 0, 0, 0, [],
                [.. targets.Select(t => new CoverTargetEntry(t.ToString(), TargetState.GivenUp, $"the job's save {s} is missing"))], [], DateTimeOffset.UtcNow);
            Queue.Complete(job, missing, made);
            return missing;
        }
        PlaythroughHost host;
        try
        {
            host = job.Save is { } save
                ? PlaythroughHost.From(GameState.Load(plugins, SaveFile.Read(Queue.Resolve(save), plugins)))
                : PlaythroughHost.NewGame(plugins);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            var unread = new CoverResult(job.Id, $"its save didn't load: {e.Message}", 0, 0, 0, [],
                [.. targets.Select(t => new CoverTargetEntry(t.ToString(), TargetState.GivenUp, $"the job's save didn't load: {e.Message}"))], [], DateTimeOffset.UtcNow);
            Queue.Complete(job, unread, made);
            return unread;
        }
        var goal = new CoverGoal(targets)
        {
            MostFailures = root.MostFailures,
            CoveredElsewhere = () => Queue.Ledger().Keys.Select(RecordId.Parse).ToHashSet(),
        };
        var options = Options with { Saves = Queue.SavesOf(job.Id), MostWall = wall };
        var run = new Playthrough(host, model, graph, goal, options) { Progress = Progress };
        Progress?.Invoke($"job {job.Id}: {targets.Count} targets, from {job.Save ?? "a new game"}, for at most {wall.TotalMinutes:0.#} minutes");
        var report = run.Run();
        report.Write(Queue.ReportOf(job.Id));
        var cover = run.Coverage!;
        var summary = report.Summary;
        var outOfTime = summary?.Why?.Contains("wall time", StringComparison.Ordinal) == true;

        // Branches, from the saves before the steps that lost their targets.
        var n = 0;
        foreach (var branch in cover.Branches)
        {
            if (branch.Save is null)
                continue;
            made.Add(new CoverJob($"{job.Id}.{++n}", job.Id, Queue.Relative(branch.Save), [.. branch.Targets.Select(t => t.ToString())],
                job.Depth + 1, branch.Step, branch.Action, branch.Predicted ? "the loss check said it would lose them" : "lost after the step"));
        }
        // Stopped by the worker's own time with targets still live: carried on from here.
        var live = cover.Targets.Where(t => t.State == TargetState.Live).ToList();
        if (live.Count > 0 && outOfTime && cutByWorker)
        {
            var path = Path.Combine(Queue.SavesOf(job.Id), "carried-on.sav");
            if (run.SaveNow(path))
            {
                made.Add(new CoverJob(NextCarry(job.Id), job.Id, Queue.Relative(path), [.. live.Select(t => t.Flag.ToString())], job.Depth,
                    Why: "carried on: the worker's time ran out"));
                foreach (var t in live)
                    t.Why = "carried on in another job";
            }
        }
        var why = summary is null ? "no summary" : summary.Reached ? "every target covered or given up" : summary.Why ?? "stopped";
        foreach (var t in cover.Targets.Where(t => t.State == TargetState.Live && t.Why is null))
            t.Why = outOfTime && !cutByWorker ? $"the job's {root.JobMinutes:0.#} minutes ran out" : $"the job stopped: {why}";
        var result = new CoverResult(
            job.Id,
            why,
            summary?.Steps ?? 0,
            summary?.Ticks ?? 0,
            clock.Elapsed.TotalSeconds,
            [.. cover.Seen.Select(x => new CoverSeenEntry(x.Flag.ToString(), x.Step, x.Tick, x.Save is null ? null : Queue.Relative(x.Save)))],
            [.. cover.Targets.Select(t => new CoverTargetEntry(t.Flag.ToString(), t.State, t.Why))],
            [.. made.Select(j => j.Id)],
            DateTimeOffset.UtcNow)
        {
            Costs = new Dictionary<string, double>
            {
                ["looks"] = cover.Looks,
                ["lookSeconds"] = cover.LookSeconds,
                ["lossChecks"] = cover.LossChecks,
                ["lossSeconds"] = cover.LossSeconds,
                ["plans"] = cover.Plans,
                ["planSeconds"] = cover.PlanSeconds,
                ["routes"] = cover.Routes,
                ["routeSeconds"] = cover.RouteSeconds,
            },
        };
        Queue.Complete(job, result, made);
        Progress?.Invoke($"job {job.Id} done: {cover}; made {made.Count} job(s)");
        return result;
    }

    /// <summary>The name of a job carried on from another: <c>&lt;id&gt;+n</c>, the next n not yet in the queue.</summary>
    private string NextCarry(string id)
    {
        var stem = System.Text.RegularExpressions.Regex.Replace(id, @"\+\d+$", "");
        for (var n = 1; ; n++)
        {
            var name = $"{stem}+{n}";
            if (Queue.Find(name) is null)
                return name;
        }
    }
}
