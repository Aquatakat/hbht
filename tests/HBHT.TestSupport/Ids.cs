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
