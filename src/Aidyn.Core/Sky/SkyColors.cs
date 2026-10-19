using System.Numerics;

namespace Aidyn.Core.Sky;

/// <summary>
/// The colours the sky sets for a frame (<c>Sky::SetColors</c>, <c>gGlobals.sky.colors</c>), in
/// on-screen values. The ROM's machine code agrees on the constants: 0.65, 0.15 and 0.2.
/// </summary>
/// <param name="Tint">
/// <c>gMainColor</c>, which every chunk's scenery and scene object is multiplied by: a grey from
/// the gradient's tint row, lifted, plus a fifth of the sky colour.
/// </param>
/// <param name="Sky">
/// <c>gSkyColor</c>. Half of it is bodies' ambient light. It's worked out as <see cref="Tint"/> is,
/// but from the tint row's green; the row is grey, so the two are the same.
/// </param>
/// <param name="Fog">The fog colour, and what the sky fades to in fog.</param>
/// <param name="Cloud">The clouds' colour.</param>
/// <param name="Sun">The sun's colour, and its light on bodies.</param>
public readonly record struct SkyColors(Vector3 Tint, Vector3 Sky, Vector3 Fog, Vector3 Cloud, Vector3 Sun)
{
    /// <summary>
    /// The colours at <paramref name="timeOfDay"/>, part way from <paramref name="from"/> to
    /// <paramref name="to"/> while the sky changes.
    /// </summary>
    /// <param name="change">How far the change has got, 0 to 1 (<c>gGlobals.sky.gray</c>).</param>
    /// <param name="lensFlare">Darkens the scenery while the camera looks toward the sun (<c>lensFlareVal</c>).</param>
    /// <param name="brightness">The screen fade, 1 when nothing is fading (<c>gGlobals.brightness</c>).</param>
    /// <param name="dim">A slower fade the game sets for effects (<c>gGlobals.sky.unk40</c>), usually 1.</param>
    public static SkyColors At(SkyGradient from, SkyGradient? to, float change, float timeOfDay,
        float lensFlare = 0, float brightness = 1, float dim = 1)
    {
        Vector3 Row(int row)
        {
            var c = from.Sample(row, timeOfDay);
            return change > 0 && to is not null ? c - (c - to.Sample(row, timeOfDay)) * change : c;
        }

        // As RenderSky does (SkyColors.At, 0x8001cee0): the grey and the fifth are worked out in doubles (the literals
        // 0.65, 0.15 and 0.2 are doubles) and cast to float, and brightness and then dim scale each in turn.
        var tintRow = Row(SkyGradient.TintRow);
        Vector3 Scaled(Vector3 c) => c * brightness * dim;
        var sky = Scaled(Row(SkyGradient.SkyRow));
        var lift = -lensFlare * 0.5f;
        float Grey(float row) => (float)(row * 0.65 + 0.15 + lift) * brightness * dim;
        var tintGrey = Grey(tintRow.X);
        var skyGrey = Grey(tintRow.Y);
        var fifth = (float)(brightness * 0.2 * dim);
        return new SkyColors(
            ToBytes(Vector3.Clamp(sky * fifth + new Vector3(tintGrey), Vector3.Zero, Vector3.One)),
            ToBytes(Vector3.Clamp(sky * fifth + new Vector3(skyGrey), Vector3.Zero, Vector3.One)),
            ToBytes(Scaled(Row(SkyGradient.FogRow))),
            ToBytes(Scaled(Row(SkyGradient.CloudRow))),
            ToBytes(Scaled(Row(SkyGradient.SunRow))));
    }

    /// <summary>
    /// The colours on the frame lightning flashes (<c>RenderSky</c>, <c>0x8001d840</c> in 1.0): the tint and the sky
    /// colour white, so the scenery is drawn untinted and bodies' ambient is half white. The fog, cloud and sun colours
    /// don't change.
    /// </summary>
    public SkyColors Flashed() => this with { Tint = Vector3.One, Sky = Vector3.One };

    /// <summary>
    /// Where the scenery's fog starts on the 0–1000 depth scale it ends at 1000 on (<c>Zones.SetSceneColors</c>,
    /// <c>0x8000c298</c>): 996, less the weather's drawn fog × 40, truncated, so from 966 at its thickest.
    /// </summary>
    public static int FogStart(float fogTime) => 996 - (short)(int)(fogTime * 40);

    /// <summary>
    /// As the game stores them, 8 bits each (<c>V3ToRGB</c>), made by <see cref="ColorBytes.Single"/>: × <c>255.0f</c> and
    /// truncated (<c>0x8001d1b4</c> and on). The original clamps the tint and sky to 0–1 first and the rest not at all; every
    /// colour here is within 0–1 already, so the clamp is only the tint's and sky's.
    /// </summary>
    public static Vector3 ToBytes(Vector3 c) =>
        new(Byte(c.X), Byte(c.Y), Byte(c.Z));

    private static float Byte(float channel) => ColorBytes.Single(Math.Clamp(channel, 0, 1) * 255f) / 255f;
}
