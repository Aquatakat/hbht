namespace HBHT.Rom.Db;

/// <summary>One item roll in a loot table: a percent chance of between Min and Max of the item.</summary>
public readonly record struct RomLootDrop(ItemId Item, byte Chance, byte Min, byte Max);

/// <summary>A <c>ChestDB</c> record (0x38 bytes): what an enemy drops or a chest holds.</summary>
/// <param name="GoldMin">The game rolls between the two and doubles the result.</param>
/// <param name="WeaponChances">Percent chances of the three weapon drops.</param>
/// <param name="ReagentChance">Percent chance of a random reagent (spice, herb or gemstone).</param>
/// <param name="Drops">
/// Six item rolls. Only the first two store a quantity range; the ROM has no range for the
/// last four, which always give one, so they're read as 1–1.
/// </param>
public sealed record RomLootRecord(
    ItemId Id,
    string Name,
    ushort GoldMin,
    ushort GoldMax,
    byte ArmorChance,
    byte ShieldChance,
    IReadOnlyList<byte> WeaponChances,
    byte ReagentChance,
    byte ReagentMin,
    byte ReagentMax,
    IReadOnlyList<RomLootDrop> Drops)
{
    public const int Size = 0x38;

    internal static RomLootRecord Read(ref RecordReader r)
    {
        var name = r.Name(20);
        var id = r.Id();
        var goldMin = r.U16Le();
        var goldMax = r.U16Le();
        var armor = r.U8();
        var shield = r.U8();
        var weapons = r.Array(3);
        var reagentChance = r.U8();
        var reagentMin = r.U8();
        var reagentMax = r.U8();

        var drops = new RomLootDrop[6];
        for (var i = 0; i < drops.Length; i++)
            drops[i] = i < 2 ? new(r.Id(), r.U8(), r.U8(), r.U8()) : new(r.Id(), r.U8(), 1, 1);

        return new RomLootRecord(id, name, goldMin, goldMax, armor, shield, weapons, reagentChance, reagentMin, reagentMax, drops);
    }
}
