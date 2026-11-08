using System.Globalization;
using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Magic;
using HBHT.Core.Movement;

namespace HBHT.Core.Combat;

/// <summary>
/// Camera: Enhanced, a battle's camera for turns played the original's way (docs/systems/cameras.md, "The enhanced battle camera"). It
/// runs on top of the original's (<see cref="CombatCamera"/>), which goes on moving its own camera underneath, and shows a shot of its
/// own: in the player's turn low over the character's shoulder at the enemy it heads for, and its pack; once anyone acts, one shot
/// holding all of it, planned as it acts; an arrow, a throw or a spell at someone further off as two shots, the loosing and the hit; in
/// aiming the reticle in the middle, a spell's targets with some of its reach, or an area from above; and the original's again once the
/// player turns the camera themselves, until they aim. Every shot is taken from the side of the battle's vantage (<see cref="Vantage"/>),
/// and no one is framed under the conversation box. It moves to each shot with its acceleration capped, not its speed
/// (<see cref="CappedMotion"/>), trailing a moving goal rather than overshooting it, quicker while the character is out of view, and cuts
/// a long way, but never while the player walks the character.
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

    /// <summary>The character the shot is carried with in the player's turn, and where it stood last tick.</summary>
    private (Combatant Who, Vector3 At)? _carry;

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

    /// <summary>The player turned the camera in this turn: it shows the original's until they aim or the turn ends.</summary>
    public bool Released { get; private set; }

    /// <summary>
    /// Whether its caps were raised this tick: the player's hand on it, an aim following the reticle, or the view catching up with the
    /// character out of it.
    /// </summary>
    public bool Quick { get; private set; }

    /// <summary>Whether the player walks the character this tick: the caps are lowered (<see cref="CameraTuning.WalkPace"/>), and it never cuts.</summary>
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
        _carry = null;
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
        _motion.Caps = Released
            ? new MotionCaps(caps.Aim * t.HandAcceleration, caps.Turn * t.HandAcceleration, caps.Zoom * t.HandAcceleration, caps.Settle * hand, caps.ZoomSettle * hand)
            : _aiming is not null ? caps with { Aim = caps.Aim * t.AimingAcceleration, Settle = caps.Settle * MathF.Sqrt(t.AimingAcceleration) }
            : new MotionCaps(caps.Aim * pace, caps.Turn * pace, caps.Zoom * pace, caps.Settle * settle, caps.ZoomSettle * settle);
        _motion.GoalJump = t.GoalJump;
        // Only the player's own hand is followed at its speed; the director's shots are trailed, so a goal that stops isn't overshot.
        _motion.MostFollowed = Released ? (t.FollowSpeed, Rad(t.FollowTurn)) : (0, 0);
        _motion.NearestDistance = MathF.Min(t.NearestDistance, goal.Distance);
        // In the player's turn the shot moves with the character, as a third person's camera does: only the change of framing is capped,
        // so the view never trails one who runs, and stops as they stop.
        if (Walking && battle.Current is { } walker)
        {
            var at = walker.Body.Sphere.Position;
            if (_carry is { } c && c.Who == walker && Vector3.Distance(at, c.At) <= t.GoalJump)
                _motion.Carry(at - c.At);
            _carry = (walker, at);
        }
        else
        {
            _carry = null;
        }

        var now = _motion.Current;
        var travel = Vector3.Distance(goal.Aim, now.Aim);
        // A swing round is judged only where the yaw means something: not looking straight down.
        var turn = MathF.Max(now.Pitch, goal.Pitch) < Rad(75) ? MathF.Abs(Shot.Wrap(goal.Yaw - now.Yaw)) : 0;
        var tilt = MathF.Abs(goal.Pitch - now.Pitch);
        // Never while the player walks the character: the stick's way would turn under them. Their turn begins with them in view, cut
        // to unless it's near.
        var mayCut = !Released && (!Walking || entering);
        if (_cutNow || mayCut && (travel > t.CutDistance || turn > Rad(t.CutTurn) || tilt > Rad(t.CutPitch))
            || entering && !Released && (lost || travel > t.EnterCutDistance || turn > Rad(t.EnterCutTurn)))
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
        }
        if (battle.State != CombatState.Turn && battle.State != CombatState.TargetSpell)
            _framing = null;
        if (battle.State != CombatState.TargetSpell)
            _spellAim = null;
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
                return Aimed(battle, current, aimed, Ground(battle, marker.Centre, current), marker.Radius * t.AreaMargin, Rad(t.TopDownPitch));
            }
            case CombatState.TargetThrown:
                Kind = "aiming a throw";
                return Aimed(battle, current, aimed, Ground(battle, battle.ThrownAim, current), 1, Rad(t.AimPitch));
            case CombatState.TargetSpell when battle.Aim is { } reach:
                return SpellAim(battle, current, reach);
            case CombatState.TargetAttack when IsRanged(current):
                Kind = "aiming a ranged attack";
                return Aimed(battle, current, aimed, Reticle(battle, current), 1, Rad(t.AimPitch));
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

    private Shot TurnShot(Battle battle, Combatant current, Shot originalShot)
    {
        var t = Tuning;
        var me = current.Body.Sphere.Position;
        _candidates.Clear();
        foreach (var c in battle.Combatants)
        {
            if (c.Hostile != current.Hostile && !c.Freed && !c.Sheet.IsDead && c.Active)
                _candidates.Add(new TargetCandidate(c, c.Body.Sphere.Position));
        }
        if (_cone.Pick(me, current.Body.Heading, _candidates, t))
        {
            _sinceDecide = int.MaxValue / 2;
            _framing = null;
        }
        _subjects.Clear();
        _subjects.Add(Sphere(current.Body));
        if (Target is not { } target)
        {
            _framing = null;
            // The original's shot, where it holds the character; at the turn's start it's still on the last one's, gliding over.
            if (ShotFraming.Holds(originalShot, _subjects[0] with { Radius = 0 }, t.Lens))
                return Cleared(originalShot, _subjects, "following, no enemy ahead");
            var follow = new Shot(me, originalShot.Yaw, Rad(t.PreferredPitch), 0);
            follow = follow with { Distance = Math.Clamp(ShotFraming.DistanceToHold(_subjects, me, follow.Yaw, follow.Pitch, t.Lens), t.MinDistance, t.ShoulderDistance) };
            return Cleared(follow, _subjects, "following, no enemy ahead, the original's on its way");
        }
        _subjects.Add(Sphere(target.Body));
        // The target's pack: the shot looks at the middle of them all, and holds them while they're near.
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
        var behind = Look(me, ahead);
        _frameBy = (Lead(me, ahead), t.ShoulderDistance);
        Frame(behind, force: false);
        Kind = n == 1 ? "over the shoulder" : near ? $"over the shoulder at a pack of {n}" : $"over the shoulder toward a pack of {n}";
        return FramedNow(behind);
    }

    /// <summary>
    /// Finds the framing of <see cref="_subjects"/> over the shoulder (<paramref name="behind"/>) again where it's due: every look, at
    /// once once the framing held hides one (the character walking across its own sight line), or when <paramref name="force"/>d. A
    /// framing found replaces the one held only if it's better by <see cref="CameraTuning.SwitchMargin"/>.
    /// </summary>
    private void Frame(float behind, bool force)
    {
        var t = Tuning;
        var finder = _finder!;
        if (_framing is not null && !force && ++_sinceDecide < t.DecideEvery && finder.HiddenWeight(FramedNow(behind), _subjects) == 0)
            return;
        _sinceDecide = 0;
        var heldScore = _framing is not null && !force ? finder.Score(FramedNow(behind), _subjects, _from, behind, _pairs) : float.NegativeInfinity;
        if (finder.Best(_subjects, _from, behind, _pairs, _frameBy.Aim, _frameBy.Most) is var (best, score)
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
    /// (<see cref="ShotFinder.Planned"/>), from the vantage's side. It's kept the whole action, planned again only if one of those leaves
    /// it. While the AI thinks (state 8, before it has acted), the shot now stays. An action at someone further off is two shots instead
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
        Acted(battle, ai ? current.Ai?.Target ?? -1 : battle.Target);
        foreach (var slot in battle.TargetsB.SelectedSlots)
            Acted(battle, slot);
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
        var kind = ai ? "an AI's turn, planned" : "the player's action, planned";
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

    /// <summary>Adds the combatant in a slot to those an action acts on, if it's on the field and not already one.</summary>
    private void Acted(Battle battle, int slot)
    {
        if (slot >= 0 && slot < battle.Slots.Count && battle.Slots[slot] is { Freed: false } c && c != battle.Current && !_acted.Contains(c))
            _acted.Add(c);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // An arrow, a throw or a spell at someone further off: the loosing, then the hit

    /// <summary>
    /// An arrow, a throw, a flask or a spell at someone further than <see cref="CameraTuning.ReleaseDistance"/>, from its facing to its
    /// result (states 6, 2, 3 and 4): two shots, each cut to. The loosing: low over the shoulder of the one who acts, near them, at whom
    /// they act on. The hit, once the missile has flown <see cref="CameraTuning.HitAt"/> of its way, or
    /// <see cref="CameraTuning.ReleaseBeat"/> ticks after the spell's visual starts at the caster's strike, but not before the loosing has
    /// been shown <see cref="CameraTuning.LoosingTicks"/>; or as the blow lands: the target close, three-quarters on from the vantage's
    /// side (<see cref="HitShot"/>). Null for anything else; a spell's next target is a volley of its own.
    /// </summary>
    private Shot? Volley(Battle battle, Combatant current)
    {
        var t = Tuning;
        if (battle.State is not (CombatState.Facing or CombatState.Action or CombatState.Cast or CombatState.Result)
            || battle.Target < 0 || battle.Target >= battle.Slots.Count || battle.Slots[battle.Target] is not { } target || target == current)
        {
            _volley = null;
            return null;
        }
        if (_volley is not { } v || v.Who != current || v.At != target)
        {
            // A new one only for a target selected: in a walk's facing the target is the last action's.
            var spell = battle.TargetsA.Selected == 0 && battle.TargetsB.IsSelected(battle.Target);
            var missile = battle.TargetsA.IsSelected(battle.Target)
                && (battle.Flight is not null || current.Action == CombatAction.Flask || IsRanged(current));
            if (target.Freed || !spell && !missile || Flat(target.Body.Sphere.Position - current.Body.Sphere.Position) <= t.ReleaseDistance)
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
                shot = HitShot(battle, current, target);
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
    /// The hit: the target close (and a spell's other targets still to come, beside it), looking along the way the missile came turned
    /// <see cref="CameraTuning.HitTurn"/> toward the vantage's side, so it's seen three-quarters on and the missile comes in from the side
    /// the loosing was shot from.
    /// </summary>
    private Shot HitShot(Battle battle, Combatant who, Combatant at)
    {
        var t = Tuning;
        var there = at.Body.Sphere.Position;
        _subjects.Clear();
        _subjects.Add(Sphere(at.Body));
        _acted.Clear();
        foreach (var slot in battle.TargetsB.SelectedSlots)
            Acted(battle, slot);
        foreach (var c in _acted)
        {
            if (c != at)
                _subjects.Add(Sphere(c.Body) with { Weight = t.ClusterWeight });
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

    /// <summary>Where a body walks to: the last corner it's queued, on the ground; null if it isn't walking.</summary>
    private Vector3? Destination(Combatant c)
    {
        var body = c.Body;
        if (body.Queued.Count == 0)
            return null;
        var (goal, _) = body.Queued.Last();
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
    private Shot Aimed(Battle battle, Combatant c, (CombatState State, Combatant Who, float Yaw)? aimed, Vector3 reticle, float radius, float pitch)
    {
        var yaw = aimed is { } a && a.State == battle.State && a.Who == c ? a.Yaw : _from;
        _aiming = (battle.State, c, yaw);
        return _finder!.Aimed(reticle, radius, yaw, pitch);
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
