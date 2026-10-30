using System.Numerics;
using HBHT.Core.Chunks;

namespace HBHT.Core.Physics;

/// <summary>A triangle a <see cref="TriangleGrid{T}"/> bins by the X and Z extent of its corners.</summary>
internal interface IGridTriangle
{
    Vector3 A { get; }
    Vector3 B { get; }
    Vector3 C { get; }
}

/// <summary>
/// The grid <see cref="CollisionWorld"/> and <see cref="VisibleWorld"/> find triangles with: parts added under a chunk's
/// key (<see cref="ChunkKey"/>), each binned into 4-unit cells in X and Z, and removed by that key. <see cref="Near"/> lists the triangles
/// whose cells a box touches, each once, in a fixed order: parts in the order a dictionary lists them, and within
/// a part the cells row by row (X, then Z), each cell's triangles in the order they were added. The original's
/// sweeps take the first triangle facing them in its grid's order, so the order is behaviour.
/// </summary>
internal sealed class TriangleGrid<T> where T : struct, IGridTriangle
{
    private const float CellSize = 4;

    private readonly Dictionary<ChunkKey, Part> _parts = [];

    /// <summary>The triangles a query has listed so far, one set a thread, so threads may query a grid nothing changes at once.</summary>
    [ThreadStatic] private static HashSet<int>? _seen;

    private sealed record Part(T[] Triangles, Dictionary<(int X, int Z), int[]> Cells, float MinX, float MinZ, float MaxX, float MaxZ);

    public int Count => _parts.Values.Sum(p => p.Triangles.Length);

    /// <summary>Bins <paramref name="triangles"/> under <paramref name="key"/>, in place of any already under it.</summary>
    public void Add(ChunkKey key, List<T> triangles)
    {
        var cells = new Dictionary<(int, int), List<int>>();
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        for (var i = 0; i < triangles.Count; i++)
        {
            var t = triangles[i];
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
        _parts[key] = new Part([.. triangles], cells.ToDictionary(c => c.Key, c => c.Value.ToArray()), minX, minZ, maxX, maxZ);
    }

    public bool Remove(ChunkKey key) => _parts.Remove(key);

    public void Clear() => _parts.Clear();

    /// <summary>The triangles under <paramref name="key"/>, which the caller may change in place, or null.</summary>
    public T[]? Triangles(ChunkKey key) => _parts.TryGetValue(key, out var part) ? part.Triangles : null;

    /// <summary>
    /// The triangles that may touch the box from (<paramref name="minX"/>, <paramref name="minZ"/>) to
    /// (<paramref name="maxX"/>, <paramref name="maxZ"/>) in X and Z, each once, in the grid's order: of every
    /// part, or of the one added under <paramref name="only"/>. Replaces what <paramref name="into"/> held.
    /// </summary>
    public void Near(float minX, float minZ, float maxX, float maxZ, List<T> into, ChunkKey? only = null)
    {
        into.Clear();
        var seen = _seen ??= [];
        foreach (var (key, part) in _parts)
        {
            if (only is { } one && one != key)
                continue;
            if (maxX < part.MinX || minX > part.MaxX || maxZ < part.MinZ || minZ > part.MaxZ)
                continue;
            seen.Clear();
            for (var x = Cell(minX); x <= Cell(maxX); x++)
            {
                for (var z = Cell(minZ); z <= Cell(maxZ); z++)
                {
                    if (!part.Cells.TryGetValue((x, z), out var cell))
                        continue;
                    foreach (var i in cell)
                    {
                        if (seen.Add(i))
                            into.Add(part.Triangles[i]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The triangles of the parts other than <paramref name="except"/> with the same corners as <paramref name="triangle"/>, in any
    /// order, each within <paramref name="tolerance"/>: its copies where chunks overlap. Replaces what <paramref name="into"/> held
    /// with each copy's part and index, which the caller may change in place.
    /// </summary>
    public void Copies(ChunkKey except, in T triangle, float tolerance, List<(T[] Triangles, int Index)> into)
    {
        into.Clear();
        var lowX = Math.Min(triangle.A.X, Math.Min(triangle.B.X, triangle.C.X)) - tolerance;
        var lowZ = Math.Min(triangle.A.Z, Math.Min(triangle.B.Z, triangle.C.Z)) - tolerance;
        var highX = Math.Max(triangle.A.X, Math.Max(triangle.B.X, triangle.C.X)) + tolerance;
        var highZ = Math.Max(triangle.A.Z, Math.Max(triangle.B.Z, triangle.C.Z)) + tolerance;
        var squared = tolerance * tolerance;
        var seen = _seen ??= [];
        foreach (var (key, part) in _parts)
        {
            if (key == except || highX < part.MinX || lowX > part.MaxX || highZ < part.MinZ || lowZ > part.MaxZ)
                continue;
            seen.Clear();
            for (var x = Cell(lowX); x <= Cell(highX); x++)
            {
                for (var z = Cell(lowZ); z <= Cell(highZ); z++)
                {
                    if (!part.Cells.TryGetValue((x, z), out var cell))
                        continue;
                    foreach (var i in cell)
                    {
                        if (seen.Add(i) && SameCorners(triangle, part.Triangles[i], squared) && SameCorners(part.Triangles[i], triangle, squared))
                            into.Add((part.Triangles, i));
                    }
                }
            }
        }
    }

    /// <summary>Whether every corner of <paramref name="a"/> is within the tolerance of one of <paramref name="b"/>'s.</summary>
    private static bool SameCorners(in T a, in T b, float squared) =>
        Near(a.A, b, squared) && Near(a.B, b, squared) && Near(a.C, b, squared);

    private static bool Near(Vector3 p, in T b, float squared) =>
        Vector3.DistanceSquared(p, b.A) <= squared || Vector3.DistanceSquared(p, b.B) <= squared || Vector3.DistanceSquared(p, b.C) <= squared;

    private static int Cell(float v) => (int)MathF.Floor(v / CellSize);
}
