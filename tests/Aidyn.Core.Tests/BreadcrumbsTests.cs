using System.Numerics;
using Aidyn.Core.Trek;
using Aidyn.Plugins;

namespace Aidyn.Core.Tests;

/// <summary>
/// The places the player stood in trek, kept for fleeing a battle (<c>Zones.Breadcrumbs</c> <c>0x800D90E0</c>,
/// <c>HandleZoneEngineFrame</c> <c>0x8000E784</c>, <c>Zones.FindBreadcrumb</c> <c>0x80009A48</c>).
/// </summary>
public sealed class BreadcrumbsTests
{
    private static readonly RecordId Overworld = RecordId.Decimal(RecordTypes.MapSet, 0);
    private static readonly RecordId Interiors = RecordId.Decimal(RecordTypes.MapSet, 1);

    private static Breadcrumb At(float x, float z = 0, int row = 0, int column = 0, RecordId? mapSet = null) =>
        new(mapSet ?? Overworld, row, column, new Vector3(x, 0, z), Vector2.UnitX);

    /// <summary>
    /// Ticks <see cref="Breadcrumbs.Ticks"/> times at <paramref name="place"/>. The first of them is the one that tests, as
    /// the last such batch left the timer at one (or it was just reset).
    /// </summary>
    private static void Stay(Breadcrumbs crumbs, Breadcrumb place)
    {
        for (var i = 0; i < Breadcrumbs.Ticks; i++)
            crumbs.Tick(place);
    }

    private static Breadcrumbs From(Breadcrumb start)
    {
        var crumbs = new Breadcrumbs();
        crumbs.Reset(start);
        return crumbs;
    }

    [Fact]
    public void ResetPutsAllFifteenWhereThePlayerStands()
    {
        var crumbs = new Breadcrumbs();
        var here = At(5, 6, row: 3, column: 4);

        crumbs.Reset(here);

        Assert.Equal(15, Breadcrumbs.Count);
        Assert.Equal(15, crumbs.All.Count);
        Assert.All(crumbs.All, c => Assert.Equal(here, c));
    }

    [Fact]
    public void TheFirstTickAfterAResetTestsAtOnce()
    {
        var crumbs = From(At(0));

        crumbs.Tick(At(20));

        Assert.Equal(At(20), crumbs.All[0]);
        Assert.Equal(14, crumbs.All.Count(c => c == At(0)));
    }

    [Fact]
    public void TheFirstTickAtTheSpotOfTheResetKeepsNothing()
    {
        var crumbs = From(At(0));

        crumbs.Tick(At(3));

        Assert.All(crumbs.All, c => Assert.Equal(At(0), c));
    }

    [Fact]
    public void ACrumbIsKeptOnlyEvery120Ticks()
    {
        var crumbs = From(At(0));
        crumbs.Tick(At(20));

        // 119 ticks later nothing is kept, wherever the player is.
        for (var i = 0; i < Breadcrumbs.Ticks - 1; i++)
            crumbs.Tick(At(100));
        Assert.Equal(At(0), crumbs.All[1]);

        // The 120th tick after the last test is the next test.
        crumbs.Tick(At(100));
        Assert.Equal(At(100), crumbs.All[1]);
    }

    [Fact]
    public void ASkippedTestStillWaitsAFull120Ticks()
    {
        var crumbs = From(At(0));
        crumbs.Tick(At(2)); // within 10 of a crumb: skipped, but the timer restarts

        for (var i = 0; i < Breadcrumbs.Ticks - 1; i++)
            crumbs.Tick(At(100));
        Assert.All(crumbs.All, c => Assert.Equal(At(0), c));

        crumbs.Tick(At(100));
        Assert.Equal(At(100), crumbs.All[0]);
    }

    // `near != null and distance3D(near.pos, player.pos) < 10.0`: strictly under 10.
    [Fact]
    public void APlaceTenAwayFromTheNearestIsKept()
    {
        var crumbs = From(At(0));

        crumbs.Tick(At(10));

        Assert.Equal(At(10), crumbs.All[0]);
    }

    [Fact]
    public void APlaceUnderTenAwayIsNotKept()
    {
        var crumbs = From(At(0));

        crumbs.Tick(At(9.99f));

        Assert.All(crumbs.All, c => Assert.Equal(At(0), c));
    }

    [Fact]
    public void TheDistanceIsInThreeDimensions()
    {
        var crumbs = From(At(0));
        var up = new Breadcrumb(Overworld, 0, 0, new Vector3(0, 12, 0), Vector2.UnitX);

        crumbs.Tick(up);

        Assert.Equal(up, crumbs.All[0]);
    }

    // The test compares the plain positions within their chunks, as though both were in the same one.
    [Fact]
    public void TheTestForNearbyComparesPositionsWithinTheirChunksNotTheGlobalOnes()
    {
        var crumbs = From(At(5, 5, row: 0, column: 0));

        // A whole chunk away (100) but at the same place in its chunk: the original skips it.
        crumbs.Tick(At(5, 5, row: 1, column: 0));

        Assert.All(crumbs.All, c => Assert.Equal(At(5, 5), c));
    }

    [Fact]
    public void APlaceInAnotherMapSetHasNothingNearAndIsKept()
    {
        var crumbs = From(At(5));
        var inside = At(5, mapSet: Interiors);

        crumbs.Tick(inside);

        Assert.Equal(inside, crumbs.All[0]);
    }

    [Fact]
    public void TheRingKeepsTheLastFifteenAndOverwritesTheOldest()
    {
        var crumbs = From(At(0));

        // 16 places 20 apart: the first 15 fill the ring, the 16th takes the first one's place.
        for (var k = 1; k <= 16; k++)
            Stay(crumbs, At(20 * k));

        Assert.Equal(At(320), crumbs.All[0]);
        for (var i = 1; i < 15; i++)
            Assert.Equal(At(20 * (i + 1)), crumbs.All[i]);
    }

    [Fact]
    public void ResetStartsTheRingAndTheTimerOver()
    {
        var crumbs = From(At(0));
        Stay(crumbs, At(20));
        Stay(crumbs, At(40));

        crumbs.Reset(At(100));
        crumbs.Tick(At(120));

        Assert.Equal(At(120), crumbs.All[0]);
        Assert.Equal(14, crumbs.All.Count(c => c == At(100)));
    }

    // Zones.FindBreadcrumb: only crumbs in the same map set; nearest takes the last of equals, the farthest the last of equals.
    [Fact]
    public void FindIsNullWhenNoneIsInTheMapSet()
    {
        var crumbs = From(At(0));

        Assert.Null(crumbs.Find(At(0, mapSet: Interiors), farthest: false));
        Assert.Null(crumbs.Find(At(0, mapSet: Interiors), farthest: true));
    }

    [Fact]
    public void FindIgnoresCrumbsOfOtherMapSetsEvenWhenTheyAreNearest()
    {
        var crumbs = From(At(500));
        var inside = At(0, mapSet: Interiors);
        crumbs.Tick(inside);
        Assert.Contains(inside, crumbs.All);

        Assert.Equal(At(500), crumbs.Find(At(0), farthest: false));
        Assert.Equal(At(500), crumbs.Find(At(0), farthest: true));
        Assert.Equal(inside, crumbs.Find(At(0, mapSet: Interiors), farthest: false));
    }

    [Fact]
    public void TheNearestOfEqualsIsTheLaterInTheRing()
    {
        var east = At(10);
        var west = At(-10);

        var eastFirst = From(At(500));
        eastFirst.Tick(east);
        Stay(eastFirst, west);
        Assert.Equal(east, eastFirst.All[0]);
        Assert.Equal(west, eastFirst.All[1]);

        var westFirst = From(At(500));
        westFirst.Tick(west);
        Stay(westFirst, east);

        Assert.Equal(west, eastFirst.Find(At(0), farthest: false));
        Assert.Equal(east, westFirst.Find(At(0), farthest: false));
    }

    [Fact]
    public void TheFarthestOfEqualsIsTheLaterInTheRing()
    {
        var east = At(100);
        var west = At(-100);

        var eastFirst = From(At(5));
        eastFirst.Tick(east);
        Stay(eastFirst, west);
        Assert.Equal(east, eastFirst.All[0]);
        Assert.Equal(west, eastFirst.All[1]);

        var westFirst = From(At(5));
        westFirst.Tick(west);
        Stay(westFirst, east);

        Assert.Equal(west, eastFirst.Find(At(0), farthest: true));
        Assert.Equal(east, westFirst.Find(At(0), farthest: true));
    }

    [Fact]
    public void FarthestAlwaysFindsOneWhenAnyIsInTheMapSet()
    {
        // All 15 are at the place asked from: the distance 0 is not under the starting 0 of the search, so it counts.
        var crumbs = From(At(7));

        Assert.Equal(At(7), crumbs.Find(At(7), farthest: true));
        Assert.Equal(At(7), crumbs.Find(At(7), farthest: false));
    }

    // A chunk is a literal 100 away a row or column, whatever the map set's cell size.
    [Fact]
    public void AChunkAwayIsAHundredPerRowAndPerColumn()
    {
        // From the origin of chunk (0, 0): the start crumb is 50 off in Z, one kept is a row on and 60 back in X, a global distance of 40.
        var crumbs = From(At(0, 50));
        crumbs.Tick(At(-60, 0, row: 1));
        Assert.Equal(At(-60, 0, row: 1), crumbs.All[0]);
        var from = At(0);

        Assert.Equal(At(-60, 0, row: 1), crumbs.Find(from, farthest: false));
        Assert.Equal(At(0, 50), crumbs.Find(from, farthest: true));
    }

    [Fact]
    public void AColumnIsAHundredAlongZ()
    {
        var crumbs = From(At(0, 150));
        crumbs.Tick(At(0, -60, column: 1)); // global (0, 40): 40 from the origin, against the first's 150
        var from = At(0);

        Assert.Equal(At(0, -60, column: 1), crumbs.Find(from, farthest: false));
        Assert.Equal(At(0, 150), crumbs.Find(from, farthest: true));
    }
}
