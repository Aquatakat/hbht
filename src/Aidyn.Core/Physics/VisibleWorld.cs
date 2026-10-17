using System.Numerics;

namespace Aidyn.Core.Physics;

/// <summary>
/// The solid surfaces the loaded chunks draw, in world coordinates, with a grid to find the triangles near a
/// line: what the port's rain and snow land on, where the original's land on the collision
/// (<see cref="CollisionWorld"/>), invisible walls and all. Each chunk's triangles go in under a key to remove
/// them by, as its collision does.
/// </summary>
public sealed class VisibleWorld
{
    private const float CellSize = 4;

    private readonly Dictionary<object, Part> _parts = [];
    private readonly HashSet<int> _seen = [];

    private readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal);

    private sealed record Part(Triangle[] Triangles, Dictionary<(int X, int Z), int[]> Cells, float MinX, float MinZ, float MaxX, float MaxZ);

    public int Count => _parts.Values.Sum(p => p.Triangles.Length);

    /// <summary>
    /// Adds a chunk's triangles, moved by <paramref name="offset"/>, under a key to remove them by, in place of any
    /// already under it. Triangles with no area are left out.
    /// </summary>
    public void Add(object key, IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> triangles, Vector3 offset)
    {
        var list = new List<Triangle>();
        foreach (var (a, b, c) in triangles)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.Length() < 1e-6f)
                continue;
            list.Add(new Triangle(a + offset, b + offset, c + offset, Vector3.Normalize(normal)));
        }

        var cells = new Dictionary<(int, int), List<int>>();
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        for (var i = 0; i < list.Count; i++)
        {
            var t = list[i];
            var lowX = Math.Min(t.A.X, Math.Min(t.B.X, t.C.X));
            var lowZ = Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z));
            var highX = Math.Max(t.A.X, Math.Max(t.B.X, t.C.X));
            var highZ = Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z));
            (minX, minZ, maxX, maxZ) = (Math.Min(minX, lowX), Math.Min(minZ, lowZ), Math.Max(maxX, highX), Math.Max(maxZ, highZ));
            for (var x = Cell(lowX); x <= Cell(highX); x++)
            {
                for (var z = Cell(lowZ); z <= Cell(highZ); z++)
                {
                    if (!cells.TryGetValue((x, z), out var cell))
                        cells[(x, z)] = cell = [];
                    cell.Add(i);
                }
            }
        }
        _parts[key] = new Part([.. list], cells.ToDictionary(c => c.Key, c => c.Value.ToArray()), minX, minZ, maxX, maxZ);
    }

    public bool Remove(object key) => _parts.Remove(key);

    /// <summary>Removes every part, as changing map set does.</summary>
    public void Clear() => _parts.Clear();

    /// <summary>
    /// The nearest surface the line from <paramref name="from"/> to <paramref name="to"/> crosses, from either side:
    /// where, and its unit normal turned to face back along the line. Of every part, or of the one added under
    /// <paramref name="only"/>.
    /// </summary>
    public bool Raycast(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal, object? only = null)
    {
        var delta = to - from;
        var best = float.MaxValue;
        point = normal = default;
        float minX = Math.Min(from.X, to.X), maxX = Math.Max(from.X, to.X);
        float minZ = Math.Min(from.Z, to.Z), maxZ = Math.Max(from.Z, to.Z);
        foreach (var (key, part) in _parts)
        {
            if (only is not null && !only.Equals(key))
                continue;
            if (maxX < part.MinX || minX > part.MaxX || maxZ < part.MinZ || minZ > part.MaxZ)
                continue;
            _seen.Clear();
            for (var x = Cell(minX); x <= Cell(maxX); x++)
            {
                for (var z = Cell(minZ); z <= Cell(maxZ); z++)
                {
                    if (!part.Cells.TryGetValue((x, z), out var cell))
                        continue;
                    foreach (var i in cell)
                    {
                        if (!_seen.Add(i))
                            continue;
                        var t = part.Triangles[i];
                        if (Cross(from, delta, t) is not { } share || share >= best)
                            continue;
                        best = share;
                        normal = Vector3.Dot(t.Normal, delta) > 0 ? -t.Normal : t.Normal;
                    }
                }
            }
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

    private static int Cell(float v) => (int)MathF.Floor(v / CellSize);
}
