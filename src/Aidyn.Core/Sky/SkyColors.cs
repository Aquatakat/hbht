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

        var tintRow = Row(SkyGradient.TintRow);
        var scale = brightness * dim;
        var sky = Row(SkyGradient.SkyRow) * scale;
        var lift = -lensFlare * 0.5f;
        var tintGrey = (tintRow.X * 0.65f + 0.15f + lift) * scale;
        var skyGrey = (tintRow.Y * 0.65f + 0.15f + lift) * scale;
        var fifth = brightness * 0.2f * dim;
        return new SkyColors(
            ToBytes(Vector3.Clamp(sky * fifth + new Vector3(tintGrey), Vector3.Zero, Vector3.One)),
            ToBytes(Vector3.Clamp(sky * fifth + new Vector3(skyGrey), Vector3.Zero, Vector3.One)),
            ToBytes(Row(SkyGradient.FogRow) * scale),
            ToBytes(Row(SkyGradient.CloudRow) * scale),
            ToBytes(Row(SkyGradient.SunRow) * scale));
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

    /// <summary>As the game stores them, truncated to 8 bits (<c>V3ToRGB</c>).</summary>
    internal static Vector3 ToBytes(Vector3 c) =>
        new(MathF.Floor(Math.Clamp(c.X, 0, 1) * 255) / 255, MathF.Floor(Math.Clamp(c.Y, 0, 1) * 255) / 255, MathF.Floor(Math.Clamp(c.Z, 0, 1) * 255) / 255);
}
