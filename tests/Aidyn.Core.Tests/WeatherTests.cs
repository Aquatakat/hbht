using System.Numerics;
using Aidyn.Core.Calendar;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class WeatherTests
{
    /// <summary>The base game's rules, as the importer writes them.</summary>
    internal static readonly WeatherRecord Rules = new(RecordId.Decimal(RecordTypes.Weather, 0), [43, 48, 33, 25, 36, 40],
        KeepAtMost: 75, FallBelow: 70, IntensitySpread: 0.15f, FallFog: new WeatherRange(0.1f, 0.35f), Fog: new WeatherRange(0.1f, 0.75f),
        WindChance: 0.05f, WindTurn: 0.1f, WindRise: new WeatherRange(-0.004f, 0.001f), WindGust: 1.5f / 648, WindMaxSpeed: 150f / 648,
        FallRate: 1f / 300, FogRate: 1f / 900, LightningEvery: 20, LightningShare: 0.25f);

    private static readonly TerrainRecord Plains = Terrain(0, chance: 0, scale: 0.5f);
    private static readonly TerrainRecord Snowy = Terrain(5, chance: 30, scale: 0.6f, snow: true);
    private static readonly TerrainRecord Dry = Terrain(3, chance: -100, scale: 0);

    private static TerrainRecord Terrain(int n, int chance, float scale, bool snow = false) =>
        new(RecordId.Decimal(RecordTypes.Terrain, n), RecordId.Hex(RecordTypes.Image, 0), true, 0, chance, scale, snow);

    /// <summary>The value that makes a d100 (<c>RollD(1, 100)</c>) come up <paramref name="roll"/>.</summary>
    private static uint D100(int roll) => (uint)Math.Ceiling((roll - 1) * 4294967296.0 / 100);

    /// <summary>The value that makes a float from 0 to 1 come up about <paramref name="share"/>.</summary>
    private static uint Share(double share) => (uint)(share * 4294967295.0);

    [Fact]
    public void ADieOfSeventyFiveOrLessKeepsTheWeather()
    {
        var weather = new Weather(Rules);
        weather.Force(WeatherKind.Fog, fog: 0.4f);
        var random = new ScriptedRandom(D100(75));
        weather.Roll(0, Plains, random);
        Assert.Equal((WeatherKind.Fog, 0.4f), (weather.Kind, weather.Fog));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void OverSeventyFiveClearsItAndPrecipitationNeedsTheSeasonsAndTerrainsChance()
    {
        var weather = new Weather(Rules);
        weather.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.6f, 0.3f);
        // Season 0's 43 and the plains' 0: a 44 misses.
        var random = new ScriptedRandom(D100(76), D100(44));
        weather.Roll(0, Plains, random);
        Assert.Equal((WeatherKind.Clear, Precipitation.None, 0f, 0f, 0f), (weather.Kind, weather.Precipitation, weather.Intensity, weather.Fog, weather.Storm));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void RainFallsAtTheTerrainsScaleWithALittleFogAndAStorm()
    {
        var weather = new Weather(Rules);
        // 43 hits; 69 is rain; the intensity half way from 0.35 to 0.65, the fog the bottom of 0.1 to 0.35, the storm half the rain.
        var random = new ScriptedRandom(D100(76), D100(43), D100(69), Share(0.5), 0, Share(0.5));
        weather.Roll(0, Plains, random);
        Assert.Equal((WeatherKind.Precipitation, Precipitation.Rain), (weather.Kind, weather.Precipitation));
        Assert.Equal(0.5f, weather.Intensity, 3);
        Assert.Equal(0.1f, weather.Fog, 3);
        Assert.Equal(0.25f, weather.Storm, 3);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void ItSnowsOnSnowyTerrainWithNoStorm()
    {
        var weather = new Weather(Rules);
        // Season 3's 25 and the terrain's 30 make 55.
        var random = new ScriptedRandom(D100(100), D100(55), D100(1), 0, Share(1));
        weather.Roll(3, Snowy, random);
        Assert.Equal((WeatherKind.Precipitation, Precipitation.Snow), (weather.Kind, weather.Precipitation));
        Assert.Equal(0.45f, weather.Intensity, 3);
        Assert.Equal(0.35f, weather.Fog, 3);
        Assert.Equal(0f, weather.Storm);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void SeventyAndOverIsFog()
    {
        var weather = new Weather(Rules);
        var random = new ScriptedRandom(D100(80), D100(1), D100(70), Share(1));
        weather.Roll(0, Plains, random);
        Assert.Equal((WeatherKind.Fog, Precipitation.None, 0f), (weather.Kind, weather.Precipitation, weather.Intensity));
        Assert.Equal(0.75f, weather.Fog, 3);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void ADryTerrainNeverPrecipitates()
    {
        var weather = new Weather(Rules);
        var random = new ScriptedRandom(D100(100), D100(1));
        weather.Roll(1, Dry, random);
        Assert.Equal(WeatherKind.Clear, weather.Kind);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void ItRollsWhenThePartOfTheDayChangesOrTheTerrainDoes()
    {
        var weather = new Weather(Rules) { Wind = Vector3.Zero };
        var still = new ScriptedRandom();
        // First seen: no roll. The same part: none. The wind would draw, so it's left out.
        weather.Frame(PartOfDay.Morning, 0, Plains, still, still, wind: false);
        weather.Frame(PartOfDay.Morning, 0, Plains, still, still, wind: false);
        var once = new ScriptedRandom(D100(1));
        weather.Frame(PartOfDay.Midday, 0, Plains, once, still, wind: false);
        Assert.Equal(0, once.Remaining);

        Assert.False(weather.SetTerrain(0));
        Assert.False(weather.SetTerrain(0));
        weather.Frame(PartOfDay.Midday, 0, Plains, still, still, wind: false);
        Assert.True(weather.SetTerrain(5));
        var again = new ScriptedRandom(D100(1));
        weather.Frame(PartOfDay.Midday, 0, Snowy, again, still, wind: false);
        Assert.Equal(0, again.Remaining);
        Assert.Equal(PartOfDay.Midday, weather.PartOfDay);
    }

    [Fact]
    public void TheWindWandersAndKeepsUnderItsMost()
    {
        var weather = new Weather(Rules) { Wind = new Vector3(1, 0, 0) * (150f / 648) };
        // No turn (a share over 0.05), then a gust at the top of its range, which the most holds back.
        weather.DriftWind(new ScriptedRandom(Share(0.5), 0, Share(1)));
        Assert.Equal(150f / 648, weather.Wind.Length(), 5);
        Assert.Equal(1, Vector3.Normalize(weather.Wind).X, 5);

        // A turn at the bottom of each range, then no gust: the speed holds.
        weather.DriftWind(new ScriptedRandom(0, 0, 0, 0, Share(0.5)));
        Assert.Equal(150f / 648, weather.Wind.Length(), 5);
        var turned = Vector3.Normalize(new Vector3(0.9f, -0.004f, -0.1f));
        Assert.Equal(turned.X, Vector3.Normalize(weather.Wind).X, 5);
        Assert.Equal(turned.Z, Vector3.Normalize(weather.Wind).Z, 5);
    }

    [Fact]
    public void RainSetsInAtAThreeHundredthATickAndDiesAwayBeforeSnow()
    {
        // The first frame takes the fog as it is, which then moves.
        var weather = new Weather(Rules);
        weather.Draw(skyDrawn: true);
        (weather.Kind, weather.Precipitation, weather.Intensity, weather.Fog) = (WeatherKind.Precipitation, Precipitation.Rain, 0.5f, 0.2f);
        weather.Draw(skyDrawn: true);
        Assert.Equal(Precipitation.Rain, weather.Falling);
        Assert.Equal(1f / 300, weather.FallIntensity, 6);
        Assert.Equal(1f / 900, weather.FogTime, 6);
        for (var i = 0; i < 200; i++)
            weather.Draw(skyDrawn: true);
        Assert.Equal(0.5f, weather.FallIntensity, 6);
        Assert.Equal(0.2f, weather.FogTime, 6);

        weather.Precipitation = Precipitation.Snow;
        weather.Draw(skyDrawn: true);
        Assert.Equal(Precipitation.Rain, weather.Falling);
        Assert.Equal(0.5f - 1f / 300, weather.FallIntensity, 5);
        for (var i = 0; i < 160; i++)
            weather.Draw(skyDrawn: true);
        Assert.Equal(Precipitation.Snow, weather.Falling);
    }

    [Fact]
    public void ThereIsNoFogWhereTheSkyIsntDrawn()
    {
        var weather = new Weather(Rules);
        weather.Force(WeatherKind.Fog, fog: 0.5f);
        weather.Draw(skyDrawn: false);
        Assert.Equal(0f, weather.FogTime);
        Assert.Equal(0.5f, weather.Fog);
        // Coming out under the sky, the fog is there at once.
        weather.Draw(skyDrawn: true);
        Assert.Equal(0.5f, weather.FogTime);
    }

    [Fact]
    public void ALoadedGameDrawsTheRainAsTheSaveHasItWithTheRainSkyAtOnce()
    {
        var saved = new Weather(Rules);
        saved.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.5f, 0.3f);
        saved.Draw(skyDrawn: true);
        // What a save holds (GameState.Save), into a fresh weather.
        var loaded = new Weather(Rules)
        {
            Kind = saved.Kind, Precipitation = saved.Precipitation, Intensity = saved.Intensity, Fog = saved.Fog, Storm = saved.Storm,
            Falling = saved.Falling, FallIntensity = saved.FallIntensity, FogTime = saved.FogTime,
        };
        Assert.False(loaded.WantsRainSky);
        loaded.ResumeDrawing();
        // The first chunk chooses the rain sky at once, and the rain goes on as hard as it was, its loop at its level.
        Assert.True(loaded.WantsRainSky && loaded.HoldsSky);
        loaded.Draw(skyDrawn: true);
        Assert.Equal((Precipitation.Rain, 0.5f, 0.3f), (loaded.Falling, loaded.FallIntensity, loaded.FogTime));
        Assert.Equal((true, 191), (loaded.RainLoop, (int)loaded.RainLoopLevel));
    }

    [Fact]
    public void RainBringsTheRainSkyAsItSetsInAndTheTerrainsComesBackAsSoonAsItsWantedNoMore()
    {
        var weather = new Weather(Rules);
        var changes = new List<bool>();
        weather.SkyChange += changes.Add;
        weather.Draw(skyDrawn: true);
        (weather.Kind, weather.Precipitation, weather.Intensity) = (WeatherKind.Precipitation, Precipitation.Rain, 0.5f);
        // Under no sky, it falls, but brings no sky and no emitters.
        weather.Draw(skyDrawn: false);
        Assert.Equal((Precipitation.Rain, false, false), (weather.Falling, weather.Emitting, weather.SkyOverridden));
        Assert.Empty(changes);
        weather.Draw(skyDrawn: true);
        weather.Draw(skyDrawn: true);
        Assert.Equal([true], changes);
        Assert.True(weather.SkyOverridden && weather.Emitting && weather.HoldsSky);

        // Cleared: the terrain's sky at once, while the rain still dies away and holds the sky against entering chunks.
        weather.Precipitation = Precipitation.None;
        weather.Draw(skyDrawn: true);
        Assert.Equal([true, false], changes);
        Assert.False(weather.SkyOverridden || weather.Emitting);
        Assert.Equal(Precipitation.Rain, weather.Falling);
        Assert.True(weather.HoldsSky);
        for (var i = 0; i < 160; i++)
            weather.Draw(skyDrawn: true);
        Assert.False(weather.HoldsSky);

        // Fog alone changes nor holds the sky.
        weather.Fog = 0.75f;
        weather.Draw(skyDrawn: true);
        Assert.Equal([true, false], changes);
        Assert.False(weather.HoldsSky || weather.WantsRainSky);
    }

    [Fact]
    public void RainTurningToSnowKeepsTheRainSkyAndChoosesItAgainAsTheSnowSetsIn()
    {
        var weather = new Weather(Rules);
        var changes = new List<bool>();
        weather.SkyChange += changes.Add;
        weather.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.1f);
        weather.Draw(skyDrawn: true);
        weather.Precipitation = Precipitation.Snow;
        weather.Draw(skyDrawn: true);
        Assert.False(weather.Emitting);
        Assert.True(weather.SkyOverridden);
        for (var i = 0; i < 40 && weather.Falling != Precipitation.Snow; i++)
            weather.Draw(skyDrawn: true);
        Assert.Equal(Precipitation.Snow, weather.Falling);
        Assert.Equal([true, true], changes);
        Assert.True(weather.Emitting);
    }

    [Fact]
    public void TheRainLoopPlaysWhileRainFallsUnderTheSkyAtItsIntensityTimesOneAndAHalf()
    {
        var weather = new Weather(Rules);
        weather.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.5f);
        weather.Draw(skyDrawn: true);
        Assert.True(weather.RainLoop);
        Assert.Equal(1, weather.RainLoopStarts);
        // trunc(0.5 × 1.5 × 255) = 191, and full from two thirds.
        Assert.Equal(191, weather.RainLoopLevel);
        weather.FallIntensity = 0.7f;
        Assert.Equal(255, weather.RainLoopLevel);
        weather.FallIntensity = 0.5f;

        // Where the sky isn't drawn it stops at once, and starts again coming out.
        weather.Draw(skyDrawn: false);
        Assert.False(weather.RainLoop);
        weather.Draw(skyDrawn: true);
        Assert.Equal((true, 2), (weather.RainLoop, weather.RainLoopStarts));

        // Dying away, it plays on, quieter, until the rain is gone.
        weather.Precipitation = Precipitation.None;
        weather.Draw(skyDrawn: true);
        Assert.True(weather.RainLoop);
        Assert.Equal((int)((0.5f - 1f / 300) * 1.5 * 255), weather.RainLoopLevel);
        for (var i = 0; i < 160; i++)
            weather.Draw(skyDrawn: true);
        Assert.False(weather.RainLoop);

        // Snow is silent.
        weather.Force(WeatherKind.Precipitation, Precipitation.Snow, 0.5f);
        weather.Draw(skyDrawn: true);
        Assert.False(weather.RainLoop);
    }

    [Fact]
    public void ANewGameMayChangeTheWeatherAsItsClockIsSetToNoon()
    {
        var weather = new Weather(Rules);
        var random = new ScriptedRandom(D100(76), D100(43), D100(70), 0);
        weather.NewGame(GameClock.NewGame(), Plains, random, new ScriptedRandom(Share(1), Share(1)));
        Assert.Equal((WeatherKind.Fog, 0.1f, PartOfDay.Afternoon), (weather.Kind, weather.Fog, weather.PartOfDay));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void LightningStrikesEveryTwentiethFrameOnAShareUnderAQuarterOfTheStorm()
    {
        var weather = new Weather(Rules);
        weather.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.8f);
        // Each frame draws a share; the twentieth's 0.1 is under 0.8 × 0.25, and thunder 3 plays.
        var shares = Enumerable.Repeat(0u, 19).Append(Share(0.1)).Append(uint.MaxValue).ToArray();
        var random = new ScriptedRandom(shares);
        for (var frame = 1; frame < 20; frame++)
        {
            weather.SkyFrame(true, random);
            Assert.False(weather.Flash);
        }
        weather.SkyFrame(true, random);
        Assert.Equal((true, 3), (weather.Flash, weather.Thunder));
        Assert.Equal(0, random.Remaining);

        // Snow and fog have no storm, and draw nothing; nor does a sky that isn't drawn.
        weather.Force(WeatherKind.Precipitation, Precipitation.Snow);
        weather.SkyFrame(true, new ScriptedRandom());
        weather.Force(WeatherKind.Precipitation, Precipitation.Rain);
        weather.SkyFrame(false, new ScriptedRandom());
        Assert.False(weather.Flash);
    }
}
