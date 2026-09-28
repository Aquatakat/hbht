using System.Numerics;

namespace Aidyn.Core.Sky;

/// <summary>
/// The sky behind the scenery (<c>Sky::GenerateGradient</c>, <c>Sky::RenderSky</c>): rows 0–31 of
/// the gradient drawn down the original's 240-line screen, one colour a line, moved up the screen
/// as the camera looks down.
/// </summary>
/// <remarks>
/// The gradient spans 200 lines, and its foot, the horizon, is at line 200 when the camera looks
/// level. Looking down by a pitch moves it up by sin(sin(pitch)) × 240 lines: the game takes the
/// sine of the look's height, which is already a sine. Looking up doesn't move it down, but drops
/// the gradient's top line. Below the foot, the rest of the screen takes the last line's colour, and
/// looking down so far that no line is left, the background is black. It isn't projected, so it
/// doesn't meet the scenery's horizon exactly, and the far plane at 180 hides the difference.
/// </remarks>
public static class SkyBackdrop
{
    /// <summary>The original's screen height, which the game's 2D drawing works in (<c>SCREEN_HEIGHT</c>, and <c>GetVRes</c>, always 240).</summary>
    public const int ScreenLines = 240;

    /// <summary>The lines the gradient spans, and the line its foot is on when the camera looks level.</summary>
    public const int Span = 200;

    /// <summary>
    /// Fills <paramref name="lines"/>, one colour for each of the <see cref="ScreenLines"/> screen
    /// lines from the top, in on-screen values truncated to bytes.
    /// </summary>
    /// <param name="look">The way the camera looks (<c>gCamera.aim − gCamera.pos</c>), normalised.</param>
    /// <param name="fog">The frame's fog colour (<see cref="SkyColors.Fog"/>), which the sky fades to in the weather's fog.</param>
    /// <param name="fogTime">How thick the weather's fog is, 0 to 1 (<c>weather.fogTime</c>). The sky is fully fogged from a half.</param>
    /// <param name="lensFlare">Brightens the sky while the camera looks toward the sun (<c>lensFlareVal</c>).</param>
    public static void Draw(SkyGradient from, SkyGradient? to, float change, float timeOfDay, Vector3 look, Vector3 fog,
        Span<Vector3> lines, float fogTime = 0, float lensFlare = 0, float brightness = 1, float dim = 1)
    {
        if (lines.Length < ScreenLines)
            throw new ArgumentException($"The screen has {ScreenLines} lines.", nameof(lines));

        // The game measures from the camera to its aim's opposite: pos − aim.
        var down = (int)(MathF.Sin(-look.Y) * ScreenLines);
        var drawn = (short)(Span - down);
        if (drawn <= 0)
        {
            lines[..ScreenLines].Clear();
            return;
        }
        drawn = Math.Min(drawn, (short)(Span - 1));
        var top = Span - drawn;

        var x = timeOfDay * SkyGradient.Columns;
        var column = (short)(int)x % SkyGradient.Columns;
        var next = (column + 1) % SkyGradient.Columns;
        var across = x - column;
        var fogged = MathF.Min(fogTime * 2, 1);

        for (var i = 0; i < drawn; i++)
        {
            var y = (i + top) / (float)Span * (SkyGradient.SkyRows - 1);
            var row = (short)(int)y;
            var c = Sample(from, row, y - row, column, next, across);
            if (change > 0 && to is not null)
                c -= (c - Sample(to, row, y - row, column, next, across)) * change;
            if (lensFlare != 0 || fogged != 0)
            {
                c += new Vector3(lensFlare);
                c -= (c - fog * fogged) * fogged;
                c = Vector3.Clamp(c, Vector3.Zero, Vector3.One);
            }
            if (brightness < 1)
                c *= brightness;
            if (dim < 1)
                c *= dim;
            lines[i] = SkyColors.ToBytes(c);
        }
        lines[drawn..ScreenLines].Fill(lines[drawn - 1]);
    }

    /// <summary>Between two rows and two columns, as the game interpolates the image.</summary>
    private static Vector3 Sample(SkyGradient g, int row, float down, int column, int next, float across)
    {
        var left = Vector3.Lerp(g[column, row], g[column, row + 1], down);
        var right = Vector3.Lerp(g[next, row], g[next, row + 1], down);
        return Vector3.Lerp(left, right, across);
    }
}
