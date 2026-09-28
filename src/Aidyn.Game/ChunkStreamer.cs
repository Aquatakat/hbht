using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Keeps the 3×3 chunks around a point loaded, as the original's zone engine does
/// (<c>ZoneDatMtx</c>): each chunk's scenery, its see-through scenery, the models its scene
/// objects place, and its collision.
/// </summary>
public partial class ChunkStreamer : Node3D
{
    private readonly PluginSet? _plugins;
    private readonly Models? _models;
    private readonly MapSetRecord? _set;
    private readonly Dictionary<(int Row, int Column), ChunkEntry> _chunks = [];
    private readonly Dictionary<(int Row, int Column), Node3D> _loaded = [];

    public ChunkStreamer()
    {
    }

    public ChunkStreamer(PluginSet plugins, Models models, MapSetRecord set)
    {
        _plugins = plugins;
        _models = models;
        _set = set;
        Name = "Chunks";
        foreach (var chunk in set.Chunks)
            _chunks[(chunk.Row, chunk.Column)] = chunk;
    }

    public MapSetRecord MapSet => _set!;

    /// <summary>The chunk a point is in: rows run along X and columns along Z.</summary>
    public (int Row, int Column) CellAt(Vector3 position) =>
        ((int)Math.Floor(position.X / MapSet.CellSize), (int)Math.Floor(position.Z / MapSet.CellSize));

    public ChunkEntry? ChunkAt(Vector3 position) => _chunks.GetValueOrDefault(CellAt(position));

    public ChunkEntry? Find(string name) => _chunks.Values.FirstOrDefault(c => c.Name == name);

    public Vector3 Origin(ChunkEntry chunk) => Collision.ToGodot(MapSet.Origin(chunk.Row, chunk.Column));

    /// <summary>Loads the chunks around <paramref name="position"/> and frees the rest. Returns whether any changed.</summary>
    public bool Update(Vector3 position)
    {
        var (row, column) = CellAt(position);
        var wanted = new HashSet<(int, int)>();
        for (var r = row - 1; r <= row + 1; r++)
        {
            for (var c = column - 1; c <= column + 1; c++)
            {
                if (_chunks.ContainsKey((r, c)))
                    wanted.Add((r, c));
            }
        }

        var changed = false;
        foreach (var cell in _loaded.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _loaded[cell].QueueFree();
            _loaded.Remove(cell);
            changed = true;
        }
        foreach (var cell in wanted.Where(k => !_loaded.ContainsKey(k)))
        {
            var node = Load(_chunks[cell]);
            AddChild(node);
            _loaded[cell] = node;
            changed = true;
        }
        return changed;
    }

    private Node3D Load(ChunkEntry chunk)
    {
        var root = new Node3D { Name = chunk.Name, Position = Origin(chunk) };
        foreach (var scene in new[] { chunk.Scene, chunk.AltScene }.OfType<RecordId>())
        {
            if (_models!.Instantiate(scene) is { } node)
                root.AddChild(node);
        }

        if (_plugins!.FindAsset(chunk.Map, "glb") is { } collision)
            root.AddChild(Collision.Build(CollisionMesh.Read(File.ReadAllBytes(collision))));

        var scenery = new Node3D { Name = "Scenery" };
        root.AddChild(scenery);
        foreach (var o in _plugins.Get<MapRecord>(chunk.Map).Objects.OfType<SceneObject>())
        {
            if (Place(o) is { } node)
                scenery.AddChild(node);
        }
        return root;
    }

    /// <summary>A scene object's levels of detail, each drawn over its own range of distances.</summary>
    private Node3D? Place(SceneObject o)
    {
        // The runtime plays with the Expansion Pak. Objects gated on real flags wait for the flag
        // engine (M2), so for now only those that can never appear are left out.
        if (o.Pak == PakVariant.Without || IsNever(o.Visible))
            return null;

        var q = o.Orientation();
        var node = new Node3D
        {
            Name = $"Object {o.Index}",
            Transform = new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)) * Basis.FromScale(Collision.ToGodot(o.Scale)),
                Collision.ToGodot(o.Position)),
        };
        var begin = 0f;
        for (var i = 0; i < Math.Min(o.Lods, o.Models.Count); i++)
        {
            var model = o.Models[i];
            var end = Math.Min(model.DrawDistance, o.Radius);
            if (Instantiate(model.Model) is { } instance)
            {
                foreach (var mesh in FindAll<GeometryInstance3D>(instance))
                {
                    mesh.VisibilityRangeBegin = begin;
                    mesh.VisibilityRangeEnd = end;
                }
                node.AddChild(instance);
            }
            begin = Math.Max(0, model.DrawDistance - 2);
        }
        return node;
    }

    private Node3D? Instantiate(RecordId model)
    {
        if (model.Type != RecordTypes.Actor)
            return _models!.Instantiate(model);
        return _models!.InstantiateActor(_plugins!.Get<ActorRecord>(model))?.Node;
    }

    private static bool IsNever(FlagGate gate) => gate.Flag.Key switch
    {
        "0" => !gate.Invert,
        "1" => gate.Invert,
        _ => false,
    };

    public static List<T> FindAll<T>(Node root) where T : Node
    {
        var found = new List<T>();
        var stack = new Stack<Node>([root]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is T t)
                found.Add(t);
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
        return found;
    }
}
