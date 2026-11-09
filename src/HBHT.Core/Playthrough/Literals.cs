using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>
/// Something an action needs of the state it starts from: a flag set or clear, someone in the party or not, an item held or not,
/// the party's size, a place the player can reach, or a gate the game decides in code (gold, a skill check, a lock), which the
/// planner takes to hold and lists with the step.
/// </summary>
public abstract record Literal
{
    /// <summary>A short account for reports, such as <c>flag 566 set</c>.</summary>
    public abstract string Describe();

    public sealed override string ToString() => Describe();
}

/// <summary>A flag is set, or clear.</summary>
public sealed record FlagIs(RecordId Flag, bool Set) : Literal
{
    public override string Describe() => $"flag {Key(Flag)} {(Set ? "set" : "clear")}";

    internal static string Key(RecordId id) => id.Namespace == RecordId.BaseNamespace ? id.Key : id.ToString();
}

/// <summary>Someone is in the party, or isn't.</summary>
public sealed record MemberIs(RecordId Entity, bool In) : Literal
{
    public override string Describe() => $"{Entity} {(In ? "in" : "not in")} the party";
}

/// <summary>The party holds an item (carried or worn), or doesn't.</summary>
public sealed record ItemIs(RecordId Item, bool Held) : Literal
{
    public override string Describe() => $"{Item} {(Held ? "held" : "not held")}";
}

/// <summary>The party's size is in a range, inclusive.</summary>
public sealed record PartySizeIn(int Min, int Max) : Literal
{
    public override string Describe() => Min == Max ? $"party of {Min}" : $"party of {Min}–{Max}";
}

/// <summary>The player can get to a place: a map object, or a chunk (<see cref="IPlaceReach"/>).</summary>
public sealed record Reaches(RecordId Place) : Literal
{
    public override string Describe() => $"reach {Place}";
}

/// <summary>
/// A gate the game decides in code rather than by flags: the party's gold, a skill check's roll, a stat, a lock's or a trap's level.
/// The planner takes it to hold; the pilot finds out.
/// </summary>
public sealed record CodeGate(string What) : Literal
{
    /// <summary>What the game decides, as data the run can weigh against the party (<see cref="CodeGates.Meets"/>); null if unknown.</summary>
    public CodeCheck? Check { get; init; }

    public override string Describe() => What;
}

/// <summary>What kind of thing the game decides in code.</summary>
public enum CodeCheckKind
{
    /// <summary>The party's gold in <see cref="CodeCheck.Lo"/>–<see cref="CodeCheck.Hi"/>.</summary>
    Gold,
    /// <summary>A script's check of a skill (<see cref="CodeCheck.A"/>), a roll the first of which the map object keeps.</summary>
    Skill,
    /// <summary>The party's best value of a stat (<see cref="CodeCheck.A"/>).</summary>
    BestStat,
    /// <summary>The party's worst value of a stat (<see cref="CodeCheck.A"/>).</summary>
    WorstStat,
    /// <summary>A member's (<see cref="CodeCheck.A"/>) value of a stat (<see cref="CodeCheck.B"/>).</summary>
    MemberStat,
    /// <summary>Picking a lock of level <see cref="CodeCheck.Lo"/>.</summary>
    Lock,
    /// <summary>Disarming a trap of level <see cref="CodeCheck.Lo"/>, a roll.</summary>
    Trap,
    /// <summary>A loot table's drop of <see cref="CodeCheck.A"/>, at <see cref="CodeCheck.Lo"/> percent.</summary>
    Chance,
    /// <summary>Buying <see cref="CodeCheck.A"/> at the shop a script opens.</summary>
    Buy,
    /// <summary>Winning a battle.</summary>
    Win,
    /// <summary>The Shadow reaching Alaron and merging with him.</summary>
    Merge,
    /// <summary>A check of a kind the model doesn't know.</summary>
    Unknown,
}

/// <summary>A code gate as data: its kind, the range it wants, and what it's of.</summary>
public sealed record CodeCheck(CodeCheckKind Kind, int Lo = 0, int Hi = 0, RecordId? A = null, RecordId? B = null);

/// <summary>What an action does to the state, in order.</summary>
public abstract record Effect
{
    public abstract string Describe();

    public sealed override string ToString() => Describe();
}

/// <summary>Sets or clears a flag.</summary>
public sealed record SetFlag(RecordId Flag, bool Value) : Effect
{
    public override string Describe() => $"{(Value ? "set" : "clear")} {FlagIs.Key(Flag)}";
}

/// <summary>Someone joins the party (a script's <c>party.addMember</c>), with their joined and left flags. Nothing if it's full.</summary>
public sealed record Join(RecordId Entity) : Effect
{
    public override string Describe() => $"{Entity} joins";
}

/// <summary>Someone leaves the party (<c>party.removeMember</c>), with their joined and left flags.</summary>
public sealed record Leave(RecordId Entity) : Effect
{
    public override string Describe() => $"{Entity} leaves";
}

/// <summary>The party gains an item, and a key item's held and region flags.</summary>
public sealed record Gain(RecordId Item) : Effect
{
    public override string Describe() => $"gain {Item}";
}

/// <summary>The party gives up an item, and a key item's held flag.</summary>
public sealed record Lose(RecordId Item) : Effect
{
    public override string Describe() => $"lose {Item}";
}

/// <summary>The ending's credits roll (a trigger's <c>credits.roll</c> with <c>ending</c>).</summary>
public sealed record RollCredits : Effect
{
    public override string Describe() => "the ending's credits";
}

/// <summary>The game is over (a trigger's <c>game.over</c>). The planner never takes such a step.</summary>
public sealed record EndGame : Effect
{
    public override string Describe() => "game over";
}

/// <summary>
/// A script object's cut-scene starts at once, whatever its gates (a trigger's <c>script.start</c>): the next step is one of its
/// ways, and nothing else.
/// </summary>
public sealed record StartScript(RecordId MapObject) : Effect
{
    public override string Describe() => $"starts {MapObject}'s script";
}
