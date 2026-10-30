using System.Numerics;
using HBHT.Core.Chunks;
using HBHT.Plugins.Assets;
using Triangle = HBHT.Core.Physics.CollisionWorld.Triangle;

namespace HBHT.Core.Physics;

/// <summary>
/// The original's sphere physics (<c>collide.cpp</c>, <c>collide2.cpp</c>, <c>physics.cpp</c>):
/// a sphere steps by its velocity, then is pushed out of each triangle it's in, and out of the
/// edges and corners the triangle marks as open. Walls stop it; a ledge lower than about its
/// radius lifts it, since the face below the sphere's centre misses and the top pushes it up.
/// </summary>
/// <remarks>
/// Checked against the 1.0 ROM's machine code where the decomp could be wrong. Some behaviour
/// that looks like a slip is the game's own and is kept: the edge test places the closest point
/// with the sweep's parameter rather than the edge's, a bounce off an edge or corner only turns
/// velocity that points away from it, and the tests look one step ahead along the velocity.
/// </remarks>
public static class SpherePhysics
{
    /// <summary>What the original treats as zero length (<c>NORMALIZE_MIN</c>).</summary>
    public const float Epsilon = 1e-6f;

    [ThreadStatic] private static List<Triangle>? _near;

    /// <summary>
    /// Every surface keeps no velocity on contact except 1 and 2, the plain "player and camera"
    /// and "player" collision, which keep all of it (<c>EnvPropBools</c>, <c>InitEnvProps</c>).
    /// </summary>
    public static float SurfaceKeep(int surface) => surface is 1 or 2 ? 1 : 0;

    /// <summary>
    /// Moves the sphere by its velocity for <paramref name="ticks"/> ticks, then collides it with
    /// the world once (<c>ProcessCollisionSphere</c>).
    /// </summary>
    public static void Step(CollisionWorld world, CollisionSphere s, int ticks = 1)
    {
        var start = s.Position;
        for (var i = 0; i < ticks; i++)
        {
            s.Velocity += s.Gravity;
            s.Position += s.Velocity;
        }

        var reach = new Vector2(s.Velocity.X, s.Velocity.Z).Length() + s.Radius + 1;
        var near = _near ??= [];
        world.Near(s.Position.X - reach, s.Position.Z - reach, s.Position.X + reach, s.Position.Z + reach, near);
        // The original counts contacts per grid cell and reads the count while colliding; with one
        // pass over the triangles, it's always zero until the end, as it is in the first cell.
        s.Touching = false;
        s.Trod.Clear();
        s.Touching = Collide(s, near);

        // Settle: a sphere that barely moved while touching stays where it was.
        var still = ticks * 0.006f;
        if (s.Touching && s.Velocity.Length() < still && Vector3.Distance(start, s.Position) < still)
        {
            s.Velocity = Vector3.Zero;
            s.Position = start;
        }
    }

    /// <summary>
    /// Whether a sphere moving from <paramref name="from"/> to <paramref name="to"/> hits a
    /// triangle facing it, and where its front touches (<c>CheckCollision</c>). The camera uses
    /// this, skipping see-through triangles. The original sweeps one chunk's collision; the port
    /// sweeps every loaded chunk's, or with <paramref name="part"/> the one added under that key.
    /// </summary>
    public static bool Sweep(CollisionWorld world, Vector3 from, Vector3 to, float radius, bool skipSeeThrough, out Vector3 point, out Vector3 normal,
        ChunkKey? part = null)
    {
        var delta = to - from;
        foreach (var t in Near(world, from, delta, radius, part))
        {
            if (skipSeeThrough && t.Has(CollisionFlags.SeeThrough))
                continue;
            if (Vector3.Dot(delta, t.Normal) <= 0 && SweepPlane(from, delta, radius, t, out point))
            {
                normal = t.Normal;
                return true;
            }
        }
        point = normal = Vector3.Zero;
        return false;
    }

    /// <summary>
    /// Like <see cref="Sweep"/>, but the triangle it touches soonest rather than the first in the
    /// grid's order, of those facing at least <paramref name="minUp"/> upward (the Y of their
    /// normal). The original has no such query; bodies' shadows use it to stay off walls.
    /// </summary>
    public static bool SweepNearest(CollisionWorld world, Vector3 from, Vector3 to, float radius, bool skipSeeThrough, float minUp, out Vector3 point, out Vector3 normal)
    {
        var delta = to - from;
        var best = float.MaxValue;
        point = normal = Vector3.Zero;
        foreach (var t in Near(world, from, delta, radius))
        {
            if ((skipSeeThrough && t.Has(CollisionFlags.SeeThrough)) || t.Normal.Y < minUp)
                continue;
            if (Vector3.Dot(delta, t.Normal) <= 0 && SweepPlane(from, delta, radius, t, out var p, out var share) && share < best)
            {
                best = share;
                point = p;
                normal = t.Normal;
            }
        }
        return best != float.MaxValue;
    }

    /// <summary>
    /// Whether there's ground within <paramref name="depth"/> below a sphere, other than plain
    /// collision surfaces 1 and 2 (<c>processPlayers_sub</c>).
    /// </summary>
    public static bool Ground(CollisionWorld world, Vector3 position, float radius, float depth)
    {
        var delta = new Vector3(0, -depth, 0);
        foreach (var t in Near(world, position, delta, radius))
        {
            if (t.Surface is not (1 or 2) && SweepPlane(position, delta, radius, t, out _))
                return true;
        }
        return false;
    }

    private static List<Triangle> Near(CollisionWorld world, Vector3 from, Vector3 delta, float radius, ChunkKey? part = null)
    {
        var reach = new Vector2(delta.X, delta.Z).Length() + radius + 1;
        var near = _near ??= [];
        world.Near(from.X - reach, from.Z - reach, from.X + reach, from.Z + reach, near, part);
        return near;
    }

    /// <summary>One pass over the triangles (<c>CollideCollisionSphereWithVoxelPolys</c>). Returns whether it touched any.</summary>
    private static bool Collide(CollisionSphere s, List<Triangle> triangles)
    {
        var touching = false;
        // The distance each test last reported. The original shares one variable between them,
        // and the edge test leaves it alone when it gives up early.
        var distance = 0f;
        foreach (var t in triangles)
        {
            if (s.IgnoresSeeThrough && t.Has(CollisionFlags.SeeThrough))
                continue;
            var passable = t.Has(CollisionFlags.Passable);
            var hit = false;
            // Every sphere's and surface's contact margin (collisionTypeA.unk8) is 0.
            if (MovingPlane(s.Position, s.Velocity, s.Radius, t, out distance))
            {
                if (!passable)
                {
                    Bounce(ref s.Velocity, t.Normal);
                    s.Position -= t.Normal * distance;
                    Dampen(s, SurfaceKeep(t.Surface), t.Normal);
                    s.ContactNormal = t.Normal;
                    touching = true;
                }
                hit = true;
            }
            else if (!passable)
            {
                if (MathF.Abs(distance - s.Radius) < 0.005f && s.Touching)
                    touching = true;
                // Edges in the order A–B, B–C, C–A.
                for (var i = 0; i < 3 && !hit; i++)
                {
                    if (!t.Has(i switch { 0 => CollisionFlags.EdgeAB, 1 => CollisionFlags.EdgeBC, _ => CollisionFlags.EdgeCA }))
                        continue;
                    if (Edge(s, t[i], t[(i + 1) % 3], ref distance, out var direction))
                    {
                        Bounce(ref s.Velocity, direction);
                        s.Position += direction * (distance - s.Radius);
                        Dampen(s, SurfaceKeep(t.Surface), direction);
                        s.ContactNormal = direction;
                        touching = hit = true;
                    }
                    else if (MathF.Abs(distance - s.Radius) < 0.005f && s.Touching)
                    {
                        touching = true;
                    }
                }
                for (var i = 0; i < 3 && !hit; i++)
                {
                    if (!t.Has(i switch { 0 => CollisionFlags.VertexA, 1 => CollisionFlags.VertexB, _ => CollisionFlags.VertexC }))
                        continue;
                    var direction = t[i] - s.Position;
                    distance = Normalize(ref direction);
                    if (distance <= s.Radius)
                    {
                        Bounce(ref s.Velocity, direction);
                        s.Position += direction * (distance - s.Radius);
                        Dampen(s, SurfaceKeep(t.Surface), direction);
                        // Unlike the edges, a corner reports the face's normal.
                        s.ContactNormal = t.Normal;
                        touching = hit = true;
                    }
                    else if (MathF.Abs(distance - s.Radius) < 0.005f && s.Touching)
                    {
                        touching = true;
                    }
                }
            }
            // Every triangle has the game's default callback, which keeps the surface as the ground type
            // and runs a trigger triangle's object (check_trigger).
            if (hit)
            {
                s.Surface = t.Surface;
                if (t.Role == CollisionRole.Trigger)
                    s.Trod.Add((t.Part, t.Target));
            }
        }
        return touching;
    }

    /// <summary>
    /// Whether the sphere, one step further along its velocity, is into the triangle's plane
    /// within the triangle (<c>0x800a4da4</c>). <paramref name="depth"/> is how far that step's
    /// front is past the plane: negative when it's in.
    /// </summary>
    private static bool MovingPlane(Vector3 position, Vector3 velocity, float radius, in Triangle t, out float depth)
    {
        var front = position - t.Normal * radius + velocity;
        var centre = Vector3.Dot(position - t.A, t.Normal);
        depth = Vector3.Dot(front - t.A, t.Normal);
        if (centre < 0 && -centre > radius)
            return false;
        if (depth > 0)
            return false;
        var share = centre + depth;
        if (share != 0)
            share = centre / share;
        return Inside(position + velocity * share, t);
    }

    /// <summary>Whether a sphere's front, moving by <paramref name="delta"/>, crosses the triangle (<c>0x800a4f7c</c>).</summary>
    private static bool SweepPlane(Vector3 from, Vector3 delta, float radius, in Triangle t, out Vector3 point) =>
        SweepPlane(from, delta, radius, t, out point, out _);

    /// <param name="share">How much of <paramref name="delta"/> it moves before it touches.</param>
    private static bool SweepPlane(Vector3 from, Vector3 delta, float radius, in Triangle t, out Vector3 point, out float share)
    {
        var start = from - t.Normal * radius;
        var before = Vector3.Dot(start - t.A, t.Normal);
        var after = Vector3.Dot(start + delta - t.A, t.Normal);
        point = default;
        share = 0;
        if (before < 0 || after > 0)
            return false;
        share = after < 0 ? before - after : before + after;
        if (share > 0)
            share = MathF.Abs(before) / share;
        point = start + delta * share;
        return Inside(point, t);
    }

    /// <summary>
    /// Whether an edge is within the radius of the segment from the sphere's centre a radius along
    /// its velocity (<c>0x800a5148</c>). The point on the edge it measures to is placed with
    /// the segment's parameter, as the game does.
    /// </summary>
    private static bool Edge(CollisionSphere s, Vector3 a, Vector3 b, ref float distance, out Vector3 direction)
    {
        direction = default;
        var ahead = s.Velocity;
        Normalize(ref ahead);
        ahead *= s.Radius;
        var toEdge = a - s.Position;
        var along = b - a;
        var cross = Vector3.Dot(ahead, along);
        var aheadSquared = Vector3.Dot(ahead, ahead);
        var denominator = Vector3.Dot(along, along) * aheadSquared - cross * cross;
        if (MathF.Abs(denominator) < Epsilon)
            return false;
        var onAhead = Vector3.Dot(toEdge, ahead);
        var onAlong = Vector3.Dot(toEdge, along);
        var edgeShare = (onAhead * cross - onAlong * aheadSquared) / denominator;
        var aheadShare = (onAhead + cross * edgeShare) / aheadSquared;
        if (edgeShare < 0 || edgeShare > 1 || aheadShare < 0 || aheadShare > 1)
            return false;
        direction = a + along * aheadShare - s.Position;
        distance = Normalize(ref direction);
        return distance < s.Radius;
    }

    /// <summary>
    /// Whether a point is inside the triangle, seen along the axis its normal is closest to
    /// (<c>0x800a4ac0</c>). Points on an edge count as inside.
    /// </summary>
    private static bool Inside(Vector3 p, in Triangle t)
    {
        var n = t.Normal;
        var ax = MathF.Abs(n.X);
        var ay = MathF.Abs(n.Y);
        var az = MathF.Abs(n.Z);
        // 0 drops X, 1 drops Y, 2 drops Z; ties go the way the game's comparisons do.
        var drop = (n.Y > 0 ? ax <= n.Y : ax <= -n.Y)
            ? (az < ay ? 1 : 2)
            : (az < ax ? 0 : 2);
        var sum = 0;
        for (var i = 0; i < 3; i++)
        {
            var (u0, v0) = Project(t[i] - p, drop);
            var (u1, v1) = Project(t[(i + 1) % 3] - p, drop);
            sum += u0 * v1 - u1 * v0 < 0 ? -1 : 1;
        }
        return Math.Abs(sum) == 3;
    }

    private static (float U, float V) Project(Vector3 v, int drop) => drop switch
    {
        0 => (v.Z, v.Y),
        1 => (v.X, v.Z),
        _ => (v.X, v.Y),
    };

    /// <summary>
    /// Mirrors a velocity heading into a surface off it, keeping its speed
    /// (<c>collision_velocity_func</c>). A velocity already heading along the normal is left alone.
    /// </summary>
    private static void Bounce(ref Vector3 velocity, Vector3 normal)
    {
        var speed = Normalize(ref velocity);
        var into = Vector3.Dot(velocity, normal);
        if (into <= 0)
        {
            velocity -= normal * (2 * into);
            Normalize(ref velocity);
        }
        velocity *= speed;
    }

    /// <summary>
    /// Takes the velocity along a contact out, one axis at a time, unless it's within 20° of the
    /// last contact, then keeps the surface's share of what's left, times the sphere's own
    /// (<c>FUN_800af578</c>). Every sphere and surface would scale the first part by one
    /// (<c>collisionTypeA.unk0</c> is always 0).
    /// </summary>
    /// <param name="keep">The share of the velocity the other side keeps: a surface's, or another body's.</param>
    private static void Dampen(CollisionSphere s, float keep, Vector3 normal)
    {
        if (!s.Touching || MathF.Abs(Vector3.Dot(normal, s.ContactNormal)) < 0.9397f)
        {
            s.Velocity.X *= 1 - MathF.Abs(normal.X);
            s.Velocity.Y *= 1 - MathF.Abs(normal.Y);
            s.Velocity.Z *= 1 - MathF.Abs(normal.Z);
        }
        s.Velocity *= keep * s.Keep;
    }

    /// <summary>
    /// Pushes a body's sphere out of another body's, across the ground only, each at its scale
    /// (<c>FUN_800b003c</c>, <c>FUN_800af120</c>): it moves straight away from the other until they
    /// touch, and its velocity bounces and dampens as off a wall. The other doesn't move.
    /// </summary>
    /// <param name="distance">
    /// How far apart their centres are across the ground. When they don't touch, the original measures
    /// it with the other's radius at this body's scale rather than its own, and so does this.
    /// </param>
    /// <returns>Whether they touched.</returns>
    public static bool PushApart(CollisionSphere s, float scale, CollisionSphere other, float otherScale, out float distance)
    {
        var away = other.Position - s.Position;
        away.Y = 0;
        var gap = Normalize(ref away) - other.Radius * otherScale;
        if (gap > s.Radius * scale)
        {
            distance = gap + other.Radius * scale;
            return false;
        }
        Bounce(ref s.Velocity, away);
        s.Position += away * (gap - s.Radius * scale);
        Dampen(s, other.Keep, away);
        distance = gap + other.Radius * otherScale;
        return true;
    }

    /// <summary>Normalizes in place and returns the length, never less than <see cref="Epsilon"/> (<c>Vec3Normalize</c>).</summary>
    internal static float Normalize(ref Vector3 v)
    {
        var length = MathF.Max(v.Length(), Epsilon);
        v /= length;
        return length;
    }
}
