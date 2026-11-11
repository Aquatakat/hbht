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
                                               starts a cover tree in <dir>: one job from a new game (or the save) over
                                               every flag the static pass can set that isn't set at the start (or the
                                               file's flags, numbers or record IDs, one or more a line); each job may take
                                               <n> minutes (120), and <n> failed steps towards a target give it up (3)
          hbht playthrough cover work --root <dir> [--most-minutes <n>] [--most-jobs <n>] [--threads <n>]
                                               claims pending jobs one after another and plays them, until none is left,
                                               the minutes run out (a job cut short is carried on by another), or <n> jobs
                                               are done; <n> workers on threads share one model and world
                                               graph (1; about 20 suits a 32-thread machine), and several
                                               processes may work one root at once; exits 0 once no job
                                               is pending, 3 when the minutes or jobs ran out first
          hbht playthrough cover status --root <dir> [--all]
                                               the targets covered, live and given up, the jobs by status, the branches,
                                               and the uncovered targets with their last reason (--all lists every one)
        """;

    public static int Run(PluginSet plugins, string[] args) => args switch
    {
        ["init", .. var rest] => Init(plugins, rest),
        ["work", .. var rest] => Work(plugins, rest),
        ["status", .. var rest] => Status(plugins, rest),
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
        var options = Options(args);
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
            : StaticTargets(model, PlanState.From(model, game));
        var root = new CoverRoot([.. targets.Select(t => t.ToString())], save, Number(options, "--job-minutes", 120),
            (int)Number(options, "--most-failures", 3), DateTimeOffset.UtcNow);
        queue.Init(root);
        Console.WriteLine($"A cover tree in {queue.Root}: {targets.Count} targets, from {save ?? "a new game"}, in {clock.Elapsed.TotalSeconds:F1} s");
        return 0;
    }

    /// <summary>Every flag, plain or derived, the relaxation from the state sets that isn't set in it.</summary>
    public static List<RecordId> StaticTargets(QuestModel model, PlanState state)
    {
        var result = new Relaxation(model, Enumerable.Range(0, model.Actions.Count)).Run(state);
        var flags = model.Flags;
        return [.. Enumerable.Range(0, flags.Count)
            .Where(h => flags.Kind(h) is FlagKind.Plain or FlagKind.Derived && result.FactLayer[model.FlagFact(h, true)] > 0)
            .Select(flags.Id)];
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
