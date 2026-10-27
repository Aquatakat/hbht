using HBHT.Plugins.Assets;
using Godot;

namespace HBHT.Views;

/// <summary>
/// Loading the importer's glTF binaries (<c>.glb</c>) into a Godot node tree with the N64's materials.
/// The runtime and the editor both load scenes through here.
/// </summary>
public static class GlbFile
{
    /// <summary>
    /// The rate Godot bakes the glTF's animations at as it loads them: the importer's, a sample a tick. Godot
    /// resamples every translation, rotation and scale track at the rate <c>GenerateScene</c> is given (which overrides
    /// the state's), 30 a second by default, and the views pose a scene every tick, so on every other tick a node stood
    /// halfway between two samples, its translation and rotation interpolated apart. A node turning fast about a pivot
    /// away from its origin then left the pivot: the purple cube on Ardra's sceptre in the ending's fourth shot (scene
    /// 0x2097's transform 145, turning 100° a tick) flew up to 1.5 units off the sceptre on the even ticks.
    /// </summary>
    public const float BakeFps = SceneNames.AnimationSamplesPerSecond;

    /// <summary>
    /// Loads the binary as Godot's glTF import does, then replaces its materials with
    /// <see cref="N64Material"/>'s. Null, with the reason in <paramref name="error"/>, if Godot can't.
    /// </summary>
    public static Node3D? Load(GlbScene glb, out Error error)
    {
        var document = new GltfDocument();
        var state = new GltfState();
        error = document.AppendFromBuffer(glb.Bytes, "", state);
        if (error != Error.Ok)
            return null;
        if (document.GenerateScene(state, BakeFps) is not Node3D scene)
        {
            error = Error.Failed;
            return null;
        }
        N64Material.Apply(glb.Reader.Root, state, scene);
        return scene;
    }

    /// <summary>
    /// The node tree of a binary with no geometry, and so no binary chunk, which Godot's reader warns about: each node with its
    /// name and transform, from the glTF's own scene. A particle-only actor's scene is one, whose locators place its emitters.
    /// </summary>
    public static Node3D LoadNodes(GlbScene glb, string name)
    {
        var root = new Node3D { Name = name };
        var json = glb.Reader.Root;
        if (!json.TryGetProperty("nodes", out var nodes))
            return root;
        var scene = json.TryGetProperty("scene", out var s) ? s.GetInt32() : 0;
        if (json.TryGetProperty("scenes", out var scenes) && scene < scenes.GetArrayLength()
            && scenes[scene].TryGetProperty("nodes", out var top))
        {
            foreach (var i in top.EnumerateArray())
                root.AddChild(Build(nodes, i.GetInt32()));
        }
        foreach (var node in Nodes.FindAll<Node>(root))
        {
            if (node != root)
                node.Owner = root;
        }
        return root;
    }

    private static Node3D Build(System.Text.Json.JsonElement nodes, int index)
    {
        var json = nodes[index];
        var node = new Node3D { Name = (json.TryGetProperty("name", out var n) ? n.GetString() : null) ?? $"node {index}" };
        float[] F(string key) => [.. json.GetProperty(key).EnumerateArray().Select(e => e.GetSingle())];
        if (json.TryGetProperty("matrix", out _))
        {
            var m = F("matrix");
            node.Transform = new Transform3D(new Basis(new Vector3(m[0], m[1], m[2]), new Vector3(m[4], m[5], m[6]), new Vector3(m[8], m[9], m[10])),
                new Vector3(m[12], m[13], m[14]));
        }
        else
        {
            var basis = Basis.Identity;
            if (json.TryGetProperty("rotation", out _))
            {
                var r = F("rotation");
                basis = new Basis(new Quaternion(r[0], r[1], r[2], r[3]));
            }
            if (json.TryGetProperty("scale", out _))
            {
                var c = F("scale");
                basis = basis.Scaled(new Vector3(c[0], c[1], c[2]));
            }
            var t = json.TryGetProperty("translation", out _) ? F("translation") : [0, 0, 0];
            node.Transform = new Transform3D(basis, new Vector3(t[0], t[1], t[2]));
        }
        if (json.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
                node.AddChild(Build(nodes, child.GetInt32()));
        }
        return node;
    }
}
