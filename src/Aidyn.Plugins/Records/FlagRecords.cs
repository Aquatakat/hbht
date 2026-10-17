namespace Aidyn.Plugins.Records;

/// <summary>
/// An event flag (<c>aidyn:flag/n</c>, one of the original's <c>GameStateFunnel</c> entries). A
/// plain flag is set by scripts, map objects or the engine. A derived flag has a
/// <see cref="Formula"/> over other flags, and the flag network keeps it up to date.
/// </summary>
/// <remarks>
/// <c>aidyn:flag/0</c> always reads as clear and <c>aidyn:flag/1</c> as set, whatever they hold.
/// Base-game flags keep their numbers as the network's handles, and the network evaluates derived
/// flags in that order, which the original's results depend on.
/// </remarks>
/// <param name="Label">What the flag means, where engine code says. Most flags have none.</param>
/// <param name="Initial">Whether a plain flag starts a new game set.</param>
/// <param name="Journal">
/// The journal entry this derived flag adds when it turns on: the game raises the new-entry flag
/// (<see cref="StateFlagsRecord.NewJournalEntry"/>) and opens the journal at it.
/// </param>
public sealed record FlagRecord(RecordId Id, string? Label = null, bool Initial = false, FlagFormula? Formula = null,
    RecordId? Journal = null);

/// <summary>How a derived flag's inputs are combined (the original's <c>event_flag_typeA</c>).</summary>
public enum FlagFormulaKind
{
    /// <summary>A gate over the inputs: <see cref="FlagOperation.And"/> to <see cref="FlagOperation.Mask"/>.</summary>
    Logic,
    /// <summary>The inputs as the bits of a number, the first the lowest, compared with the operand.</summary>
    Value,
    /// <summary>How many inputs are set, compared with the operand.</summary>
    Count,
}

/// <summary>A derived flag's operation (the original's <c>Event_flag_typeB</c>).</summary>
public enum FlagOperation
{
    /// <summary>Every input is set.</summary>
    And,
    /// <summary>Any input is set.</summary>
    Or,
    /// <summary>Exactly one input is set.</summary>
    Xor,
    /// <summary>No input is set.</summary>
    Not,
    /// <summary>The inputs as bits equal the operand.</summary>
    Mask,
    Equal,
    Greater,
    Less,
    NotEqual,
}

/// <param name="Operand">What value and count flags compare with, and what a mask must equal.</param>
/// <param name="Inputs">At most 16, as the original counts them in 16 bits.</param>
public sealed record FlagFormula(FlagFormulaKind Kind, FlagOperation Op, int Operand, IReadOnlyList<RecordId> Inputs)
{
    /// <summary>What it tests, without its inputs' IDs, such as "any of 3 set" or "5 as bits > 20".</summary>
    public string Describe()
    {
        var n = Inputs.Count;
        var compare = Op switch
        {
            FlagOperation.Equal => "=",
            FlagOperation.Greater => ">",
            FlagOperation.Less => "<",
            FlagOperation.NotEqual => "≠",
            _ => null,
        };
        return (Kind, Op) switch
        {
            (FlagFormulaKind.Logic, FlagOperation.And) => $"all of {n} set",
            (FlagFormulaKind.Logic, FlagOperation.Or) => $"any of {n} set",
            (FlagFormulaKind.Logic, FlagOperation.Xor) => $"one of {n} set",
            (FlagFormulaKind.Logic, FlagOperation.Not) => $"none of {n} set",
            (FlagFormulaKind.Logic, FlagOperation.Mask) => $"{n} as bits = {Operand}",
            (FlagFormulaKind.Value, _) when compare is not null => $"{n} as bits {compare} {Operand}",
            (FlagFormulaKind.Count, _) when compare is not null => $"how many of {n} set {compare} {Operand}",
            _ => $"{Kind} {Op}, always clear",
        };
    }
}

/// <summary>
/// A journal entry (<c>aidyn:journal/n</c>). The journal lists the entries whose flag is set, and a
/// derived flag names the entry it adds (<see cref="FlagRecord.Journal"/>).
/// </summary>
public sealed record JournalRecord(RecordId Id, RecordId Flag, string Text);

/// <summary>
/// The flags the engine writes its own state into, so that content can read it, and the flags it
/// reads as options (<c>aidyn:stateflags/0</c>, the only one; <c>gameStateFunnel.cpp</c>). Each list
/// holds a number's bits, lowest first; a number too large for its flags keeps only its low bits.
/// </summary>
/// <remarks>
/// The original also names flags for the party's best Intelligence, Willpower, Endurance, Dexterity,
/// Strength, Mechanic, Loremaster and Wizard, but its setters never find their table entries, so
/// they're never written. They aren't here: the flag records' labels say so.
/// </remarks>
/// <param name="MoonPhase">0 new, 1 crescent, 2 quarter, 3 full.</param>
/// <param name="TimeOfDay">0 morning (from 6:00), 1 midday (9:00), 2 afternoon (12:00), 3 evening (18:00), 4 night (21:00).</param>
/// <param name="DayOfSeason">The week of the season × 7 + the day of the week, 0–55.</param>
/// <param name="Season">0–5.</param>
/// <param name="DayOfWeek">0–6.</param>
/// <param name="Week">The week of the season, 0–7.</param>
/// <param name="Rain">0 clear, 1 rain, 2 snow.</param>
/// <param name="Terrain">The terrain type of the chunk the player is in. The original has four flags for 28 types.</param>
/// <param name="NewJournalEntry">Set when a flag with a journal entry turns on, until the player sees the notice.</param>
/// <param name="RandomSkillChecks">
/// A flag the engine reads: while it's set, party skill checks in scripts roll 0–21. A debug
/// option that nothing in the game sets.
/// </param>
public sealed record StateFlagsRecord(
    RecordId Id,
    IReadOnlyList<RecordId> MoonPhase,
    IReadOnlyList<RecordId> TimeOfDay,
    IReadOnlyList<RecordId> DayOfSeason,
    IReadOnlyList<RecordId> Season,
    IReadOnlyList<RecordId> DayOfWeek,
    IReadOnlyList<RecordId> Week,
    IReadOnlyList<RecordId> Rain,
    IReadOnlyList<RecordId> Terrain,
    RecordId NewJournalEntry,
    RecordId? RandomSkillChecks = null);
