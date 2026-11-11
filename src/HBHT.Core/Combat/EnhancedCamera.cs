using System.Globalization;
using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Magic;
using HBHT.Core.Movement;

namespace HBHT.Core.Combat;

/// <summary>
/// Camera: Enhanced, a battle's camera for turns played the original's way (docs/systems/cameras.md, "The enhanced battle camera"). It runs
/// on top of the original's (<see cref="CombatCamera"/>), which goes on moving its own camera underneath, and shows a shot of its own: in
/// the player's turn low over the character's shoulder at the enemy it heads for, orbiting its pack with them, or with none ahead trailing
/// them on a leash, its aim leading them, all of it following a point drawn smoothly after them; once anyone acts, one shot holding all of
/// it, planned as it acts, an AI's walk or flight shown from near where it ends, turning after the walker; an arrow, a throw or a spell
/// at someone further off, or a spell at an ally, as two shots, the loosing and the hit, each target a spell lands on close; in aiming the reticle in the middle, a spell's targets with some of its reach, an area from above,
/// its ring down to the conversation box, or a blow's the shot that already holds it, panned to rather than cut to; and the original's
/// again once the player turns the camera themselves, until they aim. Every shot is taken from the side of the battle's vantage (<see cref="Vantage"/>), and no one is framed
/// under the conversation box. It moves to each shot with its acceleration capped, not its speed (<see cref="CappedMotion"/>), trailing a
/// moving goal rather than overshooting it, quicker while the character is out of view, and cuts a long way, but never while the player
/// walks the character.
/// </summary>
/// <remarks>
/// The shot it shows is the battle's camera (<see cref="Battle.Camera"/>), so the stick and the aim are read against what the player
/// sees. It only reads the battle and draws no random numbers, so no rule's outcome changes but by the player's own stick.
/// </remarks>
public sealed class EnhancedCamera
{
    private CappedMotion? _motion;
    private ShotFinder? _finder;
    private Physics.CollisionWorld? _world;
    private readonly List<ShotSubject> _subjects = [];
    private readonly List<TargetCandidate> _candidates = [];
    private readonly List<Combatant> _acted = [];
    private readonly List<ScreenSide> _pairs = [];

    /// <summary>The target's pack over the shoulder, each as far as it's held (<see cref="CameraTuning.PackFade"/>).</summary>
    private readonly List<(Combatant Who, float Held)> _pack = [];
    private readonly TargetCone _cone = new();

    /// <summary>The turn it last saw begin, and the state it last saw.</summary>
    private Combatant? _turnOf;

    private CombatState? _stateWas;

    /// <summary>The framing held, of the player's turn or of a spell's aim: its turn from straight over the shoulder, and its pitch.</summary>
    private (float Offset, float Pitch)? _framing;

    /// <summary>What the framing aims at and how far it may stand: over the shoulder, near the character; else the subjects' middle.</summary>
    private (Vector3? Aim, float? Most) _frameBy;

    /// <summary>What a spell's aim's framing was found for: the cursor, how many are selected, and whether its reach is shown.</summary>
    private (Combatant? Cursor, int Selected, bool Reach)? _spellAim;

    private int _sinceDecide;

    /// <summary>What's put on the original's shot to clear its sight lines, and how many looks in a row it has been clear without it.</summary>
    private (float Yaw, float Pitch, float Scale)? _correction;

    private int _clear;

    /// <summary>The aim being framed: its state, who aims, and the yaw the view had as it began.</summary>
    private (CombatState State, Combatant Who, float Yaw)? _aiming;

    /// <summary>An action's shot, once it's under way.</summary>
    private (Combatant Who, Shot Shot)? _plan;

    /// <summary>Where an action's body has stood and been going as its shot was planned, which a plan made again keeps in view.</summary>
    private readonly List<ShotSubject> _shown = [];

    /// <summary>How many of the action's subjects its plan couldn't hold.</summary>
    private int _planMissed;

    /// <summary>
    /// An action at someone further off under way: who looses at whom, whether the camera has cut to the hit, the ticks since a spell's
    /// visual started, and the shot held.
    /// </summary>
    private (Combatant Who, Combatant At, bool Hit, int Loosed, int Shown, Shot Shot)? _volley;

    /// <summary>The goal is cut to this tick, not moved to: the loosing, or the hit.</summary>
    private bool _cutNow;

    /// <summary>
    /// The point the shot follows whoever's turn it is by (<see cref="FollowPoint"/>), and whose: the shot is carried with it as they walk
    /// in the player's turn or an action, so their stride doesn't jolt the view.
    /// </summary>
    private readonly FollowPoint _follow = new();

    private Combatant? _followOf;

    /// <summary>Whether this tick's shot is carried with an action's walker (<see cref="ActionShot"/>), as the player's turn's is with the character.</summary>
    private bool _tracking;

    /// <summary>A pan under way, into an aim, about one or back from one: the state it's for, the shot it left, and its ticks done and in all.</summary>
    private (CombatState State, Shot From, int Done, int Ticks)? _pan;

    /// <summary>The shot an aim at a blow keeps, and the body under the cursor it was found for (<see cref="BlowAim"/>).</summary>
    private (Combatant Cursor, Shot Shot)? _blowAim;

    /// <summary>The target the shot over the shoulder looked at last tick and the yaw over the shoulder at it; how far the view turns with it this tick.</summary>
    private (Combatant Target, float Behind)? _behind;

    /// <summary>How far the shot was turned with the way over the shoulder the last tick, radians: its speed, whose change is capped.</summary>
    private float _carried;

    /// <summary>
    /// A swing under way (<see cref="CameraTuning.SwingTime"/>): over the shoulder onto <see cref="_framingTo"/>, or with no enemy ahead
    /// onto a view cleared of what hides the character; how far it turns, pitches and draws back in all, and its ticks done and in all.
    /// </summary>
    private (Vector3 By, int Done, int Ticks)? _swing;

    /// <summary>How far the swing turned the shot the last tick, radians: its speed, which the orbit or the leash takes on if it's left.</summary>
    private float _swung;

    /// <summary>Why the framing held over the shoulder was found again, for the overlay (<see cref="CameraIntent.Swung"/>).</summary>
    private string? _framedWhy;

    /// <summary>How many ticks in a row the framing held over the shoulder has hidden the character or the target (<see cref="CameraTuning.HiddenPatience"/>).</summary>
    private int _hiddenFor;

    /// <summary>How far the shot was turned by itself over the shoulder or on its leash the last tick, radians, the two together.</summary>
    private float _lastTurn;

    /// <summary>The goal the player's turn's shot glides on (<see cref="ShotGlide"/>), and whether it did last tick.</summary>
    private readonly ShotGlide _glide = new();

    private bool _glided;

    /// <summary>The aim's lead from the character in their turn, and how fast it changes (<see cref="LeadBy"/>).</summary>
    private (Vector3 At, Vector3 Speed)? _leadBy;

    /// <summary>The turn the shot's carried by while gliding, radians a tick, and how fast that changes (<see cref="CameraTuning.TurnSmoothing"/>).</summary>
    private float _turnRate, _turnRateSpeed;

    /// <summary>How many ticks the target over the shoulder has been let go, with none in its place (<see cref="CameraTuning.TrailAfter"/>).</summary>
    private int _letGo;

    /// <summary>How far the shot over the shoulder stands back, which comes in only slowly (<see cref="CameraTuning.ZoomInTime"/>).</summary>
    private float? _shoulderDistance;

    /// <summary>An AI's walk as it's shown (<see cref="WalkShot"/>), from its start.</summary>
    private Walk? _walk;

    /// <summary>The walk's shot last tick and this, which the camera is carried along between.</summary>
    private (Shot From, Shot To)? _walkMoved;

    /// <summary>The subjects of a walk's end and of its start, as it's planned.</summary>
    private readonly List<ShotSubject> _ends = [];
    private readonly List<ShotSubject> _starts = [];

    /// <summary>
    /// An AI's walk: who walks, from where (the point that follows them) to where, the eye the shot ends from and what it ends aimed at,
    /// the share of the walk the eye moves with (<see cref="CameraTuning.WalkPan"/>), how long its rail is (<see cref="_rail"/>), how far
    /// along it the shot is and how fast it goes on, how far along the walker was measured and how fast, its shot last tick, and whether
    /// they've stopped, its end framed again round where they stand.
    /// </summary>
    private readonly record struct Walk(Combatant Who, Vector3 From, Vector3 To, Vector3 EyeEnd, Vector3 AimEnd, float Share, float Length,
        float Along, float AlongSpeed, float Measured, float MeasuredSpeed, Shot? Last, bool Arrived = false);

    /// <summary>
    /// An AI's walk's rail: the way it goes as it set off, a point every <see cref="RailStep"/>, its corners rounded
    /// (<see cref="CameraTuning.WalkRound"/>), which its shot rides as the walker comes along it.
    /// </summary>
    private readonly List<Vector3> _rail = [];

    private readonly List<Vector3> _railRaw = [];
    private readonly List<Vector2> _corners = [];
    private const float RailStep = 0.25f;

    private float _turnBy;

    /// <summary>
    /// Whether the player's turn has opened, and where the character stood and faced as it did, until they move or turn: till then its
    /// target is held, the one ahead or else the nearest enemy, whom the action before planned toward (<see cref="NextYaw"/>).
    /// </summary>
    private bool _opened;

    private (Vector3 At, Vector2 Heading)? _opening;

    /// <summary>
    /// The shot with no enemy ahead: its yaw, pitch and distance, taken from the shot as the target was lost, its yaw trailing the character
    /// on a leash while its aim leads them.
    /// </summary>
    private (float Yaw, float Pitch, float Distance)? _ahead;

    /// <summary>A new framing over the shoulder being swung onto (<see cref="CameraTuning.SwingTime"/>), rather than stepped to.</summary>
    private (float Offset, float Pitch)? _framingTo;

    /// <summary>
    /// The vantage, its side of the way from the party to the enemies (<see cref="Vantage.Place"/>), the way it was placed for, and how
    /// many of each were on the field last tick.
    /// </summary>
    private Vector2? _vantage;

    private int _vantageSide;
    private Vector2 _vantageLine;

    /// <summary>The middle of the line between the party and the enemies, and its length, as the vantage was placed.</summary>
    private (Vector2 Middle, float Length) _vantageFor;
    private (int Party, int Enemies) _counts;

    /// <summary>The yaw the view has, or the original's before it has started.</summary>
    private float _from;

    /// <summary>What it shows: the battle's camera while it's on.</summary>
    public FollowCamera Camera { get; } = new(default, Vector2.UnitY) { Mode = CameraMode.Interior };

    /// <summary>What it tried to do on its last tick, for the runtime's debug overlay.</summary>
    public CameraIntent Intent { get; } = new();

    /// <summary>Whether this tick's shot was found for subjects of its own, rather than kept or taken whole.</summary>
    private bool _subjectsNew;

    public CameraTuning Tuning { get; set; } = CameraTuning.Default;

    /// <summary>The motion its shot moves by, once it has started.</summary>
    public CappedMotion? Motion => _motion;

    /// <summary>What finds its shots, once it has started.</summary>
    public ShotFinder? Finder => _finder;

    /// <summary>The shot it moved toward this tick.</summary>
    public Shot Goal { get; private set; }

    /// <summary>The point it follows whoever's turn it is by (<see cref="CameraTuning.FollowSmoothing"/>), once it has one.</summary>
    public Vector3? FollowAt => _followOf is null ? null : _follow.At;

    /// <summary>That point's velocity, units a second, once it has one.</summary>
    public Vector3? FollowSpeed => _followOf is null ? null : _follow.Speed;

    /// <summary>Whether it's panning, into an aim, about one or back from one (<see cref="CameraTuning.PanTicks"/>).</summary>
    public bool Panning => _pan is not null;

    /// <summary>The player turned the camera in this turn: it shows the original's until they aim or the turn ends.</summary>
    public bool Released { get; private set; }

    /// <summary>Whether its caps were raised this tick past the glide's: the player's hand on it, or an aim following the reticle.</summary>
    public bool Quick { get; private set; }

    /// <summary>Whether the player walks the character this tick: its goal glides (<see cref="CameraTuning.GlideTime"/>), and it never cuts.</summary>
    public bool Walking { get; private set; }

    /// <summary>Whether the player's turn has opened and the character hasn't yet moved or turned: its opening shot is held.</summary>
    public bool Opening => _opened && _opening is not null;

    /// <summary>
    /// Set by Start Early's director while it holds a shot (the outcome hold, <see cref="BattleDirector"/>): what its own shots would cut
    /// or pan to waits, moved to under the caps if the goal it's handed changes.
    /// </summary>
    public bool Holding { get; set; }

    /// <summary>Whom the player heads for in their turn, its representative target.</summary>
    public Combatant? Target => _cone.Target as Combatant;

    /// <summary>The battle's vantage across the ground, whose side every shot is taken from (<see cref="Vantage"/>); null before the battle has both sides.</summary>
    public Vector2? VantagePoint => _vantage;

    /// <summary>The vantage's side of the way from the party to the enemies: +1 its left, −1 its right, 0 before it's placed (<see cref="Vantage.Place"/>).</summary>
    public int VantageSide => _vantageSide;

    /// <summary>What it's showing, in words.</summary>
    public string Kind { get; private set; } = "";

    /// <summary>How often it has cut rather than moved.</summary>
    public int Cuts { get; private set; }

    /// <summary>F3's line.</summary>
    public string Status
    {
        get
        {
            if (_motion is not { } m)
                return "camera: enhanced, not yet started";
            var s = m.Current;
            // Where the field starts, as the overlay's other places are.
            var vantage = _vantage is { } v && (v - (_finder?.Field?.Min ?? Vector2.Zero)) is var at
                ? string.Create(CultureInfo.InvariantCulture, $", vantage ({at.X:F1}, {at.Y:F1})") : "";
            return string.Create(CultureInfo.InvariantCulture,
                $"camera: enhanced, {Kind}{(Target is { } t ? $", target {t.Sheet.Name}" : "")}{(Released ? ", yours" : "")}; " +
                $"yaw {Deg(s.Yaw):F0} pitch {Deg(s.Pitch):F0} distance {s.Distance:F1}, aim speed {m.AimSpeed.Length():F1}, cuts {Cuts}{vantage}");
        }
    }

    /// <summary>It starts afresh from the original's camera, as it's turned on.</summary>
    public void Begin()
    {
        _motion = null;
        _turnOf = null;
        _stateWas = null;
        _correction = null;
        _vantage = null;
        _vantageSide = 0;
        _counts = default;
        _followOf = null;
        _pan = null;
        TurnEnded();
    }

    /// <summary>
    /// The player turns the camera in their turn: it lets go, and the original's camera takes over from the shot shown until they aim or
    /// the turn ends, its leash swooping it in as it pulls.
    /// </summary>
    internal void TakeHand(FollowCamera original)
    {
        if (Released || _motion is null)
            return;
        Released = true;
        original.PlaceAt(Camera.Position);
        original.AimAt(Camera.Aim);
        original.LookXZ = Camera.LookXZ;
    }

    /// <summary>The turn ends: the director has the camera again, and forgets the target, the aim and the plan.</summary>
    internal void TurnEnded()
    {
        // The first tick after looks at once.
        _sinceDecide = int.MaxValue / 2;
        _rested = (null, Vector3.Zero);
        Released = false;
        _cone.Clear();
        _framing = null;
        _spellAim = null;
        _aiming = null;
        _plan = null;
        _volley = null;
        _walk = null;
        _opened = false;
        _opening = null;
        _ahead = null;
        _behind = null;
        _carried = 0;
        _swing = null;
        _swung = 0;
        _hiddenFor = 0;
        _lastTurn = 0;
        _framingTo = null;
        _blowAim = null;
        _shoulderDistance = null;
        _glided = false;
        _letGo = 0;
        _leadBy = null;
    }

    /// <summary>The live battle as this tick reads it, under Camera: Enhanced.</summary>
    private readonly CameraScene _live = new();

    /// <summary>The scene this tick reads.</summary>
    private CameraScene _scene = null!;

    /// <summary>
    /// A tick of Camera: Enhanced, after the original's camera has stepped: it reads the live battle (<see cref="CameraScene.Read"/>).
    /// </summary>
    /// <param name="original">The original's camera, which goes on underneath.</param>
    /// <param name="glide">The bodies the original's glide runs between, while one is on.</param>
    internal void Step(Battle battle, FollowCamera original, (Body From, Body To)? glide)
    {
        _live.Read(battle);
        Step(_live, new DrawnCamera(original.Position, original.Aim, original.LookXZ), glide is { } g
            ? (new ShotSubject(g.From.Sphere.Position, g.From.ScaledRadius), g.To != g.From ? new ShotSubject(g.To.Sphere.Position, g.To.ScaledRadius) : null)
            : null);
    }

    /// <summary>
    /// What else each shot must hold this tick, beyond what it finds for itself: Start Early's director's beats of other actions
    /// (<see cref="BattleDirector"/>). None under Camera: Enhanced.
    /// </summary>
    public Func<Shot, IReadOnlyList<ShotSubject>, Shot>? Reframe { get; set; }

    /// <summary>A tick, reading <paramref name="scene"/>: the live battle's, or the drawing's.</summary>
    /// <param name="original">The original's camera, which goes on underneath.</param>
    /// <param name="glide">Where the bodies the original's glide runs between are, while one is on.</param>
    public void Step(CameraScene scene, DrawnCamera original, (ShotSubject From, ShotSubject? To)? glide)
    {
        _scene = scene;
        var t = Tuning;
        if (!ReferenceEquals(_world, scene.World) || _finder is null)
        {
            _world = scene.World;
            _finder = new ShotFinder(new SightLines(_world), t);
        }
        _finder.Tuning = t;
        _finder.Field = scene.Field;
        var sight = _finder.Sight;
        sight.Radius = t.SightRadius;
        sight.BodyScale = t.BodyOcclusion;
        sight.Bodies.Clear();
        foreach (var b in scene.Bodies)
        {
            if (!b.Freed)
                sight.Bodies.Add(b.Sphere);
        }
        PlaceVantage();
        // The point the shot follows whoever's turn it is by, drawn after their body, or put on it where it was put somewhere.
        if (scene.Current is { } followed)
        {
            var body = scene.Centre(followed);
            if (_followOf != followed || Vector3.Distance(body, _follow.At) > t.FollowReach)
            {
                _follow.Reset(body);
                _followOf = followed;
            }
            else
            {
                _follow.Step(body, t.FollowSmoothing);
            }
        }
        else
        {
            _followOf = null;
        }
        var originalShot = Shot.Of(original.Position, original.Aim, _motion?.Current.Yaw ?? Shot.YawOf(original.LookXZ));
        _from = _motion?.Current.Yaw ?? originalShot.Yaw;
        var fresh = scene.Current != _turnOf;
        if (fresh)
        {
            _turnOf = scene.Current;
            TurnEnded();
        }
        // The player's turn begun, or come back to from a menu or an aim: the one tick of it that may cut.
        var entering = scene.State == CombatState.Turn && (fresh || _stateWas != CombatState.Turn);
        _stateWas = scene.State;
        // Aiming takes the camera back from the player's hand.
        if (scene.State is CombatState.TargetAttack or CombatState.TargetSpell or CombatState.TargetThrown or CombatState.PlaceArea)
            Released = false;

        var aimed = _aiming;
        _aiming = null;
        _cutNow = false;
        _turnBy = 0;
        _tracking = false;
        _walkMoved = null;
        _subjectsNew = false;
        var intent = Intent;
        (intent.Behind, intent.Framing, intent.FramingTo, intent.EyeRail, intent.Along) = (null, null, null, null, null);
        if (intent.Swung is { } swung)
            intent.Swung = swung with { Ago = swung.Ago + 1 };
        intent.Rail.Clear();
        var goal = Decide(originalShot, glide, aimed);
        // Start Early's director: widened, or another taken, to hold what else is happening.
        if (!Released && Reframe is { } reframe)
            goal = reframe(goal, _subjects);
        // The player's own camera is the original's as it is; the director's shots keep their distance.
        if (!Released)
            goal = goal with { Pitch = Math.Clamp(goal.Pitch, Rad(-60), Rad(89)), Distance = MathF.Max(goal.Distance, t.NearestDistance) };
        Goal = goal;

        var caps = t.Caps;
        var player = scene.Current is { } who && !scene.Controlled(who) ? who : null;
        Walking = !Released && _aiming is null && scene.State == CombatState.Turn && player is not null;
        // Whether the character is out of the shot shown in their turn, which their turn's first tick cuts for.
        var edge = 0f;
        if (!Released && scene.State == CombatState.Turn && player is { } me && _motion is { } shown)
        {
            var (x, y, depth) = ShotFraming.OnScreen(shown.Current, scene.Centre(me), t.Lens);
            edge = depth <= 0 ? float.PositiveInfinity : MathF.Max(MathF.Abs(x), MathF.Abs(y));
        }
        var lost = edge > 1;
        Quick = Released || _aiming is not null;
        if (_motion is null)
        {
            // The battle's first shot is cut to: there's nothing yet to move from.
            _motion = new CappedMotion(goal, caps);
            Camera.Cut();
        }
        var hand = MathF.Sqrt(t.HandAcceleration);
        // The player walking, the goal glides and the camera rides it (below), so it neither trails them nor needs to catch up.
        _motion.Caps = Released
            ? new MotionCaps(caps.Aim * t.HandAcceleration, caps.Turn * t.HandAcceleration, caps.Zoom * t.HandAcceleration, caps.Settle * hand,
                caps.TurnSettle * hand, caps.ZoomSettle * hand)
            : _aiming is not null ? caps with { Aim = caps.Aim * t.AimingAcceleration, Settle = caps.Settle * MathF.Sqrt(t.AimingAcceleration) }
            : Walking ? new MotionCaps(caps.Aim * t.GlidePace, caps.Turn * t.GlidePace, caps.Zoom * t.GlidePace, caps.Settle * MathF.Sqrt(t.GlidePace),
                caps.TurnSettle * MathF.Sqrt(t.GlidePace), caps.ZoomSettle * MathF.Sqrt(t.GlidePace))
            : caps;
        // Start Early's director drawing back to hold what else is happening (a result, a walker): the view widens quicker than it
        // closes in, so what it widens for isn't left at the frame's edge while it does.
        if (Reframe is not null && !Released && _aiming is null && goal.Distance > _motion.Current.Distance * 1.05f)
            _motion.Caps = _motion.Caps with { Zoom = _motion.Caps.Zoom * t.DrawBackPace, ZoomSettle = _motion.Caps.ZoomSettle * MathF.Sqrt(t.DrawBackPace) };
        _motion.GoalJump = t.GoalJump;
        // Only the player's own hand is followed at its speed; the director's shots are trailed, so a goal that stops isn't overshot.
        _motion.MostFollowed = Released ? (t.FollowSpeed, Rad(t.FollowTurn)) : (0, 0);
        _motion.NearestDistance = MathF.Min(t.NearestDistance, goal.Distance);
        // In the player's turn, and in an action whose body walks, the shot moves with the walker, as a third person's camera does: only
        // the change of framing is capped, so the view never trails one who runs, and stops as they stop. It's carried by the point that
        // follows them, so their stride doesn't jolt it.
        var (carriedBy, turnedBy) = (Vector3.Zero, 0f);
        if ((Walking || _tracking) && _followOf is { } walker && walker == _scene.Current)
        {
            (carriedBy, turnedBy) = (_follow.Moved, _turnBy);
            // Gliding, the turn carried is smoothed: its speed follows the orbit's and the easing's as a critically damped spring
            // (TurnSmoothing), so the view starts and stops turning without a kick, and what it falls short by the glide makes up.
            if (Walking)
            {
                if (!_glided)
                    (_turnRate, _turnRateSpeed) = (0, 0);
                Smooth(ref _turnRate, ref _turnRateSpeed, _turnBy, 1 / MathF.Max(t.TurnSmoothing, 1e-3f));
                turnedBy = _turnRate;
            }
            _motion.Carry(carriedBy);
            // And turned with the way to its target as they walk round it, so the angle doesn't trail them either.
            if (turnedBy != 0)
                _motion.Turn(turnedBy);
        }
        // An AI's walk: carried along with its shot's own change, so the eye that moves a little and the turn after the walker don't trail.
        if (_walkMoved is { } moved)
            _motion.Follow(moved.From, moved.To);

        var now = _motion.Current;
        var travel = Vector3.Distance(goal.Aim, now.Aim);
        // A swing round is judged only where the yaw means something: not looking straight down.
        var turn = MathF.Max(now.Pitch, goal.Pitch) < Rad(75) ? MathF.Abs(Shot.Wrap(goal.Yaw - now.Yaw)) : 0;
        var tilt = MathF.Abs(goal.Pitch - now.Pitch);
        // Never while the player walks the character: the stick's way would turn under them. Their turn begins with them in view, cut
        // to unless it's near.
        var mayCut = !Released && (!Walking || entering);
        var cut = !Holding && (_cutNow || mayCut && (travel > t.CutDistance || turn > Rad(t.CutTurn) || tilt > Rad(t.CutPitch))
            || entering && !Released && (lost || travel > t.EnterCutDistance || turn > Rad(t.EnterCutTurn)));
        if (Holding)
            _cutNow = false;
        // Into an aim, about one, or back from one to the turn, the move is a pan, not a cut, so the player sees where they're aiming. A
        // pan carries on through its state, its end following the goal; a new state is judged afresh.
        var (aimingNow, wasAiming) = (_aiming is not null, aimed is not null);
        if (Released || _cutNow || _pan is { } p && p.State != _scene.State)
            _pan = null;
        if (_pan is null && !Released && !_cutNow && !Holding
            && (aimingNow && (wasAiming ? cut : travel > 1 || turn > Rad(10) || tilt > Rad(10)) || wasAiming && entering && cut))
            _pan = (_scene.State, now, 0, Math.Clamp((int)MathF.Round(t.PanTicks + travel * t.PanTicksPerUnit), t.PanTicks, Math.Max(t.PanTicks, t.PanMostTicks)));
        Quick |= _pan is not null;
        if (_pan is { } pan)
        {
            var done = pan.Done + 1;
            var s = (float)done / pan.Ticks;
            _motion.Cut(Blend(pan.From, goal, s * s * (3 - 2 * s)));
            _pan = done >= pan.Ticks ? null : pan with { Done = done };
        }
        else if (cut)
        {
            _motion.Cut(goal);
            Camera.Cut();
            Cuts++;
        }
        else if (Walking)
        {
            // The player's turn: its goal glides after the shot found (ShotGlide), its place, speed and acceleration all continuous, so a
            // new framing, target or distance bends the view's course rather than kicking it; and the camera rides the glide at its speed,
            // carried with the character and the orbit as the glide is, so it neither trails nor needs to catch up.
            _glide.Time = t.GlideTime;
            if (_glided)
            {
                _glide.Carry(carriedBy);
                _glide.Turn(turnedBy);
            }
            else
            {
                var (yawSpeed, pitchSpeed, zoomSpeed) = _motion.Speeds;
                _glide.Reset(_motion.Current, (_motion.AimSpeed, yawSpeed, pitchSpeed, zoomSpeed));
            }
            _glide.Step(goal);
            _motion.Step(_glide.Current, _glide.Speeds);
        }
        else
        {
            // A long way to go may be a swoop (TravelZoom): stood back by the way left, both ends in view, coming down as it arrives.
            _motion.Step(t.TravelZoom > 0 && travel * t.TravelZoom > goal.Distance ? goal with { Distance = travel * t.TravelZoom } : goal);
        }
        _glided = Walking && _pan is null && !cut;
        if (cut && Walking)
        {
            _glide.Time = t.GlideTime;
            _glide.Reset(goal, default);
            _glided = true;
        }
        var shot = _motion.Current;
        Place(shot);

        // A shot kept from an earlier tick (a volley's, a choice held) keeps what it was found for.
        if (_subjectsNew || Kind != intent.Kind)
            intent.Subjects.Clear();
        if (_subjectsNew)
            intent.Subjects.AddRange(_subjects);
        (intent.Goal, intent.Shown, intent.Kind, intent.Lens) = (goal, shot, Kind, t.Lens);
        (intent.Turn, intent.Target, intent.Follow) = (_scene.Current, _scene.State == CombatState.Turn ? _behind?.Target ?? Target : Target, FollowAt);
        (intent.Vantage, intent.Field) = (_vantage, _finder.Field);
    }

    /// <summary>Puts the camera shown on <paramref name="shot"/>, its eye kept above the ground.</summary>
    private void Place(Shot shot)
    {
        var eye = shot.Eye;
        if (_finder!.Sight.GroundAt(eye.X, eye.Z, MathF.Max(eye.Y, shot.Aim.Y) + 30) is { } ground && eye.Y < ground + Tuning.FloorClearance)
            eye.Y = ground + Tuning.FloorClearance;
        Camera.PlaceAt(eye);
        Camera.AimAt(shot.Aim);
        // The look from the yaw, not the eye, so a shot from above still turns the stick by the screen's up.
        Camera.LookXZ = shot.LookXZ;
    }

    /// <summary>
    /// Cuts to <paramref name="shot"/> now, from outside its own choosing: Start Early's director keeping a beat about to strike in shot
    /// (<see cref="BattleDirector"/>). Its own shot moves on from there.
    /// </summary>
    public void CutTo(Shot shot)
    {
        if (_motion is null || _finder is null)
            return;
        _motion.Cut(shot);
        _pan = null;
        _glided = false;
        Camera.Cut();
        Cuts++;
        Place(shot);
        Intent.Shown = shot;
    }

    /// <summary>A tick of a critically damped spring drawing <paramref name="x"/> after <paramref name="goal"/>, <paramref name="w"/> a second.</summary>
    private static void Smooth(ref float x, ref float v, float goal, float w)
    {
        const float tick = 1f / 60;
        var e = x - goal;
        var decay = MathF.Exp(-w * tick);
        var temp = (v + w * e) * tick;
        (x, v) = (goal + (e + temp) * decay, (v - w * temp) * decay);
    }

    /// <summary>Starts this tick's subjects afresh.</summary>
    private void NewSubjects()
    {
        _subjects.Clear();
        _subjectsNew = true;
    }

    private Shot Decide(Shot originalShot, (ShotSubject From, ShotSubject? To)? glide, (CombatState, Combatant, float)? aimed)
    {
        if (_scene.Current is not { } current)
        {
            Kind = "the original's";
            return originalShot;
        }
        if (Released)
        {
            Kind = "the original's, the player's hand on it";
            return originalShot;
        }
        var acting = _scene.Controlled(current) || _scene.State is CombatState.Facing or CombatState.Action or CombatState.Cast or CombatState.Result
            or CombatState.Moving or CombatState.WaitIdle;
        if (!acting)
        {
            _plan = null;
            _volley = null;
            _walk = null;
        }
        if (_scene.State != CombatState.Turn && _scene.State != CombatState.TargetSpell)
            _framing = null;
        if (_scene.State != CombatState.Turn)
        {
            _ahead = null;
            _behind = null;
            _framingTo = null;
        }
        if (_scene.State != CombatState.TargetSpell)
            _spellAim = null;
        if (_scene.State != CombatState.TargetAttack)
            _blowAim = null;
        if (acting)
            return ActionShot(current, originalShot);
        var t = Tuning;
        switch (_scene.State)
        {
            case CombatState.PlaceArea when _scene.Facts.AreaMarker is { } marker:
            {
                Kind = "placing an area, from above";
                return AreaShot(current, aimed, marker);
            }
            case CombatState.TargetThrown:
                Kind = "aiming a throw";
                return Aimed(current, aimed, Ground(_scene.Facts.ThrownAim, current), 1, Rad(t.AimPitch));
            case CombatState.TargetSpell when _scene.Facts.Aim is { } reach:
                return SpellAim(current, reach);
            case CombatState.TargetAttack when _scene.Facts.Ranged:
                Kind = "aiming a ranged attack";
                return Aimed(current, aimed, Reticle(current), 1, Rad(t.AimPitch));
            case CombatState.TargetAttack:
                return BlowAim(current, originalShot);
            case CombatState.Turn:
                return TurnShot(current, originalShot);
            case CombatState.MenuSpells or CombatState.MenuItems or CombatState.TroubadourPrompt or CombatState.Pause
                or CombatState.PauseRequested when _motion is { } m:
                // The player choosing: the view holds where it is, rather than going to the original's close shot.
                Kind = "held while the player chooses";
                return m.Current;
        }
        NewSubjects();
        var owner = Owner() ?? current;
        _subjects.Add(_scene.Sphere(owner));
        if (owner != current && !_scene.Freed(current))
            _subjects.Add(_scene.Sphere(current));
        if (glide is { } g)
        {
            _subjects.Add(g.From);
            if (g.To is { } to)
                _subjects.Add(to);
        }
        return Cleared(originalShot, _subjects, "the original's");
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // The original's shot, kept clear: where nothing else is shown

    /// <summary>
    /// The original's shot, turned, raised or drawn back where it hides a subject it holds behind a wall or a body. Whether it does is
    /// looked at every <see cref="CameraTuning.DecideEvery"/> ticks, and the change is given up once the original's shot has been clear
    /// <see cref="CameraTuning.ClearChecks"/> looks in a row.
    /// </summary>
    private Shot Cleared(Shot originalShot, List<ShotSubject> subjects, string kind)
    {
        var t = Tuning;
        var finder = _finder!;
        // It's judged by the subjects the original's shot holds; they're what it shows.
        var held = subjects.Where(s => ShotFraming.Holds(originalShot, s with { Radius = 0 }, t.Lens)).ToList();
        if (held.Count == 0)
            held = subjects.Take(1).ToList();
        if (++_sinceDecide >= t.DecideEvery)
        {
            _sinceDecide = 0;
            if (finder.HiddenWeight(originalShot, held) == 0)
            {
                if (_correction is not null && ++_clear >= t.ClearChecks)
                    _correction = null;
            }
            else
            {
                _clear = 0;
                if (_correction is not { } c || finder.HiddenWeight(finder.Corrected(originalShot, c, held), held) > 0)
                    _correction = finder.Clearing(originalShot, held) ?? _correction;
            }
        }
        Kind = _correction is null ? kind : kind + ", kept clear";
        return _correction is { } k ? finder.Corrected(originalShot, k, held) : originalShot;
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // The player's turn: low over the shoulder at the enemy they head for, and its pack

    /// <summary>
    /// The player's turn: low over the character's shoulder at the enemy they head for (<see cref="TargetCone"/>) and its pack, near the
    /// character, orbiting the pack as they go round it, every turn of it continuous. It opens on the enemy ahead, else the nearest within
    /// <see cref="CameraTuning.TargetRange"/>, held until they move or turn, so the opening shot isn't changed under the player before
    /// they've done anything. With no enemy ahead, it trails them on a leash as a third person's camera does, its aim leading them by how
    /// fast they go (<see cref="CameraTuning.LeadTime"/>). Its goal glides after the shot found here (<see cref="ShotGlide"/>).
    /// </summary>
    private Shot TurnShot(Combatant current, Shot originalShot)
    {
        var t = Tuning;
        var me = _scene.Centre(current);
        // What the shot is placed by: the point that follows them, so their stride doesn't jolt it.
        var anchor = _followOf == current ? _follow.At : me;
        var heading = _scene.Heading(current);
        // The most the speed the shot's turned at by itself changes in a tick.
        var speedUp = Rad(t.TurnAcceleration) / 3600;
        _candidates.Clear();
        var hostile = _scene.Hostile(current);
        foreach (var c in _scene.Bodies)
        {
            if (c.Hostile != hostile && !c.Freed && !c.Dead && c.Active)
                _candidates.Add(new TargetCandidate(c.Who, c.Centre));
        }
        var opening = !_opened;
        if (!_opened)
        {
            _opened = true;
            _opening = (me, heading);
            _cone.Pick(me, heading, _candidates, t, atOnce: true);
            if (Target is null && Nearest(current) is { } nearest && Flat(_scene.Centre(nearest) - me) <= t.TargetRange)
                _cone.Hold(nearest);
            _sinceDecide = int.MaxValue / 2;
            _framing = null;
            _framingTo = null;
        }
        else if (_opening is { } o && (Flat(me - o.At) > 0.05f || Vector2.Dot(heading, o.Heading) < MathF.Cos(Rad(2))))
        {
            _opening = null;
        }
        // A new target held: the shot isn't turned to it, and its framing is found again only if the one held doesn't show the character
        // and it.
        if (_opening is null && _cone.Pick(me, heading, _candidates, t))
            _sinceDecide = int.MaxValue / 2;
        NewSubjects();
        _subjects.Add(_scene.Sphere(current));
        // A target let go is still shot over the shoulder at for a moment (TrailAfter), so heading past one toward another doesn't put the
        // trailing shot between them, its aim flung about and back.
        var target = Target;
        if (target is not null)
            _letGo = 0;
        else if (_behind is { Target: var was } && !_scene.Freed(was) && _scene.Active(was) && !_scene.Dead(was) && ++_letGo < t.TrailAfter)
            target = was;
        if (target is null)
        {
            // A swing onto a framing over the shoulder left, the leash takes on its speed.
            if (_framing is not null)
                LeaveSwing();
            _framing = null;
            _framingTo = null;
            _behind = null;
            _shoulderDistance = null;
            // From the shot as the target was lost, low and near.
            var was = _motion?.Current ?? originalShot;
            _ahead ??= (was.Yaw, was.Pitch, was.Distance);
            // Trailed on a leash, as a third person's camera is: the eye stays where it is but for being drawn after the character, so a
            // step across the view turns it round after them, and walking on they come to be seen from behind; a step toward it or away
            // only draws it along. The turn's speed changes no faster than TurnAcceleration, from the orbit's as the target's let go, so
            // it neither starts nor stops hard.
            var leash = 0f;
            if (_motion is { } m && _followOf == current && _follow.Moved != Vector3.Zero)
            {
                var shown = m.Current;
                var eye = shown.Eye;
                var aimed = shown.Aim + _follow.Moved;
                var back = new Vector2(eye.X - aimed.X, eye.Z - aimed.Z);
                if (back.Length() > 0.5f)
                    leash = Shot.Wrap(MathF.Atan2(back.X, back.Y) - shown.Yaw);
            }
            _carried = Math.Clamp(leash, _carried - speedUp, _carried + speedUp);
            // A view cleared of what hides the character swung onto (SwingTime), turned with the leash.
            var swing = StepSwing();
            _turnBy = _lastTurn = _carried + swing.X;
            if (_turnBy != 0 || swing != Vector3.Zero)
            {
                var (y, p, d) = _ahead.Value;
                _ahead = (Shot.Wrap(y + _turnBy), p + swing.Y, d + swing.Z);
            }
            var lead = anchor + LeadBy(Leading(current));
            var follow = Ahead(lead);
            // A body or a wall hiding the character turns, raises or draws the view back, swung onto (taken at once as the turn opens),
            // and it stays so rather than swinging back.
            if (_swing is null && ++_sinceDecide >= t.DecideEvery)
            {
                _sinceDecide = 0;
                if (_finder!.HiddenWeight(follow, _subjects) > 0 && _finder.Clearing(follow, _subjects) is { } by)
                {
                    var cleared = _finder.Corrected(follow, by, _subjects);
                    var (y, p, d) = _ahead.Value;
                    if (opening)
                    {
                        _ahead = (cleared.Yaw, cleared.Pitch, cleared.Distance);
                        follow = Ahead(lead);
                    }
                    else
                    {
                        _swing = Swing(new Vector3(Shot.Wrap(cleared.Yaw - y), cleared.Pitch - p, cleared.Distance - d));
                        Intent.Swung = ("the character hidden, trailing", 0);
                    }
                }
            }
            Kind = "no enemy ahead, trailing them";
            return follow;
        }
        // A swing onto a view cleared with no enemy ahead left, the orbit takes on its speed.
        if (_ahead is not null)
            LeaveSwing();
        _ahead = null;
        _subjects.Add(_scene.Sphere(target));
        // The target's pack: the shot leads toward the middle of them all, and holds them while they're near.
        var at = _scene.Centre(target);
        // Each of the pack counted as far as it's in it, fading out over the last PackFade of ClusterRadius from the target, and held as
        // far as the character is near it too, over the last PackFade of PackRange: so a body comes into the framing and the aim's lead
        // bit by bit, rather than all at once, and the view doesn't jump as one does.
        var nearness = Fade(t.PackRange - Flat(at - me), t.PackFade);
        var near = nearness > 0;
        var (pack, weight, n) = (at, 1f, 1);
        _pack.Clear();
        foreach (var c in _candidates)
        {
            var other = (Combatant)c.Who;
            var inPack = Fade(t.ClusterRadius - Flat(c.At - at), t.PackFade);
            if (other == target || inPack <= 0)
                continue;
            if (inPack * nearness > 0)
                _pack.Add((other, inPack * nearness));
            pack += c.At * inPack;
            weight += inPack;
            n++;
        }
        _pairs.Clear();
        AddPair(current, target);
        var ahead = pack / weight;
        // Orbiting the target as the character goes round it: the way over their shoulder at it turns the shot with it straight away
        // (Turn), up to CarryTurn a second, that speed changing no faster than TurnAcceleration; not at all within StrikeRange of it, where
        // the way means little and swings wildly as they step about it, and coming in over ShoulderNear beyond; not while it's being let
        // go, and not by a new target's jump. What the shot isn't turned by goes into its framing, the offset from the way, so it isn't
        // caught up later: the view never turns hard to a target. The way to the target, not to its pack's middle: walking straight at one
        // at the pack's edge doesn't turn the view.
        var behind = Look(anchor, at);
        var (change, most) = _behind is { } b
            ? (Shot.Wrap(behind - b.Behind),
                b.Target != target || _cone.Losing || _letGo > 0 ? 0 : Rad(t.CarryTurn) / 60 * Fade(Flat(at - anchor) - t.StrikeRange, t.ShoulderNear))
            : (0, 0);
        var carried = Math.Clamp(Math.Clamp(change, -most, most), _carried - speedUp, _carried + speedUp);
        Absorb(change - carried);
        _frameBy = (anchor + LeadBy(Lead(anchor, ahead) - anchor), t.ShoulderDistance);
        // Held as far as each is: a body barely in the pack is drawn in toward the aim, so it asks the framing for little room.
        foreach (var (other, held) in _pack)
        {
            var sphere = _scene.Sphere(other);
            var aim = _frameBy.Aim!.Value;
            _subjects.Add(new ShotSubject(aim + (sphere.At - aim) * held, sphere.Radius * held) { Weight = t.ClusterWeight * held });
        }
        // Taken over the shoulder from the view the trailing left (it had no target), as it is: kept if it shows both.
        if (_framing is null && !opening && _motion is { } trailed)
            _framing = (Shot.Wrap(trailed.Current.Yaw - behind), Math.Clamp(trailed.Current.Pitch, Rad(t.Pitches.Min()), Rad(t.Pitches.Max())));
        // Found again only where the character or the target has left it or is hidden (a new target among them), and swung onto, so the
        // shot never steps as they walk, and a view that already shows both is kept however far off the middle they are.
        if (_framingTo is null)
        {
            var held = _framing;
            Frame(behind, force: false, judged: 2);
            if (held is { } h && _framing is { } found && found != h)
            {
                (_framingTo, _framing) = (found, h);
                _swing = Swing(new Vector3(Shot.Wrap(found.Offset - h.Offset), found.Pitch - h.Pitch, 0));
                Intent.Swung = (_framedWhy ?? "a new framing", 0);
                _hiddenFor = 0;
            }
        }
        // Swung onto along the smoothest way there (SwingTime), the shot turned with it as the orbit turns it, so the view doesn't trail in
        // after its goal.
        var eased = 0f;
        if (_framingTo is { } to && _framing is { } now)
        {
            var swing = StepSwing();
            eased = swing.X;
            _framing = _swing is null ? to : (Shot.Wrap(now.Offset + swing.X), now.Pitch + swing.Y);
            if (_swing is null)
                _framingTo = null;
        }
        var total = carried + eased;
        // Never turned round by itself to the front of a character who walks: no further than MostAcross from straight behind the way they
        // head, or than its last goal was, where they've turned toward it themselves (so turning them never turns it).
        // The turn back is taken from the framing, and the shot isn't carried past it.
        if (_behind is not null && Flat(_follow.Moved) > 1e-4f && heading.LengthSquared() > 1e-6f)
        {
            var back = MathF.Atan2(-heading.X, -heading.Y);
            var limit = MathF.Max(Rad(t.MostAcross), MathF.Abs(Shot.Wrap(Goal.Yaw - back)));
            var across = Shot.Wrap(behind + _framing!.Value.Offset - back);
            // Turned toward it no faster than stops there, so it's come to softly; already turning toward it faster, as they set off, as
            // far past it as it takes to stop.
            if (total != 0 && MathF.Sign(total) == MathF.Sign(across))
            {
                if (MathF.Sign(_lastTurn) == MathF.Sign(across))
                    limit = MathF.Max(limit, MathF.Abs(Shot.Wrap(across - total)) + _lastTurn * _lastTurn / (2 * speedUp) + MathF.Abs(_lastTurn) / 2);
                var fastest = Stopping(MathF.Max(0, limit - MathF.Abs(across)), speedUp);
                if (MathF.Abs(total) > fastest)
                {
                    var slowed = MathF.Sign(total) * fastest;
                    Absorb(total - slowed);
                    across = Shot.Wrap(across - (total - slowed));
                    total = slowed;
                }
            }
            if (MathF.Abs(across) > limit)
            {
                var by = MathF.Sign(across) * limit - across;
                Absorb(-by);
                if (MathF.Sign(by) == -MathF.Sign(total))
                    total = MathF.Abs(by) >= MathF.Abs(total) ? 0 : total + by;
            }
        }
        _carried = total - eased;
        _turnBy = _lastTurn = total;
        _behind = (target, behind);
        Kind = n == 1 ? "over the shoulder" : near ? $"over the shoulder at a pack of {n}" : $"over the shoulder toward a pack of {n}";
        Intent.Behind = behind;
        Intent.Framing = Shot.Wrap(behind + _framing!.Value.Offset);
        Intent.FramingTo = _framingTo is { } towards ? Shot.Wrap(behind + towards.Offset) : null;
        var framed = FramedNow(behind);
        // Drawn back at once to hold what it must, but come in only slowly (ZoomInTime), so the view doesn't pump in and out as the
        // bodies shift about.
        _shoulderDistance = _shoulderDistance is { } stood && stood > framed.Distance
            ? stood + (framed.Distance - stood) * (1 - MathF.Exp(-1f / 60 / MathF.Max(t.ZoomInTime, 1e-3f)))
            : framed.Distance;
        return framed with { Distance = _shoulderDistance.Value };
    }

    /// <summary>
    /// A swing of <paramref name="by"/> (turn and pitch, radians, and distance) over <see cref="CameraTuning.SwingTime"/>, or longer where
    /// its turn or pitch would go faster than <see cref="CameraTuning.FramingTurn"/>: a minimum-jerk curve's fastest is 15/8 of its mean.
    /// </summary>
    private (Vector3 By, int Done, int Ticks) Swing(Vector3 by)
    {
        var t = Tuning;
        var time = MathF.Max(t.SwingTime, 1.875f * MathF.Max(MathF.Abs(by.X), MathF.Abs(by.Y)) / MathF.Max(Rad(t.FramingTurn), 1e-3f));
        return (by, 0, Math.Max(1, (int)MathF.Round(time * 60)));
    }

    /// <summary>
    /// A tick of the swing under way: how far it moves this tick along the minimum-jerk curve <c>10s³ − 15s⁴ + 6s⁵</c>, which starts and
    /// ends at rest with its acceleration nothing, the smoothest way from one framing to another in its time.
    /// </summary>
    private Vector3 StepSwing()
    {
        if (_swing is not { } w)
            return Vector3.Zero;
        var done = w.Done + 1;
        var step = w.By * (MinJerk((float)done / w.Ticks) - MinJerk((float)w.Done / w.Ticks));
        _swing = done >= w.Ticks ? null : w with { Done = done };
        _swung = step.X;
        return step;

        static float MinJerk(float s) => s * s * s * (10 + s * (-15 + 6 * s));
    }

    /// <summary>Drops the swing under way, its turn's speed taken on by the orbit or the leash.</summary>
    private void LeaveSwing()
    {
        if (_swing is not null)
            _carried += _swung;
        (_swing, _swung) = (null, 0);
    }

    /// <summary>How far in something is that's <paramref name="inside"/> its edge, 0 to 1 over the last <paramref name="over"/> of it, eased.</summary>
    private static float Fade(float inside, float over)
    {
        var s = Math.Clamp(inside / MathF.Max(over, 1e-3f), 0, 1);
        return s * s * (3 - 2 * s);
    }

    /// <summary>
    /// The fastest a turn may go, radians a tick, that slowing by <paramref name="slowing"/> a tick each tick still stops within
    /// <paramref name="room"/> radians.
    /// </summary>
    private static float Stopping(float room, float slowing) => slowing * (MathF.Sqrt(0.25f + 2 * room / slowing) - 0.5f);

    /// <summary>
    /// Turns the framing held over the shoulder, and the one it eases to, back by <paramref name="by"/> radians: the way over the shoulder
    /// has turned by it, and the shot doesn't.
    /// </summary>
    private void Absorb(float by)
    {
        if (by == 0)
            return;
        if (_framing is { } f)
            _framing = (Shot.Wrap(f.Offset - by), f.Pitch);
        if (_framingTo is { } g)
            _framingTo = (Shot.Wrap(g.Offset - by), g.Pitch);
    }

    /// <summary>
    /// How far the aim leads a character with no enemy ahead: where the point that follows them will be <see cref="CameraTuning.LeadTime"/>
    /// from now, no further than <see cref="CameraTuning.AimLeadMost"/>. So it's on them standing, a little ahead walking and more running,
    /// and eases back onto them as they stop, the point's speed doing so.
    /// </summary>
    private Vector3 Leading(Combatant current)
    {
        if (_followOf != current)
        {
            _rested = (null, Vector3.Zero);
            return Vector3.Zero;
        }
        var t = Tuning;
        var by = new Vector3(_follow.Speed.X, 0, _follow.Speed.Z) * t.LeadTime;
        var length = by.Length();
        by = length > t.AimLeadMost ? by * (t.AimLeadMost / length) : by;
        // Slowing the same way, the lead rests where it got to rather than easing back onto them as they stop (the user's, Nov 10: the
        // view wiggled back as Keelin stopped). Turned about or across, it follows the new way; it's on them as each turn begins.
        if (_rested.Of != current)
            _rested = (current, Vector3.Zero);
        var rest = _rested.Lead;
        if (by.LengthSquared() >= rest.LengthSquared()
            || by.Length() > t.LeadRestTurned && Vector3.Dot(by, rest) < MathF.Cos(Rad(t.LeadRestAngle)) * by.Length() * rest.Length())
            _rested = (current, by);
        return _rested.Lead;
    }

    /// <summary>The lead the aim rests at as a character slows (<see cref="Leading"/>), and whose it is.</summary>
    private (Combatant? Of, Vector3 Lead) _rested;

    /// <summary>
    /// The aim's lead from the point that follows the character in their turn, glided after <paramref name="wanted"/> as a critically damped
    /// spring (<see cref="CameraTuning.LeadSmoothing"/>): one lead, toward the pack over the shoulder or the way they go with no enemy
    /// ahead, so the aim neither jumps as one gives way to the other or the target changes, nor swings as they turn about.
    /// </summary>
    private Vector3 LeadBy(Vector3 wanted)
    {
        if (_leadBy is not { } by)
        {
            _leadBy = (wanted, Vector3.Zero);
            return wanted;
        }
        var w = 1 / MathF.Max(Tuning.LeadSmoothing, 1e-3f);
        var (x, y, z) = (by.At.X, by.At.Y, by.At.Z);
        var (vx, vy, vz) = (by.Speed.X, by.Speed.Y, by.Speed.Z);
        Smooth(ref x, ref vx, wanted.X, w);
        Smooth(ref y, ref vy, wanted.Y, w);
        Smooth(ref z, ref vz, wanted.Z, w);
        _leadBy = (new Vector3(x, y, z), new Vector3(vx, vy, vz));
        return _leadBy.Value.At;
    }

    /// <summary>The shot with no enemy ahead (<see cref="_ahead"/>) aimed at <paramref name="lead"/>, low and near, as far as holds the character.</summary>
    private Shot Ahead(Vector3 lead)
    {
        var t = Tuning;
        var (yaw, pitch, distance) = _ahead!.Value;
        pitch = Math.Clamp(pitch, Rad(t.Pitches.Min()), Rad(t.Pitches.Max()));
        var hold = ShotFraming.DistanceToHold(_subjects, lead, yaw, pitch, t.Lens);
        return new Shot(lead, yaw, pitch, Math.Clamp(MathF.Max(distance, hold), t.MinDistance, t.ShoulderDistance));
    }

    /// <summary>
    /// Finds the framing of <see cref="_subjects"/> over the shoulder (<paramref name="behind"/>) again where it's due: every look, at
    /// once once the framing held hides one (the character walking across its own sight line), or when <paramref name="force"/>d. Given
    /// <paramref name="judged"/>, only once the framing held hides or leaves out one of the first that many. A framing found replaces the
    /// one held only if it's better by <see cref="CameraTuning.SwitchMargin"/>.
    /// </summary>
    private void Frame(float behind, bool force, int? judged = null)
    {
        var t = Tuning;
        var finder = _finder!;
        if (_framing is not null && !force)
        {
            if (judged is { } k)
            {
                // Out of frame, found again at once; hidden, only once it has been for HiddenPatience, so a body passing between for a
                // moment doesn't swing the view.
                var shot = FramedNow(behind);
                string? outOf = null, hidden = null;
                for (var i = 0; i < Math.Min(k, _subjects.Count); i++)
                {
                    var who = i == 0 ? "the character" : i == 1 ? "the target" : "one held";
                    if (!ShotFraming.Holds(shot, _subjects[i] with { Radius = 0 }, t.Lens))
                        outOf ??= $"{who} out of frame";
                    else if (finder.Sight.Hidden(shot.Eye, _subjects[i]))
                        hidden ??= $"{who} hidden";
                }
                _hiddenFor = hidden is null ? 0 : _hiddenFor + 1;
                _framedWhy = outOf ?? (_hiddenFor >= t.HiddenPatience ? hidden : null);
                if (_framedWhy is null)
                    return;
            }
            else if (++_sinceDecide < t.DecideEvery && finder.HiddenWeight(FramedNow(behind), _subjects) == 0)
            {
                return;
            }
        }
        _sinceDecide = 0;
        // Turning from where the view is headed costs, not from where it is: gliding, the goal it glides to. So a new framing carries on
        // the way the view goes rather than turning it back.
        var course = _glided ? Goal.Yaw : _from;
        var heldScore = _framing is not null && !force ? finder.Score(FramedNow(behind), _subjects, course, behind, _pairs) : float.NegativeInfinity;
        if (finder.Best(_subjects, course, behind, _pairs, _frameBy.Aim, _frameBy.Most, t.ShoulderYaws) is var (best, score)
            && score > heldScore + (_framing is null || force ? 0 : t.SwitchMargin))
            _framing = (Shot.Wrap(best.Yaw - behind), best.Pitch);
        _framing ??= (0, Rad(t.PreferredPitch));
    }

    /// <summary>The framing held, as a shot of <see cref="_subjects"/> over the shoulder (<paramref name="behind"/>).</summary>
    private Shot FramedNow(float behind) =>
        _finder!.Framed(_subjects, Shot.Wrap(behind + _framing!.Value.Offset), _framing.Value.Pitch, _frameBy.Most, _frameBy.Aim);

    /// <summary>
    /// What a shot over the shoulder of one at <paramref name="from"/> heading for <paramref name="to"/> aims at:
    /// <see cref="CameraTuning.AimLead"/> of the way across the ground, no further than <see cref="CameraTuning.AimLeadMost"/>.
    /// </summary>
    private Vector3 Lead(Vector3 from, Vector3 to)
    {
        var t = Tuning;
        var way = new Vector3(to.X - from.X, 0, to.Z - from.Z);
        var length = way.Length();
        return length < 1e-3f ? from : from + way / length * MathF.Min(t.AimLead * length, t.AimLeadMost);
    }

    /// <summary>Whether a combatant attacks from afar: a ranged weapon and a ranged action.</summary>
    internal static bool IsRanged(Combatant c) =>
        Battle.WeaponOf(c) is { } w && (w.Range > 0 || w.Missile is not null) && c.Action is CombatAction.Ranged or CombatAction.MeleeOrRanged;

    // ---------------------------------------------------------------------------------------------------------------------------
    // An action: one shot for all of it

    /// <summary>
    /// An action, an AI's turn or the player's once they've chosen: one shot holding where the body stands, where it walks to, whom it
    /// acts on and whom the original's camera follows, turned least from the shot now and the next player's turn's
    /// (<see cref="ShotFinder.Planned"/>), from the vantage's side. It's kept the whole action, and planned again only if one of those
    /// leaves it. The player's character walking, it's carried along with them. An AI walking, or fleeing, is shown from near where its
    /// walk ends, turned after it (<see cref="WalkShot"/>), and that walk's last shot is the one kept. While the AI thinks (state 8, before
    /// it has acted), the shot now stays. An action at someone further off, or a spell at an ally, is two shots instead
    /// (<see cref="Volley"/>).
    /// </summary>
    private Shot ActionShot(Combatant current, Shot originalShot)
    {
        if (Volley(current) is { } volley)
            return volley;
        var ai = _scene.Controlled(current);
        NewSubjects();
        _subjects.Add(_scene.Sphere(current));
        var to = Destination(current);
        if (to is { } d)
            _subjects.Add(new ShotSubject(d, _scene.Radius(current)));
        _acted.Clear();
        // One fleeing acts on no one, whoever it last had in mind.
        if (!_scene.Facts.Fleeing)
        {
            Acted(ai ? _scene.Facts.AiTarget : _scene.Facts.Target);
            foreach (var c in _scene.Facts.ChosenB)
                Acted(c);
        }
        foreach (var c in _acted)
            _subjects.Add(_scene.Sphere(c));
        if (Owner() is { } owner && owner != current && !_acted.Contains(owner))
            _subjects.Add(_scene.Sphere(owner));
        _pairs.Clear();
        foreach (var c in _acted)
            AddPair(current, c);
        // Planned within the lens's margins, it's kept while each is anywhere on the screen, or while no more are off it than the plan
        // couldn't help (one too far for any shot), and it's planned again at most every look.
        var screen = Tuning.Lens with { Margin = 1, Floor = 1 };
        // An AI walking: from near where its walk ends, turned after it, and once it's there, settling with the point that follows it
        // onto the walk's end for the rest of its action. Planned again if where it's going moves.
        if (ai && _followOf == current && (to is not null || _walk is { } held && held.Who == current))
        {
            if (to is { } end && (_walk is not { } w || w.Who != current || Flat(w.To - end) > 1))
                _walk = PlanWalk(current, end);
            // Stopped, perhaps short of where it was going: its end framed again from the same way round where it stands, whom it acts
            // on held in it.
            if (to is null && _walk is { Arrived: false } stopped)
            {
                var planned = Shot.Of(stopped.EyeEnd, stopped.AimEnd);
                var framed = _finder!.Framed(_subjects, planned.Yaw, planned.Pitch);
                _walk = stopped with { To = _scene.Centre(current), EyeEnd = framed.Eye, AimEnd = framed.Aim, Last = null, Arrived = true };
            }
            var (was, walked) = (_walk!.Value.Last, WalkShot(current));
            if (was is { } from)
                _walkMoved = (from, walked);
            _plan = (current, walked);
            _planMissed = _subjects.Count(s => !ShotFraming.Holds(walked, s with { Radius = 0 }, screen));
            _sinceDecide = 0;
            Kind = (_scene.Facts.Fleeing ? "an AI's flight" : "an AI's walk") + (to is null ? ", ended" : ", turned after");
            return walked;
        }
        _walk = null;
        // The player's character walking, the shot is carried with them by the point that follows them, as their turn's is.
        if (to is not null && _followOf == current)
        {
            _tracking = true;
            if (_plan is { } carried && carried.Who == current)
                _plan = (current, carried.Shot with { Aim = carried.Shot.Aim + _follow.Moved });
        }
        var kind = ai ? "an AI's turn, planned" : to is null ? "the player's action, planned" : "the player's action, following the walk";
        if (_plan is { } p && p.Who == current
            && (_subjects.Count(s => !ShotFraming.Holds(p.Shot, s with { Radius = 0 }, screen)) <= _planMissed || ++_sinceDecide < Tuning.DecideEvery))
        {
            Kind = kind;
            return p.Shot;
        }
        if (_plan is null && _scene.State == CombatState.AI)
        {
            Kind = "an AI's turn, thinking";
            return _motion?.Current ?? originalShot;
        }
        // Planned again, it keeps what the action has shown in view (where the body stood and was going), so the shot widens rather than swings.
        if (_plan is null)
            _shown.Clear();
        _shown.AddRange(_subjects.Take(to is null ? 1 : 2));
        if (_finder!.Planned([.. _subjects, .. _shown], _from, NextYaw(), _pairs) is not var (shot, _))
            return originalShot;
        _plan = (current, shot);
        _sinceDecide = 0;
        _planMissed = _subjects.Count(s => !ShotFraming.Holds(shot, s with { Radius = 0 }, screen));
        Kind = kind;
        return shot;
    }

    /// <summary>
    /// Plans how an AI's walk (or flight) to <paramref name="to"/> is shown, with <see cref="_subjects"/> as <see cref="ActionShot"/> made
    /// them: its end, the walker there with whom it acts on, framed from round the way it comes (<see cref="CameraTuning.WalkYaws"/>
    /// turned from straight ahead of it, the walker coming at the eye) at <see cref="CameraTuning.PlannedPitches"/>; its start, that eye
    /// moved back toward where it sets off by <see cref="CameraTuning.WalkPan"/> of the walk, no further than
    /// <see cref="CameraTuning.WalkPanMost"/>, aimed at the walker. The one seeing both best, whom it acts on seen beside it as it sets
    /// off if they can be, from the vantage's side, turning least from the shot now and to the next player's turn.
    /// </summary>
    private Walk PlanWalk(Combatant who, Vector3 to)
    {
        var t = Tuning;
        var finder = _finder!;
        var from = _follow.At;
        var way = to - from;
        var length = Flat(way);
        _ends.Clear();
        _starts.Clear();
        _ends.Add(new ShotSubject(to, _scene.Radius(who)));
        _starts.Add(new ShotSubject(from, _scene.Radius(who)));
        // After the walker and where it goes: whom it acts on and whom the original's camera follows.
        for (var i = 2; i < _subjects.Count; i++)
        {
            _ends.Add(_subjects[i]);
            _starts.Add(_subjects[i] with { Weight = t.ClusterWeight });
        }
        var ahead = length > 1e-3f ? MathF.Atan2(way.X, way.Z) : _from;
        var share = length > 1e-3f ? MathF.Min(t.WalkPan, t.WalkPanMost / length) : 0;
        var next = NextYaw();
        (Shot End, float Score)? best = null;
        foreach (var dy in t.WalkYaws)
        {
            foreach (var p in t.PlannedPitches)
            {
                var end = finder.Framed(_ends, Shot.Wrap(ahead + Rad(dy)), Rad(p));
                var start = Shot.Of(end.Eye + share * (from - to), Lead(from, to), end.Yaw);
                var score = finder.Seen(end, _ends, t.PlannedPitch) + finder.Seen(start, _starts, t.PlannedPitch) - finder.Sides(end, _pairs)
                    - t.TurnCost * MathF.Abs(Shot.Wrap(start.Yaw - _from)) - t.NextTurnCost * MathF.Abs(Shot.Wrap(end.Yaw - next));
                if (best is not { } b || score > b.Score)
                    best = (end, score);
            }
        }
        var chosen = best?.End ?? finder.Framed(_ends, ahead, Rad(t.PlannedPitch));
        var rail = LayRail(who, from, to);
        return new Walk(who, from, to, chosen.Eye, chosen.Aim, share, rail, 0, 0, 0, 0, null);
    }

    /// <summary>
    /// Lays an AI's walk's rail (<see cref="_rail"/>) from <paramref name="from"/> along the corners its body will walk to
    /// <paramref name="to"/>, a point every <see cref="RailStep"/> at the height of its middle over the ground there, each the average of
    /// those within <see cref="CameraTuning.WalkRound"/> of it along the way (fewer near its ends, which stay put): so its corners are
    /// rounded and its rises smoothed. Returns its length.
    /// </summary>
    private float LayRail(Combatant who, Vector3 from, Vector3 to)
    {
        var facts = _scene.Facts;
        var radius = _scene.Radius(who);
        _corners.Clear();
        _corners.Add(new Vector2(from.X, from.Z));
        if (facts.Moving)
            _corners.Add(facts.Goal);
        foreach (var goal in facts.Queued)
            _corners.Add(goal);
        _corners.Add(new Vector2(to.X, to.Z));
        _railRaw.Clear();
        var y = from.Y;
        for (var i = 1; i < _corners.Count; i++)
        {
            var (a, b) = (_corners[i - 1], _corners[i]);
            var steps = (int)MathF.Ceiling(Vector2.Distance(a, b) / RailStep);
            for (var k = _railRaw.Count == 0 ? 0 : 1; k <= steps; k++)
            {
                var p = steps == 0 ? b : Vector2.Lerp(a, b, (float)k / steps);
                y = Height(p.X, p.Y, y - radius) + radius;
                _railRaw.Add(new Vector3(p.X, y, p.Y));
            }
        }
        // Resampled evenly along the way.
        _rail.Clear();
        _rail.Add(_railRaw[0]);
        var along = 0f;
        for (var i = 1; i < _railRaw.Count; i++)
        {
            var (a, b) = (_railRaw[i - 1], _railRaw[i]);
            var step = Vector3.Distance(a, b);
            while (step > 1e-5f && along + step >= RailStep * _rail.Count)
                _rail.Add(Vector3.Lerp(a, b, (RailStep * _rail.Count - along) / step));
            along += step;
        }
        if (Vector3.Distance(_rail[^1], _railRaw[^1]) > 1e-3f)
            _rail.Add(_railRaw[^1]);
        // Rounded: each the average of those either side of it, as many each side as it has.
        _railRaw.Clear();
        _railRaw.AddRange(_rail);
        var reach = (int)MathF.Round(Tuning.WalkRound / RailStep);
        for (var i = 1; i < _rail.Count - 1; i++)
        {
            var k = Math.Min(reach, Math.Min(i, _rail.Count - 1 - i));
            var sum = Vector3.Zero;
            for (var j = i - k; j <= i + k; j++)
                sum += _railRaw[j];
            _rail[i] = sum / (2 * k + 1);
        }
        return (_rail.Count - 1) * RailStep;
    }

    /// <summary>The rail's point <paramref name="along"/> its way.</summary>
    private Vector3 RailAt(float along)
    {
        if (_rail.Count < 2)
            return _rail.Count == 1 ? _rail[0] : Vector3.Zero;
        var at = Math.Clamp(along / RailStep, 0, _rail.Count - 1);
        var i = Math.Min((int)at, _rail.Count - 2);
        return Vector3.Lerp(_rail[i], _rail[i + 1], at - i);
    }

    /// <summary>
    /// How far along the rail a walker at <paramref name="at"/> is: where it's nearest them across the ground, of its stretch from a unit
    /// before <paramref name="from"/> to four after, and never back.
    /// </summary>
    private float Measure(Vector3 at, float from)
    {
        var (first, last) = (Math.Max(0, (int)((from - 1) / RailStep)), Math.Min(_rail.Count - 2, (int)((from + 4) / RailStep)));
        var (best, nearest) = (from, float.MaxValue);
        var p = new Vector2(at.X, at.Z);
        for (var i = first; i <= last; i++)
        {
            var (a, b) = (new Vector2(_rail[i].X, _rail[i].Z), new Vector2(_rail[i + 1].X, _rail[i + 1].Z));
            var ab = b - a;
            var s = ab.LengthSquared() < 1e-8f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
            var d = Vector2.DistanceSquared(a + ab * s, p);
            if (d < nearest)
                (best, nearest) = ((i + s) * RailStep, d);
        }
        return MathF.Max(best, from);
    }

    /// <summary>
    /// An AI's walk's shot this tick (<see cref="PlanWalk"/>), riding its rail (<see cref="_rail"/>): how far along it the walker is is
    /// measured from the point that follows them (whose spring takes out their stride), and the shot goes on along it tracking that, its
    /// place and speed a critically damped pair
    /// (<see cref="CameraTuning.WalkSmoothing"/>) that doesn't trail a steady walker, never back. Its eye is moved by the walk's share of how far that point of the rail is
    /// from where it ends, aimed along the rail ahead of it and coming onto what the end's aimed at as it nears it. So the view turns after
    /// the walker as they come, moving only a little, and the stride's unevenness, a body knocked aside and a corner don't shake it.
    /// Stopped, it's the end framed again round where they stand.
    /// </summary>
    private Shot WalkShot(Combatant who)
    {
        var t = Tuning;
        var w = _walk!.Value;
        if (w.Arrived)
        {
            var still = Shot.Of(w.EyeEnd, w.AimEnd, w.Last?.Yaw ?? _from);
            _walk = w with { Last = still };
            return still;
        }
        var measured = Measure(_followOf == who ? _follow.At : _scene.Centre(who), w.Measured);
        // Tracked as a critically damped pair, place and speed: the speed made up from what's left of the way to the walker, so a steady
        // walker isn't trailed, and their stride's bursts are smoothed away rather than passed on.
        var k = 1 / MathF.Max(t.WalkSmoothing, 1e-3f) / 60;
        var (gain, pull) = (2 * k - k * k, k * k);
        var guess = w.Along + w.AlongSpeed / 60;
        var left = measured - guess;
        var (along, speed) = (guess + gain * left, MathF.Max(0, w.AlongSpeed + pull * 60 * left));
        along = Math.Clamp(along, w.Along, w.Length);
        var pace = speed;
        var at = RailAt(along);
        var progress = w.Length < 1e-3f ? 1 : along / w.Length;
        var eased = progress * progress * (3 - 2 * progress);
        var eye = w.EyeEnd + w.Share * (at - w.To);
        var lead = RailAt(along + MathF.Min(t.AimLead * (w.Length - along), t.AimLeadMost));
        var aim = Vector3.Lerp(lead, at + (w.AimEnd - w.To), eased);
        var shot = Shot.Of(eye, aim, w.Last?.Yaw ?? _from);
        _walk = w with { Along = along, AlongSpeed = speed, Measured = measured, MeasuredSpeed = pace, Last = shot };
        var intent = Intent;
        for (var i = 0; i < _rail.Count; i += 4)
            intent.Rail.Add(_rail[i]);
        intent.Rail.Add(_rail[^1]);
        intent.EyeRail = (w.EyeEnd + w.Share * (_rail[0] - w.To), w.EyeEnd);
        intent.Along = along;
        return shot;
    }

    /// <summary>Adds a combatant to those an action acts on, if it's on the field and not already one.</summary>
    private void Acted(Combatant? c)
    {
        if (c is not null && !_scene.Freed(c) && c != _scene.Current && !_acted.Contains(c))
            _acted.Add(c);
    }

    /// <summary>Whom the original's camera follows, if it's on the field.</summary>
    private Combatant? Owner() => _scene.CameraOwner is { } o && !_scene.Freed(o) ? o : null;

    // ---------------------------------------------------------------------------------------------------------------------------
    // An arrow, a throw or a spell at someone further off: the loosing, then the hit

    /// <summary>
    /// An arrow, a throw, a flask or a spell at someone further than <see cref="CameraTuning.ReleaseDistance"/>, or a spell at an ally
    /// however near, from its facing to its result (states 6, 2, 3 and 4): two shots, each cut to. The loosing: low over the shoulder of the one who acts, near them, at whom
    /// they act on. The hit, once the missile has flown <see cref="CameraTuning.HitAt"/> of its way, or
    /// <see cref="CameraTuning.ReleaseBeat"/> ticks after the spell's visual starts at the caster's strike, but not before the loosing has
    /// been shown <see cref="CameraTuning.LoosingTicks"/>; or as the blow lands: the target close, three-quarters on from the vantage's
    /// side (<see cref="HitShot"/>). Null for anything else; a spell's next target is a volley of its own.
    /// </summary>
    private Shot? Volley(Combatant current)
    {
        var t = Tuning;
        var facts = _scene.Facts;
        if (_scene.State is not (CombatState.Facing or CombatState.Action or CombatState.Cast or CombatState.Result) || facts.Target is not { } target)
        {
            _volley = null;
            return null;
        }
        if (target == current)
        {
            // A spell that takes others lands on its caster too: that's shown close as well, cut to straight away, as it has no loosing.
            if (_volley is { } own && own.Who == current && own.At == current)
            {
                Kind = "the hit, at the target";
                return own.Shot;
            }
            if (facts.SelectedA != 0 || !facts.TargetInB || facts.SpellTargets <= 1)
            {
                _volley = null;
                return null;
            }
            _volley = (current, current, true, 0, 0, HitShot(current, current));
            _cutNow = true;
            Kind = "the hit, at the target";
            return _volley.Value.Shot;
        }
        if (_volley is not { } v || v.Who != current || v.At != target)
        {
            // A new one only for a target selected: in a walk's facing the target is the last action's.
            var spell = facts.SelectedA == 0 && facts.TargetInB;
            var missile = facts.TargetInA && (facts.Flight is not null || facts.Flask || facts.Ranged);
            // A spell at an ally is shown landing on them however near, as a buff's or a heal's effect is the point of it.
            var ally = spell && _scene.Hostile(target) == _scene.Hostile(current);
            if (_scene.Freed(target) || !spell && !missile || !ally && Flat(_scene.Centre(target) - _scene.Centre(current)) <= t.ReleaseDistance)
            {
                _volley = null;
                return null;
            }
            _volley = (current, target, false, 0, 0, Loosing(current, target));
            _cutNow = true;
        }
        var (who, at, hit, loosed, shown, shot) = _volley.Value;
        if (!hit)
        {
            shown++;
            if (facts.CastLoosed)
                loosed++;
            var flown = facts.Flight is { Length: > 0 } f && Vector3.Distance(f.Position, f.From) >= f.Length * t.HitAt;
            if ((flown || loosed > t.ReleaseBeat) && shown >= t.LoosingTicks || _scene.State == CombatState.Result)
            {
                shot = HitShot(current, target);
                hit = true;
                _cutNow = true;
            }
            _volley = (who, at, hit, loosed, shown, shot);
        }
        Kind = hit ? "the hit, at the target" : "loosing, over the shoulder";
        return shot;
    }

    /// <summary>The loosing: low over the shoulder of <paramref name="who"/>, near them, at <paramref name="at"/>, from the vantage's side.</summary>
    private Shot Loosing(Combatant who, Combatant at)
    {
        var t = Tuning;
        var (me, there) = (_scene.Centre(who), _scene.Centre(at));
        NewSubjects();
        _subjects.Add(_scene.Sphere(who));
        _subjects.Add(_scene.Sphere(at) with { Weight = t.ClusterWeight });
        _pairs.Clear();
        AddPair(who, at);
        var behind = Look(me, there);
        return _finder!.Best(_subjects, behind, behind, _pairs, Lead(me, there), t.ShoulderDistance)?.Shot
            ?? _finder.Framed(_subjects, behind, Rad(t.PreferredPitch), t.ShoulderDistance, Lead(me, there));
    }

    /// <summary>
    /// The hit: the target alone, close, looking along the way the missile came turned <see cref="CameraTuning.HitTurn"/> toward the
    /// vantage's side, so it's seen three-quarters on and the missile comes in from the side the loosing was shot from. A spell's other
    /// targets are left out, so each it lands on is shown close in its turn. On the caster themself, from the vantage.
    /// </summary>
    private Shot HitShot(Combatant who, Combatant at)
    {
        var t = Tuning;
        var there = _scene.Centre(at);
        NewSubjects();
        _subjects.Add(_scene.Sphere(at));
        if (who == at)
        {
            var from = _vantage is { } toward ? MathF.Atan2(toward.X - there.X, toward.Y - there.Z) : _from;
            return _finder!.Best(_subjects, from, from)?.Shot ?? _finder.Framed(_subjects, from, Rad(t.PreferredPitch));
        }
        var along = Look(_scene.Centre(who), there);
        var turn = Rad(t.HitTurn);
        var yaw = along + turn;
        if (_vantage is { } v)
        {
            // The eye on the vantage's side of the line the missile flew.
            var toward = new Vector2(v.X - there.X, v.Y - there.Z);
            yaw = Toward(along + turn) >= Toward(along - turn) ? along + turn : along - turn;

            float Toward(float y) => Vector2.Dot(new Vector2(MathF.Sin(y), MathF.Cos(y)), toward);
        }
        yaw = Shot.Wrap(yaw);
        return _finder!.Best(_subjects, yaw, yaw)?.Shot ?? _finder.Framed(_subjects, yaw, Rad(t.PreferredPitch));
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // The vantage: the side every shot is taken from

    /// <summary>
    /// Places the battle's vantage (<see cref="Vantage.Place"/>) as both sides are first on the field, again as a reserve comes onto it,
    /// and again once the line between the party's middle and the enemies' has turned <see cref="CameraTuning.VantageTurn"/> from the one
    /// it was placed for.
    /// </summary>
    private void PlaceVantage()
    {
        var (party, enemies) = (Vector2.Zero, Vector2.Zero);
        var (np, ne) = (0, 0);
        foreach (var c in _scene.Bodies)
        {
            if (c.Freed || c.Dead || !c.Active)
                continue;
            var at = new Vector2(c.Centre.X, c.Centre.Z);
            if (c.Hostile)
            {
                enemies += at;
                ne++;
            }
            else
            {
                party += at;
                np++;
            }
        }
        var was = _counts;
        _counts = (np, ne);
        if (np == 0 || ne == 0)
            return;
        var t = Tuning;
        var line = enemies / ne - party / np;
        var length = line.Length();
        if (length < 1e-3f)
            return;
        line /= length;
        var middle = (party / np + enemies / ne) / 2;
        // As it was placed: no reserve come on, the line not turned far, and the fight not moved off or drawn apart or together by more
        // than half the vantage's reach then (on the battle's first tick the party may still stand where trek left it).
        var drift = MathF.Max(t.VantageNearest, t.VantageReach * _vantageFor.Length) / 2;
        if (_vantage is not null && np <= was.Party && ne <= was.Enemies && Vector2.Dot(line, _vantageLine) >= MathF.Cos(Rad(t.VantageTurn))
            && Vector2.Distance(middle, _vantageFor.Middle) <= drift && MathF.Abs(length - _vantageFor.Length) <= drift)
            return;
        if (Vantage.Place(party / np, enemies / ne, _finder!.Field, _vantageSide, t) is not var (point, side))
            return;
        (_vantage, _vantageSide, _vantageLine, _vantageFor) = (point, side, line, (middle, length));
    }

    /// <summary>Adds the side two who deal with each other should be framed on, a party member and their enemy, the way the vantage sees them.</summary>
    private void AddPair(Combatant a, Combatant b)
    {
        if (_scene.Hostile(a) != _scene.Hostile(b) && _vantage is { } v && Vantage.Side(_scene.Centre(a), _scene.Centre(b), v) is { } side)
            _pairs.Add(side);
    }

    /// <summary>
    /// Where a body walks to: the last corner it's queued, or with none left the one it's heading for, on the ground; null if it isn't
    /// walking.
    /// </summary>
    private Vector3? Destination(Combatant c)
    {
        var facts = _scene.Facts;
        Vector2 goal;
        if (facts.Queued.Count > 0)
            goal = facts.Queued[^1];
        else if (facts.Moving)
            goal = facts.Goal;
        else
            return null;
        var y = _scene.Centre(c).Y;
        var radius = _scene.Radius(c);
        return new Vector3(goal.X, Height(goal.X, goal.Y, y - radius) + radius, goal.Y);
    }

    /// <summary>
    /// The yaw of the next player's turn's likely shot: over that character's shoulder at its nearest enemy. With no player's turn left in
    /// the round, the way from the party's middle to the enemies'.
    /// </summary>
    private float NextYaw()
    {
        foreach (var next in _scene.Facts.Upcoming)
        {
            if (!_scene.Controlled(next) && !_scene.Freed(next) && !_scene.Dead(next) && Nearest(next) is { } enemy)
                return Look(_scene.Centre(next), _scene.Centre(enemy));
        }
        var (party, enemies) = (Vector3.Zero, Vector3.Zero);
        var (np, ne) = (0, 0);
        foreach (var c in _scene.Bodies)
        {
            if (c.Freed || c.Dead)
                continue;
            if (c.Hostile)
            {
                enemies += c.Centre;
                ne++;
            }
            else
            {
                party += c.Centre;
                np++;
            }
        }
        return np > 0 && ne > 0 ? Look(party / np, enemies / ne) : _from;
    }

    /// <summary>The nearest of a combatant's enemies, alive and on the field.</summary>
    private Combatant? Nearest(Combatant of)
    {
        Combatant? nearest = null;
        var best = float.MaxValue;
        var (hostile, at) = (_scene.Hostile(of), _scene.Centre(of));
        foreach (var c in _scene.Bodies)
        {
            if (c.Hostile == hostile || c.Freed || c.Dead || !c.Active)
                continue;
            var d = Vector3.DistanceSquared(c.Centre, at);
            if (d < best)
            {
                best = d;
                nearest = c.Who;
            }
        }
        return nearest;
    }

    /// <summary>The yaw of a shot looking from <paramref name="from"/> toward <paramref name="to"/> across the ground.</summary>
    private static float Look(Vector3 from, Vector3 to)
    {
        var away = from - to;
        return MathF.Atan2(away.X, away.Z);
    }

    /// <summary>A way's length across the ground.</summary>
    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    // ---------------------------------------------------------------------------------------------------------------------------
    // Aiming

    /// <summary>An aim's shot (<see cref="ShotFinder.Aimed"/>), from the yaw the view had as the aim began.</summary>
    private Shot Aimed(Combatant c, (CombatState State, Combatant Who, float Yaw)? aimed, Vector3 reticle, float radius, float pitch) =>
        _finder!.Aimed(reticle, radius, AimYaw(c, aimed), pitch);

    /// <summary>The yaw the view had as the aim began, now the aim's.</summary>
    private float AimYaw(Combatant c, (CombatState State, Combatant Who, float Yaw)? aimed)
    {
        var yaw = aimed is { } a && a.State == _scene.State && a.Who == c ? a.Yaw : _from;
        _aiming = (_scene.State, c, yaw);
        return yaw;
    }

    /// <summary>
    /// An aim at a blow (state 1, not a ranged weapon's): the shot the turn was settling on, or the one shown, is kept while it holds the
    /// attacker and the body under the cursor and hides neither, as the player's turn's over the shoulder at it does; else over the
    /// attacker's shoulder at that body, found once for it.
    /// </summary>
    private Shot BlowAim(Combatant current, Shot originalShot)
    {
        var t = Tuning;
        _aiming = (_scene.State, current, _from);
        NewSubjects();
        _subjects.Add(_scene.Sphere(current));
        if (Owner() is not { } cursor || cursor == current)
        {
            _blowAim = null;
            Kind = "aiming a blow";
            return _motion?.Current ?? originalShot;
        }
        _subjects.Add(_scene.Sphere(cursor));
        if (_blowAim is { } held && held.Cursor == cursor)
        {
            Kind = "aiming a blow, the shot held";
            return held.Shot;
        }
        // The shot the turn was settling on, else the one shown.
        var finder = _finder!;
        foreach (var kept in (Shot?[])[_motion is null ? null : Goal, _motion?.Current])
        {
            if (kept is { } k && _subjects.All(s => ShotFraming.Holds(k, s with { Radius = 0 }, t.Lens) && !finder.Sight.Hidden(k.Eye, s)))
            {
                _blowAim = (cursor, k);
                Kind = "aiming a blow, the shot kept";
                return k;
            }
        }
        var (me, there) = (_scene.Centre(current), _scene.Centre(cursor));
        _pairs.Clear();
        AddPair(current, cursor);
        var behind = Look(me, there);
        var shot = finder.Best(_subjects, _from, behind, _pairs, Lead(me, there), t.ShoulderDistance, t.ShoulderYaws)?.Shot
            ?? finder.Framed(_subjects, behind, Rad(t.PreferredPitch), t.ShoulderDistance, Lead(me, there));
        _blowAim = (cursor, shot);
        Kind = "aiming a blow, over the shoulder";
        return shot;
    }

    /// <summary>A shot <paramref name="s"/> of the way from one to another: the aim along a line, the turn the short way round, the distance by proportion.</summary>
    private static Shot Blend(Shot from, Shot to, float s) =>
        new(Vector3.Lerp(from.Aim, to.Aim, s), Shot.Wrap(from.Yaw + Shot.Wrap(to.Yaw - from.Yaw) * s), from.Pitch + (to.Pitch - from.Pitch) * s,
            MathF.Exp(MathF.Log(from.Distance) + (MathF.Log(to.Distance) - MathF.Log(from.Distance)) * s));

    /// <summary>How much wider than its radius the runtime draws an aim's marker (<c>CombatNumbersView</c>).</summary>
    private const float MarkerRim = 0.2f;

    /// <summary>
    /// An area placed (state 11): from above, its centre in the middle of the screen, as near as holds the ring drawn round it with its
    /// near side down to the conversation box's top (<see cref="ShotFinder.Placed"/>), so the ring is as big as the screen allows
    /// whatever its size.
    /// </summary>
    private Shot AreaShot(Combatant c, (CombatState State, Combatant Who, float Yaw)? aimed, AimMarker marker)
    {
        var t = Tuning;
        var centre = Ground(marker.Centre, c);
        var yaw = AimYaw(c, aimed);
        var pitch = Rad(t.TopDownPitch);
        NewSubjects();
        const int points = 16;
        for (var i = 0; i < points; i++)
        {
            var (sin, cos) = MathF.SinCos(MathF.Tau * i / points);
            _subjects.Add(new ShotSubject(Ground(marker.Centre + new Vector2(cos, sin) * (marker.Radius + MarkerRim), c)));
        }
        return _finder!.Placed(centre, _subjects, yaw, pitch);
    }

    /// <summary>
    /// A spell aimed at targets (state 10): low, from the caster's way, at every target selected and the cursor's, and some of the
    /// spell's reach where it can: the point of its ring beyond the targets, the nearest of it to them
    /// (<see cref="CameraTuning.ReachWeight"/>), which a low shot sees just above them. The caster needn't be in it: the line in the box
    /// says who casts. Found again as the cursor or the selection changes.
    /// </summary>
    private Shot SpellAim(Combatant caster, AimMarker reach)
    {
        var t = Tuning;
        NewSubjects();
        _acted.Clear();
        foreach (var c in _scene.Facts.ChosenB)
            Acted(c);
        var cursor = Owner() is { } o && o != caster ? o : null;
        if (cursor is not null && !_acted.Contains(cursor))
            _acted.Add(cursor);
        var me = _scene.Centre(caster);
        var middle = me;
        if (_acted.Count == 0)
        {
            // At the caster themselves.
            _subjects.Add(_scene.Sphere(caster));
        }
        else
        {
            middle = Vector3.Zero;
            foreach (var c in _acted)
            {
                _subjects.Add(_scene.Sphere(c));
                middle += _scene.Centre(c);
            }
            middle /= _acted.Count;
        }
        _pairs.Clear();
        if (cursor is not null)
            AddPair(caster, cursor);
        var targets = _subjects.Count;
        // Its ring's nearest point to the targets: from its centre through their middle.
        var centre = _scene.Origin + new Vector3(reach.Centre.X, 0, reach.Centre.Y);
        var way = new Vector2(middle.X - centre.X, middle.Z - centre.Z);
        if (way.LengthSquared() < 1e-4f)
            way = -(_motion?.Current.LookXZ ?? Vector2.UnitY);
        var edge = reach.Centre + Vector2.Normalize(way) * reach.Radius;
        var ring = new ShotSubject(Ground(edge, caster) + new Vector3(0, 0.5f, 0), 0, t.ReachWeight);
        var behind = _acted.Count > 0 ? Look(me, middle) : _from;
        _frameBy = (null, null);
        // The reach is shown only where it doesn't draw the shot far back from the targets: a wide spell's ring is far beyond them.
        var pitch = Rad(t.PreferredPitch);
        var alone = _finder!.Framed(_subjects, behind, pitch, float.MaxValue).Distance;
        var withRing = _finder.Framed([.. _subjects, ring], behind, pitch, float.MaxValue).Distance;
        var shown = withRing <= MathF.Max(alone, t.MinDistance) * t.ReachStretch;
        if (shown)
            _subjects.Add(ring);
        var key = (cursor, _scene.Facts.SelectedB, shown);
        Frame(behind, force: _spellAim != key);
        _spellAim = key;
        var shot = FramedNow(behind);
        // Where the reach can't be held with the targets after all, the targets alone.
        if (shown && _subjects.Take(targets).Any(s => !ShotFraming.Holds(shot, s with { Radius = 0 }, t.Lens)))
        {
            _subjects.RemoveAt(_subjects.Count - 1);
            shot = _finder.Framed(_subjects, shot.Yaw, shot.Pitch);
        }
        Kind = shown ? "aiming a spell, its targets and some of its reach" : "aiming a spell, its targets";
        _aiming = (_scene.State, caster, behind);
        return shot;
    }

    /// <summary>What the player aims at: the body the cursor is on, or the caster's own.</summary>
    private Vector3 Reticle(Combatant current) => _scene.Centre(Owner() ?? current);

    /// <summary>A point of the grid on the ground, at the caster's height where none is found.</summary>
    private Vector3 Ground(Vector2 cell, Combatant c)
    {
        var at = _scene.Origin + new Vector3(cell.X, 0, cell.Y);
        return new Vector3(at.X, Height(at.X, at.Z, _scene.Centre(c).Y), at.Z);
    }

    private float Height(float x, float z, float fallback) => _finder?.Sight.GroundAt(x, z, fallback + 30) ?? fallback;

    private static float Rad(float degrees) => degrees * MathF.PI / 180;

    private static float Deg(float radians) => radians * 180 / MathF.PI;
}
