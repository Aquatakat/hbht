using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aidyn.Rom.Graphics;

/// <summary>
/// Builds a binary glTF 2.0 file (<c>.glb</c>): JSON plus one embedded buffer. It has no
/// dependencies, and its output depends only on what's added, in order, as the importer
/// requires (ADR 0001).
/// </summary>
public sealed class GltfBuilder
{
    public const int Float = 5126;
    public const int UnsignedShort = 5123;
    public const int UnsignedInt = 5125;
    private const int ArrayBuffer = 34962;
    private const int ElementArrayBuffer = 34963;

    private readonly MemoryStream _buffer = new();
    private readonly JsonArray _bufferViews = [], _accessors = [], _images = [], _samplers = [], _textures = [];
    private readonly JsonArray _materials = [], _meshes = [], _nodes = [], _cameras = [], _lights = [];
    private readonly SortedSet<string> _extensionsUsed = new(StringComparer.Ordinal);

    public int AddPositions(IReadOnlyList<Vector3> values)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in values)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        return AddAccessor(Floats(values.SelectMany(v => new[] { v.X, v.Y, v.Z })), Float, values.Count, "VEC3", ArrayBuffer,
            new JsonArray(min.X, min.Y, min.Z), new JsonArray(max.X, max.Y, max.Z));
    }

    public int AddVectors(IReadOnlyList<Vector2> values) =>
        AddAccessor(Floats(values.SelectMany(v => new[] { v.X, v.Y })), Float, values.Count, "VEC2", ArrayBuffer);

    public int AddVectors(IReadOnlyList<Vector3> values) =>
        AddAccessor(Floats(values.SelectMany(v => new[] { v.X, v.Y, v.Z })), Float, values.Count, "VEC3", ArrayBuffer);

    public int AddVectors(IReadOnlyList<Vector4> values) =>
        AddAccessor(Floats(values.SelectMany(v => new[] { v.X, v.Y, v.Z, v.W })), Float, values.Count, "VEC4", ArrayBuffer);

    public int AddIndices(IReadOnlyList<int> indices, int vertexCount)
    {
        byte[] data;
        int type;
        if (vertexCount <= ushort.MaxValue)
        {
            data = new byte[indices.Count * 2];
            for (var i = 0; i < indices.Count; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), (ushort)indices[i]);
            type = UnsignedShort;
        }
        else
        {
            data = new byte[indices.Count * 4];
            for (var i = 0; i < indices.Count; i++)
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4), indices[i]);
            type = UnsignedInt;
        }
        return AddAccessor(data, type, indices.Count, "SCALAR", ElementArrayBuffer);
    }

    public int AddPng(byte[] png, string name)
    {
        _images.Add(new JsonObject { ["name"] = name, ["bufferView"] = AddBufferView(png, null), ["mimeType"] = "image/png" });
        return _images.Count - 1;
    }

    public int AddSampler(int magFilter, int minFilter, int wrapS, int wrapT) =>
        Add(_samplers, new JsonObject { ["magFilter"] = magFilter, ["minFilter"] = minFilter, ["wrapS"] = wrapS, ["wrapT"] = wrapT });

    public int AddTexture(int image, int sampler) =>
        Add(_textures, new JsonObject { ["sampler"] = sampler, ["source"] = image });

    public int AddMaterial(JsonObject material) => Add(_materials, material);
    public int AddMesh(JsonObject mesh) => Add(_meshes, mesh);
    public int AddNode(JsonObject node) => Add(_nodes, node);

    /// <summary>A node added earlier, to finish it.</summary>
    public JsonObject Node(int index) => _nodes[index]!.AsObject();
    public int AddCamera(JsonObject camera) => Add(_cameras, camera);

    /// <summary>A <c>KHR_lights_punctual</c> light.</summary>
    public int AddLight(JsonObject light)
    {
        UseExtension("KHR_lights_punctual");
        return Add(_lights, light);
    }

    public void UseExtension(string name) => _extensionsUsed.Add(name);

    /// <summary>Writes the file with one scene holding <paramref name="roots"/>.</summary>
    public byte[] ToGlb(IEnumerable<int> roots, JsonObject? sceneExtras = null)
    {
        var scene = new JsonObject { ["nodes"] = new JsonArray(roots.Select(r => (JsonNode)r).ToArray()) };
        if (sceneExtras is not null)
            scene["extras"] = sceneExtras;

        var root = new JsonObject { ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "hbht Aidyn.Rom" } };
        if (_extensionsUsed.Count > 0)
            root["extensionsUsed"] = new JsonArray(_extensionsUsed.Select(e => (JsonNode)e).ToArray());
        if (_lights.Count > 0)
            root["extensions"] = new JsonObject { ["KHR_lights_punctual"] = new JsonObject { ["lights"] = _lights } };
        root["scene"] = 0;
        root["scenes"] = new JsonArray(scene);
        foreach (var (name, array) in new[]
                 {
                     ("nodes", _nodes), ("meshes", _meshes), ("materials", _materials), ("textures", _textures),
                     ("images", _images), ("samplers", _samplers), ("cameras", _cameras), ("accessors", _accessors),
                     ("bufferViews", _bufferViews),
                 })
            if (array.Count > 0)
                root[name] = array;
        if (_buffer.Length > 0)
            root["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = _buffer.Length });

        var json = Encoding.UTF8.GetBytes(root.ToJsonString());
        var jsonLength = Align4(json.Length);
        var binLength = Align4((int)_buffer.Length);
        var total = 12 + 8 + jsonLength + (binLength > 0 ? 8 + binLength : 0);

        var glb = new byte[total];
        var s = glb.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s, 0x46546C67); // "glTF"
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(s[8..], total);
        BinaryPrimitives.WriteInt32LittleEndian(s[12..], jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(s[16..], 0x4E4F534A); // "JSON"
        json.CopyTo(s[20..]);
        s.Slice(20 + json.Length, jsonLength - json.Length).Fill((byte)' ');
        if (binLength > 0)
        {
            var at = 20 + jsonLength;
            BinaryPrimitives.WriteInt32LittleEndian(s[at..], binLength);
            BinaryPrimitives.WriteUInt32LittleEndian(s[(at + 4)..], 0x004E4942); // "BIN\0"
            _buffer.GetBuffer().AsSpan(0, (int)_buffer.Length).CopyTo(s[(at + 8)..]);
        }
        return glb;
    }

    private int AddAccessor(byte[] data, int componentType, int count, string type, int target,
        JsonArray? min = null, JsonArray? max = null)
    {
        var accessor = new JsonObject
        {
            ["bufferView"] = AddBufferView(data, target),
            ["componentType"] = componentType,
            ["count"] = count,
            ["type"] = type,
        };
        if (min is not null)
            accessor["min"] = min;
        if (max is not null)
            accessor["max"] = max;
        return Add(_accessors, accessor);
    }

    private int AddBufferView(byte[] data, int? target)
    {
        while (_buffer.Length % 4 != 0)
            _buffer.WriteByte(0);
        var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = _buffer.Length, ["byteLength"] = data.Length };
        if (target is { } t)
            view["target"] = t;
        _buffer.Write(data);
        return Add(_bufferViews, view);
    }

    private static byte[] Floats(IEnumerable<float> values)
    {
        var floats = values.ToArray();
        var data = new byte[floats.Length * 4];
        for (var i = 0; i < floats.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4), floats[i]);
        return data;
    }

    private static int Add(JsonArray array, JsonNode node)
    {
        array.Add(node);
        return array.Count - 1;
    }

    private static int Align4(int n) => (n + 3) & ~3;
}
