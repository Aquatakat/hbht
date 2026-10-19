using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Quests;

namespace Aidyn.Rom.Import;

/// <summary>Combat's record (<c>aidyn:combat/0</c>): who fights, who doesn't, and the flee rules.</summary>
internal static class CombatImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Combat");
        ctx.WriteRecords("combat.json", new[]
        {
            BuildCombat(ctx.Engine, ctx.Db, ctx.Items, ctx.Rom.Table(CodeTable.BowKindWeapons), ctx.Rom.Table(CodeTable.ThrownKindWeapons)),
        });
    }

    /// <summary>
    /// The combat record. The code names entities by their place in the entity list (<see cref="EngineTables.EntityList"/>),
    /// read from the ROM; the places, and the encounter flags' table, are carried as facts, checked in 1.0's code: Sholeh
    /// is 0xA4 (<c>Combat.FindSholeh</c>), the Skeleton 0xBB (<c>BattleSetup.FillEnemies</c>), the Shadow 0xAC, Pochanargat 0xAB, who never moves in battle (<c>Combatant.UpdateRadius</c>); the bystanders
    /// 0–0x56, 0x82–0x90 and 0x92–0x96 (<c>Combat.IsExcludedEnemy</c>); the bosses who forbid fleeing the twelve of
    /// <c>Combat.NoFleeEntities</c> (<c>0x800D2688</c>); the thirteen the AI gives the bosses' styles (<c>EnemyAI.HasBossStyle</c>,
    /// <c>0x8005BA40</c>, by its jump table at <c>0x800D24B0</c>); and the encounters <c>0x800D26A0</c>'s three. The weapons that fly are the
    /// ROM's two lists of weapon DB positions (<c>Combat.BowKindWeapons</c> <c>0x800E0C08</c> and <c>Combat.ThrownKindWeapons</c>
    /// <c>0x800E0C14</c>, read per version), each with the numbers <see cref="Muzzles"/> carries; a listed weapon with no fact, a fact
    /// for none listed, or a name that differs is an error. Spikes, position 100, is the one ranged weapon allowed in melee.
    /// </summary>
    internal static CombatRecord BuildCombat(
        EngineTables tables, GameDb db, ItemTables items, ReadOnlySpan<byte> bowList, ReadOnlySpan<byte> thrownList)
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
            Rooted: [At(0xAB)],
            Missiles: Missiles(db, items, Positions(bowList), Positions(thrownList)),
            CloseRange: [Weapon(db, SpikesPosition, "Spikes")],
            Bosses: [.. new[] { 0x57, 0x58, 0x59, 0x5E, 0x5F, 0x7A, 0xA7, 0xA8, 0xA9, 0xAB, 0xAD, 0xBA, 0xBC }.Select(At)]);
    }

    /// <summary>The weapon DB position of Spikes, the one ranged weapon that may strike in melee too (<c>Weapon.AllowsMelee</c>, <c>0x8007B730</c>).</summary>
    private const int SpikesPosition = 100;

    /// <summary>What a bow flies as: Borg5 <c>0x348F</c>, the arrow (<c>Combatant.LoadWeaponScene</c>, <c>0x80066CF8</c>).</summary>
    private const int ArrowScene = 0x348F;

    /// <summary>The positions in a list that ends with <c>0xFF</c>.</summary>
    private static List<int> Positions(ReadOnlySpan<byte> list)
    {
        var positions = new List<int>();
        foreach (var position in list)
        {
            if (position == 0xFF)
                return positions;
            positions.Add(position);
        }
        throw new InvalidDataException("A weapon list doesn't end.");
    }

    private static RecordId Weapon(GameDb db, int position, string name) =>
        db.Weapons[position].Name == name
            ? DbRecordIds.Required(db.Weapons[position].Id)
            : throw new InvalidDataException($"Weapon position {position} is {db.Weapons[position].Name}, not {name}.");

    /// <summary>
    /// <c>Combatant.WeaponMuzzle</c> (<c>0x80066DD0</c>) is a switch on the weapon's position in the DB (jump table <c>0x800D2A18</c>,
    /// positions 53 to 102), which sets three numbers: the first (<see cref="Missile.Speed"/>) from the floats at <c>0x800D2AE0</c> to
    /// <c>0x800D2B44</c>, a second that the port doesn't use (20, 6, 12 or 8 for some, else 0), and the third (<see cref="Missile.Turn"/>),
    /// 90 for position 102 alone. A position it doesn't list gets 4.0 and 8. Read case by case from the disassembly.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, (string Name, float Speed, float Turn)> Muzzles = new Dictionary<int, (string, float, float)>
    {
        [53] = ("Bow of Accuracy", 9.9f, 0),
        [54] = ("Bow of Shielding", 8, 0),
        [55] = ("Bow of Thunder", 9, 0),
        [56] = ("Heartseeker Bow", 9, 0),
        [57] = ("Great Bow", 9, 0),
        [58] = ("Hunter's Bow", 8, 0),
        [59] = ("Long Bow", 8, 0),
        [60] = ("Short Bow", 5, 0),
        [94] = ("Dragon Fang", 8, 0),
        [95] = ("Throwing Iron", 8, 0),
        [96] = ("Throwing Knife", 5, 0),
        [97] = ("Cyclops Hurlstar", 5, 0),
        [98] = ("Dart of Distance", 8, 0),
        [99] = ("Poison Dart", 5, 0),
        [101] = ("Hatchet", 5, 0),
        [102] = ("Javelin", 5, 90),
    };

    /// <summary>The weapons that fly: the bow list's with the arrow, then the thrown list's with their own models (<c>LoadWeaponScene</c>).</summary>
    private static List<Missile> Missiles(GameDb db, ItemTables items, List<int> bows, List<int> thrown)
    {
        var listed = bows.Concat(thrown).ToList();
        if (listed.Count != Muzzles.Count || listed.Except(Muzzles.Keys).Any())
            throw new InvalidDataException($"The weapons that fly are {string.Join(", ", listed)}, but the facts are for {string.Join(", ", Muzzles.Keys)}.");
        return [.. bows.Select(p => Flying(p, true)).Concat(thrown.Select(p => Flying(p, false)))];

        Missile Flying(int position, bool bow)
        {
            var (name, speed, turn) = Muzzles[position];
            // A weapon with no model loads no scene at all, so it couldn't fly.
            if (!items.WeaponModels.ContainsKey(position))
                throw new InvalidDataException($"{name} (position {position}) flies but has no model.");
            return new(Weapon(db, position, name), bow ? RecordId.Hex(RecordTypes.Scene, ArrowScene) : null, speed, turn);
        }
    }
}
