using Aidyn.Core.Cameras;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Shows the view from the core's <see cref="FollowCamera"/>, and a free-flying camera for looking
/// around, which F toggles and WASD, Space and C move. The mouse orbits the follow camera while Camera Hold is held (the right
/// button by default), or always with the mouse on Camera (<see cref="MouseLook"/>); dragging with the right button turns the free one.
/// </summary>
public partial class CameraRig : Node3D
{
    private const float FlySpeed = 15f, MouseSpeed = 0.005f;

    // The original's far plane, where the fog is full. Its near plane is 1.
    private readonly Camera3D _camera = new() { Name = "Follow", Fov = 45, Near = 0.1f, Far = SkyView.Far };
    // Moved every frame rather than every tick, so not interpolated.
    private readonly Camera3D _fly = new() { Name = "Fly", Fov = 45, Near = 0.1f, Far = 2000, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
    private Vector2 _drag;

    public FollowCamera? Follow { get; set; }

    /// <summary>The follow camera's far plane: the original's 180 in trek, 125 in a cut-scene.</summary>
    public float Far
    {
        get => _camera.Far;
        set => _camera.Far = value;
    }

    /// <summary>For placing the free camera from the command line: radians, as Godot's rotations.</summary>
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public bool Flying { get; private set; }

    /// <summary>Whether the mouse, captured, always turns the follow camera: Camera takes it, and trek or a battle runs.</summary>
    public bool MouseLook { get; set; }

    /// <summary>Whether the mouse turns the follow camera now: always with <see cref="MouseLook"/>, else while Camera Hold is held.</summary>
    public bool MouseSteers => MouseLook || Input.IsActionPressed(Core.Controls.GameControls.CameraHold);

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

    /// <summary>The mouse movement, in pixels, since the last call, while it steers.</summary>
    public Vector2 TakeDrag()
    {
        var drag = _drag;
        _drag = Vector2.Zero;
        return drag;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventMouseMotion m)
            return;
        if (Flying)
        {
            if ((m.ButtonMask & MouseButtonMask.Right) != 0)
            {
                var rotation = _fly.Rotation;
                rotation.Y -= m.Relative.X * MouseSpeed;
                rotation.X = Mathf.Clamp(rotation.X - m.Relative.Y * MouseSpeed, -1.5f, 1.5f);
                _fly.Rotation = rotation;
            }
        }
        else if (MouseSteers)
        {
            _drag += m.Relative;
        }
    }

    public override void _Process(double delta)
    {
        if (!Flying)
            return;
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var up = (Input.IsActionPressed("fly_up") ? 1 : 0) - (Input.IsActionPressed("fly_down") ? 1 : 0);
        var speed = Input.IsActionPressed("walk") ? FlySpeed * 4 : FlySpeed;
        _fly.GlobalPosition += FreeFly.Direction(_fly.GlobalTransform.Basis, input.X, up, input.Y) * speed * (float)delta;
    }
}
