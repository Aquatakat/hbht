using System.Buffers.Binary;

namespace HBHT.Rom.Audio;

/// <summary>
/// A Borg11: one mono PCM sample, which Borg12 sequences play as their instruments (roadmap 1D).
/// </summary>
/// <remarks>
/// A 0x10-byte header of flags, the PCM's length in bytes, its length in samples and the offset of
/// the PCM (always 0x10; <c>BorgSample.Fixup</c>, 1.0 <c>0x8009D580</c>, makes it a pointer), all
/// u32. Flag 0x10 is always set, and 0x08 (16-bit, big-endian) or 0x04 (8-bit) says the width. The
/// driver tests only 0x04. Both are signed, and the driver widens 8-bit samples to 16 with
/// <c>b &lt;&lt; 8</c> (<c>dmaProc</c>, 1.0 <c>0x8009001C</c>). There's no rate, base note or loop:
/// a sequence's events give the rate to play it at, and its instruments the loop.
/// </remarks>
public sealed class Borg11Sample
{
    public const int HeaderSize = 0x10;
    private const uint Flag16Bit = 0x08, Flag8Bit = 0x04, FlagAlways = 0x10;

    private Borg11Sample(short[] samples, bool eightBit)
    {
        Samples = samples;
        EightBit = eightBit;
    }

    /// <summary>The PCM as the driver plays it: 16-bit, with 8-bit samples widened.</summary>
    public short[] Samples { get; }

    /// <summary>Whether the ROM stores it as 8-bit.</summary>
    public bool EightBit { get; }

    public static Borg11Sample Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"A Borg11 is at least 0x{HeaderSize:X} bytes, not {data.Length}.");

        var flags = BinaryPrimitives.ReadUInt32BigEndian(data);
        var bytes = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var count = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        var offset = BinaryPrimitives.ReadUInt32BigEndian(data[0xC..]);

        if (flags is not (FlagAlways | Flag16Bit) and not (FlagAlways | Flag8Bit))
            throw new InvalidDataException($"Unknown Borg11 flags 0x{flags:X}.");
        var eightBit = (flags & Flag8Bit) != 0;
        if (count * (eightBit ? 1u : 2u) != bytes)
            throw new InvalidDataException($"A Borg11 of {count} {(eightBit ? 8 : 16)}-bit samples isn't {bytes} bytes.");
        if (offset != HeaderSize || offset + bytes > data.Length)
            throw new InvalidDataException($"The Borg11's {bytes} bytes at 0x{offset:X} overrun its 0x{data.Length:X} bytes.");

        var pcm = data.Slice((int)offset, (int)bytes);
        var samples = new short[count];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = eightBit ? (short)(pcm[i] << 8) : BinaryPrimitives.ReadInt16BigEndian(pcm[(i * 2)..]);
        return new Borg11Sample(samples, eightBit);
    }
}
