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
