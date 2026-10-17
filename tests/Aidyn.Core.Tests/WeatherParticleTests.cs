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
            Fall = new WeatherParticles(Head, new Lcg(7), () => Aim, () => World, () => Part);
            Weather.Force(WeatherKind.Precipitation, precipitation, intensity);
        }

        /// <summary>Ticks of trek: the emitters, then the weather, then the particles.</summary>
        public void Run(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                Fall.Frame(Weather, SkyDrawn, Look);
                Weather.Draw(SkyDrawn);
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
    public void DropsOnlyTestThePlayersChunkAndPassSeeThroughTriangles()
    {
        // Ground of another chunk: the drops fall through it without landing, and rain's own splashes find none.
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
        // A flake every frame, of which the ones still falling: from 9.25 above the ground at 0.045 a tick, 205 ticks.
        Assert.InRange(flakes.Count, 100, 105);
        Assert.NotEmpty(scene.Drawn(splashes));
    }

    [Fact]
    public void NothingFallsWhereTheSkyIsntDrawnAndComingInFreesThem()
    {
        var scene = new Scene { SkyDrawn = false };
        scene.Run(100);
        Assert.Null(scene.Fall.Drops);
        Assert.Equal(0, scene.Head.EmittersInUse);

        scene.SkyDrawn = true;
        scene.Run(100);
        Assert.Equal(2, scene.Head.EmittersInUse);
        scene.SkyDrawn = false;
        scene.Run(1);
        Assert.Equal((0, 0), (scene.Head.EmittersInUse, scene.Head.ParticlesInUse));
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
        scene.Fall.Forget();
        scene.Head.KillActive();
        Assert.Equal((0, 0), (scene.Head.EmittersInUse, scene.Head.ParticlesInUse));
        // The next frame makes a new pair about the player.
        scene.Run(1);
        Assert.NotNull(scene.Fall.Drops);
        Assert.Equal(2, scene.Head.EmittersInUse);
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

    [Fact]
    public void TheOriginalsPoolCapsTheRainAtTheOriginalDensity()
    {
        var scene = new Scene(Precipitation.Snow, 1);
        scene.Run(600);
        Assert.True(scene.Head.ParticlesInUse <= ParticleHead.DefaultMaxParticles);
    }
}
