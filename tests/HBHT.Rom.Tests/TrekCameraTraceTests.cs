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
}
