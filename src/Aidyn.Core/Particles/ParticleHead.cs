using System.Numerics;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Particles;

/// <summary>A particle as it was drawn, on the frame it was drawn, and where it was drawn the frame before.</summary>
/// <param name="Emitter">Its emitter's slot, 0–15, which is the order the original draws them in.</param>
/// <param name="Previous">Where it was drawn the frame before, or <see cref="Position"/> if it wasn't.</param>
/// <param name="Size">Its square's sides, in scene units: 0.7071 × its scale.</param>
/// <param name="Color">Its vertex colour, an on-screen value, which multiplies the texel, alpha too.</param>
/// <param name="Orientation">How its square is turned, from facing +Z, or null to face the camera.</param>
public readonly record struct ParticleSprite(int Emitter, RecordId? Texture, Vector3 Position, Vector3 Previous, Vector2 Size, Rgba Color,
    Quaternion? Orientation = null);

/// <summary>
/// The engine's particles (<c>Particles.cpp</c>'s particle head, <c>Particles.Head</c> at <c>0x800d84b8</c>): a pool of 16
/// emitters and 384 particles, which actors' emitters start in and code makes its own in (the weather's rain and snow),
/// and the frame steps and draws. Each emitter keeps its particles in a list after a head particle of its own, which
/// with a few more "modifier" particles carries the accelerations and the scaling.
/// </summary>
/// <remarks>
/// <para>
/// The original runs it once a rendered frame with the frame's ticks as its delta, multiplying motion and ageing by it
/// but spawning once a frame (<c>ProcessAndRenderParticleHead</c>, <c>0x800a842c</c>). The port runs a frame every
/// <see cref="Cadence.FrameTicks"/> ticks, as the original does at 30 fps, and its view moves the sprites on between.
/// </para>
/// <para>
/// Its random stream is its own (ADR 0006): the original's is a private generator (<c>0x800e4ab0</c>) seeded from the
/// clock (<c>osGetTime() × 64 / 3000</c>, <c>Rng.Seed</c>) as the head is made, which no run can match, so the port's
/// starts from the boot seed and isn't saved.
/// </para>
/// </remarks>
public sealed class ParticleHead
{
    /// <summary>The particles the pool holds with the Expansion Pak (<c>InitZoneEngine</c>); without it 64 in trek, 96 in combat and 16 in a cut-scene.</summary>
    public const int DefaultMaxParticles = 0x180;

    /// <summary>The emitters the pool holds.</summary>
    public const int MaxEmitters = 16;

    /// <summary>A square's side at scale 1, in scene units: vertices ±353.55 × 16, scaled by 0.001 and drawn at 16 to the unit (<c>0x800d5914</c>).</summary>
    public const float SquareSide = 2 * 353.55f * 0.001f;

    /// <summary>What the record's vectors are scaled by, to units a tick (<c>Vec3.Scale</c> by <c>0x800d5970</c>).</summary>
    public const float Units = 0.006f;

    private const double Bounce = 0.6;

    private readonly Particle[] _particles;
    private readonly short[] _freeParticles;
    private readonly Emitter[] _emitters = new Emitter[MaxEmitters];
    private readonly short[] _freeEmitters = new short[MaxEmitters];
    private readonly List<ParticleSprite> _sprites = [];
    private int _usedParticles, _usedEmitters, _ticks;

    /// <param name="maxParticles">How many particles it hands out: the original's 384 unless more are asked for.</param>
    /// <param name="capacity">How many it has room for, which <see cref="MaxParticles"/> can be raised to later: at least <paramref name="maxParticles"/>.</param>
    public ParticleHead(IRandom? random = null, int maxParticles = DefaultMaxParticles, int capacity = 0)
    {
        Random = random ?? new Lcg();
        Capacity = Math.Max(capacity, maxParticles);
        if (Capacity >= short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(capacity), "A particle's id is a short.");
        _maxParticles = maxParticles;
        _particles = new Particle[Capacity + 1];
        _freeParticles = new short[Capacity + 1];
        for (var i = 0; i <= Capacity; i++)
        {
            _freeParticles[i] = (short)i;
            _particles[i] = new Particle((short)i);
        }
        for (var i = 0; i < MaxEmitters; i++)
        {
            _freeEmitters[i] = (short)i;
            _emitters[i] = new Emitter(i);
        }
    }

    /// <summary>The particles' random stream.</summary>
    public IRandom Random { get; }

    /// <summary>
    /// How many particles the pool hands out, at most <see cref="Capacity"/>: the original's 384 (<see cref="DefaultMaxParticles"/>),
    /// or more for denser rain and snow (<see cref="WeatherParticles.Density"/>). A full pool hands out the
    /// <see cref="Sentinel"/>, which is never linked or drawn. Lowered below what's in use, it hands out nothing new until
    /// enough have died.
    /// </summary>
    public int MaxParticles
    {
        get => _maxParticles;
        set => _maxParticles = Math.Clamp(value, 0, Capacity);
    }

    private int _maxParticles;

    /// <summary>How many particles the pool has room for, which <see cref="MaxParticles"/> can be raised to.</summary>
    public int Capacity { get; }

    /// <summary>What a full pool hands out: a particle that's written to and dropped.</summary>
    public short Sentinel => (short)Capacity;

    /// <summary>
    /// What the screen's fade leaves of the picture, from 0 to 1, which darkens every particle's colour and alpha below 1
    /// (the head's <c>+0x334</c>, which <c>ScreenFade.Step</c> writes).
    /// </summary>
    public float Brightness { get; set; } = 1;

    /// <summary>The particles in use, emitters' heads and modifiers among them.</summary>
    public int ParticlesInUse => _usedParticles;

    public int EmittersInUse => _usedEmitters;

    /// <summary>The pool's emitters by slot, in use or not.</summary>
    public IReadOnlyList<Emitter> Emitters => _emitters;

    /// <summary>
    /// The ticks since the last frame, from 0 to <see cref="Cadence.FrameTicks"/> − 1, for moving the sprites on from
    /// where the frame before drew them, at 0, to where the last one did, by the next frame.
    /// </summary>
    public int TicksSinceFrame => _ticks;

    /// <summary>What the last frame drew, in the order it drew them: by emitter, newest first.</summary>
    public IReadOnlyList<ParticleSprite> Sprites => _sprites;

    /// <summary>A particle of the pool, by id.</summary>
    public ref Particle this[int id] => ref _particles[id];

    /// <summary>A tick. Every <see cref="Cadence.FrameTicks"/> ticks, it runs a frame of that many.</summary>
    public void Step()
    {
        if (++_ticks < Cadence.FrameTicks)
            return;
        _ticks = 0;
        Frame(Cadence.FrameTicks);
    }

    // ---- Code's emitters ------------------------------------------------------------------------------------------

    /// <summary>
    /// An emitter from the pool with its head particle (<c>AllocParticleEmitter</c>, <c>0x800a8b64</c>), or null if
    /// every emitter is in use or the pool has no particle for its head. Its head is a modifier kept at life 0, with no
    /// vector; give it one, with its flags, for an acceleration or a fall.
    /// </summary>
    public Emitter? AllocEmitter(int life, RecordId? texture, int interval, EmitterCallback? update = null, EmitterCallback? spawn = null,
        ParticleCallback? step = null)
    {
        if (_usedEmitters >= MaxEmitters)
            return null;
        var head = AllocSlot(life);
        if (head == Sentinel)
            return null;
        var e = _emitters[_freeEmitters[_usedEmitters++]];
        e.Flags = EmitterFlags.Default;
        e.Life = life;
        e.Delay = e.Counter = e.SpawnTicks = e.SpawnClock = 0;
        e.Interval = interval;
        e.Texture = texture;
        e.Floor = 0;
        e.Head = head;
        (e.Update, e.Spawn, e.Step) = (update, spawn, step);
        (e.Record, e.Anchor) = (null, null);
        e.Position = e.TargetPosition = e.Direction = Vector3.Zero;
        _particles[head].Flags = ParticleFlags.Modifier | ParticleFlags.KeepAtDeath | ParticleFlags.Chain | ParticleFlags.Unmodified;
        return e;
    }

    /// <summary>
    /// A particle linked into an emitter's list just after its head, visible, new and freed at death
    /// (<c>ParticleHead.AllocParticle</c>, <c>0x800a8a00</c>). A full pool gives the <see cref="Sentinel"/>.
    /// </summary>
    public short AllocParticle(Emitter e, int life)
    {
        var id = AllocSlot(life);
        Link(e, id);
        return id;
    }

    /// <summary>A modifier, which lives on at life 0 changing its list (<c>ParticleHead.AllocModifier</c>, <c>0x800a8a50</c>).</summary>
    public short AllocModifier(Emitter e, int life)
    {
        var id = AllocSlot(life);
        _particles[id].Flags = ParticleFlags.Modifier | ParticleFlags.KeepAtDeath | ParticleFlags.Chain | ParticleFlags.Unmodified;
        Link(e, id);
        return id;
    }

    /// <summary>
    /// Moves a particle into another emitter's list, after its head, as the weather's drops become splashes
    /// (<c>ParticleHead.MoveParticle</c>, <c>0x800a8b14</c>). If that emitter has been freed, the particle is freed
    /// instead (<c>ParticleHead.Link</c> finds no head), and it returns false.
    /// </summary>
    public bool MoveParticle(short id, Emitter to)
    {
        if (id == Sentinel)
            return true;
        Unlink(id);
        if (!to.InUse)
        {
            _freeParticles[--_usedParticles] = _particles[id].Id;
            return false;
        }
        Link(to, id);
        return true;
    }

    /// <summary>Unlinks a particle and returns it to the pool (<c>ParticleHead.FreeParticle</c>, <c>0x800a8ab0</c>).</summary>
    public void FreeParticle(int id)
    {
        if (id == Sentinel)
            return;
        Unlink(id);
        _freeParticles[--_usedParticles] = _particles[id].Id;
    }

    /// <summary>
    /// Frees an emitter and its particles (<c>FreeParticleEmitter</c>, <c>0x800a8c98</c>). With <paramref name="wait"/>
    /// and <see cref="EmitterFlags.WaitForParticles"/>, it does nothing while a particle that isn't a modifier is still in
    /// its list.
    /// </summary>
    public void Free(Emitter e, bool wait = false)
    {
        if (!e.InUse)
            return;
        if (wait && e.Flags.HasFlag(EmitterFlags.WaitForParticles))
        {
            for (int id = _particles[e.Head].Next; id != -1; id = _particles[id].Next)
            {
                if (!_particles[id].Flags.HasFlag(ParticleFlags.Modifier))
                    return;
            }
        }
        for (int id = _particles[e.Head].Next, next; id != -1; id = next)
        {
            next = _particles[id].Next;
            FreeParticle(id);
        }
        FreeParticle(e.Head);
        e.Flags = 0;
        e.Life = 0;
        _freeEmitters[--_usedEmitters] = (short)e.Slot;
    }

    // ---- Actors' emitters ------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts a Borg5 emitter on an actor (<c>ParticleHead.Start</c>, <c>0x800a9df8</c>): its head and modifier
    /// particles, its place from the anchor, and its burst. Returns false if it has no lifetime, or the pool has no
    /// emitter free or no particle for its head.
    /// </summary>
    public bool Start(ParticleEmitter record, IParticleAnchor anchor)
    {
        if (record.Lifetime < 1 || record.ParticleLifetime < 1)
            return false;
        if (AllocEmitter(record.Lifetime, record.Texture, record.Interval, Place, record.Interval != 0 ? SpawnFromRecord : null) is not { } e)
            return false;
        (e.Record, e.Anchor) = (record, anchor);
        e.Delay = record.Delay;
        (e.Position, e.TargetPosition, e.Direction) = (record.Position, record.TargetPosition, record.Direction);
        if (record.Flags.HasFlag(ParticleEmitterFlags.BounceAtZero))
        {
            e.Floor = 0;
            e.Flags |= EmitterFlags.Bounces;
        }
        ref var h = ref _particles[e.Head];
        h.Flags |= ParticleFlags.AddVelocity | ParticleFlags.MultiplyByW;
        h.Vector = Scaled(record.Acceleration, record.AccelerationScale);

        if (record.Flags.HasFlag(ParticleEmitterFlags.AccelerationChanges))
            Changer(e, e.Head, Scaled(record.AccelerationChange, record.AccelerationChangeScale));
        if (record.Flags.HasFlag(ParticleEmitterFlags.Acceleration2))
        {
            var second = AllocModifier(e, record.Lifetime);
            if (second != Sentinel)
            {
                _particles[second].Flags |= ParticleFlags.AddVelocity | ParticleFlags.MultiplyByW;
                _particles[second].Vector = Scaled(record.Acceleration2, record.Acceleration2Scale);
            }
            if (record.Flags.HasFlag(ParticleEmitterFlags.Acceleration2Changes))
                Changer(e, second, Scaled(record.Acceleration2Change, record.Acceleration2ChangeScale));
        }
        if (record.Flags.HasFlag(ParticleEmitterFlags.Scales))
        {
            var scaler = AllocModifier(e, record.Lifetime);
            if (scaler != Sentinel)
            {
                var d = (record.EndScale - record.MinScale) / record.ParticleLifetime;
                _particles[scaler].Flags |= ParticleFlags.AddScale;
                _particles[scaler].Vector = new Vector4(d, d, d, 0);
            }
        }
        // Flag 0x200 moves the emitter itself, colliding with the map (ProcessCollisionSphere). No emitter has it.

        Place(this, e);
        for (var i = 0; i < record.Burst; i++)
            SpawnFromRecord(this, e);
        return true;
    }

    /// <summary>
    /// Stops every emitter on an actor, with all its particles at once, as freeing an actor does
    /// (<c>ParticleHead.Stop</c>, <c>0x800aa17c</c>).
    /// </summary>
    public void Stop(IParticleAnchor anchor)
    {
        foreach (var e in _emitters)
        {
            if (e.InUse && e.Anchor == anchor)
                Free(e);
        }
    }

    /// <summary>Kills every emitter still spawning, as a teleport does (<c>ParticleHead.KillActive</c>, <c>0x800a8e04</c>).</summary>
    public void KillActive()
    {
        foreach (var e in _emitters)
        {
            if (e.InUse && e.Life > 0)
                Free(e);
        }
    }

    /// <summary>Frees every emitter and particle, as freeing the zone engine does between trek, combat and a cut-scene.</summary>
    public void Clear()
    {
        foreach (var e in _emitters)
            Free(e);
        _sprites.Clear();
    }

    // ---- The frame -------------------------------------------------------------------------------------------------

    /// <summary>
    /// A frame of <paramref name="delta"/> ticks (<c>ProcessAndRenderParticleHead</c>): each emitter in slot order waits
    /// out its delay, then runs its update, ages, spawns, and steps and draws its list.
    /// </summary>
    /// <param name="only">Runs only the emitters this picks, leaving the rest as they are.</param>
    public void Frame(int delta, Func<Emitter, bool>? only = null)
    {
        _sprites.Clear();
        var dt = (float)delta;
        foreach (var e in _emitters)
        {
            if (!e.InUse || only is not null && !only(e))
                continue;
            // A freed actor stops its emitters as it goes.
            if (e.Anchor is { Alive: false })
            {
                Free(e);
                continue;
            }
            if (e.Delay > 0)
            {
                e.Delay = Math.Max(e.Delay - delta, 0);
                continue;
            }
            if (e.Life >= 0)
            {
                e.Update?.Invoke(this, e);
                e.Life -= delta;
                if (e.Spawn is { } spawn && e.Life > 0)
                {
                    if (e.SpawnTicks <= 0)
                    {
                        Spawn(e, spawn, delta);
                    }
                    else
                    {
                        // By frames of its own, the first at once, then as many as this frame's ticks reach.
                        for (; e.SpawnClock <= 0; e.SpawnClock += e.SpawnTicks)
                            Spawn(e, spawn, e.SpawnTicks);
                        e.SpawnClock -= delta;
                    }
                }
                StepList(e, delta, dt);
                if (e.Life > 0)
                    continue;
            }
            // Spent: it waits for its particles to die before it's freed.
            if (e.Flags.HasFlag(EmitterFlags.KeepAtEnd))
                e.Life = 0;
            if (e.Flags.HasFlag(EmitterFlags.FreeAtEnd))
                Free(e, wait: true);
        }
    }

    /// <summary>
    /// A frame's spawning: the counter loses the frame's ticks while it's above 0; at 0 or below it's set to the interval
    /// and the emitter spawns once, or with an interval under 0 that many times, so an interval of 0 spawns once every frame.
    /// </summary>
    private void Spawn(Emitter e, EmitterCallback spawn, int delta)
    {
        if (e.Counter > 0)
        {
            e.Counter -= delta;
            return;
        }
        e.Counter = e.Interval;
        for (var i = 0; i < (e.Interval >= 0 ? 1 : -e.Interval); i++)
            spawn(this, e);
    }

    /// <summary>The list: the head, the newest particle to the oldest, then the modifiers made at the start.</summary>
    private void StepList(Emitter e, int delta, float dt)
    {
        for (int id = e.Head, next; id != -1; id = next)
        {
            ref var p = ref _particles[id];
            if (p.Life >= 0)
            {
                p.Life = (short)(p.Life - delta);
                StepParticle(ref p, e, dt);
                if (p.Flags.HasFlag(ParticleFlags.Visible) && p.Life > 0)
                {
                    if (p.Flags.HasFlag(ParticleFlags.New))
                        p.Flags &= ~ParticleFlags.New;
                    else
                        Draw(e, ref p);
                }
            }
            next = p.Next;
            if (p.Life < 1)
            {
                if (p.Flags.HasFlag(ParticleFlags.KeepAtDeath))
                {
                    p.Flags &= ~ParticleFlags.Visible;
                    p.Life = 0;
                }
                if (p.Flags.HasFlag(ParticleFlags.FreeAtDeath))
                    FreeParticle(id);
            }
            else if (id != e.Head)
            {
                e.Step?.Invoke(this, e, ref p);
            }
        }
    }

    private void Draw(Emitter e, ref Particle p)
    {
        _sprites.Add(new ParticleSprite(e.Slot, e.Texture, p.Position, p.WasDrawn ? p.LastDrawn : p.Position, p.Scale * SquareSide,
            Bytes(p.Color), p.Flags.HasFlag(ParticleFlags.OwnOrientation) ? p.Orientation : null));
        (p.LastDrawn, p.WasDrawn) = (p.Position, true);
    }

    /// <summary>A particle from the pool (<c>ParticleHead.AllocSlot</c>, <c>0x800a895c</c>), or the sentinel when it's full.</summary>
    private short AllocSlot(int life)
    {
        var id = _usedParticles >= MaxParticles ? Sentinel : _freeParticles[_usedParticles++];
        ref var p = ref _particles[id];
        p.Reset();
        p.Flags = ParticleFlags.FreeAtDeath | ParticleFlags.Visible | ParticleFlags.New;
        p.Life = (short)life;
        return id;
    }

    /// <summary>A particle that adds to another's vector for the emitter's lifetime: an acceleration's change.</summary>
    private void Changer(Emitter e, short target, Vector4 change)
    {
        var id = AllocParticle(e, e.Life);
        if (id == Sentinel)
            return;
        ref var p = ref _particles[id];
        p.Flags = (p.Flags | ParticleFlags.AddToTarget) & ~ParticleFlags.Visible;
        p.Target = target;
        p.Vector = change;
    }

    /// <summary>Puts a particle after the head (<c>ParticleHead.Link</c>, <c>0x800a8864</c>). The sentinel is never linked.</summary>
    private void Link(Emitter e, short id)
    {
        if (id == Sentinel)
            return;
        ref var head = ref _particles[e.Head];
        ref var p = ref _particles[id];
        p.Next = head.Next;
        p.Previous = head.Id;
        head.Next = id;
        if (p.Next != -1)
            _particles[p.Next].Previous = id;
    }

    private void Unlink(int id)
    {
        ref var p = ref _particles[id];
        if (p.Previous != -1)
            _particles[p.Previous].Next = p.Next;
        if (p.Next != -1)
            _particles[p.Next].Previous = p.Previous;
        p.Next = p.Previous = -1;
    }

    /// <summary>
    /// Follows a Borg5 emitter's locators (<c>BorgParticleEmitterCalback</c>, <c>0x800a98a4</c>): its place, its
    /// locator's normal for flag 0x10 and its target's place for 0x2000. A missing locator puts it at the origin and ends it.
    /// </summary>
    private static void Place(ParticleHead head, Emitter e)
    {
        var record = e.Record!;
        if (!e.Anchor!.TryLocator(record.Locator, out var world))
        {
            e.Position = Vector3.Zero;
            e.Direction = Vector3.UnitY;
            e.TargetPosition = Vector3.One;
            e.Life = 0;
            return;
        }
        e.Position = world.Translation;
        if (record.Flags.HasFlag(ParticleEmitterFlags.LocatorNormal))
        {
            // The locator's +Y; the original divides it by the model's scale, and no emitter that leaves along it has a
            // speed, so its length never shows (SceneGetLocatorNorm, 0x8009edd0).
            var normal = Vector3.Normalize(new Vector3(world.M21, world.M22, world.M23));
            e.Direction = new Vector3(normal.X == 0 ? 1e-4f : normal.X, normal.Y == 0 ? 1e-4f : normal.Y, normal.Z == 0 ? 1e-4f : normal.Z);
        }
        if (record.Flags.HasFlag(ParticleEmitterFlags.Target) && e.Anchor.TryLocator(record.Target, out var target))
            e.TargetPosition = target.Translation;
    }

    /// <summary>
    /// A particle from a Borg5 emitter (<c>ParticleHead.SpawnFromRecord</c>, <c>0x800a99e8</c>), with every draw the
    /// original makes, even when the pool is full and the particle is dropped.
    /// </summary>
    private static void SpawnFromRecord(ParticleHead head, Emitter e)
    {
        var record = e.Record!;
        var id = head.AllocParticle(e, record.ParticleLifetime);
        var jitter = head.Direction(record.Flags.HasFlag(ParticleEmitterFlags.ExactSpawnJitter) ? record.SpawnJitter : head.FloatRange(0, record.SpawnJitter));
        var position = e.Position + jitter;
        var velocity = head.Direction(record.VelocityJitter) + e.Direction * record.Speed;
        velocity *= Units;
        var scale = head.FloatRange(record.MinScale, record.MaxScale);
        var color = new Vector4(
            head.FloatRange(Byte(record.MinColor.R), Byte(record.MaxColor.R)),
            head.FloatRange(Byte(record.MinColor.G), Byte(record.MaxColor.G)),
            head.FloatRange(Byte(record.MinColor.B), Byte(record.MaxColor.B)),
            head.FloatRange(Byte(record.MinColor.A), Byte(record.MaxColor.A)));
        var life = record.ParticleLifetime;
        if (record.Flags.HasFlag(ParticleEmitterFlags.Target))
        {
            // Aimed to reach the target, jittered, as it dies: a velocity added once.
            var aim = head.Direction(record.Flags.HasFlag(ParticleEmitterFlags.ExactTargetJitter) ? record.TargetJitter : head.FloatRange(0, record.TargetJitter));
            velocity += (aim + e.TargetPosition - position) * (float)(1.0 / life);
        }
        if (id == head.Sentinel)
            return;
        ref var p = ref head._particles[id];
        p.Position = position;
        p.Velocity = velocity;
        p.Scale = new Vector2(scale, scale);
        p.Color = color;
        if (record.Flags.HasFlag(ParticleEmitterFlags.Fade))
        {
            var fade = new Vector4(Byte(record.FadeColor.R), Byte(record.FadeColor.G), Byte(record.FadeColor.B), Byte(record.FadeColor.A));
            p.Vector = (fade - color) * (float)(1.0 / life);
            p.Target = p.Id;
            p.Flags |= ParticleFlags.AddColor;
        }
    }

    /// <summary>
    /// A particle's step (<c>ParticleHead.StepParticle</c>, <c>0x800a82fc</c>): it moves by its velocity, bounces off the
    /// emitter's floor, then applies its vector to its target or to the whole list.
    /// </summary>
    private void StepParticle(ref Particle p, Emitter e, float dt)
    {
        var vy = p.Velocity.Y;
        var y = p.Position.Y + vy * dt;
        p.Position = new Vector3(p.Position.X + p.Velocity.X * dt, y, p.Position.Z + p.Velocity.Z * dt);
        if (e.Flags.HasFlag(EmitterFlags.Bounces) && y < e.Floor)
        {
            p.Position.Y = y - vy;
            p.Velocity = new Vector3((float)(p.Velocity.X * Bounce), (float)(vy * -Bounce), (float)(p.Velocity.Z * Bounce));
        }
        if (p.Flags.HasFlag(ParticleFlags.Chain))
            ApplyVector(p.Vector, p.Flags, e.Head, dt);
        else if (p.Target != -1)
            ApplyVector(p.Vector, p.Flags, p.Target, dt);
    }

    /// <summary>
    /// A particle's vector added to its target, or to every particle from it on with <see cref="ParticleFlags.Chain"/>
    /// (<c>ParticleHead.ApplyVector</c>, <c>0x800a7fa0</c>), times the frame's ticks and, with
    /// <see cref="ParticleFlags.MultiplyByW"/>, its own W.
    /// </summary>
    private void ApplyVector(Vector4 v, ParticleFlags flags, int target, float dt)
    {
        for (int id = target; id != -1;)
        {
            ref var t = ref _particles[id];
            id = flags.HasFlag(ParticleFlags.Chain) ? t.Next : -1;
            var m = flags.HasFlag(ParticleFlags.MultiplyByW) ? v.W : 1;
            if (flags.HasFlag(ParticleFlags.AddToTarget))
                t.Vector += new Vector4(v.X * dt * m, v.Y * dt * m, v.Z * dt * m, v.W * dt * m);
            if (t.Flags.HasFlag(ParticleFlags.Unmodified))
                continue;
            if (flags.HasFlag(ParticleFlags.AddPosition))
                t.Position += new Vector3(v.X * dt * m, v.Y * dt * m, v.Z * dt * m);
            if (flags.HasFlag(ParticleFlags.AddVelocity))
                t.Velocity += new Vector3(v.X * dt * m, v.Y * dt * m, v.Z * dt * m);
            if (flags.HasFlag(ParticleFlags.AddColor))
                t.Color = Vector4.Clamp(t.Color + new Vector4(v.X * dt * m, v.Y * dt * m, v.Z * dt * m, v.W * dt * m), Vector4.Zero, Vector4.One);
            if (flags.HasFlag(ParticleFlags.AddScale))
                t.Scale = Vector2.Max(t.Scale + new Vector2(v.X * dt * m, v.Y * dt * m), Vector2.Zero);
        }
    }

    /// <summary>A number between two, in either order, or the first with no draw if they're equal (<c>ParticleHead.FloatRange</c>, <c>0x80093470</c>).</summary>
    public float FloatRange(float a, float b) => FloatRange(Random, a, b);

    /// <inheritdoc cref="FloatRange(float, float)"/>
    public static float FloatRange(IRandom random, float a, float b)
    {
        if (a == b)
            return a;
        var (low, high) = a <= b ? (a, b) : (b, a);
        return random.Float(1) * (high - low) + low;
    }

    /// <summary>
    /// A level vector <paramref name="length"/> long in a random direction, as X and Z (<c>ParticleHead.Direction2</c>,
    /// <c>0x800934dc</c>): two draws from −1 to 1, normalised, so a point in a square rather than a circle, which favours
    /// the diagonals.
    /// </summary>
    public static Vector2 Direction2(IRandom random, float length)
    {
        var x = FloatRange(random, -1, 1);
        var z = FloatRange(random, -1, 1);
        if (x == 0)
            x = 1e-6f;
        var v = new Vector2(x, z);
        return v / Math.Max(v.Length(), 1e-6f) * length;
    }

    /// <summary>
    /// A vector <paramref name="length"/> long in a random direction (<c>ParticleHead.Direction</c>, <c>0x8009358c</c>): three
    /// draws from −1 to 1, normalised, so not quite even over the sphere, as they're drawn in a cube.
    /// </summary>
    public Vector3 Direction(float length)
    {
        var x = FloatRange(-1, 1);
        var y = FloatRange(-1, 1);
        var z = FloatRange(-1, 1);
        if (x == 0)
            x = 1e-6f;
        var v = new Vector3(x, y, z);
        return v / Math.Max(v.Length(), 1e-6f) * length;
    }

    private static float Byte(byte b) => (float)(b / 255.0);

    private static Vector4 Scaled(Vector3 xyz, float w) => new(xyz * Units, w);

    /// <summary>
    /// The colour as the original sends it: darkened by the screen's fade, times <c>255.0f</c>, truncated and wrapped to a
    /// byte (<c>ParticleHead.Draw</c>, <c>0x800a9480</c>). A channel that has run negative wraps, as there; one can only
    /// by a fade's overshoot, and only just.
    /// </summary>
    internal Rgba Bytes(Vector4 c)
    {
        if (Brightness < 1)
            c *= Brightness;
        static byte B(float x) => ColorBytes.Single(x * 255f);
        return new Rgba(B(c.X), B(c.Y), B(c.Z), B(c.W));
    }
}
