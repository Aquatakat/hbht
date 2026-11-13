using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>Where a party job starts: a trunk save, the members to have in the party first and those to keep out, and how far that is.</summary>
/// <param name="Step">The trunk step the save is before.</param>
/// <param name="Members">The members the target needs in the party from there (<see cref="TrunkLook.Needs"/>).</param>
/// <param name="Far">
/// The target's distance from the save, each member's distance to join (0 for one in the party), and each member to keep out's distance
/// to leave (0 for one out of it).
/// </param>
public sealed record PartyStart(int Step, IReadOnlyList<RecordId> Members, float Far)
{
    /// <summary>The members the target needs out of the party from there, and kept out (<see cref="TrunkLook.Out"/>).</summary>
    public IReadOnlyList<RecordId> Out { get; init; } = [];

    /// <summary>The save and the members, as <c>trunk/party.json</c> keeps what each target was tried from.</summary>
    public string Key => $"{Step}:{string.Join("_", Members.Select(m => m.Key))}{(Out.Count > 0 ? $":out {string.Join("_", Out.Select(m => m.Key))}" : "")}";

    /// <summary>The party it gets first.</summary>
    public PartyGoal Goal => new(Members, Out);

    public bool Equals(PartyStart? other) => other is not null && Step == other.Step && Members.SequenceEqual(other.Members) && Out.SequenceEqual(other.Out);

    public override int GetHashCode() => Out.Aggregate(Members.Aggregate(Step, HashCode.Combine), HashCode.Combine);
}

/// <summary>
/// Party jobs (docs/systems/playthrough.md, "Party jobs"): once a trunk tree is done, the targets still uncovered that need someone in the
/// party from a trunk save get a job from the save best for them, which first gets that party (<see cref="CoverGoal.Party"/>), then
/// sweeps its targets as any cover job does.
/// </summary>
public static class PartyJobs
{
    /// <summary>The most members a target may need: the party holds four, Alaron always among them.</summary>
    public const int MostMembers = HBHT.Core.Characters.Party.MaxSize - 1;

    /// <summary>
    /// The trunk saves a target may start a party job from, best first: those it's live from and needs someone in the party from (no more
    /// than <see cref="MostMembers"/>, each able to join there) or someone out of it (each in the party able to leave there), each weighed
    /// by the target's distance and the members' distances to join or leave. A member it needs both in and out of it, as Godric's agreeing
    /// to join (73) needs him out until the conversation that sets it brings him in, is kept out, but for the steps that set the target.
    /// They're ordered as <see cref="Trunk.Choose"/> picks, again and again: the nearest, and of those within its slack the latest, so the
    /// party starts as far on in the story as nearness allows. Starts already tried are left out.
    /// </summary>
    /// <param name="brought">
    /// By each save's step, the members the trunk brings into the party after it (<see cref="Brought"/>): a target that needs only them
    /// has no start there.
    /// </param>
    public static List<PartyStart> Starts(RecordId target, IEnumerable<TrunkLook> looks, IReadOnlySet<string>? tried = null,
        IReadOnlyDictionary<int, HashSet<RecordId>>? brought = null)
    {
        var options = new List<PartyStart>();
        foreach (var look in looks)
        {
            if (!look.Live.TryGetValue(target, out var far))
                continue;
            var outs = look.Out.GetValueOrDefault(target) ?? [];
            var members = (look.Needs.GetValueOrDefault(target) ?? []).Where(m => !outs.Contains(m)).ToList();
            if (members.Count == 0 && outs.Count == 0)
                continue;
            // Needing only members the trunk brings in itself is the story's own order, not a party: no start here. Needing one it
            // doesn't, the party is all of them: Abrecan and Becan together for 416 at Overworld O17/1 (0x0474), though the trunk
            // brings Abrecan in at the castle. Needing someone out, it's a party whatever the trunk does: the trunk takes them in.
            var later = brought?.GetValueOrDefault(look.Step);
            if (members.Count > MostMembers || outs.Count == 0 && later is not null && members.All(later.Contains))
                continue;
            float? joins = 0;
            foreach (var m in members)
                joins = look.Joins.TryGetValue(m, out var j) ? joins + j : null;
            foreach (var m in outs.Where(look.Party.Contains))
                joins = look.Leaves.TryGetValue(m, out var l) ? joins + l : null;
            if (joins is not { } toJoin)
                continue;
            var start = new PartyStart(look.Step, members, far + toJoin) { Out = outs };
            if (tried?.Contains(start.Key) != true)
                options.Add(start);
        }
        var ordered = new List<PartyStart>();
        while (options.Count > 0)
        {
            var step = Trunk.Choose(options.Select(o => (o.Step, o.Far)))!.Value;
            var best = options.First(o => o.Step == step);
            ordered.Add(best);
            options.Remove(best);
        }
        return ordered;
    }

    /// <summary>
    /// By each trunk save's step, the members out of the party there whom the trunk itself brings in later. The story's own joins aren't
    /// what a party job is for: from before Rheda joins, most of the targets still uncovered need her, as the story does, and a cover job's
    /// planner brings her in as the trunk did, with a slot free or by a conversation that asks who leaves.
    /// </summary>
    public static Dictionary<int, HashSet<RecordId>> Brought(TrunkManifest manifest)
    {
        var saves = manifest.Saves.OrderBy(s => s.Step).ToList();
        var brought = new Dictionary<int, HashSet<RecordId>>();
        var later = new HashSet<RecordId>();
        for (var i = saves.Count - 1; i >= 0; i--)
        {
            var party = saves[i].Party.Select(m => RecordId.Parse(m.Entity)).ToHashSet();
            brought[saves[i].Step] = [.. later.Where(m => !party.Contains(m))];
            later.UnionWith(party);
        }
        return brought;
    }

    /// <summary>
    /// A party job's name: <c>p&lt;round&gt;-t&lt;step&gt;-&lt;members&gt;</c>, the members by their hex numbers, and those it keeps
    /// out after <c>out-</c>: <c>p1-t006-out-0248_024C</c>.
    /// </summary>
    public static string JobId(int round, PartyStart start)
    {
        static string Hex(IEnumerable<RecordId> members) =>
            string.Join("_", members.Select(m => m.Key.StartsWith("0x", StringComparison.Ordinal) ? m.Key[2..] : m.Key));
        var id = $"p{round}-t{start.Step:000}";
        if (start.Members.Count > 0)
            id += $"-{Hex(start.Members)}";
        if (start.Out.Count > 0)
            id += $"-out-{Hex(start.Out)}";
        return id;
    }

    /// <summary>A party round's jobs: one for each start given targets, at depth 0, with the party it gets first.</summary>
    /// <param name="folder">The trunk's folder, relative to the tree's root.</param>
    public static List<CoverJob> Jobs(TrunkManifest manifest, IReadOnlyDictionary<RecordId, PartyStart> chosen, string folder, int round)
    {
        var saves = manifest.Saves.ToDictionary(s => s.Step);
        return [.. chosen.GroupBy(c => c.Value).OrderBy(g => g.Key.Step).ThenBy(g => g.Key.Key, StringComparer.Ordinal).Select(g => new CoverJob(
            JobId(round, g.Key), null, $"{folder}/{saves[g.Key.Step].Save}", [.. g.Select(c => c.Key.ToString()).Order(StringComparer.Ordinal)], 0,
            g.Key.Step, saves[g.Key.Step].Action, $"party {round}: the targets need {g.Key.Goal.Describe()}",
            Party: [.. g.Key.Members.Select(m => m.ToString())], Out: g.Key.Out.Count > 0 ? [.. g.Key.Out.Select(m => m.ToString())] : null))];
    }

    /// <summary>
    /// The plan from a game to the party, as a job would plan it: on a model of its own, with the world graph's places from where the
    /// player stands, within <paramref name="budget"/> states, never leaving one of the members out nor bringing in one it keeps out
    /// (<see cref="Banned"/>). With it, the targets of those given that the relaxation, without what the job never takes, still sets once
    /// the plan's steps are taken (with the places as they stand in the game): a plan that gets the party by a way that loses a target is
    /// no start for it. The relaxation can't see every loss this way: from Gwernia castle the plan to Godric goes through Erromon's
    /// 7/E03/1 (0x03C3), and in the game that step loses 73, which his castle conversation sets.
    /// </summary>
    public static (Plan Plan, IReadOnlySet<RecordId> Keeps) PlanParty(QuestModel shared, WorldGraph graph, GameState game,
        PartyGoal party, IReadOnlyCollection<RecordId> targets, int budget = 4000)
    {
        var model = shared.ForRun();
        var state = PlanState.From(model, game);
        WorldReach.Layered(model, graph, game, state, graph.PlaceOf(game));
        var banned = Banned(model, party, targets);
        var plan = new QuestPlanner(model).Plan(state, party, budget, banned);
        if (!plan.Found)
            return (plan, new HashSet<RecordId>());
        foreach (var step in plan.Steps)
            state = state.Apply(step);
        var result = new Relaxation(model, Enumerable.Range(0, model.Actions.Count).Where(a => !banned.Contains(a))).Run(state);
        return (plan, targets.Where(t => model.Flags.TryHandle(t, out var h) && result.Reached(model.FlagFact(h, true))).ToHashSet());
    }

    /// <summary>What a party job never takes: the steps that leave one of its members out, and those that bring one it keeps out in.</summary>
    public static HashSet<int> Banned(QuestModel model, PartyGoal party, IEnumerable<RecordId> targets)
    {
        var banned = Leaving(model, party.Members);
        banned.UnionWith(Joining(model, party.Without, targets));
        return banned;
    }

    /// <summary>
    /// The actions that bring one of the members kept out into the party, even for a moment (<see cref="PartyNeeds.Takes"/>), but for
    /// those that set one of the targets: a party job keeping Godric out takes his castle conversation all the same, which sets 73 as he
    /// joins.
    /// </summary>
    public static HashSet<int> Joining(QuestModel model, IEnumerable<RecordId> members, IEnumerable<RecordId> targets)
    {
        var taking = PartyNeeds.Takes(model, members);
        var facts = targets.Where(t => model.Flags.TryHandle(t, out _)).Select(t => model.FlagFact(model.Flags.Handle(t), true)).ToHashSet();
        taking.RemoveWhere(a => model.Adds(a).Any(facts.Contains));
        return taking;
    }

    /// <summary>
    /// The actions that leave one of the members out of the party: their last word on the member is a <see cref="Leave"/>. A party job
    /// never takes them, so the party it made stays made.
    /// </summary>
    public static HashSet<int> Leaving(QuestModel model, IEnumerable<RecordId> members) => PartyNeeds.Leaves(model, members);
}
