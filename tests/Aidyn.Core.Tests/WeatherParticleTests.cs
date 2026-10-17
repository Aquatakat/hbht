using System.Numerics;
using Aidyn.Core.Calendar;
using Aidyn.Core.Particles;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class WeatherParticleTests
{
    private static readonly RecordId Streak = RecordId.Hex(RecordTypes.Texture, 2), Ripple = RecordId.Hex(RecordTypes.Texture, 12),
        Flake = RecordId.Hex(RecordTypes.Texture, 3);

    /// <summary>The base game's rules, with rain and snow as the importer writes them.</summary>
    private static readonly WeatherRecord Rules = WeatherTests.Rules with
    {
        Rain = new PrecipitationLook(Streak, Ripple, new WeatherColor(0.546875f, 0.7421875f, 0.8203125f, 0.8f), true, 0.075, 0, 6, -4, 240, 0, 2),
        Snow = new PrecipitationLook(Flake, Flake, new WeatherColor(1, 1, 1, 1), false, 0.03, 0.02, 6, -7, 360, -60, 0),
    };

    /// <summary>Rain or snow falling at an intensity, the wind still, over level ground with the camera aiming at the origin, looking along +Z.</summary>
    private sealed class Scene
    {
        public readonly ParticleHead Head = new(new Lcg(), capacity: WeatherParticles.PoolCapacity);
        public readonly Weather Weather = new(Rules) { Wind = Vector3.Zero };
        public readonly CollisionWorld World = new();
        public readonly WeatherParticles Fall;
        public Vector3 Aim = new(0, 1, 0);
        public Vector2 Look = Vector2.UnitY;
        public bool SkyDrawn = true;
        public object? Part = "ground";

        public Scene(Precipitation precipitation = Precipitation.Rain, float intensity = 0.5f)
        {
            World.Add("ground", Worlds.Floor(0, -100, 100, -100, 100), Vector3.Zero);
            Fall = new WeatherParticles(Head, Rules, new Lcg(7), () => Aim, () => Look, () => World, () => Part);
            Weather.Force(WeatherKind.Precipitation, precipitation, intensity);
            Weather.SkyChange += rain => RainSkyAsked += rain ? 1 : 0;
        }

        /// <summary>How many times the weather has asked for the rain sky.</summary>
        public int RainSkyAsked;

        /// <summary>Ticks of trek: the weather with its emitters, then the particles.</summary>
        public void Run(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                Weather.Draw(SkyDrawn, emitters: Fall);
                Head.Step();
            }
        }

        public IEnumerable<ParticleSprite> Drawn(Emitter? e) => e is null ? [] : Head.Sprites.Where(s => s.Emitter == e.Slot);
    }

    [Fact]
    public void RainMakesTwoEmittersAndSteersThemByHowHardItFalls()
    {
        var scene = new Scene(intensity: 0.5f);
        scene.Weather.Wind = new Vector3(0.01f, -0.001f, 0.02f);
        scene.Run(1);
        var drops = scene.Fall.Drops!;
        var splashes = scene.Fall.Splashes!;
        Assert.Equal(Streak, drops.Texture);
        Assert.Equal(Ripple, splashes.Texture);
        // A drop every (short)(6 − 4 × 0.5) ticks, splashes 2 sooner, both kept alive at 300 while it falls.
        Assert.Equal((4, 2), (drops.Interval, splashes.Interval));
        Assert.Equal((WeatherParticles.KeptLife, WeatherParticles.KeptLife), (drops.Life, splashes.Life));
        // The drops fall by the wind and 0.075 a tick more; the splashes shrink by 1/120 a tick.
        Assert.Equal(new Vector4(0.01f, (float)(-0.001f - 0.075), 0.02f, 0), scene.Head[drops.Head].Vector);
        Assert.True(scene.Head[drops.Head].Flags.HasFlag(ParticleFlags.AddPosition));
        Assert.Equal(new Vector4(-1f / 120, -1f / 120, -1f / 120, 0), scene.Head[splashes.Head].Vector);

        // At the heaviest, a drop every 2 ticks and splashes every frame.
        scene.Weather.Force(WeatherKind.Precipitation, Precipitation.Rain, 1);
        scene.Run(1);
        Assert.Equal((2, 0), (drops.Interval, splashes.Interval));
    }

    [Fact]
    public void DropsFallFromADiscAheadOfTheAimFadingToAQuarter()
    {
        var scene = new Scene(intensity: 0.5f);
        scene.Run(4);
        // The first drop, drawn from its second frame: 5 ahead of the aim along the look and 9 − I above it, within 20.
        var drop = Assert.Single(scene.Drawn(scene.Fall.Drops));
        Assert.InRange(new Vector2(drop.Position.X, drop.Position.Z - 5).Length(), 0.01f, 20);
        // Two frames' fall of 0.075 a tick from 1 + 8.5.
        Assert.Equal(1 + 8.5f - 4 * 0.075f, drop.Position.Y, 4);
        Assert.Equal(new Vector2(WeatherParticles.DropScale * ParticleHead.SquareSide), drop.Size);
        Assert.Null(drop.Orientation);
        // Its colour, (139, 189, 209, 204), less 0.75 of it over 240 ticks: 4 ticks' worth.
        var faded = new Vector4(0.546875f, 0.7421875f, 0.8203125f, 0.8f) * (1 - 4 * 0.75f / 240);
        Assert.Equal(new Rgba((byte)(faded.X * 255), (byte)(faded.Y * 255), (byte)(faded.Z * 255), (byte)(faded.W * 255)), drop.Color);
    }

    [Fact]
    public void DropsLandAsSplashesLaidFlatOnTheGround()
    {
        var scene = new Scene(intensity: 0.5f);
        scene.Run(600);
        var splashes = scene.Drawn(scene.Fall.Splashes).ToList();
        Assert.NotEmpty(splashes);
        foreach (var s in splashes)
        {
            Assert.Equal(WeatherParticles.SplashLift, s.Position.Y, 4);
            Assert.InRange(s.Size.X, 0, WeatherParticles.DropScale * WeatherParticles.SplashGrowth * ParticleHead.SquareSide + 1e-5f);
            // Facing along the ground's normal, so +Z turns to straight down.
            var facing = Vector3.Transform(Vector3.UnitZ, s.Orientation!.Value);
            Assert.Equal(-1, facing.Y, 3);
        }
        // No drop is drawn below the ground: each becomes a splash once past where it would land.
        Assert.All(scene.Drawn(scene.Fall.Drops), d => Assert.True(d.Position.Y > -1));
    }

    [Fact]
    public void DropsLandOnEveryLoadedChunkOrOneAsTheOriginalsDoAndPassSeeThroughTriangles()
    {
        // Ground of another loaded chunk: trek's drops, which test every chunk, land on it, and rain splashes on it.
        var trek = new Scene(intensity: 0.5f) { Part = null };
        trek.World.Remove("ground");
        trek.World.Add("next", Worlds.Floor(0, -100, 100, -100, 100), Vector3.Zero);
        trek.Run(600);
        Assert.NotEmpty(trek.Drawn(trek.Fall.Splashes));
        Assert.True(trek.Fall.DropSplashes > 0 && trek.Fall.OwnSplashes > 0);
        Assert.All(trek.Drawn(trek.Fall.Drops), d => Assert.True(d.Position.Y > -1));

        // Testing one chunk's collision alone, as the original tests the player's: the drops fall through the other's
        // ground without landing, and rain's own splashes find none.
        var scene = new Scene(intensity: 0.5f) { Part = "elsewhere" };
        scene.Run(600);
        Assert.Empty(scene.Drawn(scene.Fall.Splashes));
        Assert.Contains(scene.Drawn(scene.Fall.Drops), d => d.Position.Y < -1);

        // A see-through floor: drops pass it, but rain's own splashes land on it.
        var glass = new Scene(intensity: 0.5f);
        glass.World.Add("ground", Worlds.Floor(0, -100, 100, -100, 100).Select(t => t with { Flags = CollisionFlags.SeeThrough }), Vector3.Zero);
        glass.Run(600);
        Assert.Contains(glass.Drawn(glass.Fall.Drops), d => d.Position.Y < -1);
        Assert.NotEmpty(glass.Drawn(glass.Fall.Splashes));
        Assert.All(glass.Drawn(glass.Fall.Splashes), s => Assert.Equal(WeatherParticles.SplashLift, s.Position.Y, 4));
    }

    [Fact]
    public void ASplashLandsOnTheFirstTriangleTheSweepFindsNotTheNearest()
    {
        // The collision's grid order, not the height: with the ground listed before a floor 3 above it, rain's own
        // splashes land on the ground under it, as the drops do.
        var scene = new Scene(intensity: 0.5f);
        scene.World.Add("ground", [.. Worlds.Floor(0, -100, 100, -100, 100), .. Worlds.Floor(3, -100, 100, -100, 100)], Vector3.Zero);
        scene.Run(600);
        Assert.NotEmpty(scene.Drawn(scene.Fall.Splashes));
        Assert.All(scene.Drawn(scene.Fall.Splashes), s => Assert.Equal(WeatherParticles.SplashLift, s.Position.Y, 4));
    }

    [Fact]
    public void SnowFallsSlowerLivesLongerAndMakesNoSplashesOfItsOwn()
    {
        var scene = new Scene(Precipitation.Snow, 0.75f);
        scene.Run(1);
        var drops = scene.Fall.Drops!;
        var splashes = scene.Fall.Splashes!;
        Assert.Equal((Flake, Flake), (drops.Texture, splashes.Texture));
        // (short)(6 − 7 × 0.75) = 0: a flake every frame; no ambient splashes.
        Assert.Equal(0, drops.Interval);
        Assert.Null(splashes.Spawn);
        Assert.Equal((float)(0 - (0.75 * 0.02 + 0.03)), scene.Head[drops.Head].Vector.Y);
        scene.Run(599);
        var flakes = scene.Drawn(drops).ToList();
        // White, fading toward a quarter.
        Assert.All(flakes, f => Assert.True(f.Color.R == f.Color.G && f.Color.G == f.Color.B && f.Color.B == f.Color.A && f.Color.R >= 63));
        // A flake every frame of the console's 20 fps, of which the ones still falling: from 9.25 above the ground at 0.045
        // a tick, 205 ticks.
        Assert.InRange(flakes.Count, 66, 70);
        Assert.NotEmpty(scene.Drawn(splashes));
    }

    [Fact]
    public void NothingFallsWhereTheSkyIsntDrawnAndComingInFreesThem()
    {
        var scene = new Scene { SkyDrawn = false };
        scene.Run(100);
        Assert.Null(scene.Fall.Drops);
        Assert.Equal(0, scene.Head.EmittersInUse);
        Assert.Equal(0, scene.RainSkyAsked);
        Assert.False(scene.Weather.Emitting || scene.Weather.RainLoop);

        // Out under the sky: one decision makes the pair and asks for the rain sky, once.
        scene.SkyDrawn = true;
        scene.Run(100);
        Assert.Equal(2, scene.Head.EmittersInUse);
        Assert.True(scene.Weather.Emitting && scene.Weather.RainLoop);
        Assert.Equal(1, scene.RainSkyAsked);

        // Back in: freed at once, with the loop.
        scene.SkyDrawn = false;
        scene.Run(1);
        Assert.Equal((0, 0), (scene.Head.EmittersInUse, scene.Head.ParticlesInUse));
        Assert.False(scene.Weather.Emitting || scene.Weather.RainLoop);
        Assert.Null(scene.Fall.Drops);
    }

    [Fact]
    public void WithoutRoomForThePairTheRainSkyIsAskedForEveryTickUntilThereIs()
    {
        var scene = new Scene();
        // Every emitter taken.
        var taken = Enumerable.Range(0, ParticleHead.MaxEmitters).Select(_ => scene.Head.AllocEmitter(1000, null, 0)!).ToList();
        scene.Run(3);
        Assert.False(scene.Weather.Emitting);
        Assert.Null(scene.Fall.Drops);
        Assert.Equal(3, scene.RainSkyAsked);
        scene.Head.Free(taken[0]);
        scene.Head.Free(taken[1]);
        scene.Run(3);
        Assert.True(scene.Weather.Emitting);
        Assert.NotNull(scene.Fall.Drops);
        Assert.Equal(4, scene.RainSkyAsked);
    }

    [Fact]
    public void ACutSceneWithoutParticlesKeepsTheSameDecision()
    {
        // No emitters to draw: the weather still decides the pair is made, once, as the sky and the loop follow it.
        var weather = new Weather(Rules);
        weather.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.5f);
        var asked = 0;
        weather.SkyChange += rain => asked += rain ? 1 : 0;
        for (var i = 0; i < 100; i++)
            weather.Draw(true);
        Assert.True(weather.Emitting && weather.RainLoop);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void AChangeOfWeatherForgetsTheEmittersWhichRunOut()
    {
        var scene = new Scene(intensity: 0.5f);
        scene.Run(100);
        var old = scene.Fall.Drops!;
        scene.Weather.Precipitation = Precipitation.Snow;
        scene.Run(1);
        Assert.Null(scene.Fall.Drops);
        Assert.True(old.InUse);
        // The rain dies away over 150 ticks, then snow starts in a new pair while the old runs out its 300.
        scene.Run(160);
        Assert.Equal(Flake, scene.Fall.Drops!.Texture);
        Assert.Equal(4, scene.Head.EmittersInUse);
        scene.Run(600);
        Assert.Equal(2, scene.Head.EmittersInUse);
    }

    [Fact]
    public void ATeleportForgetsTheEmittersAndKillsThemWithTheRest()
    {
        var scene = new Scene();
        scene.Run(100);
        var loops = scene.Weather.RainLoopStarts;
        Assert.Equal(1, scene.RainSkyAsked);
        scene.Weather.Forget(scene.Fall);
        scene.Head.KillActive();
        Assert.False(scene.Weather.Emitting);
        Assert.Null(scene.Fall.Drops);
        Assert.Equal((0, 0), (scene.Head.EmittersInUse, scene.Head.ParticlesInUse));
        // The next frame makes a new pair about the player, and asks for the rain sky again; the loop plays on.
        scene.Run(1);
        Assert.True(scene.Weather.Emitting);
        Assert.NotNull(scene.Fall.Drops);
        Assert.Equal(2, scene.Head.EmittersInUse);
        Assert.Equal(2, scene.RainSkyAsked);
        Assert.True(scene.Weather.RainLoop);
        Assert.Equal(loops, scene.Weather.RainLoopStarts);
    }

    [Fact]
    public void TreksWorldBuiltAgainCarriesTheRainOnWithANewPairAboutTheView()
    {
        var scene = new Scene(intensity: 0.4f);
        scene.Weather.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.8f);
        scene.Weather.FallIntensity = 0.4f;
        scene.Run(30);
        var (falling, intensity, fog, loops) = (scene.Weather.Falling, scene.Weather.FallIntensity, scene.Weather.FogTime, scene.Weather.RainLoopStarts);
        Assert.True(scene.Weather.SkyOverridden && scene.Weather.RainLoop);
        // Trek's world goes, for a cut-scene's stage, a backdrop or the help menu (TrekLoop.Free): the weather forgets
        // the pair, and the zone engine's end frees the pool.
        scene.Weather.Forget(scene.Fall);
        scene.Fall.Clear();
        scene.Head.Clear();
        Assert.False(scene.Weather.Emitting);
        // The rain sky is still wanted, so the stage chooses it at once.
        Assert.True(scene.Weather.WantsRainSky && scene.Weather.SkyOverridden);
        scene.Aim = new Vector3(500, 1, 500);
        scene.Run(1);
        // What falls goes on rising from where it was, the fog stays, the loop plays on unbroken, and a pair is made
        // about the new view, whose ask for the rain sky changes nothing, as it's up.
        Assert.Equal((falling, intensity + 1f / 300, fog), (scene.Weather.Falling, scene.Weather.FallIntensity, scene.Weather.FogTime));
        Assert.Equal((true, loops), (scene.Weather.RainLoop, scene.Weather.RainLoopStarts));
        Assert.True(scene.Weather.Emitting && scene.Weather.SkyOverridden);
        Assert.NotNull(scene.Fall.Drops);
    }

    [Fact]
    public void DensityMultipliesTheDropsAndThePool()
    {
        var original = new Scene(intensity: 0.5f);
        original.Run(200);
        var denser = new Scene(intensity: 0.5f);
        denser.Fall.Density = 3;
        denser.Run(200);
        Assert.Equal(ParticleHead.DefaultMaxParticles, original.Head.MaxParticles);
        Assert.Equal(3 * ParticleHead.DefaultMaxParticles, denser.Head.MaxParticles);
        Assert.Equal(3 * original.Drawn(original.Fall.Drops).Count(), denser.Drawn(denser.Fall.Drops).Count());

        // At 1.5, every other spawn makes two.
        var half = new Scene(intensity: 0.5f);
        half.Fall.Density = 1.5f;
        half.Run(200);
        Assert.InRange(half.Drawn(half.Fall.Drops).Count(), 1.5 * original.Drawn(original.Fall.Drops).Count() - 1, 1.5 * original.Drawn(original.Fall.Drops).Count() + 1);
    }

    [Theory]
    // (short)(6 − 4 I) ticks between drops and 2 fewer between rain's own splashes, counted down by frames of 3 ticks:
    // a spawn, then frames while the counter is above 0. At 0.5, 4 and 2: a drop every 3 frames, a splash every 2.
    [InlineData(0.5f, 600 / 9, 600 / 6)]
    // At 0.95, 2 and 0: a drop every 2 frames, a splash every frame.
    [InlineData(0.95f, 600 / 6, 600 / 3)]
    // At 0.2, 5 and 3: a drop every 3 frames, a splash every 2.
    [InlineData(0.2f, 600 / 9, 600 / 6)]
    public void RainSpawnsOnceAFrameOfTheConsoles20Fps(float intensity, int drops, int own)
    {
        var scene = new Scene(intensity: intensity);
        scene.Run(600);
        Assert.InRange(scene.Fall.DropsMade, drops, drops + 1);
        Assert.InRange(scene.Fall.OwnSplashes, own, own + 1);
        // Every drop over the ground lands as a splash, but those still falling.
        Assert.InRange(scene.Fall.DropSplashes, drops - 25, drops);
    }

    [Theory]
    [InlineData(1.5f)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void DensityKeepsItsDropsButHoldsTheSplashesToOneAndAHalfTimesTheOriginals(float density)
    {
        var original = new Scene(intensity: 0.5f);
        original.Run(1200);
        var denser = new Scene(intensity: 0.5f);
        denser.Fall.Density = density;
        denser.Run(1200);
        Assert.InRange(denser.Fall.DropsMade, density * original.Fall.DropsMade - 1, density * original.Fall.DropsMade + 1);
        var cap = WeatherParticles.SplashCap;
        Assert.InRange(denser.Fall.OwnSplashes, cap * original.Fall.OwnSplashes - 1, cap * original.Fall.OwnSplashes + 1);
        Assert.InRange(denser.Fall.DropSplashes, cap * original.Fall.DropSplashes - 4, cap * original.Fall.DropSplashes + 4);
        // So the ground holds about 1.5 times the original's splashes.
        var splashes = denser.Drawn(denser.Fall.Splashes).Count();
        var originals = original.Drawn(original.Fall.Splashes).Count();
        Assert.InRange(splashes, cap * originals - 6, cap * originals + 6);
    }

    [Fact]
    public void TheOriginalsPoolCapsTheRainAtTheOriginalDensity()
    {
        var scene = new Scene(Precipitation.Snow, 1);
        scene.Run(600);
        Assert.True(scene.Head.ParticlesInUse <= ParticleHead.DefaultMaxParticles);
    }
}
