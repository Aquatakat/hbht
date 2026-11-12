using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using HBHT.Core;
using HBHT.Core.Playthrough;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

/// <summary>
/// <c>hbht playthrough cover</c>: the flag coverage tree (docs/systems/playthrough.md, "Flag coverage"): a root job over every flag the
/// static pass can set, jobs worked by as many processes as are started on the root, and how the targets stand.
/// </summary>
internal static partial class CoverReport
{
    public const string Usage = """
          hbht playthrough cover init --root <dir> [--save <save>] [--targets all|<file>] [--job-minutes <n>] [--most-failures <n>]
                                      [--trunk [<trunk dir>]] [--threads <n>]
                                               starts a cover tree in <dir>: one job from a new game (or the save) over
                                               every flag the static pass can set that isn't set at the start (or the
                                               file's flags, numbers or record IDs, one or more a line); each job may take
                                               <n> minutes (120), and <n> failed steps towards a target give it up (3);
                                               with --trunk, the story run to the ending is played first (or the trunk
                                               folder's copied) into <dir>/trunk with a save before every step, each
                                               target is given to the trunk save it's best started from, looked at on
                                               <n> threads (20), and each save with targets is a job, t<step>
          hbht playthrough cover work --root <dir> [--most-minutes <n>] [--most-jobs <n>] [--threads <n>]
                                               claims pending jobs one after another and plays them, until none is left,
                                               the minutes run out (a job cut short is carried on by another), or <n> jobs
                                               are done; <n> workers on threads share one model and world
                                               graph (1; about 20 suits a 32-thread machine), and several
                                               processes may work one root at once; exits 0 once no job
                                               is pending, 3 when the minutes or jobs ran out first
          hbht playthrough cover retry --root <dir>
                                               once no job is pending in a trunk tree, gives each target given up
                                               a job from another trunk save in its window (the latest untried in odd
                                               rounds, the nearest in even ones), r<round>-t<step>; exits 0 if it made
                                               any, 4 if nothing is left to retry
          hbht playthrough cover party --root <dir> [--threads <n>]
                                               once no job is pending in a trunk tree, gives each target no job saw
                                               set that needs someone in the party from a trunk save a party job:
                                               from the save best for it whose party the planner reaches, it gets
                                               that party first and keeps it, then sweeps its targets,
                                               p<round>-t<step>-<members>; looked at and planned on <n> threads
                                               (20); exits 0 if it made any, 4 if none
          hbht playthrough cover status --root <dir> [--all]
                                               the targets covered, live and given up, the jobs by status, the branches,
                                               and the uncovered targets with their last reason (--all lists every one)
        """;

    public static int Run(PluginSet plugins, string[] args) => args switch
    {
        ["init", .. var rest] => Init(plugins, rest),
        ["work", .. var rest] => Work(plugins, rest),
        ["status", .. var rest] => Status(plugins, rest),
        ["retry", .. var rest] => Retry(rest),
        ["party", .. var rest] => Party(plugins, rest),
        _ => throw new ArgumentException(Usage),
    };

    private static Dictionary<string, string> Options(string[] args, params string[] flags)
    {
        var options = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (flags.Contains(args[i]))
                options[args[i]] = "";
            else if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
                options[args[i]] = args[++i];
            else
                throw new ArgumentException(Usage);
        }
        if (!options.ContainsKey("--root"))
            throw new ArgumentException(Usage);
        return options;
    }

    private static double Number(Dictionary<string, string> options, string name, double otherwise) =>
        !options.TryGetValue(name, out var text) ? otherwise
        : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0 ? n
        : throw new ArgumentException($"{name} takes a number, not {text}.");

    private static int Init(PluginSet plugins, string[] args)
    {
        // --trunk takes a folder, or nothing.
        var (trunk, trunkFrom, rest) = (false, (string?)null, new List<string>());
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != "--trunk")
            {
                rest.Add(args[i]);
                continue;
            }
            trunk = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                trunkFrom = args[++i];
        }
        var options = Options([.. rest]);
        var queue = new CoverQueue(options["--root"]);
        if (queue.Exists)
            throw new ArgumentException($"{queue.Root} holds a cover tree already.");
        var clock = Stopwatch.StartNew();
        string? save = null;
        GameState game;
        if (options.TryGetValue("--save", out var from))
        {
            game = GameState.Load(plugins, SaveFile.Read(from, plugins));
            var copy = Path.Combine(queue.Root, "saves", "start.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(from, copy, overwrite: true);
            save = "saves/start.sav";
        }
        else
        {
            game = GameState.NewGame(plugins);
        }
        var model = QuestModel.Build(plugins);
        var targets = options.TryGetValue("--targets", out var which) && which != "all"
            ? ReadTargets(which, model)
            : CoverGoal.StaticTargets(model, PlanState.From(model, game));
        var root = new CoverRoot([.. targets.Select(t => t.ToString())], save, Number(options, "--job-minutes", 120),
            (int)Number(options, "--most-failures", 3), DateTimeOffset.UtcNow);
        if (trunk)
            return InitTrunk(plugins, queue, root with { Trunk = "trunk" }, model, targets, trunkFrom, (int)Number(options, "--threads", 20));
        queue.Init(root);
        Console.WriteLine($"A cover tree in {queue.Root}: {targets.Count} targets, from {save ?? "a new game"}, in {clock.Elapsed.TotalSeconds:F1} s");
        return 0;
    }

    /// <summary>
    /// The trunk's tree: the story run to the ending with a save before every step (or the trunk folder given, copied in), the targets
    /// live from each save and the start, each target given to one save (<see cref="Trunk.Assign"/>), and a job for each save with
    /// targets. The flags the trunk saw set are its own result, <c>results/trunk.json</c>, so the ledger counts them.
    /// </summary>
    private static int InitTrunk(PluginSet plugins, CoverQueue queue, CoverRoot root, QuestModel model, List<RecordId> targets, string? from, int threads)
    {
        var graph = new WorldGraph(plugins);
        var folder = Path.Combine(queue.Root, root.Trunk!);
        GameState Start() => root.Save is { } s ? GameState.Load(plugins, SaveFile.Read(queue.Resolve(s), plugins)) : GameState.NewGame(plugins);

        var clock = Stopwatch.StartNew();
        TrunkManifest manifest;
        if (from is not null)
        {
            CopyFolder(from, folder);
            manifest = Trunk.Read(folder);
            Console.WriteLine($"The trunk from {from}: {manifest.Steps} steps, {manifest.Saves.Count} saves, played in {manifest.WallSeconds:0} s");
        }
        else
        {
            var host = root.Save is null ? PlaythroughHost.NewGame(plugins) : PlaythroughHost.From(Start());
            manifest = Trunk.Run(host, model, graph, folder, new EndingGoal(), RunOptions.Default, Console.WriteLine);
            Console.WriteLine($"The trunk: {manifest.Steps} steps, {manifest.Saves.Count} saves, {(manifest.Reached ? "the ending reached" : $"stopped: {manifest.Why}")}, " +
                $"in {clock.Elapsed.TotalSeconds:0} s");
        }

        clock.Restart();
        var games = new List<(int, Func<GameState>)> { (0, Start) };
        games.AddRange(manifest.Saves.Select(s => (s.Step, (Func<GameState>)(() => GameState.Load(plugins, SaveFile.Read(Path.Combine(folder, s.Save), plugins))))));
        var looks = Trunk.Look(model, graph, games, targets, threads);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Looked at {looks.Count} games on {threads} threads in {clock.Elapsed.TotalSeconds:0.0} s ({looks.Average(l => l.Seconds):0.0} s each, {looks.Average(l => l.Rounds):0} rounds)"));

        var covered = manifest.Seen.Select(x => RecordId.Parse(x.Flag)).ToHashSet();
        var assignment = Trunk.Assign(targets, looks.Skip(1).ToList(), looks[0], covered);
        var byStep = assignment.ByStep();
        var jobs = Trunk.Jobs(manifest, assignment, root.Trunk!, root.Save);
        queue.Init(root, jobs);
        queue.WriteResult(Trunk.Result(manifest, assignment, root.Trunk!, jobs));

        // The assignment, for whoever asks why a target went where it did.
        var windows = targets.ToDictionary(t => t, t => looks.Skip(1).Where(l => l.Live.ContainsKey(t)).Select(l => new { l.Step, Far = Math.Round(l.Live[t], 1) }).ToList());
        File.WriteAllText(Path.Combine(folder, "assignment.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Slack = Trunk.Slack,
            Ratio = Trunk.Ratio,
            Targets = targets.Select(t => new
            {
                Flag = t.ToString(),
                Job = assignment.Chosen.TryGetValue(t, out var step) ? $"t{step:000}" : assignment.FromStart.Contains(t) ? "t000"
                    : covered.Contains(t) ? "trunk" : null,
                Window = windows[t],
            }),
            assignment.Nowhere,
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var sizes = jobs.Select(j => (j.Id, j.Targets.Count)).OrderByDescending(j => j.Count).ToList();
        Console.WriteLine($"A cover tree in {queue.Root}: {targets.Count} targets; the trunk covered {assignment.Covered.Count}; " +
            $"{assignment.Chosen.Count} given to {byStep.Count} of its {manifest.Saves.Count} saves, {assignment.FromStart.Count} to a job from the start, " +
            $"{assignment.Nowhere.Count} live from nowhere");
        if (sizes.Count > 0)
        {
            var counts = sizes.Select(s => s.Count).Order().ToList();
            Console.WriteLine($"Targets a job: {counts[0]} to {counts[^1]}, median {counts[counts.Count / 2]}; the first save's job {(byStep.Count > 0 ? byStep.First().Value.Count : 0)}, " +
                $"the last's {(byStep.Count > 0 ? byStep.Last().Value.Count : 0)} (step {(byStep.Count > 0 ? byStep.Last().Key : 0)} of {manifest.Steps})");
            Console.WriteLine($"Biggest: {string.Join(", ", sizes.Take(10).Select(s => $"{s.Id} {s.Count}"))}");
        }
        return 0;
    }

    private sealed record AssignedWindow(int Step, float Far);

    private sealed record AssignedTarget(string Flag, string? Job, List<AssignedWindow> Window);

    private sealed record AssignmentFile(List<AssignedTarget> Targets);

    /// <summary>The saves each given-up target has been tried from, and the rounds of retries made: <c>trunk/retries.json</c>.</summary>
    private sealed record RetriesFile(int Rounds, Dictionary<string, List<int>> Tried);

    /// <summary>
    /// A retry round (<see cref="Trunk.Retry"/>): once no job is pending, each target no job saw set gets a job from a trunk save in its
    /// window it hasn't been tried from.
    /// </summary>
    private static int Retry(string[] args)
    {
        var options = Options(args);
        var queue = new CoverQueue(options["--root"]);
        if (!queue.Exists || queue.ReadRoot().Trunk is not { } trunk)
            throw new ArgumentException($"{queue.Root} holds no trunk tree: start one with cover init --trunk.");
        if (queue.All().Any(j => j.Status is JobStatus.Pending or JobStatus.Claimed))
            throw new ArgumentException("Jobs are still pending or claimed: work them first.");
        var folder = Path.Combine(queue.Root, trunk);
        var json = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        var assignment = System.Text.Json.JsonSerializer.Deserialize<AssignmentFile>(File.ReadAllText(Path.Combine(folder, "assignment.json")))!;
        var retriesPath = Path.Combine(folder, "retries.json");
        var retries = File.Exists(retriesPath)
            ? System.Text.Json.JsonSerializer.Deserialize<RetriesFile>(File.ReadAllText(retriesPath))!
            : new RetriesFile(0, []);
        var round = retries.Rounds + 1;
        var ledger = queue.Ledger();
        var chosen = new Dictionary<RecordId, int>();
        var none = 0;
        foreach (var target in assignment.Targets)
        {
            if (ledger.ContainsKey(target.Flag) || target.Job is null or "trunk" || target.Window.Count == 0)
                continue;
            if (!retries.Tried.TryGetValue(target.Flag, out var tried))
            {
                retries.Tried[target.Flag] = tried = [];
                if (target.Job.StartsWith('t') && int.TryParse(target.Job.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var first))
                    tried.Add(first);
            }
            if (Trunk.Retry(target.Window.Select(w => (w.Step, w.Far)), tried.ToHashSet(), round) is { } step)
            {
                chosen[RecordId.Parse(target.Flag)] = step;
                tried.Add(step);
            }
            else
            {
                none++;
            }
        }
        if (chosen.Count == 0)
        {
            Console.WriteLine($"Nothing left to retry: {none} target(s) given up have been tried from every trunk save in their windows.");
            return 4;
        }
        var manifest = Trunk.Read(folder);
        var jobs = Trunk.RetryJobs(manifest, chosen, trunk, round);
        foreach (var job in jobs)
            queue.Add(job);
        File.WriteAllText(retriesPath, System.Text.Json.JsonSerializer.Serialize(retries with { Rounds = round }, json));
        Console.WriteLine($"Retry {round}: {chosen.Count} target(s) given up, in {jobs.Count} job(s) from other trunk saves; " +
            $"{none} tried from every save in their windows");
        return 0;
    }

    /// <summary>The starts each target has been tried from in party rounds (<see cref="PartyStart.Key"/>), and the rounds made: <c>trunk/party.json</c>.</summary>
    private sealed record PartyFile(int Rounds, Dictionary<string, List<string>> Tried);

    /// <summary>How many of its starts, best first, a target's party is planned from before it's given no party job.</summary>
    private const int MostPartyStarts = 4;

    /// <summary>
    /// A party round (<see cref="PartyJobs"/>): once no job is pending, the targets no job saw set are looked at from every trunk save
    /// with the members they need; each that needs someone from a save gets the best start whose party the planner reaches from it (the
    /// world graph's places from where the save stands, the job's own budget), and each start with targets is a job.
    /// </summary>
    private static int Party(PluginSet plugins, string[] args)
    {
        var options = Options(args);
        var queue = new CoverQueue(options["--root"]);
        if (!queue.Exists || queue.ReadRoot().Trunk is not { } trunk)
            throw new ArgumentException($"{queue.Root} holds no trunk tree: start one with cover init --trunk.");
        if (queue.All().Any(j => j.Status is JobStatus.Pending or JobStatus.Claimed))
            throw new ArgumentException("Jobs are still pending or claimed: work them first.");
        var threads = (int)Number(options, "--threads", 20);
        var clock = Stopwatch.StartNew();
        var folder = Path.Combine(queue.Root, trunk);
        var manifest = Trunk.Read(folder);
        var ledger = queue.Ledger();
        var open = queue.ReadRoot().Targets.Where(t => !ledger.ContainsKey(t)).Select(RecordId.Parse).ToList();
        if (open.Count == 0)
        {
            Console.WriteLine("Nothing left: every target has been seen set.");
            return 4;
        }
        var json = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        var path = Path.Combine(folder, "party.json");
        var file = File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<PartyFile>(File.ReadAllText(path))! : new PartyFile(0, []);
        var round = file.Rounds + 1;

        var model = QuestModel.Build(plugins);
        var graph = new WorldGraph(plugins);
        GameState Load(int step) => GameState.Load(plugins, SaveFile.Read(Path.Combine(folder, manifest.Saves.First(s => s.Step == step).Save), plugins));
        var looks = Trunk.Look(model, graph, [.. manifest.Saves.Select(s => (s.Step, (Func<GameState>)(() => Load(s.Step))))], open, threads, party: open);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Looked at {looks.Count} trunk saves for {open.Count} uncovered targets in {clock.Elapsed.TotalSeconds:0.0} s"));

        var brought = PartyJobs.Brought(manifest);
        var starts = open.ToDictionary(t => t, t => PartyJobs.Starts(t, looks,
            file.Tried.TryGetValue(t.ToString(), out var tried) ? tried.ToHashSet() : null, brought).Take(MostPartyStarts).ToList());
        var needing = starts.Where(s => s.Value.Count > 0).Select(s => s.Key).ToList();
        Console.WriteLine($"{needing.Count} of them need someone the trunk doesn't bring in from a trunk save they're live from, " +
            "and haven't been tried from it");
        foreach (var group in needing.GroupBy(t => string.Join(", ", starts[t][0].Members.Select(m => m.Key))).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {group.Count(),4} best started with {group.Key}");

        // Each target takes its first start whose party the planner reaches by a way that keeps the target; each start is planned once,
        // side by side.
        var verdicts = new System.Collections.Concurrent.ConcurrentDictionary<PartyStart, (Plan Plan, IReadOnlySet<RecordId> Keeps)>();
        bool Takes(RecordId target, PartyStart start) => verdicts.TryGetValue(start, out var v) && v.Plan.Found && v.Keeps.Contains(target);
        var at = needing.ToDictionary(t => t, _ => 0);
        var chosen = new Dictionary<RecordId, PartyStart>();
        while (true)
        {
            var asked = new HashSet<PartyStart>();
            foreach (var target in needing.Where(t => !chosen.ContainsKey(t)))
            {
                while (at[target] < starts[target].Count && verdicts.ContainsKey(starts[target][at[target]]) && !Takes(target, starts[target][at[target]]))
                    at[target]++;
                if (at[target] >= starts[target].Count)
                    continue;
                var start = starts[target][at[target]];
                if (verdicts.ContainsKey(start))
                    chosen[target] = start;
                else
                    asked.Add(start);
            }
            if (asked.Count == 0)
                break;
            var list = asked.ToList();
            var next = -1;
            void Work()
            {
                int i;
                while ((i = Interlocked.Increment(ref next)) < list.Count)
                {
                    var start = list[i];
                    var timer = Stopwatch.StartNew();
                    var (plan, keeps) = verdicts[start] = PartyJobs.PlanParty(model, graph, Load(start.Step), start.Members, needing);
                    var lost = needing.Count(t => starts[t].Contains(start) && !keeps.Contains(t));
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"  t{start.Step:000} with {string.Join(", ", start.Members.Select(m => m.Key))}: " +
                        $"{(plan.Found ? $"{plan.Steps.Count} steps{(lost > 0 ? $", losing {lost} of the targets it's a start for" : "")}" : $"no plan ({plan.Why?.Split(Environment.NewLine)[0]})")}, " +
                        $"{timer.Elapsed.TotalSeconds:0.0} s"));
                }
            }
            var workers = Enumerable.Range(0, Math.Max(1, Math.Min(threads, list.Count))).Select(_ => new Thread(Work, 64 * 1024 * 1024)).ToList();
            workers.ForEach(t => t.Start());
            workers.ForEach(t => t.Join());
        }
        foreach (var target in needing)
        {
            if (!file.Tried.TryGetValue(target.ToString(), out var tried))
                file.Tried[target.ToString()] = tried = [];
            // The starts planned and found wanting are tried too: they'd be found wanting again.
            tried.AddRange(starts[target].Take(at[target] + (chosen.ContainsKey(target) ? 1 : 0)).Select(s => s.Key).Where(k => !tried.Contains(k)));
        }
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(file with { Rounds = round }, json));
        if (chosen.Count == 0)
        {
            Console.WriteLine($"No party job: no start's party the planner reaches keeping them, for the {needing.Count} target(s) ({clock.Elapsed.TotalSeconds:0} s)");
            return 4;
        }
        var jobs = PartyJobs.Jobs(manifest, chosen, trunk, round);
        foreach (var job in jobs)
            queue.Add(job);
        Console.WriteLine($"Party {round}: {chosen.Count} target(s) in {jobs.Count} job(s); {needing.Count - chosen.Count} with no start whose party the planner reaches keeping them; " +
            $"in {clock.Elapsed.TotalSeconds:0} s");
        foreach (var job in jobs)
            Console.WriteLine($"  {job.Id}: {job.Targets.Count} target(s)");
        return 0;
    }

    /// <summary>A folder's files copied into another, keeping their places under it.</summary>
    private static void CopyFolder(string from, string to)
    {
        from = Path.GetFullPath(from);
        if (!File.Exists(Path.Combine(from, Trunk.ManifestFile)))
            throw new ArgumentException($"{from} holds no trunk ({Trunk.ManifestFile}).");
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static List<RecordId> ReadTargets(string file, QuestModel model)
    {
        var targets = new List<RecordId>();
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Split('#')[0];
            foreach (var word in line.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                var id = int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? RecordId.Decimal(RecordTypes.Flag, n)
                    : RecordId.TryParse(word, out var parsed) ? parsed
                    : throw new ArgumentException($"{word} in {file} isn't a flag's number or record ID.");
                if (!model.Flags.TryHandle(id, out _))
                    throw new ArgumentException($"{file} names {id}, which no plugin has.");
                targets.Add(id);
            }
        }
        return [.. targets.Distinct()];
    }

    private static int Work(PluginSet plugins, string[] args)
    {
        var options = Options(args);
        var queue = new CoverQueue(options["--root"]);
        if (!queue.Exists)
            throw new ArgumentException($"{queue.Root} holds no cover tree: start one with cover init.");
        var most = options.ContainsKey("--most-minutes") ? TimeSpan.FromMinutes(Number(options, "--most-minutes", 0)) : (TimeSpan?)null;
        var mostJobs = (int)Number(options, "--most-jobs", int.MaxValue);
        var clock = Stopwatch.StartNew();
        var recovered = queue.Recover();
        if (recovered > 0)
            Console.WriteLine($"{recovered} job(s) whose process had died are pending again");
        var threads = (int)Number(options, "--threads", 1);
        if (!queue.All().Any(j => j.Status == JobStatus.Pending))
        {
            Console.WriteLine("No pending job is left.");
            return 0;
        }
        // One model and one world graph, shared by every worker (QuestModel.ForRun gives each run its own reach).
        var built = Stopwatch.StartNew();
        var model = QuestModel.Build(plugins);
        var graph = new WorldGraph(plugins);
        Console.WriteLine($"{model.Actions.Count} actions over {model.Ways.Count} scripts, in {built.Elapsed.TotalSeconds:F1} s; {threads} worker(s)");

        var gate = new object();
        var jobs = 0;
        var busy = 0;
        void Worker()
        {
            while (true)
            {
                CoverJob? job;
                TimeSpan? left;
                lock (gate)
                {
                    while (true)
                    {
                        left = most is { } m ? m - clock.Elapsed : null;
                        if (jobs >= mostJobs || left is { } l && l <= TimeSpan.Zero)
                            return;
                        job = queue.Claim();
                        if (job is not null)
                        {
                            jobs++;
                            busy++;
                            break;
                        }
                        // Nothing pending: done once no worker is playing a job that could make more, else wait for one to finish.
                        if (busy == 0)
                        {
                            Monitor.PulseAll(gate);
                            return;
                        }
                        Monitor.Wait(gate, TimeSpan.FromSeconds(30));
                    }
                }
                try
                {
                    var worker = new CoverWorker(queue, model, graph) { Progress = Console.WriteLine };
                    var result = worker.Work(job, left);
                    Console.WriteLine($"job {job.Id}: {result.Outcome}; {result.Steps} steps, {result.WallSeconds:0} s; " +
                        $"{result.Targets.Count(t => t.State == TargetState.Covered)} of its {result.Targets.Count} targets covered, {result.Made.Count} job(s) made");
                }
                catch (Exception e)
                {
                    // The claim stays, so the job goes back to pending when this process is gone; the other workers go on.
                    Console.Error.WriteLine($"job {job.Id} failed: {e}");
                }
                finally
                {
                    lock (gate)
                    {
                        busy--;
                        Monitor.PulseAll(gate);
                    }
                }
            }
        }
        var workers = Enumerable.Range(0, threads).Select(_ => new Thread(Worker, 64 * 1024 * 1024)).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        Console.WriteLine($"{jobs} job(s) worked in {clock.Elapsed.TotalMinutes:0.0} minutes");
        // 0 once nothing is left to play, 3 when the minutes or the jobs ran out first (tools/cover.ps1 goes by it).
        return queue.All().Any(j => j.Status == JobStatus.Pending) ? 3 : 0;
    }

    private static int Status(PluginSet plugins, string[] args)
    {
        var options = Options(args, "--all");
        var queue = new CoverQueue(options["--root"]);
        if (!queue.Exists)
            throw new ArgumentException($"{queue.Root} holds no cover tree.");
        var root = queue.ReadRoot();
        var ledger = queue.Ledger();
        var jobs = queue.All().ToList();
        var results = queue.ReadResults();
        var open = jobs.Where(j => j.Status != JobStatus.Done).ToList();
        var waiting = new Dictionary<string, string>();
        foreach (var (job, status, _) in open)
        {
            foreach (var t in job.Targets)
                waiting.TryAdd(t, $"{status.ToString().ToLowerInvariant()} in job {job.Id}");
        }
        var last = new Dictionary<string, CoverTargetEntry>();
        foreach (var result in results)
        {
            foreach (var t in result.Targets)
                last[t.Flag] = t;
        }
        var covered = root.Targets.Where(ledger.ContainsKey).ToList();
        var live = root.Targets.Where(t => !ledger.ContainsKey(t) && waiting.ContainsKey(t)).ToList();
        var given = root.Targets.Where(t => !ledger.ContainsKey(t) && !waiting.ContainsKey(t)).ToList();

        Console.WriteLine($"Cover tree {queue.Root}, from {root.Save ?? "a new game"}, made {root.Created:yyyy-MM-dd HH:mm} UTC");
        Console.WriteLine($"Targets: {root.Targets.Count}: {covered.Count} covered, {live.Count} live in jobs not done, {given.Count} given up");
        Console.WriteLine($"Flags seen set in all, targets or not: {ledger.Count}");
        Console.WriteLine($"Jobs: {string.Join(", ", Enum.GetValues<JobStatus>().Select(s => $"{jobs.Count(j => j.Status == s)} {s.ToString().ToLowerInvariant()}"))}");
        var branches = jobs.Where(j => j.Job.Parent is not null && !CarryPattern().IsMatch(j.Job.Id)).ToList();
        Console.WriteLine($"Branches: {branches.Count} ({string.Join(", ", Enum.GetValues<JobStatus>().Select(s => $"{branches.Count(j => j.Status == s)} {s.ToString().ToLowerInvariant()}"))}), " +
            $"deepest {(jobs.Count == 0 ? 0 : jobs.Max(j => j.Job.Depth))}");
        foreach (var (job, _, claim) in jobs.Where(j => j.Status == JobStatus.Claimed))
            Console.WriteLine($"  claimed: {job.Id} by {claim?.Machine} process {claim?.Pid}{(claim?.Alive() == false ? " (dead: pending again at the next claim)" : "")}");
        if (results.FirstOrDefault(r => r.Job == "trunk") is { } trunk)
            Console.WriteLine($"Trunk: {trunk.Outcome}, {trunk.Steps} steps in {trunk.WallSeconds / 60:0.0} minutes; " +
                $"{trunk.Targets.Count} targets covered by it, {trunk.Seen.Count} flags seen in all");
        TrunkJobs(jobs, ledger);
        PartyJobLines(jobs, results, ledger);
        results = [.. results.Where(r => r.Job != "trunk")];
        var steps = results.Sum(r => r.Steps);
        var seconds = results.Sum(r => r.WallSeconds);
        if (seconds > 0)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Worked: {results.Count} job(s), {steps} steps in {seconds / 60:0.0} minutes ({steps / (seconds / 60):0.0} steps and {covered.Count / (seconds / 60):0.0} targets covered a minute)"));

        var reasons = given.Select(t => last.TryGetValue(t, out var e) ? e.Why ?? e.State.ToString() : "never in a finished job").ToList();
        if (given.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Given up, by reason:");
            foreach (var group in reasons.GroupBy(Kind).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {group.Count(),5}  {group.Key}");
            Console.WriteLine();
            Console.WriteLine("Uncovered targets given up:");
            var all = options.ContainsKey("--all");
            foreach (var (flag, why) in given.Zip(reasons).Take(all ? int.MaxValue : 40))
                Console.WriteLine($"  {Label(plugins, flag)}: {why}");
            if (!all && given.Count > 40)
                Console.WriteLine($"  … {given.Count - 40} more (--all)");
        }
        return 0;
    }

    [GeneratedRegex(@"\+\d+$")]
    private static partial Regex CarryPattern();

    [GeneratedRegex(@"^t\d+$")]
    private static partial Regex TrunkJobPattern();

    /// <summary>
    /// The trunk's jobs, four a line, each with where it is (pending, running, done, or done with branches or carried-on jobs still open
    /// under it), its targets covered by anyone of its own, and how many jobs hang off it.
    /// </summary>
    private static void TrunkJobs(List<(CoverJob Job, JobStatus Status, CoverQueue.Claimant? Claim)> jobs, Dictionary<string, (string Job, CoverSeenEntry Seen)> ledger)
    {
        var trunk = jobs.Where(j => TrunkJobPattern().IsMatch(j.Job.Id)).OrderBy(j => j.Job.Id, StringComparer.Ordinal).ToList();
        if (trunk.Count == 0)
            return;
        Console.WriteLine($"Trunk jobs: {trunk.Count} (covered/targets; +n jobs under it, * while any of them is open)");
        var cells = trunk.Select(t =>
        {
            var under = jobs.Where(j => j.Job.Id.Length > t.Job.Id.Length && j.Job.Id.StartsWith(t.Job.Id, StringComparison.Ordinal)
                && j.Job.Id[t.Job.Id.Length] is '.' or '+').ToList();
            var status = t.Status switch { JobStatus.Pending => "wait", JobStatus.Claimed => "run", _ => "done" };
            var open = under.Any(j => j.Status != JobStatus.Done) ? "*" : "";
            return $"{t.Job.Id} {status} {t.Job.Targets.Count(ledger.ContainsKey)}/{t.Job.Targets.Count}{(under.Count > 0 ? $" +{under.Count}{open}" : "")}";
        }).ToList();
        var width = cells.Max(c => c.Length) + 3;
        for (var i = 0; i < cells.Count; i += 4)
            Console.WriteLine("  " + string.Concat(cells.Skip(i).Take(4).Select(c => c.PadRight(width))).TrimEnd());
    }

    /// <summary>
    /// The party jobs, a line each: the members, whether the party was made (after which step) or why not, and their targets covered by
    /// anyone of their own, with the jobs under them.
    /// </summary>
    private static void PartyJobLines(List<(CoverJob Job, JobStatus Status, CoverQueue.Claimant? Claim)> jobs, List<CoverResult> results,
        Dictionary<string, (string Job, CoverSeenEntry Seen)> ledger)
    {
        var party = jobs.Where(j => j.Job.Party is { Count: > 0 } && j.Job.Parent is null).OrderBy(j => j.Job.Id, StringComparer.Ordinal).ToList();
        if (party.Count == 0)
            return;
        var byJob = results.ToDictionary(r => r.Job);
        Console.WriteLine($"Party jobs: {party.Count} (covered/targets; the party made after step n, or why not; +n jobs under it)");
        foreach (var (job, status, _) in party)
        {
            var under = jobs.Count(j => j.Job.Id.Length > job.Id.Length && j.Job.Id.StartsWith(job.Id, StringComparison.Ordinal) && j.Job.Id[job.Id.Length] is '.' or '+');
            var made = !byJob.TryGetValue(job.Id, out var result) ? status.ToString().ToLowerInvariant()
                : result.PartyAt is { } at ? $"made after step {at}" : $"not made: {result.PartyWhy ?? result.Outcome}";
            Console.WriteLine($"  {job.Id} {job.Targets.Count(ledger.ContainsKey)}/{job.Targets.Count}{(under > 0 ? $" +{under}" : "")}: {made}");
        }
    }

    /// <summary>A reason with its particulars taken out, to count by: up to its first colon, its numbers and map objects as n.</summary>
    private static string Kind(string why)
    {
        var head = Regex.Replace(why, @"aidyn:\S+", "<id>").Split(':')[0];
        head = Regex.Replace(head, @"\(.*?\)", "(…)");
        head = Regex.Replace(Regex.Replace(head, @"\d+", "n"), @"n( n)+", "n …");
        return head.Length > 120 ? head[..120] + "…" : head;
    }

    private static string Label(PluginSet plugins, string flag) =>
        RecordId.TryParse(flag, out var id) && plugins.TryGet<FlagRecord>(id)?.Label is { } label ? $"{id.Key} ({label})"
        : RecordId.TryParse(flag, out var key) ? key.Key : flag;
}
