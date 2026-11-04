using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.TestSupport;

/// <summary>A combat record for tests that need one but not the base game's.</summary>
public static class Combats
{
    /// <summary>
    /// A combat record that names nothing: <paramref name="fallback"/> as given, the Battlefields' A01 as the default arena, the menus'
    /// words blank, gold doubled as 1.0's is. Tests set what they need with <c>with</c>; who fights for the party and the rest are the
    /// entities' own (<see cref="EntityCombat"/>), and a battle's own rules are on what starts it (<see cref="BattleRules"/>).
    /// </summary>
    public static CombatRecord Rules(RecordId fallback) => new(
        RecordId.Decimal(RecordTypes.Combat, 0), fallback, ChunkRecord.IdOf(16, 1, 1), LootGoldMultiplier: 2,
        Text: new CombatText("", "", "", "", "", new SenseAuraText("", "", "", "", "", "", "", "", 2)));
}

/// <summary>The base game's schools and elements as records (ADR 0033), for rules tests make by hand.</summary>
public static class Kinds
{
    /// <summary>The six schools: Chaos learns every school's spells, and the unschooled's effects can't be dispelled.</summary>
    public static readonly IReadOnlyDictionary<RecordId, SchoolRecord> Schools = new[]
    {
        new SchoolRecord(RecordIds.School(0), "Chaos", RecordId.Hex(RecordTypes.Image, 0x01A9), LearnsEverySchool: true),
        new SchoolRecord(RecordIds.School(1), "Elemental", RecordId.Hex(RecordTypes.Image, 0x01A5)),
        new SchoolRecord(RecordIds.School(2), "Naming", RecordId.Hex(RecordTypes.Image, 0x01A6)),
        new SchoolRecord(RecordIds.School(3), "Necromancy", RecordId.Hex(RecordTypes.Image, 0x01A7)),
        new SchoolRecord(RecordIds.School(4), "Unschooled", Dispellable: false),
        new SchoolRecord(RecordIds.School(5), "Star", RecordId.Hex(RecordTypes.Image, 0x01A8)),
    }.ToDictionary(s => s.Id);

    private static readonly string[] ElementNames =
        ["", "Earth", "Solar", "Physical", "Necromancy", "Fire", "Lunar", "Naming", "Water", "Magic", "Air", "Star", "Elemental", "Chaos",
            "Cutting", "Smashing", "Holy"];

    /// <summary>The 16 elements: Physical covers Cutting and Smashing, and Magic the magical ones.</summary>
    public static readonly IReadOnlyDictionary<RecordId, ElementRecord> Elements = Enumerable.Range(1, 16).Select(n => new ElementRecord(
        RecordIds.Element(n), ElementNames[n], n switch
        {
            3 => [RecordIds.Element(14), RecordIds.Element(15)],
            9 => [.. new[] { 1, 2, 4, 5, 6, 7, 8, 10, 11, 12, 13 }.Select(RecordIds.Element)],
            _ => null,
        })).ToDictionary(e => e.Id);
}
