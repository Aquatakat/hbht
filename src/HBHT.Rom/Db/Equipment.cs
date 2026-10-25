using HBHT.Plugins.Records;

namespace HBHT.Rom.Db;

/// <summary>
/// The 14-byte block that ends every weapon, armor and gear record: what the item does for
/// whoever equips it.
/// </summary>
/// <param name="Stat">Attribute raised or lowered: 0–5 as in <see cref="RomEntityRecord.Stats"/> (5 is stamina, the spell battery), or 0xFF for none.</param>
/// <param name="Skill">Skill raised or lowered: 0x00–0x0B the skills, 0x0C–0x16 the weapon skills, 0x20–0x25 the attributes, or 0xFF for none.</param>
/// <param name="Stat">
/// Attribute raised or lowered: 0–5 as in <see cref="RomEntityRecord.Stats"/>, or 0xFF for none. Stamina (5)
/// isn't raised: it's a battery that casting spells draws on first (<c>Entity::EquipStamina</c>).
/// </param>
/// <param name="Skill">
/// Skill raised or lowered, or 0xFF for none. The code indexes the 24 bytes of skills, weapon skills
/// and the shield with it, but the data only names skills 0–11.
/// </param>
/// <param name="Spell">A spell the item lets its holder cast, as on wands and scrolls.</param>
/// <param name="SpellCharges">The spell's charges (<c>malloc_equip_spell</c>'s count).</param>
/// <param name="SpellLevel">
/// The spell's level, on armor and gear. A weapon's is <see cref="RomWeaponRecord.SpellLevel"/>, and
/// this byte is unread (<c>InitWeapon</c>). 10 on the Stardrake shield.
/// </param>
/// <param name="Magic">A spell that's always on while the item is worn, with its own level.</param>
/// <param name="ResistAmount">The game multiplies this by 0.25. AidynEditor reads it as 100 − 25 × value percent resistance.</param>
public sealed record EquipmentBonuses(
    Aspect Aspect,
    byte Stat,
    sbyte StatBonus,
    byte Skill,
    sbyte SkillBonus,
    ItemId Spell,
    byte SpellCharges,
    byte SpellLevel,
    ItemId Magic,
    byte MagicLevel,
    Element Resist,
    byte ResistAmount)
{
    internal static EquipmentBonuses Read(ref RecordReader r) => new(
        (Aspect)r.U8(), r.U8(), r.S8(), r.U8(), r.S8(), r.Id(), r.U8(), r.U8(), r.Id(), r.U8(), (Element)r.U8(), r.U8());
}

/// <summary>A <c>WeaponDB</c> record (0x30 bytes).</summary>
/// <param name="Range">The game multiplies this by 5/3 on load, capped at 255.</param>
/// <param name="SpellLevel">
/// The level of the weapon's <see cref="EquipmentBonuses.Spell"/>, which a weapon reads here rather than
/// in its bonuses (<c>InitWeapon</c>, checked in the debug build's machine code).
/// </param>
/// <param name="ExpModifier">0x20 on the Lodin sword.</param>
public sealed record RomWeaponRecord(
    ItemId Id,
    string Name,
    WeaponClass Class,
    byte RequiredStrength,
    byte Hit,
    byte Damage,
    ushort Price,
    byte SpellLevel,
    byte Range,
    byte Animation,
    byte ExpModifier,
    Element DamageType,
    EquipmentBonuses Bonuses)
{
    public const int Size = 0x30;

    internal static RomWeaponRecord Read(ref RecordReader r)
    {
        var name = r.Name(21);
        return new RomWeaponRecord(
            r.Id(), name, (WeaponClass)r.U8(), r.U8(), r.U8(), r.U8(), r.U16Le(),
            r.U8(), r.U8(), r.U8(), r.U8(), (Element)r.U8(), EquipmentBonuses.Read(ref r));
    }
}

/// <summary>An <c>ArmorDB</c> record (0x30 bytes): body armor or a shield.</summary>
/// <param name="Slot">0 for body armor, 1 for a shield.</param>
/// <param name="Dexterity">What wearing it adds to Dexterity, as a signed byte: a penalty, or 0.</param>
/// <param name="Stealth">What wearing it adds to Stealth, as a signed byte.</param>
public sealed record RomArmorRecord(
    ItemId Id,
    string Name,
    byte Slot,
    byte Defense,
    byte Protection,
    sbyte Dexterity,
    byte Unk1D,
    sbyte Stealth,
    ushort Price,
    byte ExpBonus,
    EquipmentBonuses Bonuses)
{
    public const int Size = 0x30;

    internal static RomArmorRecord Read(ref RecordReader r)
    {
        var name = r.Name(23);
        return new RomArmorRecord(
            r.Id(), name, r.U8(), r.U8(), r.U8(), r.S8(), r.U8(), r.S8(), r.U16Le(), r.U8(),
            EquipmentBonuses.Read(ref r));
    }
}

/// <summary>
/// An <c>ItemDB</c> record (0x2C bytes): accessories, wands, scrolls, key items and the rest
/// of the gear that isn't a weapon or armor.
/// </summary>
public sealed record GearRecord(
    ItemId Id,
    string Name,
    ushort ExpMultiplier,
    byte Damage,
    byte Protection,
    byte RequiredStrength,
    byte RequiredIntelligence,
    ushort Price,
    EquipmentBonuses Bonuses)
{
    public const int Size = 0x2C;

    internal static GearRecord Read(ref RecordReader r)
    {
        var name = r.Name(20);
        return new GearRecord(
            r.Id(), name, r.U16Le(), r.U8(), r.U8(), r.U8(), r.U8(), r.U16Le(), EquipmentBonuses.Read(ref r));
    }
}
