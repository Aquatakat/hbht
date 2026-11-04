using HBHT.Plugins.Records;

namespace HBHT.Rom.Db;

/// <summary>The reagent a spell uses up, as the spell DB names it: an index into the reagents' items (<c>0x800E29CC</c>).</summary>
public enum Reagent
{
    None,
    Spice,
    Herb,
    Gemstone,
}

/// <summary>A <c>SpellDB</c> record (0x24 bytes).</summary>
/// <param name="Special">AidynEditor calls it the aspect. The decomp's <c>MagicSpecial</c> has 1 true name, 2 heavy, 3 solar, 4 lunar.</param>
public sealed record RomSpellRecord(
    ItemId Id,
    string Name,
    MagicSchool School,
    byte Damage,
    byte StaminaCost,
    SpellTargetCount TargetCount,
    SpellTargetType TargetType,
    byte Unk1E,
    byte WizardRequired,
    byte Special,
    byte Range,
    Reagent Reagent,
    byte ExpModifier)
{
    public const int Size = 0x24;

    internal static RomSpellRecord Read(ref RecordReader r)
    {
        var name = r.Name(23);
        return new RomSpellRecord(
            r.Id(), name, (MagicSchool)r.U8(), r.U8(), r.U8(), (SpellTargetCount)r.U8(), (SpellTargetType)r.U8(),
            r.U8(), r.U8(), r.U8(), r.U8(), (Reagent)r.U8(), r.U8());
    }
}
