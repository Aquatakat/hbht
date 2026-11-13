using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HBHT.Core.Playthrough;

/// <summary>A cover tree's root (<c>cover.json</c>): every target, where the root job starts, and the caps each job plays under.</summary>
/// <param name="Targets">The flags to cover, as record IDs.</param>
/// <param name="Save">The save the root job starts from, under the root or absolute; a new game if null.</param>
/// <param name="JobMinutes">The wall time one job may take, after which its live targets are given up.</param>
/// <param name="MostFailures">Steps towards one target that may fail before it's given up.</param>
/// <param name="Trunk">The trunk's folder under the root, when the jobs start from its saves (<see cref="Trunk"/>); null for one root job.</param>
public sealed record CoverRoot(IReadOnlyList<string> Targets, string? Save, double JobMinutes, int MostFailures, DateTimeOffset Created, string? Trunk = null);

/// <summary>A job in the tree: where it starts, which flags it's after, and where it came from.</summary>
/// <param name="Id">Its name: the root's <c>0</c>, a branch its parent's and <c>.n</c>, a job carried on its own and <c>+n</c>.</param>
/// <param name="Save">The save it starts from, relative to the root (or absolute); a new game if null.</param>
/// <param name="Depth">How many branches down the tree it is.</param>
/// <param name="Step">The parent's step it branched before, if a branch.</param>
/// <param name="Action">That step's action.</param>
/// <param name="Avoid">The steps it never takes (<see cref="CoverGoal.Avoid"/>).</param>
/// <param name="Party">The members it gets into the party first and keeps there (<see cref="CoverGoal.Party"/>), as record IDs: a party job's.</param>
/// <param name="Shun">The story triggers no route of it crosses (<see cref="CoverGoal.Shun"/>), as record IDs.</param>
/// <param name="Out">The members it gets out of the party first and keeps out (<see cref="CoverGoal.Out"/>), as record IDs: a party job's.</param>
public sealed record CoverJob(string Id, string? Parent, string? Save, IReadOnlyList<string> Targets, int Depth, int? Step = null,
    string? Action = null, string? Why = null, IReadOnlyList<string>? Avoid = null, IReadOnlyList<string>? Party = null,
    IReadOnlyList<string>? Shun = null, IReadOnlyList<string>? Out = null);

/// <summary>A flag a job saw set first: the step, the tick, and the save before the step (relative to the root).</summary>
public sealed record CoverSeenEntry(string Flag, int Step, long Tick, string? Save);

/// <summary>Where one of a job's targets ended.</summary>
public sealed record CoverTargetEntry(string Flag, TargetState State, string? Why);

/// <summary>What a job came to (<c>results/&lt;id&gt;.json</c>): the flags it saw, its targets, and the jobs it made.</summary>
public sealed record CoverResult(
    string Job,
    string Outcome,
    int Steps,
    long Ticks,
    double WallSeconds,
    IReadOnlyList<CoverSeenEntry> Seen,
    IReadOnlyList<CoverTargetEntry> Targets,
    IReadOnlyList<string> Made,
    DateTimeOffset Finished)
{
    /// <summary>Where the seconds went: the targets' looks, the loss checks, the plans and the routes, and how many of each.</summary>
    public IReadOnlyDictionary<string, double>? Costs { get; init; }

    /// <summary>For a party job, the step its party was whole after (0 as it began); null if it never was.</summary>
    public int? PartyAt { get; init; }

    /// <summary>For a party job whose party never was whole, why it stopped trying.</summary>
    public string? PartyWhy { get; init; }
}

/// <summary>Where a job is in the queue.</summary>
public enum JobStatus
{
    Pending,
    Claimed,
    Done,
}

/// <summary>
/// The cover tree's jobs on disk (docs/systems/playthrough.md, "Flag coverage"), safe for several processes on one root folder at once.
/// A job is a file that moves between <c>jobs/pending</c>, <c>jobs/claimed</c> and <c>jobs/done</c>: a claim is an atomic rename into
/// <c>claimed</c> under a name carrying the claimer's machine, process ID and process start time, so a claim whose process has died
/// goes back to pending the next time anything claims. Each job writes its result to <c>results/&lt;id&gt;.json</c>, and the ledger
/// of every flag seen is their union, read on demand, so no two processes write one file.
/// </summary>
public sealed class CoverQueue
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public CoverQueue(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string RootFile => Path.Combine(Root, "cover.json");

    private string Jobs(JobStatus status) => Path.Combine(Root, "jobs", status.ToString().ToLowerInvariant());

    public string Results => Path.Combine(Root, "results");

    /// <summary>The folder a job's saves go in.</summary>
    public string SavesOf(string job) => Path.Combine(Root, "saves", job);

    /// <summary>The path a job's report is written to, as <c>.txt</c> and <c>.json</c>.</summary>
    public string ReportOf(string job) => Path.Combine(Root, "reports", job + ".report");

    public bool Exists => File.Exists(RootFile);

    /// <summary>Starts a tree: the root, and its one pending job. Refuses a folder that holds one already.</summary>
    public CoverJob Init(CoverRoot root)
    {
        var job = new CoverJob("0", null, root.Save, root.Targets, 0, Why: "the root");
        Init(root, [job]);
        return job;
    }

    /// <summary>Starts a tree with the jobs given as its pending roots, as the trunk's are. Refuses a folder that holds one already.</summary>
    public void Init(CoverRoot root, IEnumerable<CoverJob> jobs)
    {
        if (Exists)
            throw new IOException($"{Root} holds a cover tree already.");
        foreach (var status in Enum.GetValues<JobStatus>())
            Directory.CreateDirectory(Jobs(status));
        Directory.CreateDirectory(Results);
        foreach (var job in jobs)
            Add(job);
        // The root last: a folder whose root file is there holds a whole tree.
        WriteAtomic(RootFile, JsonSerializer.Serialize(root, Json));
    }

    /// <summary>
    /// A result written that no claimed job played, as the trunk's own (<c>results/trunk.json</c>): the flags it saw count in the ledger.
    /// </summary>
    public void WriteResult(CoverResult result) =>
        WriteAtomic(Path.Combine(Results, result.Job + ".json"), JsonSerializer.Serialize(result, Json));

    public CoverRoot ReadRoot() => JsonSerializer.Deserialize<CoverRoot>(File.ReadAllText(RootFile), Json)
        ?? throw new InvalidDataException($"{RootFile} is empty.");

    /// <summary>Adds a pending job, unless one of its name is already anywhere in the queue.</summary>
    public bool Add(CoverJob job)
    {
        if (Find(job.Id) is not null)
            return false;
        WriteAtomic(Path.Combine(Jobs(JobStatus.Pending), job.Id + ".json"), JsonSerializer.Serialize(job, Json));
        return true;
    }

    /// <summary>Where a job is, or null if nowhere.</summary>
    public JobStatus? Find(string id)
    {
        if (File.Exists(Path.Combine(Jobs(JobStatus.Pending), id + ".json")))
            return JobStatus.Pending;
        if (File.Exists(Path.Combine(Jobs(JobStatus.Done), id + ".json")))
            return JobStatus.Done;
        return ClaimedFiles().Any(c => c.Id == id) ? JobStatus.Claimed : null;
    }

    /// <summary>Every job with where it is.</summary>
    public IEnumerable<(CoverJob Job, JobStatus Status, Claimant? Claim)> All()
    {
        foreach (var status in new[] { JobStatus.Pending, JobStatus.Done })
        {
            foreach (var file in Files(Jobs(status)))
            {
                if (TryRead<CoverJob>(file) is { } job)
                    yield return (job, status, null);
            }
        }
        foreach (var claimed in ClaimedFiles())
        {
            if (TryRead<CoverJob>(claimed.Path) is { } job)
                yield return (job, JobStatus.Claimed, claimed.Claim);
        }
    }

    /// <summary>Who claimed a job: the machine, the process and when it started.</summary>
    public readonly record struct Claimant(string Machine, int Pid, long Started)
    {
        public static Claimant Mine()
        {
            using var me = Process.GetCurrentProcess();
            return new Claimant(Environment.MachineName, me.Id, me.StartTime.ToUniversalTime().Ticks);
        }

        public string Name => $"{Machine}@{Pid.ToString(CultureInfo.InvariantCulture)}@{Started.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>Whether the claimer is still running: on another machine it's taken to be.</summary>
        public bool Alive()
        {
            if (!string.Equals(Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                using var process = Process.GetProcessById(Pid);
                return process.StartTime.ToUniversalTime().Ticks == Started;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return true;
            }
        }
    }

    private IEnumerable<(string Id, Claimant Claim, string Path)> ClaimedFiles()
    {
        foreach (var file in Files(Jobs(JobStatus.Claimed)))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('@');
            if (parts.Length == 4 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                && long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var started))
                yield return (parts[0], new Claimant(parts[1], pid, started), file);
        }
    }

    private string Locks => Path.Combine(Root, "jobs", "locks");

    private string LockOf(string id) => Path.Combine(Locks, id + ".lock");

    /// <summary>
    /// Claims returned to pending whose claimer has died, and how many: the claim moved back, then its lock let go. A lock left by a
    /// claimer that died before it moved its job is let go too.
    /// </summary>
    public int Recover()
    {
        var recovered = 0;
        foreach (var (id, claim, path) in ClaimedFiles().ToList())
        {
            if (claim.Alive())
                continue;
            if (TryMove(path, Path.Combine(Jobs(JobStatus.Pending), id + ".json")))
            {
                recovered++;
                TryDelete(LockOf(id));
            }
        }
        foreach (var file in Directory.Exists(Locks) ? Directory.EnumerateFiles(Locks, "*.lock").ToList() : [])
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (ClaimedFiles().Any(c => c.Id == id) || ReadShared(file) is not { } text || ParseClaimant(text) is not { } holder || holder.Alive())
                continue;
            TryDelete(file);
        }
        return recovered;
    }

    private static Claimant? ParseClaimant(string name)
    {
        var parts = name.Trim().Split('@');
        return parts.Length == 3 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var started)
            ? new Claimant(parts[0], pid, started) : null;
    }

    /// <summary>
    /// Claims the next pending job for <paramref name="claimant"/> (this process by default), the shallowest first, then the one with
    /// the most targets (so the trunk's biggest jobs, the longest, start first), then by name: null if none is left. Dead claims go back to pending first. A claim is the job's lock made new (<see cref="FileMode.CreateNew"/>, which
    /// only one maker wins), then the job moved into <c>claimed</c> under the claimer's name.
    /// </summary>
    public CoverJob? Claim(Claimant? claimant = null)
    {
        var me = claimant ?? Claimant.Mine();
        Recover();
        Directory.CreateDirectory(Locks);
        var pending = Files(Jobs(JobStatus.Pending))
            .Select(f => (File: f, Job: TryRead<CoverJob>(f)))
            .Where(p => p.Job is not null)
            .OrderBy(p => p.Job!.Depth).ThenByDescending(p => p.Job!.Targets.Count).ThenBy(p => p.Job!.Id, StringComparer.Ordinal)
            .ToList();
        foreach (var (file, job) in pending)
        {
            try
            {
                using var held = new FileStream(LockOf(job!.Id), FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
                held.Write(System.Text.Encoding.UTF8.GetBytes(me.Name));
            }
            catch (IOException)
            {
                continue;
            }
            // Ours, unless another claim finished it between our listing and our lock: then its pending file is gone.
            if (TryMove(file, Path.Combine(Jobs(JobStatus.Claimed), $"{job.Id}@{me.Name}.json")))
                return job;
            TryDelete(LockOf(job.Id));
        }
        return null;
    }

    /// <summary>
    /// A claimed job finished: its result written, the jobs it made added as pending, and then it moves to done and its lock is let go. A
    /// process that dies before the last leaves the job claimed, to be played again; the result and the jobs it made are written over.
    /// </summary>
    public void Complete(CoverJob job, CoverResult result, IEnumerable<CoverJob> made, Claimant? claimant = null)
    {
        var me = claimant ?? Claimant.Mine();
        Directory.CreateDirectory(Results);
        WriteAtomic(Path.Combine(Results, job.Id + ".json"), JsonSerializer.Serialize(result, Json));
        foreach (var next in made)
            Add(next);
        var claimed = Path.Combine(Jobs(JobStatus.Claimed), $"{job.Id}@{me.Name}.json");
        var done = Path.Combine(Jobs(JobStatus.Done), job.Id + ".json");
        if (!TryMove(claimed, done))
            WriteAtomic(done, JsonSerializer.Serialize(job, Json));
        TryDelete(LockOf(job.Id));
    }

    /// <summary>Gives a claimed job back to pending unplayed, as a process stopping before it began does.</summary>
    public void Release(CoverJob job, Claimant? claimant = null)
    {
        var me = claimant ?? Claimant.Mine();
        if (TryMove(Path.Combine(Jobs(JobStatus.Claimed), $"{job.Id}@{me.Name}.json"), Path.Combine(Jobs(JobStatus.Pending), job.Id + ".json")))
            TryDelete(LockOf(job.Id));
    }

    /// <summary>Every job's result, oldest first.</summary>
    public List<CoverResult> ReadResults() =>
        [.. Files(Results).Select(TryRead<CoverResult>).OfType<CoverResult>().OrderBy(r => r.Finished)];

    /// <summary>The ledger: every flag any job has seen set, with the first sighting of each (by when the job finished).</summary>
    public Dictionary<string, (string Job, CoverSeenEntry Seen)> Ledger()
    {
        var ledger = new Dictionary<string, (string, CoverSeenEntry)>();
        foreach (var result in ReadResults())
        {
            foreach (var seen in result.Seen)
                ledger.TryAdd(seen.Flag, (result.Job, seen));
        }
        return ledger;
    }

    /// <summary>A save's path as a job's record of it: relative to the root where it's under it.</summary>
    public string Relative(string path)
    {
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Root, full);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? full : relative.Replace('\\', '/');
    }

    /// <summary>A job's save as a path to read.</summary>
    public string Resolve(string save) => Path.IsPathRooted(save) ? save : Path.Combine(Root, save);

    private static IEnumerable<string> Files(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.json") : [];

    private static T? TryRead<T>(string file) where T : class
    {
        try
        {
            return ReadShared(file) is { } text ? JsonSerializer.Deserialize<T>(text, Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A file's text, read so that another process may rename or delete it meanwhile: a rename that met a reader's handle without
    /// <see cref="FileShare.Delete"/> falls back to copying (<see cref="File.Move(string, string)"/> lets it), which would leave a job in
    /// two places. Null if it's gone.
    /// </summary>
    private static string? ReadShared(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Writes a file whole or not at all: to a file beside it, renamed over it.</summary>
    private static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
