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
