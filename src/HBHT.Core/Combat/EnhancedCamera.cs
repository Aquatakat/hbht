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

    /// <summary>What the caps are times, away from the player's hand and an aim: lower as the player walks, higher as the character nears the edge.</summary>
    private float _pace = 1;

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

    /// <summary>How far the framing was eased toward the one it eases to the last tick, radians: its speed, whose change is capped.</summary>
    private float _easing;

    /// <summary>How far the shot was turned by itself over the shoulder or on its leash the last tick, radians, the two together.</summary>
    private float _lastTurn;

    /// <summary>An AI's walk as it's shown (<see cref="WalkShot"/>), from its start.</summary>
    private Walk? _walk;

    /// <summary>The walk's shot last tick and this, which the camera is carried along between.</summary>
    private (Shot From, Shot To)? _walkMoved;

    /// <summary>The subjects of a walk's end and of its start, as it's planned.</summary>
    private readonly List<ShotSubject> _ends = [];
    private readonly List<ShotSubject> _starts = [];

    /// <summary>
    /// An AI's walk: who walks, from where (the point that follows them) to where, the eye the shot ends from and what it ends aimed at,
    /// the share of the walk the eye moves with (<see cref="CameraTuning.WalkPan"/>), how far along they are, its shot last tick, and
    /// whether they've stopped, its end framed again round where they stand.
    /// </summary>
    private readonly record struct Walk(Combatant Who, Vector3 From, Vector3 To, Vector3 EyeEnd, Vector3 AimEnd, float Share, float Progress, Shot? Last,
        bool Arrived = false);

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

    /// <summary>A new framing over the shoulder being eased onto (<see cref="CameraTuning.FramingTurn"/>), rather than stepped to.</summary>
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

    public CameraTuning Tuning { get; set; } = CameraTuning.Default;

    /// <summary>The motion its shot moves by, once it has started.</summary>
    public CappedMotion? Motion => _motion;

    /// <summary>What finds its shots, once it has started.</summary>
    public ShotFinder? Finder => _finder;

    /// <summary>The shot it moved toward this tick.</summary>
    public Shot Goal { get; private set; }

    /// <summary>The point it follows whoever's turn it is by (<see cref="CameraTuning.FollowSmoothing"/>), once it has one.</summary>
    public Vector3? FollowAt => _followOf is null ? null : _follow.At;

    /// <summary>Whether it's panning, into an aim, about one or back from one (<see cref="CameraTuning.PanTicks"/>).</summary>
    public bool Panning => _pan is not null;

    /// <summary>The player turned the camera in this turn: it shows the original's until they aim or the turn ends.</summary>
    public bool Released { get; private set; }

    /// <summary>
    /// Whether its caps were raised this tick: the player's hand on it, an aim following the reticle, or the view catching up with the
    /// character out of it.
    /// </summary>
    public bool Quick { get; private set; }

    /// <summary>Whether the player walks the character this tick: the aim's and distance's caps are lowered (<see cref="CameraTuning.WalkPace"/>), and it never cuts.</summary>
    public bool Walking { get; private set; }

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
        _pace = 1;
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
        _easing = 0;
        _lastTurn = 0;
        _framingTo = null;
        _blowAim = null;
    }

    /// <summary>A tick, after the original's camera has stepped.</summary>
    /// <param name="original">The original's camera, which goes on underneath.</param>
    /// <param name="glide">The bodies the original's glide runs between, while one is on.</param>
    internal void Step(Battle battle, FollowCamera original, (Body From, Body To)? glide)
    {
        var t = Tuning;
        if (!ReferenceEquals(_world, battle.World) || _finder is null)
        {
            _world = battle.World;
            _finder = new ShotFinder(new SightLines(_world), t);
        }
        _finder.Tuning = t;
        _finder.Field = battle.Arena is null ? null
            : (new Vector2(battle.Origin.X, battle.Origin.Z), new Vector2(battle.Origin.X + battle.Grid.Width, battle.Origin.Z + battle.Grid.Depth));
        var sight = _finder.Sight;
        sight.Radius = t.SightRadius;
        sight.BodyScale = t.BodyOcclusion;
        sight.Bodies.Clear();
        foreach (var c in battle.Combatants)
        {
            if (!c.Freed)
                sight.Bodies.Add(Sphere(c.Body));
        }
        PlaceVantage(battle);
        // The point the shot follows whoever's turn it is by, drawn after their body, or put on it where it was put somewhere.
        if (battle.Current is { } followed)
        {
            var body = followed.Body.Sphere.Position;
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
        var fresh = battle.Current != _turnOf;
        if (fresh)
        {
            _turnOf = battle.Current;
            TurnEnded();
        }
        // The player's turn begun, or come back to from a menu or an aim: the one tick of it that may cut.
        var entering = battle.State == CombatState.Turn && (fresh || _stateWas != CombatState.Turn);
        _stateWas = battle.State;
        // Aiming takes the camera back from the player's hand.
        if (battle.State is CombatState.TargetAttack or CombatState.TargetSpell or CombatState.TargetThrown or CombatState.PlaceArea)
            Released = false;

        var aimed = _aiming;
        _aiming = null;
        _cutNow = false;
        _turnBy = 0;
        _tracking = false;
        _walkMoved = null;
        var goal = Decide(battle, originalShot, glide, aimed);
        // The player's own camera is the original's as it is; the director's shots keep their distance.
        if (!Released)
            goal = goal with { Pitch = Math.Clamp(goal.Pitch, Rad(-60), Rad(89)), Distance = MathF.Max(goal.Distance, t.NearestDistance) };
        Goal = goal;

        var caps = t.Caps;
        Walking = !Released && _aiming is null && battle.State == CombatState.Turn && battle.Current is { Controlled: false };
        // The character going out from the middle of the shot shown in their turn: the caps go up as they near its edge, all the way
        // once they're out of it, so the view keeps them rather than leaving them off the screen.
        var edge = 0f;
        if (!Released && battle.State == CombatState.Turn && battle.Current is { Controlled: false } me && _motion is { } shown)
        {
            var (x, y, depth) = ShotFraming.OnScreen(shown.Current, me.Body.Sphere.Position, t.Lens);
            edge = depth <= 0 ? float.PositiveInfinity : MathF.Max(MathF.Abs(x), MathF.Abs(y));
        }
        var lost = edge > 1;
        var calm = Walking ? t.WalkPace : 1;
        var wanted = calm + (MathF.Max(t.LostPace, calm) - calm) * Math.Clamp((edge - t.CatchUpFrom) / (1 - t.CatchUpFrom), 0, 1);
        // Up at once, down over CatchUpTicks: caps that fell under a quick move would let it overshoot.
        _pace = wanted >= _pace ? wanted : MathF.Max(wanted, _pace - MathF.Abs(t.LostPace - t.WalkPace) / Math.Max(1, t.CatchUpTicks));
        var pace = _pace;
        Quick = Released || _aiming is not null || pace > 1;
        if (_motion is null)
        {
            // The battle's first shot is cut to: there's nothing yet to move from.
            _motion = new CappedMotion(goal, caps);
            Camera.Cut();
        }
        var hand = MathF.Sqrt(t.HandAcceleration);
        var settle = MathF.Sqrt(pace);
        // The turn isn't slowed for the walk: with the shot carried along, a turn slower than its place trails in after it.
        var turnPace = MathF.Max(pace, 1);
        _motion.Caps = Released
            ? new MotionCaps(caps.Aim * t.HandAcceleration, caps.Turn * t.HandAcceleration, caps.Zoom * t.HandAcceleration, caps.Settle * hand,
                caps.TurnSettle * hand, caps.ZoomSettle * hand)
            : _aiming is not null ? caps with { Aim = caps.Aim * t.AimingAcceleration, Settle = caps.Settle * MathF.Sqrt(t.AimingAcceleration) }
            : new MotionCaps(caps.Aim * pace, caps.Turn * turnPace, caps.Zoom * pace, caps.Settle * settle, caps.TurnSettle * MathF.Sqrt(turnPace),
                caps.ZoomSettle * settle);
        _motion.GoalJump = t.GoalJump;
        // Only the player's own hand is followed at its speed; the director's shots are trailed, so a goal that stops isn't overshot.
        _motion.MostFollowed = Released ? (t.FollowSpeed, Rad(t.FollowTurn)) : (0, 0);
        _motion.NearestDistance = MathF.Min(t.NearestDistance, goal.Distance);
        // In the player's turn, and in an action whose body walks, the shot moves with the walker, as a third person's camera does: only
        // the change of framing is capped, so the view never trails one who runs, and stops as they stop. It's carried by the point that
        // follows them, so their stride doesn't jolt it.
        if ((Walking || _tracking) && _followOf is { } walker && walker == battle.Current)
        {
            _motion.Carry(_follow.Moved);
            // And turned with the way to its target as they walk round it, so the angle doesn't trail them either.
            if (_turnBy != 0)
                _motion.Turn(_turnBy);
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
        var cut = _cutNow || mayCut && (travel > t.CutDistance || turn > Rad(t.CutTurn) || tilt > Rad(t.CutPitch))
            || entering && !Released && (lost || travel > t.EnterCutDistance || turn > Rad(t.EnterCutTurn));
        // Into an aim, about one, or back from one to the turn, the move is a pan, not a cut, so the player sees where they're aiming. A
        // pan carries on through its state, its end following the goal; a new state is judged afresh.
        var (aimingNow, wasAiming) = (_aiming is not null, aimed is not null);
        if (Released || _cutNow || _pan is { } p && p.State != battle.State)
            _pan = null;
        if (_pan is null && !Released && !_cutNow
            && (aimingNow && (wasAiming ? cut : travel > 1 || turn > Rad(10) || tilt > Rad(10)) || wasAiming && entering && cut))
            _pan = (battle.State, now, 0, Math.Clamp((int)MathF.Round(t.PanTicks + travel * t.PanTicksPerUnit), t.PanTicks, Math.Max(t.PanTicks, t.PanMostTicks)));
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
        else
        {
            // A long way to go may be a swoop (TravelZoom): stood back by the way left, both ends in view, coming down as it arrives.
            _motion.Step(t.TravelZoom > 0 && travel * t.TravelZoom > goal.Distance ? goal with { Distance = travel * t.TravelZoom } : goal);
        }
        var shot = _motion.Current;
        var eye = shot.Eye;
        if (sight.GroundAt(eye.X, eye.Z, MathF.Max(eye.Y, shot.Aim.Y) + 30) is { } ground && eye.Y < ground + t.FloorClearance)
            eye.Y = ground + t.FloorClearance;
        Camera.PlaceAt(eye);
        Camera.AimAt(shot.Aim);
        // The look from the yaw, not the eye, so a shot from above still turns the stick by the screen's up.
        Camera.LookXZ = shot.LookXZ;
    }

    private Shot Decide(Battle battle, Shot originalShot, (Body From, Body To)? glide, (CombatState, Combatant, float)? aimed)
    {
        if (battle.Current is not { } current)
        {
            Kind = "the original's";
            return originalShot;
        }
        if (Released)
        {
            Kind = "the original's, the player's hand on it";
            return originalShot;
        }
        var acting = current.Controlled || battle.State is CombatState.Facing or CombatState.Action or CombatState.Cast or CombatState.Result
            or CombatState.Moving or CombatState.WaitIdle;
        if (!acting)
        {
            _plan = null;
            _volley = null;
            _walk = null;
        }
        if (battle.State != CombatState.Turn && battle.State != CombatState.TargetSpell)
            _framing = null;
        if (battle.State != CombatState.Turn)
        {
            _ahead = null;
            _behind = null;
            _framingTo = null;
        }
        if (battle.State != CombatState.TargetSpell)
            _spellAim = null;
        if (battle.State != CombatState.TargetAttack)
            _blowAim = null;
        if (acting)
            return ActionShot(battle, current, originalShot);
        var t = Tuning;
        switch (battle.State)
        {
            case CombatState.PlaceArea when current.CurrentSpell(battle) is { } source:
            {
                var level = Casting.Level(current.Sheet, source);
                var marker = battle.Aim ?? new AimMarker(battle.AreaAim, level, Spell: true);
                Kind = "placing an area, from above";
                return AreaShot(battle, current, aimed, marker);
            }
            case CombatState.TargetThrown:
                Kind = "aiming a throw";
                return Aimed(battle, current, aimed, Ground(battle, battle.ThrownAim, current), 1, Rad(t.AimPitch));
            case CombatState.TargetSpell when battle.Aim is { } reach:
                return SpellAim(battle, current, reach);
            case CombatState.TargetAttack when IsRanged(current):
                Kind = "aiming a ranged attack";
                return Aimed(battle, current, aimed, Reticle(battle, current), 1, Rad(t.AimPitch));
            case CombatState.TargetAttack:
                return BlowAim(battle, current, originalShot);
            case CombatState.Turn:
                return TurnShot(battle, current, originalShot);
            case CombatState.MenuSpells or CombatState.MenuItems or CombatState.TroubadourPrompt or CombatState.Pause
                or CombatState.PauseRequested when _motion is { } m:
                // The player choosing: the view holds where it is, rather than going to the original's close shot.
                Kind = "held while the player chooses";
                return m.Current;
        }
        _subjects.Clear();
        var owner = battle.CameraOwner is { Freed: false } o ? o : current;
        _subjects.Add(Sphere(owner.Body));
        if (owner != current && !current.Freed)
            _subjects.Add(Sphere(current.Body));
        if (glide is { } g)
        {
            _subjects.Add(Sphere(g.From));
            if (g.To != g.From)
                _subjects.Add(Sphere(g.To));
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
    /// they've done anything. With no enemy ahead, it trails them on a leash as a third person's camera does, its aim leading them the way
    /// they face (<see cref="CameraTuning.LookAhead"/>).
    /// </summary>
    private Shot TurnShot(Battle battle, Combatant current, Shot originalShot)
    {
        var t = Tuning;
        var me = current.Body.Sphere.Position;
        // What the shot is placed by: the point that follows them, so their stride doesn't jolt it.
        var anchor = _followOf == current ? _follow.At : me;
        var heading = current.Body.Heading;
        // The most the speed the shot's turned at by itself changes in a tick.
        var speedUp = Rad(t.TurnAcceleration) / 3600;
        _candidates.Clear();
        foreach (var c in battle.Combatants)
        {
            if (c.Hostile != current.Hostile && !c.Freed && !c.Sheet.IsDead && c.Active)
                _candidates.Add(new TargetCandidate(c, c.Body.Sphere.Position));
        }
        var opening = !_opened;
        if (!_opened)
        {
            _opened = true;
            _opening = (me, heading);
            _cone.Pick(me, heading, _candidates, t);
            if (Target is null && Nearest(battle, current) is { } nearest && Flat(nearest.Body.Sphere.Position - me) <= t.TargetRange)
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
        _subjects.Clear();
        _subjects.Add(Sphere(current.Body));
        if (Target is not { } target)
        {
            _framing = null;
            _framingTo = null;
            _behind = null;
            (_carried, _easing) = (_carried + _easing, 0);
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
            _turnBy = _lastTurn = _carried = Math.Clamp(leash, _carried - speedUp, _carried + speedUp);
            if (_turnBy != 0)
            {
                var (y, p, d) = _ahead.Value;
                _ahead = (Shot.Wrap(y + _turnBy), p, d);
            }
            var lead = Lead(anchor, anchor + new Vector3(heading.X, 0, heading.Y) * t.LookAhead);
            var follow = Ahead(lead);
            // A body or a wall hiding the character turns, raises or draws the view back, and it stays so rather than swinging back.
            if (++_sinceDecide >= t.DecideEvery)
            {
                _sinceDecide = 0;
                if (_finder!.HiddenWeight(follow, _subjects) > 0 && _finder.Clearing(follow, _subjects) is { } by)
                {
                    var cleared = _finder.Corrected(follow, by, _subjects);
                    _ahead = (cleared.Yaw, cleared.Pitch, cleared.Distance);
                    follow = Ahead(lead);
                }
            }
            Kind = "no enemy ahead, trailing them";
            return follow;
        }
        _ahead = null;
        _subjects.Add(Sphere(target.Body));
        // The target's pack: the shot leads toward the middle of them all, and holds them while they're near.
        var at = target.Body.Sphere.Position;
        var near = Flat(at - me) <= t.PackRange;
        var (pack, n) = (at, 1);
        foreach (var c in _candidates)
        {
            var other = (Combatant)c.Who;
            if (other == target || Flat(c.At - at) > t.ClusterRadius)
                continue;
            if (near)
                _subjects.Add(Sphere(other.Body) with { Weight = t.ClusterWeight });
            pack += c.At;
            n++;
        }
        _pairs.Clear();
        AddPair(current, target);
        var ahead = pack / n;
        // Orbiting the target as the character goes round it: the way over their shoulder at it turns the shot with it straight away
        // (Turn), up to CarryTurn a second, that speed changing no faster than TurnAcceleration; less near it, where the way means little
        // and swings wildly, not at all while it's being let go, and not by a new target's jump. What the shot isn't turned by goes into
        // its framing, the offset from the way, so it isn't caught up later: the view never turns hard to a target. The way to the target,
        // not to its pack's middle: walking straight at one at the pack's edge doesn't turn the view.
        var behind = Look(anchor, at);
        var (change, most) = _behind is { } b
            ? (Shot.Wrap(behind - b.Behind),
                b.Target != target || _cone.Losing ? 0 : Rad(t.CarryTurn) / 60 * Math.Clamp(Flat(at - anchor) / t.ShoulderNear, 0, 1))
            : (0, 0);
        var carried = Math.Clamp(Math.Clamp(change, -most, most), _carried - speedUp, _carried + speedUp);
        Absorb(change - carried);
        _frameBy = (Lead(anchor, ahead), t.ShoulderDistance);
        // Taken over the shoulder from the view the trailing left (it had no target), and eased round from there.
        var force = false;
        if (_framing is null && !opening && _motion is { } trailed)
        {
            var widest = Rad(t.ShoulderYaws.Select(MathF.Abs).DefaultIfEmpty().Max());
            _framing = (Math.Clamp(Shot.Wrap(trailed.Current.Yaw - behind), -widest, widest),
                Math.Clamp(trailed.Current.Pitch, Rad(t.Pitches.Min()), Rad(t.Pitches.Max())));
            force = true;
        }
        // Found again only where the character or the target is lost from it (a new target among them), or once it's taken over the
        // shoulder from the trailing, and eased onto, so the shot never steps as they walk.
        if (_framingTo is null || force)
        {
            var held = _framing;
            Frame(behind, force, judged: 2);
            if (held is { } h && _framing is { } found && found != h)
                (_framingTo, _framing) = (found, h);
        }
        // Eased onto: its turn's speed up to FramingTurn a second, changing no faster than TurnAcceleration and slowing in time to stop
        // on it, and the shot turned with it as the orbit turns it, so the view doesn't trail in after its goal. Its pitch eases at
        // FramingTurn.
        var eased = 0f;
        if (_framingTo is { } to && _framing is { } now)
        {
            var step = Rad(t.FramingTurn) / 60;
            var (offset, pitch) = (Shot.Wrap(to.Offset - now.Offset), to.Pitch - now.Pitch);
            var fastest = MathF.Min(step, Stopping(MathF.Abs(offset), speedUp));
            _easing = Math.Clamp(MathF.Sign(offset) * fastest, _easing - speedUp, _easing + speedUp);
            eased = MathF.Sign(_easing) == MathF.Sign(offset) && MathF.Abs(_easing) >= MathF.Abs(offset) ? offset : _easing;
            _framing = (Shot.Wrap(now.Offset + eased), now.Pitch + Math.Clamp(pitch, -step, step));
            // Arrived, what's left of its speed goes on in the orbit's, to slow under the cap.
            if (MathF.Abs(Shot.Wrap(to.Offset - _framing.Value.Offset)) < 1e-5f && MathF.Abs(pitch) <= step)
                (_framingTo, _carried, _easing) = (null, _carried + eased, 0);
        }
        else
        {
            _easing = 0;
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
        return FramedNow(behind);
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
                var shot = FramedNow(behind);
                if (!_subjects.Take(k).Any(s => !ShotFraming.Holds(shot, s with { Radius = 0 }, t.Lens) || finder.Sight.Hidden(shot.Eye, s)))
                    return;
            }
            else if (++_sinceDecide < t.DecideEvery && finder.HiddenWeight(FramedNow(behind), _subjects) == 0)
            {
                return;
            }
        }
        _sinceDecide = 0;
        var heldScore = _framing is not null && !force ? finder.Score(FramedNow(behind), _subjects, _from, behind, _pairs) : float.NegativeInfinity;
        if (finder.Best(_subjects, _from, behind, _pairs, _frameBy.Aim, _frameBy.Most, t.ShoulderYaws) is var (best, score)
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
    private static bool IsRanged(Combatant c) =>
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
    private Shot ActionShot(Battle battle, Combatant current, Shot originalShot)
    {
        if (Volley(battle, current) is { } volley)
            return volley;
        var ai = current.Controlled;
        _subjects.Clear();
        _subjects.Add(Sphere(current.Body));
        var to = Destination(current);
        if (to is { } d)
            _subjects.Add(new ShotSubject(d, current.Body.ScaledRadius));
        _acted.Clear();
        // One fleeing acts on no one, whoever it last had in mind.
        if (!current.Fleeing)
        {
            Acted(battle, ai ? current.Ai?.Target ?? -1 : battle.Target);
            foreach (var slot in battle.TargetsB.SelectedSlots)
                Acted(battle, slot);
        }
        foreach (var c in _acted)
            _subjects.Add(Sphere(c.Body));
        if (battle.CameraOwner is { Freed: false } owner && owner != current && !_acted.Contains(owner))
            _subjects.Add(Sphere(owner.Body));
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
                _walk = PlanWalk(battle, current, end);
            // Stopped, perhaps short of where it was going: its end framed again from the same way round where it stands, whom it acts
            // on held in it.
            if (to is null && _walk is { Arrived: false } stopped)
            {
                var planned = Shot.Of(stopped.EyeEnd, stopped.AimEnd);
                var framed = _finder!.Framed(_subjects, planned.Yaw, planned.Pitch);
                _walk = stopped with { To = current.Body.Sphere.Position, EyeEnd = framed.Eye, AimEnd = framed.Aim, Progress = 1, Last = null, Arrived = true };
            }
            var (was, walked) = (_walk!.Value.Last, WalkShot());
            if (was is { } from)
                _walkMoved = (from, walked);
            _plan = (current, walked);
            _planMissed = _subjects.Count(s => !ShotFraming.Holds(walked, s with { Radius = 0 }, screen));
            _sinceDecide = 0;
            Kind = (current.Fleeing ? "an AI's flight" : "an AI's walk") + (to is null ? ", ended" : ", turned after");
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
        if (_plan is null && battle.State == CombatState.AI)
        {
            Kind = "an AI's turn, thinking";
            return _motion?.Current ?? originalShot;
        }
        // Planned again, it keeps what the action has shown in view (where the body stood and was going), so the shot widens rather than swings.
        if (_plan is null)
            _shown.Clear();
        _shown.AddRange(_subjects.Take(to is null ? 1 : 2));
        if (_finder!.Planned([.. _subjects, .. _shown], _from, NextYaw(battle), _pairs) is not var (shot, _))
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
    private Walk PlanWalk(Battle battle, Combatant who, Vector3 to)
    {
        var t = Tuning;
        var finder = _finder!;
        var from = _follow.At;
        var way = to - from;
        var length = Flat(way);
        _ends.Clear();
        _starts.Clear();
        _ends.Add(new ShotSubject(to, who.Body.ScaledRadius));
        _starts.Add(new ShotSubject(from, who.Body.ScaledRadius));
        // After the walker and where it goes: whom it acts on and whom the original's camera follows.
        for (var i = 2; i < _subjects.Count; i++)
        {
            _ends.Add(_subjects[i]);
            _starts.Add(_subjects[i] with { Weight = t.ClusterWeight });
        }
        var ahead = length > 1e-3f ? MathF.Atan2(way.X, way.Z) : _from;
        var share = length > 1e-3f ? MathF.Min(t.WalkPan, t.WalkPanMost / length) : 0;
        var next = NextYaw(battle);
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
        return new Walk(who, from, to, chosen.Eye, chosen.Aim, share, 0, null);
    }

    /// <summary>
    /// An AI's walk's shot this tick (<see cref="PlanWalk"/>): the eye moved with the point that follows the walker by the walk's share of
    /// how far it is from where it ends, aimed at them, leading the way they go, and coming onto what the end's aimed at as they near it,
    /// so the view turns after them as they come, moving only a little, and settles on them and whom they act on as they arrive.
    /// </summary>
    private Shot WalkShot()
    {
        var w = _walk!.Value;
        var at = _follow.At;
        var length = Flat(w.To - w.From);
        var progress = MathF.Max(w.Progress, length < 1e-3f ? 1 : Math.Clamp(1 - Flat(w.To - at) / length, 0, 1));
        var eased = progress * progress * (3 - 2 * progress);
        var eye = w.EyeEnd + w.Share * (at - w.To);
        var aim = Vector3.Lerp(Lead(at, w.To), at + (w.AimEnd - w.To), eased);
        var shot = Shot.Of(eye, aim, w.Last?.Yaw ?? _from);
        _walk = w with { Progress = progress, Last = shot };
        return shot;
    }

    /// <summary>Adds the combatant in a slot to those an action acts on, if it's on the field and not already one.</summary>
    private void Acted(Battle battle, int slot)
    {
        if (slot >= 0 && slot < battle.Slots.Count && battle.Slots[slot] is { Freed: false } c && c != battle.Current && !_acted.Contains(c))
            _acted.Add(c);
    }

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
    private Shot? Volley(Battle battle, Combatant current)
    {
        var t = Tuning;
        if (battle.State is not (CombatState.Facing or CombatState.Action or CombatState.Cast or CombatState.Result)
            || battle.Target < 0 || battle.Target >= battle.Slots.Count || battle.Slots[battle.Target] is not { } target)
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
            if (battle.TargetsA.Selected != 0 || !battle.TargetsB.IsSelected(battle.Target) || battle.SpellTargetCount(current) <= 1)
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
            var spell = battle.TargetsA.Selected == 0 && battle.TargetsB.IsSelected(battle.Target);
            var missile = battle.TargetsA.IsSelected(battle.Target)
                && (battle.Flight is not null || current.Action == CombatAction.Flask || IsRanged(current));
            // A spell at an ally is shown landing on them however near, as a buff's or a heal's effect is the point of it.
            var ally = spell && target.Hostile == current.Hostile;
            if (target.Freed || !spell && !missile || !ally && Flat(target.Body.Sphere.Position - current.Body.Sphere.Position) <= t.ReleaseDistance)
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
            if (battle.CastLoosed)
                loosed++;
            var flown = battle.Flight is { Length: > 0 } f && Vector3.Distance(f.Position, f.From) >= f.Length * t.HitAt;
            if ((flown || loosed > t.ReleaseBeat) && shown >= t.LoosingTicks || battle.State == CombatState.Result)
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
        var (me, there) = (who.Body.Sphere.Position, at.Body.Sphere.Position);
        _subjects.Clear();
        _subjects.Add(Sphere(who.Body));
        _subjects.Add(Sphere(at.Body) with { Weight = t.ClusterWeight });
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
        var there = at.Body.Sphere.Position;
        _subjects.Clear();
        _subjects.Add(Sphere(at.Body));
        if (who == at)
        {
            var from = _vantage is { } toward ? MathF.Atan2(toward.X - there.X, toward.Y - there.Z) : _from;
            return _finder!.Best(_subjects, from, from)?.Shot ?? _finder.Framed(_subjects, from, Rad(t.PreferredPitch));
        }
        var along = Look(who.Body.Sphere.Position, there);
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
    private void PlaceVantage(Battle battle)
    {
        var (party, enemies) = (Vector2.Zero, Vector2.Zero);
        var (np, ne) = (0, 0);
        foreach (var c in battle.Combatants)
        {
            if (c.Freed || c.Sheet.IsDead || !c.Active)
                continue;
            var at = new Vector2(c.Body.Sphere.Position.X, c.Body.Sphere.Position.Z);
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
        if (a.Hostile != b.Hostile && _vantage is { } v && Vantage.Side(a.Body.Sphere.Position, b.Body.Sphere.Position, v) is { } side)
            _pairs.Add(side);
    }

    /// <summary>
    /// Where a body walks to: the last corner it's queued, or with none left the one it's heading for, on the ground; null if it isn't
    /// walking.
    /// </summary>
    private Vector3? Destination(Combatant c)
    {
        var body = c.Body;
        Vector2 goal;
        if (body.Queued.Count > 0)
            goal = body.Queued.Last().Goal;
        else if (body.Moving)
            goal = body.Goal;
        else
            return null;
        var y = body.Sphere.Position.Y;
        var radius = body.ScaledRadius;
        return new Vector3(goal.X, Height(goal.X, goal.Y, y - radius) + radius, goal.Y);
    }

    /// <summary>
    /// The yaw of the next player's turn's likely shot: over that character's shoulder at its nearest enemy. With no player's turn left in
    /// the round, the way from the party's middle to the enemies'.
    /// </summary>
    private float NextYaw(Battle battle)
    {
        var order = battle.Order;
        for (var i = order.Now + 1; i < order.Turns.Count; i++)
        {
            if (order.Turns[i] < battle.Slots.Count && battle.Slots[order.Turns[i]] is { Controlled: false, Freed: false } next
                && !next.Sheet.IsDead && Nearest(battle, next) is { } enemy)
                return Look(next.Body.Sphere.Position, enemy.Body.Sphere.Position);
        }
        var (party, enemies) = (Vector3.Zero, Vector3.Zero);
        var (np, ne) = (0, 0);
        foreach (var c in battle.Combatants)
        {
            if (c.Freed || c.Sheet.IsDead)
                continue;
            if (c.Hostile)
            {
                enemies += c.Body.Sphere.Position;
                ne++;
            }
            else
            {
                party += c.Body.Sphere.Position;
                np++;
            }
        }
        return np > 0 && ne > 0 ? Look(party / np, enemies / ne) : _from;
    }

    /// <summary>The nearest of a combatant's enemies, alive and on the field.</summary>
    private static Combatant? Nearest(Battle battle, Combatant of)
    {
        Combatant? nearest = null;
        var best = float.MaxValue;
        foreach (var c in battle.Combatants)
        {
            if (c.Hostile == of.Hostile || c.Freed || c.Sheet.IsDead || !c.Active)
                continue;
            var d = Vector3.DistanceSquared(c.Body.Sphere.Position, of.Body.Sphere.Position);
            if (d < best)
            {
                best = d;
                nearest = c;
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
    private Shot Aimed(Battle battle, Combatant c, (CombatState State, Combatant Who, float Yaw)? aimed, Vector3 reticle, float radius, float pitch) =>
        _finder!.Aimed(reticle, radius, AimYaw(battle, c, aimed), pitch);

    /// <summary>The yaw the view had as the aim began, now the aim's.</summary>
    private float AimYaw(Battle battle, Combatant c, (CombatState State, Combatant Who, float Yaw)? aimed)
    {
        var yaw = aimed is { } a && a.State == battle.State && a.Who == c ? a.Yaw : _from;
        _aiming = (battle.State, c, yaw);
        return yaw;
    }

    /// <summary>
    /// An aim at a blow (state 1, not a ranged weapon's): the shot the turn was settling on, or the one shown, is kept while it holds the
    /// attacker and the body under the cursor and hides neither, as the player's turn's over the shoulder at it does; else over the
    /// attacker's shoulder at that body, found once for it.
    /// </summary>
    private Shot BlowAim(Battle battle, Combatant current, Shot originalShot)
    {
        var t = Tuning;
        _aiming = (battle.State, current, _from);
        _subjects.Clear();
        _subjects.Add(Sphere(current.Body));
        if (battle.CameraOwner is not { Freed: false } cursor || cursor == current)
        {
            _blowAim = null;
            Kind = "aiming a blow";
            return _motion?.Current ?? originalShot;
        }
        _subjects.Add(Sphere(cursor.Body));
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
        var (me, there) = (current.Body.Sphere.Position, cursor.Body.Sphere.Position);
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
    private Shot AreaShot(Battle battle, Combatant c, (CombatState State, Combatant Who, float Yaw)? aimed, AimMarker marker)
    {
        var t = Tuning;
        var centre = Ground(battle, marker.Centre, c);
        var yaw = AimYaw(battle, c, aimed);
        var pitch = Rad(t.TopDownPitch);
        _subjects.Clear();
        const int points = 16;
        for (var i = 0; i < points; i++)
        {
            var (sin, cos) = MathF.SinCos(MathF.Tau * i / points);
            _subjects.Add(new ShotSubject(Ground(battle, marker.Centre + new Vector2(cos, sin) * (marker.Radius + MarkerRim), c)));
        }
        return _finder!.Placed(centre, _subjects, yaw, pitch);
    }

    /// <summary>
    /// A spell aimed at targets (state 10): low, from the caster's way, at every target selected and the cursor's, and some of the
    /// spell's reach where it can: the point of its ring beyond the targets, the nearest of it to them
    /// (<see cref="CameraTuning.ReachWeight"/>), which a low shot sees just above them. The caster needn't be in it: the line in the box
    /// says who casts. Found again as the cursor or the selection changes.
    /// </summary>
    private Shot SpellAim(Battle battle, Combatant caster, AimMarker reach)
    {
        var t = Tuning;
        _subjects.Clear();
        _acted.Clear();
        foreach (var slot in battle.TargetsB.SelectedSlots)
            Acted(battle, slot);
        var cursor = battle.CameraOwner is { Freed: false } o && o != caster ? o : null;
        if (cursor is not null && !_acted.Contains(cursor))
            _acted.Add(cursor);
        var me = caster.Body.Sphere.Position;
        var middle = me;
        if (_acted.Count == 0)
        {
            // At the caster themselves.
            _subjects.Add(Sphere(caster.Body));
        }
        else
        {
            middle = Vector3.Zero;
            foreach (var c in _acted)
            {
                _subjects.Add(Sphere(c.Body));
                middle += c.Body.Sphere.Position;
            }
            middle /= _acted.Count;
        }
        _pairs.Clear();
        if (cursor is not null)
            AddPair(caster, cursor);
        var targets = _subjects.Count;
        // Its ring's nearest point to the targets: from its centre through their middle.
        var centre = battle.Origin + new Vector3(reach.Centre.X, 0, reach.Centre.Y);
        var way = new Vector2(middle.X - centre.X, middle.Z - centre.Z);
        if (way.LengthSquared() < 1e-4f)
            way = -(_motion?.Current.LookXZ ?? Vector2.UnitY);
        var edge = reach.Centre + Vector2.Normalize(way) * reach.Radius;
        var ring = new ShotSubject(Ground(battle, edge, caster) + new Vector3(0, 0.5f, 0), 0, t.ReachWeight);
        var behind = _acted.Count > 0 ? Look(me, middle) : _from;
        _frameBy = (null, null);
        // The reach is shown only where it doesn't draw the shot far back from the targets: a wide spell's ring is far beyond them.
        var pitch = Rad(t.PreferredPitch);
        var alone = _finder!.Framed(_subjects, behind, pitch, float.MaxValue).Distance;
        var withRing = _finder.Framed([.. _subjects, ring], behind, pitch, float.MaxValue).Distance;
        var shown = withRing <= MathF.Max(alone, t.MinDistance) * t.ReachStretch;
        if (shown)
            _subjects.Add(ring);
        var key = (cursor, battle.TargetsB.Selected, shown);
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
        _aiming = (battle.State, caster, behind);
        return shot;
    }

    /// <summary>What the player aims at: the body the cursor is on, or the caster's own.</summary>
    private static Vector3 Reticle(Battle battle, Combatant current) =>
        (battle.CameraOwner is { Freed: false } o ? o : current).Body.Sphere.Position;

    /// <summary>A point of the grid on the ground, at the caster's height where none is found.</summary>
    private Vector3 Ground(Battle battle, Vector2 cell, Combatant c)
    {
        var at = battle.Origin + new Vector3(cell.X, 0, cell.Y);
        return new Vector3(at.X, Height(at.X, at.Z, c.Body.Sphere.Position.Y), at.Z);
    }

    private float Height(float x, float z, float fallback) => _finder?.Sight.GroundAt(x, z, fallback + 30) ?? fallback;

    private static ShotSubject Sphere(Body body) => new(body.Sphere.Position, body.ScaledRadius);

    private static float Rad(float degrees) => degrees * MathF.PI / 180;

    private static float Deg(float radians) => radians * 180 / MathF.PI;
}
