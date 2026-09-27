using System.Buffers.Binary;
using Aidyn.Rom.Compression;

namespace Aidyn.Rom.Text;

/// <summary>
/// A string table as loaded by the game's <c>RomString::Load</c>: a big-endian u16
/// decompressed size, then an LZ01x stream.
/// </summary>
/// <remarks>
/// The decompressed payload is a u16 string count, then each string as a length byte followed
/// by that many encrypted bytes. Splitting and decryption belong to the DB readers (roadmap 0.4).
/// </remarks>
public static class RomStringBlock
{
    public static byte[] Decompress(RomImage rom, RomStringTable table)
    {
        if (!rom.Layout.StringTables.TryGetValue(table, out var range))
            throw new NotSupportedException($"{rom.Version.Name} has no verified location for the {table} string table.");
        return Decompress(rom.Slice(range));
    }

    /// <summary>Decompresses a block. Trailing bytes after the stream's end marker are padding.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> block)
    {
        if (block.Length < 2)
            throw new InvalidDataException("String block is too short for its size prefix.");

        var output = new byte[BinaryPrimitives.ReadUInt16BigEndian(block)];
        var result = Lzo1x.Decompress(block[2..], output);
        if (result.BytesWritten != output.Length)
            throw new DecompressionException(
                $"String block decompressed to {result.BytesWritten} bytes, but its prefix says {output.Length}.");
        return output;
    }
}
