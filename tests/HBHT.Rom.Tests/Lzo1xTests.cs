using HBHT.Rom.Compression;

namespace HBHT.Rom.Tests;

public class Lzo1xTests
{
    private static readonly byte[] EndMarker = [0x11, 0x00, 0x00];

    private static byte[] Decode(byte[] stream, int outputSize)
    {
        var output = new byte[outputSize];
        var result = Lzo1x.Decompress(stream, output);
        Assert.Equal(new DecompressResult(stream.Length, outputSize), result);
        return output;
    }

    private static byte[] Bytes(params object[] parts) =>
        [.. parts.SelectMany(p => p switch
        {
            byte b => [b],
            int i => [(byte)i],
            string s => s.Select(ch => (byte)ch),
            byte[] a => a,
            _ => throw new ArgumentException(p.GetType().Name),
        })];

    [Fact]
    public void InitialLiteralRun() =>
        Assert.Equal("hello"u8.ToArray(), Decode(Bytes(17 + 5, "hello", EndMarker), 5));

    [Fact]
    public void ShortInitialRunThenLongMatch() =>
        // 3 literals, then a 0x20-form match: length 4 + 2, distance (0x08 >> 2) + 1 = 3.
        Assert.Equal("abcabcabc"u8.ToArray(), Decode(Bytes(17 + 3, "abc", 0x24, 0x08, 0x00, EndMarker), 9));

    [Fact]
    public void ShortMatchWithTrailingLiteral() =>
        // 0x4D = length 3, distance 4, then 1 trailing literal from the state bits.
        Assert.Equal("abcdabcz"u8.ToArray(), Decode(Bytes(17 + 4, "abcd", 0x4D, 0x00, "z", EndMarker), 8));

    /// <summary>Encodes 19 or more literals as instruction 0 plus a long length.</summary>
    private static byte[] EncodeLongRun(byte[] literals)
    {
        var extra = literals.Length - 18;
        Assert.True(extra >= 1, "run too short for the long form");
        var zeros = (extra - 1) / 255;
        return Bytes(0, new byte[zeros], extra - zeros * 255, literals);
    }

    [Fact]
    public void LongLiteralRun()
    {
        var literals = new byte[300];
        new Random(2).NextBytes(literals);
        var stream = Bytes(EncodeLongRun(literals), EndMarker);
        Assert.Equal(Bytes(0, 0, 27), stream[..3]); // 3 + 15 + 255 + 27
        Assert.Equal(literals, Decode(stream, 300));
    }

    [Fact]
    public void TwoByteMatchAfterState()
    {
        // "abcd", 0x4D copies "abc" with state 1, "z" is the trailing literal, then a
        // sub-16 instruction is a 2-byte match: distance 1 + (4 >> 2) + (0 << 2) = 2.
        var stream = Bytes(17 + 4, "abcd", 0x4D, 0x00, "z", 0x04, 0x00, EndMarker);
        Assert.Equal("abcdabczcz"u8.ToArray(), Decode(stream, 10));
    }

    [Fact]
    public void FarMatch()
    {
        var source = new byte[0x5010];
        new Random(3).NextBytes(source);
        // 0x10-form, length 1 + 2: distance 0x4000 + (0x14 >> 2) + (0x40 << 6) = 0x5005.
        var stream = Bytes(EncodeLongRun(source), 0x11, 0x14, 0x40, EndMarker);
        byte[] expected = [.. source, .. source.AsSpan(source.Length - 0x5005, 3)];
        Assert.Equal(expected, Decode(stream, expected.Length));
    }

    [Fact]
    public void TruncatedInputThrows() =>
        Assert.Throws<DecompressionException>(() => Lzo1x.Decompress(Bytes(17 + 5, "hello"), new byte[5]));

    [Fact]
    public void BackReferenceBeforeStartThrows() =>
        Assert.Throws<DecompressionException>(() =>
            Lzo1x.Decompress(Bytes(17 + 3, "abc", 0x24, 0x28, 0x00, EndMarker), new byte[9]));

    [Fact]
    public void OutputOverrunThrows() =>
        Assert.Throws<DecompressionException>(() =>
            Lzo1x.Decompress(Bytes(17 + 5, "hello", EndMarker), new byte[4]));
}
