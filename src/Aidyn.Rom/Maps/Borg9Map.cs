using System.Numerics;
using Aidyn.Plugins.Assets;

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
    /// <summary>Blocks only the player: every such triangle is also <see cref="CollisionFlags.SeeThrough"/>.</summary>
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
    /// <summary>Would slow and lighten whatever touches it, but no triangle has <see cref="CollisionFlags.TakesSpeed"/>.</summary>
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

/// <summary>A collision triangle of a Borg9 (<c>borg9_phys</c>, 0x20 bytes).</summary>
/// <param name="A">Indices into <see cref="Borg9Map.Vertices"/>, counter-clockwise seen from the front.</param>
/// <param name="Normal">The unit normal of A, B, C, apart from 44 degenerate triangles.</param>
/// <param name="Flags">As stored: the plugin's collision asset carries them unchanged.</param>
/// <param name="Ground">As stored. See <see cref="Role"/>.</param>
public sealed record RomCollisionTriangle(int A, int B, int C, Vector3 Normal, CollisionSurface Surface, CollisionFlags Flags, ushort Ground)
{
    public const int Size = 0x20;

    /// <summary>What the ground type's top four bits make it: 0x1000 a toggle, 0x2000 a trigger, 0x4000 Expansion Pak only, else normal.</summary>
    public CollisionRole Role => (Ground & 0xF000) switch
    {
        0x1000 => CollisionRole.Toggle,
        0x2000 => CollisionRole.Trigger,
        0x4000 => CollisionRole.ExpansionPak,
        _ => CollisionRole.Normal,
    };

    /// <summary>For a <see cref="CollisionRole.Toggle"/>, the id its <see cref="Plugins.Records.TriggerKind.ChangeCollision"/> triggers name, else null.</summary>
    public int? ToggleId => Role == CollisionRole.Toggle ? (Ground + 1) & 0x7F : null;

    /// <summary>For a <see cref="CollisionRole.Trigger"/>, the index of the object it runs, else null.</summary>
    public int? TriggerObject => Role == CollisionRole.Trigger ? Ground & 0x7F : null;
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
    IReadOnlyList<RomCollisionTriangle> Triangles,
    IReadOnlyList<VoxelObject> Objects,
    IReadOnlyList<CollisionCell> Cells)
{
    private const int HeaderSize = 0x50;
    private const int CellRecordSize = 0x14;

    /// <summary>
    /// The neighbours drawn while the player is in this chunk whatever the camera's view (header byte 0x1B, the
    /// decomp's <c>byte0x1b</c>), a bit each in the order of the zone engine's table at <c>0x800CD700</c>: row and
    /// column −1 −1, +1 −1, −1 +1, +1 +1, then 0 −1, 0 +1, −1 0 and +1 0 (<c>RenderZones</c>, <c>0x8000d574</c>,
    /// and <c>RenderTransZones</c>, <c>0x8000dd44</c>, in 1.0). 22 Borg9s set it, 21 of them Overworld chunks.
    /// </summary>
    public byte DrawnNeighbours { get; init; }

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
        var origin = new Vector3(Be.F32(s, 0), Be.F32(s, 4), Be.F32(s, 8));
        var cellSize = new Vector2(Be.F32(s, 0xC), Be.F32(s, 0x10));
        int triangleIndexCount = Be.U16(s, 0x14), objectIndexCount = Be.U16(s, 0x18);
        int columns = Be.U16(s, 0x1C), rows = Be.U16(s, 0x1E);
        var vertexCount = (int)Be.U32(s, 0x20);
        int triangleCount = Be.U16(s, 0x24), cellCount = Be.U16(s, 0x2A);
        int triangleIndices = (int)Be.U32(s, 0x2C), objectIndices = (int)Be.U32(s, 0x34), vertices = (int)Be.U32(s, 0x38),
            triangles = (int)Be.U32(s, 0x3C), cells = (int)Be.U32(s, 0x48);

        // 0x30 and 0x40 are offsets the game fixes up and never uses, and hold leftovers.
        Be.Expect("Borg9", "triangle indices", triangleIndices, HeaderSize);
        Be.Expect("Borg9", "object indices", objectIndices, Align(triangleIndices + triangleIndexCount * 2));
        Be.Expect("Borg9", "vertices", vertices, Align(objectIndices + objectIndexCount * 2));
        Be.Expect("Borg9", "triangles", triangles, Align(vertices + vertexCount * 12));
        Be.Expect("Borg9", "objects", (int)Be.U32(s, 0x44), triangles + triangleCount * RomCollisionTriangle.Size);
        if (cellCount != columns * rows)
            throw new InvalidDataException($"The Borg9 has {cellCount} cells for a {columns}×{rows} grid.");
        if (cells + cellCount * CellRecordSize > data.Length)
            throw new InvalidDataException($"The Borg9's cells run past its end (0x{data.Length:X}).");

        var vertexList = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
            vertexList[i] = new Vector3(Be.F32(s, vertices + i * 12), Be.F32(s, vertices + i * 12 + 4), Be.F32(s, vertices + i * 12 + 8));

        var triangleList = new RomCollisionTriangle[triangleCount];
        for (var i = 0; i < triangleCount; i++)
        {
            var t = s.Slice(triangles + i * RomCollisionTriangle.Size, RomCollisionTriangle.Size);
            var surface = Be.U32(t, 0x18);
            if (surface > (uint)CollisionSurface.TransStoneHugeEcho)
                throw new InvalidDataException($"Triangle {i} has unknown surface {surface}.");
            triangleList[i] = new RomCollisionTriangle(
                Vertex(Be.U32(t, 0), vertexCount, i), Vertex(Be.U32(t, 4), vertexCount, i), Vertex(Be.U32(t, 8), vertexCount, i),
                new Vector3(Be.F32(t, 0xC), Be.F32(t, 0x10), Be.F32(t, 0x14)), (CollisionSurface)surface,
                (CollisionFlags)Be.U16(t, 0x1C), Be.U16(t, 0x1E));
        }

        var cellList = new CollisionCell[cellCount];
        for (var i = 0; i < cellCount; i++)
        {
            var c = s.Slice(cells + i * CellRecordSize, CellRecordSize);
            cellList[i] = new CollisionCell(
                Indices(s, triangleIndices, (int)Be.U32(c, 0), Be.U16(c, 0xC), triangleIndexCount, triangleCount, i),
                Indices(s, objectIndices, (int)Be.U32(c, 8), Be.U16(c, 0x10), objectIndexCount, Be.U16(s, 0x28), i));
        }

        return new Borg9Map(origin, cellSize, columns, rows, vertexList, triangleList, Borg9Voxels.Read(data), cellList)
        {
            DrawnNeighbours = s[0x1B],
        };
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
            indices[i] = Be.U16(s, list + offset + i * 2);
            if (indices[i] >= limit)
                throw new InvalidDataException($"Cell {cell} names {indices[i]}, but there are {limit}.");
        }
        return indices;
    }

    private static int Align(int offset) => (offset + 7) & ~7;
}
