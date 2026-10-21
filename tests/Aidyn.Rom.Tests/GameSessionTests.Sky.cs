using Aidyn.Core;
using Aidyn.Core.Rng;
using Aidyn.Core.Sky;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Tests;

/// <summary>The sky as the session steps it, headless on NA 1.0's data: chosen with trek's chunk, stepped with trek, and the stars' stream saved.</summary>
public sealed partial class GameSessionTests
{
    [Fact]
    public void TheSkyIsUpAsTrekStartsAndBlowsItsCloudsWithTrekButNotUnderTheMenu()
    {
        var (session, _) = Start("G14", 40, 40);
        var sky = session.Sky;
        Assert.Same(session.Zone.Sky, sky);
        Assert.NotNull(sky.State.Current);
        Assert.True(sky.Drawn);

        Assert.Equal(SessionStep.Trek, Tick(session));
        Assert.True(sky.Applied);
        var clouds = sky.Clouds!.Positions.ToArray();
        Tick(session);
        Assert.NotEqual(clouds, sky.Clouds.Positions);

        session.OpenMenu();
        clouds = [.. sky.Clouds.Positions];
        var colors = sky.Colors;
        Assert.Equal(SessionStep.Held, Tick(session));
        Assert.Equal(clouds, sky.Clouds.Positions);
        Assert.Equal(colors, sky.Colors);
    }

    [Fact]
    public void TheStarsTwinkleEveryFrameInEveryModeOnTheirOwnStreamAndASaveKeepsIt()
    {
        var (session, _) = Start("G14", 40, 40);
        var game = session.Game;
        game.Clock.SetTimeOfDay(2);
        var stars = Data.Set.TrySingleton<SkyRecord>()!.Stars.Count;
        var expected = new Lcg();
        for (var frame = 0; frame < 4; frame++)
        {
            if (frame == 2)
                session.OpenMenu();
            Tick(session);
            Tick(session);
            byte[] greys = [.. Enumerable.Range(0, stars).Select(_ => (byte)expected.Range(160, 255))];
            Assert.Equal(greys, session.Sky.Objects!.Greys);
        }
        Assert.Equal(expected.Seed, game.Random.Twinkle.Seed);

        var saved = game.Save();
        Assert.Equal(expected.Seed, saved.Random!.Twinkle);
        Assert.Equal(expected.Seed, GameState.Load(Data.Set, saved).Random.Twinkle.Seed);
        // A save from before it was kept starts it as a new game does.
        Assert.Equal(Lcg.BootSeed, GameState.Load(Data.Set, saved with { Random = saved.Random with { Twinkle = null } }).Random.Twinkle.Seed);
    }

    [Fact]
    public void EveryTerrainsSkyAndTheRainSkyDecodeAsGradientsAndI08sNoonIsTheOneTheSkyTestsUse()
    {
        var terrains = Data.Set.All<TerrainRecord>(RecordTypes.Terrain).ToList();
        Assert.NotEmpty(terrains);
        var images = terrains.Select(t => t.Sky).Append(Data.Set.TrySingleton<WeatherRecord>()!.RainSky!.Value).Distinct();
        foreach (var image in images)
        {
            var png = PngImage.Read(Data.Set.FindAsset(image, "png")!);
            Assert.Equal((SkyGradient.Columns, SkyGradient.Rows), (png!.Value.Width, png.Value.Height));
        }

        // The new game's chunk, I08, is terrain 2, under sky 0: its noon column, 6.
        var plains = TerrainRecord.Find(Data.Set, 2)!;
        var gradient = new SkyGradient(PngImage.Read(Data.Set.FindAsset(plains.Sky, "png")!)!.Value.Rgba);
        (int, int, int) Bytes(int row) => ((int)MathF.Round(gradient[6, row].X * 255), (int)MathF.Round(gradient[6, row].Y * 255), (int)MathF.Round(gradient[6, row].Z * 255));
        Assert.Equal((225, 225, 225), Bytes(SkyGradient.TintRow));
        Assert.Equal((188, 205, 217), Bytes(SkyGradient.SkyRow));
        Assert.Equal((79, 109, 153), Bytes(SkyGradient.FogRow));
    }

    [Fact]
    public void WhatTheImporterWritesReadsBack()
    {
        var image = new RgbaImage(7, 3, [.. Enumerable.Range(0, 7 * 3 * 4).Select(i => (byte)(i * 41 + 3))]);
        var read = PngImage.Decode(Png.Encode(image))!.Value;
        Assert.Equal((7, 3), (read.Width, read.Height));
        Assert.Equal(image.Pixels, read.Rgba);
    }
}
