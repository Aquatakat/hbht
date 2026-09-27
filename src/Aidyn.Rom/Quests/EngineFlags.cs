namespace Aidyn.Rom.Quests;

/// <summary>A run of plain flags that engine code writes, rather than any data.</summary>
/// <param name="Bits">How many flags hold the value, least significant bit first.</param>
public sealed record EngineFlagRange(ushort First, int Bits, string Description)
{
    public ushort Last => (ushort)(First + Bits - 1);
}

/// <summary>
/// The flags engine code writes, from the decomp (<c>gameStateFunnel.cpp</c>, <c>Flagindecies.h</c>).
/// These are facts the importer carries itself (ADR 0008 layer 1). The rest of the code's flag
/// references are roadmap 1H.
/// </summary>
public static class EngineFlags
{
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

    /// <summary>Winning the battle a script starts sets this flag instead of the map object's (<c>dialogmode.cpp</c>).</summary>
    public static IReadOnlyDictionary<int, ushort> BattleFlags { get; } = new Dictionary<int, ushort>
    {
        [0x365] = 978,  // The Goblin Ambush
        [0x4D1] = 5250, // Lugash. A derived flag, so the write does nothing.
    };

    /// <summary>
    /// Derived flags from here on are journal triggers: flag <c>JournalTriggers + i</c> turns on with
    /// journal entry <c>i</c>'s flag, and turning on shows the "new entry" notice.
    /// </summary>
    public const ushort JournalTriggers = 5556;
}
