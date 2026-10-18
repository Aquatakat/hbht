using System.Numerics;
using Aidyn.Core.Calendar;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Sky;

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
    private const float Deg = MathF.PI / 180;

    /// <summary>
    /// Where the middle of the sun was last drawn, for the lens flare (<c>gLensFlarePos</c>). It
    /// starts off the screen, and keeps its place while the sun isn't drawn.
    /// </summary>
    public Vector2 SunMiddle { get; private set; } = new(-1, -1);

    /// <summary>How they're placed across the screen: the original's way, or projected.</summary>
    public SkyPlacement Placement { get; set; }

    /// <summary>Adds this frame's sprites to <paramref name="sprites"/>, in the order they're drawn.</summary>
    /// <param name="look">The way the camera looks, normalised.</param>
    /// <param name="sunColor">The sky's sun colour (<see cref="SkyColors.Sun"/>), which the sun is drawn in.</param>
    /// <param name="twinkle">Picks the stars' greys each frame, 160 to 255: the cosmetic stream (ADR 0006), so a
    /// save keeps its place and the sky can't shift a gameplay stream.</param>
    /// <param name="fogTime">How thick the weather's fog is, which fades the sun, and the moon and stars faster.</param>
    public void Draw(List<SkySprite> sprites, uint time, Vector3 look, Vector3 sunColor, ImageSize size, IRandom twinkle,
        float fogTime = 0, float brightness = 1)
    {
        var timeOfDay = (float)(time % GameClock.Day) / GameClock.Day;
        var sunHeight = SkyLight.SunHeight(timeOfDay);
        if (sunHeight <= 0 || sunHeight >= 180)
        {
            // Brighter the lower the sun.
            var night = MathF.Sin(-sunHeight * Deg);
            foreach (var star in sky.Stars)
            {
                var grey = (byte)twinkle.Range(160, 255);
                Add(star, star.Image, star.Height, new Rgba(grey, grey, grey, star.Color.A), 2, night);
            }
        }
        if (Add(sky.Sun, sky.Sun.Image, sunHeight, Bytes(sunColor, sky.Sun.Color.A), 1, null) is { } sun)
        {
            var image = size(sky.Sun.Image);
            SunMiddle = sun.Patch?.At(0.5f, 0.5f) ?? sun.At + new Vector2((int)image.X >> 1, (int)image.Y >> 1) * sky.Sun.Scale;
        }
        Add(sky.Moon, MoonImage(sky.MoonPhases, time), MoonHeight(timeOfDay), sky.Moon.Color, 1.5f, null, moon: true);

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
        var sway = moon ? MathF.Sin(height * Deg) * 40 : 0;
        var a = height <= 90 || height >= 270 ? azimuth + sway : azimuth + 180 - sway;
        var (sa, ca) = MathF.SinCos(a * Deg);
        var (se, ce) = MathF.SinCos(-(MathF.Sin(height * Deg) * 90) * Deg);
        return (sa, ca, se, ce);
    }

    /// <summary>The moon's first angle, in degrees (<c>Skyobjects::UpdateMoon</c>).</summary>
    public static float MoonHeight(float timeOfDay)
    {
        var height = (timeOfDay * 360 + 90) * 2;
        while (height > 360)
            height -= 360;
        return height;
    }

    /// <summary>
    /// The moon's phase on a date (<c>World::set_moonPhase</c>): 0 new, 1 crescent, 2 quarter and 3
    /// full, over a cycle of four weeks.
    /// </summary>
    /// <remarks>
    /// Days 0–2 of the cycle are full, 3–7 a quarter, 9–13 a crescent, 14–16 new, 17–21 a crescent
    /// and 23–27 a quarter. Day 8 turns from quarter to crescent at noon, and day 22 from crescent to
    /// quarter. The images count the first two weeks as waxing, though the moon wanes then.
    /// </remarks>
    public static (int Phase, bool Waxing) MoonPhase(uint time)
    {
        var week = (int)(time % GameClock.Season / GameClock.Week);
        var day = (int)(time % GameClock.Week / GameClock.Day);
        var afternoon = time % GameClock.Day / GameClock.Hour >= 12;
        var phase = (day + (week & 3) * 7) switch
        {
            < 3 => 3,
            >= 14 and <= 16 => 0,
            (>= 3 and <= 7) or >= 23 => 2,
            8 => afternoon ? 1 : 2,
            22 => afternoon ? 2 : 1,
            _ => 1,
        };
        return (phase, (week & 3) >> 1 == 0);
    }

    /// <summary>The moon's image on a date.</summary>
    public static RecordId MoonImage(MoonImages images, uint time) => MoonPhase(time) switch
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
