using HBHT.Plugins.Assets;
using HBHT.TestSupport;

namespace HBHT.Plugins.Tests;

public class PngImageTests
{
    /// <summary>A 5 × 4 image whose samples all differ, so every filter predicts something.</summary>
    private static byte[] Samples(int channels) => [.. Enumerable.Range(0, 5 * 4 * channels).Select(i => (byte)(i * 37 + i / 3 * 11))];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryRowFilterReadsBackTheSamePixels(int filter)
    {
        var rgba = Samples(4);
        var image = PngImage.Decode(TestPng.Encode(5, 4, 6, rgba, filter: y => y == 0 ? 0 : filter));

        Assert.Equal((5, 4), (image!.Value.Width, image.Value.Height));
        Assert.Equal(rgba, image.Value.Rgba);
    }

    [Fact]
    public void FiltersCanChangeFromRowToRow()
    {
        var rgba = Samples(4);
        Assert.Equal(rgba, PngImage.Decode(TestPng.Encode(5, 4, 6, rgba, filter: y => 4 - y))!.Value.Rgba);
    }

    [Fact]
    public void RgbIsOpaqueAndItsTransparentColourIsClear()
    {
        var rgb = Samples(3);
        var key = new byte[] { 0, rgb[3], 0, rgb[4], 0, rgb[5] };
        var image = PngImage.Decode(TestPng.Encode(5, 4, 2, rgb, filter: _ => 1, chunks: [("tRNS", key)]))!.Value;

        Assert.Equal([rgb[0], rgb[1], rgb[2], 255, rgb[3], rgb[4], rgb[5], 0], image.Rgba[..8]);
    }

    [Fact]
    public void APaletteGivesItsColoursAndAlphas()
    {
        byte[] palette = [10, 20, 30, 40, 50, 60];
        byte[] indices = [.. Enumerable.Range(0, 20).Select(i => (byte)(i % 2))];
        var image = PngImage.Decode(TestPng.Encode(5, 4, 3, indices, filter: _ => 2, chunks: [("PLTE", palette), ("tRNS", [128])]))!.Value;

        Assert.Equal([10, 20, 30, 128, 40, 50, 60, 255], image.Rgba[..8]);
    }

    [Fact]
    public void SixteenBitGreyAndAlphaTakesEachSamplesTopByte()
    {
        byte[] samples = [.. Enumerable.Range(0, 5 * 4 * 4).Select(i => (byte)(i * 13))];
        var image = PngImage.Decode(TestPng.Encode(5, 4, 4, samples, depth: 16, filter: _ => 3))!.Value;

        Assert.Equal([samples[0], samples[0], samples[0], samples[2]], image.Rgba[..4]);
    }

    [Fact]
    public void WhatItCantReadIsNull()
    {
        Assert.Null(PngImage.Decode("not a png"u8));
        // Interlaced (Adam7).
        var interlaced = TestPng.Encode(5, 4, 6, Samples(4));
        interlaced[8 + 8 + 12] = 1;
        Assert.Null(PngImage.Decode(interlaced));
        // A 4-bit palette.
        Assert.Null(PngImage.Decode(TestPng.Encode(4, 4, 3, new byte[8], depth: 4, chunks: ("PLTE", new byte[48]))));
    }
}
