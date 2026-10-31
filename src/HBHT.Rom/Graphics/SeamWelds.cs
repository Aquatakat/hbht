using System.Numerics;
using HBHT.Rom.Maps;

namespace HBHT.Rom.Graphics;

/// <summary>A vertex a scene draws: its draw's place in <see cref="Borg5Scene.Draws"/>, and its place in that draw's mesh.</summary>
public readonly record struct DrawVertex(int Draw, int Vertex);

/// <summary>
/// Closes the cracks where two meshes meant to meet miss by a hair (docs/systems/rendering.md#seams-between-meshes), the
/// player's choice at import (<c>ImportOptions.WeldSeams</c>). It finds the edges that border one triangle only and lie
/// along another such edge, and moves both ends of each pair to the same point. Between chunks it welds only neighbours'
/// scenes at their shared border, scenery to scenery and water to water, so a chunk's stand-in for land past its own cell is
/// left alone; within a scene, only what doesn't move apart. A vertex lying along another mesh's edge is moved onto it.
/// Collision isn't touched, so nothing of the rules changes.
/// </summary>
public static class SeamWelds
{
    /// <summary>How far apart two chunks' edges can be and be welded: the gaps at the borders fall off by here.</summary>
    internal const float ChunkGap = 0.25f;

    /// <summary>How far apart a scene's own edges can be and be welded: 2½ vertex units at the usual scale of 1/16.</summary>
    internal const float ModelGap = 0.01f;

    /// <summary>How far past its own cell a chunk's seam can be: the ordinary overlap at the borders.</summary>
    internal const float Border = 16;

    /// <summary>The gap is at most this part of either edge's length, so a small triangle isn't pulled out of shape.</summary>
    internal const float EdgeShare = 0.25f;

    /// <summary>
    /// The two triangles must turn away from each other by more than 60° about their edges, or they lie over each other, as
    /// a decal over its ground or a stand-in over a neighbour's, and aren't a seam.
    /// </summary>
    internal const float Fold = 0.5f;

    /// <summary>Welded points are snapped to 1/256 of a unit, a vertex unit at the usual scale, which floats hold exactly.</summary>
    internal const float Grid = 256;

    /// <summary>Closer than this, two vertices are already one.</summary>
    private const float Same = 1e-5f;

    /// <summary>
    /// The welds of every map set's chunk scenes, between neighbours and within each, by scene. A scene more than one chunk
    /// draws (in the Battlefields) is left as it is.
    /// </summary>
    /// <param name="read">Reads a Borg by archive index.</param>
    /// <param name="animated">Whether a scene has animations, which can move its nodes apart.</param>
    public static Dictionary<int, Dictionary<DrawVertex, Vector3>> ForChunks(IReadOnlyList<MapChunk> chunks, Func<int, byte[]> read, Func<int, bool> animated)
    {
        var uses = chunks.SelectMany(c => new[] { c.Scene, c.AltScene }).Where(s => s != 0).CountBy(s => s).ToDictionary();
        var models = new Dictionary<int, Borg2Geometry>();
        var welds = new Dictionary<int, Dictionary<DrawVertex, Vector3>>();
        foreach (var set in chunks.GroupBy(c => c.Set))
        {
            var cellSize = MapSets.CellSize(set.Key);
            var geometry = new Geometry();
            foreach (var chunk in set)
            {
                foreach (var (scene, alt) in new[] { (chunk.Scene, false), (chunk.AltScene, true) })
                {
                    if (scene == 0)
                        continue;
                    var piece = new Piece(scene, MapSets.ChunkOrigin(chunk), alt, animated(scene), (chunk.Row, chunk.Column), cellSize, uses[scene] > 1);
                    geometry.Add(piece, Borg5Scene.Parse(read(scene)), i => Model(models, read, i));
                }
            }
            foreach (var (scene, moved) in Weld(geometry))
                welds[scene] = moved;
        }
        return welds;
    }

    /// <summary>The welds of a scene on its own, within each of its nodes, which may move apart.</summary>
    public static Dictionary<DrawVertex, Vector3> ForScene(int index, Borg5Scene scene, Func<int, byte[]> read)
    {
        var models = new Dictionary<int, Borg2Geometry>();
        var geometry = new Geometry();
        geometry.Add(new Piece(index, Vector3.Zero, false, true, null, 0, false), scene, i => Model(models, read, i));
        return Weld(geometry).GetValueOrDefault(index) ?? [];
    }

    private static Borg2Geometry Model(Dictionary<int, Borg2Geometry> models, Func<int, byte[]> read, int index)
    {
        if (!models.TryGetValue(index, out var model))
            models[index] = model = Borg2Geometry.Parse(read(index));
        return model;
    }

    /// <summary>A scene placed in the world: a chunk's scenery or water at its origin, or a scene alone.</summary>
    /// <param name="NodesMove">Its nodes can move apart, so only what's on one node is welded.</param>
    /// <param name="Cell">Its chunk's row and column, or null for a scene alone.</param>
    /// <param name="Fixed">Nothing of it moves: it's drawn in more than one place.</param>
    internal sealed record Piece(int Scene, Vector3 Offset, bool Alt, bool NodesMove, (int Row, int Column)? Cell, float CellSize, bool Fixed)
    {
        /// <summary>How far a point is past this chunk's cell, across the ground.</summary>
        public float Past(Vector3 p)
        {
            var dx = MathF.Max(0, MathF.Max(Offset.X - p.X, p.X - (Offset.X + CellSize)));
            var dz = MathF.Max(0, MathF.Max(Offset.Z - p.Z, p.Z - (Offset.Z + CellSize)));
            return MathF.Max(dx, dz);
        }
    }

    internal readonly record struct Vertex(int Piece, DrawVertex At, Vector3 Position);

    internal readonly record struct Triangle(int A, int B, int C, int Piece, int Node);

    /// <summary>The drawn triangles of the pieces welded together, in world coordinates.</summary>
    internal sealed class Geometry
    {
        public List<Piece> Pieces { get; } = [];
        public List<Vertex> Vertices { get; } = [];
        public List<Triangle> Triangles { get; } = [];

        /// <summary>Adds what the scene's glTF draws (<see cref="SceneGltf"/>), as it stands before anything animates it. Billboards turn, so they're left out.</summary>
        public void Add(Piece piece, Borg5Scene scene, Func<int, Borg2Geometry> model)
        {
            var p = Pieces.Count;
            Pieces.Add(piece);
            for (var d = 0; d < scene.Draws.Count; d++)
            {
                var draw = scene.Draws[d];
                var geometry = model(scene.Models[draw.Model]);
                var transform = scene.Transforms[draw.Transform];
                if (!SceneGltf.IsDrawn(geometry, draw.DisplayList) || transform.Op != Borg5TransformOp.PivotRotate
                    || transform.Flags.HasFlag(Borg5TransformFlags.Billboard))
                    continue;
                var mesh = geometry.Meshes[draw.DisplayList];
                var world = scene.WorldMatrix(draw.Transform);
                var first = Vertices.Count;
                for (var v = 0; v < mesh.Vertices.Length; v++)
                    Vertices.Add(new Vertex(p, new DrawVertex(d, v), Vector3.Transform(SceneGltf.Position(geometry, mesh.Vertices[v]), world) + piece.Offset));
                for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
                    Triangles.Add(new Triangle(first + mesh.Indices[i], first + mesh.Indices[i + 1], first + mesh.Indices[i + 2], p, draw.Transform));
            }
        }

        /// <summary>Adds triangles as one draw, its vertices in the piece's own coordinates, for tests.</summary>
        public void Add(Piece piece, IReadOnlyList<Vector3> vertices, IEnumerable<(int A, int B, int C, int Node)> triangles)
        {
            var p = Pieces.Count;
            Pieces.Add(piece);
            var first = Vertices.Count;
            for (var v = 0; v < vertices.Count; v++)
                Vertices.Add(new Vertex(p, new DrawVertex(0, v), vertices[v] + piece.Offset));
            foreach (var (a, b, c, node) in triangles)
                Triangles.Add(new Triangle(first + a, first + b, first + c, p, node));
        }
    }

    /// <summary>One end of a boundary edge, and what borders it.</summary>
    private readonly record struct Edge(int P, int Q, int Triangle, int Third);

    /// <summary>Where the pieces' welded vertices go, in each one's own scene's coordinates, by scene.</summary>
    internal static Dictionary<int, Dictionary<DrawVertex, Vector3>> Weld(Geometry g)
    {
        // Vertices at one place (on one layer: scenery and water don't weld) are one point.
        var pointOf = new int[g.Vertices.Count];
        var points = new List<Vector3>();
        var members = new List<List<int>>();
        var keys = new Dictionary<(bool, long, long, long), int>();
        for (var v = 0; v < g.Vertices.Count; v++)
        {
            var position = g.Vertices[v].Position;
            var key = (g.Pieces[g.Vertices[v].Piece].Alt, Quantise(position.X), Quantise(position.Y), Quantise(position.Z));
            if (!keys.TryGetValue(key, out var point))
            {
                keys[key] = point = points.Count;
                points.Add(position);
                members.Add([]);
            }
            pointOf[v] = point;
            members[point].Add(v);
        }

        // An edge one triangle borders is open: half a seam, or the mesh's true edge.
        var uses = new Dictionary<(int, int), (int Count, int Triangle, int Third)>();
        for (var t = 0; t < g.Triangles.Count; t++)
        {
            var tri = g.Triangles[t];
            int[] corners = [pointOf[tri.A], pointOf[tri.B], pointOf[tri.C]];
            if (corners[0] == corners[1] || corners[1] == corners[2] || corners[0] == corners[2])
                continue;
            for (var e = 0; e < 3; e++)
            {
                var key = Ordered(corners[e], corners[(e + 1) % 3]);
                uses[key] = uses.TryGetValue(key, out var u) ? (u.Count + 1, u.Triangle, u.Third) : (1, t, corners[(e + 2) % 3]);
            }
        }
        var edges = new List<Edge>();
        var edgeAt = new Dictionary<(int, int), int>();
        var edgesOf = new Dictionary<int, List<int>>();
        foreach (var (key, u) in uses)
        {
            if (u.Count != 1)
                continue;
            edgeAt[key] = edges.Count;
            foreach (var end in new[] { key.Item1, key.Item2 })
                (edgesOf.TryGetValue(end, out var list) ? list : edgesOf[end] = []).Add(edges.Count);
            edges.Add(new Edge(key.Item1, key.Item2, u.Triangle, u.Third));
        }
        var grid = new SpatialHash(ChunkGap);
        foreach (var point in edgesOf.Keys)
            grid.Add(points[point], point);

        // Pair each open edge with another whose ends are each near its own.
        var parent = Enumerable.Range(0, points.Count).ToArray();
        var limitOf = new float[points.Count];
        int Root(int p) => parent[p] == p ? p : parent[p] = Root(parent[p]);
        for (var i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            foreach (var p in grid.Near(points[e.P]))
            {
                foreach (var q in grid.Near(points[e.Q]))
                {
                    if (p == q || !edgeAt.TryGetValue(Ordered(p, q), out var j) || j <= i)
                        continue;
                    var f = edges[j];
                    var gap = MathF.Max(Vector3.Distance(points[e.P], points[p]), Vector3.Distance(points[e.Q], points[q]));
                    var limit = Limit(g, e.Triangle, f.Triangle, (points[e.P] + points[e.Q]) / 2);
                    if (gap > limit || gap > EdgeShare * MathF.Min(Length(points, e), Length(points, f))
                        || !Seam(points[e.P], points[e.Q], points[e.Third], points[f.Third]))
                        continue;
                    foreach (var (a, b) in new[] { (e.P, p), (e.Q, q) })
                    {
                        if (a == b)
                            continue;
                        limitOf[a] = MathF.Max(limitOf[a], limit);
                        limitOf[b] = MathF.Max(limitOf[b], limit);
                        parent[Root(a)] = Root(b);
                    }
                }
            }
        }

        // Each group of points goes to their middle, unless that's farther than they may move or it would close one of their edges.
        var moved = new Dictionary<int, Vector3>();
        foreach (var group in edgesOf.Keys.GroupBy(Root))
        {
            var list = group.ToList();
            if (list.Count < 2)
                continue;
            var middle = list.Aggregate(Vector3.Zero, (sum, p) => sum + points[p]) / list.Count;
            if (list.Any(p => Vector3.Distance(points[p], middle) > limitOf[p])
                || list.Any(p => list.Any(q => p < q && uses.ContainsKey(Ordered(p, q))))
                || list.Any(p => members[p].Any(v => g.Pieces[g.Vertices[v].Piece].Fixed)))
                continue;
            var to = Snap(middle);
            foreach (var p in list)
                moved[p] = to;
        }
        Vector3 At(int p) => moved.TryGetValue(p, out var to) ? to : points[p];

        // A point along another mesh's open edge, beside it rather than on it, goes onto it, if one of its own open edges runs along it too.
        var lines = new SpatialHash(ChunkGap);
        for (var i = 0; i < edges.Count; i++)
            lines.AddSegment(At(edges[i].P), At(edges[i].Q), i);
        foreach (var r in edgesOf.Keys.Where(p => !moved.ContainsKey(p)).ToList())
        {
            var at = points[r];
            (float Gap, Vector3 Foot)? best = null;
            foreach (var i in lines.Near(at))
            {
                var e = edges[i];
                var (p, q) = (At(e.P), At(e.Q));
                var along = q - p;
                var s = Vector3.Dot(at - p, along) / along.LengthSquared();
                if (s is <= 0 or >= 1 || r == e.P || r == e.Q)
                    continue;
                var foot = p + along * s;
                var gap = Vector3.Distance(at, foot);
                var own = edgesOf[r].Select(k => edges[k]).ToList();
                var limit = own.Max(k => Limit(g, k.Triangle, e.Triangle, foot));
                if (gap < Same || gap > limit || Vector3.Distance(at, p) <= limit || Vector3.Distance(at, q) <= limit
                    || own.Any(k => k.Triangle == e.Triangle)
                    || !own.Any(k => Seam(p, q, points[e.Third], Centroid(g, k.Triangle)))
                    || !own.Any(k => Off(At(k.P == r ? k.Q : k.P), p, q) <= limit))
                    continue;
                if (best is null || gap < best.Value.Gap)
                    best = (gap, foot);
            }
            if (best is { } b && !members[r].Any(v => g.Pieces[g.Vertices[v].Piece].Fixed))
                moved[r] = b.Foot;
        }

        var welds = new Dictionary<int, Dictionary<DrawVertex, Vector3>>();
        foreach (var (point, to) in moved)
        {
            foreach (var v in members[point])
            {
                var vertex = g.Vertices[v];
                var piece = g.Pieces[vertex.Piece];
                (welds.TryGetValue(piece.Scene, out var scene) ? scene : welds[piece.Scene] = [])[vertex.At] = to - piece.Offset;
            }
        }
        return welds;
    }

    /// <summary>How far apart the edges of two triangles can be welded at <paramref name="site"/>, or 0 where they mustn't be.</summary>
    private static float Limit(Geometry g, int a, int b, Vector3 site)
    {
        var (ta, tb) = (g.Triangles[a], g.Triangles[b]);
        var (pa, pb) = (g.Pieces[ta.Piece], g.Pieces[tb.Piece]);
        if (ta.Piece == tb.Piece)
            return ta.Node == tb.Node || !pa.NodesMove ? ModelGap : 0;
        if (pa.Cell is not { } ca || pb.Cell is not { } cb || pa.Alt != pb.Alt
            || Math.Max(Math.Abs(ca.Row - cb.Row), Math.Abs(ca.Column - cb.Column)) > 1)
            return 0;
        return pa.Past(site) <= Border && pb.Past(site) <= Border ? ChunkGap : 0;
    }

    /// <summary>Whether two triangles on an edge, by their corners off it, turn away from each other: a seam, not one over the other.</summary>
    internal static bool Seam(Vector3 p, Vector3 q, Vector3 a, Vector3 b)
    {
        var axis = Vector3.Normalize(q - p);
        Vector3 Across(Vector3 x) => x - p - axis * Vector3.Dot(x - p, axis);
        var (u, v) = (Across(a), Across(b));
        if (u.LengthSquared() < 1e-12f || v.LengthSquared() < 1e-12f)
            return false;
        return Vector3.Dot(Vector3.Normalize(u), Vector3.Normalize(v)) < Fold;
    }

    /// <summary>How far a point is from the line through <paramref name="p"/> and <paramref name="q"/>.</summary>
    private static float Off(Vector3 x, Vector3 p, Vector3 q)
    {
        var axis = Vector3.Normalize(q - p);
        var d = x - p;
        return (d - axis * Vector3.Dot(d, axis)).Length();
    }

    private static Vector3 Centroid(Geometry g, int t)
    {
        var tri = g.Triangles[t];
        return (g.Vertices[tri.A].Position + g.Vertices[tri.B].Position + g.Vertices[tri.C].Position) / 3;
    }

    private static float Length(List<Vector3> points, Edge e) => Vector3.Distance(points[e.P], points[e.Q]);

    internal static Vector3 Snap(Vector3 v) => new(MathF.Round(v.X * Grid) / Grid, MathF.Round(v.Y * Grid) / Grid, MathF.Round(v.Z * Grid) / Grid);

    private static long Quantise(float x) => (long)MathF.Round(x / Same);

    private static (int, int) Ordered(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>Points and segments by the cubes of a size they pass through, found again by the cube a point is in and those round it.</summary>
    private sealed class SpatialHash(float size)
    {
        private readonly Dictionary<(int, int, int), List<int>> _cells = [];

        private (int, int, int) Cell(Vector3 v) => ((int)MathF.Floor(v.X / size), (int)MathF.Floor(v.Y / size), (int)MathF.Floor(v.Z / size));

        public void Add(Vector3 at, int item) => Add(Cell(at), item);

        private void Add((int, int, int) cell, int item) =>
            (_cells.TryGetValue(cell, out var list) ? list : _cells[cell] = []).Add(item);

        public void AddSegment(Vector3 p, Vector3 q, int item)
        {
            var steps = Math.Min(10000, (int)(Vector3.Distance(p, q) / (size / 2)) + 1);
            var seen = new HashSet<(int, int, int)>();
            for (var i = 0; i <= steps; i++)
            {
                var cell = Cell(Vector3.Lerp(p, q, i / (float)steps));
                if (seen.Add(cell))
                    Add(cell, item);
            }
        }

        public IEnumerable<int> Near(Vector3 at)
        {
            var (x, y, z) = Cell(at);
            var found = new HashSet<int>();
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        if (_cells.TryGetValue((x + dx, y + dy, z + dz), out var list))
                            foreach (var item in list)
                                if (found.Add(item))
                                    yield return item;
                    }
        }
    }
}
