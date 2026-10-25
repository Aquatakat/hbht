using HBHT.Core.Physics;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Editor;

/// <summary>
/// A map set's collision, a mesh per chunk, coloured by surface or by role and drawn see-through
/// over the scenery, and which triangle is under the cursor.
/// </summary>
public sealed class CollisionLayer
{
    public enum Colouring { Off, Surface, Role }

    private static readonly Color[] SurfaceColours =
    [
        .. new[]
        {
            "#777777", "#b0b0b8", "#ff4d4d", "#ffffff", "#5cb85c", "#e8d18a", "#7a5c3a", "#ff8c1a", "#6b6b8f",
            "#b07a45", "#8f5a2e", "#9aa4ad", "#76828c", "#56606a", "#3d8bff",
        }.Select(html => Color.FromHtml(html)),
    ];

    private static readonly string[] RoleNames = ["Normal", "Toggle", "Trigger", "Expansion Pak only"];
    private static readonly Color[] RoleColours = [.. new[] { "#6f7480", "#ff9f43", "#ff4dd2", "#ffe14d" }.Select(html => Color.FromHtml(html))];

    private static readonly Shader Shader = new()
    {
        Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, blend_mix, skip_vertex_transform;
            varying vec3 world_normal;
            void vertex() {
                world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
                // Pulled a little toward the camera, so it lies over the scenery it traces.
                VERTEX = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz * 0.998;
            }
            void fragment() {
                float light = 0.6 + 0.4 * abs(dot(world_normal, normalize(vec3(0.4, 1.0, 0.3))));
                ALBEDO = COLOR.rgb * light;
                ALPHA = 0.75;
            }
            """,
    };

    private readonly ShaderMaterial _material = new() { Shader = Shader };
    private readonly Dictionary<(int Row, int Column), Chunk> _chunks = [];

    /// <summary>A chunk's triangles, in the map set's coordinates.</summary>
    private sealed class Chunk(ChunkEntry entry, CollisionTriangle[] triangles, Vector3[] corners, Aabb bounds, MeshInstance3D? mesh)
    {
        public ChunkEntry Entry { get; } = entry;
        public CollisionTriangle[] Triangles { get; } = triangles;
        public Vector3[] Corners { get; } = corners;
        public Aabb Bounds { get; } = bounds;
        public MeshInstance3D? Mesh { get; } = mesh;
    }

    /// <summary>A triangle a ray meets.</summary>
    public readonly record struct Hit(ChunkEntry Chunk, CollisionTriangle Triangle, Vector3 Point, float Distance);

    public Node3D Node { get; } = new() { Name = "Collision" };

    public int Triangles => _chunks.Values.Sum(c => c.Triangles.Length);

    public void Clear()
    {
        foreach (var chunk in _chunks.Values)
            chunk.Mesh?.QueueFree();
        _chunks.Clear();
    }

    public bool Contains(ChunkEntry chunk) => _chunks.ContainsKey((chunk.Row, chunk.Column));

    /// <summary>The chunk's collision bounds, in the map set's coordinates, if it has any.</summary>
    public Aabb? Bounds(ChunkEntry chunk) =>
        _chunks.TryGetValue((chunk.Row, chunk.Column), out var c) && c.Triangles.Length > 0 ? c.Bounds : null;

    /// <summary>
    /// The middle of the trigger triangles in a chunk that run its object <paramref name="target"/>, in
    /// the map set's coordinates, or null if none do or the chunk isn't loaded.
    /// </summary>
    public Vector3? TriggerCentre(ChunkEntry entry, int target)
    {
        if (!_chunks.TryGetValue((entry.Row, entry.Column), out var chunk))
            return null;
        var sum = Vector3.Zero;
        var count = 0;
        for (var i = 0; i < chunk.Triangles.Length; i++)
        {
            if (chunk.Triangles[i].Role != CollisionRole.Trigger || chunk.Triangles[i].Target != target)
                continue;
            sum += chunk.Corners[3 * i] + chunk.Corners[3 * i + 1] + chunk.Corners[3 * i + 2];
            count += 3;
        }
        return count > 0 ? sum / count : null;
    }

    /// <summary>Adds a chunk's collision, read from its map's asset, at its origin.</summary>
    public void Add(ChunkEntry entry, IReadOnlyList<CollisionTriangle> triangles, Vector3 origin, Colouring colouring, bool pak)
    {
        var corners = new Vector3[triangles.Count * 3];
        for (var i = 0; i < triangles.Count; i++)
        {
            corners[3 * i] = triangles[i].A.ToGodot() + origin;
            corners[3 * i + 1] = triangles[i].B.ToGodot() + origin;
            corners[3 * i + 2] = triangles[i].C.ToGodot() + origin;
        }
        var bounds = new Aabb(corners.Length > 0 ? corners[0] : origin, Vector3.Zero);
        foreach (var corner in corners)
            bounds = bounds.Expand(corner);
        MeshInstance3D? mesh = null;
        if (corners.Length > 0)
        {
            mesh = new MeshInstance3D { Name = entry.Name, MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            Node.AddChild(mesh);
        }
        var chunk = _chunks[(entry.Row, entry.Column)] = new Chunk(entry, [.. triangles], corners, bounds, mesh);
        Colour(chunk, colouring, pak);
    }

    /// <summary>Colours every chunk's triangles, dimming the Expansion Pak's when it's out, as the game makes them passable.</summary>
    public void Recolour(Colouring colouring, bool pak)
    {
        Node.Visible = colouring != Colouring.Off;
        foreach (var chunk in _chunks.Values)
            Colour(chunk, colouring, pak);
    }

    /// <summary>The surfaces or roles the loaded triangles have, with their names and colours, for the legend.</summary>
    public IEnumerable<(string Name, Color Colour)> Legend(Colouring colouring)
    {
        if (colouring == Colouring.Off)
            return [];
        var used = _chunks.Values.SelectMany(c => c.Triangles).Select(t => colouring == Colouring.Role ? (int)t.Role : t.Surface).Distinct().Order();
        return used.Select(v => colouring == Colouring.Role ? (RoleName(v), RoleColours[v]) : (SurfaceNames.Of(v), SurfaceColour(v)));
    }

    public static string RoleName(int role) => role >= 0 && role < RoleNames.Length ? RoleNames[role] : $"Role {role}";

    /// <summary>The nearest triangle along a ray, facing either way.</summary>
    public Hit? Cast(Vector3 from, Vector3 direction)
    {
        Hit? best = null;
        foreach (var chunk in _chunks.Values)
        {
            if (chunk.Triangles.Length == 0 || !Meets(chunk.Bounds.Grow(0.01f), from, direction))
                continue;
            for (var i = 0; i < chunk.Triangles.Length; i++)
            {
                var distance = RayTriangle(from, direction, chunk.Corners[3 * i], chunk.Corners[3 * i + 1], chunk.Corners[3 * i + 2]);
                if (distance is { } d && (best is null || d < best.Value.Distance))
                    best = new Hit(chunk.Entry, chunk.Triangles[i], from + direction * d, d);
            }
        }
        return best;
    }

    private static Color SurfaceColour(int surface) =>
        surface >= 0 && surface < SurfaceColours.Length ? SurfaceColours[surface] : Color.FromHsv(surface * 0.13f % 1, 0.7f, 0.85f);

    private void Colour(Chunk chunk, Colouring colouring, bool pak)
    {
        if (chunk.Mesh is null)
            return;
        var colours = new Color[chunk.Corners.Length];
        var normals = new Vector3[chunk.Corners.Length];
        for (var i = 0; i < chunk.Triangles.Length; i++)
        {
            var t = chunk.Triangles[i];
            var colour = colouring == Colouring.Role ? RoleColours[Math.Clamp((int)t.Role, 0, RoleColours.Length - 1)] : SurfaceColour(t.Surface);
            if (t.Role == CollisionRole.ExpansionPak && !pak)
                colour = colour.Darkened(0.8f);
            var (a, b, c) = (chunk.Corners[3 * i], chunk.Corners[3 * i + 1], chunk.Corners[3 * i + 2]);
            var normal = (b - a).Cross(c - a).Normalized();
            for (var k = 0; k < 3; k++)
            {
                colours[3 * i + k] = colour;
                normals[3 * i + k] = normal;
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = chunk.Corners;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        chunk.Mesh.Mesh = mesh;
    }

    /// <summary>Whether a ray meets a box, by the slabs.</summary>
    private static bool Meets(Aabb box, Vector3 from, Vector3 direction)
    {
        var (near, far) = (0f, float.MaxValue);
        for (var axis = 0; axis < 3; axis++)
        {
            var (o, d, min, max) = (from[axis], direction[axis], box.Position[axis], box.End[axis]);
            if (Math.Abs(d) < 1e-12f)
            {
                if (o < min || o > max)
                    return false;
                continue;
            }
            var (t1, t2) = ((min - o) / d, (max - o) / d);
            near = Math.Max(near, Math.Min(t1, t2));
            far = Math.Min(far, Math.Max(t1, t2));
            if (near > far)
                return false;
        }
        return true;
    }

    /// <summary>Möller–Trumbore: how far along the ray it meets the triangle, from either side.</summary>
    private static float? RayTriangle(Vector3 from, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var p = direction.Cross(ac);
        var det = ab.Dot(p);
        if (Math.Abs(det) < 1e-9f)
            return null;
        var inverse = 1 / det;
        var s = from - a;
        var u = s.Dot(p) * inverse;
        if (u is < 0 or > 1)
            return null;
        var q = s.Cross(ab);
        var v = direction.Dot(q) * inverse;
        if (v < 0 || u + v > 1)
            return null;
        var t = ac.Dot(q) * inverse;
        return t > 0 ? t : null;
    }
}
