using System.Buffers.Binary;

namespace Aidyn.Rom.Engine;

/// <summary>
/// Reads data in the code segment, which the game copies from ROM <c>0x1000</c> to
/// <c>0x80000400</c>. Tables of pointers are followed through that mapping.
/// </summary>
internal static class CodeSegment
{
    private const uint Vram = 0x80000400;
    private const int Rom = 0x1000;

    /// <summary>The <see cref="CodeTable"/>'s bytes, or an error if this version doesn't locate it.</summary>
    public static ReadOnlySpan<byte> Table(RomImage rom, CodeTable table) =>
        rom.Layout.CodeTables.TryGetValue(table, out var range)
            ? rom.Slice(range)
            : throw new NotSupportedException($"{rom.Version.Name} has no verified location for the {table} table.");

    /// <summary>The ROM offset of a code segment address.</summary>
    public static int RomOffset(uint address) =>
        address >= Vram && address - Vram < 0x100000
            ? (int)(address - Vram) + Rom
            : throw new InvalidDataException($"0x{address:X8} isn't in the code segment.");

    /// <summary>The ROM from the address a table entry points to, onwards.</summary>
    public static ReadOnlySpan<byte> At(RomImage rom, uint address) => rom.Data[RomOffset(address)..];

    public static ushort U16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data[at..]);

    public static short S16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadInt16BigEndian(data[at..]);

    public static uint U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

    public static float F32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadSingleBigEndian(data[at..]);
}
