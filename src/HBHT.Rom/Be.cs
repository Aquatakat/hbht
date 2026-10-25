using System.Buffers.Binary;

namespace HBHT.Rom;

/// <summary>
/// Reads the ROM's big-endian fields: a word at an offset, a whole table of them, a list up to its
/// terminator, and a bounds-checked view of a Borg's bytes. The DB's records read through <see cref="Db.RecordReader"/>, and
/// the Borg decoders mostly call <see cref="System.Buffers.Binary.BinaryPrimitives"/> themselves; this is the shared shorthand.
/// </summary>
internal static class Be
{
    public static ushort U16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data[at..]);

    public static short S16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadInt16BigEndian(data[at..]);

    public static uint U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

    public static int S32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadInt32BigEndian(data[at..]);

    public static float F32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadSingleBigEndian(data[at..]);

    /// <summary>Every whole u16 in <paramref name="data"/>.</summary>
    public static ushort[] U16s(ReadOnlySpan<byte> data)
    {
        var values = new ushort[data.Length / 2];
        for (var i = 0; i < values.Length; i++)
            values[i] = U16(data, i * 2);
        return values;
    }

    /// <summary>Every whole 32-bit word in <paramref name="data"/>, as the signed value (an unsigned one's bits cast to it).</summary>
    public static int[] S32s(ReadOnlySpan<byte> data)
    {
        var values = new int[data.Length / 4];
        for (var i = 0; i < values.Length; i++)
            values[i] = S32(data, i * 4);
        return values;
    }

    /// <summary>Every whole float in <paramref name="data"/>.</summary>
    public static float[] F32s(ReadOnlySpan<byte> data)
    {
        var values = new float[data.Length / 4];
        for (var i = 0; i < values.Length; i++)
            values[i] = F32(data, i * 4);
        return values;
    }

    /// <summary>The u16s from <paramref name="at"/> up to a 0 or the end of <paramref name="data"/>.</summary>
    public static List<int> U16sUntilZero(ReadOnlySpan<byte> data, int at = 0)
    {
        var list = new List<int>();
        for (; at + 2 <= data.Length && U16(data, at) is var v and not 0; at += 2)
            list.Add(v);
        return list;
    }

    /// <summary>The 32-bit words from <paramref name="at"/> up to a 0 or the end of <paramref name="data"/>.</summary>
    public static List<int> U32sUntilZero(ReadOnlySpan<byte> data, int at = 0)
    {
        var list = new List<int>();
        for (; at + 4 <= data.Length && U32(data, at) is var v and not 0; at += 4)
            list.Add((int)v);
        return list;
    }

    /// <summary>
    /// A bounds-checked view of <paramref name="length"/> bytes of a Borg at <paramref name="offset"/>.
    /// </summary>
    /// <param name="format">The Borg's format, such as "Borg5", which the error names.</param>
    /// <exception cref="InvalidDataException">The range runs past the data.</exception>
    public static ReadOnlySpan<byte> Slice(byte[] data, int offset, int length, string format) =>
        offset >= 0 && length >= 0 && offset <= data.Length - length
            ? data.AsSpan(offset, length)
            : throw new InvalidDataException($"0x{length:X} bytes at 0x{offset:X} run past the end of the {format} (0x{data.Length:X}).");

    /// <summary>Checks that a part of a Borg is where the layout puts it.</summary>
    /// <param name="format">The Borg's format, such as "Borg9".</param>
    /// <param name="what">The part, plural, such as "vertices".</param>
    /// <exception cref="InvalidDataException">The part is elsewhere.</exception>
    public static void Expect(string format, string what, int actual, int expected)
    {
        if (actual != expected)
            throw new InvalidDataException($"The {format}'s {what} are at 0x{actual:X}, not 0x{expected:X}.");
    }
}
