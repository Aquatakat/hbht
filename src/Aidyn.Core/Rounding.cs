namespace Aidyn.Core;

/// <summary>
/// How the original rounds a float to a byte where it does round: half away from zero, by adding or taking off a half
/// and truncating (<c>trunc.w</c>), then keeping the low byte, so a negative wraps (−1 is 255). Colours and volumes
/// truncate instead (<see cref="ColorBytes"/>).
/// </summary>
/// <remarks>
/// The half's precision is the original's, as for <see cref="ColorBytes"/>: combat's grid and reach add a single-precision
/// <c>0.5f</c>, and the barter's appraisal a <c>0.5</c> double.
/// </remarks>
public static class Rounding
{
    /// <summary>A single-precision value rounded half away from zero, then its low byte.</summary>
    public static byte HalfAwayToByte(float value) => unchecked((byte)(value > 0 ? (int)(value + 0.5f) : -(int)(0.5f - value)));

    /// <summary>A double-precision value rounded half away from zero, then its low byte.</summary>
    public static byte HalfAwayToByte(double value) => unchecked((byte)(value > 0 ? (int)(value + 0.5) : -(int)(0.5 - value)));
}
