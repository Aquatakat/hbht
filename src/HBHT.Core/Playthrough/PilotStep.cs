using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>How the pilot uses a step's map object.</summary>
public enum UseKind
{
    /// <summary>As its type and tangibility say: walk into a tangible one, talk to an npc or encounter, press A at the rest.</summary>
    Auto,

    /// <summary>Stand within its use range and press A once A would reach it (a container, a sign, a door, a save point).</summary>
    Press,

    /// <summary>Walk into its radius (a tangible teleporter, tripwire or script object, which trek starts by walking in).</summary>
    WalkInto,

    /// <summary>Walk up to the npc's or encounter's body where it wanders now, and press A once A would reach it.</summary>
    Talk,
}

/// <summary>A choice the step wants taken: at <paramref name="Node"/> of <paramref name="Script"/>, the choice whose node is <paramref name="Choice"/>.</summary>
public sealed record ChoiceWanted(RecordId Script, int Node, int Choice);

/// <summary>What an expected effect is of.</summary>
public enum EffectKind
{
    /// <summary>A flag, plain or derived: set, or clear.</summary>
    Flag,

    /// <summary>A party member, by entity: in the party, or not.</summary>
    Member,

    /// <summary>An item, held in the inventory or equipped by a member, or not.</summary>
    Item,
}

/// <summary>What a step is expected to leave so: a flag set or clear, a member in the party or not, an item held or not.</summary>
public sealed record ExpectedEffect(EffectKind Kind, RecordId Id, bool Present = true)
{
    public static ExpectedEffect FlagSet(RecordId flag) => new(EffectKind.Flag, flag);

    public static ExpectedEffect FlagCleared(RecordId flag) => new(EffectKind.Flag, flag, false);

    public static ExpectedEffect Joins(RecordId entity) => new(EffectKind.Member, entity);

    public static ExpectedEffect Leaves(RecordId entity) => new(EffectKind.Member, entity, false);

    public static ExpectedEffect Gains(RecordId item) => new(EffectKind.Item, item);

    public static ExpectedEffect Loses(RecordId item) => new(EffectKind.Item, item, false);

    /// <summary>Whether the game is as expected.</summary>
    public bool Holds(GameState game) => Kind switch
    {
        EffectKind.Flag => game.Flags.IsSet(Id) == Present,
        EffectKind.Member => game.Party.Contains(Id) == Present,
        _ => game.Party.HasItem(Id) == Present,
    };

    public override string ToString() => Kind switch
    {
        EffectKind.Flag => $"{Id} {(Present ? "set" : "clear")}",
        EffectKind.Member => $"{Id} {(Present ? "joins" : "leaves")}",
        _ => $"{Id} {(Present ? "held" : "not held")}",
    };
}

/// <summary>
/// One step of a plan for the pilot (docs/systems/playthrough.md, layer 4): go to a map object and use it, take the choices wanted in
/// what it starts, and check what it was expected to do.
/// </summary>
/// <param name="MapObject">The map object to use, <c>aidyn:mapobject/&lt;set&gt;/&lt;chunk&gt;/&lt;n&gt;</c>.</param>
/// <param name="How">How to use it; by its type if <see cref="UseKind.Auto"/>.</param>
/// <param name="Choices">The choices wanted in the conversations and cut-scenes it starts; the host's default elsewhere.</param>
/// <param name="Expect">What it should leave so, checked once its consequences have played out.</param>
public sealed record PilotStep(RecordId MapObject, UseKind How = UseKind.Auto, IReadOnlyList<ChoiceWanted>? Choices = null,
    IReadOnlyList<ExpectedEffect>? Expect = null);

/// <summary>Why a step failed.</summary>
public enum StepFailure
{
    /// <summary>It didn't: the object was used and its consequences played out.</summary>
    None,

    /// <summary>The world graph found no route (<see cref="StepOutcome.Refusal"/> says why), or the object isn't one the player uses.</summary>
    NoRoute,

    /// <summary>The pilot stalled more often than its budget allows, or replanned more.</summary>
    Stuck,

    /// <summary>It stood within use range but A never reached the object, or walking in never set it off.</summary>
    NotUsed,

    /// <summary>The step's ticks ran out.</summary>
    OutOfTicks,

    /// <summary>The game ended under it (<see cref="PlaythroughHost.End"/>): a game over, Quit or Return to Title.</summary>
    GameOver,
}

/// <summary>A wanted choice that the page didn't offer when it came up, and what it offered.</summary>
public sealed record ChoiceMiss(long Tick, ChoiceWanted Wanted, IReadOnlyList<int> Offered)
{
    public override string ToString() => $"{Tick}: {Wanted.Script} node {Wanted.Node} didn't offer {Wanted.Choice} (offered {string.Join(", ", Offered)})";
}

/// <summary>What a step came to.</summary>
/// <param name="Arrived">Whether the player came within the object's use range (or its wanderer's reach).</param>
/// <param name="Used">Whether he used it: A took it, or walking in set it off.</param>
/// <param name="Reason">What went wrong, in words; null if nothing did.</param>
/// <param name="Refusal">The world graph's reason, for <see cref="StepFailure.NoRoute"/>.</param>
/// <param name="Ticks">The session's ticks the step took, its consequences' included.</param>
/// <param name="Replans">Routes asked for after the first: after a stall, or after something took control or moved the player.</param>
/// <param name="Stalls">The times the player stopped getting nearer what he steered for.</param>
/// <param name="Changes">What changed in the game over the step: plain flags, members, items and gold.</param>
/// <param name="Misses">What was expected and doesn't hold.</param>
/// <param name="Unexpected">What changed that wasn't expected: side effects, as information.</param>
/// <param name="Transitions">The transitions the session sent during the step.</param>
/// <param name="Pages">The pages turned during the step.</param>
/// <param name="Battles">The battles begun during the step, a wanderer's on the way among them.</param>
/// <param name="ChoicesTaken">The wanted choices taken.</param>
/// <param name="ChoicesMissed">Pages a wanted choice was keyed to that came up without it.</param>
/// <param name="ChoicesNotReached">Wanted choices whose page never came up.</param>
/// <param name="Where">Where the player stood as the step ended.</param>
public sealed record StepOutcome(
    PilotStep Step,
    StepFailure Failure,
    bool Arrived,
    bool Used,
    string? Reason,
    RouteRefusal? Refusal,
    long Ticks,
    int Replans,
    int Stalls,
    GameChanges Changes,
    IReadOnlyList<ExpectedEffect> Misses,
    GameChanges Unexpected,
    IReadOnlyList<TransitionAt> Transitions,
    IReadOnlyList<PageTurned> Pages,
    IReadOnlyList<BattleFought> Battles,
    IReadOnlyList<ChoiceWanted> ChoicesTaken,
    IReadOnlyList<ChoiceMiss> ChoicesMissed,
    IReadOnlyList<ChoiceWanted> ChoicesNotReached,
    Place? Where)
{
    /// <summary>The object was used, its consequences played out, and nothing stopped it. Expectation misses don't count: they're the report's.</summary>
    public bool Done => Failure == StepFailure.None;

    public override string ToString() =>
        $"{Step.MapObject}: {(Done ? "done" : $"{Failure} ({Reason})")}, arrived {Arrived}, used {Used}, {Ticks} ticks, {Replans} replans, {Stalls} stalls, " +
        $"{Transitions.Count} transitions, {Battles.Count} battles, {Misses.Count} misses; changed {Changes}";
}
