namespace HBHT.Rom.Compression;

/// <summary>
/// "LZ01x", the compression on the ROM's text. It is LZO1X as produced by LZO 1.06, whose
/// version strings are still in the game's code segment.
/// </summary>
/// <remarks>
/// The stream is a series of instruction bytes. Each match instruction carries a 2-bit
/// "state" in its low offset byte that says how many literals (0–3) follow it before the
/// next instruction. A state of 0 means the next instruction may be a longer literal run.
/// <list type="table">
/// <item><term><c>0x40–0xFF</c></term><description>Match of 3–8 bytes, distance up to 0x800 (one extra byte).</description></item>
/// <item><term><c>0x20–0x3F</c></term><description>Match of any length, distance up to 0x4000 (two extra bytes).</description></item>
/// <item><term><c>0x10–0x1F</c></term><description>Match of any length, distance 0x4001–0xBFFF. Distance 0 is the end marker.</description></item>
/// <item><term><c>0x00–0x0F</c></term><description>After state 0: a run of 4 or more literals.
/// After a literal run: a 3-byte match beyond 0x800. After state 1–3: a 2-byte match up to 0x400.</description></item>
/// </list>
/// Long lengths are stored as a count of zero bytes (255 each) plus a final non-zero byte.
/// </remarks>
public static class Lzo1x
{
    private const uint M2MaxDistance = 0x800;

    /// <summary>
    /// Decodes up to the end marker. Anything after the marker is left unread, so callers that
    /// know the exact stream length should compare it with <see cref="DecompressResult.BytesRead"/>.
    /// </summary>
    public static DecompressResult Decompress(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        var c = new LzCursor(src, dst);
        uint t;

        // A first byte above 17 is a literal run of (byte - 17) with no instruction before it.
        if (c.Peek() > 17)
        {
            t = c.ReadByte() - 17u;
            if (t < 4)
                goto TrailingLiterals;
            c.CopyLiterals(t);
            goto AfterLiteralRun;
        }

    Instruction:
        t = c.ReadByte();
        if (t >= 16)
            goto Match;
        if (t == 0)
            t = 15 + ReadLongLength(ref c);
        c.CopyLiterals(t + 3);

    AfterLiteralRun:
        t = c.ReadByte();
        if (t >= 16)
            goto Match;
        c.CopyMatch(1 + M2MaxDistance + (t >> 2) + ((uint)c.ReadByte() << 2), 3);
        goto MatchDone;

    Match:
        if (t >= 64)
        {
            var distance = 1 + (t >> 2 & 7) + ((uint)c.ReadByte() << 3);
            c.CopyMatch(distance, (t >> 5) + 1);
        }
        else if (t >= 32)
        {
            var length = t & 31;
            if (length == 0)
                length = 31 + ReadLongLength(ref c);
            c.CopyMatch(1 + ReadDistance14(ref c), length + 2);
        }
        else if (t >= 16)
        {
            var length = t & 7;
            if (length == 0)
                length = 7 + ReadLongLength(ref c);
            var distance = ((t & 8) << 11) + ReadDistance14(ref c);
            if (distance == 0)
                return c.Result;
            c.CopyMatch(distance + 0x4000, length + 2);
        }
        else
        {
            c.CopyMatch(1 + (t >> 2) + ((uint)c.ReadByte() << 2), 2);
        }

    MatchDone:
        // The state bits live in the byte two back: the instruction itself for one-byte
        // forms, or the low distance byte for the two-byte forms.
        t = c.At(c.In - 2) & 3u;
        if (t == 0)
            goto Instruction;

    TrailingLiterals:
        c.CopyLiterals(t);
        t = c.ReadByte();
        goto Match;
    }

    /// <summary>A little-endian 14-bit distance whose low 2 bits are the state bits.</summary>
    private static uint ReadDistance14(ref LzCursor c)
    {
        uint low = c.ReadByte();
        uint high = c.ReadByte();
        return (low >> 2) + (high << 6);
    }

    private static uint ReadLongLength(ref LzCursor c)
    {
        uint length = 0;
        while (c.Peek() == 0)
        {
            length += 255;
            c.In++;
        }
        return length + c.ReadByte();
    }
}
