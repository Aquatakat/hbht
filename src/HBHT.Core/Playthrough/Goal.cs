using System.Globalization;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>What the run is after (docs/systems/playthrough.md's goals): a flag set, someone joining, or the ending.</summary>
public abstract record Goal
{
    public abstract string Describe();

    public sealed override string ToString() => Describe();

    /// <summary>
    /// <c>flag:&lt;n&gt;</c> (or a flag's record ID), <c>member</c>, <c>party:&lt;entity&gt;[,&lt;entity&gt;…]</c> (each a hex number
    /// such as <c>0x0241</c>, or a record ID) or <c>ending</c>.
    /// </summary>
    public static Goal Parse(string text) => text switch
    {
        "member" => new MemberGoal(),
        "ending" => new EndingGoal(),
        _ when text.StartsWith("flag:", StringComparison.Ordinal) => new FlagGoal(ParseFlag(text["flag:".Length..])),
        _ when text.StartsWith("party:", StringComparison.Ordinal) =>
            new PartyGoal([.. text["party:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ParseEntity)]),
        _ => throw new ArgumentException($"A goal is flag:<n>, member, party:<entities> or ending, not {text}."),
    };

    private static RecordId ParseEntity(string entity) =>
        entity.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(entity.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) ? RecordId.Hex(RecordTypes.Entity, n)
        : RecordId.TryParse(entity, out var id) ? id
        : throw new ArgumentException($"{entity} isn't an entity's hex number or record ID.");

    private static RecordId ParseFlag(string flag) =>
        int.TryParse(flag, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? RecordId.Decimal(RecordTypes.Flag, n)
        : RecordId.TryParse(flag, out var id) ? id
        : throw new ArgumentException($"{flag} isn't a flag's number or record ID.");
}

/// <summary>A flag set.</summary>
public sealed record FlagGoal(RecordId Flag) : Goal
{
    public override string Describe() => $"flag {FlagIs.Key(Flag)} set";
}

/// <summary>Someone joining the party who's living and not in it: whoever the planner finds nearest.</summary>
public sealed record MemberGoal : Goal
{
    public override string Describe() => "a member joins";
}

/// <summary>
/// Each of some members in the party at once (docs/systems/playthrough.md, "Party jobs"). The planner searches with the party's cap, so
/// with the party full it plans a joining conversation's way that asks someone else to leave.
/// </summary>
public sealed record PartyGoal(IReadOnlyList<RecordId> Members) : Goal
{
    public override string Describe() => $"the party with {string.Join(", ", Members.Select(m => m.Key))}";

    public bool Equals(PartyGoal? other) => other is not null && Members.SequenceEqual(other.Members);

    public override int GetHashCode() => Members.Aggregate(0, HashCode.Combine);
}

/// <summary>
/// A story trigger letting the player by (<see cref="StoryTriggers"/>): its gates closed, or a way it would play that doesn't turn him
/// back. The run puts it ahead of its goals when a step's way is turned back there.
/// </summary>
public sealed record PassGoal(RecordId Trigger) : Goal
{
    public override string Describe() => $"get past {Trigger}";
}

/// <summary>The model's facts all true: one of the ways a <see cref="PassGoal"/> is planned for.</summary>
public sealed record FactsGoal(IReadOnlyList<int> Facts, string What) : Goal
{
    public override string Describe() => What;
}

/// <summary>The ending's credits rolling: the last scene's end plays the ending's cinematic, whose end rolls them.</summary>
public sealed record EndingGoal : Goal
{
    public override string Describe() => "the ending";
}

/// <summary>
/// Every one of a set of flags seen set (docs/systems/playthrough.md's flag coverage): reached once each target has been seen set, in
/// this run or by another job (<see cref="CoveredElsewhere"/>), or can no longer be set from where the run stands. The run sweeps the
/// targets nearest first, preferring steps that lose none of them, and records a branch where a step does (<see cref="Coverage"/>).
/// </summary>
/// <param name="Targets">The flags to see set.</param>
public sealed record CoverGoal(IReadOnlyList<RecordId> Targets) : Goal
{
    /// <summary>Steps towards one target that may fail before it's given up.</summary>
    public int MostFailures { get; init; } = 3;

    /// <summary>The planner's plans to one target that may find nothing, at different steps, before it's given up.</summary>
    public int MostNoPlans { get; init; } = 2;

    /// <summary>The flags other jobs have seen set, asked at the start and every <see cref="RefreshSteps"/> steps; none if null.</summary>
    public Func<IReadOnlySet<RecordId>>? CoveredElsewhere { get; init; }

    public int RefreshSteps { get; init; } = 10;

    /// <summary>
    /// Steps the run never takes: the ways that lost the targets of the jobs it branched from, or stranded them. Each is
    /// <c>#&lt;index&gt; &lt;description&gt;</c>, the action (<see cref="PlayerAction.Index"/>) and its <see cref="PlayerAction.Describe"/>
    /// to check it by: a script's ways that differ only in its conditions describe alike, and banning by the description banned the way
    /// that set a branch's target with the one that lost it (0x03C3's at Erromon's 7/E03/1, for flag 29). An index whose action no longer
    /// describes so, or an entry with none, bans by the description.
    /// </summary>
    public IReadOnlyCollection<string> Avoid { get; init; } = [];

    /// <summary>
    /// Members to have in the party at once, a party job's (docs/systems/playthrough.md, "Party jobs"): once no step nearby sets a target,
    /// the run plans to this party (<see cref="PartyGoal"/>) before anything else, and never takes a step that leaves one of them out
    /// (<see cref="PartyJobs.Leaving"/>). None for an ordinary job.
    /// </summary>
    public IReadOnlyList<RecordId> Party { get; init; } = [];

    /// <summary>The actions an <see cref="Avoid"/> entry names in a model.</summary>
    public static IEnumerable<int> Avoided(QuestModel model, string entry)
    {
        if (entry.StartsWith('#') && entry.IndexOf(' ') is var space and > 1
            && int.TryParse(entry.AsSpan(1, space - 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
            && index < model.Actions.Count && model.Actions[index].Describe() == entry[(space + 1)..])
            return [index];
        var description = entry.StartsWith('#') && entry.IndexOf(' ') is var at and > 0 ? entry[(at + 1)..] : entry;
        return model.Actions.Where(a => a.Describe() == description).Select(a => a.Index);
    }

    public override string Describe() => $"cover {Targets.Count} flags";

    /// <summary>The default targets: every flag, plain or derived, that the relaxation over every action from the state sets and isn't set in it.</summary>
    public static List<RecordId> StaticTargets(QuestModel model, PlanState state)
    {
        var result = new Relaxation(model, Enumerable.Range(0, model.Actions.Count)).Run(state);
        var flags = model.Flags;
        return [.. Enumerable.Range(0, flags.Count)
            .Where(h => flags.Kind(h) is FlagKind.Plain or FlagKind.Derived && result.FactLayer[model.FlagFact(h, true)] > 0)
            .Select(flags.Id)];
    }
}
