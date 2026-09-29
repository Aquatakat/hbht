using Aidyn.Core.Physics;
using Aidyn.Core.Scenes;
using Aidyn.Inspector;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Keeps the 3×3 chunks around a point loaded, as the original's zone engine does
/// (<c>ZoneDatMtx</c>): each chunk's scenery, its see-through scenery, the models its scene
/// objects place, and its collision, which goes into the <see cref="CollisionWorld"/>.
/// </summary>
public partial class ChunkStreamer : Node3D
{
    private readonly PluginSet? _plugins;
    private readonly Models? _models;
    private readonly MapSetRecord? _set;
    private readonly CollisionWorld? _world;
    private readonly Dictionary<(int Row, int Column), ChunkEntry> _chunks = [];
    private readonly Dictionary<(int Row, int Column), Node3D> _loaded = [];
    private readonly Dictionary<(int Row, int Column), List<Draw>> _draws = [];
    private readonly Dictionary<(int Row, int Column), List<Node3D>> _billboards = [];
    private (int Row, int Column) _centre;

    /// <summary>
    /// A copy of a scene with scrolling textures that the original may draw: a chunk's own, when
    /// the chunk is drawn, or a scene object's level of detail, when it's also within
    /// <see cref="Radius"/> of the player and from <see cref="Near"/> to <see cref="Far"/> away
    /// (<c>RenderVoxelScenes</c>).
    /// </summary>
    private readonly record struct Draw(ScrollingScene Scene, Vector3? Position = null, float Radius = 0, float Near = 0, float Far = 0);

    public ChunkStreamer()
    {
    }

    public ChunkStreamer(PluginSet plugins, Models models, MapSetRecord set, CollisionWorld world)
    {
        _plugins = plugins;
        _models = models;
        _set = set;
        _world = world;
        Name = "Chunks";
        // Actors here animate with the frame, not the physics tick, so there's nothing to interpolate.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        foreach (var chunk in set.Chunks)
            _chunks[(chunk.Row, chunk.Column)] = chunk;
    }

    public MapSetRecord MapSet => _set!;

    /// <summary>The chunk a point is in: rows run along X and columns along Z.</summary>
    public (int Row, int Column) CellAt(Vector3 position) =>
        ((int)Math.Floor(position.X / MapSet.CellSize), (int)Math.Floor(position.Z / MapSet.CellSize));

    public ChunkEntry? ChunkAt(Vector3 position) => _chunks.GetValueOrDefault(CellAt(position));

    public ChunkEntry? Find(string name) => _chunks.Values.FirstOrDefault(c => c.Name == name);

    public Vector3 Origin(ChunkEntry chunk) => MapSet.Origin(chunk.Row, chunk.Column).ToGodot();

    /// <summary>Loads the chunks around <paramref name="position"/> and frees the rest. Returns whether any changed.</summary>
    public bool Update(Vector3 position)
    {
        var (row, column) = _centre = CellAt(position);
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
            _draws.Remove(cell);
            _billboards.Remove(cell);
            _world!.Remove(cell);
            changed = true;
        }
        foreach (var cell in wanted.Where(k => !_loaded.ContainsKey(k)))
        {
            var draws = _draws[cell] = [];
            var node = Load(_chunks[cell], draws);
            AddChild(node);
            _loaded[cell] = node;
            _billboards[cell] = Billboards.Facings(node);
            changed = true;
        }
        return changed;
    }

    /// <summary>The loaded chunks' billboards.</summary>
    public IEnumerable<Node3D> BillboardFacings => _billboards.Values.SelectMany(b => b);

    /// <summary>
    /// The scenes with scrolling textures the original would draw this frame: the centre
    /// chunk's, a neighbour's when it's in the camera's view (<see cref="ChunkView"/>), and the
    /// scene objects of those chunks within their ranges of the player.
    /// </summary>
    /// <remarks>
    /// The original also draws only the objects its collision grid lists for the player's square,
    /// or for the squares along a neighbour's edge, which isn't ported.
    /// </remarks>
    /// <param name="player">The player's position, which the chunks are centred on.</param>
    /// <param name="camera">The camera's position.</param>
    /// <param name="look">The way the camera looks.</param>
    public IEnumerable<ScrollingScene> DrawnScenes(Vector3 player, Vector3 camera, Vector3 look)
    {
        var corner = MapSet.Origin(_centre.Row, _centre.Column).ToGodot();
        var eye = new System.Numerics.Vector2(camera.X - corner.X, camera.Z - corner.Z);
        var flat = new System.Numerics.Vector2(look.X, look.Z);
        flat = flat == default ? System.Numerics.Vector2.UnitX : System.Numerics.Vector2.Normalize(flat);
        var cell = new System.Numerics.Vector2(MapSet.CellSize);
        foreach (var ((row, column), draws) in _draws)
        {
            if (!ChunkView.Drawn(eye, flat, row - _centre.Row, column - _centre.Column, cell))
                continue;
            foreach (var draw in draws)
            {
                if (draw.Position is not { } position)
                {
                    yield return draw.Scene;
                    continue;
                }
                var distance = player.DistanceTo(position);
                if (distance < draw.Radius && distance >= draw.Near && distance <= draw.Far)
                    yield return draw.Scene;
            }
        }
    }

    private Node3D Load(ChunkEntry chunk, List<Draw> draws)
    {
        var root = new Node3D { Name = chunk.Name, Position = Origin(chunk) };
        foreach (var scene in new[] { chunk.Scene, chunk.AltScene }.OfType<RecordId>())
        {
            if (_models!.Instantiate(scene) is not { } node)
                continue;
            root.AddChild(node);
            if (_models.Scrolls.Attach(scene, node) is { } scrolling)
                draws.Add(new Draw(scrolling));
        }

        // Toggles stay as they're stored until a trigger changes them, which needs the flag engine (M2).
        if (_plugins!.FindAsset(chunk.Map, "glb") is { } collision)
            _world!.Add((chunk.Row, chunk.Column), CollisionMesh.Read(File.ReadAllBytes(collision)), MapSet.Origin(chunk.Row, chunk.Column));

        var scenery = new Node3D { Name = "Scenery" };
        root.AddChild(scenery);
        foreach (var o in _plugins.Get<MapRecord>(chunk.Map).Objects.OfType<SceneObject>())
        {
            if (Place(o, root.Position, draws) is { } node)
                scenery.AddChild(node);
        }
        return root;
    }

    /// <summary>A scene object's levels of detail, each drawn over its own range of distances.</summary>
    private Node3D? Place(SceneObject o, Vector3 origin, List<Draw> draws)
    {
        // The runtime plays with the Expansion Pak. Objects gated on real flags wait for the flag
        // engine (M2), so for now only those that can never appear are left out.
        if (o.Pak == PakVariant.Without || IsNever(o.Visible))
            return null;

        var q = o.Orientation();
        var node = new Node3D
        {
            Name = $"Object {o.Index}",
            Transform = new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)) * Basis.FromScale(o.Scale.ToGodot()),
                o.Position.ToGodot()),
        };
        var begin = 0f;
        for (var i = 0; i < Math.Min(o.Lods, o.Models.Count); i++)
        {
            var model = o.Models[i];
            var end = Math.Min(model.DrawDistance, o.Radius);
            if (Instantiate(model.Model, o.Flags) is { } instance)
            {
                // The original averages a tinted object's tint with the scenery's (SetSceneColors).
                // Actors are lit instead.
                if (o.Flags.HasFlag(SceneObjectFlags.Tint) && o.Tint != default && model.Model.Type != RecordTypes.Actor)
                    N64Material.SetObjectTint(instance, Color.Color8(o.Tint.R, o.Tint.G, o.Tint.B));
                foreach (var mesh in FindAll<GeometryInstance3D>(instance))
                {
                    mesh.VisibilityRangeBegin = begin;
                    mesh.VisibilityRangeEnd = end;
                }
                node.AddChild(instance);
                // The original measures levels of detail from the player; this draws them by the
                // camera's distance, which Godot's visibility ranges take.
                var scene = model.Model.Type == RecordTypes.Actor ? _plugins!.Get<ActorRecord>(model.Model).Scene : model.Model;
                if (_models!.Scrolls.Attach(scene, instance) is { } scrolling)
                    draws.Add(new Draw(scrolling, origin + o.Position.ToGodot(), o.Radius, i == 0 ? float.NegativeInfinity : begin, model.DrawDistance));
            }
            begin = Math.Max(0, model.DrawDistance - 2);
        }
        return node;
    }

    /// <summary>
    /// A scene object's model. Its actors are lit by the tint and the sun, unless they're
    /// fullbright, when they're tinted like scenery (<c>set_sun_light</c>).
    /// </summary>
    private Node3D? Instantiate(RecordId model, SceneObjectFlags flags)
    {
        if (model.Type != RecordTypes.Actor)
            return _models!.Instantiate(model);
        var lighting = flags.HasFlag(SceneObjectFlags.Fullbright) ? N64Material.Lighting.Scenery : N64Material.Lighting.SceneObject;
        return _models!.InstantiateActor(_plugins!.Get<ActorRecord>(model), lighting)?.Node;
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
