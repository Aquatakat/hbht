using System.Numerics;
using Aidyn.Plugins;

namespace Aidyn.Core.Sky;

/// <summary>
/// An image the sky draws on the original's 320 × 240 screen (<c>Borg8_DrawSimple</c>): texel ×
/// colour, blended by texel alpha × alpha, with the image's top-left corner at <see cref="At"/>.
/// </summary>
/// <param name="Color">In on-screen values, 0 to 1.</param>
/// <param name="Alpha">0 to 1, from the byte the game draws with.</param>
public readonly record struct SkySprite(RecordId Image, Vector2 At, float Scale, Vector3 Color, float Alpha);

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
    public static readonly float Focal = Middle.Y / MathF.Tan(22.5f * MathF.PI / 180);

    /// <summary>
    /// Where a sky sprite goes across the screen, given the sine of its angle off the look, in X and
    /// Z, for a point in front of the camera.
    /// </summary>
    public static float Across(float sine, SkyPlacement placement) => Middle.X + placement switch
    {
        // Perspective puts a direction at the tangent of its angle, whatever its height, when the
        // camera looks level.
        SkyPlacement.Projected => Focal * sine / MathF.Sqrt(MathF.Max(1 - sine * sine, 1e-6f)),
        _ => Spread * sine,
    };
}

/// <summary>How the sky places its sprites across the screen (a fidelity setting, ADR 0006).</summary>
public enum SkyPlacement
{
    /// <summary>
    /// The original's: 300 × the sine of the angle off the look. It's close to perspective in the
    /// middle of the screen, but toward the edges the scenery turns up to about 30% faster than
    /// the sky, so clouds, sun, moon and stars seem to drag along with the camera.
    /// </summary>
    Original,

    /// <summary>At the tangent, as perspective puts the scenery, so the sky stays put against it as the camera turns.</summary>
    Projected,
}
