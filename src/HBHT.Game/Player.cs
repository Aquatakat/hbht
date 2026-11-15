using HBHT.Core.Movement;
using Godot;

namespace HBHT.Game;

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

    /// <summary>The model's meshes, which the point lights near him light.</summary>
    public GeometryInstance3D[] Meshes { get; private set; } = [];

    /// <summary>The model, drawn at the player's feet.</summary>
    public void SetModel(Node3D model)
    {
        _model?.QueueFree();
        _model = model;
        Meshes = [.. Nodes.FindAll<GeometryInstance3D>(model)];
        AddChild(model);
        Sync();
    }

    /// <summary>How much of him is drawn, 0 to 1 (<see cref="Core.Trek.TrekLoop.PlayerFade"/>).</summary>
    private float _fade = 1;

    /// <summary>
    /// Draws him at <paramref name="alpha"/>, 0 to 1, as scenery fades (<see cref="Views.N64Material.SetFade"/>): through his
    /// materials' faded copies while partly drawn, and not at all at 0.
    /// </summary>
    public void Fade(float alpha)
    {
        alpha = Mathf.Clamp(alpha, 0, 1);
        if (alpha == _fade)
            return;
        var faded = alpha is > 0 and < 1;
        if (faded != _fade is > 0 and < 1)
        {
            foreach (var mesh in Meshes)
            {
                if (mesh is MeshInstance3D instance)
                    Views.N64Material.SetFaded(instance, faded);
            }
        }
        if (_model is not null)
            _model.Visible = alpha > 0;
        Views.N64Material.SetFade(Meshes, faded ? alpha : 1);
        _fade = alpha;
    }

    /// <summary>Where his feet were as the last tick was drawn, or null after a <see cref="Snap"/>.</summary>
    private System.Numerics.Vector3? _last;

    /// <summary>
    /// Moves the node to where the motion has him. Call after each tick. The core moves him once a 30 fps frame, as
    /// <c>ProcessPlayers</c> does, so he's drawn at the mean of his last two ticks' places: a frame's step is drawn evenly over
    /// its two ticks, half a tick late, rather than all on one. Modern movement moves him every tick, and he's drawn where he is.
    /// </summary>
    public void Sync()
    {
        var feet = Motion.Feet;
        var drawn = Motion.Movement == MovementMode.Modern ? feet : ((_last ?? feet) + feet) / 2;
        Position = drawn.ToGodot();
        _last = feet;
        if (_model is not null)
            _model.Rotation = new Vector3(0, Mathf.Atan2(Motion.Heading.X, Motion.Heading.Y), 0);
    }

    /// <summary>Draws him where he is now, not partway from where he was, after a jump; then resets the interpolation.</summary>
    public void Snap()
    {
        _last = null;
        Sync();
        ResetPhysicsInterpolation();
    }
}
