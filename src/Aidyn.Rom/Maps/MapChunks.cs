using System.Buffers.Binary;
using System.Globalization;

namespace Aidyn.Rom.Maps;

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
/// <param name="AltScene">Another Borg5 some chunks swap to, or 0.</param>
/// <param name="Collision">The Borg9: collision and map objects.</param>
public sealed record MapChunk(MapSet Set, int Row, int Column, int Scene, int AltScene, int Collision, int Terrain)
{
    /// <summary>The chunk as the debug overlay names it, such as <c>H10</c>.</summary>
    public string Name => Row is >= 1 and <= 26
        ? (char)('A' + Row - 1) + Column.ToString("D2", CultureInfo.InvariantCulture)
        : $"{Row},{Column}";

    public override string ToString() => $"{Set} {Name}";
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
                chunks.Add(new MapChunk(set, U16(entry, 0), U16(entry, 2), U16(entry, 4), U16(entry, 6),
                    U16(entry, 8), U16(entry, 10)));
            }
        }
        return chunks;
    }

    private static int U16(ReadOnlySpan<byte> entry, int at) => BinaryPrimitives.ReadUInt16BigEndian(entry[at..]);
}
