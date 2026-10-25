using HBHT.Rom.Db;

namespace HBHT.Rom.Engine;

/// <summary>
/// The code's position lists (<c>EntityList</c>, <c>DialougEntList</c>, <c>SpellList</c>, <c>ArmorList</c>, <c>weaponList</c>,
/// <c>itemID_array</c>): tables that name a DB record by its place, which the code's other tables key their rows by. Each is the
/// DB's own order and nothing else: place p holds the ID of the p-th record (its low byte for all but the items', which hold the
/// whole ID), so the importer's <c>db.Weapons[position]</c> is the code's <c>weaponList[position]</c>. They add no content, so they
/// aren't imported; <see cref="Verify"/> checks that in every ROM version.
/// </summary>
public static class PositionLists
{
    /// <summary>
    /// Throws if a list isn't its DB's IDs in order, naming the first place that differs. The entity, dialogue entity and spell
    /// lists' places are the DB's (<see cref="EntityModels"/> and <see cref="MenuTables"/> rely on that); the armor, weapon and item
    /// lists have the same layout. The armor list holds armor and shields, which the armor DB holds together.
    /// </summary>
    public static void Verify(RomImage rom, GameDb db)
    {
        Check(rom, CodeTable.EntityList, db.Entities.Select(r => r.Id), wide: false);
        Check(rom, CodeTable.DialogEntityList, db.DialogEntities.Select(r => r.Id), wide: false);
        Check(rom, CodeTable.SpellList, db.Spells.Select(r => r.Id), wide: false);
        Check(rom, CodeTable.ArmorList, db.Armor.Select(r => r.Id), wide: false);
        Check(rom, CodeTable.WeaponList, db.Weapons.Select(r => r.Id), wide: false);
        Check(rom, CodeTable.ItemList, db.Gear.Select(r => r.Id), wide: true);
    }

    private static void Check(RomImage rom, CodeTable table, IEnumerable<ItemId> ids, bool wide)
    {
        var data = rom.Table(table).ToArray();
        var expected = ids.ToList();
        var width = wide ? 2 : 1;
        // A table's range may end in padding; the list is the first Count entries.
        if (data.Length < expected.Count * width)
            throw new InvalidDataException($"The {table} list holds {data.Length / width} places, the DB {expected.Count} records.");
        for (var place = 0; place < expected.Count; place++)
        {
            var listed = wide ? Be.U16(data, place * 2) : data[place];
            var id = wide ? expected[place].Value : expected[place].Index;
            if (listed != id)
                throw new InvalidDataException($"The {table} list names 0x{listed:X} at place {place}, where the DB has {expected[place]}.");
        }
    }
}
