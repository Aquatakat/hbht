using Godot;

namespace Aidyn.Game;

/// <summary>
/// The player in trek mode: a capsule the size of Alaron's collision (radius 0.5, from the new
/// game setup) that walks relative to the camera and turns to face where it's going.
/// </summary>
/// <remarks>
/// A stand-in until the original's movement is ported from <c>PlayerMove.cpp</c>: the speeds are
/// Brenna's root motion for walking (about 1.9 units a second) and a guess for running, and
/// gravity is Godot's. The stick walks under 80% tilt and runs above, as in the original; on a
/// keyboard, holding Shift walks.
/// </remarks>
public partial class Player : CharacterBody3D
{
    public const float Radius = 0.5f;
    public const float Height = 1.8f;
    private const float WalkSpeed = 1.9f, RunSpeed = 5f, TurnRate = 10f;

    private readonly float _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
    private Node3D? _model;

    /// <summary>The camera's heading, which the stick moves relative to.</summary>
    public Func<float>? CameraYaw { get; set; }

    /// <summary>Set while the camera flies free, so the player stays put.</summary>
    public bool Frozen { get; set; }

    public ActorAnimator? Animator { get; set; }

    public override void _Ready()
    {
        Name = "Player";
        CollisionLayer = 0;
        CollisionMask = Collision.PlayerLayer;
        FloorMaxAngle = Mathf.DegToRad(50);
        FloorSnapLength = 0.3f;
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = Radius, Height = Height },
            Position = new Vector3(0, Height / 2, 0),
        });
    }

    /// <summary>The model, drawn at the player's feet.</summary>
    public void SetModel(Node3D model)
    {
        _model?.QueueFree();
        _model = model;
        AddChild(model);
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        var velocity = Velocity;
        if (!IsOnFloor())
            velocity.Y -= _gravity * dt;

        var input = Frozen ? Vector2.Zero : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var tilt = Math.Min(input.Length(), 1);
        var running = tilt > 0.8f && !Input.IsActionPressed("walk");
        var request = 0;
        if (tilt > 0.1f)
        {
            var yaw = CameraYaw?.Invoke() ?? 0;
            var direction = new Vector3(input.X, 0, input.Y).Normalized().Rotated(Vector3.Up, yaw);
            var speed = running ? RunSpeed : WalkSpeed;
            velocity.X = direction.X * speed;
            velocity.Z = direction.Z * speed;
            if (_model is not null)
            {
                var target = Mathf.Atan2(direction.X, direction.Z);
                _model.Rotation = new Vector3(0, Mathf.LerpAngle(_model.Rotation.Y, target, Math.Min(1, TurnRate * dt)), 0);
            }
            request = running ? 3 : 2;
        }
        else
        {
            velocity.X = 0;
            velocity.Z = 0;
        }
        if (Animator is not null)
            Animator.Request = request;

        Velocity = velocity;
        MoveAndSlide();
    }
}
