using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// A camera orbiting a target, for the 3D views: drag to turn, right- or middle-drag to pan, and
/// scroll to zoom. With <see cref="PointUnder"/>, scrolling moves the camera toward what's under
/// the cursor instead, and the target becomes that, so turning and panning suit what's in view.
/// </summary>
public sealed class Orbit
{
    /// <summary>What's under a point on the view, if anything, for scrolling toward it.</summary>
    public Func<Vector2, Vector3?>? PointUnder { get; set; }

    /// <summary>Fixed clip planes. Otherwise they follow the distance, which suits a model but not a world.</summary>
    public float? Near { get; set; }
    public float? Far { get; set; }

    public Vector3 Target { get; set; }
    public float Yaw { get; set; } = 0.6f;
    public float Pitch { get; set; } = -0.35f;
    public float Distance { get; set; } = 5;

    /// <summary>Puts the camera where the orbit says, with its near and far planes to suit.</summary>
    public void Apply(Camera3D camera)
    {
        var offset = new Vector3(0, 0, Distance).Rotated(Vector3.Right, Pitch).Rotated(Vector3.Up, Yaw);
        camera.Position = Target + offset;
        camera.Near = Near ?? Distance / 1000;
        camera.Far = Far ?? Distance * 100;
        camera.LookAt(Target, Vector3.Up);
    }

    /// <summary>Frames a sphere: aims at its centre from far enough to see it whole.</summary>
    public void Frame(Camera3D camera, Vector3 centre, float radius)
    {
        Target = centre;
        Distance = Mathf.Max(radius, 0.01f) / Mathf.Sin(Mathf.DegToRad(camera.Fov / 2)) * 1.1f;
        Apply(camera);
    }

    /// <summary>Moves the camera and its target together, as a free camera flies, without turning.</summary>
    public void Fly(Camera3D camera, Vector3 by)
    {
        Target += by;
        Apply(camera);
    }

    /// <summary>
    /// Moves the camera a fraction of the way toward what's under a point on the view, or away for a
    /// negative fraction, without turning it, and makes that the target. With nothing under it, the
    /// camera flies that fraction of the target's distance along the cursor's ray.
    /// </summary>
    private void Dolly(Camera3D camera, Vector2 point, float fraction)
    {
        var from = camera.GlobalPosition;
        var forward = -camera.GlobalBasis.Z;
        if (PointUnder!(point) is not { } under)
        {
            var flown = from + camera.ProjectRayNormal(point) * Distance * fraction;
            Distance *= 1 - fraction;
            Target = flown + forward * Distance;
            return;
        }
        var moved = from + (under - from) * fraction;
        Distance = Mathf.Max((under - moved).Dot(forward), 0.01f);
        Target = moved + forward * Distance;
    }

    /// <summary>Turns, pans or zooms for a mouse event. Returns whether it did.</summary>
    public bool Handle(InputEvent e, Camera3D camera)
    {
        switch (e)
        {
            case InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Left) != 0:
                Yaw -= m.Relative.X * 0.01f;
                Pitch = Mathf.Clamp(Pitch - m.Relative.Y * 0.01f, -1.55f, 1.55f);
                break;
            case InputEventMouseMotion m when (m.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0:
                var basis = camera.GlobalTransform.Basis;
                Target += (-basis.X * m.Relative.X + basis.Y * m.Relative.Y) * Distance * 0.0015f;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel when PointUnder is not null:
                Dolly(camera, wheel.Position, wheel.ButtonIndex == MouseButton.WheelUp ? 0.2f : -0.25f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Distance *= 0.9f;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Distance /= 0.9f;
                break;
            default:
                return false;
        }
        Apply(camera);
        return true;
    }
}
