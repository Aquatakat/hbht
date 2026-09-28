using Aidyn.Core.Trek;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The player in trek mode: draws Alaron where <see cref="PlayerMotion"/> puts him, facing his
/// heading. The movement, collision and speeds are the core's port of the original.
/// </summary>
public partial class Player : Node3D
{
    private Node3D? _model;

    public Player(PlayerMotion motion)
    {
        Motion = motion;
        Name = "Player";
    }

    public Player() : this(new PlayerMotion(System.Numerics.Vector3.Zero, System.Numerics.Vector2.UnitY))
    {
    }

    public PlayerMotion Motion { get; }

    public ActorAnimator? Animator { get; set; }

    /// <summary>The model, drawn at the player's feet.</summary>
    public void SetModel(Node3D model)
    {
        _model?.QueueFree();
        _model = model;
        AddChild(model);
        Sync();
    }

    /// <summary>Moves the node to where the motion has him. Call after each tick.</summary>
    public void Sync()
    {
        Position = Motion.Feet.ToGodot();
        if (_model is not null)
            _model.Rotation = new Vector3(0, Mathf.Atan2(Motion.Heading.X, Motion.Heading.Y), 0);
    }
}
