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

/// <summary>The ending's credits rolling: the last scene's end plays the ending's cinematic, whose end rolls them.</summary>
public sealed record EndingGoal : Goal
{
    public override string Describe() => "the ending";
}
