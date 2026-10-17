using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace Aidyn.Plugins.Assets;

/// <summary>
/// How a scene's draw is blended, from its glTF material's <c>alphaMode</c>, which the exporter takes from the
/// N64's render mode (<c>Scene.SetRenderMode</c>; see docs/systems/rendering.md).
/// </summary>
public enum SurfaceBlend
{
    /// <summary>Drawn solid (<c>OPAQUE</c>).</summary>
    Opaque,
    /// <summary>Cut out by its texels' alpha (<c>MASK</c>): foliage, bushes, fences.</summary>
    CutOut,
    /// <summary>Blended over what's behind (<c>BLEND</c>): water, glass, light.</summary>
    Translucent,
}

/// <summary>A triangle a scene draws, in the scene's coordinates.</summary>
public readonly record struct SceneTriangle(Vector3 A, Vector3 B, Vector3 C, SurfaceBlend Blend);

/// <summary>
/// Reads the triangles a scene's glTF draws, each with its nodes' transforms applied, as the scene stands
/// before anything animates it. Billboards' cards, which turn to the camera as they're drawn, are left out.
/// </summary>
public static class SceneMesh
{
    private const int Triangles = 4;

    /// <summary>A scene's triangles, from the plugin that provides its asset, or none if no plugin has one.</summary>
    public static IReadOnlyList<SceneTriangle> For(PluginSet plugins, RecordId scene) =>
        plugins.FindAsset(scene, "glb") is { } path ? Read(File.ReadAllBytes(path)) : [];

    public static IReadOnlyList<SceneTriangle> Read(byte[] glb)
    {
        if (glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(glb) != 0x46546C67)
            throw new InvalidDataException("Not a binary glTF file.");
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var file = new Gltf(json.RootElement, glb, 20 + jsonLength + 8);
        var triangles = new List<SceneTriangle>();
        var root = json.RootElement;
        if (!root.TryGetProperty("nodes", out var nodes) || !root.TryGetProperty("scenes", out var scenes))
            return triangles;
        var scene = scenes[root.TryGetProperty("scene", out var s) ? s.GetInt32() : 0];
        if (!scene.TryGetProperty("nodes", out var roots))
            return triangles;
        foreach (var n in roots.EnumerateArray())
            file.Walk(nodes, n.GetInt32(), Matrix4x4.Identity, triangles);
        return triangles;
    }

    private sealed class Gltf(JsonElement root, byte[] glb, int bin)
    {
        public void Walk(JsonElement nodes, int index, Matrix4x4 parent, List<SceneTriangle> into)
        {
            var node = nodes[index];
            // A billboard's facing node turns its card to the camera, so where the card is depends on the view.
            if (node.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object
                && extras.TryGetProperty("facing", out var facing) && facing.ValueKind == JsonValueKind.True)
                return;
            var world = Local(node) * parent;
            if (node.TryGetProperty("mesh", out var mesh))
                AddMesh(root.GetProperty("meshes")[mesh.GetInt32()], world, into);
            if (node.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                    Walk(nodes, child.GetInt32(), world, into);
            }
        }

        private void AddMesh(JsonElement mesh, Matrix4x4 world, List<SceneTriangle> into)
        {
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                if (primitive.TryGetProperty("mode", out var mode) && mode.GetInt32() != Triangles)
                    continue;
                var positions = Floats(primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32(), 3);
                var indices = primitive.TryGetProperty("indices", out var ix) ? Indices(ix.GetInt32()) : null;
                var count = indices?.Length ?? positions.Length / 3;
                var blend = Blend(primitive);
                for (var i = 0; i + 2 < count; i += 3)
                {
                    into.Add(new SceneTriangle(
                        Position(positions, indices?[i] ?? i, world),
                        Position(positions, indices?[i + 1] ?? i + 1, world),
                        Position(positions, indices?[i + 2] ?? i + 2, world),
                        blend));
                }
            }
        }

        private SurfaceBlend Blend(JsonElement primitive)
        {
            if (!primitive.TryGetProperty("material", out var m))
                return SurfaceBlend.Opaque;
            var material = root.GetProperty("materials")[m.GetInt32()];
            return material.TryGetProperty("alphaMode", out var mode) ? mode.GetString() switch
            {
                "MASK" => SurfaceBlend.CutOut,
                "BLEND" => SurfaceBlend.Translucent,
                _ => SurfaceBlend.Opaque,
            } : SurfaceBlend.Opaque;
        }

        private static Vector3 Position(float[] p, int v, Matrix4x4 world) =>
            Vector3.Transform(new Vector3(p[3 * v], p[3 * v + 1], p[3 * v + 2]), world);

        private static Matrix4x4 Local(JsonElement node)
        {
            if (node.TryGetProperty("matrix", out var matrix))
            {
                // glTF's matrices are column-major for column vectors, which is System.Numerics' row-vector layout read row by row.
                var m = matrix.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
            }
            var scale = node.TryGetProperty("scale", out var s) ? new Vector3(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()) : Vector3.One;
            var rotation = node.TryGetProperty("rotation", out var r)
                ? new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle())
                : Quaternion.Identity;
            var translation = node.TryGetProperty("translation", out var t) ? new Vector3(t[0].GetSingle(), t[1].GetSingle(), t[2].GetSingle()) : Vector3.Zero;
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
        }

        private float[] Floats(int accessorIndex, int width)
        {
            var (accessor, offset, stride) = Accessor(accessorIndex, 4 * width);
            if (accessor.GetProperty("componentType").GetInt32() != 5126)
                throw new InvalidDataException("Positions are floats.");
            var count = accessor.GetProperty("count").GetInt32();
            var values = new float[count * width];
            for (var i = 0; i < count; i++)
            {
                for (var c = 0; c < width; c++)
                    values[i * width + c] = BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(offset + i * stride + 4 * c));
            }
            return values;
        }

        private int[] Indices(int accessorIndex)
        {
            var type = root.GetProperty("accessors")[accessorIndex].GetProperty("componentType").GetInt32();
            var size = type switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw new InvalidDataException($"Unexpected index type {type}.") };
            var (accessor, offset, stride) = Accessor(accessorIndex, size);
            var values = new int[accessor.GetProperty("count").GetInt32()];
            for (var i = 0; i < values.Length; i++)
            {
                var at = glb.AsSpan(offset + i * stride);
                values[i] = size switch { 1 => at[0], 2 => BinaryPrimitives.ReadUInt16LittleEndian(at), _ => (int)BinaryPrimitives.ReadUInt32LittleEndian(at) };
            }
            return values;
        }

        private (JsonElement Accessor, int Offset, int Stride) Accessor(int index, int packed)
        {
            var accessor = root.GetProperty("accessors")[index];
            var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
            var offset = bin + (view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0) +
                (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
            var stride = view.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : packed;
            return (accessor, offset, stride);
        }
    }
}
