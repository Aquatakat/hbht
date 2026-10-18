using System.Numerics;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// The compass at the trek HUD's bottom right (<c>Compass::Draw</c>, <c>0x800240b8</c> in 1.0): the sundial's ring, and
/// N, E, S and W around it, turned so the way the camera looks is at the top, brightest there and dimmest at the
/// bottom. Trek draws it only under an outdoor sky, with no minimap showing.
/// </summary>
public static class Compass
{
    /// <summary>The letters, images rather than text. The decomp's names for them are wrong.</summary>
    public static readonly RecordId North = HudSprite.Id(0x0018), South = HudSprite.Id(0x0019), West = HudSprite.Id(0x001A),
        East = HudSprite.Id(0x0017);

    /// <summary>The centre, (271, 199), which the letters' centres circle 25 pixels out.</summary>
    public const float CentreX = 271, CentreY = 199, Radius = 25;

    /// <summary>
    /// What the compass draws with the camera looking along <paramref name="look"/>, its unit direction in X and Z
    /// (<c>rotationXZ</c>), and the screen at <paramref name="brightness"/>. North is −Z and east +X.
    /// </summary>
    public static IEnumerable<HudSprite> Draw(Vector2 look, float brightness)
    {
        var white = ColorBytes.Single(brightness * 255);
        yield return new HudSprite(Sundial.Ring, 241, 169, 1, new Rgba(white, white, white, ColorBytes.Single(brightness * 80)));
        var (a, b) = (look.X, look.Y);
        yield return Letter(North, -a, b, 160 - 95 * b);
        yield return Letter(South, a, -b, 160 + 95 * b);
        yield return Letter(West, b, a, 160 - 95 * a);
        yield return Letter(East, -b, -a, 160 + 95 * a);

        HudSprite Letter(RecordId image, float x, float y, float alpha)
        {
            var color = HudSprite.Times(new Rgba(160, 160, 220, 0), brightness) with { A = ColorBytes.Single(alpha * brightness) };
            return new HudSprite(image, HudSprite.Quarter(CentreX + Radius * x - 8), HudSprite.Quarter(CentreY + Radius * y - 8), 1, color);
        }
    }
}
