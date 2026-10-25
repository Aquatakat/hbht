namespace HBHT.Core;

/// <summary>
/// Counts ticks to a frame of one of <see cref="Cadence"/>'s lengths (ADR 0017): <see cref="Tick"/> says whether a tick is
/// the one a frame ends on, which is where per-frame code acts.
/// </summary>
/// <param name="frameTicks">The ticks in a frame: <see cref="Cadence.FrameTicks"/> or another of its frames.</param>
public sealed class FrameGate(int frameTicks)
{
    private int _ticks;

    /// <summary>
    /// The ticks since the last frame ended, from 0 to <paramref name="frameTicks"/> − 1: where in the frame the next tick
    /// lands, for moving on from where the frame before left off.
    /// </summary>
    public int Ticks => _ticks;

    /// <summary>A tick. Whether it ends a frame, every <paramref name="frameTicks"/> ticks counted from the last <see cref="Reset"/>.</summary>
    public bool Tick()
    {
        if (++_ticks < frameTicks)
            return false;
        _ticks = 0;
        return true;
    }

    /// <summary>Several ticks at once, as <see cref="Tick()"/> counts them: how many frames they end, none to as many as they cover.</summary>
    public int Tick(int ticks)
    {
        _ticks += ticks;
        var frames = _ticks / frameTicks;
        _ticks %= frameTicks;
        return frames;
    }

    /// <summary>Starts the frame again, as the first of its ticks next.</summary>
    public void Reset() => _ticks = 0;
}
