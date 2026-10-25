using System.Numerics;
using HBHT.Core.Lights;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Particles;

/// <summary>
/// A trapped container going off (<c>Containers.Explode</c>, <c>0x8000ffe4</c> in 1.0): a fire that burns where it stood for
/// 600 ticks, three small ones thrown out of it that burn for 60, and a burst of grey debris that falls and bounces, each an
/// emitter of the particle pool made by code, with its dynamic light. See docs/systems/map-objects.md#containers.
/// </summary>
/// <remarks>
/// <para>
/// The fires (<c>Explosion.Fire</c>, <c>0x800174f8</c>, over <c>Explosion.MakeFire</c>, <c>0x8001732c</c>, and its spawn
/// <c>Explosion.SpawnFlame</c>, <c>0x80017138</c>) spawn two flames a frame, each 120 ticks of yellow fading to a clear dark
/// red as it rises and grows; a thrown fire is an emitter that moves itself (<see cref="EmitterFlags.Moves"/>), a sphere
/// thrown up and out that falls onto the ground, its flames and its light following it. The burst
/// (<c>Explosion.Burst</c>, <c>0x80016c00</c>) is 75 particles flung every way at once, falling and bouncing off the
/// container's height for 420 ticks as they fade out.
/// </para>
/// <para>
/// An emitter the pool has no room for comes without its light, as in the original. Without a pool the lights come alone
/// (<see cref="DynamicLights.Explosion"/>). The draws are the pool's stream's, the port's own (ADR 0006).
/// </para>
/// </remarks>
public static class Explosion
{
    /// <summary>The fires' lives: the one that stays, and the three thrown (<c>Containers.Explode</c>'s literals 600 and 60).</summary>
    public const int FireTicks = 600, ThrownTicks = 60, Thrown = 3;

    /// <summary>How much the stayed and the thrown fires' flames grow over their lives (<c>Containers.Explode</c>, 1.0 and 0.35).</summary>
    public const float FireGrowth = 1, ThrownGrowth = 0.35f;

    /// <summary>A flame's life, and the flames a frame (an interval of −2).</summary>
    public const int FlameTicks = 120, FlamesAFrame = 2;

    /// <summary>A flame's scale as it's born (<c>0x800cdd3c</c>).</summary>
    public const float FlameScale = 0.1f;

    /// <summary>How far from the fire a flame is born, across the ground, at most (<c>Explosion.Fire</c>'s 0.05).</summary>
    public const float FlameSpread = 0.05f;

    /// <summary>A flame rises at up to this over its life (0.4), and drifts across at up to 0.0025 a tick.</summary>
    public const float FlameRise = 0.4f, FlameDrift = 0.0025f;

    /// <summary>What pulls every flame up, a tick² (<c>0x387ba882</c>).</summary>
    public const float FlameLift = 6e-5f;

    /// <summary>A flame's colour as it's born, yellow, and what it fades to over its life, a clear dark red.</summary>
    public static readonly Vector4 FlameColor = new(1, 1, 0, 1), FlameFade = new(0.5f, 0, 0, 0);

    /// <summary>What each flame adds of red and green over its life, at most, drawn for each: so some burn oranger.</summary>
    public static readonly Vector4 FlameWarmth = new(0.2f, 0.1f, 0, 0);

    /// <summary>A thrown fire's sphere's radius and gravity, a tick² (<c>0x800d58e4</c>), the pool's one for every emitter that moves.</summary>
    public const float ThrownRadius = 0.25f, ThrownGravity = 0.006f;

    /// <summary>A thrown fire leaves at this across the ground, every way, and this up, a tick.</summary>
    public const float ThrownOut = 0.036f, ThrownUp = 0.12f;

    /// <summary>The burst: its particles, their life and their scale (<c>0x800cdd24</c>).</summary>
    public const int BurstCount = 75, BurstTicks = 420;

    public const float BurstScale = 0.25f;

    /// <summary>The burst's particles leave at a speed from 0.04 to 0.06 a tick (<c>0x800cda10</c>, <c>0x800cda14</c>).</summary>
    public const float BurstSlowest = 0.04f, BurstFastest = 0.06f;

    /// <summary>What pulls them down, a tick² (<c>0x800cda18</c>, −6/7000).</summary>
    public const float BurstGravity = 0.000857142857f;

    /// <summary>Their colour, a grey (<c>0x800cda00</c>), which fades to nothing over their life.</summary>
    public static readonly Vector4 BurstColor = new(0.35f, 0.3f, 0.25f, 1);

    /// <summary>
    /// The explosion at <paramref name="at"/>, the container's place in the world: the fire, the three thrown, then the
    /// burst, each with its light, in the pool if there is one.
    /// </summary>
    /// <param name="texture">What the particles are drawn with: the particle texture slot 2 the code names, as the base plugin's explosion record holds it.</param>
    public static void Start(Vector3 at, DynamicLights lights, ParticleHead? particles, RecordId? texture)
    {
        if (particles is null)
        {
            lights.Explosion(at);
            return;
        }
        Fire(particles, lights, at, FireGrowth, FireTicks, thrown: false, texture);
        for (var i = 0; i < Thrown; i++)
            Fire(particles, lights, at, ThrownGrowth, ThrownTicks, thrown: true, texture);
        Burst(particles, lights, at, texture);
    }

    /// <summary>
    /// A fire (<c>Explosion.Fire</c>, <c>0x800174f8</c>): an emitter of flames for <paramref name="ticks"/>, and its light for
    /// as long. A thrown one moves itself, from where the container stood.
    /// </summary>
    public static Emitter? Fire(ParticleHead head, DynamicLights lights, Vector3 at, float growth, int ticks, bool thrown, RecordId? texture)
    {
        var fire = new Flames();
        if (head.AllocEmitter(ticks, texture, -FlamesAFrame, spawn: fire.Spawn) is not { } e)
            return null;
        e.Position = at;
        ref var h = ref head[e.Head];
        h.Flags |= ParticleFlags.AddVelocity;
        h.Vector = new Vector4(0, FlameLift, 0, 1);
        // Every flame grows by the fire's growth over its life, and fades from yellow to the clear red.
        var grow = head.AllocModifier(e, ticks);
        head[grow].Flags |= ParticleFlags.AddScale;
        var g = growth / FlameTicks;
        head[grow].Vector = new Vector4(g, g, g, 0);
        var fade = head.AllocModifier(e, ticks);
        head[fade].Flags |= ParticleFlags.AddColor;
        head[fade].Vector = (FlameColor - FlameFade) * (float)(-1.0 / FlameTicks);
        if (thrown)
        {
            e.Flags |= EmitterFlags.Moves;
            var away = ParticleHead.Direction2(head.Random, ThrownOut);
            e.Sphere = new CollisionSphere
            {
                Radius = ThrownRadius,
                Gravity = new Vector3(0, -ThrownGravity, 0),
                Position = at,
                Velocity = new Vector3(away.X, ThrownUp, away.Y),
            };
        }
        fire.Light = lights.FireLight(at, ticks);
        fire.Lights = lights;
        return e;
    }

    /// <summary>
    /// The burst (<c>Explosion.Burst</c>, <c>0x80016c00</c>): 75 particles flung from the container every way at once, falling
    /// and bouncing off its height and fading out over 420 ticks, then its light.
    /// </summary>
    public static Emitter? Burst(ParticleHead head, DynamicLights lights, Vector3 at, RecordId? texture)
    {
        if (head.AllocEmitter(BurstTicks, texture, 0) is not { } e)
            return null;
        e.Position = at;
        e.Floor = at.Y;
        e.Flags |= EmitterFlags.Bounces;
        ref var h = ref head[e.Head];
        h.Flags |= ParticleFlags.AddVelocity;
        h.Vector = new Vector4(0, -BurstGravity, 0, 1);
        var fade = head.AllocModifier(e, BurstTicks);
        head[fade].Flags |= ParticleFlags.AddColor;
        // Over its life less one (Particles.InverseLife, 0x800a8e6c).
        head[fade].Vector = -BurstColor * (1f / (BurstTicks - 1));
        for (var i = 0; i < BurstCount; i++)
        {
            var id = head.AllocParticle(e, BurstTicks);
            ref var p = ref head[id];
            p.Scale = new Vector2(BurstScale, BurstScale);
            p.Position = at;
            p.Color = BurstColor;
            p.Velocity = head.Direction(head.FloatRange(BurstSlowest, BurstFastest));
        }
        lights.BurstLight(at);
        return e;
    }

    /// <summary>A fire's flames, and its light, which follows a thrown fire while it's lit (the emitter's <c>+0x1C4</c>).</summary>
    private sealed class Flames
    {
        public PointLight? Light;
        public DynamicLights? Lights;

        /// <summary>A flame (<c>Explosion.SpawnFlame</c>, <c>0x80017138</c>).</summary>
        public void Spawn(ParticleHead head, Emitter e)
        {
            var id = head.AllocParticle(e, FlameTicks);
            ref var p = ref head[id];
            p.Scale = new Vector2(FlameScale, FlameScale);
            var from = e.Flags.HasFlag(EmitterFlags.Moves) && e.Sphere is { } sphere ? sphere.Position : e.Position;
            if (Light is not null)
            {
                if (Lights!.IsLit(Light))
                    Light.Position = from;
                else
                    Light = null;
            }
            var random = head.Random;
            var spread = ParticleHead.Direction2(random, random.Float(FlameSpread));
            p.Position = from + new Vector3(spread.X, 0, spread.Y);
            var drift = ParticleHead.Direction2(random, random.Float(FlameDrift));
            var rise = random.Float(FlameRise / FlameTicks);
            p.Velocity = new Vector3(drift.X, rise, drift.Y);
            p.Color = FlameColor;
            p.Vector = new Vector4(random.Float(FlameWarmth.X / FlameTicks), random.Float(FlameWarmth.Y / FlameTicks),
                random.Float(FlameWarmth.Z / FlameTicks), random.Float(FlameWarmth.W / FlameTicks));
            p.Target = p.Id;
            p.Flags |= ParticleFlags.AddColor;
        }
    }
}
