using System.Numerics;
using Aidyn.Core.Physics;

namespace Aidyn.Core.Cameras;

/// <summary>
/// The interiors' camera (<c>Camera::Mode1</c>, with the arguments <c>Camera::ProcessGameCamera</c>
/// gives it). Checked against the 1.0 ROM's machine code.
/// </summary>
/// <remarks>
/// <para>
/// Rather than swinging round behind the player, it hangs back on a leash. Its goal is 1.5 above the
/// point it aims at, and is only dragged along to stay 2 to 4 from it in X and Z. It eases toward its
/// goal at 1/32 of the way a tick, but no more than 0.1, and turns round the aim toward it by a
/// degree a tick. It never comes nearer than 2, and it can't pass through a wall: it stops 0.25 off it.
/// </para>
/// <para>
/// When it has caught up with its goal and can't see the aim from there, its goal jumps to 3 behind
/// the player, or to the wall in the way, and it doesn't try that again for 5 seconds.
/// </para>
/// <para>
/// Each step loops over the frame's ticks or acts once a frame on what it's moving toward, so
/// stepping it every tick matches the original at any frame rate.
/// </para>
/// </remarks>
public sealed partial class FollowCamera
{
    // Camera::ProcessGameCamera's arguments for Mode1, read from the ROM.
    private const float InteriorNear = 2, InteriorFar = 4, InteriorRise = 1.5f, InteriorBehind = 3;
    private const float InteriorAimRate = 16, InteriorRate = 32, InteriorAimMost = 0.25f, InteriorMost = 0.1f;
    private const float InteriorOrbitLow = 0.5f, InteriorOrbitHigh = 2.5f;

    /// <summary>How long it waits before jumping behind the player again, in ticks.</summary>
    private const int InteriorRescueTicks = 300;

    /// <summary>Ticks until it may jump behind the player again (<c>unk84</c>).</summary>
    private int _rescueWait;

    private void StepInterior(CollisionWorld world, Vector3 target, Vector2 heading)
    {
        var back = -heading;
        var before = Position;
        AimGoal = target + new Vector3(0, Height, 0);
        Approach(ref Aim, AimGoal, InteriorAimRate, InteriorAimMost);
        if (_hold != HoldState.None)
        {
            Orbiting(world, back, InteriorBehind, InteriorOrbitLow, InteriorOrbitHigh);
            return;
        }

        PositionGoal.Y = AimGoal.Y + InteriorRise;
        Approach(ref Position, PositionGoal, InteriorRate, InteriorMost);

        // The leash: its goal stays 2 to 4 from the aim's in X and Z.
        var leash = new Vector2(PositionGoal.X - AimGoal.X, PositionGoal.Z - AimGoal.Z);
        var length = leash.Length();
        if (length > 1e-6f)
            leash /= length;
        if (length < InteriorNear)
            PositionGoal = new Vector3(AimGoal.X + InteriorNear * leash.X, PositionGoal.Y, AimGoal.Z + InteriorNear * leash.Y);
        if (InteriorFar < length)
            PositionGoal = new Vector3(AimGoal.X + InteriorFar * leash.X, PositionGoal.Y, AimGoal.Z + InteriorFar * leash.Y);

        var flat = new Vector3(Position.X - Aim.X, 0, Position.Z - Aim.Z);
        if (SpherePhysics.Normalize(ref flat) < InteriorNear)
        {
            Position.X = Aim.X + flat.X * InteriorNear;
            Position.Z = Aim.Z + flat.Z * InteriorNear;
        }

        // It can't go through a wall: it stops a sphere's radius off it.
        if (SpherePhysics.Sweep(world, before, Position, 0.25f, skipSeeThrough: true, out var wall, out var normal))
            Position = wall + normal * 0.25f;

        // A degree a tick round the aim toward its goal, keeping its distance (FUN_800ab23c).
        var from = new Vector2(Position.X - Aim.X, Position.Z - Aim.Z);
        var to = new Vector2(PositionGoal.X - Aim.X, PositionGoal.Z - Aim.Z);
        var reach = from.Length();
        if (reach > 1e-6f && to.LengthSquared() > 1e-12f)
        {
            var turned = Facing.Turn(from, to, MathF.PI / 180) * reach;
            Position.X = Aim.X + turned.X;
            Position.Z = Aim.Z + turned.Y;
        }

        if (_rescueWait > 0)
            _rescueWait--;
        // Caught up, but the aim is out of sight from its goal: jump behind the player.
        if (_rescueWait == 0 && Vector3.Distance(Position, PositionGoal) < 1
            && SpherePhysics.Sweep(world, Aim, PositionGoal, 0.25f, skipSeeThrough: true, out _, out _))
        {
            _rescueWait = InteriorRescueTicks;
            var way = back.LengthSquared() > 1e-12f ? Vector2.Normalize(back) : Vector2.Zero;
            var behind = new Vector3(AimGoal.X + InteriorBehind * way.X, PositionGoal.Y, AimGoal.Z + InteriorBehind * way.Y);
            PositionGoal = SpherePhysics.Sweep(world, Aim, behind, 0.25f, skipSeeThrough: true, out var stop, out _) ? stop : behind;
        }
        Orient();
    }
}
