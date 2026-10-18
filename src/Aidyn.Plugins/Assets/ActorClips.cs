using System.Buffers.Binary;
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
        if (glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(glb) != 0x46546C67)
            throw new InvalidDataException("Not a binary glTF file.");
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var root = json.RootElement;
        var bin = 20 + jsonLength + 8;

        var rootName = $"transform {rootTransform}";
        var rootNode = -1;
        var pivot = Vector3.Zero;
        if (root.TryGetProperty("nodes", out var nodes))
        {
            for (var i = 0; i < nodes.GetArrayLength(); i++)
            {
                var node = nodes[i];
                if (rootNode < 0 && node.TryGetProperty("name", out var name) && name.GetString() == rootName)
                    rootNode = i;
                if (node.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object
                    && extras.TryGetProperty("transform", out var t) && t.GetInt32() == rootTransform
                    && extras.TryGetProperty("pivot", out var p)
                    && !(extras.TryGetProperty("billboard", out var b) && b.GetBoolean()) && pivot == Vector3.Zero)
                    pivot = new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
            }
        }

        var clips = new Dictionary<int, ActorClip>();
        var animated = false;
        if (root.TryGetProperty("animations", out var animations))
        {
            foreach (var animation in animations.EnumerateArray())
            {
                animated = true;
                var extras = animation.GetProperty("extras");
                var length = extras.GetProperty("length").GetInt32();
                clips[extras.GetProperty("borg6").GetInt32()] = new ActorClip(length, RootTrack(animation));
            }
        }
        if (root.TryGetProperty("scenes", out var scenes) && scenes[root.TryGetProperty("scene", out var s) ? s.GetInt32() : 0] is var scene
            && scene.TryGetProperty("extras", out var sceneExtras) && sceneExtras.TryGetProperty("stillAnimations", out var still))
        {
            foreach (var animation in still.EnumerateArray())
                clips[animation.GetProperty("borg6").GetInt32()] = new ActorClip(animation.GetProperty("length").GetInt32(), null);
        }
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
                    case "translation": translation = Floats(output, 3); break;
                    case "rotation": rotation = Floats(output, 4); break;
                    case "scale": scale = Floats(output, 3); break;
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

        float[] Floats(int accessorIndex, int width)
        {
            var accessor = root.GetProperty("accessors")[accessorIndex];
            if (accessor.GetProperty("componentType").GetInt32() != 5126)
                throw new InvalidDataException("Animation values are floats.");
            var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
            var offset = bin + (view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0) +
                (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
            var stride = view.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : 4 * width;
            var count = accessor.GetProperty("count").GetInt32();
            var values = new float[count * width];
            for (var i = 0; i < count; i++)
            {
                for (var c = 0; c < width; c++)
                    values[i * width + c] = BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(offset + i * stride + 4 * c));
            }
            return values;
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
