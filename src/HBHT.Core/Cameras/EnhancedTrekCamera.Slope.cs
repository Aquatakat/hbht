using System.Numerics;
using HBHT.Core.Physics;

namespace HBHT.Core.Cameras;

public sealed partial class EnhancedTrekCamera
{
    /// <summary>
    /// The pitch follows the ground ahead by <see cref="TrekCameraTuning.SlopeShare"/> of its slope, within
    /// <see cref="TrekCameraTuning.SlopeBound"/>, eased: down a little going downhill, up going uphill. Standing, it eases back.
    /// </summary>
    private void StepSlope(CollisionWorld world, Vector3 player)
    {
        var t = Tuning;
        var goal = 0f;
        if (_travel.Length() > t.MovingSpeed && GroundSlope(world, player, Vector2.Normalize(_travel), t.SlopeAhead) is { } slope)
            goal = Math.Clamp(-t.SlopeShare * slope, -t.SlopeBound, t.SlopeBound);
        _slope += (goal - _slope) * (1 - MathF.Exp(-Tick / t.SlopeEase));
    }

    /// <summary>
    /// The ground's slope from under <paramref name="player"/> to <paramref name="ahead"/> along <paramref name="way"/>, in
    /// degrees, rising positive; null where either has no ground (facing at least half up) within reach.
    /// </summary>
    public static float? GroundSlope(CollisionWorld world, Vector3 player, Vector2 way, float ahead)
    {
        if (GroundAt(world, player) is not { } here)
            return null;
        var there = player + new Vector3(way.X, 0, way.Y) * ahead;
        if (GroundAt(world, there) is not { } far)
            return null;
        return MathF.Atan2(far - here, ahead) / Degree;
    }

    private static float? GroundAt(CollisionWorld world, Vector3 at) =>
        SpherePhysics.SweepNearest(world, at + new Vector3(0, 2, 0), at - new Vector3(0, 4, 0), 0.05f, false, 0.5f, out var point, out _)
            ? point.Y
            : null;
}
