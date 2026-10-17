using System.Numerics;
using Aidyn.Core.Scenes;

namespace Aidyn.Core.Tests;

public class BillboardTests
{
    [Fact]
    public void FacesAsModelledWithTheCameraAlongPlusZ()
    {
        var m = Billboard.Align(0, new Vector3(0, 0, -5));
        AssertNear(Matrix4x4.Identity, m);
    }

    [Fact]
    public void TurnsItsFaceToTheCamera()
    {
        var position = new Vector3(10, 2, -4);
        var camera = new Vector3(3, 6, 1);
        var m = Billboard.World(position, Vector3.One, 0, camera);
        var face = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, m));
        AssertNear(Vector3.Normalize(camera - position), face);
        // Its side stays level, and its top leans back toward the camera.
        Assert.Equal(0, Vector3.TransformNormal(Vector3.UnitX, m).Y, 1e-5f);
        Assert.True(Vector3.TransformNormal(Vector3.UnitY, m).Y > 0);
        Assert.Equal(position, m.Translation);
    }

    [Fact]
    public void RollsTheCardBeforeTurningIt()
    {
        var direction = new Vector3(1, -0.5f, 2);
        var rolled = Billboard.Align(30, direction);
        var expected = Matrix4x4.CreateRotationZ(30 * MathF.PI / 180) * Billboard.Align(0, direction);
        AssertNear(expected, rolled);
    }

    [Fact]
    public void ScalesAlongTheWorldsAxesAfterTurning()
    {
        var m = Billboard.World(Vector3.Zero, new Vector3(2, 1, 1), 0, new Vector3(-1, 0, 0));
        // The camera is along −X, so the card's +Z faces −X, and its +X is along +Z, which the X scale leaves alone.
        AssertNear(new Vector3(0, 0, 1), Vector3.TransformNormal(Vector3.UnitX, m));
        AssertNear(new Vector3(-2, 0, 0), Vector3.TransformNormal(Vector3.UnitZ, m));
    }

    [Fact]
    public void StraightAboveOrBelowItGivesTheIdentity() =>
        AssertNear(Matrix4x4.Identity, Billboard.Align(45, new Vector3(0, -3, 0)));

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-5, $"Expected {expected}, got {actual}.");

    private static void AssertNear(Matrix4x4 expected, Matrix4x4 actual)
    {
        for (var r = 0; r < 4; r++)
            for (var c = 0; c < 4; c++)
                Assert.Equal(expected[r, c], actual[r, c], 1e-5f);
    }
}

public class TextureScrollTests
{
    [Fact]
    public void ShiftsByTheHighNibbleEveryLoadWhenTheLowIsZero()
    {
        var scroll = new TextureScroll(32, 0x30);
        scroll.Load();
        scroll.Load();
        Assert.Equal((6, (byte)0x30), (scroll.Offset, scroll.Move));
    }

    [Fact]
    public void CountsLoadsUpToTheLowNibbleThenShiftsByIt()
    {
        var scroll = new TextureScroll(32, 0x02);
        var offsets = new List<int>();
        for (var i = 0; i < 7; i++)
        {
            scroll.Load();
            offsets.Add(scroll.Offset);
        }
        Assert.Equal([0, 0, 2, 2, 2, 4, 4], offsets);
        Assert.Equal(0x12, scroll.Move);
    }

    [Fact]
    public void WrapsRoundTheWidth()
    {
        var scroll = new TextureScroll(32, 0x80);
        for (var i = 0; i < 5; i++)
            scroll.Load();
        Assert.Equal(8, scroll.Offset);
    }
}

public class ChunkViewTests
{
    private static readonly Vector2 Cell = new(100, 100);

    [Fact]
    public void AlwaysDrawsTheCentreChunk() =>
        Assert.True(ChunkView.Drawn(new Vector2(50, 50), Vector2.UnitX, 0, 0, Cell));

    [Fact]
    public void DrawsTheChunksAheadAndSkipsThoseBehind()
    {
        var camera = new Vector2(50, 50);
        Assert.True(ChunkView.Drawn(camera, Vector2.UnitX, 1, 0, Cell));
        Assert.True(ChunkView.Drawn(camera, Vector2.UnitX, 1, 1, Cell));
        Assert.True(ChunkView.Drawn(camera, Vector2.UnitX, 1, -1, Cell));
        Assert.False(ChunkView.Drawn(camera, Vector2.UnitX, -1, 0, Cell));
        Assert.False(ChunkView.Drawn(camera, Vector2.UnitX, -1, 1, Cell));
        // 42.5° either side of +X reaches the chunks beside the centre one.
        Assert.True(ChunkView.Drawn(camera, Vector2.UnitX, 0, 1, Cell));
    }

    [Fact]
    public void DrawsAChunkBehindTheCameraWithinItsMargin()
    {
        // 10 from the edge with the chunk behind: the camera itself is inside that chunk grown by 20.
        Assert.True(ChunkView.Drawn(new Vector2(10, 50), Vector2.UnitX, -1, 0, Cell));
    }

    [Fact]
    public void DrawsTheNeighboursTheCentresMapForcesWhateverTheView()
    {
        var camera = new Vector2(50, 50);
        // Looking along +X, the chunk behind (row −1) isn't in view, until the map forces it: West, bit 0x40.
        Assert.False(ChunkView.Drawn(camera, Vector2.UnitX, -1, 0, Cell, Plugins.Records.Neighbours.East | Plugins.Records.Neighbours.SouthWest));
        Assert.True(ChunkView.Drawn(camera, Vector2.UnitX, -1, 0, Cell, Plugins.Records.Neighbours.West));
        Assert.True(ChunkView.Drawn(camera, Vector2.UnitX, -1, 1, Cell, Plugins.Records.Neighbours.SouthWest));
        // The bits in the order of the original's table at 0x800CD700.
        Assert.Equal([0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80],
            new[] { (-1, -1), (1, -1), (-1, 1), (1, 1), (0, -1), (0, 1), (-1, 0), (1, 0) }.Select(n => (int)ChunkView.Neighbour(n.Item1, n.Item2)));
    }
}
