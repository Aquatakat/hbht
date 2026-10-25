using System.Numerics;
using System.Text.Json;

namespace HBHT.Plugins.Assets;

/// <summary>
/// A node one of a scene's glTF animations drives, with its rest value for each channel the animation has a track for, and
/// null for each it hasn't.
/// </summary>
/// <param name="Node">The node's name, which is unique in the scene (<see cref="SceneNames.Transform"/> and the like).</param>
public sealed record DrivenNode(string Node, Vector3? Translation, Quaternion? Rotation, Vector3? Scale);

/// <summary>
/// What each of a scene's glTF animations drives: every node it has a channel for, with the node's rest values for those
/// channels. Godot's glTF loader leaves out a track whose every key is the node's rest value, so a node an animation holds at
/// rest keeps whatever the animation before it left (docs/formats/animation.md); the views pose those channels at rest from
/// here.
/// </summary>
public sealed class DrivenNodes
{
    private readonly Dictionary<string, IReadOnlyList<DrivenNode>> _byAnimation;

    private DrivenNodes(Dictionary<string, IReadOnlyList<DrivenNode>> byAnimation) => _byAnimation = byAnimation;

    /// <summary>The nodes the animation named <paramref name="animation"/> drives, none if the scene has no such animation.</summary>
    public IReadOnlyList<DrivenNode> Of(string animation) => _byAnimation.GetValueOrDefault(animation) ?? [];

    public static DrivenNodes Read(GlbReader file)
    {
        var root = file.Root;
        var byAnimation = new Dictionary<string, IReadOnlyList<DrivenNode>>();
        if (!root.TryGetProperty("animations", out var animations) || !root.TryGetProperty("nodes", out var nodes))
            return new DrivenNodes(byAnimation);
        foreach (var animation in animations.EnumerateArray())
        {
            if (!animation.TryGetProperty("name", out var nameProperty) || nameProperty.GetString() is not { } name)
                continue;
            var paths = new Dictionary<int, HashSet<string>>();
            foreach (var channel in animation.GetProperty("channels").EnumerateArray())
            {
                var target = channel.GetProperty("target");
                if (!target.TryGetProperty("node", out var n))
                    continue;
                var node = n.GetInt32();
                if (!paths.TryGetValue(node, out var set))
                    paths[node] = set = [];
                set.Add(target.GetProperty("path").GetString()!);
            }
            byAnimation[name] = [.. paths.OrderBy(p => p.Key).Select(p => Driven(nodes[p.Key], p.Value))];
        }
        return new DrivenNodes(byAnimation);
    }

    private static DrivenNode Driven(JsonElement node, HashSet<string> paths)
    {
        var name = node.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        return new DrivenNode(name,
            paths.Contains("translation") ? Vector(node, "translation", Vector3.Zero) : null,
            paths.Contains("rotation") ? Rotation(node) : null,
            paths.Contains("scale") ? Vector(node, "scale", Vector3.One) : null);
    }

    private static Vector3 Vector(JsonElement node, string key, Vector3 fallback)
    {
        if (!node.TryGetProperty(key, out var v))
            return fallback;
        return new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
    }

    private static Quaternion Rotation(JsonElement node)
    {
        if (!node.TryGetProperty("rotation", out var r))
            return Quaternion.Identity;
        return new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle());
    }
}
