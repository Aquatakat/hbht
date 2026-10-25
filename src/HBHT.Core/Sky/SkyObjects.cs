using System.Numerics;
using HBHT.Core.Calendar;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Sky;

/// <summary>
/// The sun, moon and stars (<c>Skyobjects::Render</c>), drawn over the gradient and under the
/// clouds and scenery, in an outdoor sky. The stars come first, then the sun, then the moon.
/// </summary>
/// <remarks>
/// They aren't projected. Each has two angles, a height and an azimuth, and the screen position
/// comes from sines and cosines of those against the camera's look, 300 pixels to the unit from
/// the middle of the 320 × 240 screen. The sun's height is the time of day × 360° − 90°, and the
/// moon's twice (the time of day × 360° + 90°), so it goes round twice a day. The height doesn't
/// map to an elevation evenly: an object's elevation is sin(height) × 90°, so the sun is drawn at
/// about 64° at 09:00, where it lights from 45° (<see cref="SkyLight.SunDirection"/>).
/// </remarks>
public sealed class SkyObjects(SkyRecord sky)
{
    /// <summary>Each star's grey, 160 to 255, as its last twinkle drew it (<see cref="Twinkle"/>).</summary>
    private readonly byte[] _greys = new byte[sky.Stars.Count];

    /// <summary>The moon's azimuth in the table, which its light comes from (<see cref="SkyLight.MoonDirection"/>).</summary>
    public float MoonAzimuth => sky.Moon.Azimuth;

    /// <summary>
    /// Where the middle of the sun was last drawn, for the lens flare (<c>gLensFlarePos</c>). It
    /// starts off the screen, and keeps its place while the sun isn't drawn.
    /// </summary>
    public Vector2 SunMiddle { get; private set; } = new(-1, -1);

    /// <summary>How they're placed across the screen: the original's way, or projected.</summary>
    public SkyPlacement Placement { get; set; }

    /// <summary>Each star's grey as it's drawn, 160 to 255, in the table's order, as the last twinkle left it.</summary>
    public IReadOnlyList<byte> Greys => _greys;

    /// <summary>Whether the stars show at a time of day: while the sun is down, from 18:00 to 06:00.</summary>
    public static bool Night(float timeOfDay) => GameClock.SunHeight(timeOfDay) is <= 0 or >= 180;

    /// <summary>
    /// The stars twinkle (<c>Skyobjects::Render</c>): while they show, each draws a grey from 160 to 255, in the table's order,
    /// whether it lands on the screen or not; by day nothing is drawn.
    /// </summary>
    /// <param name="random">The stars' own stream (<see cref="Rng.RandomStreams.Twinkle"/>).</param>
    /// <param name="timeOfDay">The time of day the sky shows (<see cref="GameClock.ShownTimeOfDay"/>).</param>
    public void Twinkle(IRandom random, float timeOfDay)
    {
        if (!Night(timeOfDay))
            return;
        for (var i = 0; i < _greys.Length; i++)
            _greys[i] = (byte)random.Range(160, 255);
    }

    /// <summary>Adds this frame's sprites to <paramref name="sprites"/>, in the order they're drawn.</summary>
    /// <param name="look">The way the camera looks, normalised.</param>
    /// <param name="sunColor">The sky's sun colour (<see cref="SkyColors.Sun"/>), which the sun is drawn in.</param>
    /// <param name="fogTime">How thick the weather's fog is, which fades the sun, and the moon and stars faster.</param>
    /// <param name="shown">
    /// The time of day the sky shows, if not the clock's (<see cref="GameClock.ShownTimeOfDay"/>: Darkness and Light move it). The
    /// moon's phase is the clock's date's.
    /// </param>
    /// <remarks>The stars are drawn in the greys their last twinkle drew (<see cref="Twinkle"/>).</remarks>
    public void Draw(List<SkySprite> sprites, uint time, Vector3 look, Vector3 sunColor, ImageSize size,
        float fogTime = 0, float brightness = 1, float? shown = null)
    {
        var timeOfDay = shown ?? (float)(time % GameClock.Day) / GameClock.Day;
        var sunHeight = GameClock.SunHeight(timeOfDay);
        if (Night(timeOfDay))
        {
            // Brighter the lower the sun.
            var night = MathF.Sin(Degrees.ToRadians(-sunHeight));
            for (var i = 0; i < sky.Stars.Count; i++)
            {
                var star = sky.Stars[i];
                var grey = _greys[i];
                Add(star, star.Image, star.Height, new Rgba(grey, grey, grey, star.Color.A), 2, night);
            }
        }
        if (Add(sky.Sun, sky.Sun.Image, sunHeight, Bytes(sunColor, sky.Sun.Color.A), 1, null) is { } sun)
        {
            var image = size(sky.Sun.Image);
            SunMiddle = sun.Patch?.At(0.5f, 0.5f) ?? sun.At + new Vector2((int)image.X >> 1, (int)image.Y >> 1) * sky.Sun.Scale;
        }
        Add(sky.Moon, MoonImage(sky.MoonPhases, time), GameClock.MoonHeight(timeOfDay), sky.Moon.Color, 1.5f, null, moon: true);

        SkySprite? Add(SkyBody body, RecordId image, float height, Rgba color, float fogFade, float? night, bool moon = false)
        {
            if (Place(height, body.Azimuth, moon, look, Placement) is not { } at)
                return null;
            SkyPatch? patch = null;
            if (Placement == SkyPlacement.Projected)
            {
                var (direction, heading) = Direction(height, body.Azimuth, moon);
                patch = SkyPatch.Lay(direction, heading, Vector2.Zero, size(image) * body.Scale, look);
            }
            float a = color.A;
            var fog = a * fogTime * fogFade;
            var faded = fog <= a ? MathF.Max(fog, 0) : a;
            var alpha = (uint)(color.A - (ushort)faded);
            if (night is { } n)
                alpha = (uint)((byte)alpha * n);
            var rgb = new Vector3(color.R, color.G, color.B);
            if (brightness < 1)
            {
                rgb = new Vector3(ColorBytes.Single(rgb.X * brightness), ColorBytes.Single(rgb.Y * brightness), ColorBytes.Single(rgb.Z * brightness));
                alpha = (ushort)(alpha * brightness);
            }
            var sprite = new SkySprite(image, at, body.Scale, rgb / 255, (byte)alpha / 255f, patch);
            sprites.Add(sprite);
            return sprite;
        }
    }

    /// <summary>
    /// Where the sky puts an object on the 320 × 240 screen, or null when it's behind the camera or
    /// the wrong side of straight up or down.
    /// </summary>
    /// <param name="height">The first angle, in degrees: the sun's is the time of day's.</param>
    /// <param name="azimuth">
    /// The second, in degrees, turned half round while the height is between 90° and 270°, so an
    /// object that passes overhead carries on across the sky. The moon also sways 40° either side.
    /// </param>
    public static Vector2? Place(float height, float azimuth, bool moon, Vector3 look, SkyPlacement placement = SkyPlacement.Original)
    {
        var across = MathF.Sqrt(look.X * look.X + look.Z * look.Z);
        if (across == 0)
            return null;
        var (x, z) = (look.X / across, look.Z / across);
        if (placement == SkyPlacement.Projected)
            return SkyScreen.Project(Direction(height, azimuth, moon).Direction, look);
        var (sa, ca, se, ce) = Angles(height, azimuth, moon);
        if (ca * z + sa * x <= 0 || ce * across - se * look.Y <= 0)
            return null;
        return new Vector2(ca * x - sa * z, ce * look.Y + se * across) * SkyScreen.Spread + SkyScreen.Middle;
    }

    /// <summary>
    /// The way to an object, for <see cref="SkyPlacement.Projected"/>, and its heading in X and Z:
    /// the azimuth's heading is (sin, cos), and the elevation is the negative of the angle the game
    /// works with.
    /// </summary>
    public static (Vector3 Direction, Vector2 Heading) Direction(float height, float azimuth, bool moon)
    {
        var (sa, ca, se, _) = Angles(height, azimuth, moon);
        return (SkyScreen.Direction(sa, ca, -se), new Vector2(sa, ca));
    }

    private static (float SinA, float CosA, float SinE, float CosE) Angles(float height, float azimuth, bool moon)
    {
        var sway = moon ? MathF.Sin(Degrees.ToRadians(height)) * 40 : 0;
        var a = height <= 90 || height >= 270 ? azimuth + sway : azimuth + 180 - sway;
        var (sa, ca) = MathF.SinCos(Degrees.ToRadians(a));
        var (se, ce) = MathF.SinCos(Degrees.ToRadians(-(MathF.Sin(Degrees.ToRadians(height)) * 90)));
        return (sa, ca, se, ce);
    }

    /// <summary>The moon's image on a date, by its phase (<see cref="GameClock.MoonPhase"/>).</summary>
    public static RecordId MoonImage(MoonImages images, uint time) => GameClock.MoonPhase(time) switch
    {
        (0, _) => images.New,
        (1, true) => images.WaxingCrescent,
        (1, false) => images.WaningCrescent,
        (2, true) => images.FirstQuarter,
        (2, false) => images.ThirdQuarter,
        _ => images.Full,
    };

    private static Rgba Bytes(Vector3 color, byte alpha) =>
        new((byte)MathF.Round(color.X * 255), (byte)MathF.Round(color.Y * 255), (byte)MathF.Round(color.Z * 255), alpha);
}
