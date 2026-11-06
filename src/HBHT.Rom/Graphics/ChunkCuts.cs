using System.Numerics;
using HBHT.Rom.Maps;

namespace HBHT.Rom.Graphics;

/// <summary>
/// A vertex as the scene export writes it, on its draw's node: where it is, its normal (a lit list's) or colour (an unlit one's),
/// its texture coordinates in S and T's units, and whether the RSP loads it second of a pair (<see cref="N64Vertex.SecondOfPair"/>).
/// A cut makes new ones between two of a triangle's, which carry a blend of both.
/// </summary>
public readonly record struct CutVertex(Vector3 Position, Vector3 Normal, Vector4 Color, Vector2 St, bool Second)
{
    /// <summary>A model's vertex at <paramref name="position"/>, its colour as the N64 has it, 0 to 1.</summary>
    public static CutVertex Of(N64Vertex v, Vector3 position)
    {
        var (x, y, z) = v.Normal;
        return new(position, new Vector3(x, y, z), new Vector4(v.R, v.G, v.B, v.A) / 255, new Vector2(v.S, v.T), v.SecondOfPair);
    }

    /// <summary>The vertex <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static CutVertex Lerp(in CutVertex a, in CutVertex b, float t) => new(
        Vector3.Lerp(a.Position, b.Position, t), Vector3.Lerp(a.Normal, b.Normal, t), Vector4.Lerp(a.Color, b.Color, t),
        Vector2.Lerp(a.St, b.St, t), t < 0.5f ? a.Second : b.Second);
}

/// <summary>What a chunk scene's export draws for the draws a cut changed: each one's polygons, on its node. The rest are as they are.</summary>
public sealed class SceneCut
{
    public Dictionary<int, List<CutVertex[]>> Draws { get; } = [];
}

/// <summary>A draw's polygons in a chunk's fill, in the fill's chunk's coordinates: a scene's draw, moved there from its own chunk.</summary>
public sealed record FillPiece(int Scene, int Draw, IReadOnlyList<CutVertex[]> Polygons);

/// <summary>
/// A cell's fill: what other chunks' scenes drew over it that its own don't, and the chunk nearest it that gave some, whose terrain
/// and whose Draw Distance a chunk made for the fill takes.
/// </summary>
/// <param name="Sea">The height of the sea, where the fill is a square of it (<see cref="ChunkCuts.Squares"/>).</param>
public sealed record CellFill(int Row, int Column, IReadOnlyList<FillPiece> Pieces, (int Row, int Column) Nearest, float? Sea = null);

/// <summary>
/// Takes out of a map set's chunk scenes what they draw over other chunks' cells, so that a chunk draws its own cell, and its
/// neighbours' where its art reaches into them, and nothing further (docs/systems/rendering.md#draw-distance).
/// </summary>
/// <remarks>
/// <para>
/// The original loads the 3×3 round the player and nothing else, so its chunks carry what's to be seen past that: a copy of a
/// chunk two to four away (M20 draws all of J19), a rough sheet of the land round them, and sheets of sea over cells with no
/// chunk. A chunk's byte 0x1A leaves a neighbour unloaded where it draws a copy of it (H08's of I09). The port loads the real
/// chunks instead, so each drawn triangle of a chunk's scenes, as the export draws it, is taken by the cell it's over:
/// </para>
/// <list type="bullet">
/// <item>Within the chunk's own cell and <see cref="Border"/> past it, the seams' overlap, it stays.</item>
/// <item>Over a neighbour with a chunk, it stays, as the art that reaches across an edge, unless the whole triangle is over the
/// neighbour and a copy of its own scenes there, which is dropped.</item>
/// <item>Over a cell two or more away with a chunk, or any cell without one, it's cut at the cells' edges, and each piece is dropped
/// if what the cell draws already covers it, and otherwise moved into that cell's fill, nearest chunks first: what the cell's own
/// scenes and its neighbours' draw over it covers any piece from further, and of two pieces from as far, the second is dropped
/// where it lies on the first. Opaque sea isn't taken for a copy of half-alpha sea; instead the half-alpha sea goes from a fill
/// wherever opaque sea lies at its height.</item>
/// </list>
/// <para>
/// A chunk that lost anything over a cell <see cref="Shows"/> the chunks that now draw it there, which the runtime draws while the
/// chunk is loaded.
/// </para>
/// </remarks>
public sealed class ChunkCuts
{
    /// <summary>How far past its cell a chunk's scenes keep what they draw, wherever it is: the seams' overlap.</summary>
    public const float Border = SeamWelds.Border;

    /// <summary>How far apart the points are that coverage is measured at, across the ground.</summary>
    internal const float Spacing = 2;

    /// <summary>The share of a piece's points that must be covered for it to be dropped.</summary>
    internal const float CoveredShare = 0.95f;

    /// <summary>The share of a piece's points that must lie on another's for it to be a copy.</summary>
    internal const float CopyShare = 0.9f;

    /// <summary>How near a point must be to another, up and down, to lie on it.</summary>
    internal const float CopyHeight = 0.75f;

    /// <summary>The share of a piece's points covered under which it's kept whole; between it and <see cref="CoveredShare"/>, it's cut finer.</summary>
    internal const float UncoveredShare = 0.05f;

    /// <summary>The grid a piece partly covered is cut along, to keep what isn't.</summary>
    internal const float Finer = 10;

    /// <summary>What each chunk scene the cut changed draws, by the scene's archive index.</summary>
    public IReadOnlyDictionary<int, SceneCut> Scenes { get; }

    /// <summary>The fills, by cell.</summary>
    public IReadOnlyList<CellFill> Fills { get; }

    /// <summary>The cells each chunk shows (<see cref="HBHT.Plugins.Records.ChunkEntry.Shows"/>), by its cell, sorted.</summary>
    public IReadOnlyDictionary<(int Row, int Column), IReadOnlyList<(int Row, int Column)>> Shows { get; }

    /// <summary>The chunks' scenes' scrolling water as the cut leaves it, polygons in the world: what the fills and squares meet.</summary>
    internal IReadOnlyList<Vector3[]> Water { get; }

    private ChunkCuts(IReadOnlyDictionary<int, SceneCut> scenes, IReadOnlyList<CellFill> fills,
        IReadOnlyDictionary<(int, int), IReadOnlyList<(int, int)>> shows, IReadOnlyList<Vector3[]>? water = null)
    {
        Scenes = scenes;
        Fills = fills;
        Shows = shows;
        Water = water ?? [];
    }

    /// <summary>A scene a chunk draws: its own or see-through scene, or one it swaps in.</summary>
    internal sealed record Source(int Row, int Column, int Scene, Vector3 Origin);

    /// <summary>A point of a polygon, where it is in the world and as the export writes it.</summary>
    internal readonly record struct Corner(Vector3 World, CutVertex Vertex);

    /// <summary>
    /// A triangle a source draws, with the matrix that places its draw's node in the scene, and whether its draw's texture scrolls
    /// (<see cref="Borg1Flags.Moving"/>), as water's does, and whether it's drawn opaque.
    /// </summary>
    internal readonly record struct Triangle(int Source, int Draw, Matrix4x4 World, Corner A, Corner B, Corner C, bool Scrolls = false,
        bool Opaque = true);

    /// <summary>A piece over a cell it may move into, from a triangle of a chunk further away.</summary>
    private sealed record Candidate(int Source, int Draw, int Order, Matrix4x4 World, (int Row, int Column) Cell, int Rank, Corner[] Polygon);

    /// <summary>
    /// Cuts one map set's chunk scenes.
    /// </summary>
    /// <param name="chunks">The map set's chunks.</param>
    /// <param name="swaps">The scenes chunks swap in while a flag holds, with their chunk.</param>
    /// <param name="read">Reads a Borg by archive index.</param>
    /// <param name="welds">Where <see cref="SeamWelds"/> moved the scenes' vertices, by scene, as the export takes them.</param>
    public static ChunkCuts For(IReadOnlyList<MapChunk> chunks, IEnumerable<(MapChunk Chunk, int Scene)> swaps, Func<int, byte[]> read,
        IReadOnlyDictionary<int, Dictionary<DrawVertex, Vector3>> welds)
    {
        if (chunks.Select(c => c.Set).Distinct().Count() > 1)
            throw new ArgumentException("The chunks of one map set are cut at a time.", nameof(chunks));
        var sources = new List<Source>();
        var seen = new HashSet<int>();
        // The islands' scenes stay whole, as 1.0 draws them: nothing else is loaded or shown from an island, nor an island from the
        // mainland, so their art doubles nothing. Out of the cut, their cells still chunks' (docs/systems/rendering.md#draw-distance).
        var islands = chunks.Count == 0 ? [] : MapSets.Islands(chunks);
        foreach (var (chunk, scene) in chunks.SelectMany(c => new[] { (c, c.Scene), (c, c.AltScene) }).Concat(swaps))
        {
            if (islands.Contains((chunk.Row, chunk.Column)))
                continue;
            if (scene != 0 && seen.Add(scene))
                sources.Add(new Source(chunk.Row, chunk.Column, scene, MapSets.ChunkOrigin(chunk)));
        }
        var cellSize = chunks.Count == 0 ? 1 : MapSets.CellSize(chunks[0].Set);
        var models = new Dictionary<int, Borg2Geometry>();
        var textures = new Dictionary<int, Borg1Flags>();
        var triangles = new List<Triangle>();
        for (var s = 0; s < sources.Count; s++)
        {
            var source = sources[s];
            var scene = Borg5Scene.Parse(read(source.Scene));
            var moved = welds.GetValueOrDefault(source.Scene);
            for (var d = 0; d < scene.Draws.Count; d++)
            {
                var draw = scene.Draws[d];
                if (!models.TryGetValue(scene.Models[draw.Model], out var model))
                    models[scene.Models[draw.Model]] = model = Borg2Geometry.Parse(read(scene.Models[draw.Model]));
                var transform = scene.Transforms[draw.Transform];
                if (!SceneGltf.IsDrawn(model, draw.DisplayList) || transform.Op != Borg5TransformOp.PivotRotate
                    || transform.Flags.HasFlag(Borg5TransformFlags.Billboard))
                    continue;
                var mesh = model.Meshes[draw.DisplayList];
                var flags = Borg1Flags.None;
                if (draw.Texture is { } slot && !textures.TryGetValue(scene.Textures[slot], out flags))
                    textures[scene.Textures[slot]] = flags = Borg1Texture.Parse(read(scene.Textures[slot])).Flags;
                // As the export's material has it: the tint's and the model's alpha, and the texture's own.
                var opaque = mesh.Tint.W == 0 && model.Alpha == 0 && !flags.HasFlag(Borg1Flags.HasTransparency);
                var scrolls = flags.HasFlag(Borg1Flags.Moving);
                var world = scene.WorldMatrix(draw.Transform);
                Matrix4x4.Invert(world, out var toNode);
                var corners = new Corner[mesh.Vertices.Length];
                for (var v = 0; v < corners.Length; v++)
                {
                    var position = moved is not null && moved.TryGetValue(new DrawVertex(d, v), out var welded)
                        ? Vector3.Transform(welded, toNode)
                        : SceneGltf.Position(model, mesh.Vertices[v]);
                    corners[v] = new Corner(Vector3.Transform(position, world) + source.Origin, CutVertex.Of(mesh.Vertices[v], position));
                }
                for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
                    triangles.Add(new Triangle(s, d, world, corners[mesh.Indices[i]], corners[mesh.Indices[i + 1]], corners[mesh.Indices[i + 2]],
                        scrolls, opaque));
            }
        }
        return Cut([.. chunks.Select(c => (c.Row, c.Column))], sources, cellSize, triangles);
    }

    /// <summary>
    /// Cuts the triangles of a map set's sources, whose chunks are in <paramref name="cells"/>, and makes the cells that are open sea
    /// squares of it (<see cref="Squares"/>): the cut again, with those cells drawn by their squares alone.
    /// </summary>
    internal static ChunkCuts Cut(HashSet<(int Row, int Column)> cells, IReadOnlyList<Source> sources, float cellSize, List<Triangle> triangles)
    {
        var first = new Cutter(cells, sources, cellSize, new Dictionary<(int, int), float>()).Cut(triangles);
        var waterDraws = triangles.Where(t => t.Scrolls).Select(t => (sources[t.Source].Scene, t.Draw)).ToHashSet();
        var squares = Squares(first, sources, cellSize, triangles);
        if (squares is not { } sea)
            return new ChunkCuts(first.Scenes, Met(first, cells, new Dictionary<(int, int), float>(), waterDraws, cellSize), first.Shows);
        var second = new Cutter(cells, sources, cellSize, sea.Heights).Cut(triangles);
        var nearest = first.Fills.ToDictionary(f => (f.Row, f.Column), f => f.Nearest);
        var withSquares = new ChunkCuts(second.Scenes, [.. second.Fills.Where(f => !sea.Heights.ContainsKey((f.Row, f.Column)))
            .Concat(sea.Heights.Select(s => new CellFill(s.Key.Row, s.Key.Column, [sea.Piece(s.Key, s.Value)], nearest[s.Key], s.Value)))
            .OrderBy(f => f.Row).ThenBy(f => f.Column)], second.Shows, second.Water);
        return new ChunkCuts(second.Scenes, Met(withSquares, cells, sea.Heights, waterDraws, cellSize), second.Shows);
    }

    /// <summary>
    /// The fills with their edges met to the water beside them (<see cref="SeaRims"/>): a square's to its neighbour's own water and
    /// fill, eased in from its edges; another fill's sea to a real chunk's own water.
    /// </summary>
    private static List<CellFill> Met(ChunkCuts cuts, HashSet<(int Row, int Column)> cells, IReadOnlyDictionary<(int Row, int Column), float> squares,
        HashSet<(int Scene, int Draw)> waterDraws, float cellSize)
    {
        var fills = cuts.Fills.ToDictionary(f => (f.Row, f.Column));
        Vector3 Origin((int Row, int Column) c) => new Vector3(c.Row, 0, c.Column) * cellSize;
        (int, int) CellOf(Vector3 p) => ((int)MathF.Floor(p.X / cellSize), (int)MathF.Floor(p.Z / cellSize));
        var sceneWater = cuts.Water.GroupBy(p => CellOf(p.Aggregate(Vector3.Zero, (a, v) => a + v) / p.Length))
            .ToDictionary(g => g.Key, g => g.ToList());
        IEnumerable<Vector3[]> FillWater((int, int) cell) => fills.TryGetValue(cell, out var f) && !squares.ContainsKey(cell)
            ? f.Pieces.Where(p => waterDraws.Contains((p.Scene, p.Draw))).SelectMany(p => p.Polygons)
                .Select(p => p.Select(v => v.Position + Origin(cell)).ToArray())
            : [];
        // A side's water, in the cell's coordinates: the neighbour's own, and for a square its fill too.
        SeaRims.Profile? Side((int Row, int Column) cell, int side, float height, bool withFill)
        {
            var next = (cell.Row + SeaRims.Toward[side].Row, cell.Column + SeaRims.Toward[side].Column);
            if (squares.ContainsKey(next))
                return null;
            var water = sceneWater.GetValueOrDefault(next) ?? [];
            var polygons = (withFill ? water.Concat(FillWater(next)) : water).Select(p => p.Select(v => v - Origin(cell)).ToArray());
            return SeaRims.Of(polygons, side, cellSize, height, SeaHeight);
        }

        // The squares: their edges' water, and the field that eases it in, read in the world.
        var profiles = squares.ToDictionary(s => s.Key, s => Enumerable.Range(0, 4).Select(side => Side(s.Key, side, s.Value, withFill: true)).ToList());
        var rims = new List<(Vector2, Vector2, float, float)>();
        foreach (var (cell, sides) in profiles)
        {
            var origin = Origin(cell);
            for (var side = 0; side < 4; side++)
            {
                if (sides[side] is not { } profile)
                    continue;
                foreach (var span in profile.Spans)
                {
                    Vector2 At(float t) => side < 2
                        ? new Vector2(origin.X + (side == 0 ? 0 : cellSize), origin.Z + t)
                        : new Vector2(origin.X + t, origin.Z + (side == 2 ? 0 : cellSize));
                    rims.Add((At(span.T0), At(span.T1), span.Y0 - squares[cell], span.Y1 - squares[cell]));
                }
            }
        }
        var field = new SeaRims.Field(rims);

        var met = new List<CellFill>();
        foreach (var fill in cuts.Fills)
        {
            var cell = (fill.Row, fill.Column);
            if (squares.TryGetValue(cell, out var height))
            {
                var origin = Origin(cell);
                met.Add(fill with { Pieces = [.. fill.Pieces.Select(p => p with { Polygons = SeaRims.Meet(p.Polygons, profiles[cell], cellSize,
                    v => height + field.Offset(new Vector2(origin.X + v.X, origin.Z + v.Z))) })] });
                continue;
            }
            // Another fill's sea meets a real chunk's own water.
            var pieces = fill.Pieces.Select(piece =>
            {
                if (!waterDraws.Contains((piece.Scene, piece.Draw)) || piece.Polygons.Count == 0)
                    return piece;
                var level = piece.Polygons.SelectMany(p => p).Average(v => v.Position.Y);
                var sides = Enumerable.Range(0, 4).Select(side =>
                {
                    var next = (fill.Row + SeaRims.Toward[side].Row, fill.Column + SeaRims.Toward[side].Column);
                    return cells.Contains(next) ? Side(cell, side, level, withFill: false) : null;
                }).ToList();
                return sides.All(s => s is null) ? piece : piece with { Polygons = SeaRims.Meet(piece.Polygons, sides, cellSize) };
            }).ToList();
            met.Add(fill with { Pieces = pieces });
        }
        return met;
    }

    /// <summary>How far apart a sea triangle's corners may be, up and down, and from a square's height, to be that sea.</summary>
    internal const float SeaHeight = 1.5f;

    /// <summary>How far apart the points of a square of sea are, across the ground: the N64's fog is worked out at each.</summary>
    internal const float SquareSpacing = 10;

    /// <summary>The squares of sea a cut makes, by cell, at their heights, and the square it makes for a cell.</summary>
    private sealed record SeaSquares(Dictionary<(int Row, int Column), float> Heights, Func<(int Row, int Column), float, FillPiece> Piece);

    /// <summary>
    /// The cells a first cut leaves as open sea: those whose own chunk, if they have one, draws nothing over them, and whose fill is
    /// all flat, scrolling water at one height. Each becomes a square of the sea that covers most of them, drawn as that sea's draw
    /// draws it, at the cell's height, with its texture laid on the world as that draw lays it, so the squares
    /// meet one another seamlessly.
    /// </summary>
    private static SeaSquares? Squares(ChunkCuts first, IReadOnlyList<Source> sources, float cellSize, List<Triangle> triangles)
    {
        var bySceneDraw = new Dictionary<(int Scene, int Draw), (bool Scrolls, bool Opaque)>();
        var ownArt = new HashSet<(int Row, int Column)>();
        foreach (var t in triangles)
        {
            var source = sources[t.Source];
            bySceneDraw[(source.Scene, t.Draw)] = (t.Scrolls, t.Opaque);
            foreach (var c in new[] { t.A, t.B, t.C })
            {
                if (c.World.X > source.Row * cellSize && c.World.X < (source.Row + 1) * cellSize
                    && c.World.Z > source.Column * cellSize && c.World.Z < (source.Column + 1) * cellSize)
                    ownArt.Add((source.Row, source.Column));
            }
        }
        var heights = new Dictionary<(int Row, int Column), float>();
        var mapped = new Dictionary<(int Scene, int Draw, Vector2 S, Vector2 T), float>();
        var looks = new Dictionary<(int Scene, int Draw), Dictionary<(Vector3 Normal, Vector4 Color), float>>();
        foreach (var fill in first.Fills)
        {
            if (ownArt.Contains((fill.Row, fill.Column)))
                continue;
            var weighted = 0f;
            var total = 0f;
            var low = float.MaxValue;
            var high = float.MinValue;
            var sea = true;
            foreach (var piece in fill.Pieces)
            {
                sea &= bySceneDraw.GetValueOrDefault((piece.Scene, piece.Draw)).Scrolls;
                foreach (var polygon in piece.Polygons)
                {
                    var area = Area(polygon);
                    foreach (var v in polygon)
                    {
                        low = Math.Min(low, v.Position.Y);
                        high = Math.Max(high, v.Position.Y);
                    }
                    weighted += polygon.Average(v => v.Position.Y) * area;
                    total += area;
                }
            }
            if (!sea || total == 0 || high - low > SeaHeight)
                continue;
            heights[(fill.Row, fill.Column)] = weighted / total;
            // How each opaque draw lays its texture on the world, by area, and how its corners are shaded.
            foreach (var piece in fill.Pieces)
            {
                if (!bySceneDraw[(piece.Scene, piece.Draw)].Opaque)
                    continue;
                foreach (var polygon in piece.Polygons)
                {
                    if (Gradient(polygon) is not { } g)
                        continue;
                    var key = (piece.Scene, piece.Draw, g.S, g.T);
                    mapped[key] = mapped.GetValueOrDefault(key) + Area(polygon);
                    if (!looks.TryGetValue((piece.Scene, piece.Draw), out var look))
                        looks[(piece.Scene, piece.Draw)] = look = [];
                    foreach (var v in polygon)
                        look[(v.Normal, v.Color)] = look.GetValueOrDefault((v.Normal, v.Color)) + Area(polygon) / polygon.Length;
                }
            }
        }
        // The sea's draw: the opaque one that covers the most of them, laying its texture square to the world and unmirrored.
        var draws = mapped.Where(m => m.Key.S.X > 0 && m.Key.S.Y == 0 && m.Key.T.X == 0 && m.Key.T.Y == m.Key.S.X).ToList();
        if (heights.Count == 0 || draws.Count == 0)
            return null;
        Level(heights);
        var (scene, draw, s, _) = draws.MaxBy(m => m.Value).Key;
        var (normal, color) = looks[(scene, draw)].MaxBy(l => l.Value).Key;
        var scale = s.X;
        // The texture's coordinates where the cell starts, less whole turns of the widest texture (256 texels at 64 units each).
        float Start(float origin) => origin * scale % (256 * 64);
        FillPiece Piece((int Row, int Column) cell, float height)
        {
            var (x0, z0) = (Start(cell.Row * cellSize), Start(cell.Column * cellSize));
            var steps = (int)MathF.Ceiling(cellSize / SquareSpacing);
            // The last row and column on the cell's edge exactly, where the next square's first lie.
            float Along(int i) => i == steps ? cellSize : i * (cellSize / steps);
            CutVertex At(int i, int k) => new(new Vector3(Along(i), height, Along(k)), normal, color,
                new Vector2(x0 + Along(i) * scale, z0 + Along(k) * scale), false);
            var quads = new List<CutVertex[]>();
            for (var i = 0; i < steps; i++)
            {
                for (var k = 0; k < steps; k++)
                    quads.Add([At(i, k), At(i, k + 1), At(i + 1, k + 1), At(i + 1, k)]);
            }
            return new FillPiece(scene, draw, quads);
        }
        return new SeaSquares(heights, Piece);
    }

    /// <summary>How far apart two squares side by side or corner to corner may lie, up and down, to be one sea at one height.</summary>
    internal const float LevelTolerance = 0.5f;

    /// <summary>
    /// Puts each run of squares that touch, side by side or corner to corner, within <see cref="LevelTolerance"/> of one another, at
    /// one height, their mean. The fills they're made from lie a little apart (−31.917 to −32.020 round Terminor), and at the low
    /// angle the sea is seen from, each step between two squares opened a crack along their edge, with the sky behind it.
    /// Gwernia's bay, 2.6 higher, stays a run of its own.
    /// </summary>
    private static void Level(Dictionary<(int Row, int Column), float> heights)
    {
        var seen = new HashSet<(int Row, int Column)>();
        foreach (var start in heights.Keys.OrderBy(c => c.Row).ThenBy(c => c.Column).ToList())
        {
            if (!seen.Add(start))
                continue;
            var run = new List<(int Row, int Column)> { start };
            for (var i = 0; i < run.Count; i++)
            {
                var (row, column) = run[i];
                for (var dr = -1; dr <= 1; dr++)
                {
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        var next = (row + dr, column + dc);
                        if (heights.TryGetValue(next, out var h) && MathF.Abs(h - heights[run[i]]) <= LevelTolerance && seen.Add(next))
                            run.Add(next);
                    }
                }
            }
            var mean = run.Average(c => heights[c]);
            foreach (var c in run)
                heights[c] = mean;
        }
    }

    /// <summary>How a polygon's texture coordinates change across the ground, per unit of X and of Z, rounded, or null for one with no area there.</summary>
    private static (Vector2 S, Vector2 T)? Gradient(CutVertex[] polygon)
    {
        var (a, b, c) = (polygon[0], polygon[1], polygon[2]);
        var e1 = new Vector2(b.Position.X - a.Position.X, b.Position.Z - a.Position.Z);
        var e2 = new Vector2(c.Position.X - a.Position.X, c.Position.Z - a.Position.Z);
        var det = e1.X * e2.Y - e1.Y * e2.X;
        if (MathF.Abs(det) < 0.25f)
            return null;
        var d1 = b.St - a.St;
        var d2 = c.St - a.St;
        // St = M (x, z) + o, solved from two edges.
        var ds = new Vector2((d1.X * e2.Y - d2.X * e1.Y) / det, (d2.X * e1.X - d1.X * e2.X) / det);
        var dt = new Vector2((d1.Y * e2.Y - d2.Y * e1.Y) / det, (d2.Y * e1.X - d1.Y * e2.X) / det);
        static Vector2 Round(Vector2 v) => new(MathF.Round(v.X * 4) / 4, MathF.Round(v.Y * 4) / 4);
        return (Round(ds), Round(dt));
    }

    private static float Area(CutVertex[] polygon)
    {
        var area = Vector3.Zero;
        for (var i = 1; i + 1 < polygon.Length; i++)
            area += Vector3.Cross(polygon[i].Position - polygon[0].Position, polygon[i + 1].Position - polygon[0].Position);
        return area.Length() / 2;
    }

    /// <summary>Does the cutting, over a map set's triangles.</summary>
    private sealed class Cutter(HashSet<(int Row, int Column)> real, IReadOnlyList<Source> sources, float cellSize,
        IReadOnlyDictionary<(int Row, int Column), float> squares)
    {
        private readonly HashSet<(int Row, int Column)> _real = real;
        private readonly Coverage _coverage = new(cellSize);
        private readonly Dictionary<(int Row, int Column), SortedSet<(int Row, int Column)>> _shows = [];

        private List<Triangle> _triangles = [];

        public ChunkCuts Cut(List<Triangle> triangles)
        {
            _triangles = triangles;
            // A square of sea draws all its cell, before anything else is weighed.
            foreach (var (cell, height) in squares)
            {
                Corner P(int i, int k) => new(new Vector3((cell.Row + i) * cellSize, height, (cell.Column + k) * cellSize), default);
                _coverage.Add([P(0, 0), P(0, 1), P(1, 1), P(1, 0)], cell, cell, sea: true);
            }
            // What a chunk's scenes keep is in the coverage before any piece is weighed against it, its own cell first, so a
            // neighbour's copy of it is known.
            var outside = new List<int>();
            for (var t = 0; t < triangles.Count; t++)
            {
                var tri = triangles[t];
                if (InBox(tri, Own(sources[tri.Source])) && !OverSquare(tri))
                    _coverage.Add(Polygon(tri), Cell(sources[tri.Source]), Cell(sources[tri.Source]), Sea(tri), Shallows(tri));
                else
                    outside.Add(t);
            }

            var candidates = new List<Candidate>();
            var changed = new HashSet<(int Source, int Draw)>();
            var keptParts = new Dictionary<int, List<Corner[]>>();
            foreach (var t in outside)
            {
                var tri = triangles[t];
                var source = sources[tri.Source];
                var owner = Cell(source);
                var polygon = Polygon(tri);
                // One with no area draws nothing, and goes.
                if (Solid([.. polygon]) is null)
                {
                    changed.Add((tri.Source, tri.Draw));
                    keptParts[t] = [];
                    continue;
                }
                // Sea over a square of it goes, at the square's edge, whatever chunk it's from: the square draws it there.
                var parts = new List<Corner[]> { polygon };
                var clipped = false;
                if (OverSquare(tri))
                {
                    parts.Clear();
                    foreach (var piece in Grid(polygon, cellSize))
                    {
                        var cell = CellOf(Centroid(piece));
                        if (squares.ContainsKey(cell))
                            Show(owner, cell);
                        else
                            parts.Add(piece);
                    }
                    clipped = true;
                }
                var kept = new List<Corner[]>();
                var mine = new List<Candidate>();
                foreach (var whole in parts)
                {
                    // A triangle wholly over a neighbour that's a copy of what the neighbour draws there is dropped.
                    var cells = whole.Select(c => CellOf(c.World)).Distinct().ToList();
                    if (cells.Count == 1 && cells[0] is var only && _real.Contains(only) && Distance(owner, only) == 1
                        && _coverage.Measure(whole, e => e.Owner == only, copy: true).Share >= CopyShare)
                    {
                        clipped = true;
                        Show(owner, only);
                        continue;
                    }
                    var (inside, rest) = Outside(whole, Own(source));
                    if (inside is not null)
                        kept.Add(inside);
                    foreach (var part in rest)
                    {
                        foreach (var piece in Grid(part, cellSize))
                        {
                            var cell = CellOf(Centroid(piece));
                            var distance = Distance(owner, cell);
                            if (distance == 0 || (distance == 1 && _real.Contains(cell)))
                                kept.Add(piece);
                            else
                                mine.Add(new Candidate(tri.Source, tri.Draw, t, tri.World, cell, distance, piece));
                        }
                    }
                }
                if (mine.Count == 0 && !clipped)
                {
                    _coverage.Add(polygon, owner, owner, Sea(tri), Shallows(tri));
                    continue;
                }
                changed.Add((tri.Source, tri.Draw));
                keptParts[t] = kept;
                foreach (var part in kept)
                    _coverage.Add(part, owner, owner, Sea(tri), Shallows(tri));
                candidates.AddRange(mine);
            }

            // Pieces from the nearest chunks first, each weighed against what the cell draws already.
            var fills = new Dictionary<(int Row, int Column), List<Candidate>>();
            foreach (var candidate in candidates.OrderBy(c => c.Rank).ThenBy(c => sources[c.Source].Row).ThenBy(c => sources[c.Source].Column)
                .ThenBy(c => c.Source).ThenBy(c => c.Order))
                Weigh(candidate, finer: true, fills);
            // Half-alpha sea goes from a fill where opaque sea lies at its height, whoever's: laid on it, it only flickers (S20).
            foreach (var cell in fills.Keys.ToList())
            {
                fills[cell] = [.. fills[cell].SelectMany(OffTheSea)];
                if (fills[cell].Count == 0)
                    fills.Remove(cell);
            }

            // A changed draw's polygons: its triangles as they are, but those cut, as what they keep.
            var scenes = new Dictionary<int, SceneCut>();
            var byDraw = new Dictionary<(int Source, int Draw), List<Corner[]>>();
            for (var t = 0; t < triangles.Count; t++)
            {
                var tri = triangles[t];
                if (!changed.Contains((tri.Source, tri.Draw)))
                    continue;
                if (!byDraw.TryGetValue((tri.Source, tri.Draw), out var list))
                    byDraw[(tri.Source, tri.Draw)] = list = [];
                if (keptParts.TryGetValue(t, out var parts))
                    list.AddRange(parts);
                else
                    list.Add(Polygon(tri));
            }
            foreach (var ((s, d), polygons) in byDraw)
            {
                var scene = sources[s].Scene;
                if (!scenes.TryGetValue(scene, out var cut))
                    scenes[scene] = cut = new SceneCut();
                cut.Draws[d] = [.. polygons.Select(p => p.Select(c => c.Vertex).ToArray())];
            }

            var cellFills = new List<CellFill>();
            foreach (var (cell, pieces) in fills.OrderBy(f => f.Key.Row).ThenBy(f => f.Key.Column))
            {
                var origin = new Vector3(cell.Row, 0, cell.Column) * cellSize;
                var nearest = pieces.OrderBy(p => p.Rank).First();
                cellFills.Add(new CellFill(cell.Row, cell.Column,
                    [.. pieces.GroupBy(p => (p.Source, p.Draw)).Select(g => new FillPiece(sources[g.Key.Source].Scene, g.Key.Draw,
                        [.. g.Select(p => p.Polygon.Select(c => InCell(c, p.World, origin)).ToArray())]))],
                    Cell(sources[nearest.Source])));
            }
            var water = new List<Vector3[]>();
            for (var t = 0; t < triangles.Count; t++)
            {
                if (!triangles[t].Scrolls)
                    continue;
                foreach (var part in keptParts.TryGetValue(t, out var parts) ? parts : [Polygon(triangles[t])])
                    water.Add([.. part.Select(c => c.World)]);
            }
            return new ChunkCuts(scenes, cellFills,
                _shows.ToDictionary(s => s.Key, s => (IReadOnlyList<(int, int)>)[.. s.Value]), water);
        }

        /// <summary>
        /// Drops a piece where what its cell draws covers it, or moves it into the cell's fill. A piece partly covered is cut finer
        /// once, and each of its pieces weighed.
        /// </summary>
        private void Weigh(Candidate candidate, bool finer, Dictionary<(int Row, int Column), List<Candidate>> fills)
        {
            var owner = Cell(sources[candidate.Source]);
            var cell = candidate.Cell;
            var covered = _coverage.Measure(candidate.Polygon, e => Distance(e.Owner, cell) < candidate.Rank, copy: false);
            if (covered.Share >= CoveredShare)
            {
                Show(owner, covered.Sources);
                return;
            }
            // Opaque sea is no copy of half-alpha sea, which goes from the fill where they meet instead (OffTheSea).
            var sea = Sea(_triangles[candidate.Order]);
            var copied = _coverage.Measure(candidate.Polygon, e => Distance(e.Owner, cell) == candidate.Rank && !(sea && e.Shallows), copy: true);
            if (copied.Share >= CopyShare)
            {
                Show(owner, copied.Sources);
                return;
            }
            if (finer && covered.Share > UncoveredShare)
            {
                foreach (var piece in Grid(candidate.Polygon, Finer))
                    Weigh(candidate with { Polygon = piece }, finer: false, fills);
                return;
            }
            if (!fills.TryGetValue(cell, out var list))
                fills[cell] = list = [];
            list.Add(candidate);
            _coverage.Add(candidate.Polygon, owner, cell, sea, Shallows(_triangles[candidate.Order]));
            Show(owner, cell);
        }

        /// <summary>
        /// What's left of a fill's piece of half-alpha sea off the opaque sea at its height, cut finer where it's partly on it; any
        /// other piece, whole.
        /// </summary>
        private IEnumerable<Candidate> OffTheSea(Candidate candidate)
        {
            var tri = _triangles[candidate.Order];
            if (!Shallows(tri))
                return [candidate];
            var on = _coverage.Measure(candidate.Polygon, e => e.Sea, copy: true).Share;
            if (on >= CoveredShare)
                return [];
            if (on <= UncoveredShare)
                return [candidate];
            return Grid(candidate.Polygon, Finer).Select(piece => candidate with { Polygon = piece })
                .Where(piece => _coverage.Measure(piece.Polygon, e => e.Sea, copy: true).Share < CoveredShare);
        }

        /// <summary>Whether a triangle is opaque sea: opaque, and its texture scrolls.</summary>
        private static bool Sea(Triangle t) => t.Scrolls && t.Opaque;

        /// <summary>Whether a triangle is half-alpha sea, the shallows' water: its texture scrolls, and it isn't opaque.</summary>
        private static bool Shallows(Triangle t) => t.Scrolls && !t.Opaque;

        private void Show((int Row, int Column) owner, (int Row, int Column) cell) => Show(owner, [cell]);

        private void Show((int Row, int Column) owner, IEnumerable<(int Row, int Column)> cells)
        {
            foreach (var cell in cells)
            {
                if (cell == owner)
                    continue;
                if (!_shows.TryGetValue(owner, out var set))
                    _shows[owner] = set = [];
                set.Add(cell);
            }
        }

        /// <summary>
        /// Whether a triangle is sea over a square of it: its texture scrolls, it's flat, at the square's height, and it reaches into
        /// the square's cell.
        /// </summary>
        private bool OverSquare(Triangle t)
        {
            if (!t.Scrolls || squares.Count == 0)
                return false;
            var low = Math.Min(t.A.World.Y, Math.Min(t.B.World.Y, t.C.World.Y));
            var high = Math.Max(t.A.World.Y, Math.Max(t.B.World.Y, t.C.World.Y));
            if (high - low > SeaHeight)
                return false;
            var x0 = Math.Min(t.A.World.X, Math.Min(t.B.World.X, t.C.World.X));
            var x1 = Math.Max(t.A.World.X, Math.Max(t.B.World.X, t.C.World.X));
            var z0 = Math.Min(t.A.World.Z, Math.Min(t.B.World.Z, t.C.World.Z));
            var z1 = Math.Max(t.A.World.Z, Math.Max(t.B.World.Z, t.C.World.Z));
            for (var r = (int)MathF.Floor(x0 / cellSize); r * cellSize < x1; r++)
            {
                for (var c = (int)MathF.Floor(z0 / cellSize); c * cellSize < z1; c++)
                {
                    if (squares.TryGetValue((r, c), out var height) && Math.Abs((low + high) / 2 - height) <= SeaHeight)
                        return true;
                }
            }
            return false;
        }

        private (float X0, float Z0, float X1, float Z1) Own(Source source) =>
            (source.Row * cellSize - Border, source.Column * cellSize - Border, (source.Row + 1) * cellSize + Border, (source.Column + 1) * cellSize + Border);

        private static bool InBox(Triangle t, (float X0, float Z0, float X1, float Z1) box) =>
            In(t.A.World, box) && In(t.B.World, box) && In(t.C.World, box);

        private static bool In(Vector3 p, (float X0, float Z0, float X1, float Z1) box) =>
            p.X >= box.X0 && p.X <= box.X1 && p.Z >= box.Z0 && p.Z <= box.Z1;

        private (int Row, int Column) CellOf(Vector3 p) => ((int)MathF.Floor(p.X / cellSize), (int)MathF.Floor(p.Z / cellSize));

        private static (int Row, int Column) Cell(Source source) => (source.Row, source.Column);

        private static Corner[] Polygon(Triangle t) => [t.A, t.B, t.C];

        /// <summary>A polygon's corner in a fill: in the fill's chunk's coordinates, its normal turned as its node turns it.</summary>
        private static CutVertex InCell(Corner c, Matrix4x4 world, Vector3 origin) => c.Vertex with
        {
            Position = c.World - origin,
            Normal = Vector3.TransformNormal(c.Vertex.Normal, world),
        };
    }

    /// <summary>How many cells apart two are, across or diagonally.</summary>
    internal static int Distance((int Row, int Column) a, (int Row, int Column) b) => Math.Max(Math.Abs(a.Row - b.Row), Math.Abs(a.Column - b.Column));

    internal static Vector3 Centroid(IReadOnlyList<Corner> polygon)
    {
        var sum = Vector3.Zero;
        foreach (var c in polygon)
            sum += c.World;
        return sum / polygon.Count;
    }

    /// <summary>A polygon cut by a box across the ground: the part inside it, or null, and the parts outside it.</summary>
    internal static (Corner[]? Inside, List<Corner[]> Outside) Outside(Corner[] polygon, (float X0, float Z0, float X1, float Z1) box)
    {
        var outside = new List<Corner[]>();
        Corner[]? rest = polygon;
        foreach (var (axis, value, keepAbove) in new[] { (0, box.X0, true), (0, box.X1, false), (2, box.Z0, true), (2, box.Z1, false) })
        {
            if (rest is null)
                break;
            var (below, above) = Split(rest, axis, value);
            var (gone, kept) = keepAbove ? (below, above) : (above, below);
            if (gone is not null)
                outside.Add(gone);
            rest = kept;
        }
        return (rest, outside);
    }

    /// <summary>A polygon cut along every line of a grid of <paramref name="step"/> across the ground that crosses it.</summary>
    internal static List<Corner[]> Grid(Corner[] polygon, float step)
    {
        var pieces = new List<Corner[]> { polygon };
        foreach (var axis in new[] { 0, 2 })
        {
            var next = new List<Corner[]>();
            foreach (var piece in pieces)
            {
                var low = piece.Min(c => Get(c.World, axis));
                var high = piece.Max(c => Get(c.World, axis));
                Corner[]? rest = piece;
                for (var k = (int)MathF.Floor(low / step) + 1; k * step < high && rest is not null; k++)
                {
                    var (below, above) = Split(rest, axis, k * step);
                    if (below is not null)
                        next.Add(below);
                    rest = above;
                }
                if (rest is not null)
                    next.Add(rest);
            }
            pieces = next;
        }
        return pieces;
    }

    /// <summary>
    /// A convex polygon cut by the line where <paramref name="axis"/> (0 for X, 2 for Z) is <paramref name="value"/>: the part below
    /// and the part above, either null where there's none. A corner made on the line is on it exactly, and is made the same from
    /// either side's triangle, so the two halves of a shared edge meet.
    /// </summary>
    internal static (Corner[]? Below, Corner[]? Above) Split(Corner[] polygon, int axis, float value)
    {
        var below = new List<Corner>();
        var above = new List<Corner>();
        for (var i = 0; i < polygon.Length; i++)
        {
            var p = polygon[i];
            var q = polygon[(i + 1) % polygon.Length];
            var a = Get(p.World, axis);
            var b = Get(q.World, axis);
            if (a <= value)
                below.Add(p);
            if (a >= value)
                above.Add(p);
            if ((a < value && b > value) || (a > value && b < value))
            {
                var cross = Cross(p, q, axis, value);
                below.Add(cross);
                above.Add(cross);
            }
        }
        return (Solid(below), Solid(above));
    }

    private static Corner[]? Solid(List<Corner> polygon)
    {
        if (polygon.Count < 3)
            return null;
        // Across the ground, or up a wall: what's left after a cut along a corner has no area either way.
        var area = Vector3.Zero;
        for (var i = 1; i + 1 < polygon.Count; i++)
            area += Vector3.Cross(polygon[i].World - polygon[0].World, polygon[i + 1].World - polygon[0].World);
        return area.LengthSquared() < 1e-8f ? null : [.. polygon];
    }

    private static Corner Cross(Corner p, Corner q, int axis, float value)
    {
        // From the lesser end, whichever way round the edge is walked.
        if (Less(q.World, p.World))
            (p, q) = (q, p);
        var t = (value - Get(p.World, axis)) / (Get(q.World, axis) - Get(p.World, axis));
        var world = Vector3.Lerp(p.World, q.World, t);
        if (axis == 0)
            world.X = value;
        else
            world.Z = value;
        return new Corner(world, CutVertex.Lerp(p.Vertex, q.Vertex, t));
    }

    private static bool Less(Vector3 a, Vector3 b) => a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

    private static float Get(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>
    /// What's drawn over each point of a grid across the ground, every <see cref="Spacing"/>: each surface's height there, the chunk
    /// whose scenes it came from, and the chunk that draws it now, which is a fill's for a piece moved into one.
    /// </summary>
    internal sealed class Coverage(float cellSize)
    {
        internal readonly record struct Entry(float Height, (int Row, int Column) Owner, (int Row, int Column) Drawn, bool Sea, bool Shallows, int Next);

        private readonly Dictionary<(int Row, int Column), int[]> _heads = [];
        private readonly List<Entry> _entries = [];
        private readonly int _side = (int)MathF.Ceiling(cellSize / Spacing);

        /// <summary>Adds what a polygon covers, and whether it's opaque sea or half-alpha.</summary>
        public void Add(IReadOnlyList<Corner> polygon, (int Row, int Column) owner, (int Row, int Column) drawn, bool sea = false, bool shallows = false) =>
            Raster(polygon, (cell, index, height) =>
            {
                if (!_heads.TryGetValue(cell, out var heads))
                {
                    _heads[cell] = heads = new int[_side * _side];
                    Array.Fill(heads, -1);
                }
                _entries.Add(new Entry(height, owner, drawn, sea, shallows, heads[index]));
                heads[index] = _entries.Count - 1;
            });

        /// <summary>
        /// The share of a polygon's points that something <paramref name="counts"/> covers, at any height or, for
        /// <paramref name="copy"/>, at the polygon's own, and the chunks that draw what covers them.
        /// </summary>
        public (float Share, HashSet<(int Row, int Column)> Sources) Measure(IReadOnlyList<Corner> polygon, Func<Entry, bool> counts, bool copy)
        {
            var sources = new HashSet<(int Row, int Column)>();
            int points = 0, hits = 0;
            Raster(polygon, (cell, index, height) =>
            {
                points++;
                if (!_heads.TryGetValue(cell, out var heads))
                    return;
                var hit = false;
                for (var e = heads[index]; e >= 0; e = _entries[e].Next)
                {
                    var entry = _entries[e];
                    if (!counts(entry) || (copy && MathF.Abs(entry.Height - height) >= CopyHeight))
                        continue;
                    hit = true;
                    sources.Add(entry.Drawn);
                }
                if (hit)
                    hits++;
            });
            return (points == 0 ? 0 : hits / (float)points, sources);
        }

        /// <summary>
        /// Each point of the grid a polygon covers, with its height there, by the cell it's in and its place there. A polygon too small
        /// or too steep to cover any is taken at the point nearest its middle.
        /// </summary>
        private void Raster(IReadOnlyList<Corner> polygon, Action<(int Row, int Column), int, float> point)
        {
            var any = false;
            for (var i = 1; i + 1 < polygon.Count; i++)
                any |= Triangle(polygon[0].World, polygon[i].World, polygon[i + 1].World, point);
            if (any)
                return;
            var middle = Centroid(polygon);
            Point((int)MathF.Floor(middle.X / Spacing), (int)MathF.Floor(middle.Z / Spacing), middle.Y, point);
        }

        private bool Triangle(Vector3 a, Vector3 b, Vector3 c, Action<(int Row, int Column), int, float> point)
        {
            var det = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
            if (MathF.Abs(det) < 1e-6f)
                return false;
            var x0 = (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)) / Spacing);
            var x1 = (int)MathF.Floor(MathF.Max(a.X, MathF.Max(b.X, c.X)) / Spacing);
            var z0 = (int)MathF.Floor(MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) / Spacing);
            var z1 = (int)MathF.Floor(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) / Spacing);
            var any = false;
            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    float px = (x + 0.5f) * Spacing, pz = (z + 0.5f) * Spacing;
                    var u = ((b.X - px) * (c.Z - pz) - (c.X - px) * (b.Z - pz)) / det;
                    var v = ((c.X - px) * (a.Z - pz) - (a.X - px) * (c.Z - pz)) / det;
                    var w = 1 - u - v;
                    // Half-open, so a point on an edge two triangles share counts once.
                    if (u < 0 || v < 0 || w <= 0)
                        continue;
                    Point(x, z, u * a.Y + v * b.Y + w * c.Y, point);
                    any = true;
                }
            }
            return any;
        }

        private void Point(int x, int z, float height, Action<(int Row, int Column), int, float> point)
        {
            var row = (int)MathF.Floor(x / (float)_side);
            var column = (int)MathF.Floor(z / (float)_side);
            point((row, column), (z - column * _side) * _side + (x - row * _side), height);
        }
    }
}
