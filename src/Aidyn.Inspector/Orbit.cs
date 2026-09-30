using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// A camera orbiting a target, for the 3D views: drag to turn, right- or middle-drag to pan, and
/// scroll to zoom.
/// </summary>
public sealed class Orbit
{
    public Vector3 Target { get; set; }
    public float Yaw { get; set; } = 0.6f;
    public float Pitch { get; set; } = -0.35f;
    public float Distance { get; set; } = 5;

    /// <summary>Puts the camera where the orbit says, with its near and far planes to suit.</summary>
    public void Apply(Camera3D camera)
    {
        var offset = new Vector3(0, 0, Distance).Rotated(Vector3.Right, Pitch).Rotated(Vector3.Up, Yaw);
        camera.Position = Target + offset;
        camera.Near = Distance / 1000;
        camera.Far = Distance * 100;
        camera.LookAt(Target, Vector3.Up);
    }

    /// <summary>Frames a sphere: aims at its centre from far enough to see it whole.</summary>
    public void Frame(Camera3D camera, Vector3 centre, float radius)
    {
        Target = centre;
        Distance = Mathf.Max(radius, 0.01f) / Mathf.Sin(Mathf.DegToRad(camera.Fov / 2)) * 1.1f;
        Apply(camera);
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
