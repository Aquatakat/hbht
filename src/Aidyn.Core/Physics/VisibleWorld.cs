using System.Numerics;

namespace Aidyn.Core.Physics;

/// <summary>
/// A triangle a chunk draws: one that rain splashes on, or one that takes it in without a splash
/// (<see cref="Absorbs"/>).
/// </summary>
public readonly record struct DrawnTriangle(Vector3 A, Vector3 B, Vector3 C, bool Absorbs = false);

/// <summary>
/// The surfaces the loaded chunks draw, in world coordinates, with a grid to find the triangles near a
/// line: what the port's rain and snow land on, where the original's land on the collision
/// (<see cref="CollisionWorld"/>), invisible walls and all. Each chunk's triangles go in under a key to remove
/// them by, as its collision does.
/// </summary>
public sealed class VisibleWorld
{
    private readonly TriangleGrid<Triangle> _grid = new();
    private readonly List<Triangle> _near = [];

    private readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, bool Absorbs) : IGridTriangle;

    public int Count => _grid.Count;

    /// <summary>
    /// Adds a chunk's triangles, moved by <paramref name="offset"/>, under a key to remove them by, in place of any
    /// already under it. Triangles with no area are left out.
    /// </summary>
    public void Add(object key, IEnumerable<DrawnTriangle> triangles, Vector3 offset)
    {
        var list = new List<Triangle>();
        foreach (var (a, b, c, absorbs) in triangles)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.Length() < 1e-6f)
                continue;
            list.Add(new Triangle(a + offset, b + offset, c + offset, Vector3.Normalize(normal), absorbs));
        }

        _grid.Add(key, list);
    }

    public bool Remove(object key) => _grid.Remove(key);

    /// <summary>Removes every part, as changing map set does.</summary>
    public void Clear() => _grid.Clear();

    /// <summary>
    /// The nearest surface the line from <paramref name="from"/> to <paramref name="to"/> crosses, from either side:
    /// where, its unit normal turned to face back along the line, and whether it absorbs what hits it. Of every part,
    /// or of the one added under <paramref name="only"/>.
    /// </summary>
    public bool Raycast(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal, out bool absorbs, object? only = null)
    {
        var delta = to - from;
        var best = float.MaxValue;
        point = normal = default;
        absorbs = false;
        float minX = Math.Min(from.X, to.X), maxX = Math.Max(from.X, to.X);
        float minZ = Math.Min(from.Z, to.Z), maxZ = Math.Max(from.Z, to.Z);
        _grid.Near(minX, minZ, maxX, maxZ, _near, only);
        foreach (var t in _near)
        {
            if (Cross(from, delta, t) is not { } share || share >= best)
                continue;
            best = share;
            normal = Vector3.Dot(t.Normal, delta) > 0 ? -t.Normal : t.Normal;
            absorbs = t.Absorbs;
        }
        if (best == float.MaxValue)
            return false;
        point = from + delta * best;
        return true;
    }

    // How far along the line it crosses the triangle, from 0 to 1, or null (Möller–Trumbore, both faces).
    private static float? Cross(Vector3 from, Vector3 delta, in Triangle t)
    {
        var ab = t.B - t.A;
        var ac = t.C - t.A;
        var p = Vector3.Cross(delta, ac);
        var det = Vector3.Dot(ab, p);
        if (MathF.Abs(det) < 1e-9f)
            return null;
        var inverse = 1 / det;
        var s = from - t.A;
        var u = Vector3.Dot(s, p) * inverse;
        if (u < 0 || u > 1)
            return null;
        var q = Vector3.Cross(s, ab);
        var v = Vector3.Dot(delta, q) * inverse;
        if (v < 0 || u + v > 1)
            return null;
        var share = Vector3.Dot(ac, q) * inverse;
        return share is >= 0 and <= 1 ? share : null;
    }
}
