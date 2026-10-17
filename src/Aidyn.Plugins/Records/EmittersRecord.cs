using System.Numerics;

namespace Aidyn.Plugins.Records;

/// <summary>
/// A scene's particle emitters (<c>aidyn:emitters/0xHHHH</c>, keyed by the scene's archive index), from the end of its
/// Borg5. An actor of the scene starts the ones for a request when it enters a state for that request.
/// </summary>
public sealed record EmittersRecord(RecordId Id, IReadOnlyList<ParticleEmitter> Emitters);

/// <summary>A <see cref="ParticleEmitter"/>'s flags (<c>Borg5PartFlag</c>).</summary>
[Flags]
public enum ParticleEmitterFlags
{
    None = 0,
    /// <summary>Particles bounce off height 0.</summary>
    BounceAtZero = 0x1,
    /// <summary>The target jitter is exactly <see cref="ParticleEmitter.TargetJitter"/> long, rather than up to it.</summary>
    ExactTargetJitter = 0x4,
    /// <summary>The spawn jitter is exactly <see cref="ParticleEmitter.SpawnJitter"/> long, rather than up to it.</summary>
    ExactSpawnJitter = 0x8,
    /// <summary>Particles leave along the locator's normal, rather than <see cref="ParticleEmitter.Direction"/>.</summary>
    LocatorNormal = 0x10,
    /// <summary>An actor starts the emitter when it enters a state for <see cref="ParticleEmitter.Request"/>.</summary>
    ActorRequest = 0x20,
    /// <summary>Set on two emitters.</summary>
    Unknown40 = 0x40,
    /// <summary>The emitter itself moves away from its target.</summary>
    Moves = 0x200,
    /// <summary><see cref="ParticleEmitter.Acceleration2Change"/> changes the second acceleration.</summary>
    Acceleration2Changes = 0x400,
    /// <summary>A second acceleration, <see cref="ParticleEmitter.Acceleration2"/>, applies.</summary>
    Acceleration2 = 0x800,
    /// <summary><see cref="ParticleEmitter.AccelerationChange"/> changes the acceleration.</summary>
    AccelerationChanges = 0x1000,
    /// <summary>Particles head for the target locator.</summary>
    Target = 0x2000,
    /// <summary>Particles fade to <see cref="ParticleEmitter.FadeColor"/>.</summary>
    Fade = 0x4000,
    /// <summary>Particles scale to <see cref="ParticleEmitter.EndScale"/>.</summary>
    Scales = 0x8000,
}

/// <summary>
/// Where an emitter or its target sits: locator <see cref="Index"/> (0–7) of its own scene (<see cref="Scene"/> 0),
/// or of a scene attached to it at run time (1 or 2: a spell's caster and target, an attack's victim).
/// </summary>
public readonly record struct ParticleLocator(int Index, int Scene);

/// <summary>
/// A particle emitter (<c>Borg5_particle</c>, run by <c>particles.cpp</c>), its fields as the Borg5 holds them. Times are
/// in 60 Hz ticks and distances in scene units. Each <c>Vector4</c>'s W multiplies its XYZ.
/// </summary>
/// <param name="Request">The Borg7 request that starts it, with <see cref="ParticleEmitterFlags.ActorRequest"/>.</param>
/// <param name="Lifetime">How long it spawns for after its delay.</param>
/// <param name="ParticleLifetime">How long each particle lives.</param>
/// <param name="Delay">How long it waits before its burst.</param>
/// <param name="Interval">One particle every this many ticks, or −this many every tick if negative, or none if 0.</param>
/// <param name="Burst">How many it spawns at once after its delay.</param>
/// <param name="Texture">What its particles are drawn with: the engine's particle texture for its slot.</param>
/// <param name="Position">Where it starts, before its locator places it.</param>
/// <param name="Unknown40">Between 0 and 0.8 on 20 emitters, and 0 on the rest.</param>
public sealed record ParticleEmitter(
    ParticleLocator Locator,
    ParticleLocator Target,
    int? Request,
    ParticleEmitterFlags Flags,
    int Lifetime,
    int ParticleLifetime,
    int Delay,
    int Interval,
    int Burst,
    RecordId? Texture,
    int TextureSlot,
    float EmitterSpeed,
    Vector3 Position,
    float SpawnJitter,
    Vector3 TargetPosition,
    float TargetJitter,
    float VelocityJitter,
    Vector3 Acceleration,
    float AccelerationScale,
    Vector3 AccelerationChange,
    float AccelerationChangeScale,
    Vector3 Acceleration2,
    float Acceleration2Scale,
    Vector3 Acceleration2Change,
    float Acceleration2ChangeScale,
    Vector3 Direction,
    float Speed,
    float MinScale,
    float MaxScale,
    float EndScale,
    Rgba MinColor,
    Rgba MaxColor,
    Rgba FadeColor,
    float Unknown40);
