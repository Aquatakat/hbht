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
}
