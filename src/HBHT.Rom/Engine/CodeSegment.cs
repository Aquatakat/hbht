using HBHT.Plugins.Records;

namespace HBHT.Rom.Engine;

/// <summary>
/// Reads data in the code segment, which the game copies from ROM <c>0x1000</c> to
/// <c>0x80000400</c>. Tables of pointers are followed through that mapping.
/// </summary>
internal static class CodeSegment
{
    private const uint Vram = 0x80000400;
    private const int Rom = 0x1000;

    /// <summary>The ROM offset and RAM address the code segment starts at.</summary>
    public const int RomBase = Rom;

    public const uint RamBase = Vram;

    /// <summary>The ROM offset of a code segment address.</summary>
    public static int RomOffset(uint address) =>
        address >= Vram && address - Vram < 0x100000
            ? (int)(address - Vram) + Rom
            : throw new InvalidDataException($"0x{address:X8} isn't in the code segment.");

    /// <summary>The ROM from the address a table entry points to, onwards.</summary>
    public static ReadOnlySpan<byte> At(RomImage rom, uint address) => rom.Data[RomOffset(address)..];

    /// <summary>Four bytes, red to alpha.</summary>
    public static Rgba Colour(ReadOnlySpan<byte> data, int at) => new(data[at], data[at + 1], data[at + 2], data[at + 3]);
}
