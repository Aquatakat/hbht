using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Movement;
using HBHT.Core.Physics;

namespace HBHT.Core.Trek;

/// <summary>How trek put the player back after he fell out of the world (<see cref="TrekLoop.LastRescue"/>).</summary>
public enum FallRescue
{
    /// <summary>Set down on the ground at his X and Z, as the original does.</summary>
    SetDown,

    /// <summary>At a breadcrumb where he stood, the port's.</summary>
    Breadcrumb,

    /// <summary>The original's lift, with nowhere else left: set down from on high, or left at its height to fall again.</summary>
    Lifted,
}

public sealed partial class TrekLoop
{
    /// <summary>The height the original lifts him to (<c>ConfirmPlayerWithinZone</c>).</summary>
    public const float Lift = 50;

    /// <summary><see cref="Breadcrumbs.Kept"/> as the last rescue left it: while it stands, the next rescue isn't a set-down.</summary>
    private int? _rescuedAt;

    /// <summary>How the player was last put back after falling out of the world, or null if he hasn't been.</summary>
    public FallRescue? LastRescue { get; private set; }

    /// <summary>
    /// Whether there's ground that faces up just under the player, for a breadcrumb kept before he has stepped, as trek is built.
    /// </summary>
    private bool OnGround()
    {
        var at = Player.Sphere.Position;
        return SpherePhysics.Sweep(Chunks.World, at, at - new Vector3(0, 0.2f, 0), Player.Sphere.Radius, false, out _, out var normal)
            && normal.Y > 0.5f;
    }

    /// <summary>
    /// The player fell out of the world (<see cref="PlayerMotion.FellOut"/>). The original's <c>ConfirmPlayerWithinZone</c>
    /// (<c>0x8000c078</c>) lifts him to 50 and sets him down from on high at his X and Z (<see cref="Body.SetDown"/> without a
    /// height), which leaves him at 50 to fall again where it finds no ground: a fall that never ends, as the user has seen in
    /// the original. The port sets him down so first, where it finds ground above <see cref="PlayerMotion.Floor"/>, but not again
    /// until a breadcrumb has been kept since the last rescue, since he may have fallen through where it put him; otherwise at
    /// the newest breadcrumb where he stood that no rescue has tried (<see cref="Breadcrumbs.Rescue"/>), facing as he did there
    /// with the camera's eye where it was; and only once every one has been tried, the original's lift. The user's choice.
    /// </summary>
    private void Rescue()
    {
        var motion = Player;
        var fell = motion.Sphere.Position;
        var radius = motion.Sphere.Radius;
        var again = _rescuedAt == Breadcrumbs.Kept;
        _rescuedAt = Breadcrumbs.Kept;
        Vector3? eye = null;
        if (!again && Body.SetDown(Chunks.World, fell, radius, Game.Random.World) is (var ground, true) && ground.Y >= PlayerMotion.Floor)
        {
            motion.Sphere.Position = ground;
            LastRescue = FallRescue.SetDown;
        }
        else if (Breadcrumbs.Rescue(Chunks.MapSet.Id) is { } crumb && Chunks.MapSet.Chunk(crumb.Row, crumb.Column) is { } chunk)
        {
            var origin = Chunks.Origin(chunk);
            motion.Sphere.Position = origin + crumb.Local;
            motion.Heading = crumb.Heading;
            eye = origin + crumb.Eye;
            LastRescue = FallRescue.Breadcrumb;
            _host.Transition(new Transition.Say("Fell out of the world: back where you last stood."));
        }
        else
        {
            Chunks.Update(fell with { Y = Lift });
            motion.Sphere.Position = Body.SetDown(Chunks.World, fell with { Y = Lift }, radius, Game.Random.World).Position;
            LastRescue = FallRescue.Lifted;
        }
        motion.Sphere.Velocity = Vector3.Zero;
        var at = motion.Sphere.Position;
        Chunks.Update(at);
        var here = Chunks.ChunkAt(at);
        Enter(here);
        Use.ClearCameras();
        Camera.Arrive(fell, at, motion.Heading, here is null ? Camera.Mode : FollowCamera.ModeFor(Game.Plugins, here), eye);
        _host.Arrived();
    }
}
