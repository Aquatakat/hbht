namespace HBHT.Rom.Compression;

public sealed class DecompressionException(string message) : IOException(message);

/// <summary>How far a decompressor got: input consumed and output produced.</summary>
public readonly record struct DecompressResult(int BytesRead, int BytesWritten);

/// <summary>Bounds-checked input and output positions shared by the LZ decoders.</summary>
internal ref struct LzCursor(ReadOnlySpan<byte> src, Span<byte> dst)
{
    private readonly ReadOnlySpan<byte> _src = src;
    private readonly Span<byte> _dst = dst;

    public int In;
    public int Out;

    public readonly DecompressResult Result => new(In, Out);

    public readonly byte Peek() =>
        In < _src.Length ? _src[In] : throw new DecompressionException($"Input overrun at 0x{In:X}.");

    public readonly byte At(int index) => _src[index];

    public byte ReadByte()
    {
        var b = Peek();
        In++;
        return b;
    }

    public void CopyLiterals(uint count)
    {
        if (count > _src.Length - In)
            throw new DecompressionException($"Input overrun: {count} literals at 0x{In:X}.");
        CheckOutput(count);
        _src.Slice(In, (int)count).CopyTo(_dst[Out..]);
        In += (int)count;
        Out += (int)count;
    }

    /// <summary>Copies <paramref name="count"/> bytes from <paramref name="distance"/> back in the output.
    /// Byte by byte, because the source may overlap what's being written.</summary>
    public void CopyMatch(uint distance, uint count)
    {
        if (distance == 0 || distance > Out)
            throw new DecompressionException($"Back-reference {distance} at output 0x{Out:X} is before the start.");
        CheckOutput(count);
        var from = Out - (int)distance;
        for (var i = 0; i < count; i++)
            _dst[Out++] = _dst[from + i];
    }

    private readonly void CheckOutput(uint count)
    {
        if (count > _dst.Length - Out)
            throw new DecompressionException($"Output overrun: {count} bytes at 0x{Out:X} into a {_dst.Length}-byte buffer.");
    }
}
