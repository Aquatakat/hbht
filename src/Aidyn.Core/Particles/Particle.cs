using System.Numerics;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Particles;

/// <summary>A particle's flags (the particle's <c>+0xA</c>).</summary>
[Flags]
public enum ParticleFlags : ushort
{
    None = 0,
    /// <summary>Freed when its life runs out.</summary>
    FreeAtDeath = 0x1,
    /// <summary>Hidden and kept at life 0 when its life runs out, so it goes on stepping: heads and modifiers.</summary>
    KeepAtDeath = 0x2,
    /// <summary>Drawn turned by its own <see cref="Particle.Orientation"/>, not facing the camera: the weather's splashes lie on the ground.</summary>
    OwnOrientation = 0x4,
    /// <summary>Doesn't hold up its emitter's freeing: heads and modifiers.</summary>
    Modifier = 0x8,
    Visible = 0x10,
    /// <summary>Not drawn the first time it's stepped.</summary>
    New = 0x20,
    /// <summary>Its vector applies times its own W.</summary>
    MultiplyByW = 0x40,
    /// <summary>Its vector adds to its target's position.</summary>
    AddPosition = 0x80,
    /// <summary>Its vector adds to its target's velocity: an acceleration.</summary>
    AddVelocity = 0x100,
    /// <summary>Its vector adds to its target's colour, clamped to 0–1: a fade.</summary>
    AddColor = 0x200,
    /// <summary>Its vector's X and Y add to its target's scale, not below 0.</summary>
    AddScale = 0x400,
    /// <summary>Its vector adds to its target's vector: an acceleration's change.</summary>
    AddToTarget = 0x800,
    /// <summary>No vector changes its position, velocity, colour or scale.</summary>
    Unmodified = 0x4000,
    /// <summary>Its vector applies to every particle in its emitter's list, from the head on, rather than its target.</summary>
    Chain = 0x8000,
}

/// <summary>
/// A particle of the pool (0x1A0 bytes in the original). An emitter's head and its modifiers are particles too, whose
/// vectors change the others: an acceleration adds to every particle's velocity, a fade to its own colour.
/// </summary>
public struct Particle(short id)
{
    public readonly short Id = id;

    /// <summary>Its ticks to live. Under 0 it isn't stepped.</summary>
    public short Life;

    /// <summary>The particle its vector applies to, without <see cref="ParticleFlags.Chain"/>: itself, unless set. −1 is none.</summary>
    public short Target;

    /// <summary>Its neighbours in its emitter's list, −1 at the ends.</summary>
    public short Next, Previous;

    public ParticleFlags Flags;

    /// <summary>In scene units, and units a tick.</summary>
    public Vector3 Position, Velocity;

    /// <summary>Its square's size, as a share of <see cref="ParticleHead.SquareSide"/>, across and up.</summary>
    public Vector2 Scale;

    /// <summary>0–1, on-screen values, alpha last.</summary>
    public Vector4 Color;

    /// <summary>What it adds to its target or its list each tick, by its flags.</summary>
    public Vector4 Vector;

    /// <summary>With <see cref="ParticleFlags.OwnOrientation"/>: how its square is turned, from facing +Z with +Y up.</summary>
    public Quaternion Orientation;

    // The port's, for interpolating: where it was last drawn.
    internal Vector3 LastDrawn;
    internal bool WasDrawn;

    internal void Reset()
    {
        Life = 0;
        Target = Id;
        Next = Previous = -1;
        Flags = 0;
        Position = Velocity = Vector3.Zero;
        Scale = Vector2.One;
        Color = Vector = Vector4.Zero;
        Orientation = Quaternion.Identity;
        WasDrawn = false;
    }
}

/// <summary>An emitter's flags (the emitter's <c>+0xC</c>). Every emitter starts with <see cref="Default"/>.</summary>
[Flags]
public enum EmitterFlags : ushort
{
    None = 0,
    /// <summary>Freed when its life runs out.</summary>
    FreeAtEnd = 0x1,
    /// <summary>Its life is held at 0 once it runs out.</summary>
    KeepAtEnd = 0x2,
    /// <summary>Freed only once its particles that aren't modifiers have died.</summary>
    WaitForParticles = 0x4,
    /// <summary>
    /// Run in the frame's first particle pass, after the centre chunk's scene and without the depth buffer, rather than
    /// last. Nothing the port runs has it.
    /// </summary>
    FirstPass = 0x1000,
    /// <summary>It moves itself, colliding with the map (flag 0x200 of a Borg5 emitter). No emitter has it.</summary>
    Moves = 0x2000,
    /// <summary>Its particles bounce off its <see cref="Emitter.Floor"/>.</summary>
    Bounces = 0x4000,
    InUse = 0x8000,
    Default = InUse | WaitForParticles | KeepAtEnd | FreeAtEnd,
}

/// <summary>What an emitter does each frame before it spawns (its <c>+0x1B8</c>), or to spawn one particle (<c>+0x1BC</c>).</summary>
public delegate void EmitterCallback(ParticleHead head, Emitter emitter);

/// <summary>What an emitter does to each of its live particles after stepping it (its <c>+0x1C0</c>): the weather's drops land with it.</summary>
public delegate void ParticleCallback(ParticleHead head, Emitter emitter, ref Particle particle);

/// <summary>
/// An emitter of the pool (0x210 bytes in the original): a list of particles headed by one of its own, which it spawns
/// into every <see cref="Interval"/> ticks with <see cref="Spawn"/> for its <see cref="Life"/>. A Borg5 emitter's
/// callbacks follow its actor's locators and spawn by its record; code makes its own, as the weather does.
/// </summary>
public sealed class Emitter(int slot)
{
    /// <summary>Its place in the pool, 0–15, which is the order emitters run and draw in.</summary>
    public int Slot { get; } = slot;

    public EmitterFlags Flags;

    /// <summary>Its ticks to spawn for. Code that keeps an emitter going sets it each frame, as the weather does.</summary>
    public int Life;

    /// <summary>Ticks it waits before anything runs, its burst held undrawn and unaged.</summary>
    public int Delay;

    /// <summary>A spawn every this many ticks, counted down by the frame; under 0, −this many a frame; 0 spawns nothing.</summary>
    public int Interval;

    /// <summary>The ticks to the next spawn.</summary>
    public int Counter;

    /// <summary>What its particles are drawn with.</summary>
    public RecordId? Texture;

    /// <summary>The height its particles bounce off with <see cref="EmitterFlags.Bounces"/>.</summary>
    public float Floor;

    /// <summary>Its head particle, whose vector (an acceleration, or the weather's fall) applies to the whole list.</summary>
    public short Head { get; internal set; }

    public EmitterCallback? Update, Spawn;

    public ParticleCallback? Step;

    /// <summary>A Borg5 emitter's record and actor, or null for code's.</summary>
    public ParticleEmitter? Record { get; internal set; }

    public IParticleAnchor? Anchor { get; internal set; }

    /// <summary>Where it spawns from, its target and its direction, which a Borg5 emitter's locators set each frame.</summary>
    public Vector3 Position, TargetPosition, Direction;

    public bool InUse => Flags.HasFlag(EmitterFlags.InUse);
}
