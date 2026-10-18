using System.Buffers.Binary;
using Aidyn.Rom.Compression;

namespace Aidyn.Rom.Text;

/// <summary>
/// A string table as loaded by the game's <c>RomString::Load</c>: a big-endian u16
/// decompressed size, then an LZ01x stream.
/// </summary>
/// <remarks>
/// The decompressed payload is a u16 string count, then each string as a length byte followed
/// by that many encrypted bytes.
/// </remarks>
public static class RomStringBlock
{
    /// <summary>Reads and decrypts every string in the table, in order.</summary>
    public static IReadOnlyList<string> Read(RomImage rom, RomStringTable table) => Split(Decompress(rom, table));

    public static byte[] Decompress(RomImage rom, RomStringTable table) => Decompress(rom.Table(table));

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

    /// <summary>Splits a decompressed payload into decrypted strings.</summary>
    /// <exception cref="InvalidDataException">The strings don't exactly fill the payload.</exception>
    public static IReadOnlyList<string> Split(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
            throw new InvalidDataException("String payload is too short for its count.");

        var count = BinaryPrimitives.ReadUInt16BigEndian(payload);
        var strings = new string[count];
        var position = 2;
        for (var i = 0; i < count; i++)
        {
            if (position >= payload.Length)
                throw new InvalidDataException($"String {i} of {count} starts past the end of the payload.");
            var length = payload[position++];
            if (length > payload.Length - position)
                throw new InvalidDataException($"String {i} of {count} runs past the end of the payload.");

            strings[i] = GameText.Decrypt(payload.Slice(position, length), StringCipher.RomStringSeedA, StringCipher.RomStringSeedB);
            position += length;
        }

        if (position != payload.Length)
            throw new InvalidDataException($"{payload.Length - position} bytes follow the last of {count} strings.");
        return strings;
    }
}
