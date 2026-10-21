using System.Numerics;
using Aidyn.Core.Chunks;
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

    /// <summary>The cells as <see cref="Fill"/> left them, before anyone stood on them (<c>RefillWithBodies</c>' sweep).</summary>
    private readonly byte[] _filled = new byte[Side * Side];

    /// <summary>The path marks (the grid's second layer, <c>+10000</c>), which <see cref="FindPath"/> sets on the cells it has walked.</summary>
    private readonly bool[] _marks = new bool[Side * Side];

    /// <summary>The eight directions' steps in X and Z, N, NE, E, SE, S, SW, W, NW (<c>0x800D2BD0</c>, <c>0x800D2BD8</c>).</summary>
    public static readonly (int X, int Z)[] Steps = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

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
    public void Fill(CollisionWorld world, Vector3 origin, ChunkKey? part)
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
        Array.Copy(_cells, _filled, _cells.Length);
    }

    /// <summary>Sets a cell's byte, for tests that lay a checkpoint's grid over the port's.</summary>
    internal void Set(int x, int z, byte value) => _cells[x * Side + z] = value;

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
    /// Whether a footprint from (x, z) is on the grid and none of it is taken (<c>ArenaGrid.IsFree</c>).
    /// </summary>
    public bool IsFree(int x, int z, int footprint) => !IsOutside(x, z, footprint) && !IsOccupied(x, z, footprint);

    /// <summary>
    /// Whether one could walk from (x0, z0) to (x1, z1): it rises less than 3 half units (<c>ArenaGrid.IsGentle</c>), the path
    /// finder's and the AI's walk's test, a little looser than <see cref="IsStep"/>.
    /// </summary>
    public bool IsGentle(int x0, int z0, int x1, int z1) => HeightDifference(x0, z0, x1, z1) < 3;

    /// <summary>
    /// The distance between two cells (<c>ArenaGrid.Distance</c>, <c>0x800674E4</c>): the byte differences squared and summed
    /// as a short, its square root truncated, a u16.
    /// </summary>
    public static int Distance(int x0, int z0, int x1, int z1)
    {
        var root = MathF.Sqrt(DistanceSquared(x0, z0, x1, z1));
        return (ushort)(uint)root;
    }

    /// <summary>The distance squared (<c>0x800674A0</c>), a short of the byte differences as shorts.</summary>
    private static short DistanceSquared(int x0, int z0, int x1, int z1)
    {
        var dx = (short)((x0 & 0xFF) - (x1 & 0xFF));
        var dz = (short)((z0 & 0xFF) - (z1 & 0xFF));
        return (short)(dx * dx + dz * dz);
    }

    /// <summary>
    /// The direction 0–7 (N round to NW) from one cell to another (<c>ArenaGrid.DirectionTo</c>, <c>0x80068F08</c>), by the signs
    /// of the byte differences; north for the same cell.
    /// </summary>
    public static int DirectionTo(int x, int z, int tx, int tz)
    {
        (x, z, tx, tz) = (x & 0xFF, z & 0xFF, tx & 0xFF, tz & 0xFF);
        if (x < tx)
            return tz > z ? 3 : tz == z ? 2 : 1;
        if (tx < x)
            return tz > z ? 5 : tz == z ? 6 : 7;
        return tz > z ? 4 : 0;
    }

    /// <summary>
    /// The grid as it was filled, with each combatant given standing where it was last read (<c>ArenaGrid.RefillWithBodies</c>,
    /// which sweeps the collision again: the same, as it doesn't move). The path finder's last step.
    /// </summary>
    public void Refill(IEnumerable<(int X, int Z, int Footprint)> bodies)
    {
        Array.Copy(_filled, _cells, _cells.Length);
        foreach (var (x, z, footprint) in bodies)
            Occupy(x, z, footprint);
    }

    /// <summary>
    /// A path from (sx, sz) to (gx, gz) for a footprint (<c>ArenaGrid.FindPath</c>, <c>0x80068CD4</c>): both footprints are freed,
    /// then from the start it steps to the free, unwalked, gentle neighbour nearest the goal (by the distance squared, the first of
    /// equals in the directions' order), marking each, until the goal or the diagonal's length. It never searches: at a dead end it
    /// counts back without stepping back, so any pocket fails it. Then the caller's grid is filled again
    /// (<see cref="Refill"/>), and a path found is cut to its corners (<see cref="StraightenPath"/>).
    /// </summary>
    /// <param name="path">The cells, the corners first and the goal last; it holds at least the diagonal's length.</param>
    /// <returns>The length: 0 if the start is the goal, and <see cref="Diagonal"/> if no path was found.</returns>
    public int FindPath(int sx, int sz, int gx, int gz, int footprint, (int X, int Z)[] path, IEnumerable<(int X, int Z, int Footprint)> bodies)
    {
        var diagonal = Diagonal;
        for (var x = 0; x < Width; x++)
            Array.Clear(_marks, x * Side, Depth);
        Vacate(sx, sz, footprint);
        Vacate(gx, gz, footprint);
        var (cx, cz) = ((int)(sbyte)sx, (int)(sbyte)sz);
        (gx, gz) = (gx & 0xFF, gz & 0xFF);
        var length = 0;
        while ((cx & 0xFF) != gx || (cz & 0xFF) != gz)
        {
            if (length >= diagonal)
                break;
            // ArenaGrid.Neighbours (0x80068B3C) and BestNeighbour (0x80068C94): the first least.
            var best = -1;
            var least = int.MaxValue;
            for (var d = 0; d < Steps.Length; d++)
            {
                var (nx, nz) = ((int)(sbyte)(cx + Steps[d].X), (int)(sbyte)(cz + Steps[d].Z));
                if (!IsFree(nx, nz, footprint) || Marked(nx, nz) || !IsGentle(cx, cz, nx, nz))
                    continue;
                var score = (ushort)DistanceSquared(nx, nz, gx, gz);
                if (score < least)
                {
                    least = score;
                    best = d;
                }
            }
            if (best >= 0)
            {
                (cx, cz) = ((sbyte)(cx + Steps[best].X), (sbyte)(cz + Steps[best].Z));
                path[length++] = (cx & 0xFF, cz & 0xFF);
                _marks[(sbyte)cx * Side + (sbyte)cz] = true;
            }
            else if (length == 0)
            {
                length = diagonal;
            }
            else
            {
                // The walk stays where it is, in its dead end, and counts back to a failure.
                length--;
            }
        }
        Refill(bodies);
        if (length != diagonal && length != 0)
            length = StraightenPath(sx, sz, length, path);
        return length;

        bool Marked(int x, int z) => _marks[(sbyte)x * Side + (sbyte)z];
    }

    /// <summary>
    /// Takes the loops out of a path (<c>ArenaGrid.TrimPath</c>, <c>0x80068784</c>): from each cell in turn, the start first, the
    /// first of its neighbours in the directions' order that the path reaches again later, looked for from the path's end, cuts
    /// the cells between.
    /// </summary>
    private static int TrimPath(int sx, int sz, int length, (int X, int Z)[] path)
    {
        var i = 0;
        var (cx, cz) = ((int)(sbyte)sx, (int)(sbyte)sz);
        if (length - 1 <= 0)
            return length;
        int j;
        do
        {
            var found = false;
            j = length - 1;
            for (var d = 0; d < Steps.Length && !found; d++)
            {
                var (nx, nz) = ((sbyte)(cx + Steps[d].X), (sbyte)(cz + Steps[d].Z));
                var jj = (short)j;
                if (i >= jj)
                    continue;
                while (true)
                {
                    if ((sbyte)path[jj].X == nx && (sbyte)path[jj].Z == nz)
                    {
                        var cut = jj - i;
                        length -= cut;
                        for (var q = i; q < length; q++)
                            path[q] = path[q + cut];
                        found = true;
                        j = length - 1;
                        break;
                    }
                    jj--;
                    if (jj <= i)
                        break;
                }
            }
            (cx, cz) = ((sbyte)path[i].X, (sbyte)path[i].Z);
            i++;
        }
        while (i < j);
        return length;
    }

    /// <summary>
    /// A path cut to its corners (<c>ArenaGrid.StraightenPath</c>, <c>0x8006894C</c>), once its loops are out
    /// (<see cref="TrimPath"/>): of each run of cells in one direction, only its last is kept, so the AI walks straight from
    /// corner to corner. The goal stays last.
    /// </summary>
    private static int StraightenPath(int sx, int sz, int length, (int X, int Z)[] path)
    {
        var (px, pz) = (sx & 0xFF, sz & 0xFF);
        var i = 0;
        length = TrimPath(sx, sz, length, path);
        if (length - 1 <= 0)
            return length;
        do
        {
            var (cx, cz) = path[i];
            var d0 = DirectionTo(px, pz, cx, cz);
            var j = i;
            while (j < length - 1)
            {
                if (d0 != DirectionTo(cx, cz, path[j + 1].X, path[j + 1].Z))
                    break;
                j = (short)(j + 1);
                d0 = DirectionTo(cx, cz, path[j].X, path[j].Z);
                (cx, cz) = path[j];
            }
            length += i - j;
            for (var q = i; q < length; q++)
                path[q] = path[j + (q - i)];
            i++;
            (px, pz) = path[i - 1];
        }
        while (i < length - 1);
        return length;
    }

    /// <summary>
    /// The cell a coordinate stands in (<c>Combatant.CellX</c>, <c>CellZ</c>): rounded half away from zero, as a signed
    /// byte, as the grid's functions take it.
    /// </summary>
    public static int CellOf(float coordinate) =>
        (sbyte)(coordinate > 0 ? (byte)(int)(coordinate + 0.5f) : -(byte)(int)(0.5f - coordinate));
}
