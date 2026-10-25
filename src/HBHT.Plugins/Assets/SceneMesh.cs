using System.Numerics;
using System.Text.Json;

namespace HBHT.Plugins.Assets;

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
/// <param name="Scrolls">Its texture scrolls (Borg1 flag 0x8, the scene's <c>scrollingTextures</c>): water, lava, fire.</param>
public readonly record struct SceneTriangle(Vector3 A, Vector3 B, Vector3 C, SurfaceBlend Blend, bool Scrolls = false);

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
        using var file = GlbReader.Parse(glb);
        var reader = new Gltf(file, SceneExtras.Read(file));
        var triangles = new List<SceneTriangle>();
        if (!file.Root.TryGetProperty("nodes", out var nodes) || file.Scene is not { } scene || !scene.TryGetProperty("nodes", out var roots))
            return triangles;
        foreach (var n in roots.EnumerateArray())
            reader.Walk(nodes, n.GetInt32(), Matrix4x4.Identity, triangles);
        return triangles;
    }

    private sealed class Gltf(GlbReader file, SceneExtras extras)
    {
        // The Borg1s the scene lists as scrolling.
        private readonly HashSet<int> _scrolling = [.. extras.Scrolling.Select(s => s.Borg1)];

        public void Walk(JsonElement nodes, int index, Matrix4x4 parent, List<SceneTriangle> into)
        {
            var node = nodes[index];
            // A billboard's facing node turns its card to the camera, so where the card is depends on the view.
            if (node.TryGetProperty("extras", out var nodeExtras) && nodeExtras.ValueKind == JsonValueKind.Object
                && nodeExtras.TryGetProperty(ExtrasKeys.Facing, out var facing) && facing.ValueKind == JsonValueKind.True)
                return;
            var world = Local(node) * parent;
            if (node.TryGetProperty("mesh", out var mesh))
                AddMesh(file.Root.GetProperty("meshes")[mesh.GetInt32()], world, into);
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
                var positions = file.Floats(primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32());
                var indices = primitive.TryGetProperty("indices", out var ix) ? file.Indices(ix.GetInt32()) : null;
                var count = indices?.Length ?? positions.Length / 3;
                var (blend, scrolls) = Look(primitive);
                for (var i = 0; i + 2 < count; i += 3)
                {
                    into.Add(new SceneTriangle(
                        Position(positions, indices?[i] ?? i, world),
                        Position(positions, indices?[i + 1] ?? i + 1, world),
                        Position(positions, indices?[i + 2] ?? i + 2, world),
                        blend, scrolls));
                }
            }
        }

        private (SurfaceBlend Blend, bool Scrolls) Look(JsonElement primitive)
        {
            if (!primitive.TryGetProperty("material", out var m))
                return (SurfaceBlend.Opaque, false);
            var material = file.Root.GetProperty("materials")[m.GetInt32()];
            var blend = material.TryGetProperty("alphaMode", out var mode) ? mode.GetString() switch
            {
                "MASK" => SurfaceBlend.CutOut,
                "BLEND" => SurfaceBlend.Translucent,
                _ => SurfaceBlend.Opaque,
            } : SurfaceBlend.Opaque;
            var scrolls = material.TryGetProperty("extras", out var materialExtras) && materialExtras.ValueKind == JsonValueKind.Object
                && materialExtras.TryGetProperty(ExtrasKeys.Borg1, out var borg1) && borg1.ValueKind == JsonValueKind.Number
                && _scrolling.Contains(borg1.GetInt32());
            return (blend, scrolls);
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
    }
}
