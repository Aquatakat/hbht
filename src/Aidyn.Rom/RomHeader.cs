using System.Buffers.Binary;
using System.Text;

namespace Aidyn.Rom;

/// <summary>The fields of the 64-byte cartridge header that identify a dump.</summary>
/// <param name="Name">Internal name, trimmed. Blank on the debug ROM.</param>
/// <param name="GameCode">Media type, two-letter ID and region, e.g. <c>NAYE</c>. Blank on the debug ROM.</param>
/// <param name="Revision">Revision byte at <c>0x3F</c>: 0 for NA 1.0, 1 for NA 1.1.</param>
public sealed record RomHeader(uint EntryPoint, uint Crc1, uint Crc2, string Name, string GameCode, byte Revision)
{
    public const int Size = 0x40;

    /// <summary>Parses the header from the start of a big-endian ROM image.</summary>
    public static RomHeader Parse(ReadOnlySpan<byte> rom)
    {
        if (rom.Length < Size)
            throw new InvalidDataException("Too short to hold an N64 header.");

        return new RomHeader(
            EntryPoint: BinaryPrimitives.ReadUInt32BigEndian(rom[0x08..]),
            Crc1: BinaryPrimitives.ReadUInt32BigEndian(rom[0x10..]),
            Crc2: BinaryPrimitives.ReadUInt32BigEndian(rom[0x14..]),
            Name: Text(rom.Slice(0x20, 20)),
            GameCode: Text(rom.Slice(0x3B, 4)),
            Revision: rom[0x3F]);
    }

    private static string Text(ReadOnlySpan<byte> field) =>
        Encoding.Latin1.GetString(field).Trim(' ', '\0');
}
