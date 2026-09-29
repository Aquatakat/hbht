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
    private readonly Dictionary<(int Row, int Column), ChunkScenes> _scenes = [];
    private readonly Dictionary<(int Row, int Column), List<Node3D>> _billboards = [];
    private (int Row, int Column) _centre;

    /// <summary>A chunk's scrolling scenes, which are drawn when the chunk is, and its scene objects.</summary>
    private sealed class ChunkScenes
    {
        public List<ScrollingScene> Own { get; } = [];
        public List<PlacedObject> Objects { get; } = [];
    }

    /// <summary>A scene object and its levels of detail, which <see cref="Fade"/> shows and fades.</summary>
    private sealed class PlacedObject(SceneObject o, Vector3 origin, Level?[] levels)
    {
        public SceneObject Object { get; } = o;
        public System.Numerics.Vector3 Origin { get; } = origin.ToNumerics();
        public Level?[] Levels { get; } = levels;
    }

    /// <summary>A level of detail's model, and the alpha it was last drawn at.</summary>
    private sealed class Level(Node3D node, ScrollingScene? scrolling)
    {
        public Node3D Node { get; } = node;
        public GeometryInstance3D[] Meshes { get; } = [.. FindAll<GeometryInstance3D>(node)];
        public ScrollingScene? Scrolling { get; } = scrolling;
        public byte Alpha { get; set; }
    }

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
            _scenes.Remove(cell);
            _billboards.Remove(cell);
            _world!.Remove(cell);
            changed = true;
        }
        foreach (var cell in wanted.Where(k => !_loaded.ContainsKey(k)))
        {
            var scenes = _scenes[cell] = new ChunkScenes();
            var node = Load(_chunks[cell], scenes);
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
    /// Shows the levels of detail of each scene object that the original would draw from this
    /// view, faded as it fades them (<see cref="SceneObjectFade"/>). Godot draws a faded one
    /// blended, after the opaque scenery, without writing depth, as the original does.
    /// </summary>
    public void Fade(in SceneView view, DrawDistance range)
    {
        Span<byte> alphas = stackalloc byte[3];
        foreach (var scenes in _scenes.Values)
        {
            foreach (var placed in scenes.Objects)
            {
                SceneObjectFade.Alphas(placed.Object, placed.Origin, view, alphas, range);
                for (var i = 0; i < placed.Levels.Length; i++)
                {
                    if (placed.Levels[i] is not { } level || level.Alpha == alphas[i])
                        continue;
                    level.Node.Visible = alphas[i] != 0;
                    level.Alpha = alphas[i];
                    var transparency = alphas[i] is 0 or 0xFF ? 0 : 1 - alphas[i] / 255f;
                    foreach (var mesh in level.Meshes)
                        mesh.Transparency = transparency;
                }
            }
        }
    }

    /// <summary>
    /// The scenes with scrolling textures the original would draw this frame: the centre chunk's,
    /// a neighbour's when it's in the camera's view (<see cref="ChunkView"/>), and the levels of
    /// detail of those chunks' scene objects that <see cref="Fade"/> last showed.
    /// </summary>
    /// <remarks>
    /// The original also draws only the objects its collision grid lists for the player's square,
    /// or for the squares along a neighbour's edge, which isn't ported.
    /// </remarks>
    /// <param name="camera">The camera's position.</param>
    /// <param name="look">The way the camera looks.</param>
    public IEnumerable<ScrollingScene> DrawnScenes(Vector3 camera, Vector3 look)
    {
        var corner = MapSet.Origin(_centre.Row, _centre.Column).ToGodot();
        var eye = new System.Numerics.Vector2(camera.X - corner.X, camera.Z - corner.Z);
        var flat = new System.Numerics.Vector2(look.X, look.Z);
        flat = flat == default ? System.Numerics.Vector2.UnitX : System.Numerics.Vector2.Normalize(flat);
        var cell = new System.Numerics.Vector2(MapSet.CellSize);
        foreach (var ((row, column), scenes) in _scenes)
        {
            if (!ChunkView.Drawn(eye, flat, row - _centre.Row, column - _centre.Column, cell))
                continue;
            foreach (var scene in scenes.Own)
                yield return scene;
            foreach (var placed in scenes.Objects)
            {
                foreach (var level in placed.Levels)
                {
                    if (level is { Alpha: > 0, Scrolling: { } scrolling })
                        yield return scrolling;
                }
            }
        }
    }

    private Node3D Load(ChunkEntry chunk, ChunkScenes scenes)
    {
        var root = new Node3D { Name = chunk.Name, Position = Origin(chunk) };
        foreach (var scene in new[] { chunk.Scene, chunk.AltScene }.OfType<RecordId>())
        {
            if (_models!.Instantiate(scene) is not { } node)
                continue;
            root.AddChild(node);
            if (_models.Scrolls.Attach(scene, node) is { } scrolling)
                scenes.Own.Add(scrolling);
        }

        // Toggles stay as they're stored until a trigger changes them, which needs the flag engine (M2).
        if (_plugins!.FindAsset(chunk.Map, "glb") is { } collision)
            _world!.Add((chunk.Row, chunk.Column), CollisionMesh.Read(File.ReadAllBytes(collision)), MapSet.Origin(chunk.Row, chunk.Column));

        var scenery = new Node3D { Name = "Scenery" };
        root.AddChild(scenery);
        foreach (var o in _plugins.Get<MapRecord>(chunk.Map).Objects.OfType<SceneObject>())
        {
            if (Place(o, root.Position, scenes) is { } node)
                scenery.AddChild(node);
        }
        return root;
    }

    /// <summary>A scene object's levels of detail, hidden until <see cref="Fade"/> shows them.</summary>
    private Node3D? Place(SceneObject o, Vector3 origin, ChunkScenes scenes)
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
        var levels = new Level?[Math.Min(o.Lods, o.Models.Count)];
        for (var i = 0; i < levels.Length; i++)
        {
            var model = o.Models[i];
            if (Instantiate(model.Model, o.Flags) is not { } instance)
                continue;
            // The original averages a tinted object's tint with the scenery's (SetSceneColors).
            // Actors are lit instead.
            if (o.Flags.HasFlag(SceneObjectFlags.Tint) && o.Tint != default && model.Model.Type != RecordTypes.Actor)
                N64Material.SetObjectTint(instance, Color.Color8(o.Tint.R, o.Tint.G, o.Tint.B));
            instance.Visible = false;
            node.AddChild(instance);
            var scene = model.Model.Type == RecordTypes.Actor ? _plugins!.Get<ActorRecord>(model.Model).Scene : model.Model;
            levels[i] = new Level(instance, _models!.Scrolls.Attach(scene, instance));
        }
        scenes.Objects.Add(new PlacedObject(o, origin, levels));
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
