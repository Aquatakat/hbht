using HBHT.Rom.Text;

namespace HBHT.Rom.Db;

/// <summary>Every fixed database and text table in a ROM's DB block (roadmap 0.4).</summary>
/// <remarks>
/// Records are in ROM order. Five tables split their records into groups, and each group
/// matches a field: entity <see cref="RomEntityRecord.Category"/>, weapon class, armor slot,
/// spell school and gear ID type. The groups are read and flattened, since the field says
/// which group a record is in.
/// </remarks>
public sealed record GameDb(
    IReadOnlyList<RomEntityRecord> Entities,
    IReadOnlyList<RomWeaponRecord> Weapons,
    IReadOnlyList<RomArmorRecord> Armor,
    IReadOnlyList<RomSpellRecord> Spells,
    IReadOnlyList<GearRecord> Gear,
    IReadOnlyList<RomLootRecord> Loot,
    IReadOnlyList<RomShopRecord> Shops,
    IReadOnlyList<DialogEntityRecord> DialogEntities,
    GameStateFunnelTable GameStateFunnel,
    IReadOnlyList<FlaggedText> Journal,
    IReadOnlyList<FlaggedText> CinematicTitles,
    IReadOnlyList<IReadOnlyList<Caption>> Captions,
    IReadOnlyList<CampEncounter> CampEncounters,
    IReadOnlyDictionary<RomStringTable, IReadOnlyList<string>> Strings)
{
    public static GameDb Read(RomImage rom) => new(
        Grouped(rom, DbTable.Entities, 7, RomEntityRecord.Size, RomEntityRecord.Read),
        Grouped(rom, DbTable.Weapons, 11, RomWeaponRecord.Size, RomWeaponRecord.Read),
        Grouped(rom, DbTable.Armor, 2, RomArmorRecord.Size, RomArmorRecord.Read),
        Grouped(rom, DbTable.Spells, 6, RomSpellRecord.Size, RomSpellRecord.Read),
        Grouped(rom, DbTable.Items, 11, GearRecord.Size, GearRecord.Read),
        Flat(rom, DbTable.Chests, RomLootRecord.Size, RomLootRecord.Read),
        Flat(rom, DbTable.Shops, RomShopRecord.Size, RomShopRecord.Read),
        Flat(rom, DbTable.DialogEntities, DialogEntityRecord.Size, DialogEntityRecord.Read),
        GameStateFunnelTable.Read(rom.Table(DbTable.GameStateFunnel)),
        FlaggedText.ReadList(rom.Table(DbTable.Journal), wideLengths: true),
        FlaggedText.ReadList(rom.Table(DbTable.TheatreMenu), wideLengths: false),
        CinematicText.Read(rom.Table(DbTable.CinematicTextIndex), rom.Table(DbTable.CinematicText)),
        CampEncounter.ReadAll(rom.Table(DbTable.CampEncounters)),
        rom.Layout.StringTables.Keys.Order().ToDictionary(t => t, t => RomStringBlock.Read(rom, t)));

    private delegate T RecordParser<out T>(ref RecordReader reader);

    /// <summary>A table whose header count is followed directly by its records.</summary>
    private static IReadOnlyList<T> Flat<T>(RomImage rom, DbTable table, int size, RecordParser<T> parse)
    {
        var r = new RecordReader(rom.Table(table));
        var records = new T[r.Count()];
        for (var i = 0; i < records.Length; i++)
            records[i] = ReadRecord(ref r, size, parse);
        return records;
    }

    /// <summary>A table with a total count, then a count before each group's records.</summary>
    private static IReadOnlyList<T> Grouped<T>(RomImage rom, DbTable table, int groupCount, int size, RecordParser<T> parse)
    {
        var r = new RecordReader(rom.Table(table));
        var total = r.Count();
        var records = new List<T>(total);
        for (var g = 0; g < groupCount; g++)
        {
            var count = r.Count();
            for (var i = 0; i < count; i++)
                records.Add(ReadRecord(ref r, size, parse));
        }

        if (records.Count != total)
            throw new InvalidDataException($"{table} header says {total} records, but its groups hold {records.Count}.");
        return records;
    }

    private static T ReadRecord<T>(ref RecordReader r, int size, RecordParser<T> parse)
    {
        var start = r.Position;
        var record = parse(ref r);
        if (r.Position - start != size)
            throw new InvalidOperationException($"{typeof(T).Name} parser read {r.Position - start} bytes, not {size}.");
        return record;
    }
}
