using System.Numerics;
using HBHT.Core.Physics;

namespace HBHT.Core.Cameras;

/// <summary>
/// The enhanced trek camera's lean toward the path's bend (docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced):
/// a fan of level sweeps ahead of his travel, against walls alone (<see cref="SpherePhysics.SweepWalls"/>), finds how far each
/// way is open; the lean is toward their mean weighed by the square of how open each is, so it moves continuously, scaled by
/// how blocked straight ahead is, so on an open field or a straight road it's nothing. Eased, and its speed capped. The stick
/// being camera-relative, a player holding forward then follows the path.
/// </summary>
public sealed class PathBendLean(EnhancedTrekCamera camera, Func<CollisionWorld> world) : ITrekCameraLean
{
    private const float Tick = 1f / 60;
    private const float Degree = MathF.PI / 180;

    private float _lean, _goal;
    private int _last = int.MinValue;

    public float Bound => camera.Tuning.BendBound;

    /// <summary>The lean now, in degrees.</summary>
    public float Value => _lean;

    public float Lean(Vector3 player, Vector2 travel, float yaw)
    {
        var t = camera.Tuning;
        var now = camera.Ticks;
        // Not asked for a while (standing, or the player turned the camera): it starts again from nothing.
        if (now - _last > 15)
            _lean = _goal = 0;
        if (now - _last > 1 || now % 2 == 0)
            _goal = Probe(world(), player, travel, t);
        _last = now;
        var step = (_goal - _lean) * (1 - MathF.Exp(-Tick / t.BendEase));
        var most = t.BendRate * Tick;
        _lean += Math.Clamp(step, -most, most);
        return _lean;
    }

    /// <summary>The lean's goal, in degrees: toward the open heading, by how blocked straight ahead is.</summary>
    public static float Probe(CollisionWorld world, Vector3 player, Vector2 travel, TrekCameraTuning t, float[]? free = null)
    {
        if (travel.LengthSquared() < 1e-6f)
            return 0;
        var heading = MathF.Atan2(travel.Y, travel.X);
        var rays = Math.Max(3, (int)t.BendRays | 1);
        var half = rays / 2;
        var sum = Vector2.Zero;
        var ahead = t.BendReach;
        for (var k = -half; k <= half; k++)
        {
            var off = t.BendSpread * Degree * k / half;
            var way = new Vector3(MathF.Cos(heading + off), 0, MathF.Sin(heading + off));
            var share = SpherePhysics.SweepWalls(world, player, player + way * t.BendReach, t.BendRadius, 0.45f) ?? 1;
            var open = share * t.BendReach;
            if (free is not null)
                free[k + half] = open;
            if (Math.Abs(k) <= 1)
                ahead = MathF.Min(ahead, open);
            var weight = share * share;
            sum += new Vector2(MathF.Cos(off), MathF.Sin(off)) * weight;
        }
        if (sum.LengthSquared() < 1e-8f)
            return 0;
        var blocked = Math.Clamp((t.BendReach - ahead) / MathF.Max(t.BendReach - t.BendNear, 1e-3f), 0, 1);
        blocked = blocked * blocked * (3 - 2 * blocked);
        // Up against the wall there's no bend to round, only a wall: leaning then would wheel him round along it, the view
        // and the stick turning together. It fades from 2 free to nothing at 0.8.
        var against = Math.Clamp((ahead - 0.8f) / 1.2f, 0, 1);
        blocked *= against * against * (3 - 2 * against);
        return Math.Clamp(blocked * t.BendGain * MathF.Atan2(sum.Y, sum.X) / Degree, -t.BendBound, t.BendBound);
    }
}
