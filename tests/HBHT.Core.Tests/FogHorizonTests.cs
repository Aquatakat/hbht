using System.Numerics;
using HBHT.Core.Scenes;
using HBHT.Plugins;

namespace HBHT.Core.Tests;

/// <summary>Draw Distance's Extended pulls the fog in to the edge of the cells drawn, and lets it out as a new cell comes in.</summary>
public sealed class FogHorizonTests
{
    private const float Size = 100, Floor = 180, Radius = 300;
    private static readonly RecordId Overworld = RecordId.Parse("aidyn:mapset/0");

    /// <summary>The 5×5 round cell (10, 10), less any cells left out.</summary>
    private static List<(int Row, int Column)> FiveByFive(params (int Row, int Column)[] without)
    {
        var cells = new List<(int, int)>();
        for (var r = 8; r <= 12; r++)
            for (var c = 8; c <= 12; c++)
                if (!without.Contains((r, c)))
                    cells.Add((r, c));
        return cells;
    }

    /// <summary>The middle of cell (10, 10).</summary>
    private static readonly Vector2 Middle = new(1050, 1050);

    /// <summary>The sector pointing along +X (row up), +Z (column up), −X and −Z.</summary>
    private const int PlusX = 0, PlusZ = FogHorizon.Sectors / 4, MinusX = FogHorizon.Sectors / 2, MinusZ = 3 * FogHorizon.Sectors / 4;

    private static FogHorizon Settled(List<(int, int)> drawn, Vector2 eye)
    {
        var horizon = new FogHorizon();
        horizon.Step(1 / 60f, Overworld, drawn, Size, eye, eye, Floor, Radius);
        return horizon;
    }

    [Fact]
    public void TheWholeFiveByFiveReachesTheSphereEveryWayButStraightOutFromItsMiddle()
    {
        var horizon = Settled(FiveByFive(), Middle);
        // Straight out the 5×5 reaches 250; corner to corner, about 354, past the sphere.
        Assert.Equal(250, horizon.Edges[PlusX], 1);
        Assert.Equal(250, horizon.Edges[MinusZ], 1);
        Assert.Equal(Radius, horizon.Edges[FogHorizon.Sectors / 8], 1);
    }

    [Fact]
    public void AMissingCellPullsTheFogInToItsNearEdgeButNoNearerThanTheOriginals()
    {
        // From the middle cell's far side, (10, 11) missing is 5 away: the floor.
        var eye = new Vector2(1050, 1095);
        var horizon = Settled(FiveByFive((10, 11)), eye);
        Assert.Equal(Floor, horizon.Edges[PlusZ], 1);
        // (12, 10) missing, 150 along +X from the middle: under the floor too. (8, 10)'s further edge, 250 along −X, stays.
        horizon = Settled(FiveByFive((12, 10)), Middle);
        Assert.Equal(Floor, horizon.Edges[PlusX], 1);
        Assert.Equal(250, horizon.Edges[MinusX], 1);
    }

    [Fact]
    public void TheEdgesComeInRoundAMissingCellByDegreesNotAlongALine()
    {
        var horizon = Settled(FiveByFive((12, 10)), Middle);
        // A sector away, the edge differs from its neighbour's by no more than MaxTurn a radian.
        var most = FogHorizon.MaxTurn * MathF.Tau / FogHorizon.Sectors + 0.01f;
        for (var i = 0; i < FogHorizon.Sectors; i++)
            Assert.True(MathF.Abs(horizon.Edges[i] - horizon.Edges[(i + 1) % FogHorizon.Sectors]) <= most, $"sector {i}");
    }

    [Fact]
    public void ANewRowComesInOutOfTheFogAtTheRevealSpeedOverThePlayers()
    {
        // Standing in (10, 10)'s far half, 210 from the 5×5's edge along +X, with the row past it still to come.
        var eye = new Vector2(1090, 1050);
        var horizon = new FogHorizon();
        // Settled since it was loaded.
        horizon.Step(1 / 60f, Overworld, FiveByFive(), Size, eye, eye, Floor, Radius);
        for (var i = 0; i < 31; i++)
            horizon.Step(0.1f, Overworld, FiveByFive(), Size, eye, eye, Floor, Radius);
        Assert.Equal(210, horizon.Edges[PlusX], 1);
        var withRow = FiveByFive().Concat(Enumerable.Range(8, 5).Select(c => (13, c))).ToList();
        horizon.Step(1 / 60f, Overworld, withRow, Size, eye, eye, Floor, Radius);
        Assert.Equal(0, horizon.Revealed(13, 10));
        Assert.Equal(210, horizon.Edges[PlusX], 1);
        // Standing still, it moves back at RevealSpeed alone.
        for (var i = 0; i < 600; i++)
            horizon.Step(1 / 60f, Overworld, withRow, Size, eye, eye, Floor, Radius);
        Assert.Equal(210 + 10 * FogHorizon.RevealSpeed, horizon.Edges[PlusX], 0);
        Assert.Equal(10 * FogHorizon.RevealSpeed / Size, horizon.Revealed(13, 10), 2);
    }

    [Fact]
    public void TheScenerySettlingInAfterAJumpIsRevealedAtOnce()
    {
        // The 3×3 at once, then the rest of the 5×5 as it's built, a second later.
        var horizon = new FogHorizon();
        var threeByThree = FiveByFive().Where(c => Math.Abs(c.Row - 10) <= 1 && Math.Abs(c.Column - 10) <= 1).ToList();
        horizon.Step(1 / 60f, Overworld, threeByThree, Size, Middle, Middle, Floor, Radius);
        Assert.Equal(Floor, horizon.Edges[PlusX], 1);
        // A long first frame doesn't count for more than a tenth of a second.
        horizon.Step(5, Overworld, FiveByFive().Where(c => c.Row < 12).ToList(), Size, Middle, Middle, Floor, Radius);
        // Each cell that comes in keeps it settling a while longer.
        for (var i = 0; i < 25; i++)
            horizon.Step(0.1f, Overworld, FiveByFive().Where(c => c.Row < 12).ToList(), Size, Middle, Middle, Floor, Radius);
        horizon.Step(0.1f, Overworld, FiveByFive(), Size, Middle, Middle, Floor, Radius);
        Assert.Equal(1, horizon.Revealed(12, 10));
        Assert.Equal(250, horizon.Edges[PlusX], 1);
    }

    [Fact]
    public void ACellThatGoesIsGoneFromTheEdgeAtOnce()
    {
        var horizon = Settled(FiveByFive(), Middle);
        horizon.Step(1 / 60f, Overworld, FiveByFive((12, 10)), Size, Middle, Middle, Floor, Radius);
        Assert.Equal(Floor, horizon.Edges[PlusX], 1);
        Assert.Equal(0, horizon.Revealed(12, 10));
    }

    [Fact]
    public void AJumpOrAnotherMapSetRevealsEveryCellAtOnce()
    {
        var horizon = Settled(FiveByFive(), Middle);
        var far = new Vector2(5050, 5050);
        var there = new List<(int, int)> { (50, 50), (50, 51), (51, 50) };
        horizon.Step(1 / 60f, Overworld, there, Size, far, far, Floor, Radius);
        Assert.Equal(1, horizon.Revealed(50, 51));
        horizon.Step(1 / 60f, RecordId.Parse("aidyn:mapset/1"), [.. there, (49, 50)], Size, far, far, Floor, Radius);
        Assert.Equal(1, horizon.Revealed(49, 50));
    }

    [Fact]
    public void EasingOnlyEverBringsAnEdgeIn()
    {
        var edges = new float[16];
        Array.Fill(edges, 300f);
        edges[0] = 100;
        var raw = (float[])edges.Clone();
        FogHorizon.Ease(edges, 50);
        for (var i = 0; i < edges.Length; i++)
            Assert.True(edges[i] <= raw[i]);
        // Round the circle both ways from the dip, wrapping past the start.
        Assert.Equal(150, edges[1]);
        Assert.Equal(150, edges[15]);
        Assert.Equal(200, edges[2]);
        Assert.Equal(200, edges[14]);
    }
}
