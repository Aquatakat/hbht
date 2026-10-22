using System.Numerics;
using Aidyn.Core.Physics;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Cameras;

/// <summary>
/// The camera that follows the player in trek: outdoors (<c>Camera::Mode0</c>), in interiors
/// (<c>Camera::Mode1</c>, see <see cref="CameraMode.Interior"/>), and orbiting it with R
/// (0x800a72bc). Camera objects take it over for a while (<see cref="CameraMove"/>).
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
/// interiors' does.
/// </para>
/// Checked against the 1.0 ROM's machine code.
/// </remarks>
public sealed partial class FollowCamera
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

    /// <summary>
    /// Outdoors or in an interior (<c>camera_mode</c>). A teleport chooses it by the chunk it arrives
    /// in (<see cref="Arrive"/>); walking from chunk to chunk keeps it.
    /// </summary>
    public CameraMode Mode { get; set; }

    /// <summary>
    /// Whether a camera object has the camera this tick, so the next <see cref="Step"/> leaves it be
    /// (<c>camera_mode</c> 2, then <c>RevertMode</c>).
    /// </summary>
    public bool Held { get; private set; }

    /// <summary>Places the camera behind a point, looking along <paramref name="look"/>, as a map load does.</summary>
    public FollowCamera(Vector3 target, Vector2 look) => Place(target, look);

    /// <summary>
    /// Places the camera behind <paramref name="target"/> again, looking along <paramref name="look"/>, as it was made: where the
    /// player is set down on the ground as a game starts, after his body was made above it.
    /// </summary>
    public void Place(Vector3 target, Vector2 look)
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
        if (Held)
        {
            Held = false;
            return;
        }
        if (Mode == CameraMode.Interior)
        {
            StepInterior(world, target, heading);
            return;
        }
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
    public void Orbit(Vector3 player, Vector2 stick, bool moving = false)
    {
        if (_hold == HoldState.None)
            _hold = HoldState.Starting;
        _carry = moving;
        Anchor(player);
        OrbitAngle -= Cadence.PerTick(stick.X * 3);
        Pitch += Cadence.PerTick(stick.Y);
    }

    /// <summary>
    /// The player is moving while the camera orbits, which the original never does (a port addition for a second
    /// stick): the orbit carries with him, rather than turning about the point it started from.
    /// </summary>
    private bool _carry;

    /// <summary>Where the aim was when the orbit last stepped, to carry the camera by how far it has moved.</summary>
    private Vector3 _orbitAim;

    /// <summary>
    /// R not held (<c>Actor::Move</c>): stop orbiting and follow again. Outdoors that puts the pitch
    /// back; in interiors it zeroes it, where it's only the orbit's.
    /// </summary>
    public void Follow()
    {
        if (_hold != HoldState.None)
            _hold = HoldState.Releasing;
        _carry = false;
        OrbitAngle = 0;
        Pitch = Mode == CameraMode.Outdoor ? DefaultPitch : 0;
    }

    /// <summary>
    /// The mode a teleport into <paramref name="chunk"/> chooses (<c>TeleportPlayer</c>): interior where
    /// its terrain doesn't draw the sky, the original's sky type 4.
    /// </summary>
    public static CameraMode ModeFor(PluginSet plugins, ChunkEntry chunk) =>
        TerrainRecord.Find(plugins, chunk.Terrain) is { SkyDrawn: false }
            ? CameraMode.Interior
            : CameraMode.Outdoor;

    private bool _cut;

    /// <summary>The camera jumped rather than moved, so a view shouldn't blend from where it was.</summary>
    public void Cut() => _cut = true;

    /// <summary>Whether the camera jumped since the last call, as a cut or a teleport does.</summary>
    public bool TakeCut()
    {
        var cut = _cut;
        _cut = false;
        return cut;
    }

    /// <summary>A camera object has the camera this tick (<c>Camera::SetMode(cam, 2)</c>).</summary>
    public void Hold() => Held = true;

    /// <summary>Puts the camera at <paramref name="position"/>, and its goal there (<c>Camera::SetPos</c>).</summary>
    public void PlaceAt(Vector3 position) => Position = PositionGoal = position;

    /// <summary>Aims it at <paramref name="aim"/>, and its goal there (<c>Camera::SetAim</c>).</summary>
    public void AimAt(Vector3 aim) => Aim = AimGoal = aim;

    /// <summary>
    /// Keeps it <paramref name="distance"/> behind the player, but no nearer than
    /// <see cref="MinDistance"/>, for the next half second outdoors (a camera object's flag 0x100).
    /// </summary>
    public void HoldDistance(float distance)
    {
        Distance = MathF.Max(distance, MinDistance);
        _distanceTimer = 30;
    }

    /// <summary>
    /// The player went through a teleporter from <paramref name="from"/> to <paramref name="to"/>,
    /// now facing <paramref name="heading"/> (<c>TeleportPlayer</c>). The camera moves with him, in
    /// <paramref name="mode"/>, and stops orbiting. Then it's at <paramref name="point"/> if the
    /// teleporter names one; otherwise in an interior it's 4 in front of him and 3 up, looking back
    /// at him, and outdoors it's behind him, as a map load puts it. Outdoors it swings round
    /// quickly for a few frames.
    /// </summary>
    public void Arrive(Vector3 from, Vector3 to, Vector2 heading, CameraMode mode, Vector3? point)
    {
        Mode = mode;
        Held = false;
        // ApplyZoneVelocity moves the camera with the player, and GiveCameraToPlayer stops the orbit.
        var offset = to - from;
        Position += offset;
        Aim += offset;
        PositionGoal += offset;
        AimGoal += offset;
        OrbitAngle = 0;
        Pitch = DefaultPitch;
        Anchor(to);
        var aim = to + new Vector3(0, Height, 0);
        if (point is { } p)
        {
            PlaceAt(p);
        }
        else if (mode == CameraMode.Interior)
        {
            // The original's facing is the opposite of the heading, which this subtracts.
            PlaceAt(new Vector3(to.X + heading.X * 4, to.Y + 3, to.Z + heading.Y * 4));
            AimAt(aim);
        }
        else
        {
            AimAt(aim);
            LookXZ = Vector2.Normalize(heading);
            PlaceAt(Behind());
        }
        Snap = 5;
    }

    /// <summary>
    /// One tick framing two bodies talking, by their spheres' centres (<c>ProcessPlayers</c> with the
    /// <c>ACTOR_800</c> bodies, and <c>CreateFocusFromMultiple</c>, checked in 1.0's code): it follows
    /// their midpoint from (|h.z|, −|h.x|), where h is the half of the gap between them, which is
    /// always toward −X and +Z, at (|h| + 4) / tan 33.5° but at least 5, swinging quickly. A camera
    /// object holding the camera keeps it where it is instead, as the user saw in the original.
    /// </summary>
    public void Frame(CollisionWorld world, Vector3 a, Vector3 b)
    {
        if (Held)
        {
            Held = false;
            return;
        }
        var focus = (a + b) * 0.5f;
        var h = focus - a;
        var away = new Vector2(-MathF.Abs(h.Z), MathF.Abs(h.X));
        away = SafeNormalize.Of(away, -LookXZ);
        Distance = MathF.Max((h.Length() + ConversationMargin) / MathF.Tan(ConversationHalfAngle), ConversationMinDistance);
        Snap = 1;
        Anchor(focus);
        Step(world, focus, -away);
        Distance = DefaultDistance;
    }

    /// <summary>The margin <see cref="Frame"/> leaves around the two.</summary>
    private const float ConversationMargin = 4;

    /// <summary>Half the 75° field of view, less 8°, which <see cref="Frame"/> fits the two in.</summary>
    private const float ConversationHalfAngle = (75 - 8) * 0.5f * MathF.PI / 180;

    private const float ConversationMinDistance = 5;

    /// <summary>
    /// Gives the camera back to the player at <paramref name="player"/> after a conversation
    /// (<c>GiveCameraToPlayer</c>): its pitch back to the default, and a quick swing for 5 frames.
    /// </summary>
    public void Release(Vector3 player)
    {
        Anchor(player);
        Pitch = Mode == CameraMode.Outdoor ? DefaultPitch : 0;
        Snap = 5;
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

    /// <summary>Turns <see cref="LookXZ"/> to the way it looks (<c>Camera::Orient</c>).</summary>
    internal void Orient()
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
        return new Vector3(Aim.X - LookXZ.X * Distance, Aim.Y + MathF.Sin(Degrees.ToRadians(Pitch)) * Distance, Aim.Z - LookXZ.Y * Distance);
    }

    /// <summary>The goal: <see cref="Distance"/> along <paramref name="back"/> turned by the orbit (<c>FUN_800b05d0</c>).</summary>
    private Vector3 Swing(Vector2 back)
    {
        var (sin, cos) = MathF.SinCos(Degrees.ToRadians(OrbitAngle));
        var turned = Facing.Rotate(back, sin, cos);
        return new Vector3(Aim.X + turned.X * Distance, Aim.Y + MathF.Sin(Degrees.ToRadians(Pitch)) * Distance, Aim.Z + turned.Y * Distance);
    }

    /// <summary>
    /// Orbiting (0x800a72bc): turned by <see cref="OrbitAngle"/> from where it was when R
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
        else if (_carry)
        {
            PositionGoal += Aim - _orbitAim;
        }
        _orbitAim = Aim;
        var reach = new Vector2(Position.X - Aim.X, Position.Z - Aim.Z).Length();
        reach = MathF.Max(reach, 1e-6f);
        reach -= (reach - distance) / 32;

        var way = new Vector2(PositionGoal.X - Aim.X, PositionGoal.Z - Aim.Z);
        way /= MathF.Max(way.Length(), 1e-6f);
        var (sin, cos) = MathF.SinCos(Degrees.ToRadians(OrbitAngle));
        way = Facing.Rotate(way, sin, cos) * reach;
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
    internal static void Approach(ref Vector3 point, Vector3 target, float rate, float most)
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

/// <summary>The follow camera's modes (<c>camera_mode</c>).</summary>
public enum CameraMode
{
    /// <summary>Outdoors (<c>Camera::Mode0</c>).</summary>
    Outdoor,

    /// <summary>
    /// In interiors (<c>Camera::Mode1</c>): chunks whose terrain doesn't draw the sky
    /// (<see cref="TerrainRecord.SkyDrawn"/>), the original's sky type 4.
    /// </summary>
    Interior,
}
