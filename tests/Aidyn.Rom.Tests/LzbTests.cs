using Aidyn.Rom.Compression;

namespace Aidyn.Rom.Tests;

public class LzbTests
{
    /// <summary>
    /// Emits an NRV2B stream from explicit literals and matches, tracking the output the
    /// decoder should produce. It doesn't search for matches; the tests choose them.
    /// </summary>
    private sealed class Writer
    {
        private readonly List<byte> _stream = [];
        private int _controlByte;
        private int _bitsUsed = 8;
        private uint _lastDistance = 1;

        public List<byte> Expected { get; } = [];
        public byte[] Stream => [.. _stream];

        public Writer Literals(params byte[] bytes)
        {
            foreach (var b in bytes)
            {
                Bit(1);
                _stream.Add(b);
                Expected.Add(b);
            }
            return this;
        }

        public Writer Match(uint distance, int count)
        {
            Bit(0);
            if (distance == _lastDistance)
            {
                Gamma(2);
            }
            else
            {
                Gamma(((distance - 1) >> 8) + 3);
                _stream.Add((byte)(distance - 1));
                _lastDistance = distance;
            }

            var length = (uint)count - 1 - (distance > 0xD00 ? 1u : 0u);
            Assert.True(length >= 1, "match too short to encode");
            if (length <= 3)
            {
                Bit((int)(length >> 1));
                Bit((int)(length & 1));
            }
            else
            {
                Bit(0);
                Bit(0);
                Gamma(length - 2);
            }

            for (var i = 0; i < count; i++)
                Expected.Add(Expected[^(int)distance]);
            return this;
        }

        public Writer End()
        {
            Bit(0);
            Gamma(0x1000002);
            _stream.Add(0xFF);
            return this;
        }

        private void Gamma(uint value)
        {
            var bits = 31 - uint.LeadingZeroCount(value);
            for (var i = (int)bits - 1; i >= 0; i--)
            {
                Bit((int)(value >> i & 1));
                Bit(i == 0 ? 1 : 0);
            }
        }

        // A control byte is reserved in the stream when its first bit is written, which is
        // exactly when the decoder will fetch it.
        private void Bit(int bit)
        {
            if (_bitsUsed == 8)
            {
                _controlByte = _stream.Count;
                _stream.Add(0);
                _bitsUsed = 0;
            }
            if (bit != 0)
                _stream[_controlByte] |= (byte)(0x80 >> _bitsUsed);
            _bitsUsed++;
        }
    }

    private static void AssertRoundTrip(Writer w)
    {
        var stream = w.Stream;
        var output = new byte[w.Expected.Count];
        var result = Lzb.Decompress(stream, output);
        Assert.Equal(w.Expected, output);
        Assert.Equal(new DecompressResult(stream.Length, output.Length), result);
    }

    [Fact]
    public void Literals() => AssertRoundTrip(new Writer().Literals("hello"u8.ToArray()).End());

    [Fact]
    public void EmptyStream() => AssertRoundTrip(new Writer().End());

    [Fact]
    public void OverlappingMatchRepeatsARun() =>
        AssertRoundTrip(new Writer().Literals((byte)'a').Match(1, 9).End());

    [Fact]
    public void RepeatDistanceCode() =>
        AssertRoundTrip(new Writer().Literals("abc"u8.ToArray()).Match(3, 3).Literals((byte)'x').Match(3, 3).End());

    [Fact]
    public void LongLengthsAndFarDistances()
    {
        var noise = new byte[0x1200];
        new Random(1).NextBytes(noise);
        AssertRoundTrip(new Writer()
            .Literals(noise)
            .Match(0x0E00, 5)      // beyond 0xD00: stored length is one shorter
            .Match(0x0D00, 4)      // exactly 0xD00: no adjustment
            .Match(0x1100, 300)    // long gamma length with the adjustment
            .Match(7, 2)           // shortest match
            .End());
    }

    [Fact]
    public void TruncatedInputThrows()
    {
        var stream = new Writer().Literals("hello"u8.ToArray()).End().Stream;
        Assert.Throws<DecompressionException>(() => Lzb.Decompress(stream.AsSpan(..^1), new byte[5]));
    }

    [Fact]
    public void BackReferenceBeforeStartThrows()
    {
        var w = new Writer().Literals("ab"u8.ToArray());
        w.Expected.InsertRange(0, new byte[8]); // let the writer compute a distance past the real start
        var stream = w.Match(9, 3).End().Stream;
        Assert.Throws<DecompressionException>(() => Lzb.Decompress(stream, new byte[5]));
    }

    [Fact]
    public void OutputOverrunThrows()
    {
        var stream = new Writer().Literals("hello"u8.ToArray()).End().Stream;
        Assert.Throws<DecompressionException>(() => Lzb.Decompress(stream, new byte[4]));
    }
}
