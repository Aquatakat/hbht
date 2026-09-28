namespace Aidyn.Core.Trek;

/// <summary>
/// How the port turns the original's per-frame behaviour into per-tick steps (ADR 0006).
/// </summary>
/// <remarks>
/// The original runs its logic once per rendered frame, with the number of 60 Hz ticks since the
/// last frame as its delta, and at 15–25 fps a frame covers several. Most of it loops over the
/// ticks, so it doesn't care, but some of it acts once a frame, and some loops toward a target
/// that only moves once a frame. Those parts run faster at a higher frame rate: the camera turns
/// behind the player about twice as fast per second at 30 fps as at 60. The port steps every tick
/// and scales those parts to what the original does at an idealized 30 fps.
/// </remarks>
public static class Cadence
{
    /// <summary>The ticks in the frame that per-frame behaviour is matched to: 30 fps.</summary>
    public const int FrameTicks = 2;

    /// <summary>An amount added once a frame, spread evenly over the frame's ticks.</summary>
    public static float PerTick(float perFrame) => perFrame / FrameTicks;

    /// <summary>A share of what's left, taken once a frame, as a share per tick that compounds to the same.</summary>
    public static float CompoundPerTick(float perFrame) => 1 - MathF.Pow(1 - perFrame, 1f / FrameTicks);

    /// <summary>
    /// What a frame of <paramref name="ticks"/> ticks does with <c>AdjustAim</c>'s rate: it moves a
    /// point <c>1/rate</c> of the way to its target once per tick, toward a target fixed for the frame.
    /// </summary>
    public static float FrameShare(float rate, int ticks = FrameTicks) => 1 - MathF.Pow(1 - 1 / rate, ticks);
}
