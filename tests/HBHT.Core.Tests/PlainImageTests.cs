using HBHT.Core.Ui;

namespace HBHT.Core.Tests;

public class PlainImageTests
{
    private const int Width = 64, Height = 32;

    /// <summary>
    /// A parchment that darkens to the right, with a grain of alternate rows, a dark rule painted across it, and the
    /// left four columns torn away.
    /// </summary>
    private static byte[] Parchment()
    {
        var rgba = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var at = (y * Width + x) * 4;
                var tone = (byte)(200 - x + (y % 2) * 6);
                rgba[at] = rgba[at + 1] = rgba[at + 2] = tone;
                rgba[at + 3] = (byte)(x < 4 ? 0 : 255);
            }
        }
        for (var x = 20; x < 40; x++)
            Paint(rgba, x, 10, 30);
        return rgba;
    }

    private static void Paint(byte[] rgba, int x, int y, byte value)
    {
        var at = (y * Width + x) * 4;
        rgba[at] = rgba[at + 1] = rgba[at + 2] = value;
    }

    private static int Tone(byte[] rgba, int x, int y) => rgba[(y * Width + x) * 4];

    [Fact]
    public void A_patch_takes_the_tone_round_it_and_keeps_its_own_grain()
    {
        var rgba = Parchment();
        // From the plain parchment to the right, which is darker: the patch takes the tone where it's laid.
        PlainImage.Apply(rgba, Width, Height, [new(new ScreenRect(16, 6, 44, 14), 30, 18)]);
        for (var y = 6; y < 14; y++)
        {
            for (var x = 16; x < 44; x++)
                Assert.InRange(Tone(rgba, x, y), 200 - x + (y % 2) * 6 - 2, 200 - x + (y % 2) * 6 + 2);
        }
    }

    [Fact]
    public void The_alpha_and_whats_outside_the_patches_are_kept()
    {
        var rgba = Parchment();
        var before = (byte[])rgba.Clone();
        PlainImage.Apply(rgba, Width, Height, [new(new ScreenRect(2, 6, 44, 14), 20, 18)]);
        for (var i = 3; i < rgba.Length; i += 4)
            Assert.Equal(before[i], rgba[i]);
        Assert.Equal(before[((20 * Width) + 20) * 4], rgba[((20 * Width) + 20) * 4]);
        // The rule is gone, though the patch reaches the torn edge, whose pixels it doesn't meet.
        Assert.InRange(Tone(rgba, 30, 10), 200 - 30 - 2, 200 - 30 + 2);
    }

    [Fact]
    public void A_patch_meets_the_ones_laid_before_it_and_not_the_ones_to_come()
    {
        var rgba = Parchment();
        // The rule is covered by two patches side by side; the first mustn't meet the rule where the second lies.
        PlainImage.Apply(rgba, Width, Height,
        [
            new(new ScreenRect(16, 6, 30, 14), 30, 18), new(new ScreenRect(30, 6, 44, 14), 40, 18),
        ]);
        for (var x = 16; x < 44; x++)
            Assert.InRange(Tone(rgba, x, 10), 200 - x - 2, 200 - x + 2);
    }
}
