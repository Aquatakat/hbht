using Aidyn.Rom.Db;

namespace Aidyn.Rom.Quests;

/// <summary>A run of plain flags that engine code writes, rather than any data.</summary>
/// <param name="Bits">How many flags hold the value, least significant bit first.</param>
public sealed record EngineFlagRange(ushort First, int Bits, string Description)
{
    public ushort Last => (ushort)(First + Bits - 1);
}

/// <summary>A flag engine code names directly.</summary>
public sealed record EngineFlag(ushort Flag, string Description);

/// <summary>
/// The flags engine code names directly, from the decomp and checked against 1.0's code. These are
/// facts the importer carries itself (ADR 0008 layer 1). Tables the code segment holds are read
/// from the ROM instead: see <see cref="EngineTables"/>.
/// </summary>
public static class EngineFlags
{
    // The runs of flags the engine writes its state into (SetFlagArray_on_Time, set_weather_flags
    // and set_terrain_flags). The importer writes these as the state flags record.
    public static EngineFlagRange MoonPhase { get; } = new(2299, 2, "Moon phase");
    public static EngineFlagRange TimeOfDay { get; } = new(2301, 3, "Time of day");
    public static EngineFlagRange DayOfSeason { get; } = new(2304, 6, "Day of the season (week × 7 + day of the week)");
    public static EngineFlagRange Season { get; } = new(2310, 3, "Season");
    public static EngineFlagRange DayOfWeek { get; } = new(2313, 3, "Day of the week");
    public static EngineFlagRange Week { get; } = new(2316, 3, "Week of the season");
    public static EngineFlagRange Rain { get; } = new(2319, 2, "Rain");
    // Four flags for 28 terrain types, so types from 16 on wrap round.
    public static EngineFlagRange Terrain { get; } = new(2321, 4, "Terrain type");

    /// <summary>Set when a flag with a journal entry turns on (<c>set_journalentry_flag</c>).</summary>
    public const ushort NewJournalEntry = 3617;

    /// <summary>While set, party skill checks in scripts roll 0–21 (<c>DialogEvalCallback</c>). A debug option.</summary>
    public const ushort RandomSkillChecks = 3717;

    /// <summary>Set while the game looks for a camp scene (<c>dialoug_ref_obj_func</c>).</summary>
    public const ushort CampCheck = 4240;

    /// <summary>Camping can be ambushed while set (<c>get_ranger_camping_bool</c>).</summary>
    public const ushort CampAmbushes = 5004;

    /// <summary>Values the engine writes into runs of flags (<c>gameStateFunnel.cpp</c>).</summary>
    public static IReadOnlyList<EngineFlagRange> Ranges { get; } =
    [
        MoonPhase, TimeOfDay, DayOfSeason, Season, DayOfWeek, Week, Rain, Terrain,
        new(NewJournalEntry, 1, "A new journal entry is waiting"),
        new(CampCheck, 1, "Set while the game checks for a camp dialogue"),
    ];

    /// <summary>
    /// Runs of flags for the party's best stats and skills, which derived flags read, but which the
    /// engine never writes. <c>Event_flag_stat</c> and <c>event_flag_skill_</c> look the stat up in
    /// their table with a count of 0, so the lookup always fails. None is set in any checkpoint.
    /// </summary>
    public static IReadOnlyList<EngineFlagRange> NeverWritten { get; } =
    [
        new(2325, 5, "Party's best Intelligence"),
        new(2330, 5, "Party's best Willpower"),
        new(2335, 5, "Party's best Endurance"),
        new(2340, 5, "Party's best Dexterity"),
        new(2345, 5, "Party's best Strength"),
        new(2350, 5, "Party's best Mechanic"),
        // The skill table's Loremaster entry starts and ends at 2360.
        new(2355, 5, "Party's best Diplomat (no table entry)"),
        new(2360, 5, "Party's best Loremaster (the table's entry is 2360 alone)"),
        new(2365, 5, "Party's best Wizard"),
    ];

    /// <summary>
    /// Winning a battle with one of these entities in its first four enemy slots sets the flag
    /// (<c>set_boss_flag</c>). It applies to every battle won, however it started.
    /// </summary>
    public static IReadOnlyDictionary<ItemId, ushort> BossFlags { get; } = new Dictionary<ItemId, ushort>
    {
        [new(0x021D)] = 154,  // Kitarak
        [new(0x0224)] = 3204, // Pochanargat
        [new(0x0213)] = 458,  // Shamsuk (the decomp's Ehud)
        [new(0x0297)] = 3738, // Ksathra
        [new(0x0296)] = 3737, // Shatrevar
        [new(0x0295)] = 3736, // Mehrdad
        [new(0x0294)] = 3735, // Golnar
        [new(0x0293)] = 3734, // Nasim
        [new(0x0292)] = 3733, // Behrooz
        [new(0x0298)] = 3739, // Assim
        [new(0x0262)] = 533,  // Lugash
    };

    /// <summary>The help menu's script, which only code starts (<c>gametrek.cpp</c>).</summary>
    public const int HelpScript = 1355;

    /// <summary>Other flags the engine sets.</summary>
    public static IReadOnlyList<EngineFlag> Writes { get; } =
    [
        new(47, "Set after the Goblin Ambush, when Alaron wakes in Oriana's hut"),
        new(566, "Set when the Shadow merges with Alaron in combat"),
        new(622, "Set when Sholeh dies in combat"),
        new(3611, "Set the first time a trainer teaches a spell, for the spell help"),
        new(3612, "Set the first time the party buys a potion, for the potion help"),
        new(3673, "Set by the SLASHING cheat"),
        new(3675, "Set by the DARKSIDE cheat"),
        new(3693, "Set by the !CHEATER cheat, which retail allows once"),
        new(3694, "Set by the !BINGO cheat, which retail allows once"),
    ];

    /// <summary>Flags engine code reads, apart from those it also writes.</summary>
    public static IReadOnlyList<EngineFlag> Reads { get; } =
    [
        new(590, "Alaron has his True Name while set"),
        new(3442, "When the Rooughah scene (Borg13 1268) ends, the Roog cinematic plays if this is set"),
        new(RandomSkillChecks, "Party skill checks in dialogue roll 0–21 while set"),
        new(CampAmbushes, "Camping can be ambushed while set"),
        new(5017, "The lighthouse chunks swap to their lit scenes while set"),
        new(5626, "Read after some map object actions; the result is never used"),
    ];

    /// <summary>
    /// Derived flags from here on are journal triggers: flag <c>JournalTriggers + i</c> turns on with
    /// journal entry <c>i</c>'s flag, and turning on shows the "new entry" notice.
    /// </summary>
    public const ushort JournalTriggers = 5556;
}
