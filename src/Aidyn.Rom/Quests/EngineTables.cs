using System.Buffers.Binary;
using Aidyn.Rom.Db;

namespace Aidyn.Rom.Quests;

/// <summary>A recruitable member's flags. The game keeps them in step with the party.</summary>
/// <param name="Joined">Set when the member joins, and cleared when they're removed alive.</param>
/// <param name="Left">Cleared when the member joins, and set when they're removed alive. 0 for Brenna, who never leaves.</param>
/// <param name="Died">Set while the member is in the party and dead.</param>
public sealed record PartyMemberFlags(ItemId Member, ushort Joined, ushort Left, ushort Died);

/// <summary>A key item: it gets its own inventory slot, and a flag that says the party has it.</summary>
/// <param name="Held">Set when the party gains the item, and cleared when it has none left.</param>
/// <param name="Region">A world map region flag that gaining the item also sets, or 0.</param>
public sealed record KeyItemFlags(ItemId Item, ushort Held, ushort Region);

/// <summary>Entering this overworld chunk sets the flag of the world map region it's in.</summary>
public sealed record RegionChunk(int Row, int Column, ushort Flag);

/// <summary>
/// Content tables that engine code keeps in the code segment (roadmap 1H), read from the ROM.
/// Flags the code names directly, rather than through a table, are in <see cref="EngineFlags"/>.
/// </summary>
/// <param name="EntityList">
/// The entity index the code's tables use, as the entity it names. The code looks an entity up
/// by its position in this list, not by its ID.
/// </param>
/// <param name="CampDialogueFlags">
/// When the party camps, a dialogue object nearby whose flag A is one of these starts instead of
/// the usual campfire.
/// </param>
/// <param name="ScriptBattleFlags">
/// Winning the battle one of these scripts starts sets the flag. Other scripts' battles leave
/// the encounter flag as it was.
/// </param>
public sealed record EngineTables(
    IReadOnlyList<ItemId> EntityList,
    IReadOnlyList<PartyMemberFlags> Party,
    IReadOnlyList<KeyItemFlags> KeyItems,
    IReadOnlyList<RegionChunk> Regions,
    IReadOnlyList<ushort> CampDialogueFlags,
    IReadOnlyDictionary<int, ushort> ScriptBattleFlags)
{
    private const ushort End = 0xFFFF;

    public static EngineTables Read(RomImage rom)
    {
        var entityList = Table(rom, CodeTable.EntityList).ToArray()
            .Select(b => ItemId.Of(DbType.Entity, b)).ToList();

        var joins = Rows(U16s(Table(rom, CodeTable.PartyFlags)), 3, CodeTable.PartyFlags);
        var deaths = Rows(U16s(Table(rom, CodeTable.PartyDeathFlags)), 2, CodeTable.PartyDeathFlags)
            .ToDictionary(r => r[0], r => r[1]);
        if (joins.Count != deaths.Count || joins.Any(r => !deaths.ContainsKey(r[0])))
            throw new InvalidDataException("The party and death flag tables name different members.");
        var party = joins.Select(r => new PartyMemberFlags(entityList[r[0]], r[1], r[2], deaths[r[0]])).ToList();

        var held = U16s(Table(rom, CodeTable.KeyItemFlags));
        var items = U16s(Table(rom, CodeTable.KeyItems));
        var regions = U16s(Table(rom, CodeTable.KeyItemRegionFlags));
        if (held.Length != items.Length || regions.Length != items.Length)
            throw new InvalidDataException("The key item tables differ in length.");
        var keyItems = items.Select((item, i) => new KeyItemFlags(new ItemId(item), held[i], regions[i])).ToList();

        // Entered chunks are matched until an entry with a zero field.
        var regionData = Table(rom, CodeTable.RegionFlags);
        var chunks = new List<RegionChunk>();
        for (var at = 0; ; at += 12)
        {
            var row = U32(regionData, at);
            var column = U32(regionData, at + 4);
            var flag = U32(regionData, at + 8);
            if (row == 0 || column == 0 || flag == 0)
                break;
            chunks.Add(new RegionChunk((int)row, (int)column, (ushort)flag));
        }

        var camp = U16s(Table(rom, CodeTable.CampDialogueFlags)).TakeWhile(f => f != End).ToList();
        var battles = Rows(U16s(Table(rom, CodeTable.ScriptBattleFlags)), 2, CodeTable.ScriptBattleFlags)
            .ToDictionary(r => (int)r[0], r => r[1]);

        return new EngineTables(entityList, party, keyItems, chunks, camp, battles);
    }

    private static ReadOnlySpan<byte> Table(RomImage rom, CodeTable table) =>
        rom.Layout.CodeTables.TryGetValue(table, out var range)
            ? rom.Slice(range)
            : throw new NotSupportedException($"{rom.Version.Name} has no verified location for the {table} table.");

    private static ushort[] U16s(ReadOnlySpan<byte> data)
    {
        var values = new ushort[data.Length / 2];
        for (var i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(i * 2)..]);
        return values;
    }

    private static uint U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

    /// <summary>Fixed-width rows up to the one that starts with 0xFFFF, which must be the table's last.</summary>
    private static List<ushort[]> Rows(ushort[] values, int width, CodeTable table)
    {
        var rows = values.Chunk(width).ToList();
        var end = rows.FindIndex(r => r[0] == End);
        if (end != rows.Count - 1)
            throw new InvalidDataException($"The {table} table doesn't end where expected.");
        return rows[..end];
    }
}
