using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>Why a plain flag can never be set from where the static pass starts.</summary>
public enum NeverSet
{
    /// <summary>Nothing in the plugins sets it.</summary>
    NothingSetsIt,
    /// <summary>Only the engine's code sets it: a member's death, or a member's flags for someone who never joins.</summary>
    OnlyTheEngine,
    /// <summary>Only scripts nothing starts set it.</summary>
    ScriptNeverStarted,
    /// <summary>Only scripts that are started set it, but no way through them reaches the node that does.</summary>
    NoWayThroughItsScript,
    /// <summary>The model's actions set it, but none of them can ever be done: each needs something that never comes true.</summary>
    SetterNeverPossible,
    /// <summary>Only a container's trap going off sets it, which the planner never plans: it destroys what the container holds.</summary>
    OnlyATrapGoingOff,
    /// <summary>Only map objects set it whose gate is on flag 0 or 1 and never holds, so they're never there.</summary>
    ObjectNeverThere,
    /// <summary>Something sets it that the model doesn't play (a shop with no keeper's script, an item from what isn't modelled).</summary>
    NotModelled,
}

/// <summary>A plain flag that can never be set, why, and what stands in the way.</summary>
public sealed record UnreachableFlag(RecordId Flag, NeverSet Why, bool Read, string Detail);

/// <summary>
/// The static pass: from a state (a new game), everything that can ever come true, with nothing undone (<see cref="Relaxation"/>
/// over every action): which flags can be set, who can join, which scripts can run, whether the ending can roll. Each plain flag that
/// can't is given a reason, from the model's setters and the quest atlas's (<see cref="QuestIndex"/>).
/// </summary>
public sealed class StaticPass
{
    private StaticPass(QuestModel model, Relaxation.Result result)
    {
        Model = model;
        Result = result;
    }

    public QuestModel Model { get; }

    public Relaxation.Result Result { get; }

    /// <summary>Every plain flag, and those that can be set (set from the start included).</summary>
    public int PlainFlags { get; private set; }
    public int PlainSettable { get; private set; }

    /// <summary>Every derived flag, and those their inputs can make set.</summary>
    public int DerivedFlags { get; private set; }
    public int DerivedSettable { get; private set; }

    /// <summary>The members who can join.</summary>
    public IReadOnlyList<RecordId> Joinable { get; private set; } = [];

    /// <summary>The scripts something starts, and those of them that can run (one of their actions can be done).</summary>
    public int ScriptsStarted { get; private set; }
    public IReadOnlySet<RecordId> ScriptsRun { get; private set; } = new HashSet<RecordId>();

    /// <summary>Whether the ending's credits can roll.</summary>
    public bool ReachesEnding { get; private set; }

    /// <summary>The plain flags that can never be set, by flag.</summary>
    public IReadOnlyList<UnreachableFlag> Unreachable { get; private set; } = [];

    public static StaticPass Run(QuestModel model, PlanState from, QuestIndex index)
    {
        var relaxation = new Relaxation(model, Enumerable.Range(0, model.Actions.Count));
        var result = relaxation.Run(from);
        var pass = new StaticPass(model, result);
        pass.Tally(from, index);
        return pass;
    }

    public bool CanSet(RecordId flag) => Model.Flags.TryHandle(flag, out var h) && Result.Reached(Model.FlagFact(h, true));

    private void Tally(PlanState from, QuestIndex index)
    {
        var flags = Model.Flags;
        var adders = new Dictionary<int, List<int>>();
        foreach (var action in Model.Actions)
        {
            foreach (var f in Model.Adds(action.Index))
            {
                if (!adders.TryGetValue(f, out var list))
                    adders[f] = list = [];
                list.Add(action.Index);
            }
        }
        var unreachable = new List<UnreachableFlag>();
        for (var h = 0; h < flags.Count; h++)
        {
            var reached = Result.Reached(Model.FlagFact(h, true));
            switch (flags.Kind(h))
            {
                case FlagKind.Derived:
                    DerivedFlags++;
                    if (reached)
                        DerivedSettable++;
                    break;
                case FlagKind.Plain:
                    PlainFlags++;
                    if (reached)
                        PlainSettable++;
                    else
                        unreachable.Add(Classify(flags.Id(h), adders.GetValueOrDefault(Model.FlagFact(h, true)), index, from));
                    break;
            }
        }
        Unreachable = unreachable;
        Joinable = [.. Model.Members.Keys.Where(m => Model.PersonFact(m, true) is { } f && Result.Reached(f) && !from.InParty(m))];
        ScriptsStarted = Model.Ways.Count;
        ScriptsRun = Model.Actions.Where(a => a.Script is not null && Result.ActionLayer[a.Index] >= 0).Select(a => a.Script!.Value).ToHashSet();
        ReachesEnding = Result.Reached(Model.CreditsFact);
    }

    private UnreachableFlag Classify(RecordId flag, List<int>? setters, QuestIndex index, PlanState from)
    {
        var uses = index.UsesOf(flag);
        var read = uses.Any(u => u.Access == FlagAccess.Read);
        if (setters is { Count: > 0 })
        {
            // The first thing each setter needs that never comes true.
            var blockers = setters.Select(a =>
            {
                var action = Model.Actions[a];
                var blocker = Model.Pre(a).FirstOrDefault(p => !Result.Reached(p), -1);
                return blocker >= 0 ? Model.DescribeFact(blocker)
                    : action.Effects.Any(e => e is EndGame) ? "it ends the game"
                    : action.Kind == ActionKind.Event ? "the opening has played"
                    : action.Presence.OfType<Reaches>().FirstOrDefault(r => !Model.Reach.CanReach(r.Place, from)) is { } far ? $"reaching {far.Place}"
                    : "?";
            }).Distinct().Take(3);
            return new UnreachableFlag(flag, NeverSet.SetterNeverPossible, read,
                $"{setters.Count} setter(s), needing {string.Join("; or ", blockers)}");
        }
        var sets = uses.Where(u => u.Access == FlagAccess.Set).ToList();
        if (sets.Count == 0)
            return new UnreachableFlag(flag, NeverSet.NothingSetsIt, read, read ? "read, never set" : "never used");
        var scriptRoles = new[] { FlagRole.ScriptOp, FlagRole.ScriptDone, FlagRole.ScriptBattle, FlagRole.MemberAdded, FlagRole.MemberRemoved };
        var byScript = sets.Where(u => scriptRoles.Contains(u.Role) || u.Role == FlagRole.ItemGiven && u.By.Type == "script").ToList();
        if (byScript.Count == sets.Count)
        {
            var started = byScript.Where(u => Model.Ways.ContainsKey(u.By)).ToList();
            return started.Count > 0
                ? new UnreachableFlag(flag, NeverSet.NoWayThroughItsScript, read,
                    $"set by {string.Join(", ", started.Select(u => u.At is { } at ? $"{u.By.Key} node {at}" : $"{u.By.Key} ({u.Role})").Distinct().Take(4))}")
                : new UnreachableFlag(flag, NeverSet.ScriptNeverStarted, read,
                    $"set only by {string.Join(", ", byScript.Select(u => u.By.Key).Distinct().Take(4))}, which nothing starts");
        }
        if (sets.All(u => u.Role == FlagRole.KeyItem))
            return new UnreachableFlag(flag, NeverSet.NothingSetsIt, read, $"only holding {sets[0].By}, which nothing gives");
        if (sets.All(u => u.Role == FlagRole.ContainerExploded))
            return new UnreachableFlag(flag, NeverSet.OnlyATrapGoingOff, read, $"the exploded flag of {string.Join(", ", sets.Select(u => u.By).Distinct().Take(3))}");
        if (sets.All(u => Model.NeverThere.Contains(u.By)))
            return new UnreachableFlag(flag, NeverSet.ObjectNeverThere, read, $"set by {string.Join(", ", sets.Select(u => u.By).Distinct().Take(3))}");
        if (sets.All(u => u.Role is FlagRole.Member or FlagRole.Engine))
            return new UnreachableFlag(flag, NeverSet.OnlyTheEngine, read, $"set by {string.Join(", ", sets.Select(u => $"{u.By} ({u.Role})").Distinct().Take(3))}");
        return new UnreachableFlag(flag, NeverSet.NotModelled, read,
            $"set by {string.Join(", ", sets.Select(u => $"{u.By} ({u.Role})").Distinct().Take(4))}");
    }
}
