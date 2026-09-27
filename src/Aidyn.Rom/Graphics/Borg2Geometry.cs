using System.Buffers.Binary;
using System.Numerics;

namespace Aidyn.Rom.Graphics;

/// <summary>
/// <c>Borg2Struct.flags</c>: how one of a Borg2's display lists is drawn. The texture comes from
/// the Borg5 scene that draws it.
/// </summary>
[Flags]
public enum Borg2MaterialFlags : uint
{
    None = 0,
    /// <summary>Texture coordinates are generated from the normals (<c>G_TEXTURE_GEN</c>), with a reflected look-at.</summary>
    EnvironmentMap = 0x1,
    /// <summary>With <see cref="EnvironmentMap"/>, uses <c>G_TEXTURE_GEN_LINEAR</c>.</summary>
    TexGenLinear = 0x2,
    CullBack = 0x4,
    /// <summary>
    /// The vertices carry colours, and lighting is off. When it's clear they carry normals (the
    /// data bears this out; the decomp calls the bit "Lighting" but its code agrees).
    /// </summary>
    Unlit = 0x8,
    SmoothShade = 0x10,
    Unknown20 = 0x20,
    Unknown40 = 0x40,
    /// <summary><c>G_TF_POINT</c> instead of bilinear filtering.</summary>
    PointFilter = 0x80,
    Unknown100 = 0x100,
    NoZBuffer = 0x200,
    Unknown400 = 0x400,
    Unknown800 = 0x800,
    /// <summary>
    /// The texture repeats along S rather than clamping. This and the next three are inferred:
    /// with both wrap bits set, texture coordinates span many textures' widths, and without them
    /// they stay within one. The decomp's tile setup reads them differently.
    /// </summary>
    WrapS = 0x1000,
    WrapT = 0x2000,
    MirrorS = 0x4000,
    MirrorT = 0x8000,
}

/// <summary>How one display list of a Borg2 is drawn, and the triangles it draws.</summary>
/// <param name="Tint">
/// RGB is the primitive colour used when no texture is bound. A is transparency: 0 is opaque.
/// </param>
/// <param name="BoundingBox">The 8 corners the display list culls itself against.</param>
/// <param name="Vertices">
/// The distinct vertices the triangles use, in first-use order. They can differ from
/// <see cref="Borg2Geometry.Vertices"/> where the list edits texture coordinates.
/// </param>
/// <param name="Indices">Three per triangle, into <paramref name="Vertices"/>.</param>
public sealed record Borg2Mesh(Borg2MaterialFlags Flags, Vector4 Tint, N64Vertex[] BoundingBox, N64Vertex[] Vertices, int[] Indices)
{
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>
/// A Borg2: a model's vertices and its F3DEX2 display lists, one per material (roadmap 1A).
/// </summary>
/// <remarks>
/// <para>
/// The 0x50-byte header is alpha, display-list count and scale, a position and a rotation (3
/// floats each), two unknown words, then offsets from the start of the data to the display-list
/// table, the bounding boxes and the vertices, the vertex count, an offset that's always 0, the
/// material table, and three words that are always 0.
/// </para>
/// <para>
/// The parts follow in order: 8 bounding-box vertices per display list, the vertices, the
/// display lists, the table of their offsets, and a 0x14-byte material per display list. The
/// game points segment 0xE at the bounding boxes and segment 0xA at the vertices before running
/// a display list.
/// </para>
/// </remarks>
public sealed class Borg2Geometry
{
    public const int HeaderSize = 0x50;
    private const int BoundingBoxVertices = 8;
    private const int MaterialSize = 0x14;
    private const int VertexSegment = 0xA;
    private const int BoundingBoxSegment = 0xE;

    private Borg2Geometry(float alpha, float scale, Vector3 position, Vector3 rotation, uint unknown24, uint unknown28,
        N64Vertex[] vertices, Borg2Mesh[] meshes)
    {
        Alpha = alpha;
        Scale = scale;
        Position = position;
        Rotation = rotation;
        Unknown24 = unknown24;
        Unknown28 = unknown28;
        Vertices = vertices;
        Meshes = meshes;
    }

    /// <summary>Transparency: 0 is opaque. It's 0 in every Borg2.</summary>
    public float Alpha { get; }

    /// <summary>Scales the model's vertices when it's drawn; usually 1/16.</summary>
    public float Scale { get; }

    public Vector3 Position { get; }

    /// <summary>In radians.</summary>
    public Vector3 Rotation { get; }

    /// <summary>Usually 0xFFFFFFFF, otherwise 0 to 7. Unknown.</summary>
    public uint Unknown24 { get; }

    /// <summary>0 or 1. Per the decomp, the game doesn't draw the display lists when bit 0 is set.</summary>
    public uint Unknown28 { get; }

    /// <summary>Everything segment 0xA holds.</summary>
    public IReadOnlyList<N64Vertex> Vertices { get; }

    /// <summary>One per display list, in the order Borg5 scenes refer to them.</summary>
    public IReadOnlyList<Borg2Mesh> Meshes { get; }

    public static Borg2Geometry Parse(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"A Borg2 is at least 0x{HeaderSize:X} bytes, not {data.Length}.");

        var h = data.AsSpan();
        var alpha = ReadSingle(h, 0);
        var count = BinaryPrimitives.ReadInt32BigEndian(h[4..]);
        var scale = ReadSingle(h, 8);
        var position = new Vector3(ReadSingle(h, 0xC), ReadSingle(h, 0x10), ReadSingle(h, 0x14));
        var rotation = new Vector3(ReadSingle(h, 0x18), ReadSingle(h, 0x1C), ReadSingle(h, 0x20));
        var unknown24 = BinaryPrimitives.ReadUInt32BigEndian(h[0x24..]);
        var unknown28 = BinaryPrimitives.ReadUInt32BigEndian(h[0x28..]);
        var table = (int)BinaryPrimitives.ReadUInt32BigEndian(h[0x2C..]);
        var boxes = (int)BinaryPrimitives.ReadUInt32BigEndian(h[0x30..]);
        var vertexOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(h[0x34..]);
        var vertexCount = (int)BinaryPrimitives.ReadUInt32BigEndian(h[0x38..]);
        var materials = (int)BinaryPrimitives.ReadUInt32BigEndian(h[0x40..]);

        if (count < 0)
            throw new InvalidDataException($"Negative display-list count {count}.");
        for (var at = 0x3C; at < HeaderSize; at += 4)
            if (at != 0x40 && BinaryPrimitives.ReadUInt32BigEndian(h[at..]) != 0)
                throw new InvalidDataException($"The header word at 0x{at:X} isn't 0; nothing is known to use it.");
        Expect("bounding boxes", boxes, HeaderSize);
        Expect("vertices", vertexOffset, boxes + count * BoundingBoxVertices * N64Vertex.Size);
        var listsStart = vertexOffset + vertexCount * N64Vertex.Size;
        if (table < listsStart)
            throw new InvalidDataException($"The display-list table at 0x{table:X} overlaps the vertices.");
        Expect("materials", materials, table + count * 4);
        if (data.Length != materials + count * MaterialSize)
            throw new InvalidDataException($"The Borg2 ends at 0x{materials + count * MaterialSize:X}, not 0x{data.Length:X}.");

        var memory = data.AsMemory();
        var segments = new Dictionary<int, ReadOnlyMemory<byte>>
        {
            [VertexSegment] = memory.Slice(vertexOffset, vertexCount * N64Vertex.Size),
            [BoundingBoxSegment] = memory.Slice(boxes, count * BoundingBoxVertices * N64Vertex.Size),
        };

        var vertices = ReadVertices(h[vertexOffset..], vertexCount);
        var meshes = new Borg2Mesh[count];
        for (var i = 0; i < count; i++)
        {
            var list = (int)BinaryPrimitives.ReadUInt32BigEndian(h[(table + i * 4)..]);
            if (i == 0 ? list != listsStart : list < listsStart || list >= table)
                throw new InvalidDataException($"Display list {i} is at 0x{list:X}, outside 0x{listsStart:X}-0x{table:X}.");

            var m = h[(materials + i * MaterialSize)..];
            var flags = (Borg2MaterialFlags)BinaryPrimitives.ReadUInt32BigEndian(m);
            var tint = new Vector4(ReadSingle(m, 4), ReadSingle(m, 8), ReadSingle(m, 0xC), ReadSingle(m, 0x10));
            var box = ReadVertices(h[(boxes + i * BoundingBoxVertices * N64Vertex.Size)..], BoundingBoxVertices);
            var (meshVertices, indices) = Index(F3dex2.Run(h[..table], list, segments));
            meshes[i] = new Borg2Mesh(flags, tint, box, meshVertices, indices);
        }

        return new Borg2Geometry(alpha, scale, position, rotation, unknown24, unknown28, vertices, meshes);

        static void Expect(string part, int actual, int expected)
        {
            if (actual != expected)
                throw new InvalidDataException($"The {part} are at 0x{actual:X}, not 0x{expected:X}.");
        }
    }

    private static (N64Vertex[] Vertices, int[] Indices) Index(List<N64Triangle> triangles)
    {
        var vertices = new List<N64Vertex>();
        var lookup = new Dictionary<N64Vertex, int>();
        var indices = new int[triangles.Count * 3];
        var i = 0;
        foreach (var t in triangles)
        {
            indices[i++] = IndexOf(t.A);
            indices[i++] = IndexOf(t.B);
            indices[i++] = IndexOf(t.C);
        }
        return (vertices.ToArray(), indices);

        int IndexOf(N64Vertex v)
        {
            if (!lookup.TryGetValue(v, out var index))
            {
                index = vertices.Count;
                lookup.Add(v, index);
                vertices.Add(v);
            }
            return index;
        }
    }

    private static N64Vertex[] ReadVertices(ReadOnlySpan<byte> data, int count)
    {
        var vertices = new N64Vertex[count];
        for (var i = 0; i < count; i++)
            vertices[i] = N64Vertex.Read(data[(i * N64Vertex.Size)..]);
        return vertices;
    }

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadSingleBigEndian(data[offset..]);
}
