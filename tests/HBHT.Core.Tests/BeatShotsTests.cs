using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Combat;

namespace HBHT.Core.Tests;

/// <summary>
/// Start Early's shot question (<see cref="BeatShots"/>): whether one shot can hold beats that play at once, which the composer asks as
/// it schedules, and the shot that does, which the director frames.
/// </summary>
public sealed class BeatShotsTests
{
    private static readonly CameraTuning T = CameraTuning.Default;

    private static ShotSubject At(float x, float z) => new(new Vector3(x, 1, z), 0.6f);

    [Fact]
    public void BeatsAcrossTheFieldShareAWideShotAndThoseTooFarApartDont()
    {
        var shots = new BeatShots(null, (new Vector2(-60, -60), new Vector2(60, 60)), T);
        // Twenty apart: a wide shot holds both, low across the field or from above.
        Assert.NotNull(shots.Find([At(-10, 0), At(10, 0)], 0, null));
        // A hundred apart: none within BeatDistance.
        Assert.Null(shots.Find([At(-50, 0), At(50, 0)], 0, null));
    }

    [Fact]
    public void TheShotFoundHoldsEveryBeatOnTheScreenAndIsTheSameEachTime()
    {
        var shots = new BeatShots(null, null, T);
        ShotSubject[] beats = [At(0, 0), At(8, 3), At(-4, 9)];
        var first = shots.Find(beats, 0.3f, null);
        Assert.NotNull(first);
        Assert.True(shots.Sees(first.Value, beats));
        foreach (var b in beats)
            Assert.True(ShotFraming.Holds(first.Value, b with { Radius = 0 }, T.Lens));
        Assert.Equal(first, shots.Find(beats, 0.3f, null));
        Assert.True(first.Value.Distance <= T.BeatDistance);
    }

    [Fact]
    public void TheShotNearestTheViewsWayRoundIsTaken()
    {
        var shots = new BeatShots(null, null, T);
        ShotSubject[] beats = [At(0, 0), At(4, 0)];
        foreach (var from in (float[])[0, 1.5f, -2.5f])
        {
            var shot = shots.Find(beats, from, null)!.Value;
            Assert.True(MathF.Abs(Shot.Wrap(shot.Yaw - from)) < 0.01f, $"from {from} it turned to {shot.Yaw}");
        }
    }
}
