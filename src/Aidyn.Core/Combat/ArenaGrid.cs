using System.Numerics;
using Aidyn.Core.Physics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>
/// The arena's grid (<c>Combat.Current</c>'s <c>+0x3C</c>, <c>ArenaGrid</c>, <c>0x800674A0</c>–<c>0x80068F7C</c>): 100 × 100
/// cells of a byte, a unit across in the arena chunk's own coordinates, holding the floor's height × 2 in the low seven bits
/// and whether the cell is taken in bit 7. Cells the collision doesn't reach are taken. Combatants stand on it in whole
/// cells (<see cref="Occupy"/> and <see cref="Vacate"/>, as they move), and paths and lines of sight run over it (sessions 5 and 6).
/// </summary>
public sealed class ArenaGrid
{
    /// <summary>The cells a side.</summary>
    public const int Side = 100;

    /// <summary>A cell's bit for taken: by a combatant, or off the collision.</summary>
    public const byte Taken = 0x80;

    private readonly byte[] _cells = new byte[Side * Side];

    /// <summary>The cells across in X (<c>+0x4E22</c>), from the map's object grid.</summary>
    public int Width { get; private set; }

    /// <summary>The cells across in Z (<c>+0x4E23</c>).</summary>
    public int Depth { get; private set; }

    /// <summary>The cells in all (<c>+0x4E20</c>, a u16).</summary>
    public int Area => (ushort)(Width * Depth);

    /// <summary>Half the width, and half the depth (<c>+0x4E24</c>, <c>+0x4E25</c>).</summary>
    public (int X, int Z) Half => (Width >> 1, Depth >> 1);

    /// <summary>The diagonal, truncated to a byte (<c>+0x4E26</c>), the longest path.</summary>
    public int Diagonal => (byte)(int)MathF.Sqrt(Width * Width + Depth * Depth);

    /// <summary>A cell's byte: the floor's height × 2, and <see cref="Taken"/>.</summary>
    public byte this[int x, int z] => _cells[x * Side + z];

    /// <summary>Whether a cell is in the arena's width and depth.</summary>
    public bool Inside(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Depth;

    /// <summary>
    /// Sizes the grid by the arena map's object grid (<c>ArenaGrid.Init</c>, <c>0x80067960</c>): its cell size times its
    /// columns and rows, each rounded half away from zero to a byte, and empties it.
    /// </summary>
    public void Init(MapRecord map)
    {
        Array.Clear(_cells);
        if (map.Grid is not { } grid)
        {
            Width = Depth = 0;
            return;
        }
        Width = (byte)(int)MathF.Round(grid.CellSize.X * grid.Columns, MidpointRounding.AwayFromZero);
        Depth = (byte)(int)MathF.Round(grid.CellSize.Y * grid.Rows, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Fills each cell of the width and depth from the arena's collision (<c>ArenaGrid.Fill</c>, <c>0x8005E900</c>): a sphere
    /// of 0.5 swept from 100 down to −100 through the cell's centre, see-through triangles included, takes the first that
    /// faces it in the grid's order, and the cell holds twice the height it touches, truncated; a miss leaves it taken.
    /// </summary>
    /// <param name="origin">Where the arena chunk's coordinates start.</param>
    /// <param name="part">The arena chunk's key in the collision, so that only its triangles count, as the original's.</param>
    public void Fill(CollisionWorld world, Vector3 origin, object? part)
    {
        for (var z = 0; z < Depth; z++)
        {
            for (var x = 0; x < Width; x++)
            {
                var at = origin + new Vector3(x + 0.5f, 0, z + 0.5f);
                _cells[x * Side + z] = SpherePhysics.Sweep(world, at with { Y = origin.Y + 100 }, at with { Y = origin.Y - 100 }, 0.5f,
                    skipSeeThrough: false, out var hit, out _, part)
                    ? (byte)(int)(2 * (hit.Y - origin.Y))
                    : Taken;
            }
        }
    }

    /// <summary>
    /// Takes the <paramref name="footprint"/> × <paramref name="footprint"/> cells from (x, z) on in +X and +Z
    /// (<c>ArenaGrid.Occupy</c>, <c>0x8006820C</c>), unless any of them is outside the arena, when it takes none.
    /// </summary>
    public void Occupy(int x, int z, int footprint)
    {
        if (!Inside(x, z) || !Inside(x + footprint - 1, z + footprint - 1))
            return;
        for (var i = x; i < x + footprint; i++)
        {
            for (var j = z; j < z + footprint; j++)
            {
                _cells[i * Side + j] |= Taken;
            }
        }
    }

    /// <summary>
    /// Frees the cells <see cref="Occupy"/> takes (<c>ArenaGrid.Vacate</c>, <c>0x800682E0</c>), with the same test: none if
    /// any of them is outside. A cell the collision doesn't reach is freed with the rest, as in the original.
    /// </summary>
    public void Vacate(int x, int z, int footprint)
    {
        if (!Inside(x, z) || !Inside(x + footprint - 1, z + footprint - 1))
            return;
        for (var i = x; i < x + footprint; i++)
        {
            for (var j = z; j < z + footprint; j++)
            {
                _cells[i * Side + j] &= unchecked((byte)~Taken);
            }
        }
    }

    /// <summary>
    /// Whether any cell of the <paramref name="footprint"/> square from (x, z) on in +X and +Z is off the grid
    /// (<c>ArenaGrid.IsOutside</c>, <c>0x80067CF4</c>), the coordinates as signed bytes.
    /// </summary>
    public bool IsOutside(int x, int z, int footprint = 1)
    {
        for (var j = (sbyte)z; j < (sbyte)z + footprint; j++)
        {
            for (var i = (sbyte)x; i < (sbyte)x + footprint; i++)
            {
                if (i < 0 || j < 0 || i >= Width || j >= Depth)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether any cell of the footprint square from (x, z) is taken (<c>ArenaGrid.IsOccupied</c>, <c>0x80067BEC</c>); cells
    /// off the grid don't count.
    /// </summary>
    public bool IsOccupied(int x, int z, int footprint = 1)
    {
        for (var j = (byte)z; j < (byte)z + footprint; j++)
        {
            for (var i = (byte)x; i < (byte)x + footprint; i++)
            {
                if (!IsOutside(i, j) && (_cells[i * Side + j] & Taken) != 0)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// How much higher the floor of (x1, z1) is than (x0, z0)'s, in the grid's half units (<c>ArenaGrid.HeightDifference</c>,
    /// <c>0x80067AD4</c>): a signed byte, or 0x7F if either is off the grid.
    /// </summary>
    public int HeightDifference(int x0, int z0, int x1, int z1)
    {
        if (IsOutside(x0, z0) || IsOutside(x1, z1))
            return 0x7F;
        return (sbyte)((_cells[(byte)x1 * Side + (byte)z1] & 0x7F) - (_cells[(byte)x0 * Side + (byte)z0] & 0x7F));
    }

    /// <summary>Whether one could step from (x0, z0) to (x1, z1): it rises less than 2 (<c>ArenaGrid.IsStep</c>, <c>0x80067BBC</c>).</summary>
    public bool IsStep(int x0, int z0, int x1, int z1) => HeightDifference(x0, z0, x1, z1) < 2;

    /// <summary>
    /// Whether a line over the grid from (x0, z0) to (x1, z1) is clear (<c>ArenaGrid.WalkClear</c>, <c>0x80067E6C</c>): each cell
    /// it crosses along its longer axis is on the grid, isn't taken unless the end is a step from it, and is a step from the last
    /// unless the end isn't either.
    /// </summary>
    /// <remarks>
    /// The original steps only toward +X (a line longer in X) or +Z (longer in Z): going the other way its loop never starts,
    /// and the line is clear unchecked. The port keeps that, since it decides who can be attacked without the 3D line of sight
    /// (<see cref="Battle.CanAttack"/>).
    /// </remarks>
    public bool WalkClear(int x0, int z0, int x1, int z1)
    {
        (x0, z0, x1, z1) = ((byte)x0, (byte)z0, (byte)x1, (byte)z1);
        var dx = (int)(sbyte)(x0 - x1);
        var dz = (int)(sbyte)(z0 - z1);
        if (dx == 0 && dz == 0)
            return true;
        var xMajor = Math.Abs(dz) < Math.Abs(dx);
        var (a0, b0, a1, b1, da, db) = xMajor ? (x0, z0, x1, z1, dx, dz) : (z0, x0, z1, x1, dz, dx);
        var step = da < 0 ? 1 : -1;
        var a = (int)(sbyte)(a0 + step);
        if (a >= a1)
            return true;
        var slope = (float)db / da;
        var (prevX, prevZ) = (x0, z0);
        while (true)
        {
            var bf = slope * a + (b0 - slope * a0);
            // The float's conversion to an unsigned int, as the original's.
            if (bf >= 2147483648f)
                bf -= 2147483648f;
            var b = (int)bf;
            var (x, z) = xMajor ? (a, b) : (b, a);
            if (IsOutside((sbyte)x, (sbyte)z))
                return false;
            (x, z) = (x & 0xFF, z & 0xFF);
            if (IsOccupied(x, z) && IsStep(x, z, x1, z1))
                return false;
            if (!IsStep(prevX, prevZ, x, z) && IsStep(prevX, prevZ, x1, z1))
                return false;
            (prevX, prevZ) = (x, z);
            a = (sbyte)(a + step);
            if (a1 <= a)
                return true;
        }
    }

    /// <summary>
    /// The cell a coordinate stands in (<c>Combatant.CellX</c>, <c>CellZ</c>): rounded half away from zero, as a signed
    /// byte, as the grid's functions take it.
    /// </summary>
    public static int CellOf(float coordinate) =>
        (sbyte)(coordinate > 0 ? (byte)(int)(coordinate + 0.5f) : -(byte)(int)(0.5f - coordinate));
}
