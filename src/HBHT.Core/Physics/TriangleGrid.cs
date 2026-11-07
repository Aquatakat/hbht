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

    /// <summary>How many cells a part's box may have and still keep them in an array, whatever it fills: a chunk's is 50 × 50.</summary>
    private const long DenseCells = 16384;

    private readonly Dictionary<ChunkKey, Part> _parts = [];

    /// <summary>
    /// The parts in the order the dictionary lists them, taken again after each change: a query walks this rather than the
    /// dictionary, whose enumerator cost more than the cells it found on a busy 3×3.
    /// </summary>
    private KeyValuePair<ChunkKey, Part>[]? _ordered;

    private KeyValuePair<ChunkKey, Part>[] Ordered => _ordered ??= [.. _parts];

    /// <summary>The triangles a query has listed so far, one set a thread, so threads may query a grid nothing changes at once.</summary>
    [ThreadStatic] private static HashSet<int>? _seen;

    /// <summary>
    /// A part's triangles, and the cells of its box: <see cref="Width"/> a row, from cell (<see cref="CellX"/>, <see cref="CellZ"/>),
    /// row by row in Z, each the indices of its triangles or null for none. A part whose box is far bigger than the cells it fills,
    /// as one with a stray triangle far out is, keeps them by cell in <see cref="Sparse"/> instead.
    /// </summary>
    private sealed record Part(T[] Triangles, int[]?[] Cells, Dictionary<(int X, int Z), int[]>? Sparse, int CellX, int CellZ,
        int Width, int Height, float MinX, float MinZ, float MaxX, float MaxZ)
    {
        /// <summary>The indices of the triangles in cell (<paramref name="x"/>, <paramref name="z"/>), or null for none or outside the box.</summary>
        public int[]? Cell(int x, int z)
        {
            if (Sparse is not null)
                return Sparse.TryGetValue((x, z), out var cell) ? cell : null;
            var column = x - CellX;
            var row = z - CellZ;
            return (uint)column < (uint)Width && (uint)row < (uint)Height ? Cells[row * Width + column] : null;
        }
    }

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
        var (cellX, cellZ) = triangles.Count > 0 ? (Cell(minX), Cell(minZ)) : (0, 0);
        var width = triangles.Count > 0 ? (long)Cell(maxX) - cellX + 1 : 0;
        var height = triangles.Count > 0 ? (long)Cell(maxZ) - cellZ + 1 : 0;
        if (width * height <= Math.Max(DenseCells, 4L * cells.Count))
        {
            var grid = new int[]?[width * height];
            foreach (var ((x, z), cell) in cells)
                grid[(z - cellZ) * width + (x - cellX)] = [.. cell];
            _parts[key] = new Part([.. triangles], grid, null, cellX, cellZ, (int)width, (int)height, minX, minZ, maxX, maxZ);
        }
        else
        {
            _parts[key] = new Part([.. triangles], [], cells.ToDictionary(c => c.Key, c => c.Value.ToArray()), cellX, cellZ, 0, 0,
                minX, minZ, maxX, maxZ);
        }
        _ordered = null;
    }

    public bool Remove(ChunkKey key)
    {
        _ordered = null;
        return _parts.Remove(key);
    }

    public void Clear()
    {
        _ordered = null;
        _parts.Clear();
    }

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
        foreach (var (key, part) in Ordered)
        {
            if (only is { } one && one != key)
                continue;
            if (maxX < part.MinX || minX > part.MaxX || maxZ < part.MinZ || minZ > part.MaxZ)
                continue;
            // Only the part's own cells hold anything: the box is cut to them, which keeps the cells' order.
            var (fromX, toX, fromZ, toZ) = (Cell(minX), Cell(maxX), Cell(minZ), Cell(maxZ));
            if (part.Sparse is null)
            {
                (fromX, toX) = (Math.Max(fromX, part.CellX), Math.Min(toX, part.CellX + part.Width - 1));
                (fromZ, toZ) = (Math.Max(fromZ, part.CellZ), Math.Min(toZ, part.CellZ + part.Height - 1));
            }
            if (fromX == toX && fromZ == toZ)
            {
                // One cell lists each of its triangles once.
                if (part.Cell(fromX, fromZ) is { } only1)
                {
                    foreach (var i in only1)
                        into.Add(part.Triangles[i]);
                }
                continue;
            }
            seen.Clear();
            for (var x = fromX; x <= toX; x++)
            {
                for (var z = fromZ; z <= toZ; z++)
                {
                    if (part.Cell(x, z) is not { } cell)
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
    /// The triangles that may lie within <paramref name="widen"/> of the segment from <paramref name="from"/> to
    /// <paramref name="to"/> in X and Z, each once a part: only the cells along it, where <see cref="Near"/> takes every cell
    /// of the box round it, which for a long segment across the world is most of it. Their order isn't <see cref="Near"/>'s, so
    /// it's for questions that don't hang on the order, such as whether anything is in the way. Replaces what
    /// <paramref name="into"/> held.
    /// </summary>
    public void Along(Vector2 from, Vector2 to, float widen, List<T> into)
    {
        into.Clear();
        var seen = _seen ??= [];
        var minX = Math.Min(from.X, to.X) - widen;
        var maxX = Math.Max(from.X, to.X) + widen;
        var minZ = Math.Min(from.Y, to.Y) - widen;
        var maxZ = Math.Max(from.Y, to.Y) + widen;
        var delta = to - from;
        foreach (var (_, part) in Ordered)
        {
            if (maxX < part.MinX || minX > part.MaxX || maxZ < part.MinZ || minZ > part.MaxZ)
                continue;
            seen.Clear();
            var lowZCell = Cell(Math.Max(minZ, part.MinZ));
            var highZCell = Cell(Math.Min(maxZ, part.MaxZ));
            for (var x = Cell(Math.Max(minX, part.MinX)); x <= Cell(Math.Min(maxX, part.MaxX)); x++)
            {
                // Where the segment runs within this column, widened: the Z it spans there, widened again.
                float lowZ, highZ;
                if (MathF.Abs(delta.X) < 1e-6f)
                    (lowZ, highZ) = (Math.Min(from.Y, to.Y), Math.Max(from.Y, to.Y));
                else
                {
                    var t0 = Math.Clamp((x * CellSize - widen - from.X) / delta.X, 0, 1);
                    var t1 = Math.Clamp(((x + 1) * CellSize + widen - from.X) / delta.X, 0, 1);
                    var z0 = from.Y + delta.Y * t0;
                    var z1 = from.Y + delta.Y * t1;
                    (lowZ, highZ) = (Math.Min(z0, z1), Math.Max(z0, z1));
                }
                var zFrom = Math.Max(Cell(lowZ - widen), lowZCell);
                var zTo = Math.Min(Cell(highZ + widen), highZCell);
                for (var z = zFrom; z <= zTo; z++)
                {
                    if (part.Cell(x, z) is not { } cell)
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
        foreach (var (key, part) in Ordered)
        {
            if (key == except || highX < part.MinX || lowX > part.MaxX || highZ < part.MinZ || lowZ > part.MaxZ)
                continue;
            seen.Clear();
            for (var x = Cell(lowX); x <= Cell(highX); x++)
            {
                for (var z = Cell(lowZ); z <= Cell(highZ); z++)
                {
                    if (part.Cell(x, z) is not { } cell)
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
