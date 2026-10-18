using System.Numerics;

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
/// carrying its triangle's surface, role, flags and target as floats (<see cref="SurfaceAttribute"/> and the others).
/// </summary>
public static class CollisionMesh
{
    /// <summary>The vertex attributes the exporter (<c>CollisionGltf</c>) writes, per vertex of a triangle and the same on its three: <see cref="CollisionTriangle.Surface"/>.</summary>
    public const string SurfaceAttribute = "_SURFACE";
    /// <summary><see cref="CollisionTriangle.Role"/>, as a <see cref="CollisionRole"/>'s number.</summary>
    public const string RoleAttribute = "_ROLE";
    /// <summary><see cref="CollisionTriangle.Flags"/>.</summary>
    public const string FlagsAttribute = "_FLAGS";
    /// <summary><see cref="CollisionTriangle.Target"/>; a file without it has none.</summary>
    public const string TargetAttribute = "_TARGET";

    /// <summary>A map's collision, from the plugin that provides its asset, or none if no plugin has one.</summary>
    public static IReadOnlyList<CollisionTriangle> For(PluginSet plugins, RecordId map) =>
        plugins.FindAsset(map, "glb") is { } path ? Read(File.ReadAllBytes(path)) : [];

    public static IReadOnlyList<CollisionTriangle> Read(byte[] glb)
    {
        using var file = GlbReader.Parse(glb);
        var root = file.Root;

        var triangles = new List<CollisionTriangle>();
        if (!root.TryGetProperty("meshes", out var meshes))
            return triangles;
        foreach (var mesh in meshes.EnumerateArray())
        {
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                var attributes = primitive.GetProperty("attributes");
                var positions = file.Floats(attributes.GetProperty("POSITION").GetInt32());
                var surfaces = file.Floats(attributes.GetProperty(SurfaceAttribute).GetInt32());
                var roles = file.Floats(attributes.GetProperty(RoleAttribute).GetInt32());
                var flags = file.Floats(attributes.GetProperty(FlagsAttribute).GetInt32());
                var targets = attributes.TryGetProperty(TargetAttribute, out var target) ? file.Floats(target.GetInt32()) : null;
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
}
