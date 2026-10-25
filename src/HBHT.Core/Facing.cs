using System.Numerics;

namespace HBHT.Core;

/// <summary>
/// Turning a way across the ground, in X and Z: what bodies turning to face, and the cameras turning about
/// their aim, share.
/// </summary>
public static class Facing
{
    /// <summary>
    /// <paramref name="v"/> turned by the angle whose sine and cosine are given, the way the original turns
    /// (<c>cos·x + sin·y</c>, <c>cos·y − sin·x</c>): a positive angle turns +X toward −Z.
    /// </summary>
    public static Vector2 Rotate(Vector2 v, float sin, float cos) => new(cos * v.X + sin * v.Y, cos * v.Y - sin * v.X);

    /// <summary>
    /// Turns <paramref name="from"/> toward <paramref name="to"/> by <paramref name="angle"/>,
    /// or all the way once they're that close (<c>some_trig_func_2</c>).
    /// </summary>
    public static Vector2 Turn(Vector2 from, Vector2 to, float angle)
    {
        from = Vector2.Normalize(from);
        to = Vector2.Normalize(to);
        var (sin, cos) = MathF.SinCos(angle);
        if (Vector2.Distance(from, to) <= sin)
            return to;
        var left = Rotate(from, sin, cos);
        var right = Rotate(from, -sin, cos);
        return Vector2.Distance(right, to) <= Vector2.Distance(left, to) ? right : left;
    }

    /// <summary>
    /// Whether a turn has arrived: <see cref="Turn"/> normalizes what it arrives at again, which can
    /// differ from a unit vector in its last bit.
    /// </summary>
    public static bool Arrived(Vector2 heading, Vector2 to) => Vector2.DistanceSquared(heading, to) < 1e-10f;
}
