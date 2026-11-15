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
        foreach (var (o, e) in original.Zip(enhanced))
            Assert.True(e.AheadInFrame >= o.AheadInFrame, $"{e.Walk}: the way ahead is in frame less than under the original");
        // M05's door is at his back: the arrival frame is beside him, and the swing behind as he sets off.
        var hallway = enhanced.Single(r => r.Walk.StartsWith('a'));
        Assert.True(hallway.AheadInFrame > 0.5f);
        // The swing is slow on purpose: his front shows for a moment at most.
        Assert.True(hallway.Front < 0.05f);
        // After I07's authored shot lets go, the swing behind him is slow on purpose, but he's still seen from the front less than under the original.
        Assert.True(enhanced.Single(r => r.Walk.StartsWith('b')).Front < original.Single(r => r.Walk.StartsWith('b')).Front);
    }

    /// <summary>
    /// Every teleporter arrived through, under both cameras and each way of pushing the stick (<see cref="CameraCensus"/>):
    /// under Enhanced, with a player steering into the room, the camera is never past a wall, he's in the frame at the first
    /// tick, he walks in and never back out, the camera is round behind him by 1.5 s with the way ahead in frame, its swing no
    /// faster than the tight turn, and the arrival frame reaches the place's length nearly always. About twenty seconds.
    /// </summary>
    [Fact]
    public void EveryArrivalUnderTheEnhancedCameraFramesHimAndComesRound()
    {
        var arrivals = CameraCensus.Run(fixture.Data.Set);
        output.WriteLine(CameraCensus.Report(arrivals));
        var traced = arrivals.Where(a => a.Skipped is null).ToList();
        Assert.True(traced.Count > 400);
        Assert.True(arrivals.Count(a => a.Skipped is not null and not "a camera object") < 10);
        foreach (var interior in new[] { true, false })
        {
            var group = traced.Where(a => a.Interior == interior).ToList();
            foreach (var stick in new[] { CameraStick.Steer, CameraStick.RestThenSteer })
            {
                var r = group.Select(a => a.Of(CameraStyle.Enhanced, stick)).ToList();
                float Share(Func<ArrivalRun, bool> f) => (float)r.Count(f) / r.Count;
                Assert.True(Share(x => x.Trace.PastWall > 0) <= 0.01f, $"interior {interior} {stick}: past a wall");
                Assert.True(Share(x => x.SeenAtFirst) >= 0.97f, $"interior {interior} {stick}: not seen on arriving");
                Assert.True(Share(x => x.WentIn) >= 0.9f, $"interior {interior} {stick}: didn't walk in");
                Assert.True(Share(x => x.BackOut) <= 0.01f, $"interior {interior} {stick}: walked back out");
                Assert.True(Share(x => x.Trace.Hidden > 0) <= 0.08f, $"interior {interior} {stick}: hidden too often");
                Assert.True(r.Average(x => x.Lifted) <= 0.15f, $"interior {interior} {stick}: lifted too long");
                Assert.True(r.Max(x => x.PeakTurn) <= TrekCameraTuning.Default.TightTurn + 5, $"interior {interior} {stick}: whipped round");
            }
            var steer = group.Select(a => a.Of(CameraStyle.Enhanced)).ToList();
            // The swing is slow on purpose: round by 3 s.
            Assert.True((float)steer.Count(x => x.RoundBy3) / steer.Count >= 0.8f, $"interior {interior}: not round behind him by 3 s");
            var frames = steer.Select(x => x.Frame).OfType<TrekArrival>().Where(f => f.Kind != TrekArrivalKind.Authored).ToList();
            Assert.True((float)frames.Count(f => f.Reached || f.Close) / frames.Count >= 0.95f, $"interior {interior}: the frame fell short");
            // Fewer front views than at the full length alone: under a fifth turned more than 110° from behind him.
            Assert.True((float)frames.Count(f => MathF.Abs(f.Off) > 110) / frames.Count < 0.2f, $"interior {interior}: too many front views");
        }
    }
}
