using System.Globalization;
using HBHT.Plugins;

namespace HBHT.TestSupport;

/// <summary>Shorthands for the record IDs tests name over and over. The test projects import them as static usings.</summary>
public static class Ids
{
    /// <summary>Flag <paramref name="n"/>, <c>aidyn:flag/n</c>.</summary>
    public static RecordId Flag(int n) => RecordId.Decimal(RecordTypes.Flag, n);

    /// <summary>Entity <paramref name="n"/>, <c>aidyn:entity/0x…</c>.</summary>
    public static RecordId Entity(int n) => RecordId.Hex(RecordTypes.Entity, n);

    /// <summary>The script in Borg13 archive index <paramref name="borg"/>, <c>aidyn:dialogue/0x…</c>.</summary>
    public static RecordId Script(int borg) => RecordId.Hex(RecordTypes.Dialogue, borg);

    /// <summary>The base game's potion or flask <paramref name="potion"/>, <c>aidyn:item/0x10nn</c>.</summary>
    public static RecordId PotionId(BasePotion potion) => RecordId.Hex(RecordTypes.Item, 0x1000 + (int)potion);

    /// <summary>A flag's number.</summary>
    public static int Number(RecordId flag) => int.Parse(flag.Key, CultureInfo.InvariantCulture);
}

/// <summary>
/// Shorthands for the record IDs of items, stats, skills, schools, elements and requests (ADRs 0031–0033). Not imported statically, as
/// <see cref="Ids"/> is: their names are the core's types' (<c>Stat</c>, <c>Item</c>), so tests write <c>RecordIds.Stat(4)</c>.
/// </summary>
public static class RecordIds
{
    /// <summary>Item <paramref name="n"/>, <c>aidyn:item/0x…</c>: weapons 0x07…, body armor 0x05…, shields 0x06… and the rest (ADR 0031).</summary>
    public static RecordId Item(int n) => RecordId.Hex(RecordTypes.Item, n);

    /// <summary>Stat <paramref name="n"/>, <c>aidyn:stat/n</c>: 0 Intelligence to 6 Level.</summary>
    public static RecordId Stat(int n) => RecordId.Decimal(RecordTypes.Stat, n);

    /// <summary>Skill <paramref name="n"/>, <c>aidyn:skill/n</c>: 0 Alchemist to 11 Wizard.</summary>
    public static RecordId Skill(int n) => RecordId.Decimal(RecordTypes.Skill, n);

    /// <summary>Weapon skill <paramref name="n"/>, <c>aidyn:weaponskill/n</c>: 0 Bite to 10 Tusk, and 11 the shield.</summary>
    public static RecordId WeaponSkill(int n) => RecordId.Decimal(RecordTypes.WeaponSkill, n);

    /// <summary>School <paramref name="n"/>, <c>aidyn:school/n</c>: 0 Chaos, 1 Elemental, 2 Naming, 3 Necromancy, 4 unschooled, 5 Star.</summary>
    public static RecordId School(int n) => RecordId.Decimal(RecordTypes.School, n);

    /// <summary>Element <paramref name="n"/>, <c>aidyn:element/n</c> (1 Earth to 16 Holy).</summary>
    public static RecordId Element(int n) => RecordId.Decimal(RecordTypes.Element, n);

    /// <summary>Animation request <paramref name="n"/>, <c>aidyn:request/n</c>.</summary>
    public static RecordId Request(int n) => RecordId.Decimal(RecordTypes.Request, n);
}

/// <summary>
/// The base game's potions and flasks, by the low byte of their item ID (the original's <c>PotionEnum</c>): potion <c>n</c> is
/// <c>aidyn:item/0x10nn</c>. The core knows potions only by their records (ADR 0021); tests name the base game's by these.
/// </summary>
public enum BasePotion
{
    FireFlask,
    InfernoFlask,
    SleepGas,
    AcidFlask,
    Healing,
    Stamina,
    Curing,
    Antidote,
    Restore,
    Strength,
    Dexterity,
    /// <summary>Left out of the game: gaining one gives a Healing potion.</summary>
    Aspect,
    /// <summary>Left out of the game.</summary>
    Jumping,
    Clarity,
    Charisma,
    Defence,
    Stealth,
}
