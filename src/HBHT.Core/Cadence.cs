namespace HBHT.Core;

/// <summary>
/// How the port turns the original's per-frame behaviour into per-tick steps (ADR 0017).
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

    /// <summary>
    /// The ticks in the frame that scrolling textures are matched to: 20 fps, the console's average.
    /// </summary>
    /// <remarks>
    /// Textures scroll once a frame, and at 30 fps they ran about twice as fast as on the console
    /// by the fire and gore outside Gwernia, where it runs at about 15 fps. The user chose 20 fps as
    /// a middle between such spots and interiors, which run faster.
    /// </remarks>
    public const int TextureFrameTicks = 3;

    /// <summary>
    /// The ticks in the frame that rain and snow spawn by: 20 fps, the console's average, as for textures.
    /// </summary>
    /// <remarks>
    /// The particle pool spawns once a frame per emitter, whatever the frame's ticks (<c>ProcessAndRenderParticleHead</c>,
    /// <c>0x800a842c</c>), so the weather's drops and splashes come as often as the console draws frames. At 30 fps the
    /// rain's own splashes came 1.5 to 2 times as often as on the console on the beach, where it runs at about 15 fps;
    /// the user saw about twice. The rest of the pool keeps <see cref="FrameTicks"/>.
    /// </remarks>
    public const int PrecipitationFrameTicks = 3;

    /// <summary>
    /// The ticks in a frame of the pause menu: 60 fps. The menu steps its slides, fades and scroll once a frame, and
    /// its frames are light: a slide of 32 frames at 2 ticks felt slow beside the emulator's to the user (Oct 12,
    /// 2026), and 1 is the only shorter whole number.
    /// </summary>
    public const int MenuFrameTicks = 1;

    /// <summary>The ticks in a frame of a battle's aiming cursors: 15 fps.</summary>
    /// <remarks>
    /// The flask's free cursor and an area spell's centre move 0.8 on a frame's first pad event, whatever its ticks
    /// (<c>Combat.InputThrownTarget</c>, <c>0x80084BA0</c>; <c>Combat.MoveAreaCursor</c>, <c>0x8008465C</c>). The Combat
    /// checkpoints' frames were 2 to 4 ticks (<c>Frame.Delta</c>, <c>0x800D8094</c>), 4 in both aiming states sampled. At
    /// 30 fps the user found the flask's circle twice as fast as the console's, too quick to steer.
    /// </remarks>
    public const int AimFrameTicks = 4;

    /// <summary>The ticks in <paramref name="frames"/> of the default frame, <see cref="FrameTicks"/> each.</summary>
    public static int Frames(int frames) => frames * FrameTicks;

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
