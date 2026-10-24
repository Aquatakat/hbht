using System.Buffers.Binary;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Tests;

public class GeometryTests
{
    private static readonly Dictionary<int, ReadOnlyMemory<byte>> Segments = new() { [0xA] = Vertices(4) };

    [Fact]
    public void TrianglesUseTheVerticesLoadedIntoTheBuffer()
    {
        // Load vertices 0-3 into buffer slots 2-5, then draw (2,3,4) and, with TRI2, (5,4,3) and (2,5,3).
        var triangles = F3dex2.Run(DisplayList(
            0x0100_400C, 0x0A00_0000,
            0x0504_0608, 0x0000_0000,
            0x060A_0806, 0x0004_0A06,
            0xDF00_0000, 0), 0, Segments);

        Assert.Equal([(0, 1, 2), (3, 2, 1), (0, 3, 1)], triangles.Select(t => (t.A.X, t.B.X, t.C.X)).Select(Ints));
    }

    /// <summary>
    /// The RSP lights a load's vertices two at a time from its first, so whether one is second of a pair goes by its place in
    /// the load, not its buffer slot or its place in the vertices: the same vertex loaded twice can be either.
    /// </summary>
    [Fact]
    public void VerticesAreSecondOfAPairByTheirPlaceInTheLoad()
    {
        // Vertices 0-3 into slots 2-5, then vertices 1-3 into slots 7-9; draw (2,3,4) and (7,8,9).
        var triangles = F3dex2.Run(DisplayList(
            0x0100_400C, 0x0A00_0000,
            0x0100_3014, 0x0A00_0010,
            0x0504_0608, 0x0000_0000,
            0x050E_1012, 0x0000_0000,
            0xDF00_0000, 0), 0, Segments);

        Assert.Equal([(0, false), (1, true), (2, false)], Corners(triangles[0]));
        Assert.Equal([(1, false), (2, true), (3, false)], Corners(triangles[1]));

        static (int, bool)[] Corners(N64Triangle t) => [.. new[] { t.A, t.B, t.C }.Select(v => ((int)v.X, v.SecondOfPair))];
    }

    /// <summary>
    /// G_MODIFYVTX writes texture coordinates that the texture scale has already been applied to,
    /// so at a scale of ½ they come back doubled.
    /// </summary>
    [Fact]
    public void ModifyVtxChangesTextureCoordinatesOnly()
    {
        var triangles = F3dex2.Run(DisplayList(
            0x0100_3006, 0x0A00_0000,
            0x0214_0002, 0x1234_FFFE,
            0x0500_0204, 0x0000_0000,
            0xDF00_0000, 0), 0, Segments, textureScale: 0x8000);

        var edited = triangles[0].B;
        Assert.Equal((0x2468, -4), (edited.S, edited.T));
        Assert.Equal(Vertices(4).Span.Slice(N64Vertex.Size, 8).ToArray(), Bytes(edited)[..8]);
    }

    [Fact]
    public void UnloadedVerticesAndUnknownCommandsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => F3dex2.Run(DisplayList(0x0500_0204, 0, 0xDF00_0000, 0), 0, Segments));
        Assert.Throws<InvalidDataException>(() => F3dex2.Run(DisplayList(0xDA38_0003, 0, 0xDF00_0000, 0), 0, Segments));
        Assert.Throws<InvalidDataException>(() => F3dex2.Run(DisplayList(0x0100_1002, 0x0B00_0000, 0xDF00_0000, 0), 0, Segments));
    }

    [Fact]
    public void EveryBorg2Parses()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        int models = 0, meshes = 0, triangles = 0, empty = 0, unlit = 0;
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Geometry))
        {
            var geometry = Borg2Geometry.Parse(archive.Read(listing.Index));
            models++;
            foreach (var mesh in geometry.Meshes)
            {
                meshes++;
                triangles += mesh.TriangleCount;
                if (mesh.TriangleCount == 0)
                    empty++;
                if (mesh.Flags.HasFlag(Borg2MaterialFlags.Unlit))
                    unlit++;
            }
        }

        Assert.Equal((5636, 14996, 319_709, 1013, 9833), (models, meshes, triangles, empty, unlit));
    }

    /// <summary>
    /// Lit display lists have normals where unlit ones have colours; normals are about 127 long.
    /// This pins down which way round <see cref="Borg2MaterialFlags.Unlit"/> is.
    /// </summary>
    [Fact]
    public void LitMeshesHaveNormals()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        long litUnit = 0, lit = 0, unlitUnit = 0, unlitAll = 0;
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Geometry))
        foreach (var mesh in Borg2Geometry.Parse(archive.Read(listing.Index)).Meshes)
        foreach (var v in mesh.Vertices)
        {
            var (x, y, z) = v.Normal;
            var unit = Math.Abs(Math.Sqrt(x * x + y * y + z * z) - 127) <= 9;
            if (mesh.Flags.HasFlag(Borg2MaterialFlags.Unlit))
            {
                unlitAll++;
                unlitUnit += unit ? 1 : 0;
            }
            else
            {
                lit++;
                litUnit += unit ? 1 : 0;
            }
        }

        Assert.True(litUnit > lit * 0.999, $"{litUnit} of {lit} lit vertices have unit normals.");
        Assert.True(unlitUnit < unlitAll * 0.2, $"{unlitUnit} of {unlitAll} unlit vertices look like unit normals.");
    }

    [Fact]
    public void MeshesLieInsideTheirBoundingBoxes()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Geometry))
        foreach (var mesh in Borg2Geometry.Parse(archive.Read(listing.Index)).Meshes)
        {
            if (mesh.Vertices.Length == 0)
                continue;
            var box = mesh.BoundingBox;
            foreach (var v in mesh.Vertices)
            {
                Assert.InRange(v.X, box.Min(b => b.X), box.Max(b => b.X));
                Assert.InRange(v.Y, box.Min(b => b.Y), box.Max(b => b.Y));
                Assert.InRange(v.Z, box.Min(b => b.Z), box.Max(b => b.Z));
            }
        }
    }

    /// <summary>
    /// Arenas 24 and 27's floor (Borg2 13936, scene 0x3671) wraps S and T, and its disc's texture coordinates are a plane in
    /// X and Z, each triangle shifted by whole tiles. The skirt out to ±250 would need coordinates past the 16 bits a vertex
    /// holds (at the lists' 0.5 texture scale, ±16 tiles of the 32-texel texture), and the data holds them wrapped by whole
    /// 32-tile turns instead, so its triangles stretch the texture in long streaks. That's the original's data, drawn as is.
    /// </summary>
    [Fact]
    public void ArenaFloorsSkirtHoldsItsTextureCoordinatesWrappedPastSixteenBits()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var mesh = Assert.Single(Borg2Geometry.Parse(archive.Read(13936)).Meshes);
        Assert.True(mesh.Flags.HasFlag(Borg2MaterialFlags.WrapS | Borg2MaterialFlags.WrapT));
        const int tile = 32 * 32 * 0x10000 / Borg2Geometry.TextureScale; // a 32-texel tile in 10.5 units, before the 0.5 scale
        // The plane, from the first triangle's three disc vertices: S by X, T by Z.
        var (a, b, c) = (mesh.Vertices[mesh.Indices[0]], mesh.Vertices[mesh.Indices[1]], mesh.Vertices[mesh.Indices[2]]);
        var sPerX = (double)(b.S - c.S) / (b.X - c.X);
        var tPerZ = (double)(a.T - b.T) / (a.Z - b.Z);
        double S(N64Vertex v) => b.S + (v.X - b.X) * sPerX;
        double T(N64Vertex v) => b.T + (v.Z - b.Z) * tPerZ;
        static double OffTile(double d) => Math.Abs(d / tile - Math.Round(d / tile));
        var overflowed = 0;
        foreach (var v in mesh.Vertices)
        {
            // The plane's slopes come from vertices 23 apart and reach 250 out, so allow a twentieth of a tile.
            Assert.True(OffTile(S(v) - v.S) < 0.05 && OffTile(T(v) - v.T) < 0.05, $"{v} is off the plane by part of a tile.");
            if (S(v) is < short.MinValue or > short.MaxValue || T(v) is < short.MinValue or > short.MaxValue)
                overflowed++;
        }
        // Every one of the skirt's far vertices (four corners, each in two triangles' loads), and none of the disc's.
        Assert.Equal(8, overflowed);
        Assert.Equal(8, mesh.Vertices.Count(v => v.Y == 0));
    }

    /// <summary><paramref name="count"/> vertices with X = index and every other field distinct.</summary>
    private static ReadOnlyMemory<byte> Vertices(int count)
    {
        var data = new byte[count * N64Vertex.Size];
        for (var i = 0; i < count; i++)
        {
            var v = data.AsSpan(i * N64Vertex.Size);
            BinaryPrimitives.WriteInt16BigEndian(v, (short)i);
            for (var j = 2; j < N64Vertex.Size; j++)
                v[j] = (byte)(i * 16 + j);
        }
        return data;
    }

    private static byte[] DisplayList(params uint[] words)
    {
        var data = new byte[words.Length * 4];
        for (var i = 0; i < words.Length; i++)
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i * 4), words[i]);
        return data;
    }

    private static byte[] Bytes(N64Vertex v)
    {
        var b = new byte[N64Vertex.Size];
        BinaryPrimitives.WriteInt16BigEndian(b, v.X);
        BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(2), v.Y);
        BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(4), v.Z);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(6), v.Flag);
        BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(8), v.S);
        BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(10), v.T);
        (b[12], b[13], b[14], b[15]) = (v.R, v.G, v.B, v.A);
        return b;
    }

    private static (int, int, int) Ints((short A, short B, short C) t) => (t.A, t.B, t.C);
}
