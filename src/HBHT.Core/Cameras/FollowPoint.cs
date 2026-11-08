using System.Numerics;

namespace HBHT.Core.Cameras;

/// <summary>
/// The point a camera follows a body by: drawn after it each tick as a critically damped spring, so a stride that moves the body unevenly
/// (a step a frame, a root that stops and pushes off) moves the point evenly, and it settles as the body stops, never past it. Under way
/// it trails the body by twice the spring's time times its speed.
/// </summary>
public sealed class FollowPoint
{
    private const float Tick = 1f / 60;

    /// <summary>Where the point is.</summary>
    public Vector3 At { get; private set; }

    /// <summary>Its velocity, units a second.</summary>
    public Vector3 Speed { get; private set; }

    /// <summary>How far it moved in the last tick.</summary>
    public Vector3 Moved { get; private set; }

    /// <summary>Puts the point on <paramref name="at"/>, at rest.</summary>
    public void Reset(Vector3 at)
    {
        At = at;
        Speed = Vector3.Zero;
        Moved = Vector3.Zero;
    }

    /// <summary>A tick drawing the point after a body at <paramref name="body"/>, the spring's time <paramref name="time"/> seconds; 0 puts it on the body.</summary>
    public void Step(Vector3 body, float time)
    {
        var was = At;
        if (time <= 0)
        {
            Speed = (body - At) / Tick;
            At = body;
        }
        else
        {
            var w = 1 / time;
            Speed += (w * w * (body - At) - 2 * w * Speed) * Tick;
            At += Speed * Tick;
        }
        Moved = At - was;
    }
}
