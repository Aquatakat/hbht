using System.Numerics;
using Aidyn.Core.Calendar;
using Aidyn.Core.Sky;

namespace Aidyn.Core.Tests;

public class SkyTests
{
    /// <summary>A gradient whose every column holds the given colour rows, as bytes.</summary>
    private static SkyGradient Gradient(Func<int, int, (byte R, byte G, byte B)> texel)
    {
        var rgba = new byte[SkyGradient.Columns * SkyGradient.Rows * 4];
        for (var row = 0; row < SkyGradient.Rows; row++)
        {
            for (var column = 0; column < SkyGradient.Columns; column++)
            {
                var (r, g, b) = texel(column, row);
                var i = (row * SkyGradient.Columns + column) * 4;
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (r, g, b, 255);
            }
        }
        return new SkyGradient(rgba);
    }

    /// <summary>Sky gradient 0's noon, which I08 has at the start of a new game.</summary>
    private static SkyGradient Noon() => Gradient((_, row) => row switch
    {
        SkyGradient.TintRow => (225, 225, 225),
        SkyGradient.SkyRow => (188, 205, 217),
        SkyGradient.FogRow => (79, 109, 153),
        _ => (0, 0, 0),
    });

    [Fact]
    public void TheTintIsTheLiftedGreyPlusAFifthOfTheSky()
    {
        var colors = SkyColors.At(Noon(), null, 0, 0.5f);

        // 225/255 × 0.65 + 0.15 + 0.2 × sky, truncated to bytes.
        Assert.Equal(new Vector3(222, 225, 227) / 255, colors.Tint);
        Assert.Equal(new Vector3(79, 109, 153) / 255, colors.Fog);
    }

    [Fact]
    public void ColumnsAreTwoHoursApartAndWrapAtMidnight()
    {
        var gradient = Gradient((column, _) => ((byte)(column * 20), 0, 0));

        Assert.Equal(60f / 255, gradient.Sample(0, 6 / 24f).X, 0.0001f);
        Assert.Equal(70f / 255, gradient.Sample(0, 7 / 24f).X, 0.0001f);
        // Halfway from 22:00's column back to midnight's.
        Assert.Equal(110f / 255, gradient.Sample(0, 23 / 24f).X, 0.0001f);
    }

    /// <summary>A gradient that runs from 0 at the top to 31 × 8 at row 31, in red.</summary>
    private static SkyGradient Ramp() => Gradient((_, row) => ((byte)(row < SkyGradient.SkyRows ? row * 8 : 0), 0, 0));

    private static Vector3[] Backdrop(SkyGradient gradient, Vector3 look, float fogTime = 0, Vector3 fog = default)
    {
        var lines = new Vector3[SkyBackdrop.ScreenLines];
        SkyBackdrop.Draw(gradient, null, 0, 0.5f, look, fog, lines, fogTime);
        return lines;
    }

    [Fact]
    public void LookingLevelTheGradientSpansTwoHundredLines()
    {
        var lines = Backdrop(Ramp(), -Vector3.UnitZ);

        // Line 0 is at 1/200 of the way down: looking level drops the top line.
        Assert.Equal(MathF.Floor(1 / 200f * 31 * 8) / 255, lines[0].X);
        // The foot, 199/200 of the way to row 31, then the same to the bottom of the screen.
        var foot = MathF.Floor((30 + 0.845f) * 8) / 255;
        Assert.Equal(foot, lines[198].X, 0.0001f);
        Assert.Equal(foot, lines[239].X, 0.0001f);
    }

    [Fact]
    public void LookingDownMovesTheHorizonUpBySinOfTheLooksHeight()
    {
        var look = Vector3.Normalize(new Vector3(0, -0.5f, -1));
        var lines = Backdrop(Ramp(), look);

        // sin(0.447) × 240 is 103 lines up, so the foot is at line 96, drawn from line 103 of 200.
        var drawn = SkyBackdrop.Span - (int)(MathF.Sin(-look.Y) * SkyBackdrop.ScreenLines);
        Assert.Equal(97, drawn);
        Assert.Equal(MathF.Floor(103 / 200f * 31 * 8) / 255, lines[0].X, 0.0001f);
        Assert.Equal(lines[drawn - 1], lines[drawn]);
        Assert.Equal(lines[drawn - 1], lines[239]);
        Assert.NotEqual(lines[drawn - 2], lines[drawn - 1]);
    }

    [Fact]
    public void LookingNearlyStraightDownLeavesTheBackgroundBlack()
    {
        var lines = Backdrop(Ramp(), Vector3.Normalize(new Vector3(0, -1, -0.05f)));
        Assert.All(lines, c => Assert.Equal(Vector3.Zero, c));
    }

    [Fact]
    public void FogFadesTheSkyToTheFogColourByHalfThick()
    {
        var fog = new Vector3(100, 110, 120) / 255;
        var lines = Backdrop(Ramp(), -Vector3.UnitZ, fogTime: 0.5f, fog);
        // Within a byte: the float arithmetic can land just under and truncate, as the game's would.
        Assert.All(lines, c => Assert.True(Vector3.Distance(fog, c) < 1.8f / 255));

        // A quarter thick: halfway to half the fog colour.
        var quarter = Backdrop(Ramp(), -Vector3.UnitZ, fogTime: 0.25f, fog);
        Assert.Equal(MathF.Floor((0.5f * (1 / 200f * 31 * 8 / 255) + 0.25f * fog.X) * 255) / 255, quarter[0].X, 0.0001f);
        Assert.Equal(MathF.Floor(0.25f * fog.Y * 255) / 255, quarter[0].Y, 0.0001f);
    }

    [Theory]
    [InlineData(6, 1, 0)]
    [InlineData(9, 0.7071f, 0.7071f)]
    [InlineData(12, 0, 1)]
    [InlineData(18, -1, 0)]
    [InlineData(0, 0, -1)]
    public void TheSunRisesInPlusXAndLightsFromBelowAtNight(float hour, float x, float y)
    {
        var sun = SkyLight.SunDirection(hour / 24);
        Assert.Equal(x, sun.X, 0.0001f);
        Assert.Equal(y, sun.Y, 0.0001f);
        Assert.Equal(0, sun.Z, 0.0001f);
    }

    [Fact]
    public void BodiesTakeHalfTheSkyColourAsAmbientAndTheSunOnTop()
    {
        var colors = SkyColors.At(Noon(), null, 0, 0.5f);
        var ambient = SkyLight.BodyAmbient(colors);
        // Sky 225 → 112.5, truncated.
        Assert.Equal(112f / 255, ambient.Y, 0.0001f);

        var sun = new Vector3(0.5f, 0.6f, 0.7f);
        var up = SkyLight.Shade(Vector3.UnitY, ambient, Vector3.UnitY, sun);
        Assert.Equal(Vector3.Min(ambient + sun, Vector3.One), up);
        // Facing away from the sun, the ambient alone.
        Assert.Equal(ambient, SkyLight.Shade(-Vector3.UnitY, ambient, Vector3.UnitY, sun));
        Assert.Equal(Vector3.Zero, SkyLight.SunColor(colors, outdoor: false));
    }

    [Fact]
    public void TheClockStartsAtNoonAndADayLastsTwentyMinutes()
    {
        var clock = GameClock.NewGame();
        Assert.Equal(0.5f, clock.TimeOfDay);

        clock.Step(20 * 60 * 60 / 2);
        Assert.Equal(0, clock.TimeOfDay, 0.0001f);
    }

    [Fact]
    public void AChangeOfSkyTakesSixHundredTicksAndEachChunkRestartsIt()
    {
        var day = Noon();
        var dark = Gradient((_, _) => (0, 0, 0));
        var state = new SkyState();
        state.Choose(day, drawn: true);
        Assert.Same(day, state.Current);

        state.Choose(dark, drawn: true);
        state.Step(300);
        Assert.Equal(0.5f, state.Change, 0.0001f);
        // Another chunk of the same terrain chooses the same sky again, which starts it over.
        state.Choose(dark, drawn: true);
        Assert.Equal(0, state.Change);
        state.Step(SkyState.ChangeTicks);
        Assert.Same(dark, state.Current);
        Assert.False(state.Changing);

        // A sky that stops being drawn changes at once.
        state.Choose(day, drawn: false);
        Assert.Same(day, state.Current);
    }

    [Fact]
    public void ChoosingTheSkyThatsUpAbandonsTheChange()
    {
        var day = Noon();
        var state = new SkyState();
        state.Choose(day, drawn: true);
        state.Choose(Gradient((_, _) => (0, 0, 0)), drawn: true);
        state.Step(100);

        state.Choose(day, drawn: true);
        Assert.False(state.Changing);
        Assert.Equal(SkyColors.At(day, null, 0, 0.5f), state.Colors(0.5f));
    }
}
