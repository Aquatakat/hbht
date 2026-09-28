using System.Numerics;
using Aidyn.Core.Calendar;
using Aidyn.Core.Sky;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class SkyObjectsTests
{
    private static RecordId Image(int n) => RecordId.Hex(RecordTypes.Image, n);

    private static readonly Rgba White = new(250, 250, 250, 250);

    /// <summary>A sky like the ROM's: two stars, the sun, the moon, two clouds and two flare pieces.</summary>
    private static SkyRecord Sky() => new(
        RecordId.Decimal(RecordTypes.Sky, 0),
        new SkyBody(Image(0x1A2), 1, 0, 90, White),
        new SkyBody(Image(0x198), 1, 0, 0, White),
        new MoonImages(Image(0x194), Image(0x379F), Image(0x195), Image(0x37A0), Image(0x196), Image(0x198)),
        [new SkyBody(Image(0x1A1), 0.19f, 10, 90, White), new SkyBody(Image(0x1A1), 0.19f, 10, 270, White)],
        [Image(0x18F), Image(0x190), Image(0x191)],
        [new Cloud(0, 0, -530, 255, 0.2f), new Cloud(1, 0, 530, 255, 0.4f)],
        [Image(0x192), Image(0x193), Image(0x192)],
        [new FlarePiece(0, 1, 5.02f, new Rgba(250, 250, 250, 50)), new FlarePiece(1, 0.5f, 1.12f, new Rgba(250, 250, 250, 50))]);

    private static Vector2 Size(RecordId image) => image.Key switch
    {
        "0x01A2" => new Vector2(64, 64),
        "0x018F" or "0x0190" => new Vector2(160, 60),
        _ => new Vector2(16, 16),
    };

    private static uint At(float hours) => GameClock.NewGame().Time - 12 * GameClock.Hour + (uint)(hours * GameClock.Hour);

    [Fact]
    public void TheSunRisesInPlusXAndIsDrawnInTheMiddleWhenTheCameraLooksAtIt()
    {
        // At 06:00 its height is 0: on the horizon, in +X.
        Near(new Vector2(160, 120), SkyObjects.Place(0, 90, false, Vector3.UnitX));
        Assert.Null(SkyObjects.Place(0, 90, false, -Vector3.UnitX));

        // Looking down moves it up the screen, 300 pixels to the unit of the look's height.
        var down = Vector3.Normalize(new Vector3(1, -0.2f, 0));
        Assert.Equal(120 + down.Y * 300, SkyObjects.Place(0, 90, false, down)!.Value.Y, 0.001f);

        // Overhead at noon, it's off the top of the screen, and at the edge of what's drawn at all.
        Assert.Null(SkyObjects.Place(90, 90, false, Vector3.UnitX));
    }

    [Fact]
    public void TheSunsElevationIsTheSineOfItsHeight()
    {
        // At 09:00, a height of 45° draws it at sin(45°) × 90 ≈ 63.6° up.
        var at = SkyObjects.Place(45, 90, false, Vector3.UnitX)!.Value;
        Assert.Equal(120 - MathF.Sin(63.64f * MathF.PI / 180) * 300, at.Y, 0.1f);
    }

    [Fact]
    public void StarsShowOnlyWhileTheSunIsDown()
    {
        var objects = new SkyObjects(Sky());
        var sprites = new List<SkySprite>();
        objects.Draw(sprites, At(12), Vector3.UnitX, Vector3.One, Size, new Random(1));
        Assert.DoesNotContain(sprites, s => s.Image == Image(0x1A1));

        sprites.Clear();
        objects.Draw(sprites, At(3), Vector3.UnitX, Vector3.One, Size, new Random(1));
        var star = Assert.Single(sprites, s => s.Image == Image(0x1A1));
        // Twinkling in greys, brighter the lower the sun: at 03:00 it's 45° down, so alpha × sin 45°.
        Assert.Equal(star.Color.X, star.Color.Y);
        Assert.InRange(star.Color.X, 160 / 255f, 1);
        Assert.Equal((byte)(250 * MathF.Sin(45 * MathF.PI / 180)) / 255f, star.Alpha, 0.001f);
    }

    [Fact]
    public void TheSunIsDrawnInTheSkysSunColourAndLeavesItsMiddleForTheFlare()
    {
        var objects = new SkyObjects(Sky());
        var sprites = new List<SkySprite>();
        var color = new Vector3(0.9f, 0.6f, 0.3f);
        objects.Draw(sprites, At(6), Vector3.UnitX, color, Size, new Random(1));

        var sun = Assert.Single(sprites, s => s.Image == Image(0x1A2));
        Assert.Equal(color, sun.Color, new Tolerance());
        Near(new Vector2(160, 120), sun.At);
        Near(new Vector2(192, 152), objects.SunMiddle);

        // Turning away leaves the flare's point where it was.
        objects.Draw(sprites, At(6), -Vector3.UnitX, color, Size, new Random(1));
        Near(new Vector2(192, 152), objects.SunMiddle);
    }

    [Theory]
    [InlineData(0, 0, 3, true)]
    [InlineData(0, 5, 2, true)]
    [InlineData(1, 3, 1, true)]
    [InlineData(2, 0, 0, false)]
    [InlineData(2, 2, 0, false)]
    [InlineData(3, 4, 2, false)]
    [InlineData(4, 1, 3, true)]
    public void TheMoonGoesRoundEveryFourWeeks(int week, int day, int phase, bool waxing)
    {
        var time = (uint)(week * GameClock.Week + day * GameClock.Day);
        Assert.Equal((phase, waxing), SkyObjects.MoonPhase(time));
    }

    [Fact]
    public void ANewGameStartsUnderANewMoon()
    {
        Assert.Equal((0, false), SkyObjects.MoonPhase(GameClock.NewGame().Time));
        Assert.Equal(Image(0x194), SkyObjects.MoonImage(Sky().MoonPhases, GameClock.NewGame().Time));
        // Day 8 turns from quarter to crescent at noon.
        Assert.Equal((2, true), SkyObjects.MoonPhase(GameClock.Week + GameClock.Day + 11 * GameClock.Hour));
        Assert.Equal((1, true), SkyObjects.MoonPhase(GameClock.Week + GameClock.Day + 12 * GameClock.Hour));
    }

    [Fact]
    public void NearerCloudsAreHigherLargerAndMoreOpaque()
    {
        var clouds = new Clouds(Sky());
        var sprites = new List<SkySprite>();
        // Looking along +X, the game sees the cloud at −Z, with X and Z swapped.
        clouds.Draw(sprites, Vector3.UnitX, new Vector3(0.8f), Size);

        var cloud = Assert.Single(sprites);
        var near = 1 - 530f / 750;
        Assert.Equal(near + 0.2f, cloud.Scale, 0.0001f);
        Assert.Equal((byte)(255 * MathF.Sqrt(near)) / 255f, cloud.Alpha, 0.0001f);
        Assert.Equal(new Vector2(160 - 160 * near / 2, 120 - near * 240 - 60 * near / 2), cloud.At);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(30f)]
    [InlineData(60f)]
    public void ProjectedPlacementIsPerspectivesAtAnyHeight(float height)
    {
        // The sun at an azimuth of 90° (+X), with the camera looking level, 25° to its right.
        var off = 25 * MathF.PI / 180;
        var look = new Vector3(MathF.Cos(off), 0, MathF.Sin(off));
        var projected = SkyObjects.Place(height, 90, false, look, SkyPlacement.Projected)!.Value;
        var original = SkyObjects.Place(height, 90, false, look)!.Value;

        var focal = 120 / MathF.Tan(22.5f * MathF.PI / 180);
        Assert.Equal(160 - focal * MathF.Tan(off), projected.X, 0.01f);
        Assert.Equal(160 - 300 * MathF.Sin(off), original.X, 0.01f);
        // Only across: the height is placed the same.
        Assert.Equal(original.Y, projected.Y);
    }

    [Fact]
    public void ProjectedCloudsTurnWithTheScenery()
    {
        // The cloud at −Z, which the game sees at +X (with X and Z swapped), and a look 40° to its
        // right, from +Z toward +X: the original draws it 300 × sin 40° left of the middle.
        var clouds = new Clouds(Sky()) { Placement = SkyPlacement.Projected };
        var sprites = new List<SkySprite>();
        var look = 50 * MathF.PI / 180;
        clouds.Draw(sprites, new Vector3(MathF.Sin(look), 0, MathF.Cos(look)), new Vector3(0.8f), Size);
        var cloud = Assert.Single(sprites);
        var near = 1 - 530f / 750;
        Assert.Equal(160 - SkyScreen.Focal * MathF.Tan(40 * MathF.PI / 180), cloud.At.X + 160 * near / 2, 0.01f);
    }

    [Fact]
    public void TheWindBlowsCloudsRoundToTheOtherSide()
    {
        var clouds = new Clouds(Sky()) { Wind = new Vector3(0, 0, -1) };
        // 4 × the wind a frame, 2 a tick: 110 ticks takes the first cloud from −530 to −750.
        clouds.Step(110);
        Assert.Equal(-750, clouds.Positions[0].Y, 0.01f);
        clouds.Step();
        Assert.Equal(750, clouds.Positions[0].Y, 0.01f);
    }

    [Fact]
    public void TheFlareFadesInOverFiveFramesAndDarkensMostAtTheMiddle()
    {
        var flare = new LensFlare(Sky());
        var middle = SkyScreen.Middle;
        flare.Step(middle, clear: true, tintRed: 0.8f, outdoor: true, ticks: 4);
        Assert.Equal(20, flare.Fade);
        flare.Step(middle, clear: true, tintRed: 0.8f, outdoor: true, ticks: 20);
        Assert.Equal(50, flare.Fade);
        Assert.Equal(0.4f, flare.Value, 0.0001f);

        // At the middle its pieces shrink away; 80 out they're full size.
        var sprites = new List<SkySprite>();
        flare.Draw(sprites, middle, 0.8f, Size);
        Assert.Empty(sprites);
        flare.Draw(sprites, middle + new Vector2(80, 0), 0.8f, Size);
        Assert.Equal(2, sprites.Count);
        Assert.Equal(5.02f, sprites[0].Scale);
        Assert.Equal((byte)(50 * 0.8f) / 255f, sprites[0].Alpha);

        // Hidden, it fades out, and the scenery isn't darkened.
        flare.Step(middle, clear: false, tintRed: 0.8f, outdoor: true, ticks: 2);
        Assert.Equal(40, flare.Fade);
        flare.Step(middle + new Vector2(90, 0), clear: true, tintRed: 0.8f, outdoor: true);
        Assert.Equal(0, flare.Value);
    }

    private static void Near(Vector2 expected, Vector2? actual) =>
        Assert.True(actual is { } a && Vector2.Distance(expected, a) < 0.001f, $"Expected {expected}, got {actual}.");

    private sealed class Tolerance : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.003f;
        public int GetHashCode(Vector3 v) => 0;
    }
}
