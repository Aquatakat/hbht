using System.Numerics;
using HBHT.Core.Physics;

namespace HBHT.Core.Cameras;

/// <summary>
/// One lean of the enhanced trek camera's yaw goal while it trails the player: the path's bend (slice 2) or a nudge toward
/// what he'd use or a hunter (slice 3). Each is bounded, and all together by <see cref="TrekCameraTuning.LeanBound"/>.
/// </summary>
public interface ITrekCameraLean
{
    /// <summary>The most this lean adds, either way, in degrees.</summary>
    float Bound { get; }

    /// <summary>
    /// How far to lean the yaw goal this tick, in degrees, positive turning the camera's place about him from +X toward +Z,
    /// for the player at <paramref name="player"/> travelling at <paramref name="travel"/> (X and Z, units a second).
    /// </summary>
    float Lean(Vector3 player, Vector2 travel, float yaw);

}

/// <summary>
/// The enhanced trek camera, Camera: Enhanced's in trek (docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced): a
/// boom from a pivot above the player, its yaw, pitch and length each easing toward a goal that changes continuously, at
/// capped speeds. It writes the follow camera's position, aim and look (<see cref="FollowCamera"/>), so whatever reads that
/// reads it, and whenever something else has moved the follow camera since (a camera object, a conversation, trek coming
/// back) it takes up from where the camera is and glides from there.
/// </summary>
/// <remarks>
/// <para>
/// <b>Trailing.</b> While he travels faster than <see cref="TrekCameraTuning.MovingSpeed"/>, the yaw's goal is behind his
/// travel, plus the leans; standing, it holds. Turning the camera (R, the right stick, the mouse) moves the yaw and the pitch
/// at once, and the automatic yaw and pitch wait <see cref="TrekCameraTuning.InputWait"/> after the last turn.
/// </para>
/// <para>
/// <b>Room.</b> How far the yaw may turn a second goes from <see cref="TrekCameraTuning.OpenTurn"/> with room all round to
/// <see cref="TrekCameraTuning.TightTurn"/> with none: the room is the shorter of the arm's free length and the mean of the
/// free lengths to either side of the pivot, against the boom's length. Walking at the lens in a corridor, it swings behind
/// him in about a second; outdoors it backs off and drifts round. A view he didn't choose (an arrival, an authored shot
/// let go, a conversation's end) swings behind at the tight rate as he sets off, if it's more than
/// <see cref="TrekCameraTuning.UnchosenSwing"/> off.
/// </para>
/// <para>
/// <b>The spring arm.</b> The arm is the boom's length, or the nearest wall's distance less the camera's sphere and a margin,
/// whichever is shorter: in at once, so the camera is never past a wall, and out at <see cref="TrekCameraTuning.ArmOut"/>.
/// The collision is swept from either side (<see cref="SpherePhysics.SweepEitherSide"/>), its triangles being one-sided, and
/// the drawn world is raycast where the host gives one, interiors' collision having almost no ceilings. As the arm shortens
/// the pivot rises above his head and the pitch rises (close quarters), so he sits low in the frame.
/// </para>
/// </remarks>
public sealed partial class EnhancedTrekCamera
{
    private const float Tick = 1f / 60;
    private const float Degree = MathF.PI / 180;

    /// <summary>
    /// The share of the gap his eased travel closes a tick: slow enough that a body pressed into a wall, jittering back and
    /// forth, doesn't read as travel (the yaw then turned after the jitter, and the stick with it).
    /// </summary>
    private const float TravelEase = 0.05f;

    private Vector3 _pivot;
    private float _yaw, _yawSpeed;
    private float _pitch, _pitchSpeed;
    private float _arm, _free, _freeLevel;
    private float _length, _basePitch;
    private float _userPitch;
    private float _sinceInput;
    /// <summary>
    /// The view is one the player didn't choose (an arrival, an authored shot let go, a conversation's end): the next time he
    /// moves with the camera far off behind his travel, it swings behind at the tight rate whatever the room.
    /// </summary>
    private bool _unchosen;

    /// <summary>The swing behind of an unchosen view is under way.</summary>
    private bool _swingingBehind;
    private int _swingSide;
    private Vector2 _travel;
    private Vector3 _lastPlayer;
    private bool _placed;
    private Vector3 _wrotePosition, _wroteAim;

    public TrekCameraTuning Tuning { get; set; } = TrekCameraTuning.Default;

    /// <summary>What leans the yaw's goal as he travels, summed: trek adds the path's bend and the nudges.</summary>
    public List<ITrekCameraLean> Leans { get; } = [];

    /// <summary>Sneaking's framing, which replaces the trailing yaw while it holds.</summary>
    public SneakFraming Sneak { get; } = new();

    /// <summary>The ticks it has stepped, which the leans tell a pause by.</summary>
    public int Ticks { get; private set; }

    /// <summary>What the ground's slope ahead adds to the pitch's goal now, in degrees.</summary>
    public float SlopePitch => _slope;

    private float _slope;

    /// <summary>What the boom turns about: above his head, lifted in close quarters.</summary>
    public Vector3 Pivot => _pivot;

    /// <summary>The arm's length now.</summary>
    public float Arm => _arm;

    /// <summary>The room it judged last tick, from 0 (none) to 1 (all round).</summary>
    public float Room { get; private set; } = 1;

    /// <summary>The boom's yaw in degrees: the way from the pivot to the camera, from +X toward +Z.</summary>
    public float Yaw => _yaw / Degree;

    /// <summary>Its pitch in degrees above level.</summary>
    public float Pitch => _pitch;

    /// <summary>Whether the automatic yaw is waiting after the player turned the camera.</summary>
    public bool Waiting => _sinceInput < Tuning.InputWait;

    /// <summary>The boom's goal length and pitch for where he is.</summary>
    private (float Length, float Pitch) Place(bool interior) =>
        interior ? (Tuning.InteriorLength, Tuning.InteriorPitch) : (Tuning.OutdoorLength, Tuning.OutdoorPitch);

    /// <summary>
    /// The player arrived (a teleport, a fall's rescue): behind him, looking along <paramref name="heading"/>, the arm fitted,
    /// at once; or, if the teleporter names a camera point that is already behind him (the view from it within 90° of his
    /// heading), from where <paramref name="camera"/> was put there. A point in front of him, looking back at him, is passed
    /// over for behind him, unless <paramref name="authored"/>: a camera object takes the camera there and moves on from
    /// the point, so it's kept. The follow camera has arrived first (<see cref="FollowCamera.Arrive"/>).
    /// </summary>
    public void Arrive(FollowCamera camera, CollisionWorld world, VisibleWorld? visible, Vector3 player, Vector2 heading, bool interior, bool kept,
        bool authored = false)
    {
        (_length, _basePitch) = Place(interior);
        _userPitch = 0;
        _sinceInput = Tuning.InputWait;
        _travel = Vector2.Zero;
        _lastPlayer = player;
        _swingSide = 0;
        var view = new Vector2(player.X - camera.Position.X, player.Z - camera.Position.Z);
        if (kept && (authored || Vector2.Dot(view, heading) > 0))
        {
            Adopt(camera);
            return;
        }
        _unchosen = true;
        PlaceFitted(world, visible, player, heading);
        Write(camera);
        camera.Cut();
    }

    /// <summary>
    /// One 60 Hz tick following the player's sphere at <paramref name="player"/>, who faces <paramref name="heading"/>, in a
    /// chunk that's an interior or not, with the player turning it by <paramref name="turn"/> (the orbit's stick, a frame's,
    /// or nothing).
    /// </summary>
    public void Step(FollowCamera camera, CollisionWorld world, VisibleWorld? visible, Vector3 player, Vector2 heading, bool interior,
        Vector2 turn)
    {
        var t = Tuning;
        if (!_placed)
        {
            (_length, _basePitch) = Place(interior);
            _lastPlayer = player;
            _sinceInput = t.InputWait;
            PlaceFitted(world, visible, player, heading);
        }
        else if (camera.Position != _wrotePosition || camera.Aim != _wroteAim)
        {
            Adopt(camera);
        }

        // His travel, eased over a few ticks, unless he jumped.
        var moved = new Vector2(player.X - _lastPlayer.X, player.Z - _lastPlayer.Z);
        _lastPlayer = player;
        if (moved.Length() > 1)
            moved = Vector2.Zero;
        _travel += (moved / Tick - _travel) * TravelEase;
        Ticks++;
        Sneak.Step(player, t);
        StepSlope(world, player);

        // The length and pitch of where he is, eased; sneaking past a hunter, longer and higher.
        var (length, pitch) = Place(interior);
        length += Sneak.Weight * t.SneakLength;
        pitch += Sneak.Weight * t.SneakPitch;
        var ease = 1 - MathF.Exp(-Tick / t.PlaceEase);
        _length += (length - _length) * ease;
        _basePitch += (pitch - _basePitch) * ease;

        // The player's turn wins at once.
        if (turn != Vector2.Zero)
        {
            _sinceInput = 0;
            _yaw = Wrap(_yaw + Cadence.PerTick(t.StickTurn * turn.X) * Degree);
            var raise = Cadence.PerTick(t.StickPitch * turn.Y);
            _userPitch += raise;
            _pitch += raise;
            _yawSpeed = 0;
            _pitchSpeed = 0;
        }
        else
        {
            _sinceInput += Tick;
        }
        _userPitch = Math.Clamp(_userPitch, t.LowestPitch - _basePitch, t.HighestPitch - _basePitch);
        var auto = !Waiting;
        if (auto)
            _userPitch -= MathF.Sign(_userPitch) * MathF.Min(MathF.Abs(_userPitch), 10 * Tick);

        // Close quarters go by the walls about him, the arm's room at the place's own pitch: a ceiling over a raised boom
        // would otherwise raise it further.
        var shortened = Math.Clamp(1 - _freeLevel / MathF.Max(_length, 1e-3f), 0, 1);
        StepPivot(world, visible, player, shortened);
        Room = JudgeRoom(world, visible);
        StepYaw(world, visible, player, heading, auto);
        // Close quarters part him from the lens by height, not by pitching down: the pitch is capped so a point
        // AheadSight ahead of him at head height stays inside the lens's top, and the lift does the rest.
        var pitchGoal = MathF.Min(_basePitch + t.ClosePitch * shortened + _slope, SightCap(player, heading)) + _userPitch;
        // A ceiling over the raised boom, nearer than the walls: it comes down to the place's pitch instead.
        if (_free < _freeLevel - 0.1f && _pitch > _basePitch + _userPitch)
            pitchGoal = _basePitch + _userPitch;
        pitchGoal = Math.Clamp(pitchGoal, t.LowestPitch, t.HighestPitch);
        Channel(ref _pitch, ref _pitchSpeed, pitchGoal, t.PitchSpeed, t.PitchAccel, 3);
        _pitch = Math.Clamp(_pitch, t.LowestPitch, t.HighestPitch);
        Fit(world, visible, at: false);
        Write(camera);
    }

    /// <summary>
    /// The highest pitch at which the point <see cref="TrekCameraTuning.AheadSight"/> ahead of him, at head height, stays
    /// <see cref="TrekCameraTuning.SightMargin"/> inside the top of a lens <see cref="TrekCameraTuning.LensHeight"/> high,
    /// from where the camera is now.
    /// </summary>
    private float SightCap(Vector3 player, Vector2 heading)
    {
        var t = Tuning;
        var head = player + new Vector3(0, t.Height, 0);
        var eye = _pivot + Direction * _arm;
        var ahead = heading.LengthSquared() > 1e-8f ? Vector2.Normalize(heading) : Vector2.Zero;
        var point = head + new Vector3(ahead.X, 0, ahead.Y) * t.AheadSight;
        var flat = new Vector2(point.X - eye.X, point.Z - eye.Z).Length();
        var below = MathF.Atan2(eye.Y - point.Y, MathF.Max(flat, 0.1f)) / Degree;
        return below + t.LensHeight / 2 - t.SightMargin;
    }

    /// <summary>
    /// How much of the player's model at <paramref name="player"/> (his sphere's centre) is drawn, 0 to 1, by how near a camera at
    /// <paramref name="eye"/> is to his head: whole from <see cref="TrekCameraTuning.FadeFrom"/> out, gone by
    /// <see cref="TrekCameraTuning.FadeGone"/>.
    /// </summary>
    public float Fade(Vector3 eye, Vector3 player)
    {
        var t = Tuning;
        var near = Vector3.Distance(eye, player + new Vector3(0, t.Height, 0));
        return Math.Clamp((near - t.FadeGone) / MathF.Max(t.FadeFrom - t.FadeGone, 1e-3f), 0, 1);
    }

    /// <summary>The pivot closes on its place above him, lifted as the arm shortens, but kept under the drawn ceiling.</summary>
    private void StepPivot(CollisionWorld world, VisibleWorld? visible, Vector3 player, float shortened)
    {
        var t = Tuning;
        var goal = PivotGoal(world, visible, player, shortened);
        var step = (goal - _pivot) * t.PivotRate;
        var most = t.PivotSpeed * Tick;
        if (step.Length() > most)
            step *= most / step.Length();
        _pivot += step;
    }

    /// <summary>The pivot's place: above his head, lifted as the arm shortens, under the drawn ceiling and the collision's.</summary>
    private Vector3 PivotGoal(CollisionWorld world, VisibleWorld? visible, Vector3 player, float shortened)
    {
        var t = Tuning;
        var head = player + new Vector3(0, t.Height, 0);
        var goal = head + new Vector3(0, t.CloseLift * shortened, 0);
        var top = goal + new Vector3(0, t.Radius, 0);
        if (goal.Y > head.Y && visible?.RaycastSolid(head, top) is { } ceiling)
            goal.Y = MathF.Max(head.Y, head.Y + (top.Y - head.Y) * ceiling.Share - t.Radius);
        // And under the collision's, where it has one: a sloped roof, a beam.
        top = goal + new Vector3(0, t.Radius, 0);
        if (goal.Y > head.Y && SpherePhysics.SweepEitherSide(world, head, top, 0.01f, skipSeeThrough: true) is { } roof)
            goal.Y = MathF.Max(head.Y, head.Y + (top.Y - head.Y) * roof - t.Radius);
        return goal;
    }

    /// <summary>Placed at once behind him: the arm fitted, then the pivot lifted and the pitch capped as close quarters ask, and fitted again.</summary>
    private void PlaceFitted(CollisionWorld world, VisibleWorld? visible, Vector3 player, Vector2 heading)
    {
        PlaceBehind(player, heading);
        Fit(world, visible, at: true);
        _pivot = PivotGoal(world, visible, player, Math.Clamp(1 - _freeLevel / MathF.Max(_length, 1e-3f), 0, 1));
        _pitch = MathF.Min(_pitch, SightCap(player, heading));
        Fit(world, visible, at: true);
    }

    /// <summary>
    /// The room it has, 0 to 1: the shorter of the arm's free length and the mean of the free lengths to the boom's either
    /// side, against its length.
    /// </summary>
    private float JudgeRoom(CollisionWorld world, VisibleWorld? visible)
    {
        var side = new Vector3(-MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
        var sides = (Free(world, visible, side, _length) + Free(world, visible, -side, _length)) / 2;
        return Math.Clamp(MathF.Min(_free, sides) / MathF.Max(_length, 1e-3f), 0, 1);
    }

    /// <summary>
    /// The yaw toward behind his travel, with the leans, while he moves and the player isn't turning it; else it holds. A
    /// view he didn't choose, far off behind him as he sets off, swings behind at the tight rate whatever the room.
    /// </summary>
    private void StepYaw(CollisionWorld world, VisibleWorld? visible, Vector3 player, Vector2 heading, bool auto)
    {
        var t = Tuning;
        var goal = _yaw;
        if (auto && _travel.Length() > t.MovingSpeed)
        {
            float lean = 0;
            foreach (var l in Leans)
                lean += Math.Clamp(l.Lean(player, _travel, Yaw), -l.Bound, l.Bound);
            lean = Math.Clamp(lean, -t.LeanBound, t.LeanBound);
            goal = MathF.Atan2(-_travel.Y, -_travel.X) + lean * Degree;
        }
        // Sneaking past a hunter: toward his far side from it, as quickly as its weight allows.
        var sneak = auto ? Sneak.Weight : 0;
        if (sneak > 0)
            goal += sneak * Wrap(Sneak.Yaw(player) - goal);
        var e = Wrap(goal - _yaw);
        if (!auto)
        {
            // The player turned it: the view is his.
            _unchosen = _swingingBehind = false;
        }
        else if (_unchosen && _travel.Length() > t.MovingSpeed)
        {
            _swingingBehind = MathF.Abs(e) > t.UnchosenSwing * Degree;
            _unchosen = false;
        }
        if (_swingingBehind && MathF.Abs(e) < 10 * Degree)
            _swingingBehind = false;
        // Walking at the lens, the way round is chosen once, by the way it's turning or else the side with more room, and kept.
        if (MathF.Abs(e) > 150 * Degree)
        {
            if (_swingSide == 0)
            {
                if (MathF.Abs(_yawSpeed) > 0.05f)
                {
                    _swingSide = MathF.Sign(_yawSpeed);
                }
                else
                {
                    var left = new Vector3(MathF.Cos(_yaw + MathF.PI / 3), 0, MathF.Sin(_yaw + MathF.PI / 3));
                    var right = new Vector3(MathF.Cos(_yaw - MathF.PI / 3), 0, MathF.Sin(_yaw - MathF.PI / 3));
                    _swingSide = Free(world, visible, left, _length) >= Free(world, visible, right, _length) ? 1 : -1;
                }
            }
            if (_swingSide > 0 && e < 0)
                e += 2 * MathF.PI;
            else if (_swingSide < 0 && e > 0)
                e -= 2 * MathF.PI;
        }
        else
        {
            _swingSide = 0;
        }
        // How tight it turns: by the room it lacks, and only as far as it has far round to go (from 20° to 90° off), so
        // trailing him round a bend stays calm and only walking at the lens swings quickly.
        var tight = (1 - Room) * Math.Clamp((MathF.Abs(e) / Degree - 20) / 70, 0, 1);
        if (_swingingBehind)
            tight = MathF.Max(tight, Math.Clamp((MathF.Abs(e) / Degree - 10) / 30, 0, 1));
        var most = (t.OpenTurn + (t.TightTurn - t.OpenTurn) * tight) * Degree;
        var accel = (t.OpenTurnAccel + (t.TightTurnAccel - t.OpenTurnAccel) * tight) * Degree;
        most = MathF.Max(most, sneak * t.SneakTurn * Degree);
        accel = MathF.Max(accel, sneak * t.SneakTurnAccel * Degree);
        var yaw = _yaw;
        Channel(ref yaw, ref _yawSpeed, yaw + e, most, accel, t.TurnSettle + sneak * 4);
        _yaw = Wrap(yaw);
    }

    /// <summary>
    /// The spring arm: the boom's length or the nearest wall's distance less the sphere and the margin, whichever is shorter;
    /// in at once, out at <see cref="TrekCameraTuning.ArmOut"/>, or straight to it with <paramref name="at"/>.
    /// </summary>
    private void Fit(CollisionWorld world, VisibleWorld? visible, bool at)
    {
        // Out as far as the arm is, which a camera taken up from elsewhere can have longer than the boom.
        var reach = MathF.Max(_length, _arm);
        var free = Free(world, visible, Direction, reach);
        _free = MathF.Min(free, _length);
        _freeLevel = Free(world, visible, DirectionAt(_basePitch + _userPitch), _length);
        if (at || free < MathF.Min(_arm, reach - 1e-3f))
            // A wall nearer than the camera: in at once.
            _arm = at ? _free : free;
        else if (_free < _arm)
            // Longer than the boom with no wall in the way: in gently.
            _arm = MathF.Max(_free, _arm - Tuning.ArmOut * Tick);
        else
            _arm = MathF.Min(_free, _arm + Tuning.ArmOut * Tick);
    }

    /// <summary>How far the camera's sphere can go from the pivot along <paramref name="way"/>, up to <paramref name="length"/>.</summary>
    private float Free(CollisionWorld world, VisibleWorld? visible, Vector3 way, float length)
    {
        var t = Tuning;
        var free = length;
        if (SpherePhysics.SweepEitherSide(world, _pivot, _pivot + way * length, t.Radius, skipSeeThrough: true) is { } share)
            free = share * length - t.Margin;
        // The sphere's sweep meets faces only, so a corner or an edge can slip between: a thin ray along the middle too.
        if (SpherePhysics.SweepEitherSide(world, _pivot, _pivot + way * length, 0.01f, skipSeeThrough: true) is { } line)
            free = MathF.Min(free, line * length - t.Radius - t.Margin);
        // The drawn surface is kept a sphere's radius off, measured square to it: along a ray that meets it at a slant, further.
        var reach = length + 4 * t.Radius;
        if (visible?.RaycastSolid(_pivot, _pivot + way * reach) is { } drawn)
        {
            var slant = MathF.Max(MathF.Abs(Vector3.Dot(way, drawn.Normal)), 0.25f);
            free = MathF.Min(free, drawn.Share * reach - (t.Radius + t.Margin) / slant);
        }
        return MathF.Max(free, 0);
    }

    /// <summary>The way from the pivot to the camera.</summary>
    private Vector3 Direction => DirectionAt(_pitch);

    /// <summary>The way from the pivot along the boom's yaw at <paramref name="pitch"/>.</summary>
    private Vector3 DirectionAt(float pitch)
    {
        var (sinPitch, cosPitch) = MathF.SinCos(pitch * Degree);
        return new Vector3(MathF.Cos(_yaw) * cosPitch, sinPitch, MathF.Sin(_yaw) * cosPitch);
    }

    private void PlaceBehind(Vector3 player, Vector2 heading)
    {
        _placed = true;
        _pivot = player + new Vector3(0, Tuning.Height, 0);
        var back = heading.LengthSquared() > 1e-8f ? -Vector2.Normalize(heading) : -Vector2.UnitX;
        _yaw = MathF.Atan2(back.Y, back.X);
        _pitch = _basePitch;
        _yawSpeed = _pitchSpeed = 0;
        _arm = _length;
    }

    /// <summary>Takes up from where something else left the camera: its aim the pivot, and the boom to its position.</summary>
    private void Adopt(FollowCamera camera)
    {
        _placed = true;
        _pivot = camera.Aim;
        var offset = camera.Position - camera.Aim;
        var length = offset.Length();
        if (length > 1e-4f)
        {
            _yaw = MathF.Atan2(offset.Z, offset.X);
            _pitch = Math.Clamp(MathF.Asin(Math.Clamp(offset.Y / length, -1, 1)) / Degree, Tuning.LowestPitch, Tuning.HighestPitch);
            _arm = length;
        }
        _yawSpeed = _pitchSpeed = 0;
        _swingSide = 0;
        // Close quarters are judged again at its next fit.
        _free = _freeLevel = _length;
        // A view taken up from elsewhere is one the player didn't choose.
        _unchosen = true;
        _swingingBehind = false;
    }

    private void Write(FollowCamera camera)
    {
        camera.PlaceAt(_pivot + Direction * _arm);
        // With the arm short or at nothing, the aim is kept a unit ahead of the eye along the boom, so the look stays the boom's.
        camera.AimAt(_pivot - Direction * MathF.Max(0, 1 - _arm));
        camera.Orient();
        _wrotePosition = camera.Position;
        _wroteAim = camera.Aim;
    }

    /// <summary>
    /// A tick of one channel toward <paramref name="goal"/>: its speed toward it no more than <paramref name="most"/>, nor than
    /// lets it stop there at <paramref name="accel"/>, nor than <paramref name="settle"/> times what's left; its speed
    /// changing by no more than <paramref name="accel"/> a second. <see cref="CappedMotion"/>'s channel, with its speed capped.
    /// </summary>
    private static void Channel(ref float x, ref float v, float goal, float most, float accel, float settle)
    {
        var e = goal - x;
        var want = MathF.Sign(e) * MathF.Min(most, MathF.Min(MathF.Sqrt(2 * accel * MathF.Abs(e)), settle * MathF.Abs(e)));
        v += Math.Clamp(want - v, -accel * Tick, accel * Tick);
        x += v * Tick;
    }

    private static float Wrap(float angle)
    {
        angle %= 2 * MathF.PI;
        return angle > MathF.PI ? angle - 2 * MathF.PI : angle < -MathF.PI ? angle + 2 * MathF.PI : angle;
    }
}
