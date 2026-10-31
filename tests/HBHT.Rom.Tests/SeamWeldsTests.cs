using System.Numerics;
using HBHT.Plugins;
using HBHT.Rom.Borg;
using HBHT.Rom.Graphics;
using HBHT.Rom.Maps;
using static HBHT.Rom.Graphics.SeamWelds;

namespace HBHT.Rom.Tests;

/// <summary>Welding the seams between meshes (<see cref="SeamWelds"/>), on hand-made pieces and on the Overworld's K08.</summary>
public class SeamWeldsTests
{
    private const float Cell = 100;

    private static Piece Chunk(int scene, int row, int column, bool alt = false, bool fixedPiece = false) =>
        new(scene, new Vector3(row, 0, column) * Cell, alt, false, (row, column), Cell, fixedPiece);

    /// <summary>A flat strip along the chunk's high-X edge, from x = 90 to <paramref name="x"/>, z 40 to 60.</summary>
    private static (Vector3[], (int, int, int, int)[]) Strip(float x, float y = 0) =>
        ([new(90, y, 40), new(x, y, 40), new(x, y, 60), new(90, y, 60)], [(0, 1, 2, 0), (0, 2, 3, 0)]);

    /// <summary>The next chunk's strip along its low-X edge, from <paramref name="x"/> to 10, which meets <see cref="Strip"/> at the border.</summary>
    private static (Vector3[], (int, int, int, int)[]) Beyond(float x, float y = 0) =>
        ([new(x, y, 40), new(10, y, 40), new(10, y, 60), new(x, y, 60)], [(0, 1, 2, 0), (0, 2, 3, 0)]);

    private static Dictionary<int, Dictionary<DrawVertex, Vector3>> Weld(params (Piece Piece, (Vector3[] V, (int, int, int, int)[] T) Mesh)[] pieces)
    {
        var g = new Geometry();
        foreach (var (piece, (v, t)) in pieces)
            g.Add(piece, v, t);
        return SeamWelds.Weld(g);
    }

    [Fact]
    public void NeighboursEdgesAHairApartMeetHalfway()
    {
        var welds = Weld((Chunk(1, 1, 1), Strip(100.001f)), (Chunk(2, 2, 1), Beyond(0)));
        // Both sides go to the point between them, snapped to 1/256: x 100.0005 rounds to 100, in each chunk's own coordinates.
        Assert.Equal(new Vector3(100, 0, 40), welds[1][new DrawVertex(0, 1)]);
        Assert.Equal(new Vector3(100, 0, 60), welds[1][new DrawVertex(0, 2)]);
        Assert.Equal(new Vector3(0, 0, 40), welds[2][new DrawVertex(0, 0)]);
        Assert.Equal(new Vector3(0, 0, 60), welds[2][new DrawVertex(0, 3)]);
        Assert.Equal(2, welds[1].Count);
    }

    [Fact]
    public void GapsUpToAQuarterAreWelded()
    {
        Assert.NotEmpty(Weld((Chunk(1, 1, 1), Strip(99.8f)), (Chunk(2, 2, 1), Beyond(0))));
        Assert.Empty(Weld((Chunk(1, 1, 1), Strip(99.7f)), (Chunk(2, 2, 1), Beyond(0))));
    }

    [Fact]
    public void WhatLiesOverItsNeighbourIsntASeam()
    {
        // The strip runs on over the neighbour's, a hair above it: a stand-in, or a decal, which welding would fold.
        var over = (new Vector3[] { new(90, 0.004f, 40), new(110, 0.004f, 40), new(110, 0.004f, 60), new(90, 0.004f, 60) },
            new[] { (0, 1, 2, 0), (0, 2, 3, 0) });
        var under = (new Vector3[] { new(-10, 0, 40), new(10, 0, 40), new(10, 0, 60), new(-10, 0, 60) }, new[] { (0, 1, 2, 0), (0, 2, 3, 0) });
        Assert.Empty(Weld((Chunk(1, 1, 1), over), (Chunk(2, 2, 1), under)));
    }

    [Fact]
    public void OnlyNeighboursAtTheirBorderAreWelded()
    {
        // Two chunks apart: a stand-in for land past its own cell.
        Assert.Empty(Weld((Chunk(1, 1, 1), Strip(200.004f)), (Chunk(2, 3, 1), Beyond(0))));
        // Neighbours, but meeting far past the cell they share a border with.
        var far = (new Vector3[] { new(90, 0, 40), new(130.004f, 0, 40), new(130.004f, 0, 60), new(90, 0, 60) }, new[] { (0, 1, 2, 0), (0, 2, 3, 0) });
        var beyond = (new Vector3[] { new(30, 0, 40), new(50, 0, 40), new(50, 0, 60), new(30, 0, 60) }, new[] { (0, 1, 2, 0), (0, 2, 3, 0) });
        Assert.Empty(Weld((Chunk(1, 1, 1), far), (Chunk(2, 2, 1), beyond)));
    }

    [Fact]
    public void WaterWeldsOnlyToWater()
    {
        Assert.Empty(Weld((Chunk(1, 1, 1), Strip(100.004f)), (Chunk(2, 2, 1, alt: true), Beyond(0))));
        Assert.NotEmpty(Weld((Chunk(1, 1, 1, alt: true), Strip(100.004f)), (Chunk(2, 2, 1, alt: true), Beyond(0))));
    }

    [Fact]
    public void AScenePlacedTwiceStaysAsItIs()
    {
        var welds = Weld((Chunk(1, 1, 1, fixedPiece: true), Strip(100.004f)), (Chunk(2, 2, 1), Beyond(0)));
        Assert.Empty(welds);
    }

    [Fact]
    public void WithinAScene_OnlyANodesOwnEdgesWeldWhenNodesMove()
    {
        // Two quads side by side, 0.004 apart, on nodes 0 and 1, then both on node 0.
        Vector3[] v = [new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1), new(1.004f, 0, 0), new(2, 0, 0), new(2, 0, 1), new(1.004f, 0, 1)];
        (int, int, int, int)[] Quads(int second) => [(0, 1, 2, 0), (0, 2, 3, 0), (4, 5, 6, second), (4, 6, 7, second)];
        var alone = new Piece(7, Vector3.Zero, false, true, null, 0, false);
        Assert.Empty(Weld((alone, (v, Quads(1)))));
        Assert.Equal(4, Weld((alone, (v, Quads(0))))[7].Count);
        // A chunk's scenery doesn't animate, so its nodes weld too, but only up to ModelGap.
        Assert.Equal(4, Weld((Chunk(7, 1, 1), (v, Quads(1))))[7].Count);
        Vector3[] wider = [.. v.Select(p => p.X > 1 ? p + new Vector3(0.02f, 0, 0) : p)];
        Assert.Empty(Weld((Chunk(7, 1, 1), (wider, Quads(1)))));
    }

    [Fact]
    public void AVertexBesideAnEdgeGoesOntoIt()
    {
        // One chunk's edge from z 40 to 60 at x 100; the other's two edges, its middle vertex 0.01 short of the border.
        var two = (new Vector3[] { new(0, 0, 40), new(10, 0, 40), new(10, 0, 50), new(0.01f, 0, 50), new(10, 0, 60), new(0, 0, 60) },
            new[] { (0, 1, 2, 0), (0, 2, 3, 0), (3, 2, 4, 0), (3, 4, 5, 0) });
        var welds = Weld((Chunk(1, 1, 1), Strip(100)), (Chunk(2, 2, 1), two));
        var moved = Assert.Single(welds[2]);
        Assert.Equal(new DrawVertex(0, 3), moved.Key);
        Assert.Equal(0, moved.Value.X, 4);
        Assert.Equal(50, moved.Value.Z, 4);
    }

    [Fact]
    public void TheSeamThroughK08sForestCloses()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var archive = BorgArchive.Open(rom);
        var chunks = MapChunks.Read(rom);
        var animated = archive.Listings.Where(l => l.Type == BorgType.Animation)
            .Select(l => Borg6Animation.Parse(archive.Read(l.Index)).Scene).ToHashSet();
        var welds = SeamWelds.ForChunks(chunks, archive.Read, animated.Contains);

        // J08's scenery and K08's model their sides of the path and the tree walls a hair apart (docs/systems/rendering.md).
        var j08 = chunks.Single(c => c.Set == MapSet.Overworld && c.Name == "J08");
        var k08 = chunks.Single(c => c.Set == MapSet.Overworld && c.Name == "K08");
        Assert.Equal((0x100C, 0x1037), (j08.Scene, k08.Scene));
        var west = welds[j08.Scene].Values.Select(p => p + MapSets.ChunkOrigin(j08)).Where(p => MathF.Abs(p.X - 1100) < 4).ToHashSet();
        var east = welds[k08.Scene].Values.Select(p => p + MapSets.ChunkOrigin(k08)).Where(p => MathF.Abs(p.X - 1100) < 4).ToHashSet();
        // Both sides land on the same points, exactly, along the whole seam, from z 811 to 896.
        var shared = west.Intersect(east).ToList();
        Assert.True(shared.Count >= 30, $"{shared.Count} shared points");
        Assert.InRange(shared.Min(p => p.Z), 800, 815);
        Assert.InRange(shared.Max(p => p.Z), 890, 900);
        Assert.All(shared, p => Assert.Equal(p, Snap(p)));

        // The Battlefields' scenes drawn in more than one place aren't touched.
        var twice = chunks.SelectMany(c => new[] { c.Scene, c.AltScene }).Where(s => s != 0).CountBy(s => s).Where(c => c.Value > 1).Select(c => c.Key);
        Assert.All(twice, s => Assert.False(welds.ContainsKey(s)));
    }

    [Fact]
    public void WeldingIsTheDefault() => Assert.True(ImportOptions.Default.WeldSeams);
}
