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
    /// <summary>Values the engine writes into runs of flags (<c>gameStateFunnel.cpp</c>).</summary>
    public static IReadOnlyList<EngineFlagRange> Ranges { get; } =
    [
        new(2299, 2, "Moon phase"),
        new(2301, 3, "Time of day"),
        new(2304, 6, "Day of the month (week × 7 + day)"),
        new(2310, 3, "Season"),
        new(2313, 3, "Day of the week"),
        new(2316, 3, "Week of the month"),
        new(2319, 2, "Rain"),
        new(2321, 4, "Terrain type"),
        new(2325, 5, "Party's best Intelligence"),
        new(2330, 5, "Party's best Willpower"),
        new(2335, 5, "Party's best Endurance"),
        new(2340, 5, "Party's best Dexterity"),
        new(2345, 5, "Party's best Strength"),
        new(2350, 5, "Party's best Mechanic"),
        // The skill table's Loremaster entry starts and ends at 2360, so only the lowest bit is written.
        new(2360, 1, "Party's best Loremaster (lowest bit only)"),
        new(2365, 5, "Party's best Wizard"),
        new(3617, 1, "A new journal entry is waiting"),
        new(4240, 1, "Set while the game checks for a camp dialogue"),
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

    /// <summary>
    /// What starts each of the four cinematics (<see cref="Engine.Cinematics"/>), in the game's order
    /// (<c>Cinematic::Load</c>'s callers). The last two come from <see cref="EngineTables.ScriptEndings"/>.
    /// </summary>
    public static IReadOnlyList<string> CinematicStarts { get; } =
    [
        "a new game starts",
        "the Shadow merges with Alaron in combat",
        "the Rooughah scene (Borg13 1268) ends while flag 3442 is set",
        "the last scene (Borg13 1278) ends",
    ];

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
        new(3717, "Party skill checks in dialogue roll 0–21 while set"),
        new(5004, "Camping can be ambushed while set"),
        new(5017, "The lighthouse chunks swap to their lit scenes while set"),
        new(5626, "Read after some map object actions; the result is never used"),
        new(4286, "Chooses the minimap image of an interior"),
        new(4287, "Chooses the minimap image of an interior"),
        new(4288, "Chooses the minimap image of an interior"),
        new(4289, "Chooses the minimap image of an interior"),
        new(4290, "Chooses the minimap image of an interior"),
        new(4291, "Chooses the minimap image of an interior"),
        new(4292, "Chooses the minimap image of an interior"),
        new(4293, "Chooses the minimap image of an interior"),
        new(4294, "Chooses the minimap image of an interior"),
    ];

    /// <summary>
    /// Derived flags from here on are journal triggers: flag <c>JournalTriggers + i</c> turns on with
    /// journal entry <c>i</c>'s flag, and turning on shows the "new entry" notice.
    /// </summary>
    public const ushort JournalTriggers = 5556;
}
