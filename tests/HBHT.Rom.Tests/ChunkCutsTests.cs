using System.Numerics;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using HBHT.Rom.Graphics;
using static HBHT.Rom.Graphics.ChunkCuts;

namespace HBHT.Rom.Tests;

/// <summary>Taking out of chunk scenes what they draw over other chunks' cells (<see cref="ChunkCuts"/>), on hand-made chunks.</summary>
public class ChunkCutsTests
{
    private const float Cell = 100;

    private static Source Chunk(int row, int column, int scene) => new(row, column, scene, new Vector3(row, 0, column) * Cell);

    private static Corner At(Vector3 world, Source source) =>
        new(world, new CutVertex(world - source.Origin, Vector3.UnitY, Vector4.One, new Vector2(world.X, world.Z) * 64, false));

    /// <summary>A flat square from (x0, z0) to (x1, z1) at <paramref name="y"/>, in the world, as two triangles of draw 0.</summary>
    private static IEnumerable<Triangle> Square(int source, Source of, float x0, float z0, float x1, float z1, float y = 0, bool sea = false,
        int draw = 0, bool opaque = true)
    {
        Corner P(float x, float z) => At(new Vector3(x, y, z), of);
        yield return new Triangle(source, draw, Matrix4x4.Identity, P(x0, z0), P(x1, z0), P(x1, z1), sea, opaque);
        yield return new Triangle(source, draw, Matrix4x4.Identity, P(x0, z0), P(x1, z1), P(x0, z1), sea, opaque);
    }

    private static ChunkCuts Cut((int Row, int Column)[] cells, Source[] sources, IEnumerable<Triangle> triangles) =>
        ChunkCuts.Cut([.. cells], sources, Cell, [.. triangles]);

    private static float Area(IEnumerable<CutVertex[]> polygons) => polygons.Sum(p =>
    {
        var area = 0f;
        for (var i = 1; i + 1 < p.Length; i++)
            area += Vector3.Cross(p[i].Position - p[0].Position, p[i + 1].Position - p[0].Position).Length() / 2;
        return area;
    });

    [Fact]
    public void WhatsWithinTheBorderOrOverANeighbourStays()
    {
        // Chunk (1, 1)'s ground, 16 past its edge, and a cliff reaching 60 into its neighbour (2, 1), which draws its own ground.
        var a = Chunk(1, 1, 1);
        var b = Chunk(2, 1, 2);
        var cuts = Cut([(1, 1), (2, 1)], [a, b],
            [.. Square(0, a, 84, 100, 216, 200), .. Square(0, a, 200, 120, 260, 140, y: 5), .. Square(1, b, 200, 100, 300, 200)]);
        Assert.Empty(cuts.Scenes);
        Assert.Empty(cuts.Fills);
        Assert.Empty(cuts.Shows);
    }

    [Fact]
    public void ACopyOfANeighbourIsDroppedForTheNeighbour()
    {
        var a = Chunk(1, 1, 1);
        var b = Chunk(2, 1, 2);
        var cuts = Cut([(1, 1), (2, 1)], [a, b],
            [.. Square(0, a, 100, 100, 200, 200), .. Square(0, a, 220, 120, 280, 180), .. Square(1, b, 200, 100, 300, 200)]);
        // The copy is gone and the rest of the draw stays as it was.
        Assert.Equal(10000, Area(cuts.Scenes[1].Draws[0]), 0.01f);
        Assert.Equal(new[] { (2, 1) }, cuts.Shows[(1, 1)]);
        Assert.Empty(cuts.Fills);
    }

    [Fact]
    public void ASheetTwoCellsOutIsDroppedOverAChunkAndMovedIntoAnEmptyCellsFill()
    {
        // Chunk (1, 1)'s sheet runs from its own cell across (2, 1), which has a chunk of its own one cell away, into (3, 1), which
        // has (2, 1)'s ground over half of it, and on into (4, 1), which has nothing.
        var a = Chunk(1, 1, 1);
        var b = Chunk(2, 1, 2);
        var cuts = Cut([(1, 1), (2, 1), (3, 1)], [a, b, Chunk(3, 1, 3)],
            [.. Square(0, a, 100, 100, 500, 200, y: -32), .. Square(1, b, 200, 100, 350, 200)]);
        // Kept: its own cell, 16 past it, and over its neighbour.
        Assert.Equal(200 * 100, Area(cuts.Scenes[1].Draws[0]), 0.1f);
        // Over (3, 1): dropped where (2, 1)'s ground lies, and moved into (3, 1)'s fill where it doesn't; over (4, 1), moved.
        var fills = cuts.Fills.ToDictionary(f => (f.Row, f.Column));
        Assert.Equal(new[] { (3, 1), (4, 1) }, fills.Keys.Order());
        Assert.Equal(50 * 100, Area(fills[(3, 1)].Pieces.SelectMany(p => p.Polygons)), 0.1f);
        Assert.Equal(100 * 100, Area(fills[(4, 1)].Pieces.SelectMany(p => p.Polygons)), 0.1f);
        Assert.Equal((1, 1), fills[(4, 1)].Nearest);
        // In the fill's own coordinates, cut at its edges exactly.
        var corners = fills[(4, 1)].Pieces.SelectMany(p => p.Polygons).SelectMany(p => p).ToList();
        Assert.Equal(0, corners.Min(c => c.Position.X));
        Assert.Equal(100, corners.Max(c => c.Position.X));
        Assert.All(corners, c => Assert.Equal(-32, c.Position.Y));
        Assert.Equal(new[] { (2, 1), (3, 1), (4, 1) }, cuts.Shows[(1, 1)]);
    }

    [Fact]
    public void OfTwoSheetsOverAnEmptyCellTheNearerChunksIsKept()
    {
        // (1, 1) and (5, 1) both lay sea over (3, 1) at the same height; (4, 1)'s is nearer, over half of it, higher up, past
        // its own border.
        var a = Chunk(1, 1, 1);
        var b = Chunk(5, 1, 2);
        var c = Chunk(4, 1, 3);
        var cuts = Cut([(1, 1), (5, 1), (4, 1)], [a, b, c],
            [.. Square(0, a, 300, 100, 400, 200, y: -32), .. Square(1, b, 300, 100, 400, 200, y: -32), .. Square(2, c, 330, 100, 380, 200, y: -20)]);
        var fill = Assert.Single(cuts.Fills);
        Assert.Equal((3, 1), (fill.Row, fill.Column));
        Assert.Equal((4, 1), fill.Nearest);
        // (4, 1)'s half, and of the two at the same height, the first by row and column round it.
        Assert.Equal(new[] { 3, 1 }, fill.Pieces.Select(p => p.Scene));
        Assert.Equal(50 * 100, Area(fill.Pieces[0].Polygons), 0.1f);
        Assert.Equal(50 * 100, Area(fill.Pieces[1].Polygons), 0.1f);
        Assert.Equal(new[] { (3, 1) }, cuts.Shows[(5, 1)]);
    }

    [Fact]
    public void ACellOfOpenSeaIsASquareOfItAndTheSeaAroundItIsCutAtItsEdge()
    {
        // Chunk (1, 1)'s sea runs from its own cell over (2, 1) and (3, 1), which have no chunk.
        var a = Chunk(1, 1, 1);
        var cuts = Cut([(1, 1)], [a], Square(0, a, 100, 100, 400, 200, y: -32, sea: true));
        // Its own cell alone: not the 16 past it, over the square.
        Assert.Equal(100 * 100, Area(cuts.Scenes[1].Draws[0]), 0.1f);
        var fills = cuts.Fills.ToDictionary(f => (f.Row, f.Column));
        Assert.Equal(new[] { (2, 1), (3, 1) }, fills.Keys.Order());
        foreach (var fill in fills.Values)
        {
            Assert.Equal(-32, fill.Sea);
            var piece = Assert.Single(fill.Pieces);
            Assert.Equal((1, 0), (piece.Scene, piece.Draw));
            Assert.Equal(100 * 100, Area(piece.Polygons), 0.1f);
            // On the grid the fog is worked out at, its last points on the cell's edge exactly, flat where its neighbour's sea is too.
            Assert.Equal(100, piece.Polygons.Count);
            Assert.All(piece.Polygons.SelectMany(p => p), v => Assert.Equal(-32, v.Position.Y));
            Assert.Equal([0f, 100f], piece.Polygons.SelectMany(p => p).Select(v => v.Position.X).Where(x => x is 0 or 100).Distinct().Order());
            // Its texture laid on the world as the sea's is, 64 units a unit here, but for whole turns of the widest texture.
            Assert.All(piece.Polygons.SelectMany(p => p), v =>
            {
                var off = new Vector2(fill.Row * Cell + v.Position.X, fill.Column * Cell + v.Position.Z) * 64 - v.St;
                Assert.Equal(0, off.X % 16384, 0.01f);
                Assert.Equal(0, off.Y % 16384, 0.01f);
            });
        }
        Assert.Equal(new[] { (2, 1), (3, 1) }, cuts.Shows[(1, 1)]);
    }

    [Fact]
    public void SquaresSideBySideLieAtOneHeight()
    {
        // (1, 1)'s sea over (2, 1) at −32, and (4, 1)'s over (3, 1) a tenth higher: one sea, at the mean of its squares. (6, 1)'s over (7, 1),
        // with (5, 1) between, lies apart.
        var (a, b, c) = (Chunk(1, 1, 1), Chunk(4, 1, 2), Chunk(6, 1, 3));
        var cuts = Cut([(1, 1), (4, 1), (6, 1)], [a, b, c],
            [.. Square(0, a, 100, 100, 300, 200, y: -32, sea: true), .. Square(1, b, 300, 100, 500, 200, y: -31.9f, sea: true),
             .. Square(2, c, 600, 100, 800, 200, y: -29.4f, sea: true)]);
        var fills = cuts.Fills.ToDictionary(f => (f.Row, f.Column));
        Assert.Equal(fills[(2, 1)].Sea, fills[(3, 1)].Sea);
        Assert.InRange(fills[(2, 1)].Sea!.Value, -32f, -31.9f);
        Assert.Equal(-29.4f, fills[(7, 1)].Sea!.Value, 0.001f);
    }

    [Fact]
    public void ASquaresEdgeMeetsItsNeighboursWaterPointForPoint()
    {
        // (1, 1)'s own water rises from −31.9 at its edge's ends to −31.7 at z 155, a point off the square's grid; its sea at −32 runs on
        // over (2, 1) and (3, 1), which become squares.
        var a = Chunk(1, 1, 1);
        Corner P(float x, float y, float z) => At(new Vector3(x, y, z), a);
        var (c0, c1, mid, c2, c3) = (P(100, -31.9f, 100), P(200, -31.9f, 100), P(200, -31.7f, 155), P(200, -31.9f, 200), P(100, -31.9f, 200));
        var cuts = Cut([(1, 1)], [a],
        [
            new Triangle(0, 1, Matrix4x4.Identity, c0, c1, mid, true, true),
            new Triangle(0, 1, Matrix4x4.Identity, c0, mid, c3, true, true),
            new Triangle(0, 1, Matrix4x4.Identity, c3, mid, c2, true, true),
            .. Square(0, a, 216, 100, 400, 200, y: -32, sea: true),
        ]);
        var square = cuts.Fills.Single(f => (f.Row, f.Column) == (2, 1));
        Assert.Equal(-32, square.Sea);
        var corners = square.Pieces.SelectMany(p => p.Polygons).SelectMany(p => p).ToList();
        // On the shared edge, (1, 1)'s points at its heights: z 55 put in, and between them along the line.
        Assert.Contains(corners, v => v.Position == new Vector3(0, -31.7f, 55));
        Assert.All(corners.Where(v => v.Position.X == 0), v => Assert.Equal(
            v.Position.Z <= 55 ? -31.9f + 0.2f * v.Position.Z / 55 : -31.7f - 0.2f * (v.Position.Z - 55) / 45, v.Position.Y, 0.001f));
        // Eased back to the square's own height inside, and there from the ease's reach on.
        Assert.All(corners.Where(v => v.Position.X == 10), v => Assert.InRange(v.Position.Y, -31.99f, -31.7f));
        Assert.All(corners.Where(v => v.Position.X >= SeaRims.Ease), v => Assert.Equal(-32, v.Position.Y, 0.0001f));
        // The polygon the point went into is fanned from a corner off the edge, so the fan follows it.
        var split = square.Pieces.SelectMany(p => p.Polygons).Single(p => p.Any(v => v.Position == new Vector3(0, -31.7f, 55)));
        Assert.NotEqual(0, split[0].Position.X);
        // The next square, beside it, is flat.
        Assert.All(cuts.Fills.Single(f => (f.Row, f.Column) == (3, 1)).Pieces.SelectMany(p => p.Polygons).SelectMany(p => p),
            v => Assert.Equal(-32, v.Position.Y));
    }

    [Fact]
    public void ACellWithMoreThanSeaOverItKeepsItsFill()
    {
        // Over (3, 1), besides the sea, a sheet of land.
        var a = Chunk(1, 1, 1);
        var cuts = Cut([(1, 1)], [a], [.. Square(0, a, 100, 100, 400, 200, y: -32, sea: true), .. Square(0, a, 330, 130, 370, 170, y: -20, draw: 1)]);
        var fills = cuts.Fills.ToDictionary(f => (f.Row, f.Column));
        Assert.Equal(-32, fills[(2, 1)].Sea);
        Assert.Null(fills[(3, 1)].Sea);
        Assert.Equal(new[] { 0, 1 }, fills[(3, 1)].Pieces.Select(p => p.Draw).Order());
    }

    [Fact]
    public void HalfAlphaSeaInAFillGoesWhereOpaqueSeaLiesAtItsHeight()
    {
        // Over (3, 1), shallows: half-alpha sea over a seabed. The half-alpha sea runs on over the open sea, as R20's does over S20.
        var a = Chunk(1, 1, 1);
        var cuts = Cut([(1, 1)], [a],
        [
            .. Square(0, a, 300, 100, 400, 200, y: -32, sea: true, draw: 0, opaque: false),
            .. Square(0, a, 300, 100, 350, 200, y: -35, draw: 1),
            .. Square(0, a, 350, 100, 400, 200, y: -32, sea: true, draw: 2),
        ]);
        var fill = Assert.Single(cuts.Fills);
        Assert.Null(fill.Sea);
        var areas = fill.Pieces.ToDictionary(p => p.Draw, p => Area(p.Polygons));
        Assert.Equal(50 * 100, areas[0], 0.1f);
        Assert.True(fill.Pieces.Single(p => p.Draw == 0).Polygons.SelectMany(p => p).All(v => v.Position.X <= 50.01f));
        Assert.Equal(50 * 100, areas[1], 0.1f);
        Assert.Equal(50 * 100, areas[2], 0.1f);
    }

    [Fact]
    public void BothHalvesOfASharedEdgeAreCutAtTheSamePoint()
    {
        var source = Chunk(0, 0, 1);
        var p = At(new Vector3(0, 0, 0), source);
        var q = At(new Vector3(30, 3, 10), source);
        var left = Split([p, q, At(new Vector3(0, 0, 10), source)], 0, 12.5f);
        var right = Split([q, p, At(new Vector3(30, 0, 0), source)], 0, 12.5f);
        var a = left.Below!.Concat(left.Above!).Where(c => c.World.X == 12.5f).Select(c => c.World).ToHashSet();
        var b = right.Below!.Concat(right.Above!).Where(c => c.World.X == 12.5f).Select(c => c.World).ToHashSet();
        Assert.Contains(a, b.Contains);
    }

    [Fact]
    public void AGridCutKeepsThePolygonsArea()
    {
        var source = Chunk(0, 0, 1);
        Corner[] triangle = [At(new Vector3(-13, 0, 4), source), At(new Vector3(87, 2, -21), source), At(new Vector3(35, 5, 66), source)];
        var pieces = Grid(triangle, 10);
        Assert.True(pieces.Count > 20);
        static float Of(IEnumerable<Corner[]> polygons) => polygons.Sum(p =>
        {
            var area = 0f;
            for (var i = 1; i + 1 < p.Length; i++)
                area += Vector3.Cross(p[i].World - p[0].World, p[i + 1].World - p[0].World).Length() / 2;
            return area;
        });
        Assert.Equal(Of([triangle]), Of(pieces), 0.05f);
        // Each piece in one square of the grid.
        Assert.All(pieces, piece =>
        {
            var middle = Centroid(piece);
            var (x, z) = (MathF.Floor(middle.X / 10) * 10, MathF.Floor(middle.Z / 10) * 10);
            Assert.All(piece, c => Assert.True(c.World.X >= x - 1e-4f && c.World.X <= x + 10 + 1e-4f && c.World.Z >= z - 1e-4f && c.World.Z <= z + 10 + 1e-4f));
        });
    }
}

/// <summary>What <see cref="ChunkCuts"/> made of NA 1.0's Overworld, in the base plugin.</summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class ChunkCutsImportTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    private MapSetRecord Overworld => Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0));

    private IReadOnlyList<ChunkRecord> OverworldChunks => [.. Data.Set.ChunksIn(Overworld.Id)];

    private ChunkRecord Chunk(string name) => OverworldChunks.Single(c => c.Name == name);

    [Fact]
    public void NoChunkSceneDrawsPastItsNeighbours()
    {
        var set = Overworld;
        float Reach(RecordId scene) => SceneMesh.For(Data.Set, scene).SelectMany(t => new[] { t.A, t.B, t.C })
            .Select(v => MathF.Max(MathF.Max(-v.X, v.X - set.CellSize), MathF.Max(-v.Z, v.Z - set.CellSize))).DefaultIfEmpty(0).Max();
        // The islands' scenes are left whole, as 1.0 draws them: Namer's Isle's sea reaches three cells past A22.
        Assert.True(Reach(Chunk("A22").AltScene!.Value) > 2 * set.CellSize);
        foreach (var chunk in OverworldChunks.Where(c => c.Extended))
        {
            foreach (var scene in new[] { chunk.Scene, chunk.AltScene }.OfType<RecordId>())
            {
                var reach = Reach(scene);
                // To a hair, where a triangle reaches by a sliver too thin to cut off.
                Assert.True(reach <= set.CellSize + 0.05f, $"{chunk.Name}'s {scene} draws {reach} past its cell.");
            }
        }
    }

    [Fact]
    public void AFillLiesInItsCell()
    {
        var set = Overworld;
        var fills = OverworldChunks.Where(c => c.Fill is not null).ToList();
        Assert.InRange(fills.Count, 80, 100);
        Assert.Equal(fills.Count, fills.Select(c => c.Fill).Distinct().Count());
        foreach (var chunk in fills)
        {
            var triangles = SceneMesh.For(Data.Set, chunk.Fill!.Value);
            Assert.NotEmpty(triangles);
            Assert.All(triangles.SelectMany(t => new[] { t.A, t.B, t.C }), v =>
                Assert.True(v.X is >= -0.01f and <= 100.01f && v.Z is >= -0.01f and <= 100.01f, $"{chunk.Name}'s fill draws at {v}."));
        }
    }

    [Fact]
    public void ACopyOfAChunkIsDroppedForTheChunk()
    {
        // H08 carries I09, which its byte 0x1A leaves unloaded; S18 is T17's whole scene; M20 carries all of J19.
        Assert.Contains(Ids.Chunk(0, "I09"), Chunk("H08").Shows!);
        Assert.Contains(Ids.Chunk(0, "T17"), Chunk("S18").Shows!);
        Assert.Contains(Ids.Chunk(0, "J19"), Chunk("M20").Shows!);
        Assert.Contains(Ids.Chunk(0, "K20"), Chunk("M21").Shows!);
        Assert.Empty(SceneMesh.For(Data.Set, Chunk("S18").Scene!.Value));
    }

    [Fact]
    public void WhatsOverACellWithNoChunkIsAChunkOfItsOwn()
    {
        var set = Overworld;
        var made = OverworldChunks.Where(c => c.Scene is null && c.AltScene is null && c.Fill is not null).ToList();
        Assert.InRange(made.Count, 75, 100);
        // On the original's own map for its chunks of sea, which has no objects.
        Assert.All(made, c => Assert.Equal("0x3559", c.Borg));
        Assert.All(made, c => Assert.Empty(c.Objects));
        // The sea past Lighthouse Point, two rows off the map's top, and J20, which only M20 and its neighbours drew.
        Assert.Contains(made, c => c.Row == -1 && c.Column == 15);
        Assert.Contains(made, c => c.Name == "J20");
        // A real chunk with nothing in its own cell takes the sea another drew over it; C07 takes C09's peaks past its own.
        Assert.NotNull(Chunk("L23").Fill);
        Assert.NotNull(Chunk("C07").Fill);
    }

    [Fact]
    public void OpenSeaIsASquareOfIt()
    {
        // A fill that's a square: 200 triangles and those of the points its edges took from the water beside them, scrolling, within
        // a unit of its height (H20's water stands 0.77 above the squares beside it).
        bool IsSquare(ChunkRecord c, float height)
        {
            var triangles = SceneMesh.For(Data.Set, c.Fill!.Value);
            return triangles.Count is >= 200 and < 260 && triangles.All(t => t.Scrolls)
                && triangles.SelectMany(t => new[] { t.A, t.B, t.C }).All(v => MathF.Abs(v.Y - height) < 1);
        }
        var squares = OverworldChunks.Where(c => c.Fill is not null && (IsSquare(c, -32) || IsSquare(c, -29.4f))).ToList();
        Assert.InRange(squares.Count, 55, 75);
        // None round the islands, whose art is left whole.
        Assert.DoesNotContain(squares, c => c.Name is "0_22" or "A21" or "C22" or "E21" or "G23");
        // Past Lighthouse Point, round Terminor, and Gwernia's bay, which lies higher than the rest.
        Assert.Contains(squares, c => c.Row == -1 && c.Column == 15);
        Assert.True(IsSquare(Chunk("F19"), -32));
        Assert.True(IsSquare(Chunk("N09"), -29.4f));
        // Round Terminor at one height, where their fills lay from −31.917 (G20) to −32.020 (H21), and a step between two opened a
        // crack with the sky behind it.
        float Top(string name) => SceneMesh.For(Data.Set, Chunk(name).Fill!.Value).SelectMany(t => new[] { t.A, t.B, t.C })
            .MinBy(v => Vector2.Distance(new Vector2(v.X, v.Z), new Vector2(50, 50))).Y;
        Assert.Single(new[] { "E16", "F19", "G20", "G21", "H21", "H22", "J21", "K22", "L24" }.Select(Top).Distinct());
        // The shallows off the south coast keep the sea and the seabed under it.
        Assert.DoesNotContain(squares, c => c.Name == "S21");
    }

    [Fact]
    public void NamersIslesSeaSheetsAreWeldedAllAlongWhereTheyMeet()
    {
        // A22's sea runs east to x 200, where B22's starts, 700 units north to south, most of it far past both cells: 0.009 apart in
        // the ROM, and every point of each side's edge now one of the other's.
        IEnumerable<Vector3> Edge(string name, float localX) => SceneMesh.For(Data.Set, Chunk(name).AltScene!.Value)
            .Where(t => t.Scrolls).SelectMany(t => new[] { t.A, t.B, t.C }).Where(v => MathF.Abs(v.X - localX) < 0.05f)
            .Select(v => v with { X = v.X - localX }).Distinct();
        // In B22's coordinates, A22's are 100 further west, so its edge's points are where B22's are, x and all.
        var (west, east) = (Edge("A22", 100).ToList(), Edge("B22", 0).ToList());
        Assert.InRange(west.Max(v => v.Z) - west.Min(v => v.Z), 690, 710);
        Assert.All(west, v => Assert.Contains(east, e => Vector3.Distance(e, v) < 0.001f));
    }

    [Fact]
    public void NamersIsleHasAFloorOfSeaUnderItsWater()
    {
        // With the seams joined, a sheet of the island's opaque sea lies under all of it, below the deepest seabed, so the sky doesn't
        // show under the shallows' edge, where the seabed stops short of the opaque sea and nothing lay under that.
        foreach (var name in new[] { "A22", "B22" })
        {
            var chunk = Chunk(name);
            var water = SceneMesh.For(Data.Set, chunk.AltScene!.Value).Where(t => t.Scrolls && t.Blend == SurfaceBlend.Opaque).ToList();
            var seabed = SceneMesh.For(Data.Set, chunk.Scene!.Value).Where(t => !t.Scrolls).SelectMany(t => new[] { t.A, t.B, t.C })
                .Where(v => v.Y is < -32 and > -37).Min(v => v.Y);
            var floor = water.Where(t => t.A.Y < seabed && t.B.Y < seabed && t.C.Y < seabed).ToList();
            Assert.NotEmpty(floor);
            var sheet = water.Except(floor).SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
            var under = floor.SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
            // Across the whole sheet.
            Assert.Equal(sheet.Min(v => v.X), under.Min(v => v.X), 0.01f);
            Assert.Equal(sheet.Max(v => v.Z), under.Max(v => v.Z), 0.01f);
        }
    }

    [Fact]
    public void TheShallowsStopWhereTheOpenSeaStarts()
    {
        // Half-alpha water runs right across S20, over the open sea from its middle out at the same height; what lay on the sea goes.
        var triangles = SceneMesh.For(Data.Set, Chunk("S20").Fill!.Value);
        // Not counting the original's slivers, a ten-thousandth of a unit across.
        var shallows = triangles.Where(t => t.Scrolls && t.Blend == SurfaceBlend.Translucent && Vector3.Cross(t.B - t.A, t.C - t.A).Length() > 0.1f)
            .SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
        var sea = triangles.Where(t => t.Scrolls && t.Blend == SurfaceBlend.Opaque).SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
        Assert.NotEmpty(shallows);
        Assert.InRange(sea.Min(v => v.X), 50, 60);
        Assert.True(shallows.Max(v => v.X) <= sea.Min(v => v.X) + 10);
    }

    [Fact]
    public void ExtendedDrawsFurtherOnTheMainlandAlone()
    {
        var off = OverworldChunks.Where(c => c.Scene is not null && !c.Extended).Select(c => c.Name).Order();
        // Namer's Isle and Chaos Isle.
        Assert.Equal(["A22", "B22", "D22", "D23", "E22", "E23", "F23"], off);
        Assert.All(Data.MapSets.Where(s => s.Id != Overworld.Id).SelectMany(s => Data.Set.ChunksIn(s.Id)), c => Assert.False(c.Extended));
    }
}
