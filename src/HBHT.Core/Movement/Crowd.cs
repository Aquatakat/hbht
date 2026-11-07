using System.Numerics;
using HBHT.Core.Chunks;
using HBHT.Core.Physics;

namespace HBHT.Core.Movement;

/// <summary>A body as the others push it: its sphere, its scale, and the chunk it's in.</summary>
/// <param name="Chunk">Bodies push only others in the same chunk, as the original keeps each in its chunk's coordinates.</param>
public readonly record struct CrowdMember(CollisionSphere Sphere, float Scale, ChunkKey? Chunk);

/// <summary>
/// Bodies pushing each other apart, the player among them, as each moves (the end of
/// <c>ProcessPlayers</c>, checked against 1.0's machine code).
/// </summary>
public static class Crowd
{
    /// <summary>Bodies further apart in height than this don't push each other.</summary>
    public const float Height = 3;

    /// <summary>
    /// How far into a body the one pushed out of it may be weighed again and not be pushed back: a push leaves them exactly
    /// touching, give or take the rounding.
    /// </summary>
    public const float Rounding = 1e-4f;

    /// <summary>
    /// Pushes a body that has just moved out of the others in its chunk within <see cref="Height"/>
    /// of it, each in turn. The first push starts the turn again, from the second body rather than
    /// the first, as the original's loop does; a second push puts the body back where it began the
    /// tick. Returns the nearest of the others it was weighed against, across the ground, and how far.
    /// </summary>
    /// <remarks>
    /// After the first push, a touch within <see cref="Rounding"/> isn't a second push. The original counts exactly touching
    /// too (<c>0x80013610</c>), and the body it was just pushed out of is weighed again, so whether it stays or goes back is
    /// rounding, about half the time each way. Its frames vary in length, so a body pressed against another slides on in a
    /// frame or two. The port's frame doesn't vary, so it would repeat the same frame and go back every time, stuck against
    /// the other to the bit. The user chose that it slide on as 1.0's does (Oct 31, 2026).
    /// </remarks>
    /// <param name="bodies">Every body, in the order they're stepped: the player first.</param>
    /// <param name="self">The one that moved.</param>
    /// <param name="start">Where it began the tick.</param>
    public static (int Nearest, float Distance) Push(IReadOnlyList<CrowdMember?> bodies, int self, Vector3 start)
    {
        if (bodies[self] is not { } body)
            return (-1, float.MaxValue);
        var nearest = -1;
        var best = 1_000_000f;
        var pushed = false;
        for (var i = 0; i < bodies.Count; i++)
        {
            if (i == self || bodies[i] is not { } other || other.Chunk != body.Chunk
                || MathF.Abs(other.Sphere.Position.Y - body.Sphere.Position.Y) > Height)
                continue;
            var index = i;
            if (SpherePhysics.PushApart(body.Sphere, body.Scale, other.Sphere, other.Scale, out var distance, pushed ? Rounding : 0))
            {
                if (pushed)
                {
                    body.Sphere.Position = start;
                }
                else
                {
                    pushed = true;
                    i = 0;
                }
            }
            if (distance < best)
            {
                best = distance;
                nearest = index;
            }
        }
        return (nearest, best);
    }
}
