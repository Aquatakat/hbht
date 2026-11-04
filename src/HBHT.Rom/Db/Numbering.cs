namespace HBHT.Rom.Db;

/// <summary>The magic schools as the DB numbers them; the importer writes each as <c>aidyn:school/n</c> (ADR 0033).</summary>
public enum MagicSchool
{
    /// <summary>On an entity, every school: it can learn any spell.</summary>
    Chaos = 0,
    Elemental = 1,
    Naming = 2,
    Necromancy = 3,
    /// <summary>On an entity, "Neutral": it can learn only unschooled spells. On a spell, unschooled.</summary>
    None = 4,
    Star = 5,
}

/// <summary>The damage and resistance elements as the DB numbers them; the importer writes each but None as <c>aidyn:element/n</c> (ADR 0033).</summary>
public enum Element
{
    None,
    Earth,
    Solar,
    /// <summary>A resistance to it also covers <see cref="Cutting"/> and <see cref="Smashing"/>.</summary>
    Physical,
    Necromancy,
    Fire,
    Lunar,
    Naming,
    Water,
    /// <summary>A resistance to it covers the magical elements.</summary>
    Magic,
    Air,
    Star,
    Elemental,
    Chaos,
    Cutting,
    Smashing,
    Holy,
}

/// <summary>The weapon classes as the DB numbers them, which are the weapon skills: the importer writes each as <c>aidyn:weaponskill/n</c> (ADR 0033).</summary>
public enum WeaponClass
{
    Bite,
    Breath,
    Claw,
    Hafted,
    Missile,
    Pole,
    Spit,
    Sting,
    Sword,
    Thrown,
    Tusk,
}

/// <summary>The record IDs the DB's numbers become.</summary>
public static class Numbering
{
    public static Plugins.RecordId School(MagicSchool school) => Plugins.RecordId.Decimal(Plugins.RecordTypes.School, (int)school);

    /// <summary>An element's ID, or null for none.</summary>
    public static Plugins.RecordId? Element(Element element) =>
        element == Db.Element.None ? null : Plugins.RecordId.Decimal(Plugins.RecordTypes.Element, (int)element);

    /// <summary>The school a base school's ID names, or <see cref="MagicSchool.None"/> for a mod's.</summary>
    public static MagicSchool SchoolOf(Plugins.RecordId? school) =>
        school is { Namespace: Plugins.RecordId.BaseNamespace } s && int.TryParse(s.Key, out var n) && Enum.IsDefined((MagicSchool)n)
            ? (MagicSchool)n : MagicSchool.None;

    public static Plugins.RecordId WeaponSkill(WeaponClass weapon) => Plugins.RecordId.Decimal(Plugins.RecordTypes.WeaponSkill, (int)weapon);
}
