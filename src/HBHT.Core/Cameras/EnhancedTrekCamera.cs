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

    private Vector3 _pivot, _pivotSpeed;
    private float _yaw, _yawSpeed;
    private float _pitch, _pitchSpeed;
    private float _arm, _armSpeed, _free, _freeLevel;
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

    /// <summary>Where his travel last put the yaw's goal, which it settles on once he stands; none since the view was last set.</summary>
    private float? _restGoal;
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

    /// <summary>The ticks the arm has been pulled in at once by more than 0.1 since it was made: a wall it didn't see coming.</summary>
    public int Cuts { get; private set; }

    /// <summary>How much the follow is quickened to turn the boom clear of a wall it's about to meet, 0 to 1.</summary>
    public float Urgency { get; private set; }

    /// <summary>His travel as the camera reads it, eased, units a second.</summary>
    public Vector2 Travel => _travel;

    /// <summary>How far the yaw was off its goal last tick, in degrees.</summary>
    public float YawOff { get; private set; }

    /// <summary>Its pitch in degrees above level.</summary>
    public float Pitch => _pitch;

    /// <summary>Whether the automatic yaw is waiting after the player turned the camera.</summary>
    public bool Waiting => _sinceInput < Tuning.InputWait;

    /// <summary>The boom's goal length and pitch for where he is.</summary>
    private (float Length, float Pitch) Place(bool interior) =>
        interior ? (Tuning.InteriorLength, Tuning.InteriorPitch) : (Tuning.OutdoorLength, Tuning.OutdoorPitch);

    /// <summary>
    /// The player arrived (a teleport, a fall's rescue). A teleporter's own camera point, <paramref name="kept"/>, is an
    /// authored frame: taken up from where <paramref name="camera"/> was put there, the arm pulled in only if a wall is
    /// nearer. Otherwise the arrival frame, the user's design of Nov 14, 2026: the place's own length at its pitch, from
    /// behind him if the arm reaches it there, else the first angle round from behind, 20° at a time either way (the roomier
    /// side first), where the arm reaches it (but for <see cref="TrekCameraTuning.ArrivalShortfall"/>) with him in sight; if
    /// none, the angle with the longest arm; and if even that is shorter than <see cref="TrekCameraTuning.ArrivalShortest"/>
    /// of the length (a closet), behind him lifted over his head. As a cut. The follow camera has arrived first
    /// (<see cref="FollowCamera.Arrive"/>).
    /// </summary>
    /// <param name="door">Through a door: false for a fall's rescue, which goes behind him as before, the way he was walking.</param>
    public void Arrive(FollowCamera camera, CollisionWorld world, VisibleWorld? visible, Vector3 player, Vector2 heading, bool interior, bool kept,
        bool door = true)
    {
        (_length, _basePitch) = Place(interior);
        _headY = player.Y + Tuning.Height;
        _userPitch = 0;
        _sinceInput = Tuning.InputWait;
        _travel = Vector2.Zero;
        _lastPlayer = player;
        _swingSide = 0;
        _swingingBehind = false;
        _restGoal = null;
        (_wedge, _wedgeRaw, _wedgeSide) = (0, 0, 0);
        if (kept)
        {
            Adopt(camera);
            var free = Free(world, visible, Direction, _arm);
            if (free < _arm)
                _arm = free;
            _shortened = Shortened;
            // Kept where it frames him: many points are only where the original's follow camera starts from, below his
            // head or at his feet, and it leaves them the next tick.
            if (Sees(world, visible, player) && Framed(player))
            {
                Arrival = new TrekArrival(TrekArrivalKind.Authored, 0, _arm, false);
                Write(camera);
                camera.Cut();
                return;
            }
            (_length, _basePitch) = Place(interior);
        }
        _unchosen = true;
        if (door)
        {
            PlaceArrival(world, visible, player, heading);
        }
        else
        {
            PlaceFitted(world, visible, player, heading);
            Arrival = new TrekArrival(TrekArrivalKind.Behind, 0, _arm, _arm >= _length * (1 - Tuning.ArrivalShortfall));
        }
        Write(camera);
        camera.Cut();
    }

    /// <summary>How the last arrival was framed: the angle chosen off behind him, the arm, and whether it reached the length.</summary>
    public TrekArrival Arrival { get; private set; }

    private void PlaceArrival(CollisionWorld world, VisibleWorld? visible, Vector3 player, Vector2 heading)
    {
        var t = Tuning;
        var ahead = heading.LengthSquared() > 1e-8f ? Vector2.Normalize(heading) : Vector2.UnitX;
        var behind = MathF.Atan2(-ahead.Y, -ahead.X);
        var want = _length * (1 - t.ArrivalShortfall);
        var tried = new Dictionary<float, (float Arm, bool Seen, bool Framed)>();
        (float Arm, bool Seen, bool Framed) Try(float off)
        {
            if (tried.TryGetValue(off, out var known))
                return known;
            // Judged by the arm from above his head, and seen from there as well as from the lifted place: a lift over a
            // railing isn't room, and the pivot settles back as soon as he moves.
            var arm = PlaceAtYaw(world, visible, player, behind + off * Degree);
            var seen = Sees(world, visible, player);
            var framed = Framed(player);
            var (pivot, placed) = (_pivot, _arm);
            (_pivot, _arm) = (player + new Vector3(0, t.Height, 0), arm);
            seen &= Sees(world, visible, player);
            (_pivot, _arm) = (pivot, placed);
            return tried[off] = (arm, seen, framed);
        }
        // In bands round from behind him (rear, side, front), each tried at the place's length and then close, against the
        // wall, before the next: within a band nearest behind first, and of the two sides the longer.
        (float From, float To)[] bands = [(-1, t.RearBand), (t.RearBand, t.SideBand), (t.SideBand, 180.5f)];
        foreach (var (from, to) in bands)
        {
            foreach (var close in new[] { false, true })
            {
                for (var off = 0f; off <= 180.5f; off += t.ArrivalStep)
                {
                    if (off <= from || off > to)
                        continue;
                    (float Off, float Arm)? pick = null;
                    foreach (var side in off is 0 or >= 180 ? [1f] : new[] { 1f, -1f })
                    {
                        var (arm, seen, framed) = Try(side * off);
                        var fits = seen && (close ? arm >= t.CloseFloor && framed : arm >= want);
                        if (fits && (pick is not { } p || arm > p.Arm))
                            pick = (side * off, arm);
                    }
                    if (pick is { } chosen)
                    {
                        PlaceAtYaw(world, visible, player, behind + chosen.Off * Degree);
                        Arrival = new TrekArrival(TrekArrival.KindOf(chosen.Off), chosen.Off, chosen.Arm, !close, close);
                        return;
                    }
                }
            }
        }
        var best = tried.Where(x => x.Value.Seen).Select(x => (Off: x.Key, x.Value.Arm)).DefaultIfEmpty((Off: 0f, Arm: -1f)).OrderByDescending(x => x.Arm).First();
        if (best.Arm >= t.ArrivalShortest * _length)
        {
            PlaceAtYaw(world, visible, player, behind + best.Off * Degree);
            Arrival = new TrekArrival(TrekArrival.KindOf(best.Off), best.Off, _arm, false, true);
            return;
        }
        PlaceAtYaw(world, visible, player, behind);
        Arrival = new TrekArrival(TrekArrivalKind.Lifted, 0, _arm, false);
    }

    /// <summary>Placed at once at <paramref name="yaw"/> about him, fitted as close quarters ask (<see cref="PlaceFitted"/>).</summary>
    private float PlaceAtYaw(CollisionWorld world, VisibleWorld? visible, Vector3 player, float yaw) =>
        PlaceFitted(world, visible, player, new Vector2(-MathF.Cos(yaw), -MathF.Sin(yaw)));

    /// <summary>Whether his chest is well inside the lens from where the camera is now: within 15° of the look up and down, 30° across.</summary>
    private bool Framed(Vector3 player)
    {
        var eye = _pivot + Direction * _arm;
        var aim = _pivot - Direction * MathF.Max(0, 1 - _arm);
        var forward = Vector3.Normalize(aim - eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var to = player + new Vector3(0, 0.5f, 0) - eye;
        var depth = Vector3.Dot(to, forward);
        return depth > 0.3f && MathF.Abs(Vector3.Dot(to, up)) / depth < MathF.Tan(15 * Degree)
            && MathF.Abs(Vector3.Dot(to, right)) / depth < MathF.Tan(30 * Degree);
    }

    /// <summary>Whether the camera where it is now sees his chest: no collision, either side, nor drawn solid between.</summary>
    private bool Sees(CollisionWorld world, VisibleWorld? visible, Vector3 player)
    {
        var eye = _pivot + Direction * _arm;
        var chest = player + new Vector3(0, 0.5f, 0);
        if (Tuning.BoomLimits == "drawn" && visible is not null)
            return visible.RaycastSolid(eye, chest, scrollingSolid: true) is null;
        // Under the furniture rule, the sight line passes what the boom may: a bare wall below the lens.
        var passes = Tuning.BoomLimits == "furniture" ? Furniture(world, visible, Direction) : null;
        return SpherePhysics.SweepEitherSide(world, eye, chest, 0.01f, skipSeeThrough: true, passes) is null
            && visible?.RaycastSolid(eye, chest, scrollingSolid: true) is null;
    }

    /// <summary>
    /// One 60 Hz tick following the player's sphere at <paramref name="player"/>, who faces <paramref name="heading"/>, in a
    /// chunk that's an interior or not, with the player turning it by <paramref name="turn"/> (the orbit's stick, a frame's,
    /// or nothing). <paramref name="walking"/>: whether the stick moves him this tick; only then is his movement travel, so a
    /// teleport's set-down, a push out of a wall or a wanderer's shove doesn't turn the view.
    /// </summary>
    public void Step(FollowCamera camera, CollisionWorld world, VisibleWorld? visible, Vector3 player, Vector2 heading, bool interior,
        Vector2 turn, bool walking = true)
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

        _headY = player.Y + t.Height;
        // His travel, eased over a few ticks, unless he jumped.
        var moved = new Vector2(player.X - _lastPlayer.X, player.Z - _lastPlayer.Z);
        _lastPlayer = player;
        if (moved.Length() > 1 || !walking)
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
        // How far into close quarters, eased: the room about him changes in steps as walls come and go.
        _shortened += (Shortened - _shortened) * (1 - MathF.Exp(-Tick / MathF.Max(t.CloseEase, 1e-3f)));
        var shortened = _shortened;
        StepPivot(world, visible, player, shortened);
        Room = JudgeRoom(world, visible);
        StepYaw(world, visible, player, heading, auto);
        // Close quarters part him from the lens by height, not by pitching down: the pitch is capped so a point
        // AheadSight ahead of him at head height stays inside the lens's top, and the lift does the rest.
        var sightCap = SightCap(player, heading);
        var capped = sightCap < _basePitch + t.ClosePitch * shortened + _slope;
        var pitchGoal = MathF.Min(_basePitch + t.ClosePitch * shortened + _slope, sightCap) + _userPitch;
        // A ceiling over the raised boom, nearer than the walls: it comes down to the place's pitch instead.
        if (_free < _freeLevel - 0.1f && _pitch > _basePitch + _userPitch)
            pitchGoal = _basePitch + _userPitch;
        pitchGoal = Math.Clamp(pitchGoal, t.LowestPitch, t.HighestPitch);
        Channel(ref _pitch, ref _pitchSpeed, pitchGoal, t.PitchSpeed, t.PitchAccel, 3);
        _pitch = Math.Clamp(_pitch, t.LowestPitch, t.HighestPitch);
        var cutsBefore = Cuts;
        Fit(world, visible, at: false);
        Write(camera);
        Last = new TrekCameraTick(_pivot, _yaw / Degree, _pitch, _arm, Cuts != cutsBefore, capped, shortened, _whisker, _leanNow, _lastE / Degree,
            _swingingBehind);
    }

    /// <summary>What it did last tick, for the rig to tell its movements apart.</summary>
    public TrekCameraTick Last { get; private set; }

    private float _leanNow, _shortened;

    /// <summary>The least room the arm's look ahead found last tick.</summary>
    public float EnvelopeLast { get; private set; }

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

    /// <summary>
    /// How far into close quarters it is, 0 to 1: the walls' room about him at the place's pitch against the shorter of the
    /// boom and <see cref="TrekCameraTuning.CloseFloor"/>, so a frame at the close floor against a wall isn't lifted.
    /// A camera held further out than the boom (an authored point, a view taken up from elsewhere) has room where it is, and
    /// isn't in close quarters until the arm has come in.
    /// </summary>
    private float Shortened => _arm > _length + 0.1f ? 0 : Math.Clamp(1 - _freeLevel / MathF.Max(MathF.Min(_length, Tuning.CloseFloor), 1e-3f), 0, 1);

    /// <summary>The pivot closes on its place above him, lifted as the arm shortens, but kept under the drawn ceiling.</summary>
    private void StepPivot(CollisionWorld world, VisibleWorld? visible, Vector3 player, float shortened)
    {
        var t = Tuning;
        var goal = PivotGoal(world, visible, player, shortened);
        // Critically damped, its speed and its change capped: a body jittering against a wall, or the lift coming on, no
        // longer moves the camera in steps. A ceiling coming down over it is kept to at once.
        var rate = SettleRate(t.PivotTime);
        var a = rate * rate * (goal - _pivot) - 2 * rate * _pivotSpeed;
        if (a.Length() > t.PivotAccel)
            a *= t.PivotAccel / a.Length();
        _pivotSpeed += a * Tick;
        if (_pivotSpeed.Length() > t.PivotSpeed)
            _pivotSpeed *= t.PivotSpeed / _pivotSpeed.Length();
        _pivot += _pivotSpeed * Tick;
        if (_pivot.Y > goal.Y && goal.Y < player.Y + t.Height + t.CloseLift * shortened - 1e-3f)
        {
            _pivot.Y = MathF.Min(_pivot.Y, goal.Y + 0.05f);
            _pivotSpeed.Y = MathF.Min(_pivotSpeed.Y, 0);
        }
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
    /// <returns>The arm from above his head, before the lift: the room the place itself has.</returns>
    private float PlaceFitted(CollisionWorld world, VisibleWorld? visible, Vector3 player, Vector2 heading)
    {
        PlaceBehind(player, heading);
        Fit(world, visible, at: true);
        var unlifted = _arm;
        _shortened = Shortened;
        _pivot = PivotGoal(world, visible, player, _shortened);
        _pitch = MathF.Min(_pitch, SightCap(player, heading));
        Fit(world, visible, at: true);
        return unlifted;
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
        // Standing, the goal stays where his travel last put it, so the view comes to rest behind him rather than stopping
        // where it was; it holds only where it hasn't been given one since the view was last set (an arrival, his turn).
        var goal = auto && _restGoal is { } rest ? rest : _yaw;
        if (auto && _travel.Length() > t.MovingSpeed)
        {
            float lean = 0;
            foreach (var l in Leans)
                lean += Math.Clamp(l.Lean(player, _travel, Yaw), -l.Bound, l.Bound);
            lean = Math.Clamp(lean, -t.LeanBound, t.LeanBound);
            _leanNow = lean;
            goal = MathF.Atan2(-_travel.Y, -_travel.X) + lean * Degree;
            _restGoal = goal;
        }
        // Behind him only where behind has room: whiskers about the goal push it, smoothly, toward the nearer open side.
        var aimed = auto && (_travel.Length() > t.MovingSpeed || _restGoal is not null);
        var ideal = goal;
        // Walking, where the boom behind his travel is cut and a yaw within the wedge either side keeps it: there instead.
        var striding = Math.Clamp((_travel.Length() - t.MovingSpeed) / MathF.Max(t.MovingSpeed, 1e-3f), 0, 1);
        var wedge = StepWedge(world, visible, ideal, striding, auto && !_swingingBehind && Sneak.Weight <= 0);
        goal += (StepWhiskers(world, visible, ideal, aimed) + wedge) * Degree;
        WedgeOff = MathF.Abs(Wrap(_yaw - ideal)) / Degree;
        // Sneaking past a hunter: toward his far side from it, as quickly as its weight allows.
        var sneak = auto ? Sneak.Weight : 0;
        if (sneak > 0)
            goal += sneak * Wrap(Sneak.Yaw(player) - goal);
        var e = Wrap(goal - _yaw);
        YawOff = MathF.Abs(e) / Degree;
        _lastE = e;
        if (!auto)
        {
            // The player turned it: the view is his.
            _unchosen = _swingingBehind = false;
            _restGoal = null;
        }
        else if (_unchosen && _travel.Length() > t.MovingSpeed)
        {
            _swingingBehind = MathF.Abs(e) > t.UnchosenSwing * Degree;
            _unchosen = false;
            if (_swingingBehind)
                _swingSide = OpenWayRound(world, visible, e);
        }
        if (_swingingBehind && MathF.Abs(e) < 10 * Degree)
        {
            _swingingBehind = false;
            _swingSide = 0;
        }
        if (_swingingBehind && _swingSide != 0)
        {
            // An unchosen view's swing keeps the way round it chose, unless his travel has turned so far that way would
            // take it more than 200° round: then the short way, so the view never wheels on past a half turn.
            var kept = _swingSide > 0 && e < 0 ? e + 2 * MathF.PI : _swingSide < 0 && e > 0 ? e - 2 * MathF.PI : e;
            if (MathF.Abs(kept) < 200 * Degree)
                e = kept;
            else
                _swingSide = 0;
        }
        // Walking at the lens, the way round is chosen once, by the way it's turning or else the side with more room, and kept.
        else if (MathF.Abs(e) > 150 * Degree)
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
        // trailing him round a bend stays calm and only walking at the lens swings; an unchosen view's swing, whatever the room.
        var tight = (1 - Room) * Math.Clamp((MathF.Abs(e) / Degree - 20) / 70, 0, 1);
        if (_swingingBehind)
            tight = MathF.Max(tight, Math.Clamp((MathF.Abs(e) / Degree - 10) / 30, 0, 1));
        // One smoothed approach, critically damped, its rate and its change capped: the follow's slow settle, and the swing's
        // quicker one, blended by how tight it is.
        // Walking it follows at FollowTime; as he stops it eases over to the slower SettleTime, a long tail onto its goal.
        var walking = Math.Clamp((_travel.Length() - t.MovingSpeed) / MathF.Max(t.MovingSpeed, 1e-3f), 0, 1);
        var follow = Lerp(SettleRate(t.SettleTime), SettleRate(t.FollowTime), walking);
        var rate = Lerp(follow, SettleRate(t.SwingTime), tight);
        var most = (t.OpenTurn + (t.TightTurn - t.OpenTurn) * tight) * Degree;
        var accel = (t.OpenTurnAccel + (t.TightTurnAccel - t.OpenTurnAccel) * tight) * Degree;
        rate = MathF.Max(rate, sneak * SettleRate(t.SneakTime));
        most = MathF.Max(most, sneak * t.SneakTurn * Degree);
        accel = MathF.Max(accel, sneak * t.SneakTurnAccel * Degree);
        // Turn first, shorten second: where the boom is about to lose much of its length and turning toward the goal keeps
        // it, the follow quickens to get there first.
        StepUrgency(world, visible, e, walking, auto);
        rate = Lerp(rate, MathF.Max(rate, SettleRate(t.UrgentTime)), Urgency);
        most = Lerp(most, MathF.Max(most, t.UrgentTurn * Degree), Urgency);
        accel = Lerp(accel, MathF.Max(accel, t.UrgentTurnAccel * Degree), Urgency);
        var yaw = _yaw;
        Approach(ref yaw, ref _yawSpeed, yaw + e, rate, most, accel);
        _yaw = Wrap(yaw);
    }

    /// <summary>
    /// How far round from <paramref name="goal"/> the room asks the goal to go, in degrees, eased in time
    /// (<see cref="TrekCameraTuning.WhiskerEase"/>). Every other tick a fan of whiskers, the arm's free length at the place's
    /// pitch from above his head, samples each side of the goal out to <see cref="TrekCameraTuning.WhiskerSpread"/>; each
    /// whisker's room is smoothstepped from the close floor (none) to the place's length (all). The goal moves by as much as
    /// straight behind lacks room, toward the side whose open whiskers weigh more, nearer ones counting for more, to the mean
    /// angle of that side's open whiskers. With room behind it's nothing, and as room returns the push fades. Only as he stops
    /// and stands, his goal where his travel left it; walking, it fades out, and an arrival's view holds.
    /// </summary>
    private float StepWhiskers(CollisionWorld world, VisibleWorld? visible, float goal, bool aimed)
    {
        var t = Tuning;
        if (!aimed || t.WhiskerSpread <= 0)
        {
            _whiskerRaw = 0;
        }
        else if (Ticks % 2 == 0 || !_whiskered)
        {
            _whiskered = true;
            var want = _length * (1 - t.ArrivalShortfall);
            var floor = MathF.Min(t.CloseFloor, want - 0.1f);
            float Open(float off)
            {
                var yaw = goal + off * Degree;
                var (sinPitch, cosPitch) = MathF.SinCos(_basePitch * Degree);
                var way = new Vector3(MathF.Cos(yaw) * cosPitch, sinPitch, MathF.Sin(yaw) * cosPitch);
                var free = Free(world, visible, way, _length);
                var x = Math.Clamp((free - floor) / MathF.Max(want - floor, 1e-3f), 0, 1);
                return x * x * (3 - 2 * x);
            }
            var behind = Open(0);
            if (behind > 0.999f)
            {
                _whiskerRaw = 0;
            }
            else
            {
                float weightPlus = 0, anglePlus = 0, weightMinus = 0, angleMinus = 0;
                var count = Math.Max(1, (int)MathF.Round(t.WhiskerSpread / t.ArrivalStep));
                for (var k = 1; k <= count; k++)
                {
                    var off = k * t.WhiskerSpread / count;
                    var near = MathF.Exp(-off / 60);
                    var plus = Open(off) * near;
                    var minus = Open(-off) * near;
                    (weightPlus, anglePlus) = (weightPlus + plus, anglePlus + plus * off);
                    (weightMinus, angleMinus) = (weightMinus + minus, angleMinus + minus * off);
                }
                // The side: by the open whiskers' weight, softly, with a lean to the side the camera's already on.
                var already = MathF.Tanh(Wrap(_yaw - goal) / (15 * Degree));
                var lean = (weightPlus - weightMinus) / MathF.Max(weightPlus + weightMinus, 1e-3f) + 0.3f * already;
                var side = Math.Clamp(0.5f + lean * 2, 0, 1);
                var toPlus = weightPlus > 1e-3f ? anglePlus / weightPlus : t.WhiskerSpread;
                var toMinus = weightMinus > 1e-3f ? angleMinus / weightMinus : t.WhiskerSpread;
                // Only as he stands: walking, the arm takes the tightness, and a goal pushed off his travel would steer him (the
                // stick being the camera's) off a forest path between trees behind him. It fades in as he slows.
                var walking = Math.Clamp((_travel.Length() - t.MovingSpeed) / MathF.Max(t.MovingSpeed, 1e-3f), 0, 1);
                _whiskerRaw = (1 - walking) * (1 - behind) * (side * toPlus - (1 - side) * toMinus);
            }
        }
        _whisker += (_whiskerRaw - _whisker) * (1 - MathF.Exp(-Tick / MathF.Max(t.WhiskerEase, 1e-3f)));
        return _whisker;
    }

    /// <summary>
    /// The walking wedge (the user's ask of Nov 14, 2026, in Port Saiid's E01 cave): behind his travel is the ideal, but a yaw
    /// within <see cref="TrekCameraTuning.WedgeWidth"/> either side of it will do. Where the boom at the ideal, from the pivot
    /// now and from where it will be over <see cref="TrekCameraTuning.ArmLookahead"/>, loses more than
    /// <see cref="TrekCameraTuning.WedgeLoss"/> of the length (whole by 0.2 more), and some yaw inside the wedge keeps the place's
    /// length (but for <see cref="TrekCameraTuning.ArrivalShortfall"/>) from both, the goal goes to the open yaw nearest behind,
    /// its edge found finely, <see cref="TrekCameraTuning.WedgeMargin"/> inside it. Between openings on both sides it keeps the
    /// side it has (at first, the side the camera is on), and changes only when that side closes. Eased in over
    /// <see cref="TrekCameraTuning.WedgeEase"/>, home over <see cref="TrekCameraTuning.WedgeRelease"/>; scaled by how much he's
    /// walking, so as he stops it hands over to the whiskers. Nothing whole in the wedge, or behind not cut: nothing.
    /// </summary>
    /// <returns>How far round from <paramref name="ideal"/> the goal goes, in degrees, eased.</returns>
    private float StepWedge(CollisionWorld world, VisibleWorld? visible, float ideal, float walking, bool active)
    {
        var t = Tuning;
        if (!active || t.WedgeWidth <= 0 || walking <= 0)
        {
            _wedgeRaw = 0;
            _wedgeHeld = false;
            (WedgeCut, WedgeOpen) = (0, false);
        }
        else if (Ticks % 2 == 0 || !_wedged)
        {
            _wedged = true;
            var want = _length * (1 - t.ArrivalShortfall);
            // Where the pivot will be, as the arm's look ahead takes it: as far as he really travels, short of a wall.
            var ahead = _pivotSpeed * t.ArmLookahead * Math.Clamp(_travel.Length() / 2, 0, 1);
            if (ahead.LengthSquared() > 1e-6f && SpherePhysics.SweepEitherSide(world, _pivot, _pivot + ahead, 0.01f, skipSeeThrough: true) is { } stop)
                ahead *= MathF.Max(0, stop - 0.6f / ahead.Length());
            var (sinPitch, cosPitch) = MathF.SinCos(_basePitch * Degree);
            var pivot = _pivot;
            float Room(float off)
            {
                var yaw = ideal + off * Degree;
                var way = new Vector3(MathF.Cos(yaw) * cosPitch, sinPitch, MathF.Sin(yaw) * cosPitch);
                var now = Free(world, visible, way, _length);
                if (ahead.LengthSquared() < 1e-4f)
                    return now;
                _pivot = pivot + ahead;
                var later = Free(world, visible, way, _length);
                _pivot = pivot;
                return MathF.Min(now, later);
            }
            var lost = 1 - Room(0) / MathF.Max(_length, 1e-3f);
            var cut = Smooth((lost - t.WedgeLoss) / 0.2f);
            // It takes hold only where the boom itself is about to be cut, by the arm's look ahead along its own way (behind
            // his travel being short of room while the boom where it is has it, on a forest path, is nothing to act on), and
            // then holds for as long as behind him is cut, wherever the boom has got to.
            var reach = MathF.Max(MathF.Min(_arm, _length), 0.5f);
            var threatened = Smooth((Math.Clamp((reach - EnvelopeLast) / reach, 0, 1) - t.WedgeLoss) / 0.2f);
            if (cut <= 0)
                _wedgeHeld = false;
            else if (cut * threatened > 0.3f)
                _wedgeHeld = true;
            if (!_wedgeHeld)
                cut = 0;
            WedgeCut = cut;
            float? Opening(int side)
            {
                var count = Math.Max(1, (int)MathF.Ceiling(t.WedgeWidth / t.WedgeStep));
                var shut = 0f;
                for (var k = 1; k <= count; k++)
                {
                    var off = MathF.Min(k * t.WedgeStep, t.WedgeWidth);
                    if (Room(side * off) < want)
                    {
                        shut = off;
                        continue;
                    }
                    // The edge, found finely between the last shut yaw and this open one, so the goal moves continuously.
                    var open = off;
                    for (var i = 0; i < 3; i++)
                    {
                        var mid = (shut + open) / 2;
                        if (Room(side * mid) >= want)
                            open = mid;
                        else
                            shut = mid;
                    }
                    var inside = MathF.Min(open + t.WedgeMargin, t.WedgeWidth);
                    return inside > off && Room(side * inside) < want ? off : inside;
                }
                return null;
            }
            float? plus = null, minus = null;
            if (cut > 0)
            {
                plus = Opening(1);
                minus = Opening(-1);
            }
            WedgeOpen = plus is not null || minus is not null;
            if (plus is null && minus is null)
            {
                _wedgeRaw = 0;
            }
            else
            {
                // Sticky: the side it has, while that side stays open; at first, the side the camera is on.
                var side = _wedgeSide != 0 ? _wedgeSide : Wrap(_yaw - ideal) >= 0 ? 1 : -1;
                if ((side > 0 ? plus : minus) is null)
                    side = -side;
                _wedgeSide = side;
                _wedgeRaw = cut * side * (side > 0 ? plus : minus)!.Value;
            }
        }
        var target = _wedgeRaw * walking;
        var ease = MathF.Abs(target) > MathF.Abs(_wedge) && MathF.Sign(target) == MathF.Sign(_wedge) || _wedge == 0 && target != 0
            ? t.WedgeEase : t.WedgeRelease;
        _wedge += (target - _wedge) * (1 - MathF.Exp(-Tick / MathF.Max(ease, 1e-3f)));
        if (_wedgeRaw == 0 && MathF.Abs(_wedge) < 1)
            _wedgeSide = 0;
        return _wedge;
    }

    private bool _wedged, _wedgeHeld;

    private float _whisker, _whiskerRaw, _lastE;
    private float _wedge, _wedgeRaw;
    private int _wedgeSide;
    private bool _whiskered;

    /// <summary>How far round from behind him the room has pushed the yaw's goal, in degrees.</summary>
    public float WhiskerPush => _whisker;

    /// <summary>
    /// How far round from behind his travel the walking wedge has put the yaw's goal, in degrees, eased
    /// (<see cref="StepWedge"/>).
    /// </summary>
    public float WedgePush => _wedge;

    /// <summary>Whether the walking wedge holds the goal off behind him this tick (its push more than a degree).</summary>
    public bool WedgeEngaged => MathF.Abs(_wedge) > 1;

    /// <summary>The side the wedge's goal is on, +1 or −1, or 0 while it isn't engaged.</summary>
    public int WedgeSide => WedgeEngaged ? _wedgeSide : 0;

    /// <summary>How much of a cut the wedge last found behind him, 0 to 1 (its trigger), for the rig.</summary>
    public float WedgeCut { get; private set; }

    /// <summary>Whether, at that cut, some yaw within the wedge kept the length, for the rig.</summary>
    public bool WedgeOpen { get; private set; }

    /// <summary>How far the yaw is off behind his travel (with the leans), in degrees, last tick.</summary>
    public float WedgeOff { get; private set; }

    /// <summary>
    /// How urgently the follow should turn, eased: how much of the boom the look ahead says is about to go (from 15% lost,
    /// none, to 50%, all) times how much of the length the yaws toward the goal keep (the room at the goal and at up to
    /// <see cref="TrekCameraTuning.UrgentProbe"/> round toward it, smoothstepped from half the length to 85% of it), while he
    /// walks. With walls all round, turning wouldn't help and it stays nothing.
    /// </summary>
    private void StepUrgency(CollisionWorld world, VisibleWorld? visible, float e, float walking, bool auto)
    {
        var t = Tuning;
        var raw = 0f;
        // Not in an unchosen view's swing, which keeps its calm 90°/s: this is keeping up with his own turns.
        if (auto && walking > 0 && !_swingingBehind && MathF.Abs(e) > 5 * Degree && t.UrgentTurn > 0)
        {
            var reach = MathF.Max(MathF.Min(_arm, _length), 0.5f);
            var lost = Math.Clamp((reach - EnvelopeLast) / reach, 0, 1);
            var need = Smooth((lost - 0.15f) / 0.35f);
            if (need > 0)
            {
                var yaw = _yaw;
                float RoomAt(float at)
                {
                    _yaw = at;
                    return Free(world, visible, DirectionAt(_pitch), _length);
                }
                var toward = RoomAt(yaw + MathF.Sign(e) * MathF.Min(MathF.Abs(e), t.UrgentProbe * Degree));
                var atGoal = RoomAt(yaw + e);
                _yaw = yaw;
                var kept = Smooth((MathF.Min(toward, atGoal) / _length - 0.5f) / 0.35f);
                raw = need * kept * walking;
            }
        }
        var ease = raw > Urgency ? t.UrgentRise : t.UrgentFall;
        Urgency += (raw - Urgency) * (1 - MathF.Exp(-Tick / MathF.Max(ease, 1e-3f)));
    }

    private static float Smooth(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    /// <summary>The rate of a critically damped approach that's 90% of the way, from rest, in <paramref name="seconds"/>.</summary>
    private static float SettleRate(float seconds) => 3.89f / MathF.Max(seconds, 0.05f);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// A tick of a critically damped approach of <paramref name="x"/> to <paramref name="goal"/> at <paramref name="rate"/> (its
    /// natural frequency, a second's): its change in speed no more than <paramref name="accel"/> a second, its speed no more than
    /// <paramref name="most"/>. The speed never jumps, and the end is a long tail rather than a stop.
    /// </summary>
    private static void Approach(ref float x, ref float v, float goal, float rate, float most, float accel)
    {
        var a = rate * rate * (goal - x) - 2 * rate * v;
        v += Math.Clamp(a, -accel, accel) * Tick;
        v = Math.Clamp(v, -most, most);
        x += v * Tick;
    }

    /// <summary>
    /// The way round for an unchosen view's swing behind him, <paramref name="e"/> off: the shorter, unless the arm would
    /// close up somewhere along it (a door's wall behind him) and the other way has more room all along.
    /// </summary>
    private int OpenWayRound(CollisionWorld world, VisibleWorld? visible, float e)
    {
        float Least(int side)
        {
            var span = side > 0 ? (e >= 0 ? e : e + 2 * MathF.PI) : (e <= 0 ? -e : 2 * MathF.PI - e);
            var least = float.MaxValue;
            var yaw = _yaw;
            for (var k = 1; k <= 6; k++)
            {
                _yaw = yaw + side * span * k / 6;
                least = MathF.Min(least, Free(world, visible, DirectionAt(_basePitch), _length));
            }
            _yaw = yaw;
            return least;
        }
        var shorter = e >= 0 ? 1 : -1;
        var (near, far) = (Least(shorter), Least(-shorter));
        return near < Tuning.ArrivalShortest * _length && far > near + 0.5f ? -shorter : shorter;
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
        FitLimit = $"{DrawnLimit} free {free:F2}";
        _free = MathF.Min(free, _length);
        _freeLevel = Free(world, visible, DirectionAt(_basePitch + _userPitch), _length);
        if (at || free < MathF.Min(_arm, reach - 1e-3f))
        {
            // A wall nearer than the camera, which the look ahead didn't see coming: in at once, the backstop.
            if (!at && free < _arm - 0.1f)
                Cuts++;
            _arm = at ? _free : free;
            _armSpeed = 0;
        }
        else
        {
            // The arm rides the least room it's about to need, over where the yaw and the pivot are going in the next
            // ArmLookahead, so it has shortened before a wall arrives; out as room opens, eased, never jumping.
            EnvelopeLast = Envelope(world, visible);
            var goal = MathF.Min(_free, EnvelopeLast);
            var t = Tuning;
            var inward = goal < _arm;
            Approach(ref _arm, ref _armSpeed, goal, SettleRate(inward ? t.ArmInTime : t.ArmEase), inward ? t.ArmIn : t.ArmOut,
                inward ? t.ArmInAccel : t.ArmAccel);
            _arm = MathF.Min(_arm, MathF.Max(free, 0));
        }
    }

    /// <summary>
    /// The least room over the next <see cref="TrekCameraTuning.ArmLookahead"/>: the yaw where its speed and its turn toward
    /// the goal take it, and the pivot where his travel takes it, at a few points along the way.
    /// </summary>
    private float Envelope(CollisionWorld world, VisibleWorld? visible)
    {
        var t = Tuning;
        if (t.ArmLookahead <= 0)
            return _length;
        var (yaw, pivot, pitch) = (_yaw, _pivot, _pitch);
        // How far round it goes: its speed over the span, or toward the goal at the swing's rate if that's more, not past it.
        var turn = _yawSpeed * t.ArmTurnLookahead;
        // Toward the goal at the swing's rate in a swing, and at the quickened follow's while it's urgent: the arm draws in
        // for the yaw it will have, not for a wall the turn is about to clear.
        var planned = MathF.Max(_swingingBehind ? t.TightTurn : 0, Urgency * t.UrgentTurn) * Degree * t.ArmTurnLookahead;
        var toward = MathF.Sign(_lastE) * MathF.Min(MathF.Abs(_lastE), MathF.Max(MathF.Abs(turn), planned));
        if ((Tuning.ArmPlansSwing || Urgency > 0.05f) && MathF.Abs(toward) > MathF.Abs(turn) && (turn == 0 || MathF.Sign(toward) == MathF.Sign(turn)))
            turn = toward;
        var least = float.MaxValue;
        const int Points = 8;
        // The pivot goes where it's going, at its own speed (which follows his travel, glides after an arrival, and lifts in
        // close quarters), but no further than a wall in its way.
        // Pressed into a wall he isn't going anywhere, whatever the pivot's jitter: only as far as he really travels.
        var ahead = _pivotSpeed * t.ArmLookahead * Math.Clamp(_travel.Length() / 2, 0, 1);
        if (ahead.LengthSquared() > 1e-6f && SpherePhysics.SweepEitherSide(world, pivot, pivot + ahead, 0.01f, skipSeeThrough: true) is { } stop)
            ahead *= MathF.Max(0, stop - 0.6f / ahead.Length());
        for (var k = 1; k <= Points; k++)
        {
            var share = (float)k / Points;
            _yaw = yaw + turn * share;
            _pivot = pivot + ahead * share;
            _pitch = Math.Clamp(pitch + _pitchSpeed * t.ArmLookahead * share, t.LowestPitch, t.HighestPitch);
            least = MathF.Min(least, Free(world, visible, Direction, _length));
        }
        (_yaw, _pivot, _pitch) = (yaw, pivot, pitch);
        return least;
    }

    /// <summary>How far the camera's sphere can go from the pivot along <paramref name="way"/>, up to <paramref name="length"/>.</summary>
    private float Free(CollisionWorld world, VisibleWorld? visible, Vector3 way, float length)
    {
        var t = Tuning;
        if (t.BoomLimits == "drawn" && visible is not null)
            return FreeDrawn(world, visible, way, length);
        var free = length;
        // The furniture rule: bare walls the lens looks over, with ground beyond, are passed.
        var passes = t.BoomLimits == "furniture" ? Furniture(world, visible, way) : null;
        if (SpherePhysics.SweepEitherSide(world, _pivot, _pivot + way * length, t.Radius, skipSeeThrough: true, passes) is { } share)
            free = share * length - t.Margin;
        // The sphere's sweep meets faces only, so a corner or an edge can slip between: a thin ray along the middle too.
        if (SpherePhysics.SweepEitherSide(world, _pivot, _pivot + way * length, 0.01f, skipSeeThrough: true, passes) is { } line)
            free = MathF.Min(free, line * length - t.Radius - t.Margin);
        // The drawn surface is kept a sphere's radius off, measured square to it: along a ray that meets it at a slant, further.
        // An opaque scrolling one (lava, a glow) stops it too.
        var reach = length + 4 * t.Radius;
        if (visible?.RaycastSolid(_pivot, _pivot + way * reach, scrollingSolid: true) is { } drawn)
        {
            var slant = MathF.Max(MathF.Abs(Vector3.Dot(way, drawn.Normal)), 0.25f);
            free = MathF.Min(free, drawn.Share * reach - (t.Radius + t.Margin) / slant);
        }
        return MathF.Max(free, 0);
    }

    /// <summary>
    /// How far the camera can go along <paramref name="way"/> under the drawn limits (<see cref="TrekCameraTuning.BoomLimits"/>
    /// "drawn"): the drawn world's solid surfaces, scrolling ones (lava, a glow) solid too and translucent and cut-out ones not,
    /// met by five rays (the middle one kept the sphere's radius off square to the surface, four more fanned out from the
    /// pivot to a sphere's radius round the far end, kept the margin off) so the lens has a body; collision only for bounds, the camera kept over floor (a probe
    /// down from points along the boom finds ground within 6 under each), and the see-through flag aside.
    /// </summary>
    /// <summary>
    /// The furniture rule (<see cref="TrekCameraTuning.BoomLimits"/> "furniture"), for a boom along <paramref name="way"/>: which
    /// collision triangles it may pass. A wall (under 0.45 up or down) with nothing solid drawn at it (<see cref="Bare"/>),
    /// whose top is below the lens by <see cref="TrekCameraTuning.FurnitureClear"/>: the lens's height taken at the place's own
    /// pitch and length from above his head, so the decision doesn't change as the pitch settles or the arm moves; and only
    /// where the camera's place beyond is over ground (probes down from the full length and two-thirds of it find ground within
    /// 6), so a low bare barrier at a ledge or a balcony still stops it. Null, passing nothing, where there's no ground beyond.
    /// </summary>
    private Func<CollisionWorld.Triangle, bool>? Furniture(CollisionWorld world, VisibleWorld? visible, Vector3 way)
    {
        var t = Tuning;
        var flat = new Vector3(way.X, 0, way.Z);
        if (flat.LengthSquared() < 1e-6f)
            return null;
        flat = Vector3.Normalize(flat);
        var (sinPitch, cosPitch) = MathF.SinCos(_basePitch * Degree);
        var head = new Vector3(_pivot.X, _headY, _pivot.Z);
        var place = head + (flat * cosPitch + new Vector3(0, sinPitch, 0)) * _length;
        foreach (var share in new[] { 1f, 2f / 3 })
        {
            var at = head + (place - head) * share;
            if (!SpherePhysics.SweepNearest(world, at, at - new Vector3(0, 6, 0), 0.05f, skipSeeThrough: false, minUp: 0.5f, out _, out _))
                return null;
        }
        var lens = place.Y - t.FurnitureClear;
        return tri => MathF.Abs(tri.Normal.Y) < 0.45f && MathF.Max(tri.A.Y, MathF.Max(tri.B.Y, tri.C.Y)) < lens && Bare(visible, tri);
    }

    private readonly Dictionary<(Vector3, Vector3, Vector3), bool> _bare = [];

    /// <summary>Whether nothing solid is drawn at a collision triangle's top: no drawn surface within 0.35 across it at its middle or most of the way to its top corners.</summary>
    private bool Bare(VisibleWorld? visible, CollisionWorld.Triangle tri)
    {
        if (visible is null)
            return false;
        var key = (tri.A, tri.B, tri.C);
        if (_bare.TryGetValue(key, out var known))
            return known;
        const float Near = 0.35f;
        var centre = (tri.A + tri.B + tri.C) / 3;
        var bare = true;
        // Sampled toward its top: a table's box rises a good way over the drawn table and its benches, while a room's wall is
        // drawn up to its top.
        var top = MathF.Max(tri.A.Y, MathF.Max(tri.B.Y, tri.C.Y));
        var samples = new[] { tri.A, tri.B, tri.C }.Where(v => v.Y > top - 0.3f).Select(v => centre + (v - centre) * 0.8f).Append(centre);
        foreach (var s in samples)
            if (visible.RaycastSolid(s + tri.Normal * Near, s - tri.Normal * Near, scrollingSolid: true) is not null
                || visible.RaycastSolid(s - tri.Normal * Near, s + tri.Normal * Near, scrollingSolid: true) is not null)
            {
                bare = false;
                break;
            }
        if (_bare.Count > 200_000)
            _bare.Clear();
        return _bare[key] = bare;
    }

    /// <summary>How many triangles the furniture rule has found bare, for the rig.</summary>
    public int BareWalls => _bare.Count(x => x.Value);

    private float _headY;

    /// <summary>What limited the boom under the drawn limits at its last test, for the rig.</summary>
    public string DrawnLimit { get; private set; } = "";

    /// <summary>The same for the arm's own test last tick, with the room it found.</summary>
    public string FitLimit { get; private set; } = "";

    private float FreeDrawn(CollisionWorld world, VisibleWorld visible, Vector3 way, float length)
    {
        var t = Tuning;
        var free = length;
        var reach = length + 4 * t.Radius;
        if (visible.RaycastSolid(_pivot, _pivot + way * reach, scrollingSolid: true) is { } drawn)
        {
            var slant = MathF.Max(MathF.Abs(Vector3.Dot(way, drawn.Normal)), 0.25f);
            free = MathF.Min(free, drawn.Share * reach - (t.Radius + t.Margin) / slant);
            DrawnLimit = "middle";
        }
        else
            DrawnLimit = "none";
        var side = Vector3.Cross(way, Vector3.UnitY);
        side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : Vector3.UnitX;
        var up = Vector3.Cross(side, way);
        // Four more fanned out from the pivot to a sphere's radius round the far end: from the pivot, so a ray never starts on
        // the far side of a low ceiling or a wall beside his head.
        foreach (var offset in new[] { side, -side, up, -up })
        {
            var to = _pivot + way * reach + offset * t.Radius;
            if (visible.RaycastSolid(_pivot, to, scrollingSolid: true) is { } hit && hit.Share * reach - t.Margin < free)
                (free, DrawnLimit) = (hit.Share * reach - t.Margin, "fan");
        }
        free = MathF.Max(free, 0);
        // Over floor: the boom stops at the first point along it with no ground under it.
        const int Points = 4;
        for (var k = 1; k <= Points; k++)
        {
            var d = free * k / Points;
            var at = _pivot + way * d;
            if (!SpherePhysics.SweepNearest(world, at, at - new Vector3(0, 6, 0), 0.05f, skipSeeThrough: false, minUp: 0.5f, out _, out _))
            {
                free = free * (k - 1) / Points;
                DrawnLimit = "floor";
                break;
            }
        }
        return free;
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
        _pivotSpeed = Vector3.Zero;
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
        _pivotSpeed = Vector3.Zero;
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
        (_wedge, _wedgeRaw, _wedgeSide) = (0, 0, 0);
        // Close quarters are judged again at its next fit.
        _free = _freeLevel = _length;
        // A view taken up from elsewhere is one the player didn't choose.
        _unchosen = true;
        _restGoal = null;
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

/// <summary>How an arrival was framed (<see cref="EnhancedTrekCamera.Arrival"/>).</summary>
public enum TrekArrivalKind
{
    /// <summary>The teleporter's own camera point.</summary>
    Authored,
    /// <summary>Within 30° of behind him.</summary>
    Behind,
    /// <summary>From 30° to 120° round: beside him.</summary>
    Side,
    /// <summary>From 120° to 165° round.</summary>
    FrontQuarter,
    /// <summary>More than 165° round: in front of him, looking back.</summary>
    Front,
    /// <summary>No angle had room: behind him, lifted over his head.</summary>
    Lifted,
}

/// <summary>An arrival's frame: its kind, the angle off behind him in degrees (signed), the arm, whether it reached the length, and whether it's a close frame against a wall.</summary>
public readonly record struct TrekArrival(TrekArrivalKind Kind, float Off, float Arm, bool Reached, bool Close = false)
{
    public static TrekArrivalKind KindOf(float off) => MathF.Abs(off) switch
    {
        < 30 => TrekArrivalKind.Behind,
        < 120 => TrekArrivalKind.Side,
        < 165 => TrekArrivalKind.FrontQuarter,
        _ => TrekArrivalKind.Front,
    };
}

/// <summary>
/// One tick of the enhanced trek camera, for the rig: the pivot, the yaw and pitch in degrees, the arm, whether the backstop cut
/// it, whether the sight cap held the pitch, how far into close quarters (the lift) it was, the whiskers' push and the leans,
/// how far the yaw was off its goal, and whether an unchosen view's swing was under way.
/// </summary>
public readonly record struct TrekCameraTick(Vector3 Pivot, float Yaw, float Pitch, float Arm, bool Cut, bool SightCapped, float Shortened,
    float Whisker, float Lean, float Off, bool Swinging);
