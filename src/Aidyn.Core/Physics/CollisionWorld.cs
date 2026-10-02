using System.Numerics;
using Aidyn.Plugins.Assets;

namespace Aidyn.Core.Physics;

/// <summary>
/// The collision of the loaded chunks, in world coordinates, with a grid to find the triangles near
/// a point. The original keeps one grid per chunk (<c>CollideSection</c>) and only collides with the
/// chunk the player is in; this one spans every loaded chunk, so nothing changes at a border.
/// </summary>
public sealed class CollisionWorld
{
    private const float CellSize = 4;

    private readonly Dictionary<object, Part> _parts = [];
    private readonly HashSet<int> _seen = [];

    /// <summary>
    /// A collision triangle with its unit normal, which faces out of its front, and the key of the
    /// part it's in, which names the map a trigger triangle's <see cref="Target"/> is an object of.
    /// </summary>
    internal readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, int Surface, CollisionFlags Flags,
        CollisionRole Role, int Target, object Part)
    {
        public Vector3 this[int i] => i switch { 0 => A, 1 => B, _ => C };
        public bool Has(CollisionFlags flag) => (Flags & flag) != 0;
    }

    private sealed record Part(Triangle[] Triangles, Dictionary<(int X, int Z), int[]> Cells, float MinX, float MinZ, float MaxX, float MaxZ);

    public int Count => _parts.Values.Sum(p => p.Triangles.Length);

    /// <summary>
    /// Adds a chunk's triangles, moved by <paramref name="offset"/>, under a key to remove them by.
    /// Triangles with no area are left out: they can't be hit, and their normals are meaningless.
    /// </summary>
    public void Add(object key, IEnumerable<CollisionTriangle> triangles, Vector3 offset)
    {
        var list = new List<Triangle>();
        foreach (var t in triangles)
        {
            // The original's normals are (B − A) × (C − A), normalised; the importer doesn't keep them.
            var normal = Vector3.Cross(t.B - t.A, t.C - t.A);
            if (normal.Length() < 1e-6f)
                continue;
            list.Add(new Triangle(t.A + offset, t.B + offset, t.C + offset, Vector3.Normalize(normal), t.Surface, t.Flags, t.Role, t.Target, key));
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
    /// Changes the flags of a part's toggles with id <paramref name="toggle"/>: ANDs them with
    /// <paramref name="keep"/>, then ORs in <paramref name="add"/>, as a <c>ChangeCollision</c> trigger
    /// does (<c>trigger_vobject_func</c>). They stay so until the part is added again. Returns how many changed.
    /// </summary>
    public int ChangeToggles(object key, int toggle, int keep, int add)
    {
        if (!_parts.TryGetValue(key, out var part))
            return 0;
        var changed = 0;
        for (var i = 0; i < part.Triangles.Length; i++)
        {
            var t = part.Triangles[i];
            if (t.Role != CollisionRole.Toggle || t.Target != toggle)
                continue;
            var flags = (CollisionFlags)(((int)t.Flags & keep) | add);
            if (flags == t.Flags)
                continue;
            part.Triangles[i] = t with { Flags = flags };
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// The triangles that may touch the box from <paramref name="min"/> to <paramref name="max"/> in
    /// X and Z, each once, in a fixed order.
    /// </summary>
    internal void Near(float minX, float minZ, float maxX, float maxZ, List<Triangle> into)
    {
        into.Clear();
        foreach (var part in _parts.Values)
        {
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
                        if (_seen.Add(i))
                            into.Add(part.Triangles[i]);
                    }
                }
            }
        }
    }

    private static int Cell(float v) => (int)MathF.Floor(v / CellSize);
}
