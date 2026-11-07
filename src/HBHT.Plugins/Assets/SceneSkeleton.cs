using System.Numerics;
using System.Text.Json;

namespace HBHT.Plugins.Assets;

/// <summary>
/// A scene's transforms as its glTF holds them, which the core poses a locator by without a view (ADR 0040): each node's parent and
/// rest, the nodes of its locators, and each animation's keys for the nodes it drives. The importer writes one key a tick, linear,
/// and every animation of an actor drives the same channels, so a node is posed at its key where the animation drives it and at rest
/// where it doesn't. That's what the views' posed scene comes to: Godot's player sets each driven channel at the key, and
/// <c>TransformTracks</c> those its glTF loader left out at rest (<see cref="DrivenNodes"/>).
/// </summary>
public sealed class SceneSkeleton
{
    private readonly Node[] _nodes;
    private readonly int[] _locators;
    private readonly Dictionary<int, Dictionary<int, Channels>> _animations;
    private readonly Dictionary<string, int> _byName;

    private SceneSkeleton(Node[] nodes, Dictionary<int, Dictionary<int, Channels>> animations)
    {
        _nodes = nodes;
        _animations = animations;
        _byName = [];
        for (var i = 0; i < nodes.Length; i++)
            _byName.TryAdd(nodes[i].Name, i);
        _locators = [.. Enumerable.Range(0, Locators).Select(l => _byName.GetValueOrDefault(SceneNames.Locator(l), -1))];
    }

    /// <summary>The locators a scene can have, as an emitter names them (0 to 7).</summary>
    public const int Locators = 8;

    private sealed record Node(string Name, int Parent, Vector3 Translation, Quaternion Rotation, Vector3 Scale);

    /// <summary>One node's keys in one animation, a tick each: three floats a translation or a scale, four a rotation.</summary>
    private sealed record Channels(float[]? Translation, float[]? Rotation, float[]? Scale);

    public static SceneSkeleton Read(GlbReader file)
    {
        var root = file.Root;
        var nodes = new List<Node>();
        if (root.TryGetProperty("nodes", out var array))
        {
            var parents = new int[array.GetArrayLength()];
            Array.Fill(parents, -1);
            for (var i = 0; i < parents.Length; i++)
            {
                if (array[i].TryGetProperty("children", out var children))
                {
                    foreach (var child in children.EnumerateArray())
                        parents[child.GetInt32()] = i;
                }
            }
            for (var i = 0; i < parents.Length; i++)
            {
                var n = array[i];
                nodes.Add(new Node(n.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "", parents[i],
                    Vector(n, "translation", Vector3.Zero), Rotation(n), Vector(n, "scale", Vector3.One)));
            }
        }
        var animations = new Dictionary<int, Dictionary<int, Channels>>();
        if (root.TryGetProperty("animations", out var list))
        {
            foreach (var animation in list.EnumerateArray())
            {
                if (!animation.TryGetProperty("extras", out var extras) || !extras.TryGetProperty(ExtrasKeys.Borg6, out var borg6))
                    continue;
                var samplers = animation.GetProperty("samplers");
                var driven = new Dictionary<int, Channels>();
                foreach (var channel in animation.GetProperty("channels").EnumerateArray())
                {
                    var target = channel.GetProperty("target");
                    if (!target.TryGetProperty("node", out var node))
                        continue;
                    var keys = file.Floats(samplers[channel.GetProperty("sampler").GetInt32()].GetProperty("output").GetInt32());
                    var at = node.GetInt32();
                    var c = driven.GetValueOrDefault(at) ?? new Channels(null, null, null);
                    driven[at] = target.GetProperty("path").GetString() switch
                    {
                        "translation" => c with { Translation = keys },
                        "rotation" => c with { Rotation = keys },
                        "scale" => c with { Scale = keys },
                        _ => c,
                    };
                }
                animations[borg6.GetInt32()] = driven;
            }
        }
        return new SceneSkeleton([.. nodes], animations);
    }

    /// <summary>Whether the scene has locator <paramref name="locator"/>.</summary>
    public bool HasLocator(int locator) => locator is >= 0 and < Locators && _locators[locator] >= 0;

    /// <summary>Whether the Borg6 <paramref name="borg6"/> moves the scene: a glTF animation, which the views' player has, rather than a still one.</summary>
    public bool Animates(int borg6) => _animations.ContainsKey(borg6);

    /// <summary>The node named <paramref name="name"/>, or −1.</summary>
    public int Find(string name) => _byName.GetValueOrDefault(name, -1);

    /// <summary>
    /// Where locator <paramref name="locator"/> is in the scene's space, as a matrix (System.Numerics' row vectors, so its translation is
    /// the locator's place), with the scene posed at key <paramref name="key"/> of the Borg6 <paramref name="borg6"/>, or at rest for
    /// none; null if the scene has no such locator.
    /// </summary>
    /// <param name="key">The key, from 0, the views' clock less 1; past the last, the last, as Godot's player holds it.</param>
    /// <param name="heldRoot">
    /// The actor's root transform's node, whose position the views zero as the original draws it (<c>ActorAnimator.HoldRoot</c>): its
    /// translation is the pivot, scaled and turned as it's posed, negated. −1 for none, or an actor not yet posed by an animation.
    /// </param>
    /// <param name="pivot">The root transform's pivot (<see cref="SceneExtras.Pivots"/>).</param>
    public Matrix4x4? Locator(int locator, int? borg6, int key, int heldRoot = -1, Vector3 pivot = default)
    {
        if (!HasLocator(locator))
            return null;
        var driven = borg6 is { } b ? _animations.GetValueOrDefault(b) : null;
        var world = Matrix4x4.Identity;
        for (var i = _locators[locator]; i >= 0; i = _nodes[i].Parent)
        {
            var n = _nodes[i];
            var (t, r, s) = (n.Translation, n.Rotation, n.Scale);
            if (driven?.GetValueOrDefault(i) is { } c)
            {
                if (c.Translation is { } ts)
                    t = Vec3(ts, key);
                if (c.Rotation is { } rs)
                    r = Quat(rs, key);
                if (c.Scale is { } ss)
                    s = Vec3(ss, key);
            }
            if (i == heldRoot)
                t = -Vector3.Transform(pivot * s, r);
            world *= Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
        }
        return world;
    }

    private static Vector3 Vec3(float[] keys, int key)
    {
        var k = Math.Clamp(key, 0, keys.Length / 3 - 1) * 3;
        return new Vector3(keys[k], keys[k + 1], keys[k + 2]);
    }

    private static Quaternion Quat(float[] keys, int key)
    {
        var k = Math.Clamp(key, 0, keys.Length / 4 - 1) * 4;
        return Quaternion.Normalize(new Quaternion(keys[k], keys[k + 1], keys[k + 2], keys[k + 3]));
    }

    private static Vector3 Vector(JsonElement node, string key, Vector3 fallback) =>
        node.TryGetProperty(key, out var v) ? new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()) : fallback;

    private static Quaternion Rotation(JsonElement node) =>
        node.TryGetProperty("rotation", out var r) ? new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()) : Quaternion.Identity;
}
