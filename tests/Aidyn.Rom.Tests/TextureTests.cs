using System.Buffers.Binary;
using System.IO.Compression;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Tests;

public class TextureTests
{
    [Fact]
    public void Rgba16ExpandsFiveBitChannels()
    {
        var image = Texels.Decode(TexelFormat.Rgba16, [0xF8, 0x01, 0x07, 0xC0, 0x00, 0x3E, 0x84, 0x20], 4, 1, 8);

        Assert.Equal<byte>([255, 0, 0, 255, 0, 255, 0, 0, 0, 0, 255, 0, 132, 132, 132, 0], image.Pixels);
    }

    [Fact]
    public void FourBitFormatsPutTheLeftTexelInTheHighNibble()
    {
        var i4 = Texels.Decode(TexelFormat.I4, [0xF0], 2, 1, 1);
        Assert.Equal<byte>([255, 255, 255, 255, 0, 0, 0, 0], i4.Pixels);

        // IA4 is 3 bits of intensity and 1 of alpha: 0xF is white and opaque, 0xE white and clear.
        var ia4 = Texels.Decode(TexelFormat.Ia4, [0xFE], 2, 1, 1);
        Assert.Equal<byte>([255, 255, 255, 255, 255, 255, 255, 0], ia4.Pixels);

        var ci4 = Texels.Decode(TexelFormat.Ci4, [0x01], 2, 1, 1, palette: [0x0001, 0xFFFF, .. new ushort[14]]);
        Assert.Equal<byte>([0, 0, 0, 255, 255, 255, 255, 255], ci4.Pixels);
    }

    [Fact]
    public void IaFormatsCopyIntensityToEveryColourChannel()
    {
        Assert.Equal<byte>([0x12, 0x12, 0x12, 0x34], Texels.Decode(TexelFormat.Ia16, [0x12, 0x34], 1, 1, 2).Pixels);
        Assert.Equal<byte>([0x11, 0x11, 0x11, 0x22], Texels.Decode(TexelFormat.Ia8, [0x12], 1, 1, 1).Pixels);
        Assert.Equal<byte>([0x80, 0x80, 0x80, 0x80], Texels.Decode(TexelFormat.I8, [0x80], 1, 1, 1).Pixels);
    }

    /// <summary>TMEM swaps 32-bit words on odd rows; RGBA32 swaps 64-bit words.</summary>
    [Theory]
    [InlineData(TexelFormat.I4, 32)]
    [InlineData(TexelFormat.I8, 16)]
    [InlineData(TexelFormat.Rgba16, 8)]
    [InlineData(TexelFormat.Rgba32, 8)]
    public void TmemOrderSwapsWordsOnOddRows(TexelFormat format, int width)
    {
        // The same two rows twice: once in plain order, and once with the odd row's halves of
        // every 8 (or 16) bytes swapped, the way LoadBlock leaves it in TMEM.
        var stride = width * format.BitsPerTexel() / 8;
        var linear = new byte[stride * 2];
        var tmem = new byte[stride * 2];
        var half = format == TexelFormat.Rgba32 ? 8 : 4;
        for (var i = 0; i < linear.Length; i++)
        {
            linear[i] = (byte)(i * 7 + 1);
            tmem[i < stride ? i : stride + ((i - stride) ^ half)] = linear[i];
        }

        var expected = Texels.Decode(format, linear, width, 2, stride);
        var actual = Texels.Decode(format, tmem, width, 2, stride, tmemInterleaved: true);

        Assert.Equal(expected.Pixels, actual.Pixels);
        Assert.NotEqual(expected.Pixels, Texels.Decode(format, tmem, width, 2, stride).Pixels);
    }

    [Fact]
    public void PngHoldsTheImageUnfiltered()
    {
        var image = new RgbaImage(2, 2, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);

        var png = Png.Encode(image);

        Assert.Equal<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        Assert.Equal<byte>([0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82], png[^12..]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));

        var idatLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        Assert.Equal("IDAT"u8.ToArray(), png[37..41]);
        using var zlib = new ZLibStream(new MemoryStream(png, 41, idatLength), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        Assert.Equal<byte>([0, 1, 2, 3, 4, 5, 6, 7, 8, 0, 9, 10, 11, 12, 13, 14, 15, 16], raw.ToArray());
    }

    [Fact]
    public void EveryBorg1Decodes()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var formats = new Dictionary<Borg1Format, int>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Texture))
        {
            var texture = Borg1Texture.Parse(archive.Read(listing.Index));
            formats[texture.Format] = formats.GetValueOrDefault(texture.Format) + 1;
            for (var level = 0; level < texture.LevelCount; level++)
            {
                var image = texture.Decode(level);
                Assert.Equal(texture.LevelSize(level), (image.Width, image.Height));
            }
        }

        Assert.Equal(new Dictionary<Borg1Format, int>
        {
            [Borg1Format.Rgba16] = 137, [Borg1Format.Ia16] = 56, [Borg1Format.Ci8] = 1769,
            [Borg1Format.Ia8] = 62, [Borg1Format.I8] = 5, [Borg1Format.Ci4] = 135,
            [Borg1Format.Ia4] = 1, [Borg1Format.I4] = 6, [Borg1Format.Rgba32] = 12,
        }, formats);
    }

    /// <summary>
    /// The check that the de-interleave is right. Mip levels were made by downsampling the full
    /// image, so each texture's first mip should be close to a box filter of its base level. If
    /// odd rows were decoded out of order, they'd be shifted sideways and the match would be worse.
    /// Textures of a single flat colour tie.
    /// </summary>
    [Fact]
    public void DeinterleavedTexturesMatchTheirMips()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        int mipmapped = 0, better = 0;
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Texture))
        {
            var texture = Borg1Texture.Parse(archive.Read(listing.Index));
            if (texture.LevelCount < 2)
                continue;

            mipmapped++;
            var deinterleaved = MipError(texture.Decode(0, tmemInterleaved: true), texture.Decode(1, tmemInterleaved: true));
            var raw = MipError(texture.Decode(0, tmemInterleaved: false), texture.Decode(1, tmemInterleaved: false));
            Assert.True(deinterleaved <= raw, $"Borg {listing.Index}: {deinterleaved} vs {raw} without de-interleaving");
            if (deinterleaved < raw)
                better++;
        }

        Assert.Equal(1723, mipmapped);
        Assert.True(better >= 1718, $"Only {better} of {mipmapped} textures matched their mips better.");
    }

    [Fact]
    public void EveryBorg8Decodes()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var formats = new Dictionary<Borg8Format, int>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.UiImage))
        {
            var image = Borg8Image.Parse(archive.Read(listing.Index));
            formats[image.Format] = formats.GetValueOrDefault(image.Format) + 1;
            var decoded = image.Decode();
            Assert.Equal((image.Width, image.Height), (decoded.Width, decoded.Height));
        }

        Assert.Equal(new Dictionary<Borg8Format, int>
        {
            [Borg8Format.Rgba32] = 16, [Borg8Format.Rgba16] = 1, [Borg8Format.Ci8] = 267,
            [Borg8Format.Ia8] = 275, [Borg8Format.I4] = 1,
        }, formats);
    }

    /// <summary>Sum of absolute differences between <paramref name="mip"/> and a box filter of <paramref name="full"/>.</summary>
    private static long MipError(RgbaImage full, RgbaImage mip)
    {
        var sx = full.Width / mip.Width;
        var sy = full.Height / mip.Height;
        long error = 0;
        for (var y = 0; y < mip.Height; y++)
        for (var x = 0; x < mip.Width; x++)
        for (var c = 0; c < 4; c++)
        {
            var sum = 0;
            for (var dy = 0; dy < sy; dy++)
            for (var dx = 0; dx < sx; dx++)
                sum += full.Pixels[((y * sy + dy) * full.Width + x * sx + dx) * 4 + c];
            error += Math.Abs(sum / (sx * sy) - mip.Pixels[(y * mip.Width + x) * 4 + c]);
        }
        return error;
    }
}
