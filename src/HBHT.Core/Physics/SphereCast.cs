using System.Numerics;
using HBHT.Core.Chunks;
using HBHT.Plugins.Assets;
using Triangle = HBHT.Core.Physics.CollisionWorld.Triangle;

namespace HBHT.Core.Physics;

/// <summary>
/// A sphere swept through the world's triangles, faces, edges and corners alike: where along a move it first touches, what it
/// overlaps where it stands, and which trigger triangles it touches. The port's, for Modern movement
/// (docs/systems/movement.md#modern-movement). The original's own sphere physics, <see cref="SpherePhysics"/>, looks one step
/// ahead and pushes out after; this finds the moment of contact, so nothing is pushed through or bounced.
/// </summary>
/// <remarks>
/// A triangle is one-sided, as the original's are: one whose plane the sphere's centre is behind is left out. Passable triangles
/// are left out, and see-through ones for a sphere that passes through them. The sums are made relative to the sphere's centre,
/// so the Overworld's coordinates of up to 2,600 lose nothing to them.
/// </remarks>
public static class SphereCast
{
    /// <summary>Where a moving sphere first touches a triangle.</summary>
    /// <param name="Share">How much of the move it makes before it touches, 0 to 1.</param>
    /// <param name="Normal">The way from the point it touches to its centre: the face's normal, or off an edge or corner.</param>
    /// <param name="Face">The normal of the triangle it touches.</param>
    /// <param name="Surface">That triangle's surface.</param>
    /// <param name="OnFace">Whether it touched the face rather than an edge or a corner.</param>
    public readonly record struct Hit(float Share, Vector3 Normal, Vector3 Face, int Surface, bool OnFace);

    /// <summary>Two hits closer than this share of a move are at the same moment, as where triangles meet.</summary>
    private const float Tie = 1e-4f;

    [ThreadStatic] private static List<Triangle>? _near;

    /// <summary>
    /// Whether a sphere moving by <paramref name="delta"/> touches anything, and where it first does. Of hits at the same moment,
    /// as on an edge two triangles share, one <paramref name="preferred"/> says yes to is taken. With <paramref name="only"/>, it
    /// touches only what that says yes to.
    /// </summary>
    public static bool Cast(CollisionWorld world, Vector3 from, Vector3 delta, float radius, bool skipSeeThrough, Func<Hit, bool> preferred,
        out Hit hit, Func<Hit, bool>? only = null)
    {
        hit = default;
        var found = false;
        var near = Near(world, from, delta.Length() + radius);
        foreach (var t in near)
        {
            if (!Blocks(t, skipSeeThrough) || !Cast(t, from, delta, radius, out var h))
                continue;
            if (t.Surface is not (1 or 2) && PlainCopy(t, from + delta * h.Share - h.Normal * radius, near, skipSeeThrough) is { } plain)
                h = h with { Surface = plain };
            if ((only is null || only(h))
                && (!found || h.Share < hit.Share - Tie || (h.Share < hit.Share + Tie && !preferred(hit) && preferred(h))))
            {
                hit = h with { Share = found ? MathF.Min(h.Share, hit.Share) : h.Share };
                found = true;
            }
        }
        return found;
    }

    /// <summary>How near a plain triangle must lie to a point of another, in its plane and across it, to be that one's copy.</summary>
    private const float CopyNear = 0.05f;

    /// <summary>
    /// The surface of a plain collision copy (surfaces 1 and 2) of a triangle at <paramref name="point"/>, where chunks overlap, or
    /// null: P25's own collision has plain copies of the stone faces P24 reaches over its border with, and 1.0, colliding with
    /// the chunk the player is in alone, slides off them there, so a plain copy is taken over the other.
    /// </summary>
    private static int? PlainCopy(in Triangle t, Vector3 point, List<Triangle> near, bool skipSeeThrough)
    {
        foreach (var u in near)
        {
            if (u.Surface is (1 or 2) && u.Part != t.Part && Blocks(u, skipSeeThrough) && Vector3.Dot(u.Normal, t.Normal) > 0.98f
                && MathF.Abs(Vector3.Dot(point - u.A, u.Normal)) < CopyNear && Vector3.Distance(point, Closest(point, u)) < CopyNear)
                return u.Surface;
        }
        return null;
    }

    /// <summary>How deep into a triangle a sphere may be before it's pushed out: a rounding's worth, which a cast's moves leave.</summary>
    private const float Overlap = 1e-3f;

    /// <summary>
    /// Pushes a sphere out of what it overlaps by more than a rounding's worth, a few passes over the triangles about it, each out
    /// along the way from the triangle's nearest point to its centre till it just touches. Returns whether it moved it.
    /// </summary>
    public static bool PushOut(CollisionWorld world, ref Vector3 centre, float radius, bool skipSeeThrough)
    {
        var moved = false;
        for (var pass = 0; pass < 3; pass++)
        {
            var pushed = false;
            foreach (var t in Near(world, centre, radius))
            {
                if (!Blocks(t, skipSeeThrough) || Vector3.Dot(centre - t.A, t.Normal) < 0)
                    continue;
                var off = centre - Closest(centre, t);
                var distance = off.Length();
                if (distance >= radius - Overlap || distance < 1e-6f)
                    continue;
                centre += off / distance * (radius - distance);
                pushed = moved = true;
            }
            if (!pushed)
                break;
        }
        return moved;
    }

    /// <summary>
    /// The trigger triangles a sphere touches, within <paramref name="margin"/> of its surface, passable or not: the key of the
    /// part each is in and the object it runs, as the original's callback reports them (<c>check_trigger</c>).
    /// </summary>
    public static void Touched(CollisionWorld world, Vector3 centre, float radius, float margin, HashSet<(ChunkKey Part, int Target)> into)
    {
        foreach (var t in Near(world, centre, radius + margin))
        {
            if (t.Role != CollisionRole.Trigger || Vector3.Dot(centre - t.A, t.Normal) < -radius)
                continue;
            if (Vector3.Distance(centre, Closest(centre, t)) <= radius + margin)
                into.Add((t.Part, t.Target));
        }
    }

    private static bool Blocks(in Triangle t, bool skipSeeThrough) =>
        !t.Has(CollisionFlags.Passable) && !(skipSeeThrough && t.Has(CollisionFlags.SeeThrough));

    private static List<Triangle> Near(CollisionWorld world, Vector3 at, float reach)
    {
        reach += 0.5f;
        var near = _near ??= [];
        world.Near(at.X - reach, at.Z - reach, at.X + reach, at.Z + reach, near);
        return near;
    }

    /// <summary>A sphere at the origin moving by <paramref name="d"/> against one triangle, whose corners are relative to it.</summary>
    private static bool Cast(in Triangle t, Vector3 from, Vector3 d, float r, out Hit hit)
    {
        hit = default;
        var n = t.Normal;
        var a = t.A - from;
        var b = t.B - from;
        var c = t.C - from;
        // How far in front of the plane the centre is. Further behind it than the radius, the triangle is left out, as the
        // original's MovingPlane leaves it (0x800a4da4); behind it by less, only its edges and corners are touched, as the
        // original's are, so the end of a one-sided wall stops a sphere coming round it from behind.
        var height = -Vector3.Dot(a, n);
        if (height < -r)
            return false;
        var toward = Vector3.Dot(d, n);
        // Never touching the plane on this move: nothing of the triangle can be touched either.
        if (height - r > -toward)
            return false;

        var best = 2f;
        var normal = Vector3.Zero;
        var onFace = false;
        if (toward < 0 && height >= 0)
        {
            // The face: the moment the front of the sphere reaches the plane, if it does so within the triangle.
            var share = MathF.Max(0, (height - r) / -toward);
            if (share <= 1 && Inside(d * share - n * r, a, b, c, n))
                (best, normal, onFace) = (share, n, true);
        }
        if (!onFace)
        {
            Edge(a, b, d, r, ref best, ref normal);
            Edge(b, c, d, r, ref best, ref normal);
            Edge(c, a, d, r, ref best, ref normal);
            Corner(a, d, r, ref best, ref normal);
            Corner(b, d, r, ref best, ref normal);
            Corner(c, d, r, ref best, ref normal);
        }
        if (best > 1)
            return false;
        hit = new Hit(best, normal, n, t.Surface, onFace);
        return true;
    }

    /// <summary>The moment the sphere's surface reaches the edge from <paramref name="p"/> to <paramref name="q"/>, if sooner.</summary>
    private static void Edge(Vector3 p, Vector3 q, Vector3 d, float r, ref float best, ref Vector3 normal)
    {
        var edge = q - p;
        var length2 = edge.LengthSquared();
        var edgeD = Vector3.Dot(edge, d);
        var edgeP = Vector3.Dot(edge, p);
        var qa = length2 * -d.LengthSquared() + edgeD * edgeD;
        var qb = length2 * 2 * Vector3.Dot(d, p) - 2 * edgeD * edgeP;
        var qc = length2 * (r * r - p.LengthSquared()) + edgeP * edgeP;
        if (!Lowest(qa, qb, qc, best, out var share))
            return;
        var along = (edgeD * share - edgeP) / length2;
        if (along < 0 || along > 1)
            return;
        var centre = d * share;
        var off = centre - (p + edge * along);
        // Touching it already and moving along it or away isn't touching it.
        if (off.LengthSquared() < 1e-12f || Vector3.Dot(off, d) >= 0)
            return;
        best = share;
        normal = Vector3.Normalize(off);
    }

    /// <summary>The moment the sphere's surface reaches a corner, if sooner.</summary>
    private static void Corner(Vector3 p, Vector3 d, float r, ref float best, ref Vector3 normal)
    {
        var qa = d.LengthSquared();
        var qb = -2 * Vector3.Dot(d, p);
        var qc = p.LengthSquared() - r * r;
        if (!Lowest(qa, qb, qc, best, out var share))
            return;
        var off = d * share - p;
        if (off.LengthSquared() < 1e-12f || Vector3.Dot(off, d) >= 0)
            return;
        best = share;
        normal = Vector3.Normalize(off);
    }

    /// <summary>
    /// The lower root of <paramref name="qa"/> x² + <paramref name="qb"/> x + <paramref name="qc"/> in 0 to <paramref name="below"/>, where
    /// the sphere's surface first reaches what it's measured to while moving toward it.
    /// </summary>
    private static bool Lowest(float qa, float qb, float qc, float below, out float root)
    {
        root = 0;
        if (MathF.Abs(qa) < 1e-12f)
            return false;
        var discriminant = qb * qb - 4 * qa * qc;
        if (discriminant < 0)
            return false;
        var sqrt = MathF.Sqrt(discriminant);
        var r1 = (-qb - sqrt) / (2 * qa);
        var r2 = (-qb + sqrt) / (2 * qa);
        root = MathF.Min(r1, r2);
        // Overlapping it already, or past it: that's for pushing out, not touching.
        return root >= 0 && root < below && root <= 1;
    }

    /// <summary>Whether a point on the triangle's plane is inside it, its edges counting as inside.</summary>
    private static bool Inside(Vector3 point, Vector3 a, Vector3 b, Vector3 c, Vector3 n) =>
        Vector3.Dot(Vector3.Cross(b - a, point - a), n) >= 0
        && Vector3.Dot(Vector3.Cross(c - b, point - b), n) >= 0
        && Vector3.Dot(Vector3.Cross(a - c, point - c), n) >= 0;

    /// <summary>The point of a triangle nearest to <paramref name="p"/> (Ericson's, <i>Real-Time Collision Detection</i> 5.1.5).</summary>
    internal static Vector3 Closest(Vector3 p, in Triangle t)
    {
        var (a, b, c) = (t.A, t.B, t.C);
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0)
            return a;
        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3)
            return b;
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
            return a + ab * (d1 / (d1 - d3));
        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6)
            return c;
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
            return a + ac * (d2 / (d2 - d6));
        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
        var denominator = 1 / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }
}
