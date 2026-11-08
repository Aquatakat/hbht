using System.Numerics;

namespace HBHT.Core.Cameras;

/// <summary>
/// How fast a camera's motion may change: the aim's acceleration in units a second squared, the turn's (yaw and pitch) in degrees a
/// second squared, and the distance's by its proportion: how fast its logarithm may change, a second squared, so a zoom is even at
/// every scale and a reversal close in costs little ground. Speed isn't capped. <see cref="Settle"/> is how quickly the last of the aim's
/// move closes, a second's share, so it eases onto its goal rather than wobbling round it.
/// </summary>
/// <param name="TurnSettle">The same for the yaw and pitch: quicker than the aim's, so a new shot's angle doesn't trail in after its place.</param>
/// <param name="ZoomSettle">The same for the distance, gentler, so a zoom turned round close in gives up little ground.</param>
public readonly record struct MotionCaps(float Aim, float Turn, float Zoom, float Settle, float TurnSettle, float ZoomSettle);

/// <summary>
/// A camera moved toward a goal shot with its acceleration capped, and its deceleration too, so it neither whips into a move nor stops
/// dead: each of the aim, the yaw, the pitch and the distance speeds up no faster than its cap allows and slows in time to stop on its
/// goal. A move across the field takes as long as the cap makes it, and a short one is quick. A goal that moves (a walking body) is
/// followed at its own speed, which is read from how it moved, unless it jumped.
/// </summary>
/// <remarks>
/// Each tick, each channel's error <c>e</c> gives the speed it wants, the goal's own plus <c>min(√(2a|e|), settle·|e|)</c> toward it: the
/// fastest from which the cap still stops it there, and near the goal a gentle close. Its velocity then changes toward that by at most
/// the cap times the tick. So the change in velocity a tick never passes <c>a/60</c>, whatever the goal does.
/// </remarks>
public sealed class CappedMotion
{
    private const float Tick = 1f / 60;

    private Vector3 _aimSpeed;
    private float _yawSpeed, _pitchSpeed, _distanceSpeed;
    private Shot? _lastGoal;
    private Vector3 _goalAimSpeed;
    private float _goalYawSpeed, _goalPitchSpeed, _goalDistanceSpeed;

    public CappedMotion(Shot start, MotionCaps caps)
    {
        Current = start;
        Caps = caps;
    }

    /// <summary>The caps.</summary>
    public MotionCaps Caps { get; set; }

    /// <summary>How far a goal may move in a tick and still be followed; further, it jumped, and its own speed is taken as none.</summary>
    public float GoalJump { get; set; } = 1;

    /// <summary>
    /// The most of a moving goal's own speed that's followed, units a second for the aim and radians a second for the turn and the
    /// distance's logarithm: a running body's, so a goal that lunges and stops (the original's camera chasing an aim) isn't overshot by
    /// more than the cap can stop in.
    /// </summary>
    public (float Units, float Turn) MostFollowed { get; set; } = (8, MathF.PI / 2);

    /// <summary>The nearest the eye comes to the aim, so an overshoot never puts it on the subject.</summary>
    public float NearestDistance { get; set; } = 2;

    /// <summary>The shot the camera is at.</summary>
    public Shot Current { get; private set; }

    /// <summary>The aim's velocity, in units a second.</summary>
    public Vector3 AimSpeed => _aimSpeed;

    /// <summary>The yaw's, pitch's and distance's velocities: radians a second, and the distance's logarithm's a second.</summary>
    public (float Yaw, float Pitch, float Distance) Speeds => (_yawSpeed, _pitchSpeed, _distanceSpeed);

    /// <summary>Whether it's still moving, or still short of its last goal.</summary>
    public bool Moving { get; private set; }

    /// <summary>Jumps to a shot at rest.</summary>
    public void Cut(Shot shot)
    {
        Current = shot;
        _aimSpeed = Vector3.Zero;
        _yawSpeed = _pitchSpeed = _distanceSpeed = 0;
        _lastGoal = null;
        _goalAimSpeed = Vector3.Zero;
        _goalYawSpeed = _goalPitchSpeed = _goalDistanceSpeed = 0;
        Moving = false;
    }

    /// <summary>
    /// Moves the shot by <paramref name="by"/> as it is, its speeds kept: carried along with the body it follows, so only what's left of
    /// the way to the goal past that is capped. The goal's own motion is measured from there.
    /// </summary>
    public void Carry(Vector3 by)
    {
        Current = Current with { Aim = Current.Aim + by };
        if (_lastGoal is { } last)
            _lastGoal = last with { Aim = last.Aim + by };
    }

    /// <summary>
    /// Turns the shot by <paramref name="by"/> radians about its aim as it is, its speeds kept: turned along with what it looks over the
    /// shoulder at as the body walks round it, so only what's left of the turn past that is capped.
    /// </summary>
    public void Turn(float by)
    {
        Current = Current with { Yaw = Shot.Wrap(Current.Yaw + by) };
        if (_lastGoal is { } last)
            _lastGoal = last with { Yaw = Shot.Wrap(last.Yaw + by) };
    }

    /// <summary>A tick toward <paramref name="goal"/>.</summary>
    public void Step(Shot goal)
    {
        var caps = Caps;
        // The goal's own motion, eased over a few ticks, unless it jumped.
        if (_lastGoal is { } last)
        {
            var moved = goal.Aim - last.Aim;
            var jumped = moved.Length() > GoalJump || MathF.Abs(Shot.Wrap(goal.Yaw - last.Yaw)) > 0.1f
                || MathF.Abs(goal.Pitch - last.Pitch) > 0.1f || MathF.Abs(MathF.Log(goal.Distance / last.Distance)) > 0.1f;
            if (jumped)
            {
                _goalAimSpeed = Vector3.Zero;
                _goalYawSpeed = _goalPitchSpeed = _goalDistanceSpeed = 0;
            }
            else
            {
                const float k = 0.25f;
                _goalAimSpeed += (moved / Tick - _goalAimSpeed) * k;
                _goalYawSpeed += (Shot.Wrap(goal.Yaw - last.Yaw) / Tick - _goalYawSpeed) * k;
                _goalPitchSpeed += ((goal.Pitch - last.Pitch) / Tick - _goalPitchSpeed) * k;
                _goalDistanceSpeed += (MathF.Log(goal.Distance / last.Distance) / Tick - _goalDistanceSpeed) * k;
            }
        }
        _lastGoal = goal;

        var (units, turnSpeed) = MostFollowed;
        if (_goalAimSpeed.Length() > units)
            _goalAimSpeed *= units / _goalAimSpeed.Length();
        _goalYawSpeed = Math.Clamp(_goalYawSpeed, -turnSpeed, turnSpeed);
        _goalPitchSpeed = Math.Clamp(_goalPitchSpeed, -turnSpeed, turnSpeed);
        _goalDistanceSpeed = Math.Clamp(_goalDistanceSpeed, -turnSpeed, turnSpeed);

        var c = Current;
        var aim = c.Aim;
        Channel(ref aim, ref _aimSpeed, goal.Aim, _goalAimSpeed, caps.Aim, caps.Settle);
        var turn = caps.Turn * MathF.PI / 180;
        var yaw = c.Yaw;
        Channel(ref yaw, ref _yawSpeed, yaw + Shot.Wrap(goal.Yaw - yaw), _goalYawSpeed, turn, caps.TurnSettle);
        var pitch = c.Pitch;
        Channel(ref pitch, ref _pitchSpeed, goal.Pitch, _goalPitchSpeed, turn, caps.TurnSettle);
        var log = MathF.Log(MathF.Max(c.Distance, 1e-3f));
        Channel(ref log, ref _distanceSpeed, MathF.Log(MathF.Max(goal.Distance, 1e-3f)), _goalDistanceSpeed, caps.Zoom, caps.ZoomSettle);
        var distance = MathF.Exp(log);
        if (distance < NearestDistance)
        {
            distance = NearestDistance;
            _distanceSpeed = MathF.Max(_distanceSpeed, 0);
        }
        Current = new Shot(aim, Shot.Wrap(yaw), pitch, distance);
        Moving = (goal.Aim - aim).Length() > 0.01f || MathF.Abs(Shot.Wrap(goal.Yaw - yaw)) > 0.002f || MathF.Abs(goal.Pitch - pitch) > 0.002f
            || MathF.Abs(goal.Distance - distance) > 0.01f * MathF.Max(distance, 1) || _aimSpeed.Length() > 0.05f;
    }

    private static void Channel(ref float x, ref float v, float goal, float goalSpeed, float most, float settle)
    {
        var e = goal - x;
        var want = goalSpeed + MathF.Sign(e) * MathF.Min(MathF.Sqrt(2 * most * MathF.Abs(e)), settle * MathF.Abs(e));
        v += Math.Clamp(want - v, -most * Tick, most * Tick);
        x += v * Tick;
    }

    private static void Channel(ref Vector3 x, ref Vector3 v, Vector3 goal, Vector3 goalSpeed, float most, float settle)
    {
        var e = goal - x;
        var length = e.Length();
        var want = goalSpeed + (length > 1e-6f ? e / length * MathF.Min(MathF.Sqrt(2 * most * length), settle * length) : Vector3.Zero);
        var change = want - v;
        var size = change.Length();
        if (size > most * Tick)
            change *= most * Tick / size;
        v += change;
        x += v * Tick;
    }
}
