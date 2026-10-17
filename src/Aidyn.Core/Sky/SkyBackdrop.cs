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
        Lines(from, to, change, timeOfDay, fog, lines[..drawn], top, fogTime, lensFlare, brightness, dim);
        lines[drawn..ScreenLines].Fill(lines[drawn - 1]);
    }

    /// <summary>
    /// The lines the gradient is drawn in, from its top to its foot: line <c>i</c> is <c>i + 1</c> two-hundredths of the
    /// way down, as the original draws them when the camera looks level or up, and its foot is the colour below.
    /// </summary>
    public const int DomeLines = Span - 1;

    /// <summary>
    /// The way the follow camera usually looks down, in radians, at which the projected gradient is where the
    /// original draws it: its default pitch of 15° raises the camera by sin 15° of its distance, not tan, so it looks
    /// down by atan(sin 15°), 14.5° (<see cref="Cameras.FollowCamera.DefaultPitch"/>).
    /// </summary>
    public static readonly float DomePitch = MathF.Atan(MathF.Sin(15 * MathF.PI / 180));

    /// <summary>How many lines the original moves the gradient up at <see cref="DomePitch"/>: 59.</summary>
    public static readonly int DomeDown = (int)(MathF.Sin(MathF.Sin(DomePitch)) * ScreenLines);

    /// <summary>
    /// The projected sky's gradient, a dome (<see cref="SkyPlacement.Projected"/>): how far down the gradient an
    /// <paramref name="elevation"/> in radians is, from 0 at its top to 1 at its foot, clamped to the lines the
    /// original draws. Each elevation keeps the place the original gives it when the follow camera looks down as it
    /// usually does (<see cref="DomePitch"/>), down the middle of the screen, where a line's elevation is its angle
    /// off the look less the pitch. So the gradient stays pinned against the scenery's horizon at any pitch, as the
    /// sky's objects do: 0.52 of the way down at the horizon, its foot 18.5° under it, out of sight
    /// behind the ground, and its top 17.1° up, above which the sky is its top colour.
    /// </summary>
    public static float DomeFraction(float elevation)
    {
        var angle = elevation + DomePitch;
        var line = angle >= MathF.PI / 2 - 1e-3f ? float.NegativeInfinity
            : SkyScreen.Middle.Y - SkyScreen.Focal * MathF.Tan(angle) + DomeDown;
        return Math.Clamp(line / Span, 1f / Span, DomeLines / (float)Span);
    }

    /// <summary>
    /// Fills <paramref name="lines"/> with the <see cref="DomeLines"/> lines of the gradient, from its top to its foot,
    /// as <see cref="Draw"/> colours them, for the projected sky to lay over the dome (<see cref="DomeFraction"/>).
    /// </summary>
    public static void Dome(SkyGradient from, SkyGradient? to, float change, float timeOfDay, Vector3 fog,
        Span<Vector3> lines, float fogTime = 0, float lensFlare = 0)
    {
        if (lines.Length < DomeLines)
            throw new ArgumentException($"The dome has {DomeLines} lines.", nameof(lines));
        Lines(from, to, change, timeOfDay, fog, lines[..DomeLines], 1, fogTime, lensFlare, 1, 1);
    }

    /// <summary>The gradient's lines from <paramref name="top"/> two-hundredths of the way down, one a two-hundredth.</summary>
    private static void Lines(SkyGradient from, SkyGradient? to, float change, float timeOfDay, Vector3 fog, Span<Vector3> lines,
        int top, float fogTime, float lensFlare, float brightness, float dim)
    {
        var x = timeOfDay * SkyGradient.Columns;
        var column = (short)(int)x % SkyGradient.Columns;
        var next = (column + 1) % SkyGradient.Columns;
        var across = x - column;
        var fogged = MathF.Min(fogTime * 2, 1);

        for (var i = 0; i < lines.Length; i++)
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
    }

    /// <summary>Between two rows and two columns, as the game interpolates the image.</summary>
    private static Vector3 Sample(SkyGradient g, int row, float down, int column, int next, float across)
    {
        var left = Vector3.Lerp(g[column, row], g[column, row + 1], down);
        var right = Vector3.Lerp(g[next, row], g[next, row + 1], down);
        return Vector3.Lerp(left, right, across);
    }
}
