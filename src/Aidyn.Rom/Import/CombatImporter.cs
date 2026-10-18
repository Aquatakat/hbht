using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Quests;

namespace Aidyn.Rom.Import;

/// <summary>Combat's record (<c>aidyn:combat/0</c>): who fights, who doesn't, and the flee rules.</summary>
internal static class CombatImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Combat");
        ctx.WriteRecords("combat.json", new[] { BuildCombat(ctx.Engine) });
    }

    /// <summary>
    /// The combat record. The code names entities by their place in the entity list (<see cref="EngineTables.EntityList"/>),
    /// read from the ROM; the places, and the encounter flags' table, are carried as facts, checked in 1.0's code: Sholeh
    /// is 0xA4 (<c>Combat.FindSholeh</c>), the Skeleton 0xBB (<c>BattleSetup.FillEnemies</c>), the Shadow 0xAC, Pochanargat 0xAB, who never moves in battle (<c>Combatant.UpdateRadius</c>); the bystanders
    /// 0–0x56, 0x82–0x90 and 0x92–0x96 (<c>Combat.IsExcludedEnemy</c>); the bosses who forbid fleeing the twelve of
    /// <c>Combat.NoFleeEntities</c> (<c>0x800D2688</c>); and the encounters <c>0x800D26A0</c>'s three.
    /// </summary>
    internal static CombatRecord BuildCombat(EngineTables tables)
    {
        RecordId At(int place) => DbRecordIds.Required(tables.EntityList[place], $"entity list place {place}");
        static bool Bystander(int place) => place <= 0x56 || place is >= 0x82 and <= 0x90 or >= 0x92 and <= 0x96;
        return new CombatRecord(
            RecordId.Decimal(RecordTypes.Combat, 0),
            Ally: At(0xA4),
            Bystanders: [.. Enumerable.Range(0, tables.EntityList.Count).Where(Bystander).Select(At)],
            Fallback: At(0xBB),
            NoFlee: [.. new[] { 0xA8, 0xAA, 0xAB, 0xAD, 0xAE, 0xB4, 0xB3, 0xB2, 0xB1, 0xB0, 0xAF, 0xB5 }.Select(At)],
            Flee: [At(0xAC)],
            FleeByEncounter:
            [
                new(DbRecordIds.Flag(978), false),
                new(DbRecordIds.Flag(5250), true),
                new(DbRecordIds.Flag(3749), true),
            ],
            Rooted: [At(0xAB)]);
    }
}
