using HBHT.Core.Ui;

namespace HBHT.Core.Tests;

public class EdgeFeatherTests
{
    private const int Width = 320, Height = 240, Side = 16, Rise = 12;

    /// <summary>The message box's ramp, its left gradient's alphas over the band's 136.</summary>
    private static readonly float[] Ramp = [.. new[] { 0, 17, 34, 51, 51, 68, 85, 102, 102, 119, 119, 119, 119, 119, 136, 136 }.Select(a => a / 136f)];

    private static float At(int x, int y) => EdgeFeather.Alpha(x, y, Width, Height, Side, Rise, Side, Rise, Ramp);

    [Fact]
    public void InsideTheInsetIsWhole()
    {
        Assert.Equal(1, At(Side, Rise));
        Assert.Equal(1, At(Width / 2, Height / 2));
        Assert.Equal(1, At(Width - Side - 1, Height - Rise - 1));
    }

    [Fact]
    public void AnEdgeRampsByTheBoxsOwnRamp()
    {
        // Along the middle of the left edge, each pixel takes the ramp's value for its place, so the side's 16
        // pixels are the box's 16.
        for (var x = 0; x < Side; x++)
            Assert.Equal(Ramp[x], At(x, Height / 2), 3);
        Assert.Equal(0, At(Width - 1, Height / 2));
        // The top's 12 take the same ramp, stretched, so its first pixel is all but clear.
        Assert.True(At(Width / 2, 0) < 0.05f);
    }

    [Fact]
    public void TheCornersAreRound()
    {
        // Two in from both edges, where each edge's ramp alone is a quarter up, the corner, out past the arc, is
        // clear, as the frame's corner is.
        Assert.Equal(0, At(0, 0));
        Assert.Equal(0, At(2, 2));
        Assert.Equal(Ramp[2], At(2, Height / 2), 3);
        Assert.True(At(Width / 2, 2) > Ramp[2]);
        // The four corners match.
        Assert.Equal(At(5, 4), At(Width - 1 - 5, 4), 4);
        Assert.Equal(At(5, 4), At(5, Height - 1 - 4), 4);
        Assert.Equal(At(5, 4), At(Width - 1 - 5, Height - 1 - 4), 4);
    }

    [Fact]
    public void ApplyMultipliesTheAlpha()
    {
        var rgba = Enumerable.Repeat((byte)200, Width * Height * 4).ToArray();
        EdgeFeather.Apply(rgba, Width, Height, Side, Rise, Side, Rise, Ramp);
        Assert.Equal(200, rgba[((Height / 2) * Width + Width / 2) * 4 + 3]);
        Assert.Equal(0, rgba[3]);
        Assert.Equal(200, rgba[0]);
    }
}
