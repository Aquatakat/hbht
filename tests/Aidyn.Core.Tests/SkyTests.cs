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
