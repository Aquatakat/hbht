using HBHT.Core.Rng;

namespace HBHT.Core.Tests;

/// <summary>A stream that returns the raw values it's given, in order, and fails if it's asked for more.</summary>
internal sealed class ScriptedRandom(params uint[] values) : IRandom
{
    private int _next;

    /// <summary>How many of its values haven't been drawn.</summary>
    public int Remaining => values.Length - _next;

    public uint Next() => _next < values.Length ? values[_next++] : throw new InvalidOperationException("The rules drew more dice than the test scripted.");
}
