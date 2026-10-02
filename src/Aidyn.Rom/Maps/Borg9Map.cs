using System.Buffers.Binary;
using System.Numerics;

namespace Aidyn.Rom.Maps;

/// <summary>
/// A collision triangle's surface: its physics property (<c>borg9_phys.physProp</c>). The names
/// are the game's own, from <c>String/Common</c>. Surfaces pick footstep sounds. Whatever touches
/// <see cref="PlayerAndCamera"/> or <see cref="Player"/> keeps its speed and slides along; the
/// other surfaces stop it (<c>EnvPropBools</c>).
/// </summary>
public enum CollisionSurface : byte
{
    Default,
    /// <summary>Blocks the player and the camera. Most walls and floors.</summary>
    PlayerAndCamera,
    /// <summary>Blocks only the player: every such triangle is also <see cref="TriangleFlags.SeeThrough"/>.</summary>
    Player,
    Snow,
    Grass,
    Sand,
    Mucky,
    Sizzle,
    Cave,
    Wood,
    WoodEcho,
    Stone,
    StoneEcho,
    StoneHugeEcho,
    /// <summary>Would slow and lighten whatever touches it, but no triangle has <see cref="TriangleFlags.TakesSpeed"/>.</summary>
    Water,
    TransSnow,
    TransGrass,
    TransSand,
    TransMucky,
    TransSizzle,
    TransCave,
    TransWood,
    TransWoodEcho,
    TransStone,
    TransStoneEcho,
    TransStoneHugeEcho,
}

/// <summary><c>borg9_phys.flags</c>, as stored. The game sets <see cref="Callback"/> on every triangle it loads.</summary>
[Flags]
public enum TriangleFlags : ushort
{
    None = 0,
    /// <summary>Things pass through, and only its callbacks run. A <see cref="TriggerKind.ChangeCollision"/> trigger sets it.</summary>
    Passable = 0x1,
    /// <summary>Whatever touches it takes on its surface's speed property (gravity and drag). Never set.</summary>
    TakesSpeed = 0x2,
    /// <summary>Collide with the edge from vertex A to B. Every edge no other triangle shares has its bit.</summary>
    EdgeAB = 0x4,
    /// <summary>Collide with the edge from C to A.</summary>
    EdgeCA = 0x8,
    /// <summary>Collide with the edge from B to C.</summary>
    EdgeBC = 0x10,
    VertexA = 0x20,
    VertexB = 0x40,
    VertexC = 0x80,
    /// <summary>Recompute the normal each time. Never set.</summary>
    RecomputeNormal = 0x100,
    /// <summary>Line-of-sight tests ignore it: the camera, combat markers and light occlusion.</summary>
    SeeThrough = 0x200,
    /// <summary>Report touches to the zone engine, which sets footstep sounds and runs <see cref="CollisionTriangle.TriggerObject"/>.</summary>
    Callback = 0x1000,
    /// <summary>Touch it with no extra margin. Never set.</summary>
    NoMargin = 0x2000,
}

/// <summary>What a triangle's ground type makes it (<c>borg9_phys.GroundType</c>'s top four bits).</summary>
public enum TriangleRole : ushort
{
    Normal = 0,
    /// <summary>A <see cref="TriggerKind.ChangeCollision"/> trigger can change its flags.</summary>
    Toggle = 0x1000,
    /// <summary>Walking on it runs a camera, teleporter or dialogue object.</summary>
    Trigger = 0x2000,
    /// <summary>Only there with the Expansion Pak. Without it, the game makes it passable.</summary>
    ExpansionPak = 0x4000,
}

/// <summary>A collision triangle of a Borg9 (<c>borg9_phys</c>, 0x20 bytes).</summary>
/// <param name="A">Indices into <see cref="Borg9Map.Vertices"/>, counter-clockwise seen from the front.</param>
/// <param name="Normal">The unit normal of A, B, C, apart from 44 degenerate triangles.</param>
/// <param name="Ground">As stored. See <see cref="Role"/>.</param>
public sealed record CollisionTriangle(int A, int B, int C, Vector3 Normal, CollisionSurface Surface, TriangleFlags Flags, ushort Ground)
{
    public const int Size = 0x20;

    public TriangleRole Role => (TriangleRole)(Ground & 0xF000);

    /// <summary>For a <see cref="TriangleRole.Toggle"/>, the id its <see cref="TriggerKind.ChangeCollision"/> triggers name, else null.</summary>
    public int? ToggleId => Role == TriangleRole.Toggle ? (Ground + 1) & 0x7F : null;

    /// <summary>For a <see cref="TriangleRole.Trigger"/>, the index of the object it runs, else null.</summary>
    public int? TriggerObject => Role == TriangleRole.Trigger ? Ground & 0x7F : null;
}

/// <summary>
/// A cell of a Borg9's grid (<c>CollideSection</c>): what collision and use checks look at near
/// the player.
/// </summary>
/// <param name="Triangles">Indices into <see cref="Borg9Map.Triangles"/>.</param>
/// <param name="Objects">
/// Indices into <see cref="Borg9Map.Objects"/>: the scenes drawn and the objects the player can
/// use from the cell, which A weighs only these of (<c>obj_ref_func</c>). Sounds, wander nodes,
/// monster parties and reference points are never listed.
/// </param>
public sealed record CollisionCell(IReadOnlyList<int> Triangles, IReadOnlyList<int> Objects);

/// <summary>
/// A Borg9: a map chunk's collision and map objects (roadmap 1C). Coordinates are the chunk's
/// own; the chunk sits at <see cref="MapSets.ChunkOrigin"/>.
/// </summary>
/// <param name="Origin">The collision's minimum corner, where the grid starts.</param>
/// <param name="CellSize">Each grid cell's size along X and Z.</param>
/// <param name="Columns">Grid cells along X.</param>
/// <param name="Rows">Grid cells along Z.</param>
/// <param name="Cells">Row by row: the cell at (x, z) is <c>Cells[z × Columns + x]</c>.</param>
public sealed record Borg9Map(
    Vector3 Origin,
    Vector2 CellSize,
    int Columns,
    int Rows,
    IReadOnlyList<Vector3> Vertices,
    IReadOnlyList<CollisionTriangle> Triangles,
    IReadOnlyList<VoxelObject> Objects,
    IReadOnlyList<CollisionCell> Cells)
{
    private const int HeaderSize = 0x50;
    private const int CellRecordSize = 0x14;

    public CollisionCell Cell(int x, int z) => Cells[z * Columns + x];

    /// <summary>
    /// Parses a Borg9. The parts are back to back, each 8-byte aligned: the cells' triangle
    /// indices, their object indices, the vertices, the triangles, the objects and the cells. The
    /// header's offsets are from the start of the data, and its counts are all checked.
    /// </summary>
    public static Borg9Map Parse(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"A Borg9 is at least 0x{HeaderSize:X} bytes, not 0x{data.Length:X}.");
        var s = data.AsSpan();
        var origin = new Vector3(F32(s, 0), F32(s, 4), F32(s, 8));
        var cellSize = new Vector2(F32(s, 0xC), F32(s, 0x10));
        int triangleIndexCount = U16(s, 0x14), objectIndexCount = U16(s, 0x18);
        int columns = U16(s, 0x1C), rows = U16(s, 0x1E);
        var vertexCount = (int)U32(s, 0x20);
        int triangleCount = U16(s, 0x24), cellCount = U16(s, 0x2A);
        int triangleIndices = (int)U32(s, 0x2C), objectIndices = (int)U32(s, 0x34), vertices = (int)U32(s, 0x38),
            triangles = (int)U32(s, 0x3C), cells = (int)U32(s, 0x48);

        // 0x30 and 0x40 are offsets the game fixes up and never uses, and hold leftovers.
        Expect(triangleIndices == HeaderSize, "its triangle indices", triangleIndices);
        Expect(objectIndices == Align(triangleIndices + triangleIndexCount * 2), "its object indices", objectIndices);
        Expect(vertices == Align(objectIndices + objectIndexCount * 2), "its vertices", vertices);
        Expect(triangles == Align(vertices + vertexCount * 12), "its triangles", triangles);
        Expect((int)U32(s, 0x44) == triangles + triangleCount * CollisionTriangle.Size, "its objects", (int)U32(s, 0x44));
        if (cellCount != columns * rows)
            throw new InvalidDataException($"The Borg9 has {cellCount} cells for a {columns}×{rows} grid.");
        if (cells + cellCount * CellRecordSize > data.Length)
            throw new InvalidDataException($"The Borg9's cells run past its end (0x{data.Length:X}).");

        var vertexList = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
            vertexList[i] = new Vector3(F32(s, vertices + i * 12), F32(s, vertices + i * 12 + 4), F32(s, vertices + i * 12 + 8));

        var triangleList = new CollisionTriangle[triangleCount];
        for (var i = 0; i < triangleCount; i++)
        {
            var t = s.Slice(triangles + i * CollisionTriangle.Size, CollisionTriangle.Size);
            var surface = U32(t, 0x18);
            if (surface > (uint)CollisionSurface.TransStoneHugeEcho)
                throw new InvalidDataException($"Triangle {i} has unknown surface {surface}.");
            triangleList[i] = new CollisionTriangle(
                Vertex(U32(t, 0), vertexCount, i), Vertex(U32(t, 4), vertexCount, i), Vertex(U32(t, 8), vertexCount, i),
                new Vector3(F32(t, 0xC), F32(t, 0x10), F32(t, 0x14)), (CollisionSurface)surface,
                (TriangleFlags)U16(t, 0x1C), U16(t, 0x1E));
        }

        var cellList = new CollisionCell[cellCount];
        for (var i = 0; i < cellCount; i++)
        {
            var c = s.Slice(cells + i * CellRecordSize, CellRecordSize);
            cellList[i] = new CollisionCell(
                Indices(s, triangleIndices, (int)U32(c, 0), U16(c, 0xC), triangleIndexCount, triangleCount, i),
                Indices(s, objectIndices, (int)U32(c, 8), U16(c, 0x10), objectIndexCount, U16(s, 0x28), i));
        }

        return new Borg9Map(origin, cellSize, columns, rows, vertexList, triangleList, Borg9Voxels.Read(data), cellList);
    }

    /// <summary>A triangle's vertex is a byte offset into the vertex list, which the game turns into a pointer.</summary>
    private static int Vertex(uint offset, int vertexCount, int triangle)
    {
        if (offset % 12 != 0 || offset / 12 >= vertexCount)
            throw new InvalidDataException($"Triangle {triangle} names vertex offset 0x{offset:X}.");
        return (int)(offset / 12);
    }

    private static int[] Indices(ReadOnlySpan<byte> s, int list, int offset, int count, int listCount, int limit, int cell)
    {
        if (count == 0)
            return [];
        if (offset % 2 != 0 || offset / 2 + count > listCount)
            throw new InvalidDataException($"Cell {cell}'s list at 0x{offset:X} ({count}) runs past its end.");
        var indices = new int[count];
        for (var i = 0; i < count; i++)
        {
            indices[i] = U16(s, list + offset + i * 2);
            if (indices[i] >= limit)
                throw new InvalidDataException($"Cell {cell} names {indices[i]}, but there are {limit}.");
        }
        return indices;
    }

    private static void Expect(bool ok, string what, int actual)
    {
        if (!ok)
            throw new InvalidDataException($"The Borg9 header doesn't give {what} where the layout puts them (0x{actual:X}).");
    }

    private static int Align(int offset) => (offset + 7) & ~7;
    private static ushort U16(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt16BigEndian(s[at..]);
    private static uint U32(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt32BigEndian(s[at..]);
    private static float F32(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadSingleBigEndian(s[at..]);
}
