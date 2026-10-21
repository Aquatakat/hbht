using System.Numerics;
using Aidyn.Core.Calendar;
using Aidyn.Core.Chunks;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Core.Scenes;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Particles;

/// <summary>
/// Rain and snow, as the original draws them: two emitters of the particle pool, one of drops that fall from a disc
/// above and ahead of the camera's aim and one of the splashes they become as they land, which <c>ProcessWeather</c>
/// (<c>0x8001e7c0</c> in 1.0) makes and steers by what's falling and how hard (<see cref="Weather.FallIntensity"/>).
/// Each drop casts its fall against the solid surfaces the loaded chunks draw as it's made (<see cref="VisibleWorld"/>, the
/// user's choice: the original's sweeps the player's chunk's collision), and lands where that says, laid flat on the ground as
/// a ripple that shrinks and fades, or ends without one against a wall or in what absorbs it, such as water or a canopy;
/// rain also splashes about the drops of itself. See
/// docs/systems/weather.md.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps one pointer to the pair (<c>Weather.Data</c> + 0x14), which decides the rain sky as well as the
/// particles. <see cref="Weather.Draw"/> holds that decision (<see cref="Weather.Emitting"/>) and makes, steers, keeps,
/// forgets and frees the pair through this, in <c>ProcessWeather</c>'s order, every tick, as its rates are per tick. The
/// drops' draws come from the cosmetic stream, where the original takes them from its one stream (ADR 0006).
/// </para>
/// <para>
/// <see cref="Density"/> is the port's: more drops from each spawn, and room for them in the pool, with the splashes held
/// to <see cref="SplashCap"/> times the original's. The pair spawns by frames of the console's 20 fps
/// (<see cref="Cadence.PrecipitationFrameTicks"/>), where the pool's others spawn by its 30.
/// </para>
/// </remarks>
public sealed class WeatherParticles : IPrecipitationEmitters
{
    // ProcessWeather's literals (0x800ce0c8–0x800ce0f0).

    /// <summary>The drops fall from within this of their centre (the emitter's <c>+0x54</c>, 20 at <c>0x800ce0e4</c>), set each frame.</summary>
    public const float Spread = 20;

    /// <summary>Their centre is this far ahead of the camera's aim along its level look (<c>0x800ce0e8</c>).</summary>
    public const float Ahead = 5;

    /// <summary>And 7 − (I − 2) above it (<c>0x800ce0f0</c>, <c>0x800ce0ec</c>): 9 − I. It starts at 7 above the aim.</summary>
    public const float Above = 7, AboveLess = 2;

    /// <summary>The emitters' lives as they're made, and as they're kept up every frame while it falls.</summary>
    public const int StartLife = 60, KeptLife = 300;

    // The drops' (WeatherParticles.SpawnDrop, 0x8001e444).

    /// <summary>A drop's scale (<c>0x800ce0b0</c>): 0.177 across.</summary>
    public const float DropScale = 0.25f;

    /// <summary>The nearest to their centre that drops fall (<c>0x3C23D70A</c>).</summary>
    public const float NearestSpawn = 0.01f;

    /// <summary>
    /// The least a surface's normal rises, as the cosine of its angle from straight up, for a drop to splash on it, the
    /// port's: within 60° of up. A drop that lands on anything steeper, a wall or a trunk, ends there without one. The
    /// original splashes on whatever its sweep finds.
    /// </summary>
    public const float SplashSlope = 0.5f;

    /// <summary>A drop fades by this share of its colour over its life, alpha too (−0.75 at <c>0x800ce0c0</c>), to a quarter of it.</summary>
    public const double Fade = 0.75;

    /// <summary>Where a drop that hits nothing aims to land, under the world (<c>0xC47A0000</c>), and its first nearest, squared (<c>0x800ce0b4</c>).</summary>
    public const float NowhereY = -1000, FarSquared = 100000;

    /// <summary>Past where it would land, a drop looks for the ground from this far above it to this far below (<c>WeatherParticles.DropStep</c>, <c>0x800ce094</c>).</summary>
    public const float LandingReach = 5;

    // The splashes' (WeatherParticles.MakeSplash, 0x8001e04c, and WeatherParticles.SpawnSplash, 0x8001e274).

    /// <summary>A splash sits this far along the ground's normal (<c>0x3D4CCCCD</c>).</summary>
    public const float SplashLift = 0.05f;

    /// <summary>A splash is this times a drop's scale (<c>0x40400000</c>): 0.53 across.</summary>
    public const float SplashGrowth = 3;

    /// <summary>A splash lives this many ticks, fading to black over them (<c>0x78</c>).</summary>
    public const int SplashLife = 120;

    /// <summary>
    /// What the splashes' emitter takes off their scale a tick (<c>0x800ce0c8</c>, −1/120), so a splash shrinks to nothing
    /// at 90 ticks; and the share of its colour it fades by a tick.
    /// </summary>
    public const float SplashShrink = -1f / 120;

    /// <summary>Rain's own splashes land within half the drops' spread (<c>0x800ce098</c>), found from this far above their centre to this far below (<c>0x800ce09c</c>).</summary>
    public const float AmbientShare = 0.5f, AmbientReach = 50;

    /// <summary>
    /// The most splashes, of the drops and of their own together, that any <see cref="Density"/> makes, as a multiple of
    /// the original's: the port's. Past it, rain's own splashes stop growing with the density and only a share of the
    /// drops that land splash, so that the drops keep their density without the ground carpeted in ripples.
    /// </summary>
    public const float SplashCap = 1.5f;

    /// <summary>The original's particles, which <see cref="Density"/> multiplies for the pool.</summary>
    public const int OriginalPool = ParticleHead.DefaultMaxParticles;

    /// <summary>The densest the options menu offers, which a pool made for the weather (<see cref="PoolCapacity"/>) has room for.</summary>
    public const int MaxDensity = 8;

    /// <summary>A pool with room for the rain at <see cref="MaxDensity"/>: the original's 384 that many times.</summary>
    public const int PoolCapacity = OriginalPool * MaxDensity;

    private readonly ParticleHead _head;
    private readonly IRandom _random;
    private readonly WeatherRecord _rules;
    private readonly Func<Vector3> _aim;
    private readonly Func<Vector2> _look;
    private readonly Func<VisibleWorld?> _world;
    private readonly Func<ChunkKey?> _part;
    private readonly List<Pair> _pairs = [];
    private float _density = 1;

    /// <param name="head">The particle pool.</param>
    /// <param name="rules">The weather's rules, with how rain and snow look.</param>
    /// <param name="random">The drops' stream: the cosmetic one.</param>
    /// <param name="aim">The camera's aim, which the drops fall about (<c>Camera.Listener</c> + 0xC, <c>0x800d82a8</c>), read as each spawns.</param>
    /// <param name="look">The camera's look in X and Z, a unit vector (<c>FollowCamera.LookXZ</c>, <c>0x800d82cc</c>), read as the pair is steered.</param>
    /// <param name="world">The solid surfaces the loaded chunks draw.</param>
    /// <param name="part">
    /// The key of one chunk whose surfaces alone the drops land on, as the original tests the player's chunk's collision
    /// (<c>Zones.PlayerMap</c>, <c>0x800d8288</c>); null, trek's, tests every loaded chunk's, the user's choice.
    /// </param>
    public WeatherParticles(ParticleHead head, WeatherRecord rules, IRandom random, Func<Vector3> aim, Func<Vector2> look,
        Func<VisibleWorld?> world, Func<ChunkKey?>? part = null)
    {
        _head = head;
        _rules = rules;
        _random = random;
        _aim = aim;
        _look = look;
        _world = world;
        _part = part ?? (() => null);
    }

    /// <summary>
    /// How many drops each spawn makes, a multiple of the original's 1: the port's setting. Rain's own splashes are
    /// as many times the original's up to <see cref="SplashCap"/>, and past it each drop that lands splashes by a share
    /// of <see cref="SplashCap"/> over this, so the splashes stay at that many times the original's. The pool grows to
    /// the original's 384 times it, as far as its capacity, so the rain isn't capped where the original's would be half
    /// as dense. A fraction carries over to the next spawn, and to the next drop that lands.
    /// </summary>
    public float Density
    {
        get => _density;
        set
        {
            _density = Math.Max(value, 0);
            _head.MaxParticles = (int)(OriginalPool * Math.Max(_density, 1));
        }
    }

    /// <summary>How many drops the pairs have made.</summary>
    public long DropsMade { get; private set; }

    /// <summary>How many of the drops have landed as splashes.</summary>
    public long DropSplashes { get; private set; }

    /// <summary>How many splashes rain has made of its own.</summary>
    public long OwnSplashes { get; private set; }

    /// <summary>The share of the drops that land that splash: 1, or past <see cref="SplashCap"/> that over the density.</summary>
    public float SplashShare => _density > SplashCap ? SplashCap / _density : 1;

    /// <summary>The drops' emitter now steered (<c>Weather.Data</c> + 0x14), or null.</summary>
    public Emitter? Drops => Current?.Drops;

    /// <summary>The splashes' emitter now steered (the drops' emitter's <c>+0x1C8</c>), or null.</summary>
    public Emitter? Splashes => Current?.Splashes;

    /// <summary>The pair now steered, which <see cref="Weather.Emitting"/> says is up, or null for none or one with nothing to draw.</summary>
    private Pair? Current { get; set; }

    /// <summary>
    /// Makes a pair for what falls (<see cref="Make(PrecipitationLook)"/>), when <see cref="Weather.Draw"/> finds none
    /// up. Without room it's tried again the next tick. What a plugin gives no look draws nothing, and counts as made.
    /// </summary>
    public bool Make(Precipitation falling)
    {
        if (Look(_rules, falling) is not { } look)
        {
            Current = null;
            return true;
        }
        Current = Make(look);
        return Current is not null;
    }

    /// <summary>
    /// Steers the pair, every tick it's up (<c>ProcessWeather</c>): its centre <see cref="Ahead"/> in front of the
    /// camera's aim along its look and 9 − I above it, the fall the wind and the precipitation's, and the intervals
    /// and the drops' lives by how hard it falls, I, before this tick's change.
    /// </summary>
    public void Steer(Precipitation falling, float intensity, Vector3 wind)
    {
        // Something else has freed it: nothing to steer (the original would write into a freed emitter).
        if (Current is not { } pair || !pair.Ours())
            return;
        var look = _look();
        pair.Spread = Spread;
        pair.Offset = new Vector3(look.X * Ahead, Above - (intensity - AboveLess), look.Y * Ahead);
        var rules = pair.Look;
        ref var head = ref _head[pair.Drops.Head];
        var y = (float)(wind.Y - ((double)intensity * rules.FallPerIntensity + rules.Fall));
        head.Vector = new Vector4(wind.X, y, wind.Z, head.Vector.W);
        var interval = (short)(int)(rules.Interval + intensity * rules.IntervalPerIntensity);
        pair.Drops.Interval = interval;
        pair.Life = (short)(int)(rules.Life + intensity * rules.LifePerIntensity);
        if (rules.Splashes && pair.Splashes is { } splashes)
            splashes.Interval = (short)(interval - rules.SplashesSooner);
    }

    /// <summary>Whether a pair made while it's already falling fills the air at once (<see cref="FillAir"/>); off, as the original.</summary>
    public bool FillsAir { get; set; } = true;

    /// <summary>
    /// Fills the air at once (<see cref="IPrecipitationEmitters.FillAir"/>): the new pair alone is run on for as long
    /// as a drop lives, frame by frame, kept up as it falls, so drops are at every height and splashes on the ground
    /// before it's first drawn. The other emitters don't move.
    /// </summary>
    public void FillAir()
    {
        if (!FillsAir || Current is not { } pair || !pair.Ours())
            return;
        var splashes = pair.OwnsSplashes() ? pair.Splashes : null;
        for (var ticks = 0; ticks < pair.Life; ticks += Cadence.FrameTicks)
        {
            Keep();
            _head.Frame(Cadence.FrameTicks, e => e == pair.Drops || e == splashes);
        }
        Keep();
    }

    /// <summary>Keeps both emitters' lives at <see cref="KeptLife"/> while what falls is what the weather wants.</summary>
    public void Keep()
    {
        if (Current is not { } pair || !pair.Ours())
            return;
        pair.Drops.Life = KeptLife;
        pair.Splashes?.Life = KeptLife;
    }

    /// <summary>
    /// The emitters are forgotten, left to run out (<see cref="Weather.Forget"/>): as the weather wants another to fall,
    /// and as a door's teleport forgets them before it kills every emitter still spawning (<c>TeleportPlayer</c>,
    /// <c>0x8000bb54</c>).
    /// </summary>
    public void Forget() => Current = null;

    /// <summary>Frees the pair at once, the splashes' first, where the sky isn't drawn (<c>Particles.KillEmitter</c>).</summary>
    public void Free()
    {
        if (Current is { } pair)
        {
            if (pair.OwnsSplashes())
                _head.Free(pair.Splashes!);
            if (pair.Ours())
                _head.Free(pair.Drops);
        }
        Current = null;
    }

    /// <summary>Frees every emitter this has made, and forgets them, as the zone engine's end frees the pool.</summary>
    public void Clear()
    {
        foreach (var pair in _pairs)
        {
            if (pair.OwnsSplashes())
                _head.Free(pair.Splashes!);
            if (pair.Ours())
                _head.Free(pair.Drops);
        }
        _pairs.Clear();
        Current = null;
    }

    private static PrecipitationLook? Look(WeatherRecord rules, Precipitation falling) => falling switch
    {
        Precipitation.Rain => rules.Rain,
        Precipitation.Snow => rules.Snow,
        _ => null,
    };

    /// <summary>
    /// The pair of emitters (<c>WeatherParticles.Make</c>, <c>0x8001e62c</c>): the splashes' first, its head shrinking every
    /// particle in it by <see cref="SplashShrink"/> a tick, spawning rain's own splashes; then the drops', its head moving
    /// every drop by the fall, spawning and landing drops. Without room for the second, the first is freed.
    /// </summary>
    private Pair? Make(PrecipitationLook look)
    {
        _pairs.RemoveAll(p => !p.Ours());
        var pair = new Pair(this, look);
        if (_head.AllocEmitter(StartLife, look.Splash, 0, spawn: look.Splashes ? pair.SpawnSplashes : null) is not { } splashes)
            return null;
        splashes.SpawnTicks = Cadence.PrecipitationFrameTicks;
        ref var a = ref _head[splashes.Head];
        a.Vector = new Vector4(SplashShrink, SplashShrink, SplashShrink, 0);
        a.Flags |= ParticleFlags.AddScale;
        if (_head.AllocEmitter(StartLife, look.Drop, 0, spawn: pair.SpawnDrops, step: pair.Land) is not { } drops)
        {
            _head.Free(splashes);
            return null;
        }
        drops.SpawnTicks = Cadence.PrecipitationFrameTicks;
        _head[drops.Head].Flags |= ParticleFlags.AddPosition;
        (pair.Drops, pair.Splashes) = (drops, splashes);
        _pairs.Add(pair);
        return pair;
    }

    /// <summary>A pair of emitters with what <c>ProcessWeather</c> keeps on the drops' emitter.</summary>
    private sealed class Pair
    {
        private readonly WeatherParticles _owner;
        private float _dropCarry, _splashCarry, _landCarry;

        public Pair(WeatherParticles owner, PrecipitationLook look)
        {
            _owner = owner;
            Look = look;
            Color = new Vector4(look.Color.R, look.Color.G, look.Color.B, look.Color.A);
            SpawnDrops = (_, _) => Spawn(ref _dropCarry, _owner._density, SpawnDrop);
            SpawnSplashes = (_, _) => Spawn(ref _splashCarry, Math.Min(_owner._density, SplashCap), SpawnSplash);
            Land = DropStep;
        }

        public PrecipitationLook Look { get; }

        public Emitter Drops = null!;

        /// <summary>The drops' emitter's <c>+0x1C8</c>, the splashes'.</summary>
        public Emitter? Splashes;

        /// <summary>The drops' centre from the camera's aim (<c>+0x30</c>).</summary>
        public Vector3 Offset = new(0, Above, 0);

        /// <summary>The drops' colour (<c>+0x44</c>).</summary>
        public Vector4 Color;

        /// <summary>How far from the centre drops fall (<c>+0x54</c>).</summary>
        public float Spread = WeatherParticles.Spread;

        /// <summary>A drop's life (<c>+0x1A</c>), which every frame sets before anything spawns.</summary>
        public short Life;

        public readonly EmitterCallback SpawnDrops, SpawnSplashes;
        public readonly ParticleCallback Land;

        /// <summary>Whether the drops' emitter is still this pair's, not freed and taken by another.</summary>
        public bool Ours() => Drops is not null && Drops.InUse && Drops.Spawn == SpawnDrops;

        public bool OwnsSplashes() => Splashes is { InUse: true } a && a.Step is null && (a.Spawn is null || a.Spawn == SpawnSplashes) && Ours();

        private ParticleHead Head => _owner._head;

        private static void Spawn(ref float carry, float density, Action one)
        {
            carry += density;
            for (; carry >= 1; carry--)
                one();
        }

        /// <summary>
        /// A drop (<c>WeatherParticles.SpawnDrop</c>, <c>0x8001e444</c>): somewhere from 0.01 to the spread from the centre, in a
        /// random level direction, at scale 0.25 in the colour, fading to a quarter of it over its life. It casts its
        /// fall for its life against the drawn surfaces, and keeps the nearest it hits, or a point under the world.
        /// </summary>
        private void SpawnDrop()
        {
            var head = Head;
            var id = head.AllocParticle(Drops, Life);
            ref var p = ref head[id];
            p.Scale = new Vector2(DropScale, DropScale);
            var reach = _owner._random.Between(NearestSpawn, Spread);
            var d = ParticleHead.Direction2(_owner._random, reach);
            var aim = _owner._aim();
            p.Position = new Vector3(aim.X + Offset.X + d.X, aim.Y + Offset.Y, aim.Z + Offset.Z + d.Y);
            var fall = head[Drops.Head].Vector;
            var end = new Vector3(fall.X, fall.Y, fall.Z) * Life + p.Position;
            p.Data = _owner._world() is { } world && world.Raycast(p.Position, end, out var hit, out _, out _, _owner._part())
                ? new Vector4(hit, FarSquared)
                : new Vector4(0, NowhereY, 0, FarSquared);
            p.Color = Color;
            p.Flags |= ParticleFlags.AddColor;
            p.Vector = p.Color * (float)(-Fade / Life);
            _owner.DropsMade++;
        }

        /// <summary>
        /// A drop's step (<c>WeatherParticles.DropStep</c>, <c>0x8001e138</c>): while it nears where it will land it notes how near;
        /// once it's past, each frame it looks for the ground from 5 above it to 5 below, and on finding it becomes a
        /// splash there, in the splashes' list. Past <see cref="SplashCap"/>, a drop the share leaves out ends there instead.
        /// </summary>
        /// <remarks>
        /// The port looks along the drop's own fall, from 5 back to 5 on, where the original looks straight down: so
        /// with the surfaces drawn, which have roofs and eaves, a drop that passed beside one never splashes on it. On
        /// a surface steeper than <see cref="SplashSlope"/>, or one that absorbs it (<see cref="DrawnTriangle.Absorbs"/>), it ends
        /// without a splash.
        /// </remarks>
        private void DropStep(ParticleHead head, Emitter e, ref Particle p)
        {
            var d = p.Position - new Vector3(p.Data.X, p.Data.Y, p.Data.Z);
            var squared = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
            if (squared < p.Data.W)
            {
                p.Data.W = squared;
                return;
            }
            var fall = head[e.Head].Vector;
            var way = new Vector3(fall.X, fall.Y, fall.Z);
            way = way == Vector3.Zero ? -Vector3.UnitY : Vector3.Normalize(way);
            if (_owner._world() is not { } world
                || !world.Raycast(p.Position - way * LandingReach, p.Position + way * LandingReach, out var point, out var normal, out var absorbs, _owner._part())
                || Splashes is not { } splashes)
                return;
            if (absorbs || normal.Y < SplashSlope)
            {
                head.FreeParticle(p.Id);
                return;
            }
            _landCarry += _owner.SplashShare;
            if (_landCarry < 1)
            {
                head.FreeParticle(p.Id);
                return;
            }
            _landCarry--;
            if (!head.MoveParticle(p.Id, splashes))
                return;
            MakeSplash(ref p, point, normal);
            _owner.DropSplashes++;
        }

        /// <summary>
        /// Rain's own splashes (<c>WeatherParticles.SpawnSplash</c>, <c>0x8001e274</c>): somewhere within half the drops' spread of
        /// their centre, the ground found from 50 above it to 50 below, and a splash there. The original's finds the first
        /// collision triangle, see-through ones and all; the port's the highest drawn surface, and none where that's steeper
        /// than <see cref="SplashSlope"/> or absorbs it.
        /// </summary>
        private void SpawnSplash()
        {
            var head = Head;
            var reach = _owner._random.Between(NearestSpawn, Spread * AmbientShare);
            var d = ParticleHead.Direction2(_owner._random, reach);
            var aim = _owner._aim();
            var x = aim.X + Offset.X + d.X;
            var y = aim.Y + Offset.Y;
            var z = aim.Z + Offset.Z + d.Y;
            if (_owner._world() is not { } world
                || !world.Raycast(new Vector3(x, y + AmbientReach, z), new Vector3(x, y - AmbientReach, z), out var point, out var normal, out var absorbs, _owner._part())
                || absorbs || normal.Y < SplashSlope)
                return;
            var id = head.AllocParticle(Splashes!, SplashLife);
            ref var p = ref head[id];
            p.Scale = new Vector2(DropScale, DropScale);
            p.Color = Color;
            p.Flags |= ParticleFlags.AddColor;
            p.Vector = p.Color * (float)(-Fade / Life);
            MakeSplash(ref p, point, normal);
            _owner.OwnSplashes++;
        }

        /// <summary>
        /// A particle laid on the ground as a splash (<c>WeatherParticles.MakeSplash</c>, <c>0x8001e04c</c>): 0.05 along the
        /// ground's normal, facing along it (<c>guAlign</c> of the normal, with 10⁻⁶ added to its X), three times its
        /// scale, fading from its colour to black over 120 ticks, which its life is.
        /// </summary>
        private static void MakeSplash(ref Particle p, Vector3 point, Vector3 normal)
        {
            p.Flags |= ParticleFlags.OwnOrientation;
            p.Orientation = Quaternion.CreateFromRotationMatrix(Billboard.Align(0, normal with { X = normal.X + 1e-6f }));
            p.Position = point + normal * SplashLift;
            p.Scale *= SplashGrowth;
            p.Vector = p.Color * SplashShrink;
            p.Life = SplashLife;
        }
    }
}
