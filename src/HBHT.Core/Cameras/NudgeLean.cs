using System.Numerics;

namespace HBHT.Core.Cameras;

/// <summary>
/// The enhanced trek camera's nudges (docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced): a small lean toward
/// what the use button would pick if it reached further (<see cref="Usable"/>, which trek answers with
/// <c>MapObjectUse.Reach</c>'s own choice), and toward the nearest hunter (<see cref="Hunter"/>). Each weighs by how near it
/// is, falling to nothing at its reach and close by, and by how far ahead it is, to nothing beside or behind him, so it never
/// turns the camera round and nothing pops. Not while sneaking, which frames the hunter itself.
/// </summary>
public sealed class NudgeLean(EnhancedTrekCamera camera) : ITrekCameraLean
{
    private const float Tick = 1f / 60;
    private const float Degree = MathF.PI / 180;

    private float _lean;
    private int _last = int.MinValue;

    /// <summary>Where the use button's choice at the longer reach is, for the player at a place travelling a way, or null.</summary>
    public Func<Vector3, Vector2, Vector3?> Usable { get; set; } = (_, _) => null;

    /// <summary>Where the nearest hunter is, for the player at a place, or null.</summary>
    public Func<Vector3, Vector3?> Hunter { get; set; } = _ => null;

    /// <summary>Whether he's sneaking, when it lets go.</summary>
    public Func<bool> Sneaking { get; set; } = () => false;

    public float Bound => camera.Tuning.NudgeBound;

    /// <summary>The lean now, in degrees.</summary>
    public float Value => _lean;

    public float Lean(Vector3 player, Vector2 travel, float yaw)
    {
        var t = camera.Tuning;
        var now = camera.Ticks;
        if (now - _last > 15)
            _lean = 0;
        _last = now;
        var goal = 0f;
        if (!Sneaking())
        {
            if (Usable(player, travel) is { } usable)
                goal += Toward(player, travel, usable, t.NudgeReach, t);
            if (Hunter(player) is { } hunter)
                goal += Toward(player, travel, hunter, t.HunterReach, t);
        }
        goal = Math.Clamp(goal, -t.NudgeBound, t.NudgeBound);
        var step = (goal - _lean) * (1 - MathF.Exp(-Tick / t.NudgeEase));
        var most = t.NudgeRate * Tick;
        _lean += Math.Clamp(step, -most, most);
        return _lean;
    }

    /// <summary>
    /// One nudge's lean, in degrees: its bearing off his travel, up to 60°, times its weight, which is whole to half
    /// <paramref name="reach"/> and falls smoothly to nothing at it, to nothing within a unit, and with the square of the
    /// bearing's cosine to nothing beside and behind him; at most the nudges' bound.
    /// </summary>
    public static float Toward(Vector3 player, Vector2 travel, Vector3 at, float reach, TrekCameraTuning t)
    {
        var to = new Vector2(at.X - player.X, at.Z - player.Z);
        var distance = to.Length();
        if (distance < 1e-3f || travel.LengthSquared() < 1e-8f || distance >= reach)
            return 0;
        var off = MathF.Atan2(to.Y, to.X) - MathF.Atan2(travel.Y, travel.X);
        off = MathF.Atan2(MathF.Sin(off), MathF.Cos(off));
        var ahead = MathF.Max(0, MathF.Cos(off));
        ahead *= ahead;
        var far = 1 - Smooth((distance - reach / 2) / (reach / 2));
        var near = Smooth((distance - 1) / 1.5f);
        var weight = far * near * ahead;
        return Math.Clamp(Math.Clamp(off / Degree, -60, 60) * weight, -t.NudgeBound, t.NudgeBound);
    }

    private static float Smooth(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
