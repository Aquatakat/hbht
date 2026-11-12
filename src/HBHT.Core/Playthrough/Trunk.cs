using System.Diagnostics;
using System.Text.Json;
using HBHT.Core.Flags;
using HBHT.Plugins;
using HBHT.Plugins.Saves;

namespace HBHT.Core.Playthrough;

/// <summary>A trunk save: the game before one of the story run's steps, with the player at the controls.</summary>
/// <param name="Step">The run's step it was made before, from 1.</param>
/// <param name="Action">That step's action, described.</param>
/// <param name="Tick">The host's tick.</param>
/// <param name="Where">Where the player stood.</param>
/// <param name="Party">The party's levels.</param>
/// <param name="Save">The save, relative to the trunk's folder (absolute while the run that made it is still playing).</param>
public sealed record TrunkSave(int Step, string Action, long Tick, Spot? Where, IReadOnlyList<MemberLevel> Party, string Save);

/// <summary>
/// The trunk's manifest (<c>trunk.json</c>): how the story run went, its saves before every step, and every flag it saw set first, by
/// step, with the save before that step.
/// </summary>
public sealed record TrunkManifest(
    string Goal,
    bool Reached,
    string? Why,
    int Steps,
    long Ticks,
    double WallSeconds,
    IReadOnlyList<TrunkSave> Saves,
    IReadOnlyList<CoverSeenEntry> Seen,
    DateTimeOffset Made);

/// <summary>Which targets are live from one trunk save, each with how far its nearest setter is from the player (<see cref="Trunk.Look"/>).</summary>
/// <param name="Step">The trunk step the save is before; 0 for the trunk's own start.</param>
/// <param name="Live">The live targets, each with the distance to its nearest setter.</param>
/// <param name="Rounds">The rounds of floods the places' reach took.</param>
public sealed record TrunkLook(int Step, IReadOnlyDictionary<RecordId, float> Live, int Rounds, double Seconds)
{
    /// <summary>Who's in the party there.</summary>
    public IReadOnlyList<RecordId> Party { get; init; } = [];

    /// <summary>
    /// The members each of the party targets asked about needs from there (<see cref="PartyNeeds.Needs"/>): out of the party and never
    /// joining, the relaxation no longer sets it. Only targets that need someone are listed.
    /// </summary>
    public IReadOnlyDictionary<RecordId, IReadOnlyList<RecordId>> Needs { get; init; } = new Dictionary<RecordId, IReadOnlyList<RecordId>>();

    /// <summary>How far each member who can join is from there: 0 for one in the party, else the nearest joining action the relaxation gets to.</summary>
    public IReadOnlyDictionary<RecordId, float> Joins { get; init; } = new Dictionary<RecordId, float>();
}

/// <summary>Where each target goes (<see cref="Trunk.Assign"/>).</summary>
/// <param name="Chosen">Each target given a trunk save, by the save's step.</param>
/// <param name="FromStart">Targets live from the trunk's start but from none of its saves: a job from the start plays for them.</param>
/// <param name="Nowhere">Targets live from nowhere the trunk stood, which no job is made for.</param>
/// <param name="Covered">Targets the trunk itself saw set.</param>
public sealed record TrunkAssignment(
    IReadOnlyDictionary<RecordId, int> Chosen,
    IReadOnlyList<RecordId> FromStart,
    IReadOnlyList<RecordId> Nowhere,
    IReadOnlyList<RecordId> Covered)
{
    /// <summary>The targets given to each step's save, by step.</summary>
    public SortedDictionary<int, List<RecordId>> ByStep()
    {
        var by = new SortedDictionary<int, List<RecordId>>();
        foreach (var (target, step) in Chosen)
        {
            if (!by.TryGetValue(step, out var list))
                by[step] = list = [];
            list.Add(target);
        }
        foreach (var list in by.Values)
            list.Sort((a, b) => string.CompareOrdinal(a.ToString(), b.ToString()));
        return by;
    }
}

/// <summary>
/// The trunk of flag coverage (docs/systems/playthrough.md, "Flag coverage"): the story run to the ending with a save before every step,
/// the targets live from each save, and each target given to the save it's best started from, so that the jobs start at the story's own
/// pace and level, all at once.
/// </summary>
public static class Trunk
{
    public const string ManifestFile = "trunk.json";

    /// <summary>How much further than the nearest a save may leave its target and still be chosen for being later: 50 units of distance…</summary>
    public const float Slack = 50;

    /// <summary>…or half as far again as the nearest, whichever is more.</summary>
    public const float Ratio = 1.5f;

    /// <summary>
    /// How much farther a target counts for each step the relaxation needs before the one that sets it, past the first (its fact's layer
    /// less 1): a <see cref="Slack"/>'s worth. Sheridan's 29 (Gwernia castle A07/2, 0x0375) is a conversation away before Brenna joins, and
    /// after Rheda joins only behind someone leaving at Erromon, which closes it: by distance alone the later saves won, from which no
    /// plan reached it.
    /// </summary>
    public const float LayerDistance = 50;

    /// <summary>
    /// Plays the trunk from the host's game to <paramref name="goal"/> (the ending), with a save before every step and a ledger on the host,
    /// and writes its saves, its report and its manifest to <paramref name="folder"/>.
    /// </summary>
    public static TrunkManifest Run(PlaythroughHost host, QuestModel model, WorldGraph graph, string folder, Goal? goal = null,
        RunOptions? options = null, Action<string>? progress = null)
    {
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        host.Ledger ??= new FlagLedger(model.Flags.Count);
        var run = new Playthrough(host, model, graph, goal ?? new EndingGoal(),
            (options ?? RunOptions.Default) with { Saves = Path.Combine(folder, "saves"), SaveEveryStep = true, WriteSaves = true })
        {
            Progress = progress,
        };
        var report = run.Run();
        report.Write(Path.Combine(folder, "report"));
        var summary = report.Summary;
        string Relative(string path) => Path.GetRelativePath(folder, path).Replace('\\', '/');
        var manifest = new TrunkManifest(
            run.Goal.Describe(),
            summary?.Reached == true,
            summary?.Reached == true ? null : summary?.Why,
            summary?.Steps ?? 0,
            summary?.Ticks ?? 0,
            summary?.WallSeconds ?? 0,
            [.. run.StepSaves.Select(s => s with { Save = Relative(s.Save) })],
            [.. run.StepSeen.Select(x => new CoverSeenEntry(x.Flag.ToString(), x.Step, x.Tick, x.Save is null ? null : Relative(x.Save)))],
            DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(folder, ManifestFile), JsonSerializer.Serialize(manifest, CoverQueue.Json));
        return manifest;
    }

    /// <summary>A trunk's manifest from its folder.</summary>
    public static TrunkManifest Read(string folder) =>
        JsonSerializer.Deserialize<TrunkManifest>(File.ReadAllText(Path.Combine(folder, ManifestFile)), CoverQueue.Json)
        ?? throw new InvalidDataException($"{Path.Combine(folder, ManifestFile)} is empty.");

    /// <summary>
    /// The targets live from each of the games given, side by side on <paramref name="threads"/> threads: the look a cover run makes before
    /// each step (<see cref="PlanState.From"/>, the places' reach in rounds, a relaxation over every action), each on a model of its own
    /// and the shared world graph. A target is live when the relaxation sets it and it isn't set already. Its distance is from where the
    /// player stands to the nearest action that sets it and that the relaxation gets to, by the cover run's measure
    /// (<see cref="Playthrough.Distance"/>); for a derived target, the nearest action of its relaxed plan that writes one of its inputs.
    /// </summary>
    /// <param name="games">Each game's step (0 for the start) and how to load it.</param>
    /// <param name="party">
    /// Targets to weigh the members for (<see cref="TrunkLook.Needs"/>, <see cref="TrunkLook.Joins"/>), for party jobs; none if null.
    /// </param>
    public static List<TrunkLook> Look(QuestModel model, WorldGraph graph, IReadOnlyList<(int Step, Func<GameState> Load)> games,
        IReadOnlyCollection<RecordId> targets, int threads = 1, Action<string>? progress = null, IReadOnlyCollection<RecordId>? party = null)
    {
        var plugins = model.Plugins;
        // Every action's place, and the plain setters of each flag, once for every look.
        var positions = model.Actions.Select(a => a.Place is { } p ? Playthrough.PositionOf(plugins, p) : null).ToArray();
        var setters = new List<int>?[model.Flags.Count];
        foreach (var action in model.Actions)
        {
            foreach (var f in model.Adds(action.Index))
            {
                if (f < 2 * model.Flags.Count && f % 2 == 0)
                    (setters[f / 2] ??= []).Add(action.Index);
            }
        }
        var handles = targets.Where(t => model.Flags.TryHandle(t, out _)).Select(t => (Id: t, Handle: model.Flags.Handle(t))).ToList();
        var looks = new TrunkLook?[games.Count];
        var errors = new List<Exception>();
        var next = -1;
        void Work()
        {
            int i;
            while ((i = Interlocked.Increment(ref next)) < games.Count)
            {
                try
                {
                    looks[i] = LookAt(model, graph, games[i].Step, games[i].Load(), handles, positions, setters, party);
                    progress?.Invoke($"look before step {games[i].Step}: {looks[i]!.Live.Count} live, {looks[i]!.Rounds} rounds, {looks[i]!.Seconds:0.0} s");
                }
                catch (Exception e)
                {
                    lock (errors)
                        errors.Add(new InvalidOperationException($"the look before step {games[i].Step} failed: {e.Message}", e));
                }
            }
        }
        var workers = Enumerable.Range(0, Math.Max(1, Math.Min(threads, games.Count))).Select(_ => new Thread(Work, 64 * 1024 * 1024)).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        if (errors.Count > 0)
            throw new AggregateException(errors);
        return [.. looks.Select(l => l!)];
    }

    private static TrunkLook LookAt(QuestModel shared, WorldGraph graph, int step, GameState game, List<(RecordId Id, int Handle)> targets,
        Place?[] positions, List<int>?[] setters, IReadOnlyCollection<RecordId>? party)
    {
        var clock = Stopwatch.StartNew();
        var model = shared.ForRun();
        var state = PlanState.From(model, game);
        var here = graph.PlaceOf(game);
        var reach = WorldReach.Layered(model, graph, game, state, here);
        var relaxation = new Relaxation(model, Enumerable.Range(0, model.Actions.Count));
        var result = relaxation.Run(state);
        var live = new Dictionary<RecordId, float>();
        foreach (var (id, h) in targets)
        {
            var fact = model.FlagFact(h, true);
            if (!result.Reached(fact) || LossCheck.SetFor(state, h) || game.Flags.Get(h))
                continue;
            IEnumerable<int> by;
            if (model.Flags.Kind(h) == FlagKind.Derived)
            {
                var inputs = PlainInputs(model.Flags, h);
                var plan = relaxation.Extract(result, [fact])?.Plan ?? [];
                by = plan.Where(a => model.Adds(a).Any(f => f < 2 * model.Flags.Count && inputs.Contains(f / 2)));
            }
            else
            {
                by = setters[h] ?? [];
            }
            var far = float.MaxValue;
            foreach (var a in by)
            {
                if (result.ActionLayer[a] >= 0)
                    far = Math.Min(far, Playthrough.Distance(positions[a], here));
            }
            // None that the relaxation gets to has a place (the opening, a battle the story starts): no distance to tell them by.
            // Each step the relaxation needs before it counts as LayerDistance more: a save from which it's a conversation away is
            // nearer than one from which it waits on a member leaving or the story moving on.
            live[id] = (far == float.MaxValue ? 0 : far) + LayerDistance * Math.Max(0, result.FactLayer[fact] - 1);
        }
        if (party is null)
            return new TrunkLook(step, live, reach.Rounds, clock.Elapsed.TotalSeconds) { Party = state.Party };

        // The members the party targets live from here need, and how far each member's nearest joining action is.
        var asked = party.Where(live.ContainsKey).Select(model.Flags.Handle).ToList();
        var needs = PartyNeeds.ByTarget(PartyNeeds.Needs(model, state, asked))
            .ToDictionary(p => model.Flags.Id(p.Key), p => (IReadOnlyList<RecordId>)p.Value);
        var joins = new Dictionary<RecordId, float>();
        foreach (var member in model.Members.Keys)
        {
            if (state.InParty(member))
            {
                joins[member] = 0;
                continue;
            }
            var far = float.MaxValue;
            foreach (var a in PartyNeeds.Joins(model, member))
            {
                if (result.ActionLayer[a] >= 0)
                    far = Math.Min(far, Playthrough.Distance(positions[a], here));
            }
            if (far < float.MaxValue)
                joins[member] = far;
        }
        return new TrunkLook(step, live, reach.Rounds, clock.Elapsed.TotalSeconds) { Party = state.Party, Needs = needs, Joins = joins };
    }

    /// <summary>The plain flags a derived one is made from, through any derived flags between.</summary>
    private static HashSet<int> PlainInputs(FlagModel flags, int derived)
    {
        var inputs = new HashSet<int>();
        var queue = new Queue<int>([derived]);
        var seen = new HashSet<int> { derived };
        while (queue.TryDequeue(out var d))
        {
            foreach (var i in flags.Inputs(d))
            {
                if (flags.Kind(i) == FlagKind.Derived)
                {
                    if (seen.Add(i))
                        queue.Enqueue(i);
                }
                else
                {
                    inputs.Add(i);
                }
            }
        }
        return inputs;
    }

    /// <summary>
    /// The save a target is given to, of those in its window (the saves it's live from, by step and distance): the nearest, and of the
    /// saves within <see cref="Slack"/> or <see cref="Ratio"/> of the nearest, whichever is more, the latest, so it's started from near
    /// where it is with the party as far on in the story as that allows. Null for an empty window.
    /// </summary>
    public static int? Choose(IEnumerable<(int Step, float Far)> window, float slack = Slack, float ratio = Ratio)
    {
        var list = window.ToList();
        if (list.Count == 0)
            return null;
        var nearest = list.Min(w => w.Far);
        var within = Math.Max(nearest + slack, nearest * ratio);
        return list.Where(w => w.Far <= within).Max(w => w.Step);
    }

    /// <summary>
    /// Each target given to one trunk save in its window (<see cref="Choose"/>). A target the trunk saw set is covered and given to none; one
    /// live from the start but from no save goes to a job from the start; one live from nowhere goes nowhere.
    /// </summary>
    /// <param name="saves">The looks from the trunk's saves.</param>
    /// <param name="start">The look from the trunk's start (a new game), or null.</param>
    public static TrunkAssignment Assign(IReadOnlyList<RecordId> targets, IReadOnlyList<TrunkLook> saves, TrunkLook? start,
        IReadOnlySet<RecordId> covered, float slack = Slack, float ratio = Ratio)
    {
        var chosen = new Dictionary<RecordId, int>();
        var fromStart = new List<RecordId>();
        var nowhere = new List<RecordId>();
        var coveredList = new List<RecordId>();
        foreach (var target in targets)
        {
            if (covered.Contains(target))
            {
                coveredList.Add(target);
                continue;
            }
            var window = saves.Where(s => s.Live.ContainsKey(target)).Select(s => (s.Step, s.Live[target]));
            if (Choose(window, slack, ratio) is { } step)
                chosen[target] = step;
            else if (start?.Live.ContainsKey(target) == true)
                fromStart.Add(target);
            else
                nowhere.Add(target);
        }
        return new TrunkAssignment(chosen, fromStart, nowhere, coveredList);
    }

    /// <summary>
    /// A job for each trunk save given targets, named by its step (<c>t007</c>), from that save; and <c>t000</c>, from the trunk's start, for
    /// the targets live only from there. All are roots of the tree, at depth 0.
    /// </summary>
    /// <param name="folder">The trunk's folder, relative to the tree's root.</param>
    /// <param name="start">The trunk's start's save, relative to the tree's root; a new game if null.</param>
    public static List<CoverJob> Jobs(TrunkManifest manifest, TrunkAssignment assignment, string folder, string? start)
    {
        var saves = manifest.Saves.ToDictionary(s => s.Step);
        var jobs = assignment.ByStep().Select(p => new CoverJob($"t{p.Key:000}", null, $"{folder}/{saves[p.Key].Save}",
            [.. p.Value.Select(t => t.ToString())], 0, p.Key, saves[p.Key].Action, $"the trunk's save before step {p.Key}")).ToList();
        if (assignment.FromStart.Count > 0)
            jobs.Add(new CoverJob("t000", null, start, [.. assignment.FromStart.Select(t => t.ToString())], 0,
                Why: "live from the trunk's start, but from none of its saves"));
        return jobs;
    }

    /// <summary>
    /// The trunk save to try a given-up target from again, of those in its window not tried yet: in odd rounds the latest, where the
    /// party is furthest on; in even rounds the nearest, the latest of equals. Another start brings another party, place and story, which
    /// gets past most of what gave a target up where it was first tried: a branch that had no plan from its save, a fight lost again, a
    /// loss on the way. Null when every save in its window has been tried.
    /// </summary>
    public static int? Retry(IEnumerable<(int Step, float Far)> window, IReadOnlySet<int> tried, int round)
    {
        var left = window.Where(w => !tried.Contains(w.Step)).ToList();
        if (left.Count == 0)
            return null;
        return round % 2 == 1 ? left.Max(w => w.Step) : left.OrderBy(w => w.Far).ThenByDescending(w => w.Step).First().Step;
    }

    /// <summary>A retry round's jobs (<see cref="Retry"/>): one for each trunk save given targets, <c>r&lt;round&gt;-t&lt;step&gt;</c>, at depth 0.</summary>
    /// <param name="folder">The trunk's folder, relative to the tree's root.</param>
    public static List<CoverJob> RetryJobs(TrunkManifest manifest, IReadOnlyDictionary<RecordId, int> chosen, string folder, int round)
    {
        var saves = manifest.Saves.ToDictionary(s => s.Step);
        return [.. chosen.GroupBy(c => c.Value).OrderBy(g => g.Key).Select(g => new CoverJob($"r{round}-t{g.Key:000}", null,
            $"{folder}/{saves[g.Key].Save}", [.. g.Select(c => c.Key.ToString()).Order(StringComparer.Ordinal)], 0, g.Key, saves[g.Key].Action,
            $"retry {round}: given up from the trunk saves tried before"))];
    }

    /// <summary>
    /// The trunk's own result in the tree (<c>results/trunk.json</c>): every flag it saw set, with the save before the step that set it,
    /// so the ledger counts them as covered; and its targets among them.
    /// </summary>
    /// <param name="folder">The trunk's folder, relative to the tree's root.</param>
    public static CoverResult Result(TrunkManifest manifest, TrunkAssignment assignment, string folder, IEnumerable<CoverJob> made)
    {
        var first = manifest.Seen.GroupBy(x => x.Flag).ToDictionary(g => g.Key, g => g.First());
        return new CoverResult("trunk", manifest.Reached ? "the trunk reached its goal" : $"the trunk stopped: {manifest.Why}",
            manifest.Steps, manifest.Ticks, manifest.WallSeconds,
            [.. manifest.Seen.Select(x => x with { Save = x.Save is null ? null : $"{folder}/{x.Save}" })],
            [.. assignment.Covered.Select(t => new CoverTargetEntry(t.ToString(), TargetState.Covered, $"set by the trunk at step {first[t.ToString()].Step}"))],
            [.. made.Select(j => j.Id)], DateTimeOffset.UtcNow);
    }
}
