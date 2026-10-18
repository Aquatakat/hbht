using System.Numerics;
using System.Text.Json;

namespace Aidyn.Plugins.Assets;

/// <summary>One of a scene's animations as an actor plays it.</summary>
/// <param name="Length">One loop of it in ticks: the Borg6's length, which the exporter keeps in its extras.</param>
/// <param name="Root">
/// Where the actor's root transform stands at each tick, from time 1 at index 0 to time <c>Length + 1</c>, which is time
/// 1 again: the glTF's sample, its translation plus its pivot turned and scaled by the sample's rotation and scale, as
/// the runtime draws it. Null when the animation doesn't drive the root, or holds the scene still.
/// </param>
public sealed record ActorClip(int Length, IReadOnlyList<Vector3>? Root);

/// <summary>
/// A scene's animations, read from its glTF for an actor whose root is one of its transforms: each one's length, and the
/// root's position at each tick, which the core times an actor's states and moves its body by. The exporter bakes each
/// Borg6 at one sample a tick (docs/formats/animation.md), so the samples are the original's values at each tick.
/// </summary>
public sealed class ActorClips
{
    private readonly Dictionary<int, ActorClip> _clips;

    private ActorClips(Dictionary<int, ActorClip> clips, Vector3 pivot, bool animated)
    {
        _clips = clips;
        Pivot = pivot;
        Animated = animated;
    }

    /// <summary>The root transform's pivot, which the exporter keeps in the node's extras.</summary>
    public Vector3 Pivot { get; }

    /// <summary>Whether the scene has a glTF animation, without which Godot gives it no player.</summary>
    public bool Animated { get; }

    /// <summary>The Borg6 <paramref name="animation"/>'s clip, or null if the scene has none of it.</summary>
    public ActorClip? Clip(RecordId animation) =>
        _clips.GetValueOrDefault(Convert.ToInt32(animation.Key, 16));

    /// <summary>The scene's clips for an actor rooted at <paramref name="rootTransform"/>, or null if no plugin has its asset.</summary>
    public static ActorClips? For(PluginSet plugins, RecordId scene, int rootTransform) =>
        plugins.FindAsset(scene, "glb") is { } path ? Read(File.ReadAllBytes(path), rootTransform) : null;

    public static ActorClips Read(byte[] glb, int rootTransform)
    {
        using var file = GlbReader.Parse(glb);
        return Read(file, SceneExtras.Read(file), rootTransform);
    }

    /// <summary>The clips of an already open file, whose <paramref name="extras"/> are read.</summary>
    public static ActorClips Read(GlbReader file, SceneExtras extras, int rootTransform)
    {
        var root = file.Root;

        var rootName = SceneNames.Transform(rootTransform);
        var rootNode = -1;
        var pivot = extras.Pivots.GetValueOrDefault(rootTransform);
        if (root.TryGetProperty("nodes", out var nodes))
        {
            for (var i = 0; i < nodes.GetArrayLength(); i++)
            {
                if (nodes[i].TryGetProperty("name", out var name) && name.GetString() == rootName)
                {
                    rootNode = i;
                    break;
                }
            }
        }

        var clips = new Dictionary<int, ActorClip>();
        var animated = false;
        if (root.TryGetProperty("animations", out var animations))
        {
            foreach (var animation in animations.EnumerateArray())
            {
                animated = true;
                var animationExtras = animation.GetProperty("extras");
                var length = animationExtras.GetProperty(ExtrasKeys.Length).GetInt32();
                clips[animationExtras.GetProperty(ExtrasKeys.Borg6).GetInt32()] = new ActorClip(length, RootTrack(animation));
            }
        }
        foreach (var still in extras.StillAnimations)
            clips[still.Borg6] = new ActorClip(still.Length, null);
        return new ActorClips(clips, pivot, animated);

        Vector3[]? RootTrack(JsonElement animation)
        {
            if (rootNode < 0)
                return null;
            float[]? translation = null, rotation = null, scale = null;
            var samplers = animation.GetProperty("samplers");
            foreach (var channel in animation.GetProperty("channels").EnumerateArray())
            {
                var target = channel.GetProperty("target");
                if (!target.TryGetProperty("node", out var n) || n.GetInt32() != rootNode)
                    continue;
                var output = samplers[channel.GetProperty("sampler").GetInt32()].GetProperty("output").GetInt32();
                switch (target.GetProperty("path").GetString())
                {
                    case "translation": translation = file.Floats(output); break;
                    case "rotation": rotation = file.Floats(output); break;
                    case "scale": scale = file.Floats(output); break;
                }
            }
            if (translation is null)
                return null;
            var positions = new Vector3[translation.Length / 3];
            for (var i = 0; i < positions.Length; i++)
            {
                var at = new Vector3(translation[3 * i], translation[3 * i + 1], translation[3 * i + 2]);
                if (pivot != Vector3.Zero)
                {
                    var q = rotation is null ? Quaternion.Identity
                        : new Quaternion(rotation[4 * i], rotation[4 * i + 1], rotation[4 * i + 2], rotation[4 * i + 3]);
                    var size = scale is null ? Vector3.One : new Vector3(scale[3 * i], scale[3 * i + 1], scale[3 * i + 2]);
                    at += Turn(q, pivot * size);
                }
                positions[i] = at;
            }
            return positions;
        }
    }

    /// <summary><paramref name="v"/> turned by <paramref name="q"/>, in single precision as Godot turns a vector by a quaternion.</summary>
    private static Vector3 Turn(Quaternion q, Vector3 v)
    {
        var u = new Vector3(q.X, q.Y, q.Z);
        var uv = Vector3.Cross(u, v);
        return v + (uv * q.W + Vector3.Cross(u, uv)) * 2;
    }
}
