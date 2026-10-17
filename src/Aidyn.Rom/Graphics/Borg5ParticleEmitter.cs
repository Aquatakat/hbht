using System.Buffers.Binary;
using System.Numerics;

namespace Aidyn.Rom.Graphics;

/// <summary>A <see cref="Borg5ParticleEmitter"/>'s flags (<c>Borg5PartFlag</c>).</summary>
[Flags]
public enum Borg5EmitterFlags : ushort
{
    None = 0,
    /// <summary>Particles bounce off height 0, keeping 60% of their speed, turned up.</summary>
    BounceAtZero = 0x1,
    /// <summary>The target jitter is exactly <see cref="Borg5ParticleEmitter.TargetJitter"/> long, rather than up to it.</summary>
    ExactTargetJitter = 0x4,
    /// <summary>The spawn jitter is exactly <see cref="Borg5ParticleEmitter.SpawnJitter"/> long, rather than up to it.</summary>
    ExactSpawnJitter = 0x8,
    /// <summary>Particles leave along the locator's normal, rather than <see cref="Borg5ParticleEmitter.Direction"/>.</summary>
    LocatorNormal = 0x10,
    /// <summary>An actor starts the emitter when it enters a state for <see cref="Borg5ParticleEmitter.Request"/>.</summary>
    ActorRequest = 0x20,
    /// <summary>Set on two emitters; nothing reads it.</summary>
    Unknown40 = 0x40,
    /// <summary>The emitter itself moves from its locator away from the target, colliding with the map.</summary>
    Moves = 0x200,
    /// <summary><see cref="Borg5ParticleEmitter.Acceleration2Change"/> changes the second acceleration each tick.</summary>
    Acceleration2Changes = 0x400,
    /// <summary>A second acceleration, <see cref="Borg5ParticleEmitter.Acceleration2"/>, applies.</summary>
    Acceleration2 = 0x800,
    /// <summary><see cref="Borg5ParticleEmitter.AccelerationChange"/> changes the acceleration each tick.</summary>
    AccelerationChanges = 0x1000,
    /// <summary>Particles are aimed at the target locator as they spawn, to reach it as they die: a velocity added once.</summary>
    Target = 0x2000,
    /// <summary>Particles fade to <see cref="Borg5ParticleEmitter.FadeColor"/> as they die.</summary>
    Fade = 0x4000,
    /// <summary>Particles scale to <see cref="Borg5ParticleEmitter.EndScale"/> as they die.</summary>
    Scales = 0x8000,
}

/// <summary>Where on a scene an emitter sits: one of its locator models.</summary>
/// <param name="Index">0–7: the scene's model whose <see cref="Borg2Geometry.Unknown24"/> low byte is this.</param>
/// <param name="Scene">0 for the emitter's own scene, 1 or 2 for a scene attached to it at run time.</param>
public readonly record struct Borg5Locator(int Index, int Scene)
{
    public static Borg5Locator FromRaw(ushort raw) => new(raw & 7, (raw >> 7) & 3);
}

/// <summary>
/// A particle emitter of a Borg5 scene (<c>Borg5_particle</c>, run by <c>particles.cpp</c>).
/// Times are in 60 Hz ticks, positions in scene units, and velocities in scene units per tick
/// once multiplied by 0.006, as the game does.
/// </summary>
/// <remarks>
/// <para>
/// When the emitter starts it spawns <see cref="Burst"/> particles, held undrawn while it waits
/// <see cref="Delay"/> ticks. For <see cref="Lifetime"/> ticks after that, it spawns one particle every
/// <see cref="Interval"/> ticks, or −<see cref="Interval"/> particles every frame if negative, or none
/// if 0. Each particle lives <see cref="ParticleLifetime"/> ticks, as a textured square.
/// </para>
/// <para>
/// Each frame the emitter moves to its locator. A particle spawns there, moved by a random vector up
/// to <see cref="SpawnJitter"/> long. It starts with a velocity in a random direction exactly
/// <see cref="VelocityJitter"/> long, plus the direction times <see cref="Speed"/>, all × 0.006, with a
/// random scale and a colour drawn a channel at a time from the two ranges. The acceleration's XYZ
/// × 0.006 × its W is added to every particle's velocity each tick. <c>Aidyn.Core.Particles</c> runs it;
/// see <c>docs/formats/particles.md</c>.
/// </para>
/// <para>
/// Particles are drawn with texture <see cref="Texture"/> of the engine's particle list (see
/// <see cref="Engine.ParticleTextures"/>), not the scene's. The game's generator for particles is
/// seeded from the clock, so no two runs match.
/// </para>
/// </remarks>
/// <param name="Request">For <see cref="Borg5EmitterFlags.ActorRequest"/>: the Borg7 request.</param>
/// <param name="Target">For <see cref="Borg5EmitterFlags.Target"/>: where particles head.</param>
/// <param name="Position">Overwritten by the locator's position each tick, unless the scene has no such locator.</param>
/// <param name="TargetPosition">Overwritten by the target locator's position each tick.</param>
/// <param name="Direction">Overwritten by the locator's normal each tick with <see cref="Borg5EmitterFlags.LocatorNormal"/>.</param>
/// <param name="EmitterSpeed">For <see cref="Borg5EmitterFlags.Moves"/>.</param>
/// <param name="Unknown40">Between 0 and 0.8 on 20 emitters, and 0 on the rest. Nothing reads it.</param>
public sealed record Borg5ParticleEmitter(
    Borg5Locator Locator,
    Borg5Locator Target,
    ushort Request,
    short Lifetime,
    short ParticleLifetime,
    ushort Delay,
    Borg5EmitterFlags Flags,
    float EmitterSpeed,
    short Interval,
    short Texture,
    ushort Burst,
    Vector3 Position,
    float SpawnJitter,
    Vector3 TargetPosition,
    float TargetJitter,
    float VelocityJitter,
    Vector4 Acceleration,
    Vector4 AccelerationChange,
    Vector4 Acceleration2,
    Vector4 Acceleration2Change,
    Vector3 Direction,
    float Speed,
    float MinScale,
    float MaxScale,
    float EndScale,
    Vector4 MinColor,
    Vector4 MaxColor,
    Vector4 FadeColor,
    float Unknown40)
{
    public const int Size = 0xC0;

    // Every emitter ends with these 20 bytes.
    private static readonly byte[] Tail = [.. new byte[15], 0xFF, 0, 0, 0, 0xFF];

    public static Borg5ParticleEmitter Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new InvalidDataException($"A particle emitter is 0x{Size:X} bytes, not 0x{data.Length:X}.");
        var e = data[..Size].ToArray();

        ushort U16(int at) => BinaryPrimitives.ReadUInt16BigEndian(e.AsSpan(at));
        short S16(int at) => BinaryPrimitives.ReadInt16BigEndian(e.AsSpan(at));
        float F32(int at) => BinaryPrimitives.ReadSingleBigEndian(e.AsSpan(at));
        Vector3 Vec3(int at) => new(F32(at), F32(at + 4), F32(at + 8));
        Vector4 Vec4(int at) => new(F32(at), F32(at + 4), F32(at + 8), F32(at + 12));
        Vector4 Color(int at) => new Vector4(e[at], e[at + 1], e[at + 2], e[at + 3]) / 255;

        if (U16(6) != 0 || U16(0x16) != 0 || !e.AsSpan(0xAC).SequenceEqual(Tail))
            throw new InvalidDataException("A particle emitter has an unknown field set.");
        return new Borg5ParticleEmitter(
            Borg5Locator.FromRaw(U16(0)), Borg5Locator.FromRaw(U16(2)), U16(4),
            S16(8), S16(0xA), U16(0xC), (Borg5EmitterFlags)U16(0xE), F32(0x10), S16(0x14), S16(0x18), U16(0x1A),
            Vec3(0x1C), F32(0x28), Vec3(0x2C), F32(0x38), F32(0x3C),
            Vec4(0x44), Vec4(0x54), Vec4(0x64), Vec4(0x74), Vec3(0x84), F32(0x90),
            F32(0x94), F32(0x98), F32(0x9C), Color(0xA0), Color(0xA4), Color(0xA8), F32(0x40));
    }
}
