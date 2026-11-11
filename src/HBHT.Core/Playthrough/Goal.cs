using System.Globalization;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>What the run is after (docs/systems/playthrough.md's goals): a flag set, someone joining, or the ending.</summary>
public abstract record Goal
{
    public abstract string Describe();

    public sealed override string ToString() => Describe();

    /// <summary><c>flag:&lt;n&gt;</c> (or a flag's record ID), <c>member</c> or <c>ending</c>.</summary>
    public static Goal Parse(string text) => text switch
    {
        "member" => new MemberGoal(),
        "ending" => new EndingGoal(),
        _ when text.StartsWith("flag:", StringComparison.Ordinal) => new FlagGoal(ParseFlag(text["flag:".Length..])),
        _ => throw new ArgumentException($"A goal is flag:<n>, member or ending, not {text}."),
    };

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

    public override string Describe() => $"cover {Targets.Count} flags";
}
