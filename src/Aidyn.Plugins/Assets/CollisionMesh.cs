using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace Aidyn.Plugins.Assets;

/// <summary>What a collision triangle does besides block (<c>borg9_phys.flags</c>).</summary>
[Flags]
public enum CollisionFlags
{
    None = 0,
    /// <summary>Things pass through it. A trigger can set this on a toggle to open the way.</summary>
    Passable = 0x1,
    /// <summary>Spheres collide with the edge from A to B as well as the face. Every edge no other triangle shares has its bit.</summary>
    EdgeAB = 0x4,
    /// <summary>The edge from C to A.</summary>
    EdgeCA = 0x8,
    /// <summary>The edge from B to C.</summary>
    EdgeBC = 0x10,
    /// <summary>Spheres collide with the corner at A.</summary>
    VertexA = 0x20,
    VertexB = 0x40,
    VertexC = 0x80,
    /// <summary>Line-of-sight tests ignore it, so the camera passes through. Invisible walls.</summary>
    SeeThrough = 0x200,
}

/// <summary>What a collision triangle is for.</summary>
public enum CollisionRole
{
    Normal,
    /// <summary>A trigger can make it passable.</summary>
    Toggle,
    /// <summary>Walking on it runs a map object: a camera, teleporter or dialogue.</summary>
    Trigger,
    /// <summary>Only there with the Expansion Pak, which the runtime always has.</summary>
    ExpansionPak,
}

/// <summary>A collision triangle, counter-clockwise seen from the front.</summary>
/// <param name="Surface">
/// The physics and footstep material, numbered as the original's surfaces: 0 Default, 1 blocks
/// the player and the camera, 2 blocks only the player, then Snow, Grass, Sand and so on.
/// </param>
/// <param name="Target">
/// For a <see cref="CollisionRole.Trigger"/>, the index in its map of the object it runs; for a
/// <see cref="CollisionRole.Toggle"/>, the id its triggers name. Otherwise 0.
/// </param>
public readonly record struct CollisionTriangle(Vector3 A, Vector3 B, Vector3 C, int Surface, CollisionRole Role, CollisionFlags Flags, int Target = 0);

/// <summary>
/// Reads a map's collision asset: a glTF mesh whose triangles don't share vertices, each vertex
/// carrying its triangle's <c>_SURFACE</c>, <c>_ROLE</c>, <c>_FLAGS</c> and <c>_TARGET</c> as floats.
/// </summary>
public static class CollisionMesh
{
    /// <summary>A map's collision, from the plugin that provides its asset, or none if no plugin has one.</summary>
    public static IReadOnlyList<CollisionTriangle> For(PluginSet plugins, RecordId map) =>
        plugins.FindAsset(map, "glb") is { } path ? Read(File.ReadAllBytes(path)) : [];

    public static IReadOnlyList<CollisionTriangle> Read(byte[] glb)
    {
        if (glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(glb) != 0x46546C67)
            throw new InvalidDataException("Not a binary glTF file.");
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var bin = 20 + jsonLength + 8;
        var root = json.RootElement;

        var triangles = new List<CollisionTriangle>();
        if (!root.TryGetProperty("meshes", out var meshes))
            return triangles;
        foreach (var mesh in meshes.EnumerateArray())
        {
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                var attributes = primitive.GetProperty("attributes");
                var positions = Floats(root, glb, bin, attributes.GetProperty("POSITION").GetInt32());
                var surfaces = Floats(root, glb, bin, attributes.GetProperty("_SURFACE").GetInt32());
                var roles = Floats(root, glb, bin, attributes.GetProperty("_ROLE").GetInt32());
                var flags = Floats(root, glb, bin, attributes.GetProperty("_FLAGS").GetInt32());
                var targets = attributes.TryGetProperty("_TARGET", out var target) ? Floats(root, glb, bin, target.GetInt32()) : null;
                for (var v = 0; v + 2 < surfaces.Length; v += 3)
                {
                    triangles.Add(new CollisionTriangle(
                        Position(positions, v), Position(positions, v + 1), Position(positions, v + 2),
                        (int)surfaces[v], (CollisionRole)(int)roles[v], (CollisionFlags)(int)flags[v], targets is null ? 0 : (int)targets[v]));
                }
            }
        }
        return triangles;
    }

    private static Vector3 Position(float[] p, int v) => new(p[3 * v], p[3 * v + 1], p[3 * v + 2]);

    /// <summary>A float accessor's values, tightly packed as the exporter writes them.</summary>
    private static float[] Floats(JsonElement root, byte[] glb, int bin, int accessorIndex)
    {
        var accessor = root.GetProperty("accessors")[accessorIndex];
        if (accessor.GetProperty("componentType").GetInt32() != 5126)
            throw new InvalidDataException("Collision attributes are floats.");
        var width = accessor.GetProperty("type").GetString() switch { "SCALAR" => 1, "VEC3" => 3, var t => throw new InvalidDataException($"Unexpected accessor type {t}.") };
        var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        var offset = bin + (view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0) +
            (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
        var values = new float[accessor.GetProperty("count").GetInt32() * width];
        for (var i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(offset + 4 * i));
        return values;
    }
}
