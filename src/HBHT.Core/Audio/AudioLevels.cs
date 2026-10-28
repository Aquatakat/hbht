using System.Numerics;

namespace HBHT.Core.Audio;

/// <summary>
/// How a sound's loudness and pan are worked out and made the bytes a sequence plays at (<c>Audio.UpdateSound</c>,
/// 1.0 <c>0x8004E900</c>, and <c>Audio.VolumeAndPan</c>, <c>0x8004EB90</c>), and the numbers they share
/// (docs/systems/audio.md). Each multiplies by a <c>255.0</c> double literal and truncates (<see cref="ColorBytes"/>).
/// </summary>
public static class AudioLevels
{
    /// <summary>Ticks a sound takes to fade in, without falloff, or out (<c>delta / 90</c> a frame).</summary>
    public const int FadeTicks = 90;

    /// <summary>Milliseconds a sound plays for when it starts, until it's next kept up (<c>Audio.UpdateSound</c>).</summary>
    public const int FirstDurationMs = 2000;

    /// <summary>
    /// A sound's loudness at a distance from what the camera looks at: 1 at it, 0 at <paramref name="radius"/>,
    /// linear, and not clamped (<c>ProcessAudioBubbles</c>, <c>Audio.VolumeAndPan</c>).
    /// </summary>
    public static float Loudness(float distance, float radius) => 1 - distance / radius;

    /// <summary>
    /// A kept sound's volume byte (<c>Audio.UpdateSound</c>): <c>trunc(fade × volume × 255)</c> clamped to a byte, times
    /// the sound or music volume <paramref name="level"/>, truncated.
    /// </summary>
    public static byte Volume(float fade, float volume, float level) =>
        ColorBytes.Single(Math.Clamp((int)(fade * volume * 255.0), 0, 255) * level);

    /// <summary>A kept sound's pan byte (<c>Audio.UpdateSound</c>): <c>trunc((pan + 1) × 0.5 × 255)</c> clamped, so a pan of 0 is 127.</summary>
    public static byte PanByte(float pan) => (byte)Math.Clamp((int)PanScaled(pan), 0, 255);

    /// <summary>
    /// A body's loudness byte (<c>Audio.VolumeAndPan</c>): <c>trunc(loudness × 255)</c>, which the original doesn't clamp, so
    /// below 0 its low byte wraps; the port's loudness is never below 0 (<see cref="AudioKeys.VolumeAndPan"/>).
    /// </summary>
    public static byte LoudnessByte(float loudness) => ColorBytes.Double(loudness * 255.0);

    private static double PanScaled(float pan) => (pan + 1f) * 0.5f * 255.0;

    /// <summary>
    /// A sound's pan (<c>Audio.Pan</c>, 1.0 <c>0x8004EB1C</c>): the sine of the angle across the ground from the
    /// camera's facing to it, positive to the right, the less the louder it is. Not clamped.
    /// </summary>
    public static float Pan(Vector3 position, Vector3 eye, Vector3 aim, float loudness)
    {
        var facing = new Vector2(aim.X - eye.X, aim.Z - eye.Z);
        var to = new Vector2(position.X - eye.X, position.Z - eye.Z);
        if (facing.LengthSquared() == 0 || to.LengthSquared() == 0)
            return 0;
        facing = Vector2.Normalize(facing);
        to = Vector2.Normalize(to);
        return (facing.X * to.Y - facing.Y * to.X) * (1 - loudness);
    }
}
