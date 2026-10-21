using System.Numerics;

namespace Aidyn.Core;

/// <summary>
/// A vector made a unit vector where it can be, and a fallback where it can't: <see cref="Vector2.Normalize(Vector2)"/> of
/// a zero vector is NaN. <see cref="Of(Vector2, Vector2)"/> falls back on exactly zero, as the original's callers that only
/// guard against zero do; <see cref="Above(Vector2, float, Vector2)"/> on anything not above a squared length, as the ones
/// with a threshold do.
/// </summary>
public static class SafeNormalize
{
    /// <summary><paramref name="v"/> as a unit vector, or <paramref name="fallback"/> (zero, unless given) for a zero vector.</summary>
    public static Vector2 Of(Vector2 v, Vector2 fallback = default) => v.LengthSquared() > 0 ? Vector2.Normalize(v) : fallback;

    /// <inheritdoc cref="Of(Vector2, Vector2)"/>
    public static Vector3 Of(Vector3 v, Vector3 fallback = default) => v.LengthSquared() > 0 ? Vector3.Normalize(v) : fallback;

    /// <summary>
    /// <paramref name="v"/> as a unit vector, or <paramref name="fallback"/> (zero, unless given) unless its squared length is
    /// above <paramref name="minLengthSquared"/>.
    /// </summary>
    public static Vector2 Above(Vector2 v, float minLengthSquared, Vector2 fallback = default) =>
        v.LengthSquared() > minLengthSquared ? Vector2.Normalize(v) : fallback;
}
