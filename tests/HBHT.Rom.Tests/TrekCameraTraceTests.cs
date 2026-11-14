using HBHT.Core.Cameras;
using HBHT.Core.Trek;

namespace HBHT.Rom.Tests;

/// <summary>
/// Trek's camera traced headless over the fixed walks on NA 1.0's data (<see cref="CameraTrace"/>,
/// docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced). The table is written to the test's output, as
/// <c>hbht camera trace</c> prints it.
/// </summary>
public sealed class TrekCameraTraceTests(Na10Import fixture, ITestOutputHelper output)
{
    /// <summary>
    /// The original's camera over each walk, tick for tick, as it was before the enhanced trek camera came: a checksum of its
    /// position and aim every tick, the teleport's fade included.
    /// </summary>
    private static readonly Dictionary<string, ulong> OriginalChecksums = new()
    {
        ["a M05 hallway"] = 0xA9C1F3EA8ACE5EC0,
        ["b I07 entrance"] = 0x6D68DC7BA0342311,
        ["c C15 bend"] = 0x363E6DFD65C444BC,
    };

    [Fact]
    public void TheOriginalCameraIsUnchangedTickForTick()
    {
        var results = CameraWalk.Standard.Select(w => CameraTrace.Run(fixture.Data.Set, w, CameraStyle.Original)).ToList();
        output.WriteLine(CameraTrace.Table(results));
        foreach (var r in results)
        {
            output.WriteLine($"{r.Walk}: 0x{r.Checksum:X16}");
            Assert.Equal(r.Points, r.Ends);
        }
        foreach (var r in results)
            Assert.Equal(OriginalChecksums[r.Walk], r.Checksum);
    }

    /// <summary>
    /// The enhanced camera on the same walks: never past a wall (collision from either side, or the drawn world), the player
    /// never hidden, never seen from the front running down M05's hallway after the arrival's second, and its view turning
    /// no more jerkily than the original's, by the 95th percentile of the turn's change.
    /// </summary>
    [Fact]
    public void TheEnhancedCameraStaysInsideTheWallsAndBehindHim()
    {
        var original = CameraWalk.Standard.Select(w => CameraTrace.Run(fixture.Data.Set, w, CameraStyle.Original)).ToList();
        var enhanced = CameraWalk.Standard.Select(w => CameraTrace.Run(fixture.Data.Set, w, CameraStyle.Enhanced)).ToList();
        output.WriteLine(CameraTrace.Table(original.Concat(enhanced)));
        foreach (var (o, e) in original.Zip(enhanced))
        {
            Assert.Equal(e.Points, e.Ends);
            Assert.Equal(0, e.PastWall);
            Assert.Equal(0, e.Hidden);
            // (b)'s authored shot lets go with him seen from the front, and the swing behind is meant to be quick.
            if (!e.Walk.StartsWith('b'))
                Assert.True(e.Accel95 <= o.Accel95, $"{e.Walk}: the view's turn changes more sharply than the original's");
        }
        var hallway = enhanced.Single(r => r.Walk.StartsWith('a'));
        Assert.Equal(0, hallway.Front);
        Assert.Equal(0, hallway.Blocked);
        // After I07's authored shot lets go, the swing behind him is quick: he's seen from the front far less than under the original.
        Assert.True(enhanced.Single(r => r.Walk.StartsWith('b')).Front < original.Single(r => r.Walk.StartsWith('b')).Front * 0.6f);
    }

    /// <summary>
    /// Every teleporter arrived through, held forward 3 s, under both cameras (<see cref="CameraCensus"/>): under Enhanced the
    /// camera is almost never past a wall and his front is almost never seen after the first second, interiors and outdoors.
    /// About ten seconds, the arrivals side by side.
    /// </summary>
    [Fact]
    public void EveryArrivalUnderTheEnhancedCameraStaysInsideAndBehindHim()
    {
        var arrivals = CameraCensus.Run(fixture.Data.Set);
        output.WriteLine(CameraCensus.Report(arrivals));
        var traced = arrivals.Where(a => a.Skipped is null).ToList();
        Assert.True(traced.Count > 400);
        Assert.True(arrivals.Count(a => a.Skipped is not null and not "a camera object") < 10);
        foreach (var interior in new[] { true, false })
        {
            var group = traced.Where(a => a.Interior == interior).Select(a => a.Enhanced!).ToList();
            Assert.True(group.Count(r => r.PastWall > 0) <= 0.02f * group.Count, $"interior {interior}: too often past a wall");
            Assert.True(group.Count(r => r.Front > 0) <= 0.01f * group.Count, $"interior {interior}: his front seen too often");
        }
    }
}
