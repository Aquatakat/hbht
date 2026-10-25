using System.Numerics;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>The arena's grid (<c>ArenaGrid.Init</c> <c>0x80067960</c>, <c>Fill</c> <c>0x8005E900</c>, <c>Occupy</c> <c>0x8006820C</c>).</summary>
public sealed class ArenaGridTests
{
    private static MapRecord MapOf(float cellX, float cellZ, int columns, int rows) =>
        new(RecordId.Hex(RecordTypes.Map, 1), [], new ObjectGrid(Vector2.Zero, new Vector2(cellX, cellZ), columns, rows, []));

    private static ArenaGrid Sized(int width, int depth)
    {
        var grid = new ArenaGrid();
        grid.Init(MapOf(1, 1, width, depth));
        return grid;
    }

    [Fact]
    public void TheGridIsSizedByTheObjectGridsCellSizeTimesItsColumnsAndRows()
    {
        var grid = new ArenaGrid();

        // Arena 2 of the original: 85 x 98, area 8330, halves 42 and 49, a diagonal of trunc(129.7) = 129.
        grid.Init(MapOf(5, 7, 17, 14));

        Assert.Equal(85, grid.Width);
        Assert.Equal(98, grid.Depth);
        Assert.Equal(8330, grid.Area);
        Assert.Equal((42, 49), grid.Half);
        Assert.Equal(129, grid.Diagonal);
    }

    [Fact]
    public void TheSizesRoundHalfAwayFromZero()
    {
        var grid = new ArenaGrid();

        // 0.5 x 5 = 2.5 rounds to 3 (not to the even 2), and 1.5 x 3 = 4.5 to 5.
        grid.Init(MapOf(0.5f, 1.5f, 5, 3));

        Assert.Equal((3, 5), (grid.Width, grid.Depth));
    }

    [Fact]
    public void AMapWithNoObjectGridHasNoSize()
    {
        var grid = Sized(4, 4);

        grid.Init(new MapRecord(RecordId.Hex(RecordTypes.Map, 2), []));

        Assert.Equal((0, 0), (grid.Width, grid.Depth));
        Assert.False(grid.Inside(0, 0));
    }

    [Fact]
    public void InitEmptiesTheGrid()
    {
        var grid = Sized(4, 4);
        grid.Occupy(1, 1, 2);

        grid.Init(MapOf(1, 1, 4, 4));

        Assert.All(Cells(grid, 4, 4), b => Assert.Equal(0, b));
    }

    private static IEnumerable<byte> Cells(ArenaGrid grid, int width, int depth) =>
        from x in Enumerable.Range(0, width) from z in Enumerable.Range(0, depth) select grid[x, z];

    [Fact]
    public void InsideIsTheWidthAndDepth()
    {
        var grid = Sized(3, 5);

        Assert.True(grid.Inside(0, 0));
        Assert.True(grid.Inside(2, 4));
        Assert.False(grid.Inside(3, 0));
        Assert.False(grid.Inside(0, 5));
        Assert.False(grid.Inside(-1, 0));
        Assert.False(grid.Inside(0, -1));
    }

    // Occupy: grid[x * 100 + z] |= 0x80 over the footprint going +x and +z, only if every cell is inside.
    [Fact]
    public void OccupyTakesTheFootprintGoingPlusXAndPlusZ()
    {
        var grid = Sized(5, 5);

        grid.Occupy(1, 2, 2);

        var taken = new HashSet<(int, int)> { (1, 2), (2, 2), (1, 3), (2, 3) };
        for (var x = 0; x < 5; x++)
        {
            for (var z = 0; z < 5; z++)
                Assert.Equal(taken.Contains((x, z)) ? ArenaGrid.Taken : 0, grid[x, z]);
        }
    }

    [Theory]
    [InlineData(4, 4, 2)] // the far corner is outside
    [InlineData(4, 0, 2)]
    [InlineData(0, 4, 2)]
    [InlineData(-1, 0, 1)]
    [InlineData(0, -1, 1)]
    [InlineData(5, 0, 1)]
    [InlineData(0, 0, 6)]
    public void OccupyDoesNothingIfAnyCellIsOutside(int x, int z, int footprint)
    {
        var grid = Sized(5, 5);

        grid.Occupy(x, z, footprint);

        Assert.All(Cells(grid, 5, 5), b => Assert.Equal(0, b));
    }

    [Fact]
    public void OccupyReachesTheLastCell()
    {
        var grid = Sized(5, 5);

        grid.Occupy(4, 4, 1);

        Assert.Equal(ArenaGrid.Taken, grid[4, 4]);
    }

    // Fill: a sphere of 0.5 swept from 100 down to -100 through (x + 0.5, z + 0.5); the cell holds trunc(2 * hit height).
    private static CollisionWorld FloorWorld(float height, float maxX, float maxZ, Vector3 origin = default, ChunkKey? key = null)
    {
        var world = new CollisionWorld();
        world.Add(key ?? Worlds.Chunk(0), Worlds.Floor(height, 0, maxX, 0, maxZ), origin);
        return world;
    }

    [Fact]
    public void AFlatFloorAtFourGivesEightAndACellOffTheFloorIsTaken()
    {
        // 3 wide and 4 deep, with floor only across the first unit of X: cell 0 is over it, cell 2 well clear. (Its two triangles
        // meet along x / 1 + z / 4 = 1, which no cell centre is on: a centre exactly on the diagonal falls through.)
        var grid = Sized(3, 4);
        var world = FloorWorld(4, 1, 4);

        grid.Fill(world, Vector3.Zero, Worlds.Chunk(0));

        for (var z = 0; z < 4; z++)
        {
            Assert.Equal(8, grid[0, z]);
            Assert.Equal(ArenaGrid.Taken, grid[2, z]);
        }
    }

    [Fact]
    public void TheHeightIsTwiceTheFloorTruncated()
    {
        var grid = Sized(2, 2);
        var world = FloorWorld(4.3f, 20, 23);

        grid.Fill(world, Vector3.Zero, Worlds.Chunk(0));

        // 2 x 4.3 = 8.6, truncated to 8.
        Assert.All(Cells(grid, 2, 2), b => Assert.Equal(8, b));
    }

    [Fact]
    public void TheHeightIsTakenFromTheChunksOrigin()
    {
        var grid = Sized(2, 2);
        var origin = new Vector3(450, 10, 300);
        var world = FloorWorld(3.2f, 20, 23, origin);

        grid.Fill(world, origin, Worlds.Chunk(0));

        // The floor is at 13.2 in the world, 3.2 above the chunk's origin: 2 x 3.2 = 6.4, truncated to 6.
        Assert.All(Cells(grid, 2, 2), b => Assert.Equal(6, b));
    }

    [Fact]
    public void OnlyTheArenaChunksTrianglesCount()
    {
        var grid = Sized(2, 2);
        var world = FloorWorld(4, 20, 23, key: Worlds.Chunk(1));

        grid.Fill(world, Vector3.Zero, Worlds.Chunk(0));

        Assert.All(Cells(grid, 2, 2), b => Assert.Equal(ArenaGrid.Taken, b));
    }

    [Fact]
    public void OccupyingAFilledCellKeepsItsHeight()
    {
        var grid = Sized(2, 2);
        grid.Fill(FloorWorld(4, 20, 23), Vector3.Zero, Worlds.Chunk(0));

        grid.Occupy(0, 0, 1);

        // 0x88: bad2a's cell in the original, floor 4.0856 -> 8, occupied.
        Assert.Equal(0x88, grid[0, 0]);
        Assert.Equal(8, grid[1, 1]);
    }

    [Fact]
    public void AFillOnlyCoversTheWidthAndDepth()
    {
        var grid = Sized(2, 2);

        grid.Fill(FloorWorld(4, 20, 23), Vector3.Zero, Worlds.Chunk(0));

        Assert.Equal(0, grid[2, 0]);
        Assert.Equal(0, grid[0, 2]);
        Assert.Equal(0, grid[50, 50]);
    }
}
