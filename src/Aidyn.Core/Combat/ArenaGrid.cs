using System.Numerics;
using Aidyn.Core.Physics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>
/// The arena's grid (<c>Combat.Current</c>'s <c>+0x3C</c>, <c>ArenaGrid</c>, <c>0x800674A0</c>–<c>0x80068F7C</c>): 100 × 100
/// cells of a byte, a unit across in the arena chunk's own coordinates, holding the floor's height × 2 in the low seven bits
/// and whether the cell is taken in bit 7. Cells the collision doesn't reach are taken. Combatants stand on it in whole
/// cells (<see cref="Occupy"/>; freeing them as they move is session 3's), and paths and lines of sight run over it (sessions 3, 5 and 6).
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
}
