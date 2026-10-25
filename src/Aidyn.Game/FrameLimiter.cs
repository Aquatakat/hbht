using Godot;

namespace Aidyn.Game;

/// <summary>
/// The Frame Rate Limit, in place of Godot's <c>Engine.MaxFps</c>, which in 4.7 sleeps a whole frame's length after the frame's
/// work rather than what's left of it: at 60 with the ticks' 6 ms a frame it drew 43 fps, and at 30, 22. This keeps a deadline
/// one frame's length on from the last, and once each frame is drawn waits out what's left of it, so the work comes out of the
/// wait. A frame that ran a whole frame's length past its deadline puts the next one a frame from now, not racing to catch up.
/// </summary>
public sealed partial class FrameLimiter : Node
{
    /// <summary>The most frames a second, or 0 for none.</summary>
    public int Limit { get; set; }

    /// <summary>When the frame being drawn should end, in Godot's microseconds.</summary>
    private ulong _deadline;

    public FrameLimiter() => Name = nameof(FrameLimiter);

    public override void _EnterTree() => RenderingServer.FramePostDraw += Wait;

    public override void _ExitTree() => RenderingServer.FramePostDraw -= Wait;

    private void Wait()
    {
        if (Limit <= 0)
            return;
        var step = 1_000_000UL / (ulong)Limit;
        _deadline += step;
        var now = Time.GetTicksUsec();
        if (now < _deadline)
            OS.DelayUsec((int)(_deadline - now));
        else if (now - _deadline > step)
            _deadline = now;
    }
}
