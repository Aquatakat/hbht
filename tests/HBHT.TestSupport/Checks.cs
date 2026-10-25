using System.Numerics;
using Xunit;

namespace HBHT.TestSupport;

/// <summary>Comparisons and measures several test classes make. The test projects import them as static usings.</summary>
public static class Checks
{
    /// <summary>Asserts two 2D vectors are the same within <paramref name="tolerance"/> on each axis.</summary>
    public static void AssertNear(Vector2 expected, Vector2 actual, float tolerance = 1e-4f)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
    }

    /// <summary>The angle between two directions, in degrees.</summary>
    public static float Degrees(Vector2 a, Vector2 b) =>
        MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)), -1, 1)) * 180 / MathF.PI;
}
