using Aidyn.Core.Scenes;
using Aidyn.Inspector;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// A chunk's scenery as nodes, at the chunk's origin: its scene, its see-through scene, and the
/// levels of detail of its scene objects, hidden until <see cref="Fade"/> or
/// <see cref="Level.Show"/> shows them. The runtime's <see cref="ChunkStreamer"/> keeps the 3×3
/// around the player, and the editor draws a map set's with it.
/// </summary>
public sealed class ChunkScenery
{
    private ChunkScenery(ChunkEntry chunk, Node3D root)
    {
        Chunk = chunk;
        Root = root;
    }

    public ChunkEntry Chunk { get; }

    /// <summary>The chunk's node, at its origin in the map set.</summary>
    public Node3D Root { get; }

    /// <summary>The chunk's own scenes that scroll textures, which are drawn when the chunk is.</summary>
    public List<ScrollingScene> Own { get; } = [];

    /// <summary>Its scene objects.</summary>
    public List<PlacedObject> Objects { get; } = [];

    /// <summary>Its billboards' facing nodes (<see cref="Billboards"/>).</summary>
    public List<Node3D> Billboards { get; private set; } = [];

    /// <summary>
    /// Builds a chunk's scenery. Its scene objects are placed if <paramref name="placed"/> says so,
    /// which the runtime decides as the chunk loads, as the original does
    /// (<c>VoxelObjects_Activate</c>), not every frame.
    /// </summary>
    public static ChunkScenery Load(PluginSet plugins, Models models, MapSetRecord set, ChunkEntry chunk, Func<SceneObject, bool> placed)
    {
        var scenery = new ChunkScenery(chunk, new Node3D { Name = chunk.Name, Position = set.Origin(chunk.Row, chunk.Column).ToGodot() });
        foreach (var scene in new[] { chunk.Scene, chunk.AltScene }.OfType<RecordId>())
        {
            if (models.Instantiate(scene) is not { } node)
                continue;
            scenery.Root.AddChild(node);
            if (models.Scrolls.Attach(scene, node) is { } scrolling)
                scenery.Own.Add(scrolling);
        }

        var objects = new Node3D { Name = "Scenery" };
        scenery.Root.AddChild(objects);
        foreach (var o in plugins.Get<MapRecord>(chunk.Map).Objects.OfType<SceneObject>())
        {
            if (placed(o))
                objects.AddChild(scenery.Place(plugins, models, o).Node);
        }
        scenery.Billboards = global::Aidyn.Game.Billboards.Facings(scenery.Root);
        return scenery;
    }

    /// <summary>
    /// Shows the levels of detail of each scene object that the original would draw from this
    /// view, faded as it fades them (<see cref="SceneObjectFade"/>). Godot draws a faded one
    /// blended, after the opaque scenery, without writing depth, as the original does, and its
    /// cut-outs blended rather than cut (<see cref="N64Material.Faded"/>).
    /// </summary>
    public void Fade(in SceneView view, DrawDistance range)
    {
        Span<byte> alphas = stackalloc byte[3];
        foreach (var placed in Objects)
        {
            SceneObjectFade.Alphas(placed.Object, placed.Origin, view, alphas, range);
            for (var i = 0; i < placed.Levels.Length; i++)
            {
                if (placed.Levels[i] is { } level && level.Alpha != alphas[i])
                    level.Show(alphas[i]);
            }
        }
    }

    /// <summary>The scenes that scroll textures and are drawn with the chunk: its own, and the levels of detail last shown.</summary>
    public IEnumerable<ScrollingScene> Scrolling()
    {
        foreach (var scene in Own)
            yield return scene;
        foreach (var placed in Objects)
        {
            foreach (var level in placed.Levels)
            {
                if (level is { Alpha: > 0, Scrolling: { } scrolling })
                    yield return scrolling;
            }
        }
    }

    /// <summary>A scene object's levels of detail, each hidden.</summary>
    private PlacedObject Place(PluginSet plugins, Models models, SceneObject o)
    {
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
            if (Instantiate(plugins, models, model.Model, o.Flags) is not { } instance)
                continue;
            // The original averages a tinted object's tint with the scenery's (SetSceneColors).
            // Actors are lit instead.
            if (o.Flags.HasFlag(SceneObjectFlags.Tint) && o.Tint != default && model.Model.Type != RecordTypes.Actor)
                N64Material.SetObjectTint(instance, Color.Color8(o.Tint.R, o.Tint.G, o.Tint.B));
            instance.Visible = false;
            node.AddChild(instance);
            var scene = model.Model.Type == RecordTypes.Actor ? plugins.Get<ActorRecord>(model.Model).Scene : model.Model;
            levels[i] = new Level(instance, models.Scrolls.Attach(scene, instance));
        }
        var placed = new PlacedObject(o, node, Root.Position, levels);
        Objects.Add(placed);
        return placed;
    }

    /// <summary>
    /// A scene object's model. Its actors are lit by the tint and the sun, unless they're
    /// fullbright, when they're tinted like scenery (<c>set_sun_light</c>).
    /// </summary>
    private static Node3D? Instantiate(PluginSet plugins, Models models, RecordId model, SceneObjectFlags flags)
    {
        if (model.Type != RecordTypes.Actor)
            return models.Instantiate(model);
        var lighting = flags.HasFlag(SceneObjectFlags.Fullbright) ? N64Material.Lighting.Scenery : N64Material.Lighting.SceneObject;
        return models.InstantiateActor(plugins.Get<ActorRecord>(model), lighting)?.Node;
    }
}

/// <summary>A scene object's node and its levels of detail, nearest first.</summary>
public sealed class PlacedObject(SceneObject o, Node3D node, Vector3 origin, Level?[] levels)
{
    public SceneObject Object { get; } = o;

    /// <summary>The object's node, under its chunk's, which holds a node per level.</summary>
    public Node3D Node { get; } = node;

    /// <summary>Its chunk's origin.</summary>
    public System.Numerics.Vector3 Origin { get; } = origin.ToNumerics();

    /// <summary>Null where the model has no asset.</summary>
    public Level?[] Levels { get; } = levels;
}

/// <summary>A level of detail's model, and the alpha it was last drawn at.</summary>
public sealed class Level(Node3D node, ScrollingScene? scrolling)
{
    public Node3D Node { get; } = node;
    public GeometryInstance3D[] Meshes { get; } = [.. Nodes.FindAll<GeometryInstance3D>(node)];
    public ScrollingScene? Scrolling { get; } = scrolling;
    public byte Alpha { get; private set; }

    /// <summary>The surfaces whose materials change while the model is faded: its cut-outs.</summary>
    private readonly (MeshInstance3D Mesh, int Surface, Material Opaque, Material Faded)[] _cutOuts =
    [
        .. Nodes.FindAll<MeshInstance3D>(node).SelectMany(mesh => Enumerable.Range(0, mesh.GetSurfaceOverrideMaterialCount())
            .Select(surface => (mesh, surface, material: mesh.GetSurfaceOverrideMaterial(surface)))
            .Where(s => s.material is not null && N64Material.Faded(s.material) is not null)
            .Select(s => (s.mesh, s.surface, s.material, (Material)N64Material.Faded(s.material)!))),
    ];

    /// <summary>Shows the model at an alpha, hiding it at 0.</summary>
    public void Show(byte alpha)
    {
        var faded = alpha is > 0 and < 0xFF;
        if (faded != (Alpha is > 0 and < 0xFF))
        {
            foreach (var (mesh, surface, opaque, fadedMaterial) in _cutOuts)
                mesh.SetSurfaceOverrideMaterial(surface, faded ? fadedMaterial : opaque);
        }
        Node.Visible = alpha != 0;
        Alpha = alpha;
        var transparency = faded ? 1 - alpha / 255f : 0;
        foreach (var mesh in Meshes)
            mesh.Transparency = transparency;
    }
}
