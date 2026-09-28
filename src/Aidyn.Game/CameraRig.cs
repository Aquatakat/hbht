using Aidyn.Core.Cameras;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Shows the view from the core's <see cref="FollowCamera"/>, and a free-flying camera for looking
/// around, which F toggles and WASD, Space and C move. Dragging with the right mouse button orbits
/// the follow camera, or turns the free one.
/// </summary>
public partial class CameraRig : Node3D
{
    private const float FlySpeed = 15f, MouseSpeed = 0.005f;

    private readonly Camera3D _camera = new() { Name = "Follow", Fov = 45, Near = 0.1f, Far = 1000 };
    // Moved every frame rather than every tick, so not interpolated.
    private readonly Camera3D _fly = new() { Name = "Fly", Fov = 45, Near = 0.1f, Far = 2000, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
    private Vector2 _drag;

    public FollowCamera? Follow { get; set; }

    /// <summary>For placing the free camera from the command line: radians, as Godot's rotations.</summary>
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public bool Flying { get; private set; }

    /// <summary>Whether the right mouse button is down, which holds the follow camera's orbit.</summary>
    public bool Dragging { get; private set; }

    public override void _Ready()
    {
        Name = "Camera";
        AddChild(_camera);
        AddChild(_fly);
        _camera.MakeCurrent();
    }

    public Camera3D Current => Flying ? _fly : _camera;

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

    /// <summary>Puts the view where the follow camera is. Call after each tick.</summary>
    public void Sync()
    {
        if (Follow is null)
            return;
        var from = Follow.Position.ToGodot();
        var to = Follow.Aim.ToGodot();
        if (!from.IsEqualApprox(to))
            _camera.LookAtFromPosition(from, to, Vector3.Up);
    }

    /// <summary>The mouse movement, in pixels, since the last call, while dragging.</summary>
    public Vector2 TakeDrag()
    {
        var drag = _drag;
        _drag = Vector2.Zero;
        return drag;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right } button)
        {
            Dragging = button.Pressed;
        }
        else if (e is InputEventMouseMotion m && (m.ButtonMask & MouseButtonMask.Right) != 0)
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
                _drag += m.Relative;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!Flying)
            return;
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var up = (Input.IsActionPressed("fly_up") ? 1 : 0) - (Input.IsActionPressed("fly_down") ? 1 : 0);
        var basis = _fly.GlobalTransform.Basis;
        var speed = Input.IsActionPressed("walk") ? FlySpeed * 4 : FlySpeed;
        _fly.GlobalPosition += (basis.X * input.X + basis.Z * input.Y + Vector3.Up * up) * speed * (float)delta;
    }
}
