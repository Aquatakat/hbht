using System.Numerics;
using HBHT.Core.Particles;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

public class ParticleTests
{
    /// <summary>An actor whose locator 0 is at a place, and whose other locators are missing.</summary>
    private sealed class Anchor(Vector3 at) : IParticleAnchor
    {
        public bool Alive { get; set; } = true;

        public Vector3 At { get; set; } = at;

        public Vector3? Target { get; set; }

        public bool TryLocator(ParticleLocator locator, out Matrix4x4 world)
        {
            world = Matrix4x4.CreateTranslation(locator.Index == 1 && Target is { } t ? t : At);
            return locator.Index == 0 || (locator.Index == 1 && Target is not null);
        }
    }

    private static readonly Rgba White = new(255, 255, 255, 255);

    /// <summary>An emitter at locator 0 with no jitter, no speed and no acceleration, one particle a frame.</summary>
    private static ParticleEmitter Emitter(int lifetime = 20, int particleLifetime = 10, int delay = 0, int interval = 1, int burst = 0,
        ParticleEmitterFlags flags = ParticleEmitterFlags.ActorRequest) => new(
        new ParticleLocator(0, 0), new ParticleLocator(1, 0), 5, flags, lifetime, particleLifetime, delay, interval, burst,
        RecordId.Hex(RecordTypes.Texture, 11), 13, 0, Vector3.Zero, 0, Vector3.Zero, 0, 0,
        Vector3.Zero, 0, Vector3.Zero, 0, Vector3.Zero, 0, Vector3.Zero, 0, Vector3.Zero, 0, 1, 1, 1, White, White, White, 0);

    [Fact]
    public void ABurstWaitsOutTheDelayUndrawnThenEachParticleShowsFromItsSecondFrame()
    {
        var head = new ParticleHead(new Lcg());
        Assert.True(head.Start(Emitter(delay: 4, burst: 3, interval: 0), new Anchor(new Vector3(1, 2, 3))));
        // The head and three particles, held through the delay's two frames.
        Assert.Equal(4, head.ParticlesInUse);
        head.Frame(2);
        head.Frame(2);
        Assert.Empty(head.Sprites);
        // Their first step is never drawn, their second is.
        head.Frame(2);
        Assert.Empty(head.Sprites);
        head.Frame(2);
        Assert.Equal(3, head.Sprites.Count);
        Assert.All(head.Sprites, s => Assert.Equal(new Vector3(1, 2, 3), s.Position));
        Assert.All(head.Sprites, s => Assert.Equal(new Vector2(ParticleHead.SquareSide), s.Size));
    }

    [Fact]
    public void APositiveIntervalCountsDownByTheFrameAndANegativeOneSpawnsThatManyAFrame()
    {
        // At 2 ticks a frame an interval of 3 spawns on frames 1, 4, 7: the counter goes 3, 1, −1, then spawns.
        var head = new ParticleHead(new Lcg());
        head.Start(Emitter(lifetime: 100, particleLifetime: 100, interval: 3), new Anchor(Vector3.Zero));
        var counts = new List<int>();
        for (var i = 0; i < 8; i++)
        {
            head.Frame(2);
            counts.Add(head.ParticlesInUse - 1);
        }
        Assert.Equal([1, 1, 1, 2, 2, 2, 3, 3], counts);

        var many = new ParticleHead(new Lcg());
        many.Start(Emitter(lifetime: 100, particleLifetime: 100, interval: -3), new Anchor(Vector3.Zero));
        many.Frame(2);
        many.Frame(2);
        Assert.Equal(6, many.ParticlesInUse - 1);
    }

    [Fact]
    public void AnEmitterEndsWithItsLifetimeAndIsFreedOnceItsParticlesHaveDied()
    {
        var head = new ParticleHead(new Lcg());
        head.Start(Emitter(lifetime: 4, particleLifetime: 6), new Anchor(Vector3.Zero));
        // Spawns on its first frame only: the second takes its life to 0.
        head.Frame(2);
        head.Frame(2);
        Assert.Equal((1, 2), (head.EmittersInUse, head.ParticlesInUse));
        // The particle's life runs 4, 2, 0: it dies on the third frame, and the emitter, waiting for it, goes after it.
        head.Frame(2);
        Assert.Equal((0, 0), (head.EmittersInUse, head.ParticlesInUse));
    }

    [Fact]
    public void StoppingOrFreeingTheActorTakesItsParticlesAtOnce()
    {
        var head = new ParticleHead(new Lcg());
        var anchor = new Anchor(Vector3.Zero);
        head.Start(Emitter(lifetime: 100, particleLifetime: 100), anchor);
        head.Start(Emitter(lifetime: 100, particleLifetime: 100), new Anchor(Vector3.One));
        head.Frame(2);
        head.Stop(anchor);
        Assert.Equal((1, 2), (head.EmittersInUse, head.ParticlesInUse));
        anchor.Alive = false;
        var other = new Anchor(Vector3.One);
        head.Start(Emitter(lifetime: 100, particleLifetime: 100), other);
        other.Alive = false;
        head.Frame(2);
        Assert.Equal(1, head.EmittersInUse);
    }

    [Fact]
    public void AFullPoolDropsNewParticlesAndRefusesNewEmitters()
    {
        var head = new ParticleHead(new Lcg(), maxParticles: 4);
        head.Start(Emitter(lifetime: 100, particleLifetime: 100, interval: -2), new Anchor(Vector3.Zero));
        for (var i = 0; i < 3; i++)
            head.Frame(2);
        Assert.Equal(4, head.ParticlesInUse);
        Assert.False(head.Start(Emitter(), new Anchor(Vector3.Zero)));
        Assert.Equal(3, head.Sprites.Count);

        var full = new ParticleHead(new Lcg());
        for (var i = 0; i < ParticleHead.MaxEmitters; i++)
            Assert.True(full.Start(Emitter(), new Anchor(Vector3.Zero)));
        Assert.False(full.Start(Emitter(), new Anchor(Vector3.Zero)));
    }

    [Fact]
    public void TheAccelerationAddsToEveryParticlesVelocityEachFrameBeforeItMoves()
    {
        // Gravity of (0, −1, 0) × 0.006 × W 0.5 = 0.003 a tick², at 2 ticks a frame.
        var head = new ParticleHead(new Lcg());
        var record = Emitter(lifetime: 100, particleLifetime: 100, interval: 0, burst: 1) with { Acceleration = new Vector3(0, -1, 0), AccelerationScale = 0.5f };
        head.Start(record, new Anchor(new Vector3(0, 10, 0)));
        head.Frame(2);
        head.Frame(2);
        head.Frame(2);
        // Velocity −0.006, −0.012 then −0.018 a tick: down 0.012, 0.024 then 0.036. It's first drawn on the second frame.
        Assert.Equal(10 - 0.072f, head.Sprites.Single().Position.Y, 5);
        Assert.Equal(10 - 0.036f, head.Sprites.Single().Previous.Y, 5);
    }

    [Fact]
    public void TheAccelerationsChangeAddsToItForTheEmittersLifetime()
    {
        var head = new ParticleHead(new Lcg());
        var record = Emitter(lifetime: 4, particleLifetime: 100, interval: 0, burst: 1,
            flags: ParticleEmitterFlags.ActorRequest | ParticleEmitterFlags.AccelerationChanges) with
        {
            Acceleration = new Vector3(0, 1, 0), AccelerationScale = 1, AccelerationChange = new Vector3(0, 1, 0), AccelerationChangeScale = 0,
        };
        head.Start(record, new Anchor(Vector3.Zero));
        // The head's acceleration is 0.006, then the change (last in the list) makes it 0.018, then 0.030, and then
        // the change dies with the emitter's lifetime.
        var ys = new List<float>();
        for (var i = 0; i < 5; i++)
        {
            head.Frame(2);
            ys.Add(head.Sprites.Count > 0 ? head.Sprites[0].Position.Y : float.NaN);
        }
        // Velocity a tick: 0.012, 0.048, 0.108, 0.168; each frame moves it twice that.
        Assert.Equal(2 * (0.012f + 0.048f), ys[1], 4);
        Assert.Equal(2 * (0.012f + 0.048f + 0.108f + 0.168f), ys[3], 4);
        Assert.Equal(2 * (0.012f + 0.048f + 0.108f + 0.168f + 0.228f), ys[4], 4);
    }

    [Fact]
    public void ParticlesBounceOffHeightZeroKeepingSixTenthsOfTheirSpeed()
    {
        var head = new ParticleHead(new Lcg());
        var record = Emitter(lifetime: 100, particleLifetime: 100, interval: 0, burst: 1,
            flags: ParticleEmitterFlags.ActorRequest | ParticleEmitterFlags.BounceAtZero) with { Direction = new Vector3(0, -1, 0), Speed = 50 };
        head.Start(record, new Anchor(new Vector3(0, 0.5f, 0)));
        // 0.3 a tick down: 0.5 → −0.1, which the bounce puts back up by the velocity, to 0.2, now rising at 0.18.
        head.Frame(2);
        head.Frame(2);
        var y = head.Sprites.Single().Position.Y;
        Assert.Equal(0.2f + 2 * 0.18f, y, 4);
    }

    [Fact]
    public void FadingAndScalingReachTheirEndAsTheParticleDies()
    {
        var head = new ParticleHead(new Lcg());
        var record = Emitter(lifetime: 100, particleLifetime: 10, interval: 0, burst: 1,
            flags: ParticleEmitterFlags.ActorRequest | ParticleEmitterFlags.Fade | ParticleEmitterFlags.Scales) with
        {
            MinColor = new Rgba(255, 0, 0, 255), MaxColor = new Rgba(255, 0, 0, 255), FadeColor = new Rgba(0, 0, 255, 0),
            MinScale = 1, MaxScale = 1, EndScale = 2,
        };
        head.Start(record, new Anchor(Vector3.Zero));
        var sprites = new List<ParticleSprite>();
        for (var i = 0; i < 5; i++)
        {
            head.Frame(2);
            sprites.AddRange(head.Sprites);
        }
        // Drawn at lives 6, 4 and 2: the colour has faded 4, 6 and 8 tenths of the way, before it's drawn. The scale
        // changes after the draw, so it's a frame behind: 1.2 at its first.
        Assert.Equal([4, 6, 8], sprites.Select(s => (int)Math.Round((255 - s.Color.R) / 25.5f)));
        Assert.Equal(new Rgba(51, 0, 204, 51), sprites[2].Color);
        Assert.Equal(1.2f * ParticleHead.SquareSide, sprites[0].Size.X, 4);
    }

    [Fact]
    public void TargetedParticlesAreAimedToReachTheTargetAsTheyDie()
    {
        var head = new ParticleHead(new Lcg());
        var record = Emitter(lifetime: 100, particleLifetime: 10, interval: 0, burst: 1,
            flags: ParticleEmitterFlags.ActorRequest | ParticleEmitterFlags.Target);
        head.Start(record, new Anchor(Vector3.Zero) { Target = new Vector3(10, 0, 0) });
        var xs = new List<float>();
        for (var i = 0; i < 4; i++)
        {
            head.Frame(2);
            xs.AddRange(head.Sprites.Select(s => s.Position.X));
        }
        Assert.Equal([4f, 6f, 8f], xs.Select(x => MathF.Round(x, 4)));
    }

    [Fact]
    public void TheScreensFadeDarkensColourAndAlpha()
    {
        var head = new ParticleHead(new Lcg()) { Brightness = 0.5f };
        head.Start(Emitter(burst: 1, interval: 0), new Anchor(Vector3.Zero));
        head.Frame(2);
        head.Frame(2);
        Assert.Equal(new Rgba(127, 127, 127, 127), head.Sprites.Single().Color);
    }

    [Fact]
    public void AnActorStartsItsEmittersForTheRequestOnEveryEntry()
    {
        var head = new ParticleHead(new Lcg());
        var emitters = new EmittersRecord(RecordId.Hex(RecordTypes.Emitters, 0x2BCB),
            [Emitter(lifetime: 100), Emitter(lifetime: 100) with { Request = 0 }, Emitter(lifetime: 100) with { Flags = 0 }]);
        var actor = new ActorParticles(head, emitters, new Anchor(Vector3.Zero));
        actor.Entered(5);
        Assert.Equal(1, head.EmittersInUse);
        actor.Entered(5);
        Assert.Equal(2, head.EmittersInUse);
        actor.Entered(0);
        Assert.Equal(3, head.EmittersInUse);
        actor.Stop();
        Assert.Equal(0, head.EmittersInUse);
    }

    [Fact]
    public void AnActorsParticlesShowAndFadeWithItButGoOnSteppingWhileItIsHidden()
    {
        var head = new ParticleHead(new Lcg());
        var emitters = new EmittersRecord(RecordId.Hex(RecordTypes.Emitters, 0x2BCB), [Emitter(lifetime: 100, particleLifetime: 100, interval: -1)]);
        var actor = new ActorParticles(head, emitters, new Anchor(Vector3.Zero));
        // A wanderer 25 from the camera's aim isn't drawn (trek's 20, Movement.BodyShadow), so neither are its particles.
        actor.Alpha = Movement.BodyShadow.BodyAlpha(Movement.BodyShadow.TrekDistance, 25) ?? 0;
        actor.Entered(5);
        head.Frame(2);
        head.Frame(2);
        head.Frame(2);
        Assert.Empty(head.Sprites);
        // They go on spawning and ageing unseen: one a frame.
        Assert.Equal(3, head.ParticlesInUse - 1);
        // Fading in over the last 2 units of its distance, at 19 it's half drawn, and they're drawn at its alpha.
        actor.Alpha = Movement.BodyShadow.BodyAlpha(Movement.BodyShadow.TrekDistance, 19) ?? 0;
        Assert.Equal(127, actor.Alpha);
        head.Frame(2);
        Assert.Equal(3, head.Sprites.Count);
        Assert.All(head.Sprites, s => Assert.Equal(new Rgba(255, 255, 255, 127), s.Color));
        // Whole, they're whole, and the ones drawn while hidden glide on from where they were.
        actor.Alpha = 0xFF;
        head.Frame(2);
        Assert.Equal(4, head.Sprites.Count);
        Assert.All(head.Sprites, s => Assert.Equal(White, s.Color));
        // Particles of no actor's are always drawn.
        var code = new ParticleHead(new Lcg());
        code.Start(Emitter(burst: 1, interval: 0), new Anchor(Vector3.Zero));
        code.Frame(2);
        code.Frame(2);
        Assert.Single(code.Sprites);
    }

    [Fact]
    public void AMissingLocatorEndsTheEmitter()
    {
        var head = new ParticleHead(new Lcg());
        head.Start(Emitter(lifetime: 100) with { Locator = new ParticleLocator(5, 0) }, new Anchor(Vector3.Zero));
        head.Frame(2);
        head.Frame(2);
        Assert.Equal(0, head.EmittersInUse);
    }

    [Fact]
    public void CodeMakesItsOwnEmittersAndMovesParticlesBetweenThem()
    {
        // The weather's way: drops falling by the head's vector, which land into a second emitter, laid flat.
        var head = new ParticleHead(new Lcg());
        var splashes = head.AllocEmitter(300, RecordId.Hex(RecordTypes.Texture, 12), 0)!;
        var drops = head.AllocEmitter(300, RecordId.Hex(RecordTypes.Texture, 2), 1,
            spawn: (h, e) =>
            {
                var id = h.AllocParticle(e, 100);
                h[id].Position = new Vector3(0, 1, 0);
                h[id].Color = Vector4.One;
            },
            step: (ParticleHead h, Emitter e, ref Particle p) =>
            {
                if (p.Position.Y > 0)
                    return;
                p.Flags |= ParticleFlags.OwnOrientation;
                p.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2);
                h.MoveParticle(p.Id, splashes);
            })!;
        ref var fall = ref head[drops.Head];
        fall.Flags |= ParticleFlags.AddPosition;
        fall.Vector = new Vector4(0, -0.25f, 0, 0);
        for (var i = 0; i < 4; i++)
            head.Frame(2);
        // An interval of 1 spawns every other frame at 2 ticks a frame. Each drop falls 0.5 a frame, to 0 on its second,
        // when it lands into the splashes' list.
        Assert.Contains(head.Sprites, s => s.Emitter == splashes.Slot && s.Orientation is not null);
        Assert.Contains(head.Sprites, s => s.Emitter == drops.Slot && s.Orientation is null);
        // Steering the fall: the head's vector, set each frame.
        head[drops.Head].Vector = Vector4.Zero;
        head.Frame(2);
        head.Frame(2);
        Assert.Contains(head.Sprites, s => s.Emitter == drops.Slot);
        Assert.All(head.Sprites.Where(s => s.Emitter == drops.Slot), s => Assert.Equal(s.Previous, s.Position));
    }

    [Fact]
    public void SpawningDrawsFromItsStreamInTheOriginalsOrder()
    {
        // Spawn jitter's length and direction (4), the velocity's direction (3), the scale (1) and the colour (4).
        var values = Enumerable.Range(1, 12).Select(i => (uint)i << 28).ToArray();
        var random = new ScriptedRandom(values);
        var head = new ParticleHead(random);
        var record = Emitter(interval: 0, burst: 1) with
        {
            SpawnJitter = 1, VelocityJitter = 1, MinScale = 0, MaxScale = 2,
            MinColor = new Rgba(0, 0, 0, 0), MaxColor = new Rgba(255, 255, 255, 255),
        };
        head.Start(record, new Anchor(Vector3.Zero));
        Assert.Equal(0, random.Remaining);
        head.Frame(2);
        head.Frame(2);
        var s = head.Sprites.Single();
        // Scale draw 8/16 of 0–2; colour draws 9 to 12 sixteenths.
        Assert.Equal(1 * ParticleHead.SquareSide, s.Size.X, 4);
        Assert.Equal(new Rgba(143, 159, 175, 191), s.Color);
    }
}
