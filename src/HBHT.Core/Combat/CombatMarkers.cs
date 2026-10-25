using System.Numerics;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Combat;

/// <summary>
/// The ring of teardrops round a combatant's field (<c>CombatMarkers</c>, <c>combatmarkers.cpp</c>, <c>0x800854A8</c>–
/// <c>0x800859CC</c>): a pool of 20 copies of <see cref="Scene"/>, of which 6, 16 or 20 stand round where the combatant
/// began its turn, each set on the ground. They're laid out as a turn begins (<see cref="Update"/>) and drawn every frame
/// but while an action plays (<see cref="Battle.ActionActive"/>). The scene's look, a blended billboard with a scrolling
/// texture, is its own; the code sets no scale, turn or scroll.
/// </summary>
public sealed class CombatMarkers
{
    /// <summary>The pool's size (<c>CombatMarkers.Init</c>).</summary>
    public const int Pool = 20;

    /// <summary>The teardrop.</summary>
    public static readonly RecordId Scene = RecordId.Hex(RecordTypes.Scene, 0x0794);

    /// <summary>The ring's radius up to which it has 6, and then 16 (<c>0x800D43C8</c>, <c>0x800D43CC</c>).</summary>
    public const float Few = 3, Some = 7;

    /// <summary>The sphere each is set down as, and the heights its sweep runs between (<c>CombatMarkers.RingPoint</c>).</summary>
    public const float Probe = 0.5f, Top = 100, Bottom = -100;

    /// <summary>The sweeps a teardrop's ground takes at most: the first, and four a little to the side.</summary>
    public const int Tries = 5;

    private readonly Vector3[] _points = new Vector3[Pool];

    /// <summary>How many stand round the field (<c>CombatMarkers.CountFor</c>).</summary>
    public int Count { get; private set; }

    /// <summary>Where each stands, the first <see cref="Count"/>, in the world's coordinates.</summary>
    public ReadOnlySpan<Vector3> Points => _points.AsSpan(0, Count);

    /// <summary>The ring's radius, in units: the field's, and the body's (twice for one the AI plays).</summary>
    public float RingRadius { get; private set; }

    /// <summary>How many teardrops a ring of radius <paramref name="radius"/> takes (<c>CombatMarkers.CountFor</c>, <c>0x8008580C</c>).</summary>
    public static int CountFor(float radius) => radius <= Few ? 6 : radius <= Some ? 16 : Pool;

    /// <summary>
    /// Lays the ring out round <paramref name="combatant"/> (<c>CombatMarkers.Update</c>, <c>0x80085548</c>): its radius
    /// worked out first (<see cref="Combatant.UpdateRadius"/>); the ring's radius the field's and the body's scaled radius,
    /// the body's again for one the AI plays; centred where it began its turn; the first teardrop due +Z and the rest round
    /// toward −X, evenly; each set on the ground (<see cref="Ground"/>), and one that finds none half way between its
    /// neighbours' heights.
    /// </summary>
    /// <remarks>
    /// The original keeps the way round in two statics it never resets, so each ring starts a hair further round than the
    /// last, by the float error of turning a full circle (a ten-thousandth a ring). The port starts each due +Z.
    /// </remarks>
    public void Update(Battle battle, Combatant combatant)
    {
        combatant.UpdateRadius(battle);
        var body = combatant.Body;
        var radius = combatant.Radius + body.ScaledRadius;
        if (combatant.Controlled)
            radius += body.ScaledRadius;
        RingRadius = radius;
        Count = CountFor(radius);
        var centre = battle.Origin + new Vector3(combatant.TurnStart.X, 0, combatant.TurnStart.Y);
        var step = 360f / Count * 0.017453f;
        var (cos, sin) = (MathF.Cos(step), MathF.Sin(step));
        var (dx, dz) = (0f, 1f);
        Span<bool> missed = stackalloc bool[Count];
        for (var i = 0; i < Count; i++)
        {
            var at = new Vector3(centre.X + dx * radius, 0, centre.Z + dz * radius);
            if (Ground(battle.World, at, battle.Game?.Random.Combat) is { } ground)
            {
                _points[i] = ground;
            }
            else
            {
                _points[i] = at with { Y = body.Sphere.Position.Y };
                missed[i] = true;
            }
            (dx, dz) = (cos * dx - sin * dz, cos * dz + sin * dx);
        }
        for (var i = 0; i < Count; i++)
        {
            if (!missed[i])
                continue;
            var before = _points[(i + Count - 1) % Count].Y;
            var after = _points[(i + 1) % Count].Y;
            _points[i].Y = before + (after - before) * 0.5f;
        }
    }

    /// <summary>
    /// Where a teardrop at <paramref name="at"/> stands on the ground (<c>CombatMarkers.RingPoint</c>, <c>0x80085240</c>):
    /// a sphere of <see cref="Probe"/> swept from <see cref="Top"/> to <see cref="Bottom"/>, past see-through triangles,
    /// rests on the first facing it, 0.01 above; on a miss it tries again from a little to the side of where it began, up
    /// to a quarter unit each way by two draws, four times. Null if every sweep misses.
    /// </summary>
    public static Vector3? Ground(CollisionWorld world, Vector3 at, IRandom? random)
    {
        for (var tries = 0; ; tries++)
        {
            var start = at;
            if (tries > 0 && random is not null)
            {
                var angle = random.Float(2 * MathF.PI);
                var reach = random.Float(0.25f);
                start.X += (MathF.Sin(angle) + 1) * 0.5f * reach;
                start.Z += (MathF.Cos(angle) + 1) * 0.5f * reach;
            }
            if (SpherePhysics.Sweep(world, start with { Y = Top }, start with { Y = Bottom }, Probe, skipSeeThrough: true, out var hit, out var normal))
                return new Vector3(hit.X + normal.X * Probe, hit.Y + 0.01f + normal.Y * Probe, hit.Z + normal.Z * Probe);
            if (tries >= Tries - 1 || random is null)
                return null;
        }
    }
}
