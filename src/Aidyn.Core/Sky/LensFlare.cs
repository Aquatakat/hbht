using System.Numerics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Sky;

/// <summary>
/// The lens flare (<c>Lensflare::Render</c>): eight images along the line from the middle of the
/// screen through the sun, drawn over the scene while the sun is on screen and nothing hides it.
/// While the sun is within 80 pixels of the middle, it also darkens the scenery and lifts the sky's
/// gradient (<see cref="Value"/>).
/// </summary>
/// <remarks>
/// It fades in over five frames and out over five, in steps of 10 up to 50. The original tests
/// whether anything hides the sun by the depth buffer under the middle of its image, at the end of
/// the frame, for the next frame; the caller decides that. The images are drawn in white and pink
/// with alpha up to 50, scaled by the scenery's tint's red.
/// </remarks>
public sealed class LensFlare(SkyRecord sky)
{
    /// <summary>How near the middle the sun has to be to darken the scenery, and from where the images are full size.</summary>
    public const float Reach = 80;

    /// <summary>How far faded in, 0 to 50 (<c>lensflare_bss</c>).</summary>
    public float Fade { get; private set; }

    /// <summary>
    /// How much the flare darkens the scenery's tint (by half this) and lifts the gradient
    /// (<c>gGlobals.sky.lensFlareVal</c>), set by <see cref="Step"/> for the next frame's sky.
    /// </summary>
    public float Value { get; private set; }

    /// <summary>
    /// Fades the flare in or out for <paramref name="ticks"/>, at 30 fps's pace, and works out
    /// <see cref="Value"/>.
    /// </summary>
    /// <param name="sunMiddle">Where the middle of the sun was last drawn (<see cref="SkyObjects.SunMiddle"/>).</param>
    /// <param name="clear">Whether nothing was drawn over that point: the sky shows there.</param>
    /// <param name="tintRed">The scenery's tint's red, 0 to 1, which scales the flare.</param>
    /// <param name="outdoor">Only an outdoor sky has a flare; elsewhere it stays as it is, and off.</param>
    public void Step(Vector2 sunMiddle, bool clear, float tintRed, bool outdoor, int ticks = 1)
    {
        Value = 0;
        if (!outdoor)
            return;
        var onScreen = clear && sunMiddle.X > 10 && sunMiddle.X < SkyScreen.Width - 10 && sunMiddle.Y > 10 && sunMiddle.Y < SkyScreen.Height - 10;
        var step = Cadence.PerTick(10) * ticks;
        Fade = Math.Clamp(Fade + (onScreen ? step : -step), 0, 50);
        var distance = Vector2.Distance(sunMiddle, SkyScreen.Middle);
        if (Fade > 0 && distance <= Reach)
            Value = tintRed * (1 - distance / Reach) * 0.5f * Fade / 50;
    }

    /// <summary>Adds the flare's sprites to <paramref name="sprites"/>, if it shows.</summary>
    public void Draw(List<SkySprite> sprites, Vector2 sunMiddle, float tintRed, ImageSize size)
    {
        if (Fade <= 0)
            return;
        var offset = sunMiddle - SkyScreen.Middle;
        var along = MathF.Min(offset.Length() / Reach, 1);
        var alpha = (byte)(Fade * tintRed) / 255f;
        foreach (var piece in sky.LensFlare)
        {
            var scale = piece.Scale * along;
            if (scale <= 0.2f)
                continue;
            var image = sky.FlareImages[piece.Image];
            var extent = size(image);
            var half = new Vector2((int)extent.X >> 1, (int)extent.Y >> 1);
            var at = SkyScreen.Middle + piece.Distance * offset - half * scale;
            sprites.Add(new SkySprite(image, at, scale, new Vector3(piece.Color.R, piece.Color.G, piece.Color.B) / 255, alpha));
        }
    }
}
