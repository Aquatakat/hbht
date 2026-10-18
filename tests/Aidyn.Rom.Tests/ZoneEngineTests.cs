using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Calendar;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Particles;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The zone engine, headless, on NA 1.0's data: what loads with a chunk and in what order, a stage made from trek's sharing
/// its sounds and weather, what freeing and ending keep, and the game's ticker, which trek's and the cut-scene's cameras keep
/// their place by.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class ZoneEngineTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    private MapSetRecord Set(int id) => Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, id));

    /// <summary>A zone engine on a new game's overworld, loaded about Gwernia (M07), with the camera there.</summary>
    private (ZoneEngine Zone, GameState Game) Gwernia(ParticleHead? particles = null)
    {
        var game = GameState.NewGame(Data.Set);
        var zone = new ZoneEngine(game, new ChunkStreamer(Data.Set, game, new CollisionWorld(), Set(0), new VisibleWorld()),
            particles: particles);
        var chunks = zone.Chunks;
        var at = chunks.Origin(chunks.Find("M07")!) + new Vector3(40, 0, 55);
        zone.Camera = new FollowCamera(at, new Vector2(0, -1));
        chunks.Update(at);
        return (zone, game);
    }

    [Fact]
    public void AModesOwnHearsOfAChunkBeforeItsSoundsAndLights()
    {
        var game = GameState.NewGame(Data.Set);
        var zone = new ZoneEngine(game, new ChunkStreamer(Data.Set, game, new CollisionWorld(), Set(0)));
        var loaded = new List<LoadedMap>();
        var unloaded = new List<LoadedMap>();
        zone.Loaded += (_, map) =>
        {
            Assert.Empty(zone.Lights.In(map));
            loaded.Add(map);
        };
        zone.Unloading += (_, map) =>
        {
            Assert.Equal(map.Record.Objects.OfType<LightObject>().Count(), zone.Lights.In(map).Count());
            unloaded.Add(map);
        };
        var chunks = zone.Chunks;
        // About a chunk with light map objects, which are kept once each has loaded.
        var lit = chunks.MapSet.Chunks.First(c => Data.Set.Get<MapRecord>(c.Map).Objects.OfType<LightObject>().Any());
        chunks.Update(chunks.Origin(lit) + new Vector3(1, 0, 1));

        Assert.Equal(chunks.All.Select(c => c.Map), loaded);
        Assert.Contains(loaded, m => m.Record.Objects.OfType<LightObject>().Any());
        Assert.All(loaded, m => Assert.Equal(m.Record.Objects.OfType<LightObject>().Count(), zone.Lights.In(m).Count()));

        zone.Free();
        Assert.Equal(loaded, unloaded);
        Assert.All(loaded, m => Assert.Empty(zone.Lights.In(m)));
        Assert.Empty(chunks.All);
        Assert.Equal(0, zone.World.Count);
    }

    [Fact]
    public void AStageSharesTheSoundsAndTheWeathersButHasItsOwnChunksLightsAndRain()
    {
        var particles = new ParticleHead(new Lcg());
        var (trek, _) = Gwernia(particles);
        trek.Free();
        var stage = trek.Stage(Set(4));

        Assert.Same(trek.Audio, stage.Audio);
        Assert.Same(trek.Sounds, stage.Sounds);
        Assert.Same(trek.Keys, stage.Keys);
        Assert.Same(trek.WeatherSounds, stage.WeatherSounds);
        Assert.Same(trek.Particles, stage.Particles);
        // Its chunks fill the same collision and drawn surfaces, in a map set of its own.
        Assert.Same(trek.World, stage.World);
        Assert.Same(trek.Visible, stage.Visible);
        Assert.NotSame(trek.Chunks, stage.Chunks);
        Assert.Equal(Set(4).Id, stage.Chunks.MapSet.Id);
        Assert.NotSame(trek.Lights, stage.Lights);
        Assert.NotNull(stage.Precipitation);
        Assert.NotSame(trek.Precipitation, stage.Precipitation);
        Assert.Null(stage.Camera);

        // Loading the stage fills the shared world, not trek's chunks.
        stage.Chunks.Update(stage.Chunks.Origin(stage.Chunks.MapSet.Chunks[0]) + new Vector3(1, 0, 1));
        Assert.NotEmpty(stage.Chunks.All);
        Assert.Empty(trek.Chunks.All);
        Assert.True(trek.World.Count > 0);
    }

    [Fact]
    public void TheSkysFramesCountOnFromTreksWorldToAStage()
    {
        // One tick of trek's weather and one of the stage's make a frame of the sky, as two of trek's do: a storm's sky
        // frame draws on the cosmetic stream, so the streams agree only if the frame is counted across both.
        (ZoneEngine Zone, GameState Game) Stormy()
        {
            var (zone, game) = Gwernia();
            game.Weather!.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.9f);
            return (zone, game);
        }
        var (alone, aloneGame) = Stormy();
        alone.StepWeather(true, null);
        alone.StepWeather(true, null);

        var (trek, game) = Stormy();
        trek.StepWeather(true, null);
        trek.Free();
        trek.Stage(Set(0)).StepWeather(true, null);

        Assert.NotEqual(GameState.NewGame(Data.Set).Random.Cosmetic.Seed, aloneGame.Random.Cosmetic.Seed);
        Assert.Equal(aloneGame.Random.Cosmetic.Seed, game.Random.Cosmetic.Seed);
    }

    [Fact]
    public void FreeingKeepsTheDynamicLightsAndWhatTheWeatherDraws()
    {
        var particles = new ParticleHead(new Lcg());
        var (zone, game) = Gwernia(particles);
        var weather = game.Weather!;
        weather.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.5f);
        zone.StepWeather(true, zone.Precipitation);
        Assert.True(weather.Emitting);
        zone.Lights.Dynamic.Explosion(zone.Camera!.Aim);
        var lit = zone.Lights.Dynamic.Count;
        Assert.True(lit > 0);

        zone.Free();
        Assert.Empty(zone.Chunks.All);
        Assert.Equal(0, zone.World.Count);
        Assert.False(weather.Emitting);
        Assert.Null(zone.Precipitation!.Drops);
        Assert.Equal(lit, zone.Lights.Dynamic.Count);
        Assert.Equal(Precipitation.Rain, weather.Falling);
        Assert.Equal(0.5f, weather.FallIntensity);
    }

    [Fact]
    public void AStagesEndEmptiesTheParticlePoolAndFreesItsWorld()
    {
        var particles = new ParticleHead(new Lcg());
        var (trek, game) = Gwernia(particles);
        game.Weather!.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.5f);
        trek.Free();
        var stage = trek.Stage(Set(0));
        stage.Camera = new FollowCamera(Vector3.Zero, Vector2.UnitX);
        stage.Chunks.Update(stage.Chunks.Origin(stage.Chunks.Find("M07")!) + new Vector3(40, 0, 55));
        stage.StepWeather(true, stage.Precipitation);
        particles.Step();
        Assert.True(particles.EmittersInUse > 0);

        stage.End();
        Assert.Equal(0, particles.EmittersInUse);
        Assert.False(game.Weather.Emitting);
        Assert.Empty(stage.Chunks.All);
        Assert.Equal(0, stage.World.Count);
    }

    [Fact]
    public void TheTickerCountsEveryTickWithTheTimePlayedButIsntSaved()
    {
        var game = GameState.NewGame(Data.Set);
        var played = game.PlayTime;
        for (var i = 0; i < 5; i++)
            game.CountTick();
        Assert.Equal(played + 5, game.PlayTime);
        Assert.Equal(5, game.Ticker);

        var loaded = GameState.Load(Data.Set, game.Save());
        Assert.Equal(game.PlayTime, loaded.PlayTime);
        Assert.Equal(0, loaded.Ticker);
    }

    /// <summary>Walks and runs move a body along; every state lasts a second.</summary>
    private sealed class Walking : IActorMotion
    {
        public static readonly Walking Instance = new();

        public int StateLength => 60;

        public float Tick(int request) => request switch { PlayerMotion.Walk => 0.03f, PlayerMotion.Run => 0.06f, _ => 0 };
    }

    [Fact]
    public void ACutScenesCamerasMoveTheSameWhateverTheTickerReads()
    {
        // The script's cameras keep their place by the game's ticker, which the runtime counts once a tick before the
        // cut-scene steps, and which a headless run leaves alone: both play every move alike, as a move is stepped every tick.
        var played = 0;
        foreach (var (_, map, dialogue) in Data.Of<DialogueObject>().Where(d => d.Object.Stage is not null
            && Data.Set.TryGet<DialogueRecord>(d.Object.Script) is { Mode: DialogueMode.Cutscene }).Take(40))
        {
            var script = Data.Set.Get<DialogueRecord>(dialogue.Script);
            var start = new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index));
            var stage = CutSceneStage.For(Data.Set, dialogue.Stage!, new CollisionWorld())!;
            var streamer = new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), stage.World, stage.Set);
            streamer.Update(stage.Origin + new Vector3(stage.Set.CellSize / 2, 0, stage.Set.CellSize / 2));
            var still = new CutScene(GameState.NewGame(Data.Set), script, start, stage);
            var counted = GameState.NewGame(Data.Set);
            var ticking = new CutScene(counted, script, start, stage);
            still.Start();
            ticking.Start();
            for (var tick = 0; tick < 3000 && !still.Over; tick++)
            {
                foreach (var scene in new[] { still, ticking })
                {
                    if (scene.Accepts && scene.Page is { } page)
                    {
                        if (page.Choices.Count > 0 && scene.Runner.Wait != ScriptWait.Player)
                            scene.Press(() => scene.Runner.Choose(0));
                        else
                            scene.Press(scene.Runner.Continue);
                    }
                }
                counted.CountTick();
                still.Tick(_ => Walking.Instance);
                ticking.Tick(_ => Walking.Instance);
                Assert.Equal(still.Camera.Position, ticking.Camera.Position);
                Assert.Equal(still.Camera.Aim, ticking.Camera.Aim);
            }
            Assert.Equal(still.Over, ticking.Over);
            played++;
        }
        Assert.Equal(40, played);
    }
}
