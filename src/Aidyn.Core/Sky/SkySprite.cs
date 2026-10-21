using System.Numerics;
using Aidyn.Plugins;

namespace Aidyn.Core.Sky;

/// <summary>
/// An image the sky draws on the original's 320 × 240 screen (<c>Borg8_DrawSimple</c>): texel ×
/// colour, blended by texel alpha × alpha, with the image's top-left corner at <see cref="At"/>.
/// </summary>
/// <param name="Color">In on-screen values, 0 to 1.</param>
/// <param name="Alpha">0 to 1, from the byte the game draws with.</param>
/// <param name="Patch">
/// Where the image lies on the sky, when it's projected (<see cref="SkyPlacement.Projected"/>): then
/// it's drawn over the patch's projection, not as a rectangle at <see cref="At"/>.
/// </param>
public readonly record struct SkySprite(RecordId Image, Vector2 At, float Scale, Vector3 Color, float Alpha, SkyPatch? Patch = null);

/// <summary>
/// An image pinned to the sky: a flat patch a unit from the camera, facing it, projected as the
/// scenery is. Nearby sprites keep their places against each other however the camera turns, and
/// they stretch and tilt toward the screen's edges as the scenery does.
/// </summary>
/// <param name="Corner">The image's top-left corner.</param>
/// <param name="Across">From its left edge to its right.</param>
/// <param name="Down">From its top edge to its bottom.</param>
/// <param name="Look">The way the camera looks, which it's projected for.</param>
public readonly record struct SkyPatch(Vector3 Corner, Vector3 Across, Vector3 Down, Vector3 Look)
{
    /// <summary>
    /// Where a point of the image goes on the 320 × 240 screen, from 0 to 1 across and down it, or
    /// null when it's behind the camera.
    /// </summary>
    public Vector2? At(float across, float down) => SkyScreen.Project(Corner + Across * across + Down * down, Look);

    /// <summary>
    /// Lays an image on the sky as it's drawn on the 320 × 240 screen when the camera looks straight
    /// along <paramref name="direction"/>: its top-left corner <paramref name="offset"/> pixels from
    /// the middle, <paramref name="size"/> pixels large, and upright against the horizon.
    /// </summary>
    /// <param name="heading">The direction's heading in X and Z, normalised, which keeps it upright even overhead.</param>
    public static SkyPatch Lay(Vector3 direction, Vector2 heading, Vector2 offset, Vector2 size, Vector3 look)
    {
        var right = new Vector3(-heading.Y, 0, heading.X);
        var up = Vector3.Cross(right, direction);
        var corner = direction + (right * offset.X - up * offset.Y) / SkyScreen.Focal;
        return new SkyPatch(corner, right * (size.X / SkyScreen.Focal), -up * (size.Y / SkyScreen.Focal), look);
    }
}

/// <summary>The size of an image in pixels, which placing some sprites needs.</summary>
public delegate Vector2 ImageSize(RecordId image);

/// <summary>The original's 2D screen, which the sky's sprites are placed on.</summary>
public static class SkyScreen
{
    public const float Width = 320, Height = 240;

    /// <summary>The middle of the screen, where the camera looks.</summary>
    public static Vector2 Middle => new(Width / 2, Height / 2);

    /// <summary>
    /// How far the sky's placement moves a sprite on the screen for a unit of the sines and cosines
    /// it works from: a radian, near the middle of the screen.
    /// </summary>
    public const float Spread = 300;

    /// <summary>
    /// How far perspective moves a point on the screen for a unit of the tangent of its angle off
    /// the look: 120 / tan 22.5°, for the Borg3 cameras' 45° field of view. It's within 4% of
    /// <see cref="Spread"/>, so the two agree in the middle of the screen and part toward the edges.
    /// </summary>
    public static readonly float Focal = Middle.Y / MathF.Tan(Degrees.ToRadians(22.5f));

    /// <summary>
    /// Where perspective puts a direction on the screen, as it puts the scenery, for a camera that
    /// looks along <paramref name="look"/> without rolling; or null when it's behind the camera. A
    /// sprite placed here stays pinned to its direction however the camera turns, as if infinitely
    /// far away, like a skybox. Off the 320 × 240 screen it carries on, onto a wider window's sides.
    /// </summary>
    /// <param name="look">The way the camera looks, normalised, not straight up or down.</param>
    public static Vector2? Project(Vector3 direction, Vector3 look)
    {
        var ahead = Vector3.Dot(direction, look);
        if (ahead <= 1e-3f)
            return null;
        var right = Vector3.Normalize(Vector3.Cross(look, Vector3.UnitY));
        var up = Vector3.Cross(right, look);
        return Middle + new Vector2(Vector3.Dot(direction, right), -Vector3.Dot(direction, up)) * (Focal / ahead);
    }

    /// <summary>A direction from its heading in X and Z, normalised, and the sine of its elevation.</summary>
    public static Vector3 Direction(float x, float z, float sineUp)
    {
        var level = MathF.Sqrt(MathF.Max(1 - sineUp * sineUp, 0));
        return new Vector3(x * level, sineUp, z * level);
    }
}

/// <summary>How the sky places its sprites across the screen (a setting, ADR 0018).</summary>
public enum SkyPlacement
{
    /// <summary>
    /// The original's: 300 × the sine of the angle off the look, across and up separately. It's
    /// close to perspective in the middle of the screen, but toward the edges the scenery turns up
    /// to about 30% faster than the sky, so clouds, sun, moon and stars seem to drag along with the
    /// camera, and a 16:9 window shows more of the edges than the N64's 4:3 did.
    /// </summary>
    Original,

    /// <summary>
    /// Each sprite is laid on the sky from a direction and projected as the scenery is
    /// (<see cref="SkyPatch"/>), so the sky stays pinned against the scenery as the camera turns, at
    /// any pitch. Where the camera looks straight at a sprite, it's drawn as the original draws it. The
    /// gradient behind them is a dome, each elevation keeping the line the original gives it at the
    /// follow camera's usual pitch (<see cref="SkyBackdrop.DomeFraction"/>).
    /// </summary>
    Projected,
}
