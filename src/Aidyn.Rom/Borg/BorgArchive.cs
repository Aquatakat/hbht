using System.Buffers.Binary;
using Aidyn.Rom.Compression;

namespace Aidyn.Rom.Borg;

/// <summary>Borg asset types. 0, 10 and 14 are named in the decomp but no asset uses them.</summary>
public enum BorgType : short
{
    Gen = 0,
    Texture = 1,
    Geometry = 2,
    Camera = 3,
    Light = 4,
    Scene = 5,
    Animation = 6,
    Actor = 7,
    UiImage = 8,
    MapChunk = 9,
    CollisionMaterial = 10,
    Sample = 11,
    Sequence = 12,
    Dialogue = 13,
    GameState = 14,
}

public enum BorgCompression : short
{
    None = 0,
    Lz01x = 1,
    Lzb = 2,
}

/// <summary>One <c>Borg_List</c> entry.</summary>
/// <param name="Offset">From the start of the archive.</param>
public readonly record struct BorgListing(
    int Index, BorgType Type, BorgCompression Compression, int CompressedSize, int UncompressedSize, int Offset);

/// <summary>
/// The Borg asset archive and its <c>Borg_List</c> index (roadmap 0.3).
/// </summary>
/// <remarks>
/// <c>Borg_List</c> is an 8-byte header (entry count, then the archive's size in bytes) followed
/// by 16-byte entries: type (s16), compression (s16), compressed size, uncompressed size and
/// offset (s32 each).
/// </remarks>
public sealed class BorgArchive
{
    private const int HeaderSize = 8;
    private const int EntrySize = 16;

    private readonly RomImage _rom;
    private readonly int _archiveSize;

    private BorgArchive(RomImage rom, int archiveSize, BorgListing[] listings)
    {
        _rom = rom;
        _archiveSize = archiveSize;
        Listings = listings;
    }

    public IReadOnlyList<BorgListing> Listings { get; }
    public int Count => Listings.Count;

    /// <summary>Reads and validates the index. Doesn't decompress anything.</summary>
    public static BorgArchive Open(RomImage rom)
    {
        var layout = rom.Layout;
        var header = rom.Slice(layout.BorgList, HeaderSize);
        var count = BinaryPrimitives.ReadInt32BigEndian(header);
        var archiveSize = BinaryPrimitives.ReadInt32BigEndian(header[4..]);

        if (archiveSize != layout.BorgList - layout.BorgArchive)
            throw new InvalidDataException(
                $"Borg_List says the archive is 0x{archiveSize:X} bytes, but the layout puts it at 0x{layout.BorgList - layout.BorgArchive:X}.");
        if (count < 0)
            throw new InvalidDataException($"Borg_List has a negative entry count ({count}).");

        var table = rom.Slice(layout.BorgList + HeaderSize, count * EntrySize);
        var listings = new BorgListing[count];
        for (var i = 0; i < count; i++)
        {
            var e = table.Slice(i * EntrySize, EntrySize);
            var listing = new BorgListing(
                i,
                (BorgType)BinaryPrimitives.ReadInt16BigEndian(e),
                (BorgCompression)BinaryPrimitives.ReadInt16BigEndian(e[2..]),
                BinaryPrimitives.ReadInt32BigEndian(e[4..]),
                BinaryPrimitives.ReadInt32BigEndian(e[8..]),
                BinaryPrimitives.ReadInt32BigEndian(e[12..]));
            Validate(listing, archiveSize);
            listings[i] = listing;
        }

        return new BorgArchive(rom, archiveSize, listings);
    }

    /// <summary>The asset's bytes as stored in the ROM, still compressed.</summary>
    public ReadOnlySpan<byte> ReadStored(int index)
    {
        var listing = Listings[index];
        return _rom.Slice(_rom.Layout.BorgArchive + listing.Offset, listing.CompressedSize);
    }

    /// <summary>
    /// The asset's decompressed bytes. Throws unless the output is exactly the listed size and
    /// the stream ends within the stored size's 8-byte padding.
    /// </summary>
    /// <remarks>
    /// Stored sizes are rounded up to a multiple of 8 with zero bytes, so a stream normally
    /// ends 0–7 bytes early. The game's decompressor reports that as "input not consumed" and
    /// the loader ignores it.
    /// </remarks>
    public byte[] Read(int index)
    {
        var listing = Listings[index];
        var stored = ReadStored(index);
        if (listing.Compression == BorgCompression.None)
            return stored.ToArray();

        var output = new byte[listing.UncompressedSize];
        var result = listing.Compression == BorgCompression.Lzb
            ? Lzb.Decompress(stored, output)
            : Lzo1x.Decompress(stored, output);

        var padding = stored[result.BytesRead..];
        if (result.BytesWritten != output.Length || padding.Length >= 8 || padding.ContainsAnyExcept((byte)0))
            throw new DecompressionException(
                $"Borg {index}: read {result.BytesRead}/{stored.Length} bytes and wrote {result.BytesWritten}/{output.Length}.");
        return output;
    }

    private static void Validate(BorgListing l, int archiveSize)
    {
        if (!Enum.IsDefined(l.Type))
            throw new InvalidDataException($"Borg {l.Index} has unknown type {(short)l.Type}.");
        if (!Enum.IsDefined(l.Compression))
            throw new InvalidDataException($"Borg {l.Index} has unknown compression {(short)l.Compression}.");
        if (l.CompressedSize < 0 || l.UncompressedSize < 0 || l.Offset < 0 || l.Offset > archiveSize - l.CompressedSize)
            throw new InvalidDataException(
                $"Borg {l.Index} (0x{l.Offset:X}+0x{l.CompressedSize:X}) falls outside the 0x{archiveSize:X}-byte archive.");
        if (l.Compression == BorgCompression.None && l.CompressedSize != l.UncompressedSize)
            throw new InvalidDataException($"Borg {l.Index} is uncompressed but its two sizes differ.");
    }
}
