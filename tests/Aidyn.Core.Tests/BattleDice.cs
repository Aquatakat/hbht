namespace Aidyn.Core.Tests;

/// <summary>
/// Raw values for a <see cref="ScriptedRandom"/> that make the original's draws come out as a test wants. A draw is
/// <c>Below(n) = (n * next) >> 32</c> and <c>Range(a, b) = Below(b - a + 1) + a</c>, so the smallest raw value that gives
/// <c>Below(n) = v</c> is <c>ceil(v * 2^32 / n)</c>.
/// </summary>
internal static class BattleDice
{
    /// <summary>The raw value whose <c>Below(n)</c> is <paramref name="value"/>.</summary>
    public static uint Below(int n, int value) => (uint)((((ulong)value << 32) + (ulong)n - 1) / (ulong)n);

    /// <summary>The raw value whose <c>Range(lo, hi)</c> is <paramref name="result"/>.</summary>
    public static uint Range(int lo, int hi, int result) => Below(hi - lo + 1, result - lo);
}
