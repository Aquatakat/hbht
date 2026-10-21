using System.Numerics;
using System.Text.Json;
using Aidyn.Core.Calendar;
using Aidyn.Core.Rng;
using Aidyn.Core.Sky;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>The sky stepped headless, as the session steps it: from plugins with a sky record, terrains and their gradients.</summary>
public sealed class SkyDirectorTests : IDisposable
{
    private static RecordId Image(int n) => RecordId.Hex(RecordTypes.Image, n);

    private static readonly Rgba White = new(250, 250, 250, 250);

    /// <summary>Three stars, the sun, the moon, two clouds and a flare piece.</summary>
    private static readonly SkyRecord Record = new(
        RecordId.Decimal(RecordTypes.Sky, 0),
        new SkyBody(Image(0x1A2), 1, 0, 90, White),
        new SkyBody(Image(0x198), 1, 0, 0, White),
        new MoonImages(Image(0x194), Image(0x379F), Image(0x195), Image(0x37A0), Image(0x196), Image(0x198)),
        [new SkyBody(Image(0x1A1), 0.19f, 10, 90, White), new SkyBody(Image(0x1A1), 0.19f, 20, 180, White), new SkyBody(Image(0x1A1), 0.19f, 30, 270, White)],
        [Image(0x18F)],
        [new Cloud(0, 0, -530, 255, 0.2f), new Cloud(0, 700, 300, 255, 0.4f)],
        [Image(0x192)],
        [new FlarePiece(0, 1, 5.02f, new Rgba(250, 250, 250, 50))]);

    /// <summary>The gradients: a day's, a dark one and the rain's, each one colour down every column but for the sky row.</summary>
    private const int Day = 1, Dark = 2, Rain = 3;

    private static readonly ChunkEntry Plains = Chunk(0, 0, terrain: 0), Hills = Chunk(0, 1, terrain: 1), Cave = Chunk(0, 2, terrain: 2),
        MorePlains = Chunk(1, 0, terrain: 0);

    private static ChunkEntry Chunk(int row, int column, int terrain) => new(row, column, null, null, RecordId.Hex(RecordTypes.Map, 0), terrain);

    private readonly TempPlugins _temp = new();
    private readonly PluginSet _plugins;

    public SkyDirectorTests()
    {
        TerrainRecord Terrain(int n, int sky, bool drawn) => new(RecordId.Decimal(RecordTypes.Terrain, n), Image(sky), drawn);
        _plugins = _temp.Aidyn(
            ("sky.json", JsonSerializer.Serialize(new[] { Record }, PluginJson.Options)),
            ("terrain.json", JsonSerializer.Serialize(new[] { Terrain(0, Day, true), Terrain(1, Dark, true), Terrain(2, Dark, false) }, PluginJson.Options)));
        var images = Path.Combine(_temp.Root, "aidyn", "assets", "aidyn", "image");
        Directory.CreateDirectory(images);
        foreach (var (n, level) in new[] { (Day, 200), (Dark, 40), (Rain, 120) })
            File.WriteAllBytes(Path.Combine(images, Image(n).Key + ".png"), Gradient((byte)level));
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>A gradient PNG of a grey that grows redder through the day, with its sky row a little bluer, so each is told apart by its colours.</summary>
    private static byte[] Gradient(byte level)
    {
        var rgba = new byte[SkyGradient.Columns * SkyGradient.Rows * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var blue = i / 4 / SkyGradient.Columns == SkyGradient.SkyRow ? (byte)(level + 10) : level;
            var red = (byte)(level + i / 4 % SkyGradient.Columns * 3);
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (red, level, blue, 255);
        }
        return TestPng.Encode(SkyGradient.Columns, SkyGradient.Rows, 6, rgba, filter: y => y % 5);
    }

    private static float Level(SkyGradient? gradient) => gradient![0, SkyGradient.TintRow].X * 255;

    private static GameClock At(float hours)
    {
        var clock = GameClock.NewGame();
        clock.SetTimeOfDay(hours);
        return clock;
    }

    private SkyDirector Director(GameClock? clock = null, Weather? weather = null, IRandom? twinkle = null) =>
        new(_plugins, clock ?? At(12), weather, twinkle ?? new Lcg());

    private static Weather Weather() => new(WeatherTests.Rules with { RainSky = Image(Rain) });

    [Fact]
    public void AChunkChoosesItsTerrainsSkyAtOnceAndTheNextTerrainsOverSixHundredTicks()
    {
        var sky = Director();
        Assert.Null(sky.State.Current);
        sky.Enter(Plains);
        Assert.Equal(200, Level(sky.State.Current));
        Assert.True(sky.Drawn);

        sky.Enter(Hills);
        Assert.Equal(40, Level(sky.State.Next));
        for (var i = 0; i < SkyState.ChangeTicks - 1; i++)
            sky.Step();
        Assert.Equal(200, Level(sky.State.Current));
        // A 600th added up 600 times in single precision falls just short of 1, so the change lands a tick later.
        sky.Step();
        Assert.Equal(200, Level(sky.State.Current));
        sky.Step();
        Assert.Equal(40, Level(sky.State.Current));

        // Under a sky that isn't drawn the change is at once; and once the world has gone, the next chunk chooses at once too.
        sky.Enter(Cave);
        Assert.False(sky.Drawn);
        sky.Enter(Plains);
        Assert.Equal(200, Level(sky.State.Current));
        sky.Enter(Hills);
        sky.ClearSky();
        sky.Enter(MorePlains);
        Assert.False(sky.State.Changing);
        Assert.Equal(200, Level(sky.State.Current));
    }

    [Fact]
    public void TheWeatherBringsTheRainSkyOverSixHundredTicksAndHoldsItAgainstChunks()
    {
        var weather = Weather();
        var sky = Director(weather: weather);
        sky.Enter(Plains);
        weather.Force(WeatherKind.Precipitation, Precipitation.Rain);
        weather.Draw(skyDrawn: true);
        Assert.Equal(120, Level(sky.State.Next));

        sky.Enter(Hills);
        Assert.Equal(120, Level(sky.State.Next));
        // Chosen again at once, the rain sky is up at once.
        sky.ChooseAgain();
        Assert.Equal(120, Level(sky.State.Current));
    }

    [Fact]
    public void TheCloudsBlowOnTheWeathersWindTickForTick()
    {
        var weather = Weather();
        var sky = Director(weather: weather);
        sky.Enter(Plains);
        var alone = new Clouds(Record);
        foreach (var wind in new[] { Calendar.Weather.NewGameWind, new Vector3(0.2f, 0, -0.1f), new Vector3(-1, 0, 0.5f) })
        {
            weather.Wind = wind;
            alone.Wind = wind;
            for (var i = 0; i < 400; i++)
            {
                sky.Step();
                alone.Step();
                Assert.Equal(alone.Positions, sky.Clouds!.Positions);
            }
        }
        Assert.NotEqual(Record.Clouds.Select(c => new Vector2(c.X, c.Z)), sky.Clouds!.Positions);
    }

    [Fact]
    public void TheStarsTwinkleOnTheirOwnStreamByNightUnderAnOutdoorSky()
    {
        var clock = At(3);
        var twinkle = new Lcg();
        var sky = Director(clock, twinkle: twinkle);
        // Nothing is up yet.
        sky.StepTwinkle();
        Assert.Equal(Lcg.BootSeed, twinkle.Seed);

        sky.Enter(Plains);
        var expected = new Lcg();
        for (var tick = 0; tick < 3; tick++)
        {
            sky.StepTwinkle();
            byte[] greys = [.. Record.Stars.Select(_ => (byte)expected.Range(160, 255))];
            Assert.Equal(greys, sky.Objects!.Greys);
        }
        Assert.Equal(expected.Seed, twinkle.Seed);

        // Under a sky that isn't drawn, and by day, they don't.
        sky.Enter(Cave);
        sky.StepTwinkle();
        sky.Enter(Plains);
        clock.SetTimeOfDay(12);
        sky.StepTwinkle();
        Assert.Equal(expected.Seed, twinkle.Seed);
    }

    [Fact]
    public void TheLensFlareFadesInWhereTheSunWasDrawnWhileTheSkyShowsThere()
    {
        var sky = Director(At(6));
        sky.Enter(Plains);
        sky.Apply();
        var plain = sky.Colors.Tint;
        // Drawn looking at the sun on the horizon, its middle lands near the screen's.
        sky.Objects!.Draw([], sky.Clock.Time, Vector3.UnitX, sky.Colors.Sun, _ => new Vector2(64, 64), shown: sky.TimeOfDay);
        var asked = new List<Vector2>();
        sky.SkyShows = at =>
        {
            asked.Add(at);
            return true;
        };
        for (var i = 0; i < 10; i++)
            sky.Step();

        Assert.All(asked, at => Assert.Equal(sky.Objects.SunMiddle, at));
        Assert.Equal(50, sky.Flare!.Fade);
        Assert.True(sky.LensFlareValue > 0);
        Assert.True(sky.Colors.Tint.X < plain.X);

        // Hidden, it fades out again.
        sky.SkyShows = _ => false;
        for (var i = 0; i < 10; i++)
            sky.Step();
        Assert.Equal(0, sky.Flare.Fade);
        // Headless, nothing says the sky shows: it never fades in.
        var headless = Director(At(6));
        headless.Enter(Plains);
        headless.Step();
        Assert.Equal(0, headless.Flare!.Fade);
    }

    [Fact]
    public void TheColoursAreWorkedOutAsTheyreAppliedAndKeptUntilTheNext()
    {
        var clock = At(12);
        var weather = Weather();
        weather.Force(WeatherKind.Fog, fog: 0.5f);
        var sky = Director(clock, weather);
        sky.Apply();
        Assert.False(sky.Applied);

        sky.Enter(Plains);
        sky.Apply();
        Assert.True(sky.Applied);
        Assert.Equal(SkyColors.At(sky.State.Current!, null, 0, sky.TimeOfDay), sky.Colors);
        Assert.Equal(SkyColors.FogStart(0.5f), sky.SceneryFogStart);
        Assert.Equal(SkyLight.SunDirection(0.5f), sky.SunDirection);
        Assert.Equal(sky.Colors.Sun, sky.SunColor);

        // The clock moves on, but the colours are what they were until the next tick of the sky.
        var colors = sky.Colors;
        clock.SetTimeOfDay(20);
        Assert.Equal(colors, sky.Colors);
        sky.Step();
        Assert.NotEqual(colors, sky.Colors);

        // A cut-scene's scenery fog ignores the weather's; under a sky that isn't drawn, the sun is black; a cast dims it.
        sky.CutScene = true;
        sky.Enter(Cave);
        sky.Dim = 0.5f;
        sky.Apply();
        Assert.Equal(SkyColors.FogStart(0), sky.SceneryFogStart);
        Assert.Equal(Vector3.Zero, sky.SunColor);
        Assert.Equal(SkyColors.At(sky.State.Current!, null, 0, sky.TimeOfDay, dim: 0.5f), sky.Colors);
    }

    [Fact]
    public void TheMoonLightsWhileItsUp()
    {
        Assert.NotNull(Director(At(9)).Moon);
        Assert.NotNull(Director(At(21)).Moon);
        Assert.Null(Director(At(15)).Moon);
        Assert.Null(Director(At(3)).Moon);
    }

    [Fact]
    public void ATerrainWithoutASkyIsToldAndChoosesNothing()
    {
        var sky = Director();
        var told = new List<string>();
        sky.Warn = told.Add;
        sky.Enter(Chunk(5, 5, terrain: 9));
        Assert.Null(sky.State.Current);
        Assert.Equal(["No sky for terrain 9."], told);
    }
}
