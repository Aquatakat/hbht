using HBHT.Plugins.Records;

namespace HBHT.Rom.Db;

/// <summary>An element resistance: the element and a step, as in <see cref="EquipmentBonuses.ResistAmount"/>.</summary>
public readonly record struct RomResistance(Element Element, byte Amount);

/// <summary>An <c>EntityDB</c> record (0x88 bytes): party members, NPCs and enemies.</summary>
/// <param name="InternalName">A second name field. The game doesn't copy it to RAM.</param>
/// <param name="Category">The decomp says only the Chaos value is checked.</param>
/// <param name="Unk2B">Copied to RAM but apparently unused.</param>
/// <param name="HasTrueName">Nonzero for true. Kept raw, like <paramref name="IsHeavy"/>.</param>
/// <param name="IsHeavy">Nonzero for true. Mostly 0 or 1, but a few records hold other values (Alaron's is 220).</param>
/// <param name="Skills">Base skill levels, in skill order (Alchemist to Wizard). 0xFF means the entity can't learn it.</param>
/// <param name="WeaponSkills">Base weapon skill levels, indexed by <see cref="WeaponClass"/>. 0xFF means can't learn.</param>
/// <param name="Stats">Base attributes: intelligence, willpower, dexterity, endurance, strength, stamina.</param>
/// <param name="Morale">How likely the entity is to flee combat.</param>
/// <param name="Unk4E">Called <c>BaseDamage</c> in the decomp's RAM struct.</param>
/// <param name="Unk55">Copied to RAM; the decomp ties it to monster vision, with values 10 or 0.</param>
/// <param name="Unk67">Nine bytes the game copies to RAM but doesn't use.</param>
/// <param name="ShieldSkill">0xFF on party members means can't learn; enemies use 0.</param>
/// <param name="RitualIds">Starting rituals from early builds. Unused in the final game.</param>
/// <param name="ExpReward">The game multiplies this by 50 on load, and by 1.5 again when awarding it.</param>
/// <param name="LootCategory">The index of a <see cref="RomLootRecord"/>.</param>
public sealed record RomEntityRecord(
    ItemId Id,
    string Name,
    string InternalName,
    byte Category,
    byte Unk2B,
    Aspect Aspect,
    byte HasTrueName,
    byte IsHeavy,
    IReadOnlyList<byte> Skills,
    IReadOnlyList<byte> WeaponSkills,
    IReadOnlyList<byte> Stats,
    byte Morale,
    byte Level,
    byte Unk4E,
    IReadOnlyList<ItemId> Weapons,
    byte Unk55,
    byte Unk56,
    IReadOnlyList<ItemId> Spells,
    MagicSchool School,
    IReadOnlyList<byte> SpellLevels,
    IReadOnlyList<byte> Unk67,
    ItemId Armor,
    byte BaseProtection,
    ItemId Shield,
    byte ShieldSkill,
    IReadOnlyList<RomResistance> Resistances,
    IReadOnlyList<ItemId> RitualIds,
    IReadOnlyList<byte> RitualLevels,
    byte ExpReward,
    byte LootCategory)
{
    public const int Size = 0x88;

    internal static RomEntityRecord Read(ref RecordReader r)
    {
        var name = r.Name(20);
        var internalName = r.Name(20);
        var category = r.U8();
        return new RomEntityRecord(
            Id: r.Id(),
            Name: name,
            InternalName: internalName,
            Category: category,
            Unk2B: r.U8(),
            Aspect: (Aspect)r.U8(),
            HasTrueName: r.U8(),
            IsHeavy: r.U8(),
            Skills: r.Array(12),
            WeaponSkills: r.Array(11),
            Stats: r.Array(6),
            Morale: r.U8(),
            Level: r.U8(),
            Unk4E: r.U8(),
            Weapons: r.Ids(3),
            Unk55: r.U8(),
            Unk56: r.U8(),
            Spells: r.Ids(5),
            School: (MagicSchool)r.U8(),
            SpellLevels: r.Array(5),
            Unk67: r.Array(9),
            Armor: r.Id(),
            BaseProtection: r.U8(),
            Shield: r.Id(),
            ShieldSkill: r.U8(),
            Resistances: [new((Element)r.U8(), r.U8()), new((Element)r.U8(), r.U8())],
            RitualIds: r.Ids(4),
            RitualLevels: r.Array(4),
            ExpReward: r.U8(),
            LootCategory: r.U8());
    }
}

/// <summary>A <c>DialogEntity</c> record (0x18 bytes): signs, animals and other simple speakers.</summary>
public sealed record DialogEntityRecord(ItemId Id, string Name)
{
    public const int Size = 0x18;

    internal static DialogEntityRecord Read(ref RecordReader r)
    {
        var name = r.Name(22);
        return new DialogEntityRecord(r.Id(), name);
    }
}
