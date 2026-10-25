using HBHT.Core.Calendar;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Ui;

/// <summary>
/// The sundial at the trek HUD's bottom left (<c>Sundial::Draw</c>, <c>0x800248a4</c> in 1.0): a dark ring, the moon's phase
/// at its centre, twelve rays, and the sun going round it twice a day, a glow by day and a small dot by night.
/// </summary>
public static class Sundial
{
    /// <summary>
    /// The ring (also the compass's), the rays, the moon by phase (new, crescent and quarter in the first and second
    /// halves of the moon's four weeks, full), and the sun's glow and dot.
    /// </summary>
    public static readonly RecordId Ring = HudSprite.Id(0x001B), Rays = HudSprite.Id(0x01BA),
        NewMoon = HudSprite.Id(0x01B8), CrescentEarly = HudSprite.Id(0x01B3), CrescentLate = HudSprite.Id(0x01B4),
        QuarterEarly = HudSprite.Id(0x01B6), QuarterLate = HudSprite.Id(0x01B7), FullMoon = HudSprite.Id(0x01B5),
        SunGlow = HudSprite.Id(0x01BB), SunDot = HudSprite.Id(0x01B9);

    /// <summary>The centre, (49, 199), which the sun circles 25 pixels out.</summary>
    public const float CentreX = 49, CentreY = 199, Radius = 25;

    /// <summary>
    /// What the sundial draws at a time, with the screen at <paramref name="brightness"/> (the scene's fade). The moon and
    /// the sun show only under an outdoor sky (<paramref name="sky"/>): entering a cave's hides them, and only the Detect
    /// Moon and Sun Phase spells show them there, which aren't ported.
    /// </summary>
    /// <param name="time">The clock's time, for the moon's phase.</param>
    public static IEnumerable<HudSprite> Draw(uint time, float brightness, bool sky)
    {
        var white = ColorBytes.Single(brightness * 255);
        var dim = ColorBytes.Single(brightness * 80);
        yield return new HudSprite(Ring, 19, 169, 1, new Rgba(white, white, white, dim));
        if (sky)
            yield return new HudSprite(Moon(time), 39, 189, 1, HudSprite.Times(new Rgba(160, 160, 220, 160), brightness));

        // The sun's angle: 720° a day from midnight at the top, clockwise, in whole degrees.
        var degrees = (int)((float)(time % GameClock.Day) / GameClock.Day * 720) % 720;
        var hour = degrees / 30f;
        var (color, glow, dot) = Light(hour, brightness);
        yield return new HudSprite(Rays, 24, 174, 1, color with { A = dim });
        if (!sky)
            yield break;
        var angle = degrees * 0.0174532924f;
        var x = HudSprite.Quarter(CentreX + Radius * MathF.Sin(angle) - 10);
        var y = HudSprite.Quarter(CentreY - Radius * MathF.Cos(angle) - 10);
        if (glow != 0)
            yield return new HudSprite(SunGlow, x, y, 1, color with { A = glow });
        if (dot != 0)
            yield return new HudSprite(SunDot, x, y, 1, color with { A = dot });
    }

    /// <summary>The moon's image by its phase (<see cref="GameClock.MoonPhase"/>), whatever the hour.</summary>
    public static RecordId Moon(uint time) => GameClock.MoonPhase(time) switch
    {
        (0, _) => NewMoon,
        (1, true) => CrescentEarly,
        (1, false) => CrescentLate,
        (2, true) => QuarterEarly,
        (2, false) => QuarterLate,
        _ => FullMoon,
    };

    /// <summary>
    /// The rays' and the sun's colour, and the glow's and the dot's alpha, by the hour: peach by day (7 to 21), blue by
    /// night (23 to 5), blending between at dawn and dusk. The original doesn't dim the blend's colour by the
    /// brightness, as it does the day's and the night's.
    /// </summary>
    public static (Rgba Color, byte Glow, byte Dot) Light(float hour, float brightness)
    {
        byte B(float v) => ColorBytes.Single(v);
        if (hour > 7 && hour < 21)
            return (HudSprite.Times(new Rgba(255, 190, 150, 0), brightness), B(160 * brightness), 0);
        if (hour > 23 || hour < 5)
            return (HudSprite.Times(new Rgba(120, 100, 190, 0), brightness), 0, B(160 * brightness));
        if (hour <= 7)
        {
            var u = hour - 5;
            return (new Rgba(B(120 + 67.5f * u), B(100 + 45 * u), B(190 - 20 * u), 0), B(80 * u * brightness), B((160 - 80 * u) * brightness));
        }
        var v = hour - 21;
        return (new Rgba(B(255 - 67.5f * v), B(190 - 45 * v), B(150 + 20 * v), 0), B((160 - 80 * v) * brightness), B(80 * v * brightness));
    }
}
