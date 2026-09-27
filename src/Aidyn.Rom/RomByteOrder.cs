using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Aidyn.Rom;

/// <summary>The byte order a ROM dump was saved in, named after the usual file extensions.</summary>
public enum RomByteOrder
{
    /// <summary><c>.z64</c>: native big-endian. First word <c>80 37 12 40</c>.</summary>
    BigEndian,

    /// <summary><c>.v64</c>: bytes swapped within each 16-bit half. First word <c>37 80 40 12</c>.</summary>
    ByteSwapped,

    /// <summary><c>.n64</c>: each 32-bit word reversed. First word <c>40 12 37 80</c>.</summary>
    LittleEndian,
}

public static class RomByteOrders
{
    /// <summary>Detects the byte order from the PI settings word at the start of the ROM.</summary>
    /// <exception cref="InvalidDataException">The first word isn't an N64 PI settings word in any order.</exception>
    public static RomByteOrder Detect(ReadOnlySpan<byte> rom)
    {
        if (rom.Length < 4)
            throw new InvalidDataException("Too short to be an N64 ROM.");

        return BinaryPrimitives.ReadUInt32BigEndian(rom) switch
        {
            0x80371240 => RomByteOrder.BigEndian,
            0x37804012 => RomByteOrder.ByteSwapped,
            0x40123780 => RomByteOrder.LittleEndian,
            var word => throw new InvalidDataException($"Not an N64 ROM: first word is {word:X8}."),
        };
    }

    /// <summary>
    /// Converts <paramref name="rom"/> in place between big-endian and <paramref name="order"/>.
    /// Both swaps are their own inverse, so the same call converts in either direction.
    /// </summary>
    public static void Swap(Span<byte> rom, RomByteOrder order)
    {
        if (rom.Length % 4 != 0)
            throw new InvalidDataException($"ROM size {rom.Length} isn't a multiple of 4.");

        switch (order)
        {
            case RomByteOrder.BigEndian:
                break;
            case RomByteOrder.ByteSwapped:
                var halves = MemoryMarshal.Cast<byte, ushort>(rom);
                BinaryPrimitives.ReverseEndianness(halves, halves);
                break;
            case RomByteOrder.LittleEndian:
                var words = MemoryMarshal.Cast<byte, uint>(rom);
                BinaryPrimitives.ReverseEndianness(words, words);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(order));
        }
    }
}
