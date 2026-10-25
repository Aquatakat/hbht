namespace HBHT.Core.Ui;

/// <summary>
/// A tab's image with what's painted into it covered by plain parchment from elsewhere in it: the pause menu draws it over a
/// tab beside the one showing to fade the tab into the parchment, the painted layout with the widgets.
/// </summary>
/// <remarks>
/// Each patch copies a piece of plain parchment over an area and blends it in seamlessly: the copy keeps its own grain,
/// plus a smooth correction that meets the image all round the area's edge, so the copy takes on the tone and the stains
/// round it and leaves no seam. The correction is the membrane over the differences on the edge (it solves Laplace's
/// equation inside the area, by over-relaxation). Patches are laid in order, each meeting what's laid before it, and
/// an edge pixel that's in a patch still to come, or by the torn edge, is left out of the sum.
/// </remarks>
public static class PlainImage
{
    /// <summary>A piece of plain parchment, from (<paramref name="SourceX"/>, <paramref name="SourceY"/>), laid over an area.</summary>
    public readonly record struct Patch(ScreenRect Area, int SourceX, int SourceY);

    /// <summary>The over-relaxation's passes and factor, enough to settle a patch 50 pixels across.</summary>
    private const int Passes = 200;

    private const float Relaxation = 1.9f;

    /// <summary>How near the torn edge, in steps across and down, an edge pixel is left out, as the edge is darkened.</summary>
    private const int TornEdge = 2;

    /// <summary>
    /// Lays the patches over an image of RGBA bytes, in place. The copies are read from the image as given, and its
    /// alpha is kept.
    /// </summary>
    public static void Apply(byte[] rgba, int width, int height, IReadOnlyList<Patch> patches)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException("The image isn't width × height RGBA bytes.", nameof(rgba));
        var original = (byte[])rgba.Clone();
        var torn = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                torn[y * width + x] = NearTransparent(original, width, height, x, y);
        }
        for (var i = 0; i < patches.Count; i++)
            Lay(rgba, original, torn, width, height, patches[i], patches.Skip(i + 1).Select(p => p.Area).ToList());
    }

    private static bool NearTransparent(byte[] rgba, int width, int height, int x, int y)
    {
        for (var dy = -TornEdge; dy <= TornEdge; dy++)
        {
            for (var dx = -TornEdge; dx <= TornEdge; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > TornEdge)
                    continue;
                int u = x + dx, v = y + dy;
                if (u >= 0 && u < width && v >= 0 && v < height && rgba[(v * width + u) * 4 + 3] < 255)
                    return true;
            }
        }
        return false;
    }

    private static void Lay(byte[] rgba, byte[] original, bool[] torn, int width, int height, Patch patch, List<ScreenRect> later)
    {
        var area = patch.Area;
        int w = area.Right - area.Left, h = area.Bottom - area.Top;
        if (w <= 0 || h <= 0)
            return;
        if (area.Left < 0 || area.Top < 0 || area.Right > width || area.Bottom > height
            || patch.SourceX < 0 || patch.SourceY < 0 || patch.SourceX + w > width || patch.SourceY + h > height)
            throw new ArgumentException($"The patch {patch} isn't inside the image.", nameof(patch));

        // The grid is the area and the ring round it: known on the ring where it can be read, solved inside.
        int gw = w + 2, gh = h + 2;
        var known = new bool[gw * gh];
        var c = new float[gw * gh * 3];
        var sum = new double[3];
        var count = 0;
        for (var gy = 0; gy < gh; gy++)
        {
            for (var gx = 0; gx < gw; gx++)
            {
                var inside = gx > 0 && gx < gw - 1 && gy > 0 && gy < gh - 1;
                var corner = (gx == 0 || gx == gw - 1) && (gy == 0 || gy == gh - 1);
                if (inside || corner)
                    continue;
                int x = area.Left + gx - 1, y = area.Top + gy - 1;
                int sx = patch.SourceX + gx - 1, sy = patch.SourceY + gy - 1;
                if (x < 0 || x >= width || y < 0 || y >= height || torn[y * width + x] || later.Any(r => Contains(r, x, y))
                    || sx < 0 || sx >= width || sy < 0 || sy >= height)
                    continue;
                known[gy * gw + gx] = true;
                count++;
                for (var ch = 0; ch < 3; ch++)
                {
                    var d = rgba[(y * width + x) * 4 + ch] - original[(sy * width + sx) * 4 + ch];
                    c[(gy * gw + gx) * 3 + ch] = d;
                    sum[ch] += d;
                }
            }
        }
        if (count == 0)
            return;
        for (var gy = 1; gy < gh - 1; gy++)
        {
            for (var gx = 1; gx < gw - 1; gx++)
            {
                known[gy * gw + gx] = true;
                for (var ch = 0; ch < 3; ch++)
                    c[(gy * gw + gx) * 3 + ch] = (float)(sum[ch] / count);
            }
        }

        // Red-black over-relaxation; a neighbour that isn't known is left out of the average.
        for (var pass = 0; pass < Passes; pass++)
        {
            for (var parity = 0; parity < 2; parity++)
            {
                for (var gy = 1; gy < gh - 1; gy++)
                {
                    for (var gx = 1 + (gy + 1 + parity) % 2; gx < gw - 1; gx += 2)
                    {
                        int at = gy * gw + gx, n = 0;
                        float r = 0, g = 0, b = 0;
                        foreach (var next in (ReadOnlySpan<int>)[at - 1, at + 1, at - gw, at + gw])
                        {
                            if (!known[next])
                                continue;
                            n++;
                            r += c[next * 3];
                            g += c[next * 3 + 1];
                            b += c[next * 3 + 2];
                        }
                        c[at * 3] += Relaxation * (r / n - c[at * 3]);
                        c[at * 3 + 1] += Relaxation * (g / n - c[at * 3 + 1]);
                        c[at * 3 + 2] += Relaxation * (b / n - c[at * 3 + 2]);
                    }
                }
            }
        }

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                int to = ((area.Top + y) * width + area.Left + x) * 4, from = ((patch.SourceY + y) * width + patch.SourceX + x) * 4;
                var at = ((y + 1) * gw + x + 1) * 3;
                for (var ch = 0; ch < 3; ch++)
                    rgba[to + ch] = (byte)Math.Clamp(MathF.Round(original[from + ch] + c[at + ch]), 0, 255);
            }
        }
    }

    private static bool Contains(ScreenRect r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
}
