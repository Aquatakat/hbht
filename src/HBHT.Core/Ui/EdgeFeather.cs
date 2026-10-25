namespace HBHT.Core.Ui;

/// <summary>
/// Fades an image out at its edges with round corners, as the original's feathered boxes are
/// (<c>IntroMenu_ShadowBG</c>, the message box): each pixel's alpha is multiplied by a ramp of its distance from the
/// rectangle inset by the feather's widths, so along an edge it ramps across, and in a corner its contours are arcs
/// round the inset rectangle's corner, an ellipse's where the sides' and the top's widths differ. The ramp is the
/// box's own, read off its edge image, which eases out: 0, 17, 34, 51, 51, 68 … 136 over its 16 pixels.
/// </summary>
public static class EdgeFeather
{
    /// <summary>
    /// Multiplies the alpha of an RGBA8 image by the feather, <paramref name="left"/>, <paramref name="top"/>,
    /// <paramref name="right"/> and <paramref name="bottom"/> pixels in from each edge.
    /// </summary>
    /// <param name="ramp">The ramp from the edge in, a value a pixel, from 0 to 1 at its last.</param>
    public static void Apply(byte[] rgba, int width, int height, int left, int top, int right, int bottom, IReadOnlyList<float> ramp)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var a = Alpha(x, y, width, height, left, top, right, bottom, ramp);
                var i = (y * width + x) * 4 + 3;
                rgba[i] = (byte)Math.Round(rgba[i] * a);
            }
        }
    }

    /// <summary>The feather at a pixel, from 0 to 1, by its centre's distance from the inset rectangle.</summary>
    public static float Alpha(int x, int y, int width, int height, int left, int top, int right, int bottom, IReadOnlyList<float> ramp)
    {
        var (px, py) = (x + 0.5f, y + 0.5f);
        var dx = px < left ? (left - px) / left : px > width - right ? (px - (width - right)) / right : 0;
        var dy = py < top ? (top - py) / top : py > height - bottom ? (py - (height - bottom)) / bottom : 0;
        var d = MathF.Sqrt(dx * dx + dy * dy);
        if (d <= 0)
            return 1;
        // Where along the ramp the pixel is, its first pixel's centre at the edge.
        var at = Math.Clamp((1 - d) * ramp.Count - 0.5f, 0, ramp.Count - 1);
        var below = (int)at;
        var above = Math.Min(below + 1, ramp.Count - 1);
        return ramp[below] + (ramp[above] - ramp[below]) * (at - below);
    }
}
