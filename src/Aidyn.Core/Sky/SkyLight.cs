using System.Numerics;
using Aidyn.Core.Calendar;

namespace Aidyn.Core.Sky;

/// <summary>
/// How the sky lights bodies and animated scene objects: an ambient colour, and the sun from its
/// place in the sky in <see cref="SkyColors.Sun"/> (<c>some_player_render_sub</c>, <c>set_sun_light</c>).
/// Scenery isn't lit by it: its lit meshes get a white ambient and no sun, and the tint instead.
/// </summary>
/// <remarks>
/// The RSP lights a vertex with the ambient plus each light's colour times the cosine between the
/// vertex's normal and the way to the light, where that's positive, clamped to 1, in on-screen
/// values. The camera is in the projection matrix, so the lights' directions are in world space.
/// <para>
/// The game writes a light's colour into the second of the two copies <c>Light_t</c> holds and
/// zeroes the first (<c>Scene::addLight</c>, checked against the debug ROM's machine code). The
/// sun still lights Alaron on the emulator, as the brightest parts of his shirt in daylight (about
/// 216) are well over what the ambient alone could give (at most 127), so F3DEX2 reads the second.
/// </para>
/// </remarks>
public static class SkyLight
{
    /// <summary>
    /// The way to the sun (<c>gGlobals.SunPos</c>, <c>Skyobjects::UpdateSun</c>): it rises in +X at
    /// 06:00, is overhead at noon and sets in −X at 18:00, and lights from below at night.
    /// </summary>
    /// <param name="azimuth">The sun's second angle in the sky objects' table, 90 in the ROM, in degrees.</param>
    public static Vector3 SunDirection(float timeOfDay, float azimuth = 90)
    {
        var height = Degrees.ToRadians(GameClock.SunHeight(timeOfDay));
        var (sin, cos) = MathF.SinCos(Degrees.ToRadians(azimuth));
        var across = MathF.Cos(height);
        return new Vector3(across * sin, MathF.Sin(height), across * cos);
    }

    /// <summary>
    /// The way to the moon (<c>Sky.MoonDirection</c>, <c>0x800D9538</c>, <c>SkyObjects.UpdateMoon</c>, <c>0x8002A090</c>): at its
    /// height (<see cref="GameClock.MoonHeight"/>) and its azimuth in the sky objects' table, without the sway it's drawn with.
    /// It's up while y is over 0, from 06:00 to noon and from 18:00 to midnight.
    /// </summary>
    public static Vector3 MoonDirection(float timeOfDay, float azimuth = 0)
    {
        var height = Degrees.ToRadians(GameClock.MoonHeight(timeOfDay));
        var (sin, cos) = MathF.SinCos(Degrees.ToRadians(azimuth));
        var across = MathF.Cos(height);
        return new Vector3(across * sin, MathF.Sin(height), across * cos);
    }

    /// <summary>
    /// A body's ambient (<c>playerData.skyTint</c>): half the sky colour, truncated to bytes
    /// (<c>Scene::SetLightColors</c> takes u8s).
    /// </summary>
    public static Vector3 BodyAmbient(SkyColors colors) => SkyColors.ToBytes(colors.Sky * 0.5f);

    /// <summary>An animated scene object's ambient: the scenery's tint (<c>set_sun_light</c>).</summary>
    public static Vector3 ObjectAmbient(SkyColors colors) => colors.Tint;

    /// <summary>The sun's light, which only an outdoor sky gives (<c>SkyTypeOutdoor</c>); in caves it's black.</summary>
    public static Vector3 SunColor(SkyColors colors, bool outdoor) => outdoor ? colors.Sun : Vector3.Zero;

    /// <summary>The RSP's lighting of one vertex, in on-screen values.</summary>
    public static Vector3 Shade(Vector3 normal, Vector3 ambient, Vector3 sunDirection, Vector3 sunColor) =>
        Vector3.Min(ambient + sunColor * MathF.Max(Vector3.Dot(normal, sunDirection), 0), Vector3.One);
}
