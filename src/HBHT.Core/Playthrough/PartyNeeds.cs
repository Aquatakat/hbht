using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>
/// The targets that need someone in the party (docs/systems/playthrough.md, "Party jobs"). The relaxation lets anyone join whenever
/// their joining action can be done, as if the party had no cap, so a target only someone outside the party leads to looks live from a
/// full party. Each member's part is measured by taking something away and running the relaxation again: the member, out of the party
/// with their joining actions left out (gone for good); the actions that take them out (kept for good); or the actions that need them in
/// the party then (<see cref="Present"/>).
/// </summary>
public static class PartyNeeds
{
    /// <summary>The actions that bring a member into the party: those whose effects make their person fact true.</summary>
    public static IReadOnlyList<int> Joins(QuestModel model, RecordId member) =>
        model.PersonFact(member, true) is not { } fact ? []
        : [.. model.Actions.Where(a => model.Adds(a.Index).Contains(fact)).Select(a => a.Index)];

    /// <summary>The actions that need a member in the party as they're done: their person fact among their preconditions.</summary>
    public static IReadOnlyList<int> NeedingIn(QuestModel model, RecordId member) =>
        model.PersonFact(member, true) is not { } fact ? []
        : [.. model.Actions.Where(a => model.Pre(a.Index).Contains(fact)).Select(a => a.Index)];

    /// <summary>The actions that leave one of the members out of the party: their last word on one of them is a <see cref="Leave"/>.</summary>
    public static HashSet<int> Leaves(QuestModel model, IEnumerable<RecordId> members)
    {
        var set = members.ToHashSet();
        var leaving = new HashSet<int>();
        foreach (var action in model.Actions)
        {
            var last = new Dictionary<RecordId, bool>();
            foreach (var effect in action.Effects)
            {
                if (effect is Join j && set.Contains(j.Entity))
                    last[j.Entity] = true;
                else if (effect is Leave l && set.Contains(l.Entity))
                    last[l.Entity] = false;
            }
            if (last.ContainsValue(false))
                leaving.Add(action.Index);
        }
        return leaving;
    }

    /// <summary>
    /// For each member who can join (<see cref="QuestModel.Members"/>), the targets of those given (by flag handle) that need them in the
    /// party: the relaxation from the state sets them, and doesn't once the member is gone for good (out of the party, their joining
    /// actions left out), though it does with the member kept for good. Those it loses either way need the member to leave, as Arturo's
    /// conversation on the bridge (0x0402 at Overworld D14/6) needs him gone, his joined flag clear, and aren't theirs. Members who lose
    /// nothing are left out.
    /// </summary>
    public static Dictionary<RecordId, HashSet<int>> Needs(QuestModel model, PlanState state, IEnumerable<int> targets)
    {
        var list = targets.ToList();
        var gone = Without(model, state, list, m => Joins(model, m), takeOut: true);
        var kept = Away(model, state, list);
        foreach (var (member, lost) in gone)
        {
            if (kept.TryGetValue(member, out var away))
                lost.ExceptWith(away);
        }
        return gone.Where(g => g.Value.Count > 0).ToDictionary();
    }

    /// <summary>The actions that bring one of the members into the party, even for a moment: any <see cref="Join"/> of one of them.</summary>
    public static HashSet<int> Takes(QuestModel model, IEnumerable<RecordId> members)
    {
        var set = members.ToHashSet();
        return [.. model.Actions.Where(a => a.Effects.Any(e => e is Join j && set.Contains(j.Entity))).Select(a => a.Index)];
    }

    /// <summary>
    /// For each member who can join, the targets of those given (by flag handle) that need them out of the party, and kept out: the
    /// relaxation from the state sets them, and doesn't once the member is in the party for good. For a member in the party, that's with
    /// their leaving actions left out, where taking them out with their joins left out still sets the target: Rheda's talk in Port Saiid
    /// (0x040F, 262) needs her out of the party then (derived 5245 wants her joined flag 91 clear). For a member out of it, it's with them
    /// put in and the whole party kept, every member's leaving actions left out, where the party kept as it is still sets the target: with
    /// Abrecan in, Gwernia castle's conversations before Erromon (Sheridan's 29, Gabrion's 32) need Godric and Rheda kept out, since
    /// derived 5004, at least two of the three in the party, closes them, and the relaxation, which keeps no order, would have one leave
    /// again only at Erromon, after they're closed. Where no one alone loses a target but two together do, as with Alaron alone any two of
    /// the three close them, of those pairs the member in most is kept out until none is left. Members the relaxation never brings in from
    /// the state are left out, and so are those who lose nothing.
    /// </summary>
    public static Dictionary<RecordId, HashSet<int>> Out(QuestModel model, PlanState state, IEnumerable<int> targets)
    {
        var all = Enumerable.Range(0, model.Actions.Count).ToList();
        var full = new Relaxation(model, all).Run(state);
        var reached = targets.Where(h => full.Reached(model.FlagFact(h, true))).ToList();
        var result = new Dictionary<RecordId, HashSet<int>>();
        if (reached.Count == 0)
            return result;
        var partyLeaves = AnyLeave(model, state.Party);
        var kept = new Relaxation(model, all.Where(a => !partyLeaves.Contains(a))).Run(state);
        var keptReached = reached.Where(h => kept.Reached(model.FlagFact(h, true))).ToList();
        var outside = new List<RecordId>();
        foreach (var member in model.Members.Keys.OrderBy(m => m.ToString(), StringComparer.Ordinal))
        {
            HashSet<int> lost;
            if (state.InParty(member))
            {
                var leaves = AnyLeave(model, [member]);
                var away = new Relaxation(model, all.Where(a => !leaves.Contains(a))).Run(state);
                var joins = Joins(model, member).ToHashSet();
                var gone = new Relaxation(model, all.Where(a => !joins.Contains(a))).Run(state.Left(member));
                lost = [.. reached.Where(h => !away.Reached(model.FlagFact(h, true)) && gone.Reached(model.FlagFact(h, true)))];
            }
            else
            {
                if (!Joins(model, member).Any(a => full.ActionLayer[a] >= 0))
                    continue;
                outside.Add(member);
                lost = Forced(model, state, all, keptReached, [member]);
            }
            if (lost.Count > 0)
                result[member] = lost;
        }

        // Two who join together where neither alone loses the target: with Alaron alone, any two of Abrecan, Godric and Rheda close the
        // castle's conversations. Of each target's pairs, the member in most is kept out, those whose joins the relaxation comes to last
        // first, until none is left: Godric and Rheda, and Abrecan, whom the story brings first, may join.
        var pairs = new Dictionary<int, List<(RecordId A, RecordId B)>>();
        var single = keptReached.Where(h => !outside.Any(m => result.TryGetValue(m, out var l) && l.Contains(h))).ToList();
        for (var i = 0; i < outside.Count && single.Count > 0; i++)
        {
            for (var j = i + 1; j < outside.Count; j++)
            {
                foreach (var h in Forced(model, state, all, single, [outside[i], outside[j]]))
                {
                    if (!pairs.TryGetValue(h, out var list))
                        pairs[h] = list = [];
                    list.Add((outside[i], outside[j]));
                }
            }
        }
        int Layer(RecordId m) => Joins(model, m).Select(a => full.ActionLayer[a]).Where(l => l >= 0).DefaultIfEmpty(int.MaxValue).Min();
        foreach (var (h, list) in pairs)
        {
            while (list.Count > 0)
            {
                var member = list.SelectMany(p => new[] { p.A, p.B }).GroupBy(m => m)
                    .OrderByDescending(g => g.Count()).ThenByDescending(g => Layer(g.Key)).ThenBy(g => g.Key.ToString(), StringComparer.Ordinal).First().Key;
                if (!result.TryGetValue(member, out var lost))
                    result[member] = lost = [];
                lost.Add(h);
                list.RemoveAll(p => p.A == member || p.B == member);
            }
        }
        return result;
    }

    /// <summary>
    /// The targets of those given that the relaxation no longer sets with the members put in the party and the whole party kept, every
    /// member's leaving actions left out.
    /// </summary>
    private static HashSet<int> Forced(QuestModel model, PlanState state, List<int> all, IEnumerable<int> targets, IReadOnlyList<RecordId> members)
    {
        var leaves = AnyLeave(model, [.. state.Party, .. members]);
        var joined = members.Aggregate(state, (s, m) => s.Joined(m));
        var forced = new Relaxation(model, all.Where(a => !leaves.Contains(a))).Run(joined);
        return [.. targets.Where(h => !forced.Reached(model.FlagFact(h, true)))];
    }

    /// <summary>
    /// The actions with any <see cref="Leave"/> of one of the members among their effects, whatever their last word: Arturo's scene on the
    /// bridge (0x0404) has ways that take him out and in again, whose adds, which the relaxation takes all of, clear his joined flag.
    /// </summary>
    private static HashSet<int> AnyLeave(QuestModel model, IEnumerable<RecordId> members)
    {
        var set = members.ToHashSet();
        return [.. model.Actions.Where(a => a.Effects.Any(e => e is Leave l && set.Contains(l.Entity))).Select(a => a.Index)];
    }

    /// <summary>For each member who can join, the targets that need them to leave: the relaxation with the member kept for good loses them.</summary>
    public static Dictionary<RecordId, HashSet<int>> Away(QuestModel model, PlanState state, IEnumerable<int> targets) =>
        Without(model, state, targets, m => [.. Leaves(model, [m])], takeOut: false);

    /// <summary>
    /// For each member who can join, the targets the relaxation sets only through actions that need the member in the party then: with
    /// those actions left out, and the member joining as before, it no longer sets them.
    /// </summary>
    public static Dictionary<RecordId, HashSet<int>> Present(QuestModel model, PlanState state, IEnumerable<int> targets) =>
        Without(model, state, targets, m => NeedingIn(model, m), takeOut: false);

    private static Dictionary<RecordId, HashSet<int>> Without(QuestModel model, PlanState state, IEnumerable<int> targets,
        Func<RecordId, IReadOnlyList<int>> leftOut, bool takeOut)
    {
        var all = Enumerable.Range(0, model.Actions.Count).ToList();
        var full = new Relaxation(model, all).Run(state);
        var reached = targets.Where(h => full.Reached(model.FlagFact(h, true))).ToList();
        var result = new Dictionary<RecordId, HashSet<int>>();
        foreach (var member in model.Members.Keys.OrderBy(m => m.ToString(), StringComparer.Ordinal))
        {
            var removed = leftOut(member).ToHashSet();
            var from = takeOut && state.InParty(member) ? state.Without(member) : state;
            var without = new Relaxation(model, all.Where(a => !removed.Contains(a))).Run(from);
            var lost = reached.Where(h => !without.Reached(model.FlagFact(h, true))).ToHashSet();
            if (lost.Count > 0)
                result[member] = lost;
        }
        return result;
    }

    /// <summary>The members each target needs, turned round from <see cref="Needs"/>, <see cref="Away"/> or <see cref="Present"/>.</summary>
    public static Dictionary<int, List<RecordId>> ByTarget(Dictionary<RecordId, HashSet<int>> needs)
    {
        var by = new Dictionary<int, List<RecordId>>();
        foreach (var (member, targets) in needs.OrderBy(n => n.Key.ToString(), StringComparer.Ordinal))
        {
            foreach (var h in targets)
            {
                if (!by.TryGetValue(h, out var list))
                    by[h] = list = [];
                list.Add(member);
            }
        }
        return by;
    }
}
