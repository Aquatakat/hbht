using Godot;

namespace Aidyn.Game;

/// <summary>
/// A third-person camera behind the player, pulled in by walls it can't see through. Hold the right
/// mouse button, or use Q and E or the right stick, to turn it. F toggles a free-flying camera for
/// looking around, which WASD, Space and C move.
/// </summary>
/// <remarks>A stand-in for the original's SM64-style camera (<c>camera.cpp</c>), which comes later in M1.</remarks>
public partial class CameraRig : Node3D
{
    private const float Distance = 5f, Height = 1.6f, TurnSpeed = 2.5f, FlySpeed = 15f, MouseSpeed = 0.005f;

    private readonly SpringArm3D _arm = new() { SpringLength = Distance, CollisionMask = Collision.CameraLayer, Margin = 0.2f };
    private readonly Camera3D _camera = new() { Fov = 45, Near = 0.1f, Far = 1000 };
    private readonly Camera3D _fly = new() { Fov = 45, Near = 0.1f, Far = 2000 };

    /// <summary>The node the camera follows.</summary>
    public Node3D? Target { get; set; }

    public float Yaw { get; set; }
    public float Pitch { get; set; } = -0.3f;
    public bool Flying { get; private set; }

    public override void _Ready()
    {
        Name = "Camera";
        _arm.AddChild(_camera);
        AddChild(_arm);
        AddChild(_fly);
        _camera.MakeCurrent();
    }

    /// <summary>Places the orbit camera by the command line's <c>--distance</c>, if given.</summary>
    public void SetDistance(float distance) => _arm.SpringLength = distance;

    public void ToggleFly()
    {
        Flying = !Flying;
        if (Flying)
        {
            _fly.GlobalTransform = _camera.GlobalTransform;
            _fly.MakeCurrent();
        }
        else
        {
            _camera.MakeCurrent();
        }
    }

    public Camera3D Current => Flying ? _fly : _camera;

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m && (m.ButtonMask & MouseButtonMask.Right) != 0)
        {
            if (Flying)
            {
                var rotation = _fly.Rotation;
                rotation.Y -= m.Relative.X * MouseSpeed;
                rotation.X = Mathf.Clamp(rotation.X - m.Relative.Y * MouseSpeed, -1.5f, 1.5f);
                _fly.Rotation = rotation;
            }
            else
            {
                Yaw -= m.Relative.X * MouseSpeed;
                Pitch = Mathf.Clamp(Pitch - m.Relative.Y * MouseSpeed, -1.3f, 0.6f);
            }
        }
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        if (Flying)
        {
            var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
            var up = (Input.IsActionPressed("fly_up") ? 1 : 0) - (Input.IsActionPressed("fly_down") ? 1 : 0);
            var basis = _fly.GlobalTransform.Basis;
            var speed = Input.IsActionPressed("walk") ? FlySpeed * 4 : FlySpeed;
            _fly.GlobalPosition += (basis.X * input.X + basis.Z * input.Y + Vector3.Up * up) * speed * dt;
            return;
        }

        Yaw += Input.GetAxis("camera_right", "camera_left") * TurnSpeed * dt;
        if (Target is not null)
            GlobalPosition = Target.GlobalPosition + new Vector3(0, Height, 0);
        Rotation = new Vector3(0, Yaw, 0);
        _arm.Rotation = new Vector3(Pitch, 0, 0);
    }
}
