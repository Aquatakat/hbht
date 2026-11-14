using HBHT.Core.Cameras;
using HBHT.Core.Trek;

namespace HBHT.Rom.Tests;

/// <summary>
/// The enhanced trek camera's path's bend, slopes, nudges and sneaking measured on NA 1.0's data (<see cref="CameraMeasures"/>,
/// docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced), as <c>hbht camera measure</c> prints them.
/// </summary>
public sealed class TrekCameraMeasureTests(Na10Import fixture, ITestOutputHelper output)
{
    private (MeasureResult Original, MeasureResult Enhanced) Both(string name)
    {
        var walk = MeasureWalk.Standard.Single(w => w.Name.StartsWith(name, StringComparison.Ordinal));
        var original = CameraMeasures.Run(fixture.Data.Set, walk, CameraStyle.Original);
        var enhanced = CameraMeasures.Run(fixture.Data.Set, walk, CameraStyle.Enhanced);
        output.WriteLine(CameraMeasures.Table([original, enhanced]));
        return (original, enhanced);
    }

    /// <summary>
    /// Holding forward alone into a forest path's bend: under the original's camera he runs into the trees and stays pressed
    /// against them; with the bend's lean he rounds it.
    /// </summary>
    [Theory]
    [InlineData("c forward")]
    [InlineData("d forward")]
    public void HoldingForwardFollowsThePathRoundItsBend(string walk)
    {
        var (original, enhanced) = Both(walk);
        Assert.True(original.Progress < 0.6f * original.PathLength);
        Assert.True(original.Pressed > 0.2f);
        Assert.True(enhanced.Progress > 0.9f * enhanced.PathLength);
        Assert.True(enhanced.Pressed < 0.05f);
        Assert.True(enhanced.BendPeak <= TrekCameraTuning.Default.BendBound + 1e-3f);
    }

    /// <summary>On open ground the bend's lean stays near nothing and doesn't wag.</summary>
    [Fact]
    public void OnOpenGroundTheBendLeansNot()
    {
        var (_, enhanced) = Both("open");
        Assert.True(enhanced.BendPeak < 2);
        Assert.True(enhanced.BendSpeed95 < 2);
    }

    /// <summary>Up D08's slope it looks up a little, and down it, down.</summary>
    [Fact]
    public void ThePitchFollowsTheSlope()
    {
        var (_, up) = Both("up");
        Assert.True(up.Slope > 10);
        Assert.InRange(up.SlopePitch, -TrekCameraTuning.Default.SlopeBound, -3);
        var (_, down) = Both("down");
        Assert.True(down.Slope < -6);
        Assert.InRange(down.SlopePitch, 2, TrekCameraTuning.Default.SlopeBound);
    }

    /// <summary>Sneaking past E14's encounter, the hunter is in the frame beyond him most of the way, where it seldom is under the original's.</summary>
    [Fact]
    public void SneakingKeepsTheHunterInTheFrame()
    {
        var (original, enhanced) = Both("sneak");
        Assert.True(original.InFrame < 0.2f);
        Assert.True(enhanced.InFrame > 0.6f);
    }

    /// <summary>Walking past a container and an npc, the nudge leans toward each, within its bound, and lets go after.</summary>
    [Theory]
    [InlineData("container", 1f)]
    [InlineData("npc", 5f)]
    public void TheNudgeLeansTowardWhatAWouldUseAndLetsGo(string walk, float least)
    {
        var (_, enhanced) = Both(walk);
        Assert.InRange(enhanced.NudgePeak, least, TrekCameraTuning.Default.NudgeBound + 1e-3f);
        if (walk == "container")
            Assert.True(enhanced.NudgeEnd < 1);
    }
}
