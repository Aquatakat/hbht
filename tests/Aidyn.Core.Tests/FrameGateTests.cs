using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class FrameGateTests
{
    /// <summary>A frame ends on its last tick: the first of two ticks doesn't, the second does.</summary>
    [Fact]
    public void AFrameEndsOnItsLastTick()
    {
        var gate = new FrameGate(Cadence.FrameTicks);
        var ends = Enumerable.Range(0, 6).Select(_ => gate.Tick()).ToArray();
        Assert.Equal([false, true, false, true, false, true], ends);
    }

    [Fact]
    public void EachCadenceFrameEndsOnceInItsLength()
    {
        foreach (var length in new[] { Cadence.MenuFrameTicks, Cadence.FrameTicks, Cadence.TextureFrameTicks })
        {
            var gate = new FrameGate(length);
            Assert.Equal(4, Enumerable.Range(0, 4 * length).Count(_ => gate.Tick()));
        }
    }

    /// <summary>With a frame of one tick, every tick ends a frame, as the pause menu's does.</summary>
    [Fact]
    public void AFrameOfOneTickEndsOnEveryTick()
    {
        var gate = new FrameGate(Cadence.MenuFrameTicks);
        Assert.All(Enumerable.Range(0, 5), _ => Assert.True(gate.Tick()));
        Assert.Equal(0, gate.Ticks);
    }

    [Fact]
    public void TicksSaysHowFarIntoTheFrameItIs()
    {
        var gate = new FrameGate(Cadence.TextureFrameTicks);
        Assert.Equal(0, gate.Ticks);
        gate.Tick();
        Assert.Equal(1, gate.Ticks);
        gate.Tick();
        Assert.Equal(2, gate.Ticks);
        Assert.True(gate.Tick());
        Assert.Equal(0, gate.Ticks);
    }

    /// <summary>Resetting starts the frame over, so a menu that opens counts its first frame from there.</summary>
    [Fact]
    public void ResetStartsTheFrameOver()
    {
        var gate = new FrameGate(Cadence.TextureFrameTicks);
        gate.Tick();
        gate.Tick();
        gate.Reset();
        Assert.False(gate.Tick());
        Assert.False(gate.Tick());
        Assert.True(gate.Tick());
    }

    /// <summary>A pulse's restart keeps the ticks toward its next step, but its reset starts the frame over.</summary>
    [Fact]
    public void APulseRestartKeepsTheFrameAndItsResetDoesNot()
    {
        var from = new Rgba(0, 0, 0, 0);
        var to = new Rgba(200, 200, 200, 200);

        var kept = new MenuPulse(Cadence.TextureFrameTicks);
        kept.Step();
        kept.Step();
        kept.Restart();
        kept.Step();

        var reset = new MenuPulse(Cadence.TextureFrameTicks);
        reset.Step();
        reset.Step();
        reset.Reset();
        reset.Step();

        Assert.Equal(new Rgba(10, 10, 10, 10), kept.Now(from, to));
        Assert.Equal(from, reset.Now(from, to));
    }
}
