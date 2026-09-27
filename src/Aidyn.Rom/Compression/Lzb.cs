namespace Aidyn.Rom.Compression;

/// <summary>
/// "LZB", the compression on most Borg assets. It is UCL's NRV2B format with an 8-bit bit
/// buffer: control bits are packed MSB-first into bytes that sit in the same stream as the
/// literal and offset bytes, each fetched at the moment the decoder first needs a bit from it.
/// </summary>
/// <remarks>
/// Stream grammar, repeated until the end marker:
/// <list type="bullet">
/// <item>Literals: each <c>1</c> bit is followed by one literal byte. A <c>0</c> bit ends the run.</item>
/// <item>Offset: an Elias-gamma-style number <c>g</c> (see <see cref="ReadGamma"/>). <c>g == 2</c> reuses the
/// previous distance. Otherwise one more byte gives <c>distance = (g - 3) * 256 + byte + 1</c>; the value
/// that wraps to <c>0xFFFFFFFF</c> before the <c>+ 1</c> marks the end of the stream.</item>
/// <item>Length: two bits give 1–3. <c>00</c> escapes to a gamma number plus 2. Distances over
/// <c>0xD00</c> add one. The copy is that length plus one byte.</item>
/// </list>
/// </remarks>
public static class Lzb
{
    private const uint EndMarker = 0xFFFFFFFF;
    private const uint FarDistance = 0xD00;

    public static DecompressResult Decompress(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        var c = new LzCursor(src, dst);
        var bits = new BitBuffer();
        uint lastDistance = 1;

        while (true)
        {
            while (bits.Read(ref c) == 1)
                c.CopyLiterals(1);

            uint distance = ReadGamma(ref c, ref bits);
            if (distance == 2)
            {
                distance = lastDistance;
            }
            else
            {
                distance = unchecked((distance - 3) * 256 + c.ReadByte());
                if (distance == EndMarker)
                    return c.Result;
                lastDistance = ++distance;
            }

            uint length = bits.Read(ref c) << 1;
            length |= bits.Read(ref c);
            if (length == 0)
                length = ReadGamma(ref c, ref bits) + 2;
            if (distance > FarDistance)
                length++;

            c.CopyMatch(distance, length + 1);
        }
    }

    /// <summary>
    /// A number with an implicit leading 1: each step appends one data bit, then reads a stop
    /// bit (<c>1</c> = done). The smallest value is 2.
    /// </summary>
    private static uint ReadGamma(ref LzCursor c, ref BitBuffer bits)
    {
        uint value = 1;
        do
            value = unchecked(value * 2 + bits.Read(ref c));
        while (bits.Read(ref c) == 0);
        return value;
    }

    /// <summary>Holds the current control byte, shifted left, with a sentinel 1 below it that
    /// reaches bit 8 once all 8 bits are used.</summary>
    private struct BitBuffer
    {
        private uint _bits;

        public uint Read(ref LzCursor c)
        {
            _bits <<= 1;
            if ((_bits & 0xFF) == 0)
                _bits = (uint)c.ReadByte() << 1 | 1;
            return _bits >> 8 & 1;
        }
    }
}
