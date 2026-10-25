using System.Numerics;
using HBHT.Core.Lights;
using HBHT.Core.Particles;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>A trapped container going off (<c>Containers.Explode</c>, 0x8000ffe4): what it starts in the particle pool and the lights.</summary>
public class ExplosionTests
{
    private static readonly RecordId Texture = RecordId.Hex(RecordTypes.Texture, 1);

    private static readonly Vector3 Chest = new(2, 0, 3);

    [Fact]
    public void ItStartsAFireThreeThrownFiresAndABurstEachWithItsLight()
    {
        var head = new ParticleHead(new Lcg());
        var lights = new DynamicLights();
        Explosion.Start(Chest, lights, head, Texture);

        var emitters = head.Emitters.Where(e => e.InUse).ToList();
        Assert.Equal(5, emitters.Count);
        Assert.All(emitters, e => Assert.Equal(Texture, e.Texture));

        // The fire that stays: 600 ticks of two flames a frame, where the chest is.
        var fire = emitters[0];
        Assert.Equal((600, -2), (fire.Life, fire.Interval));
        Assert.Equal(Chest, fire.Position);
        Assert.False(fire.Flags.HasFlag(EmitterFlags.Moves));

        // The three thrown: 60 ticks each, a sphere leaving the chest at 0.036 across the ground and 0.12 up.
        foreach (var thrown in emitters.Skip(1).Take(3))
        {
            Assert.Equal((60, -2), (thrown.Life, thrown.Interval));
            Assert.True(thrown.Flags.HasFlag(EmitterFlags.Moves));
            var sphere = thrown.Sphere!;
            Assert.Equal(Chest, sphere.Position);
            Assert.Equal(0.25f, sphere.Radius);
            Assert.Equal(0.12f, sphere.Velocity.Y);
            Assert.Equal(0.036f, new Vector2(sphere.Velocity.X, sphere.Velocity.Z).Length(), 5);
        }

        // The burst: 75 grey particles flung every way at once, bouncing off the chest's height.
        var burst = emitters[4];
        Assert.Equal(420, burst.Life);
        Assert.True(burst.Flags.HasFlag(EmitterFlags.Bounces));
        Assert.Equal(Chest.Y, burst.Floor);
        var debris = List(head, burst).Where(id => !head[id].Flags.HasFlag(ParticleFlags.Modifier)).ToList();
        Assert.Equal(75, debris.Count);
        Assert.All(debris, id =>
        {
            Assert.Equal(Chest, head[id].Position);
            Assert.Equal(Explosion.BurstColor, head[id].Color);
            Assert.InRange(head[id].Velocity.Length(), 0.04f - 1e-6f, 0.06f + 1e-6f);
        });

        // The lights, as the original adds them: each fire's after its emitter, then the burst's.
        var lit = lights.Lit.ToList();
        Assert.Equal(5, lit.Count);
        Assert.Equal([LightKind.Random, LightKind.Random, LightKind.Random, LightKind.Random, LightKind.Alternate], lit.Select(l => l.Kind));
        Assert.Equal([5f, 5, 5, 5, 10], lit.Select(l => l.Radius));
    }

    [Fact]
    public void TheFireSpawnsTwoYellowFlamesAFrameThatRiseAndFade()
    {
        var head = new ParticleHead(new Lcg());
        var lights = new DynamicLights();
        Explosion.Start(Chest, lights, head, Texture);
        var fire = head.Emitters[0];

        head.Frame(2);
        var flames = Flames(head, fire);
        Assert.Equal(2, flames.Count);
        Assert.All(flames, id =>
        {
            // Yellow, a frame into its fade: what each adds of its own warmth is clamped away while it's full.
            Assert.InRange(Vector4.Distance(Explosion.FlameColor + (Explosion.FlameFade - Explosion.FlameColor) * (2f / 120), head[id].Color), 0, 1e-5f);
            Assert.InRange(Vector2.Distance(new(head[id].Position.X, head[id].Position.Z), new(Chest.X, Chest.Z)), 0, 0.06f);
        });

        for (var i = 0; i < 30; i++)
            head.Frame(2);
        flames = Flames(head, fire);
        // Two a frame, none dead yet at 62 ticks of their 120.
        Assert.Equal(62, flames.Count);
        var oldest = flames[^1];
        Assert.True(head[oldest].Position.Y > Chest.Y);
        Assert.True(head[oldest].Scale.X > Explosion.FlameScale);
        // Toward the clear dark red: less green and alpha, red no lower than the fade's.
        Assert.True(head[oldest].Color.Y < 0.6f && head[oldest].Color.W < 0.6f && head[oldest].Color.X > 0.7f);

        // By the end of a flame's life, at most two frames' worth of each are alive at once.
        for (var i = 0; i < 60; i++)
            head.Frame(2);
        Assert.InRange(Flames(head, fire).Count, 118, 120);
    }

    [Fact]
    public void AThrownFireFallsOntoTheGroundAndItsLightFollowsIt()
    {
        var head = new ParticleHead(new Lcg()) { World = Worlds.Flat() };
        var lights = new DynamicLights();
        Explosion.Start(Chest, lights, head, Texture);
        var thrown = head.Emitters[1];
        var light = lights.Lit.ElementAt(1);

        var highest = 0f;
        for (var i = 0; i < 29; i++)
        {
            head.Frame(2);
            lights.Step(2);
            highest = MathF.Max(highest, thrown.Sphere!.Position.Y);
        }
        // Up to 1.2 above the chest at its height, then down onto the ground, a radius above it, away from the chest.
        Assert.InRange(highest, 1.1f, 1.3f);
        var sphere = thrown.Sphere!;
        Assert.InRange(sphere.Position.Y, 0.2f, 0.3f);
        Assert.True(Vector2.Distance(new(sphere.Position.X, sphere.Position.Z), new(Chest.X, Chest.Z)) > 1);
        Assert.Equal(sphere.Position, light.Position);
        Assert.True(lights.IsLit(light));
    }

    [Fact]
    public void TheBurstsDebrisFallsAndBouncesOffTheChestsHeight()
    {
        var at = new Vector3(0, 5, 0);
        var head = new ParticleHead(new Lcg());
        Explosion.Start(at, new DynamicLights(), head, Texture);
        var burst = head.Emitters[4];
        var lowest = float.MaxValue;
        for (var i = 0; i < 200; i++)
        {
            head.Frame(2);
            lowest = Math.Min(lowest, Flames(head, burst).Min(id => head[id].Position.Y));
        }
        // A particle below the floor goes back up by its last velocity, so none falls far under.
        Assert.InRange(lowest, at.Y - 0.5f, at.Y);
        // Gone by its life's end.
        for (var i = 0; i < 20; i++)
            head.Frame(2);
        Assert.False(burst.InUse);
    }

    [Fact]
    public void WithoutAPoolItsLightsComeAloneAndAnEmitterWithoutRoomComesWithoutItsLight()
    {
        var lights = new DynamicLights();
        Explosion.Start(Chest, lights, null, Texture);
        Assert.Equal(5, lights.Count);

        // A pool with room for three fires (a head and two modifiers each): they get their lights, the rest none.
        var head = new ParticleHead(new Lcg(), maxParticles: 9);
        var few = new DynamicLights();
        Explosion.Start(Chest, few, head, Texture);
        Assert.Equal(3, head.EmittersInUse);
        Assert.Equal(3, few.Count);
    }

    private static List<short> List(ParticleHead head, Emitter e)
    {
        var ids = new List<short>();
        for (var id = head[e.Head].Next; id != -1; id = head[id].Next)
            ids.Add(id);
        return ids;
    }

    private static List<short> Flames(ParticleHead head, Emitter e) =>
        [.. List(head, e).Where(id => !head[id].Flags.HasFlag(ParticleFlags.Modifier))];
}
