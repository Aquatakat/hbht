using System.Numerics;
using Aidyn.Core.Chunks;
using Aidyn.Plugins.Assets;

namespace Aidyn.Core.Physics;

/// <summary>
/// The collision of the loaded chunks, in world coordinates, with a grid to find the triangles near
/// a point. The original keeps one grid per chunk (<c>CollideSection</c>) and only collides with the
/// chunk the player is in; this one spans every loaded chunk, so nothing changes at a border.
/// </summary>
public sealed class CollisionWorld
{
    private readonly TriangleGrid<Triangle> _grid = new();

    /// <summary>
    /// A collision triangle with its unit normal, which faces out of its front, and the key of the
    /// part it's in, which names the map a trigger triangle's <see cref="Target"/> is an object of.
    /// </summary>
    internal readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, int Surface, CollisionFlags Flags,
        CollisionRole Role, int Target, ChunkKey Part) : IGridTriangle
    {
        public Vector3 this[int i] => i switch { 0 => A, 1 => B, _ => C };
        public bool Has(CollisionFlags flag) => (Flags & flag) != 0;
    }

    public int Count => _grid.Count;

    /// <summary>
    /// Adds a chunk's triangles, moved by <paramref name="offset"/>, under a key to remove them by.
    /// Triangles with no area are left out: they can't be hit, and their normals are meaningless.
    /// </summary>
    public void Add(ChunkKey key, IEnumerable<CollisionTriangle> triangles, Vector3 offset)
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

        _grid.Add(key, list);
    }

    public bool Remove(ChunkKey key) => _grid.Remove(key);

    /// <summary>Removes every part, as changing map set does.</summary>
    public void Clear() => _grid.Clear();

    /// <summary>
    /// Changes the flags of a part's toggles with id <paramref name="toggle"/>: ANDs them with
    /// <paramref name="keep"/>, then ORs in <paramref name="add"/>, as a <c>ChangeCollision</c> trigger
    /// does (<c>trigger_vobject_func</c>). They stay so until the part is added again. Returns how many changed.
    /// </summary>
    public int ChangeToggles(ChunkKey key, int toggle, int keep, int add)
    {
        if (_grid.Triangles(key) is not { } triangles)
            return 0;
        var changed = 0;
        for (var i = 0; i < triangles.Length; i++)
        {
            var t = triangles[i];
            if (t.Role != CollisionRole.Toggle || t.Target != toggle)
                continue;
            var flags = (CollisionFlags)(((int)t.Flags & keep) | add);
            if (flags == t.Flags)
                continue;
            triangles[i] = t with { Flags = flags };
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// The triangles that may touch the box from <paramref name="min"/> to <paramref name="max"/> in
    /// X and Z, each once, in a fixed order: of every part, or of the one added under <paramref name="only"/>.
    /// </summary>
    internal void Near(float minX, float minZ, float maxX, float maxZ, List<Triangle> into, ChunkKey? only = null) =>
        _grid.Near(minX, minZ, maxX, maxZ, into, only);
}
