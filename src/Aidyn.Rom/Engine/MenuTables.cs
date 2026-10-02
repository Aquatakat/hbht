using Aidyn.Rom.Db;

namespace Aidyn.Rom.Engine;

/// <summary>
/// The tables in code that the pause menu reads (roadmap M2): spells' symbols, the lines that describe
/// items, potions and spells, the inventory's order of kinds, and the equipment slots' empty images.
/// </summary>
/// <param name="SpellSymbols">
/// Each spell's icon symbol, a key of <see cref="ItemTables.SpellSymbolIcons"/>, for every spell in
/// <c>SpellList</c>. A spell the symbol table doesn't list has 0 (<c>SpellDB::GetIcon</c>).
/// </param>
/// <param name="PotionDescriptions">Each potion's line in the PotionDetails string table.</param>
/// <param name="ItemDescriptions">Each item's line in the Items string table.</param>
/// <param name="SpellDescriptions">Each spell's line in the Spells string table. Four spells have none.</param>
/// <param name="InventoryOrder">The DB types, as <see cref="DbType"/> values, in the order the inventory list shows them.</param>
/// <param name="EquipmentSlotImages">The Borg8 image of each equipment slot while it's empty, by slot.</param>
/// <param name="Messages">The inventory's messages that are in code rather than a string table.</param>
public sealed record MenuTables(
    IReadOnlyDictionary<ItemId, int> SpellSymbols,
    IReadOnlyDictionary<ItemId, int> PotionDescriptions,
    IReadOnlyDictionary<ItemId, int> ItemDescriptions,
    IReadOnlyDictionary<ItemId, int> SpellDescriptions,
    IReadOnlyList<int> InventoryOrder,
    IReadOnlyList<ushort> EquipmentSlotImages,
    CodeMessages Messages)
{
    public static MenuTables Read(RomImage rom)
    {
        // SpellSymbols: (position in SpellList, symbol) byte pairs, ending with 0xFF.
        var list = CodeSegment.Table(rom, CodeTable.SpellList).ToArray();
        var symbols = list.Select(_ => 0).ToArray();
        var pairs = CodeSegment.Table(rom, CodeTable.SpellSymbols).ToArray();
        var end = false;
        for (var at = 0; at + 1 < pairs.Length && !end; at += 2)
        {
            if (pairs[at] == 0xFF)
                end = true;
            else if (pairs[at] < symbols.Length)
                symbols[pairs[at]] = pairs[at + 1];
            else
                throw new InvalidDataException($"A spell symbol is for position {pairs[at]}, past the {list.Length} spells.");
        }
        if (!end)
            throw new InvalidDataException("The spell symbols have no terminator.");

        var spellSymbols = Enumerable.Range(0, list.Length)
            .ToDictionary(i => ItemId.Of(DbType.Spell, list[i]), i => symbols[i]);

        // InventoryOrder: u16 types ending with 0.
        var order = CodeSegment.Table(rom, CodeTable.InventoryOrder).ToArray();
        var kinds = new List<int>();
        for (var at = 0; at + 1 < order.Length && CodeSegment.U16(order, at) != 0; at += 2)
            kinds.Add(CodeSegment.U16(order, at));

        // EquipmentSlotImages: u32 Borg8 indices.
        var slots = CodeSegment.Table(rom, CodeTable.EquipmentSlotImages).ToArray();
        var slotImages = Enumerable.Range(0, slots.Length / 4).Select(i => checked((ushort)CodeSegment.U32(slots, i * 4))).ToList();

        return new MenuTables(spellSymbols, Descriptions(rom, CodeTable.PotionDescriptions), Descriptions(rom, CodeTable.ItemDescriptions),
            Descriptions(rom, CodeTable.SpellDescriptions), kinds, slotImages,
            new CodeMessages(Text(rom, CodeTable.PotionNotNow), Text(rom, CodeTable.PotionNotInTrek), Text(rom, CodeTable.ScrollNoSpell)));
    }

    /// <summary>A C string: ASCII up to its terminator, which the table's range includes.</summary>
    private static string Text(RomImage rom, CodeTable table)
    {
        var data = CodeSegment.Table(rom, table);
        var end = data.IndexOf((byte)0);
        return end < 0
            ? throw new InvalidDataException($"The {table} string has no terminator.")
            : System.Text.Encoding.ASCII.GetString(data[..end]);
    }

    /// <summary>(ID, line) u16 pairs, ending with a 0 ID.</summary>
    private static Dictionary<ItemId, int> Descriptions(RomImage rom, CodeTable table)
    {
        var data = CodeSegment.Table(rom, table).ToArray();
        var lines = new Dictionary<ItemId, int>();
        for (var at = 0; at + 3 < data.Length && CodeSegment.U16(data, at) != 0; at += 4)
            lines.Add(new ItemId(CodeSegment.U16(data, at)), CodeSegment.U16(data, at + 2));
        return lines;
    }
}

/// <summary>The inventory's messages that are literals in code (<c>Entity::CanUsePotion</c>, <c>Party::UseScroll</c>).</summary>
/// <param name="PotionNotNow">A potion that would do nothing now: one that heals at full health, and so on.</param>
/// <param name="PotionNotInTrek">A flask, or a potion with no effect, drunk outside combat.</param>
/// <param name="ScrollNoSpell">A scroll without a spell, read: the Lighthouse scroll.</param>
public sealed record CodeMessages(string PotionNotNow, string PotionNotInTrek, string ScrollNoSpell);
