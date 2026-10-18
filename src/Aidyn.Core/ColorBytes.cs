namespace Aidyn.Core;

/// <summary>
/// How a colour channel, an alpha or a volume is made a byte from a float, as 1.0 makes it: the value is multiplied
/// (by 255 for a 0–1 colour), <b>truncated toward zero</b> (<c>trunc.w.s</c>, <c>trunc.w.d</c>), and its low byte is
/// kept. Nothing rounds (1.0's code segment holds no <c>cvt.w</c>, <c>round</c> or <c>floor</c>) and nothing clamps, so a
/// negative wraps (−1 is 255) and a value of 256 is 0.
/// </summary>
/// <remarks>
/// The multiply's precision is the original's, not ours to pick, and differs by site: most multiply by <c>255.0f</c> in
/// single precision (<see cref="Single"/>), but the rain loop, the scene objects' view-cone alpha and the audio keys'
/// volume and pan multiply by a <c>255.0</c> double literal (<see cref="Double"/>), which can differ by one on a value
/// near a whole number. The caller does the multiply, in the precision of its original, and passes the product here.
/// Past ±2³¹ .NET saturates where the console's <c>trunc.w</c> overflows, which no site reaches.
/// </remarks>
public static class ColorBytes
{
    /// <summary>A single-precision product as a byte: truncated, then the low byte (<c>mul.s</c> × <c>255.0f</c>, <c>trunc.w.s</c>, <c>andi 0xff</c>).</summary>
    public static byte Single(float value) => unchecked((byte)(int)value);

    /// <summary>A double-precision product as a byte: truncated, then the low byte (<c>mul.d</c> × <c>255.0</c>, <c>trunc.w.d</c>, <c>andi 0xff</c>).</summary>
    public static byte Double(double value) => unchecked((byte)(int)value);
}
