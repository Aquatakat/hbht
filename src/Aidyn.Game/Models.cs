using System.Text.Json;
using Aidyn.Inspector;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Scene assets as Godot nodes. Each glTF is parsed once, and every use gets a copy, so a forest
/// of one tree costs one parse.
/// </summary>
public sealed class Models(PluginSet plugins) : IDisposable
{
    private readonly Dictionary<RecordId, Node3D?> _templates = [];
    private readonly Dictionary<RecordId, byte[]?> _files = [];

    /// <summary>The scenes' scrolling textures, which each copy of a scene has its own of.</summary>
    public TextureScrolls Scrolls { get; } = new();

    /// <summary>A new copy of the scene's nodes, or null if no plugin has its asset.</summary>
    public Node3D? Instantiate(RecordId scene)
    {
        if (!_templates.TryGetValue(scene, out var template))
            _templates[scene] = template = Load(scene);
        return template?.Duplicate() as Node3D;
    }

    /// <summary>
    /// An actor's scene with its head, if it has one, and an animator running its state machine.
    /// </summary>
    /// <param name="lighting">How it's lit: as a body, unless it's a scene object's.</param>
    internal (Node3D Node, ActorAnimator? Animator)? InstantiateActor(ActorRecord actor, N64Material.Lighting lighting = N64Material.Lighting.Body)
    {
        if (Instantiate(actor.Scene) is not { } node)
            return null;
        if (actor.Head is { } head && Instantiate(head.Scene) is { } headNode)
        {
            // The original aligns the head with the locator but keeps it upright, and turns it
            // toward things the player could use. Parenting it follows the neck, which is close.
            var locatorName = $"locator {head.Locator}";
            if (ChunkStreamer.FindAll<Node3D>(node).FirstOrDefault(n => n.Name == locatorName) is { } locator)
                locator.AddChild(headNode);
            else
                GD.PushWarning($"{actor.Scene} has no {locatorName} for its head.");
        }
        N64Material.SetLighting(node, lighting);
        return (node, ActorAnimator.Attach(node, actor, Glb(actor.Scene)!));
    }

    /// <summary>The asset's glTF, for what Godot's importer doesn't keep, such as node extras.</summary>
    public byte[]? Glb(RecordId scene)
    {
        if (!_files.TryGetValue(scene, out var bytes))
            _files[scene] = bytes = plugins.FindAsset(scene, "glb") is { } path ? File.ReadAllBytes(path) : null;
        return bytes;
    }

    /// <summary>Frees the parsed templates, which are never in the tree.</summary>
    public void Dispose()
    {
        foreach (var template in _templates.Values)
            template?.Free();
        _templates.Clear();
    }

    private Node3D? Load(RecordId scene)
    {
        if (Glb(scene) is not { } glb)
        {
            GD.PushWarning($"No asset for {scene}.");
            return null;
        }
        // A scene with no geometry has no binary chunk, which Godot's reader warns about.
        if (glb.Length == 20 + BitConverter.ToInt32(glb, 12))
            return new Node3D { Name = scene.ToString().Replace(':', '_').Replace('/', '_') };
        var document = new GltfDocument();
        var state = new GltfState();
        if (document.AppendFromBuffer(glb, "", state) != Error.Ok || document.GenerateScene(state) is not Node3D node)
        {
            GD.PushWarning($"Godot couldn't load {scene}'s glTF.");
            return null;
        }
        N64Material.Apply(glb, state, node);
        node.Name = scene.ToString().Replace(':', '_').Replace('/', '_');
        using (var json = JsonDocument.Parse(glb.AsMemory(20, BitConverter.ToInt32(glb, 12))))
            Scrolls.Add(scene, json.RootElement);
        // Billboards are turned to the camera in world space, whatever their parents do.
        foreach (var facing in Billboards.Facings(node))
            facing.TopLevel = true;
        return node;
    }
}
