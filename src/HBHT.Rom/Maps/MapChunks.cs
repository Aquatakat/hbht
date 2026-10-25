using System.Globalization;
using System.Numerics;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Maps;

/// <summary>
/// A set of map chunks the game loads as one world (the decomp's <c>EnumMapDatA</c>). Interiors
/// and dungeons are separate sets that teleporters move between.
/// </summary>
public enum MapSet : byte
{
    Overworld,
    GoblinCamp,
    PortSaiid,
    Lighthouse,
    GwerniaCastle,
    GwerniaInteriors,
    MiscInteriors,
    Erromon,
    Terminor,
    Barrows,
    RoogCave,
    JundarInteriors,
    EhudUnder,
    Ugarit,
    Ugarit2,
    ChaosIsle,
    Battlefields,
}

/// <summary>
/// One map chunk: a grid cell of a <see cref="MapSet"/>, with its scenery and collision. The game
/// keeps the 3×3 chunks around the player loaded.
/// </summary>
/// <param name="Row">1-based. The debug overlay shows it as a letter, so row 8 is H.</param>
/// <param name="Scene">The Borg5 drawn for the chunk.</param>
/// <param name="AltScene">
/// A second Borg5, or 0. The game draws it after everything else in the chunks around the player
/// (<c>renderTransZones_</c>), so it holds see-through surfaces such as water.
/// </param>
/// <param name="Collision">The Borg9: collision and map objects.</param>
public sealed record MapChunk(MapSet Set, int Row, int Column, int Scene, int AltScene, int Collision, int Terrain)
{
    /// <summary>The chunk as the debug overlay names it, such as <c>H10</c>.</summary>
    public string Name => Row is >= 1 and <= 26
        ? (char)('A' + Row - 1) + Column.ToString("D2", CultureInfo.InvariantCulture)
        : $"{Row},{Column}";

    public override string ToString() => $"{Set} {Name}";
}

/// <summary>How the chunks of a <see cref="MapSet"/> sit in the world.</summary>
public static class MapSets
{
    /// <summary>
    /// The spacing of a set's chunks, the same along X and Z. It's a literal in
    /// <c>BorgMaps::LoadMap</c> (checked against 1.0's code): a fact the importer carries (ADR 0008
    /// layer 1). Interiors and Ehud's tower space their rooms far apart.
    /// </summary>
    public static float CellSize(MapSet set) => set switch
    {
        MapSet.MiscInteriors => 500,
        MapSet.Erromon => 30,
        MapSet.Terminor => 200,
        MapSet.Barrows => 25,
        MapSet.EhudUnder => 1000,
        MapSet.Battlefields => 150,
        _ => 100,
    };

    /// <summary>
    /// The places <c>Zones.SetMapSet</c> (<c>0x80026264</c> in 1.0) puts the player when it's asked for a set's portal: a chunk, as the
    /// row and column immediates the code loads (<c>addiu s7</c> and <c>s6</c>), and x and z, the floats it loads from its literals
    /// (<c>0x800CE7D4</c> on). Carried as facts, checked against 1.0's code (ADR 0008 layer 1). A set with none, like the Goblin camp,
    /// leaves the player where he is; and a portal of -1 does too. A Battle arena's portal is its number, the chunk its place in the
    /// set's list (<c>0x800DCA74</c>) at (20, 20), for which <paramref name="chunks"/> is wanted.
    /// </summary>
    public static IReadOnlyList<SpawnPoint>? Spawns(MapSet set, IReadOnlyList<MapChunk> chunks) => set switch
    {
        MapSet.Overworld => [new(0, 9, 8, new(16, 62))],
        MapSet.MiscInteriors => [new(0, 5, 3, new(5.9f, 3)), new(1, 1, 1, new(5, 5))],
        MapSet.Erromon => [new(0, 1, 15, new(13, 25))],
        MapSet.Barrows => [new(0, 4, 1, new(10, 10))],
        MapSet.JundarInteriors => [new(0, 1, 1, new(16, 15))],
        MapSet.EhudUnder => [new(0, 17, 1, new(5, 5))],
        MapSet.Ugarit => [new(0, 13, 5, new(10, 82))],
        MapSet.Battlefields => [.. chunks.Where(c => c.Set == set).Select((c, i) => new SpawnPoint(i, c.Row, c.Column, new(20, 20)))],
        _ => null,
    };

    /// <summary>
    /// Where a chunk's own coordinates start in its set: rows run along X and columns along Z. The
    /// player moves to the next chunk on leaving 0 to <see cref="CellSize"/> on either axis, and a
    /// chunk's collision reaches up to about 30 units into its neighbours.
    /// </summary>
    public static Vector3 ChunkOrigin(MapChunk chunk) =>
        new Vector3(chunk.Row, 0, chunk.Column) * CellSize(chunk.Set);
}

public static class MapChunks
{
    private const int EntrySize = 12;

    /// <summary>Reads every map set's chunk list from the code segment.</summary>
    public static IReadOnlyList<MapChunk> Read(RomImage rom)
    {
        var data = rom.Data;
        var offset = rom.Layout.MapChunkLists;
        var chunks = new List<MapChunk>();
        foreach (var set in Enum.GetValues<MapSet>())
        {
            while (true)
            {
                if (offset + EntrySize > data.Length)
                    throw new InvalidDataException($"The {set} chunk list runs off the end of the ROM.");
                var entry = data.Slice(offset, EntrySize);
                offset += EntrySize;
                if (!entry.ContainsAnyExcept((byte)0))
                    break;
                chunks.Add(new MapChunk(set, Be.U16(entry, 0), Be.U16(entry, 2), Be.U16(entry, 4), Be.U16(entry, 6),
                    Be.U16(entry, 8), Be.U16(entry, 10)));
            }
        }
        return chunks;
    }

}
