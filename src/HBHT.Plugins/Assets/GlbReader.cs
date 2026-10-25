using System.Buffers.Binary;
using System.Text.Json;

namespace HBHT.Plugins.Assets;

/// <summary>
/// A binary glTF (<c>.glb</c>) the importer wrote, open for reading: the header's chunks, the JSON, and the float and
/// index accessors that point into the binary chunk. Every reader of the importer's glTFs (the collision, scene and
/// actor readers here, and the runtime's and the editor's views) goes through it. The caller disposes it.
/// </summary>
public sealed class GlbReader : IDisposable
{
    // A glb is a 12-byte header, then chunks of (length, type, data): the JSON chunk's length is at 12, its data at 20,
    // and the binary chunk's 8-byte header follows it.
    private const uint Magic = 0x46546C67;
    private const int JsonLengthOffset = 12, JsonOffset = 20, ChunkHeader = 8;
    private const int Float = 5126, UnsignedByte = 5121, UnsignedShort = 5123, UnsignedInt = 5125;

    private readonly byte[] _glb;
    private readonly JsonDocument _json;
    private readonly int _bin;

    private GlbReader(byte[] glb, JsonDocument json, int bin)
    {
        _glb = glb;
        _json = json;
        _bin = bin;
    }

    /// <summary>Opens a binary glTF. Throws <see cref="InvalidDataException"/> if it isn't one.</summary>
    public static GlbReader Parse(byte[] glb)
    {
        if (glb.Length < JsonOffset || BinaryPrimitives.ReadUInt32LittleEndian(glb) != Magic)
            throw new InvalidDataException("Not a binary glTF file.");
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(JsonLengthOffset));
        if (jsonLength < 0 || JsonOffset + jsonLength > glb.Length)
            throw new InvalidDataException("The glTF's JSON chunk runs past its end.");
        return new GlbReader(glb, JsonDocument.Parse(glb.AsMemory(JsonOffset, jsonLength)), JsonOffset + jsonLength + ChunkHeader);
    }

    /// <summary>The JSON chunk's root.</summary>
    public JsonElement Root => _json.RootElement;

    /// <summary>Whether the file has a binary chunk after its JSON, which a scene with no geometry doesn't.</summary>
    public bool HasBinaryChunk => _glb.Length > _bin - ChunkHeader;

    /// <summary>The glTF scene the file shows: its <c>scene</c>, else the first.</summary>
    public JsonElement? Scene =>
        Root.TryGetProperty("scenes", out var scenes) && scenes.GetArrayLength() > 0
            ? scenes[Root.TryGetProperty("scene", out var index) ? index.GetInt32() : 0] : null;

    /// <summary>A float accessor's values, a vertex or sample at a time (the accessor's type gives the width: 1, 2, 3 or 4), honouring its view's stride.</summary>
    public float[] Floats(int accessorIndex)
    {
        var accessor = Root.GetProperty("accessors")[accessorIndex];
        if (accessor.GetProperty("componentType").GetInt32() != Float)
            throw new InvalidDataException("The importer's accessors are floats.");
        var width = accessor.GetProperty("type").GetString() switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            var t => throw new InvalidDataException($"Unexpected accessor type {t}."),
        };
        var (offset, stride) = Locate(accessor, 4 * width);
        var count = accessor.GetProperty("count").GetInt32();
        var values = new float[count * width];
        for (var i = 0; i < count; i++)
        {
            for (var c = 0; c < width; c++)
                values[i * width + c] = BinaryPrimitives.ReadSingleLittleEndian(_glb.AsSpan(offset + i * stride + 4 * c));
        }
        return values;
    }

    /// <summary>An index accessor's values, whichever unsigned width it has.</summary>
    public int[] Indices(int accessorIndex)
    {
        var accessor = Root.GetProperty("accessors")[accessorIndex];
        var type = accessor.GetProperty("componentType").GetInt32();
        var size = type switch { UnsignedByte => 1, UnsignedShort => 2, UnsignedInt => 4, _ => throw new InvalidDataException($"Unexpected index type {type}.") };
        var (offset, stride) = Locate(accessor, size);
        var values = new int[accessor.GetProperty("count").GetInt32()];
        for (var i = 0; i < values.Length; i++)
        {
            var at = _glb.AsSpan(offset + i * stride);
            values[i] = size switch { 1 => at[0], 2 => BinaryPrimitives.ReadUInt16LittleEndian(at), _ => (int)BinaryPrimitives.ReadUInt32LittleEndian(at) };
        }
        return values;
    }

    public void Dispose() => _json.Dispose();

    /// <summary>Where an accessor's first element is in the file, and the step between elements (<paramref name="packed"/> when its view has no stride).</summary>
    private (int Offset, int Stride) Locate(JsonElement accessor, int packed)
    {
        var view = Root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        var offset = _bin + (view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0) +
            (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
        return (offset, view.TryGetProperty("byteStride", out var stride) ? stride.GetInt32() : packed);
    }
}
