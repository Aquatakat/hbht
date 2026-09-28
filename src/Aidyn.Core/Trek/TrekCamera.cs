using System.Numerics;

namespace Aidyn.Core.Trek;

/// <summary>
/// The outdoor camera (<c>Camera::Mode0</c>), and orbiting it with R (<c>FUN_800b0fac</c>).
/// </summary>
/// <remarks>
/// <para>
/// It aims 1.05 above the player, chasing that point at 1/12 of the gap a tick. Its goal is 5
/// behind the player, and 5 × sin 15° above the aim. Each frame it sets its position goal 5 back
/// along where it's looking now, nudges that toward the goal, and eases toward it at 1/16 a tick.
/// Because the goal is set anew each frame, the turn is proportional to the sine of the angle
/// off the player's back: it stalls when he faces it and slows as it arrives.
/// </para>
/// <para>
/// The nudge is 1/144 of the way while the player stands still. As he moves away from where he
/// stopped, it grows, reaching 1/16 at 3 units. How much a frame adds up to depends on the frame
/// rate, so it's matched to 30 fps (<see cref="Cadence"/>): from facing it, the camera takes
/// about a minute to come round behind a player standing still.
/// </para>
/// <para>
/// Orbiting keeps the camera 0 to 5 above the aim at a distance easing to 5, which tops out at
/// 45°, and pulls it in when a wall is behind. Outside that, <c>Mode0</c> doesn't avoid walls;
/// interiors use another mode (<c>Mode1</c>), which isn't ported yet.
/// </para>
/// Checked against the 1.0 ROM's machine code.
/// </remarks>
public sealed class TrekCamera
{
    /// <summary>R held (<c>holdCamera</c>).</summary>
    private enum HoldState { None, Starting, Holding, Releasing }

    public Vector3 Position;
    public Vector3 Aim;

    /// <summary>Where it's headed (<c>posTarget</c>).</summary>
    public Vector3 PositionGoal;

    /// <summary>The point above the player it aims at (<c>aimTarget</c>).</summary>
    public Vector3 AimGoal;

    /// <summary>The way it looks, in X and Z (<c>rotationXZ</c>). The stick steers relative to it.</summary>
    public Vector2 LookXZ = Vector2.UnitX;

    /// <summary>
    /// Following, the angle in degrees its goal is above the aim (<c>unk48</c>), kept between
    /// <see cref="MinPitch"/> and <see cref="MaxPitch"/>. Orbiting, ten times its height above
    /// where the orbit started.
    /// </summary>
    public float Pitch = 15;

    public float DefaultPitch = 15, MinPitch = 0, MaxPitch = 45;

    /// <summary>Degrees the orbit has turned it (<c>unk58</c>).</summary>
    public float OrbitAngle;

    /// <summary>How far behind it sits (<c>unk5c</c>), which camera objects can change for a while.</summary>
    public float Distance = 5;

    public float DefaultDistance = 5;

    /// <summary>It never gets nearer than this in X and Z while following (<c>unk64</c>).</summary>
    public float MinDistance = 3;

    /// <summary>How far above the player it aims (<c>camHeight</c>).</summary>
    public float Height = 1.05f;

    /// <summary>
    /// Where the player last stood still, raised by <see cref="Height"/> (<c>unk70</c>). The
    /// farther he is from it, the faster the camera swings round, up to this far (<c>unk7e</c>).
    /// </summary>
    private Vector3 _anchor;

    private const float AnchorReach = 3;

    /// <summary>Set until the player first moves or stands (<c>unk7c</c>): swing at the fastest.</summary>
    private bool _unanchored = true;

    /// <summary>Frames left to swing round quickly (<c>unk80</c>). Loading and dialogue set it.</summary>
    public int Snap;

    /// <summary>Whether it could see the aim last time it looked (<c>unk82</c>).</summary>
    private bool _clearView = true;

    private int _distanceTimer;
    private HoldState _hold;

    /// <summary>Places the camera behind a point, looking along <paramref name="look"/>, as a map load does.</summary>
    public TrekCamera(Vector3 target, Vector2 look)
    {
        LookXZ = Vector2.Normalize(look);
        Aim = AimGoal = target;
        PositionGoal = Position = Behind();
    }

    public Vector3 Look => Vector3.Normalize(Aim - Position);

    /// <summary>
    /// One 60 Hz tick, following <paramref name="target"/>, the player's sphere, from behind
    /// <paramref name="heading"/> (<c>Mode0</c>). The player's controls are read after it.
    /// </summary>
    public void Step(CollisionWorld world, Vector3 target, Vector2 heading, bool checkView = true)
    {
        var back = -heading;
        var before = Position;
        if (_distanceTimer != 0 && --_distanceTimer <= 0)
        {
            _distanceTimer = 0;
            Distance = DefaultDistance;
        }

        if (_hold != HoldState.None)
        {
            if (_hold != HoldState.Holding)
                Pitch = 0;
            AimGoal = target + new Vector3(0, Height, 0);
            Approach(ref Aim, AimGoal, 16, 0.25f);
            Orbiting(world, back, Distance, 0, 5);
            return;
        }

        AimGoal = target + new Vector3(0, Height, 0);
        Approach(ref Aim, AimGoal, 12, 5);
        var moved = Vector3.Distance(AimGoal, _anchor);
        if (AnchorReach < moved)
            _unanchored = true;
        if (0.25f < moved)
        {
            ResetOrbit();
            _unanchored = false;
        }
        Orient();
        PositionGoal = Behind();
        var goal = Swing(back);
        var rate = _unanchored ? 16 : MathF.Max(16, (AnchorReach - moved) / AnchorReach * 128 + 16);
        if (Snap != 0)
        {
            rate = 5;
            if (Vector3.Distance(PositionGoal, goal) < 0.5f && --Snap < 1)
                ResetOrbit();
        }
        Approach(ref PositionGoal, goal, GoalRate(rate), 5);

        var flat = new Vector3(Position.X - Aim.X, 0, Position.Z - Aim.Z);
        if (SpherePhysics.Normalize(ref flat) < MinDistance)
        {
            Position.X = Aim.X + flat.X * MinDistance;
            Position.Z = Aim.Z + flat.Z * MinDistance;
        }
        Approach(ref Position, PositionGoal, 16, 5);

        if (Snap < 2 && checkView)
            _clearView = !SpherePhysics.Sweep(world, Position, Aim, 0.25f, skipSeeThrough: false, out _, out _);
        if (SpherePhysics.Sweep(world, before, Position, 0.25f, skipSeeThrough: true, out _, out _) && Snap == 1 && _clearView)
            ResetOrbit();
        Orient();
    }

    /// <summary>
    /// R held with the stick at <paramref name="stick"/>, for the player at <paramref name="player"/>
    /// (<c>Actor::Move</c>). Right turns it one way at 3° a frame, and up raises it.
    /// </summary>
    public void Orbit(Vector3 player, Vector2 stick)
    {
        if (_hold == HoldState.None)
            _hold = HoldState.Starting;
        Anchor(player);
        OrbitAngle -= Cadence.PerTick(stick.X * 3);
        Pitch += Cadence.PerTick(stick.Y);
    }

    /// <summary>R not held (<c>Actor::Move</c>): stop orbiting and follow again.</summary>
    public void Follow()
    {
        if (_hold != HoldState.None)
            _hold = HoldState.Releasing;
        OrbitAngle = 0;
        Pitch = DefaultPitch;
    }

    /// <summary>The player is standing still at <paramref name="player"/> (<c>SetFeild70</c>).</summary>
    public void Anchor(Vector3 player)
    {
        _anchor = player + new Vector3(0, Height, 0);
        _unanchored = false;
    }

    /// <summary>Stop a quick swing, and if it can see the player, drop the orbit (<c>FUN_800b04ec</c>).</summary>
    private void ResetOrbit()
    {
        Snap = 0;
        if (_clearView)
        {
            OrbitAngle = 0;
            Pitch = DefaultPitch;
        }
    }

    private void Orient()
    {
        var flat = new Vector2(Aim.X - Position.X, Aim.Z - Position.Z);
        var length = MathF.Max(flat.Length(), 1e-6f);
        LookXZ = flat / length;
    }

    /// <summary>
    /// <see cref="Distance"/> back along where it looks, and <see cref="Pitch"/> up, after
    /// keeping the pitch in range (<c>FUN_800b050c</c>).
    /// </summary>
    private Vector3 Behind()
    {
        Pitch = Math.Clamp(Pitch, MinPitch, MaxPitch);
        return new Vector3(Aim.X - LookXZ.X * Distance, Aim.Y + MathF.Sin(Pitch * MathF.PI / 180) * Distance, Aim.Z - LookXZ.Y * Distance);
    }

    /// <summary>The goal: <see cref="Distance"/> along <paramref name="back"/> turned by the orbit (<c>FUN_800b05d0</c>).</summary>
    private Vector3 Swing(Vector2 back)
    {
        var (sin, cos) = MathF.SinCos(OrbitAngle * MathF.PI / 180);
        var x = cos * back.X + sin * back.Y;
        var z = cos * back.Y - sin * back.X;
        return new Vector3(Aim.X + x * Distance, Aim.Y + MathF.Sin(Pitch * MathF.PI / 180) * Distance, Aim.Z + z * Distance);
    }

    /// <summary>
    /// Orbiting (<c>FUN_800b0fac</c>): turned by <see cref="OrbitAngle"/> from where it was when R
    /// went down, easing to <paramref name="distance"/> away, and between
    /// <paramref name="low"/> and <paramref name="high"/> above the aim.
    /// </summary>
    private void Orbiting(CollisionWorld world, Vector2 back, float distance, float low, float high)
    {
        if (_hold is HoldState.Starting or HoldState.Releasing)
        {
            _hold = _hold == HoldState.Starting ? HoldState.Holding : HoldState.None;
            PositionGoal = Position;
        }
        var reach = new Vector2(Position.X - Aim.X, Position.Z - Aim.Z).Length();
        reach = MathF.Max(reach, 1e-6f);
        reach -= (reach - distance) / 32;

        var way = new Vector2(PositionGoal.X - Aim.X, PositionGoal.Z - Aim.Z);
        way /= MathF.Max(way.Length(), 1e-6f);
        var (sin, cos) = MathF.SinCos(OrbitAngle * MathF.PI / 180);
        way = new Vector2(cos * way.X + sin * way.Y, cos * way.Y - sin * way.X) * reach;
        Position = new Vector3(Aim.X + way.X, PositionGoal.Y, Aim.Z + way.Y);
        if (SpherePhysics.Sweep(world, Aim, Position, 0.25f, skipSeeThrough: true, out var hit, out var normal))
        {
            // Pull in toward the wall: an eighth of the way a frame.
            hit -= normal * 0.25f;
            var wall = new Vector2(hit.X - Aim.X, hit.Z - Aim.Z).Length();
            reach -= (reach - wall) * Cadence.CompoundPerTick(1f / 8);
            way = Vector2.Normalize(way) * reach;
            Position = new Vector3(Aim.X + way.X, PositionGoal.Y, Aim.Z + way.Y);
        }
        var flat = new Vector2(Position.X - Aim.X, Position.Z - Aim.Z);
        var length = MathF.Max(flat.Length(), 1e-6f);
        if (length < 0.5f)
        {
            flat /= length;
            Position.X = Aim.X + flat.X * 0.5f;
            Position.Z = Aim.Z + flat.Y * 0.5f;
        }

        Position.Y += Pitch / 10;
        if (Position.Y < Aim.Y + low)
        {
            Position.Y = Aim.Y + low;
            Pitch = (Position.Y - PositionGoal.Y) * 10;
        }
        if (Aim.Y + high < Position.Y)
        {
            Position.Y = Aim.Y + high;
            Pitch = (Position.Y - PositionGoal.Y) * 10;
        }
        Orient();
    }

    /// <summary>
    /// Moves <paramref name="point"/> <c>1/rate</c> of the way to <paramref name="target"/>, but
    /// no more than <paramref name="most"/> (<c>AdjustAim</c>, one tick).
    /// </summary>
    private static void Approach(ref Vector3 point, Vector3 target, float rate, float most)
    {
        var step = (point - target) / rate;
        var length = SpherePhysics.Normalize(ref step);
        point -= step * MathF.Min(length, most);
    }

    /// <summary>
    /// The per-tick rate for the goal's nudge that turns the camera as fast as a 30 fps frame
    /// does. Over a frame, the original nudges the goal <c>FrameShare(rate)</c> of the way, then
    /// eases the camera <c>FrameShare(16)</c> toward it, and the next frame starts from where the
    /// camera got to. Stepping every tick, the camera eases 1/16 a tick, so the nudge makes up the rest.
    /// </summary>
    internal static float GoalRate(float rate) =>
        Cadence.FrameTicks / (Cadence.FrameShare(rate) * Cadence.FrameShare(16) * 16);
}
