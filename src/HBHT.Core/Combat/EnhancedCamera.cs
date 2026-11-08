using System.Globalization;
using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Magic;
using HBHT.Core.Movement;

namespace HBHT.Core.Combat;

/// <summary>
/// Camera: Enhanced, a battle's camera for turns played the original's way (docs/systems/cameras.md, "The enhanced battle camera"). It
/// runs on top of the original's (<see cref="CombatCamera"/>), which goes on moving its own camera underneath, and shows a shot of its
/// own: in the player's turn over the character's shoulder at the enemy it heads for; in an AI's turn one shot holding all of it, planned
/// as it acts; in aiming the reticle in the middle, or from above for an area; the original's for the player's own attacks and casts,
/// kept clear of walls and bodies; and the original's again once the player turns the camera themselves, until they aim. It moves to
/// each shot with its acceleration capped, not its speed (<see cref="CappedMotion"/>), and cuts a long way.
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
    private readonly TargetCone _cone = new();

    /// <summary>The turn it last saw begin.</summary>
    private Combatant? _turnOf;

    /// <summary>The framing of the character and the target held: its turn from straight over the shoulder, and its pitch.</summary>
    private (float Offset, float Pitch)? _framing;

    private int _sinceDecide;

    /// <summary>What's put on the original's shot to clear its sight lines, and how many looks in a row it has been clear without it.</summary>
    private (float Yaw, float Pitch, float Scale)? _correction;

    private int _clear;

    /// <summary>The aim being framed: its state, who aims, and the yaw the view had as it began.</summary>
    private (CombatState State, Combatant Who, float Yaw)? _aiming;

    /// <summary>An AI's turn's shot, once it has acted.</summary>
    private (Combatant Who, Shot Shot)? _plan;

    /// <summary>Where an AI's turn's body has stood and been going as its shot was planned, which a plan made again keeps in view.</summary>
    private readonly List<ShotSubject> _shown = [];

    /// <summary>How many of the AI's turn's subjects its plan couldn't hold.</summary>
    private int _planMissed;

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

    /// <summary>Whether its caps were raised this tick: the player's hand on it, or an aim following the reticle.</summary>
    public bool Quick { get; private set; }

    /// <summary>Whom the player heads for in their turn, its representative target.</summary>
    public Combatant? Target => _cone.Target as Combatant;

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
            return string.Create(CultureInfo.InvariantCulture,
                $"camera: enhanced, {Kind}{(Target is { } t ? $", target {t.Sheet.Name}" : "")}{(Released ? ", yours" : "")}; " +
                $"yaw {Deg(s.Yaw):F0} pitch {Deg(s.Pitch):F0} distance {s.Distance:F1}, aim speed {m.AimSpeed.Length():F1}, cuts {Cuts}");
        }
    }

    /// <summary>It starts afresh from the original's camera, as it's turned on.</summary>
    public void Begin()
    {
        _motion = null;
        _turnOf = null;
        _correction = null;
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
        _aiming = null;
        _plan = null;
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
        var sight = _finder.Sight;
        sight.Radius = t.SightRadius;
        sight.BodyScale = t.BodyOcclusion;
        sight.Bodies.Clear();
        foreach (var c in battle.Combatants)
        {
            if (!c.Freed)
                sight.Bodies.Add(Sphere(c.Body));
        }
        var originalShot = Shot.Of(original.Position, original.Aim, _motion?.Current.Yaw ?? Shot.YawOf(original.LookXZ));
        _from = _motion?.Current.Yaw ?? originalShot.Yaw;
        if (battle.Current != _turnOf)
        {
            _turnOf = battle.Current;
            TurnEnded();
        }
        // Aiming takes the camera back from the player's hand.
        if (battle.State is CombatState.TargetAttack or CombatState.TargetSpell or CombatState.TargetThrown or CombatState.PlaceArea)
            Released = false;

        var aimed = _aiming;
        _aiming = null;
        var goal = Decide(battle, originalShot, glide, aimed);
        // The player's own camera is the original's as it is; the director's shots keep their distance.
        if (!Released)
            goal = goal with { Pitch = Math.Clamp(goal.Pitch, Rad(-60), Rad(89)), Distance = MathF.Max(goal.Distance, t.NearestDistance) };
        Goal = goal;

        var caps = t.Caps;
        Quick = Released || _aiming is not null;
        if (_motion is null)
        {
            // The battle's first shot is cut to: there's nothing yet to move from.
            _motion = new CappedMotion(goal, caps);
            Camera.Cut();
        }
        var hand = MathF.Sqrt(t.HandAcceleration);
        _motion.Caps = Released
            ? new MotionCaps(caps.Aim * t.HandAcceleration, caps.Turn * t.HandAcceleration, caps.Zoom * t.HandAcceleration, caps.Settle * hand, caps.ZoomSettle * hand)
            : _aiming is not null ? caps with { Aim = caps.Aim * t.AimingAcceleration, Settle = caps.Settle * MathF.Sqrt(t.AimingAcceleration) }
            : caps;
        _motion.GoalJump = t.GoalJump;
        _motion.MostFollowed = (_aiming is not null ? t.AimingFollowSpeed : t.FollowSpeed, Rad(t.FollowTurn));
        _motion.NearestDistance = MathF.Min(t.NearestDistance, goal.Distance);

        var now = _motion.Current;
        var travel = Vector3.Distance(goal.Aim, now.Aim);
        // A swing round is judged only where the yaw means something: not looking straight down.
        var turn = MathF.Max(now.Pitch, goal.Pitch) < Rad(75) ? MathF.Abs(Shot.Wrap(goal.Yaw - now.Yaw)) : 0;
        var tilt = MathF.Abs(goal.Pitch - now.Pitch);
        if (!Released && (travel > t.CutDistance || turn > Rad(t.CutTurn) || tilt > Rad(t.CutPitch)))
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
        if (current.Controlled)
            return AiTurn(battle, current, originalShot);
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
            case CombatState.TargetSpell when battle.Aim is not null:
                Kind = "aiming a spell";
                return Aimed(battle, current, aimed, Reticle(battle, current), 1, Rad(t.AimPitch));
            case CombatState.TargetAttack when IsRanged(current):
                Kind = "aiming a ranged attack";
                return Aimed(battle, current, aimed, Reticle(battle, current), 1, Rad(t.AimPitch));
            case CombatState.Turn:
                return TurnShot(battle, current, originalShot);
        }
        _framing = null;
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
    // Events: the original's shot, kept clear

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
    // The player's turn: over the shoulder at the enemy they head for

    private Shot TurnShot(Battle battle, Combatant current, Shot originalShot)
    {
        var t = Tuning;
        var finder = _finder!;
        var me = current.Body.Sphere.Position;
        _candidates.Clear();
        foreach (var c in battle.Combatants)
        {
            var at = c.Body.Sphere.Position;
            if (c.Hostile != current.Hostile && !c.Freed && !c.Sheet.IsDead && c.Active
                && new Vector2(at.X - me.X, at.Z - me.Z).Length() <= t.TargetRange)
                _candidates.Add(new TargetCandidate(c, at));
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
            return Cleared(originalShot, _subjects, "following, no enemy ahead");
        }
        _subjects.Add(Sphere(target.Body));
        // Straight over the shoulder: the eye behind the character, looking at the target.
        var away = me - target.Body.Sphere.Position;
        var behind = MathF.Atan2(away.X, away.Z);
        // Looked at again every look, or at once once the framing held hides one (the character walking across its own sight line).
        if (_framing is null || ++_sinceDecide >= t.DecideEvery
            || finder.HiddenWeight(finder.Framed(_subjects, Shot.Wrap(behind + _framing.Value.Offset), _framing.Value.Pitch), _subjects) > 0)
        {
            _sinceDecide = 0;
            var heldScore = _framing is { } h
                ? finder.Score(finder.Framed(_subjects, Shot.Wrap(behind + h.Offset), h.Pitch), _subjects, _from, behind)
                : float.NegativeInfinity;
            if (finder.Best(_subjects, _from, behind) is var (best, score) && score > heldScore + (_framing is null ? 0 : t.SwitchMargin))
                _framing = (Shot.Wrap(best.Yaw - behind), best.Pitch);
        }
        Kind = "over the shoulder";
        var (offset, pitch) = _framing ?? (0, Rad(t.PreferredPitch));
        return finder.Framed(_subjects, Shot.Wrap(behind + offset), pitch);
    }

    /// <summary>Whether a combatant attacks from afar: a ranged weapon and a ranged action.</summary>
    private static bool IsRanged(Combatant c) =>
        Battle.WeaponOf(c) is { } w && (w.Range > 0 || w.Missile is not null) && c.Action is CombatAction.Ranged or CombatAction.MeleeOrRanged;

    // ---------------------------------------------------------------------------------------------------------------------------
    // An AI's turn: one shot for all of it

    /// <summary>
    /// An AI's turn: once it has acted (left its thinking state, state 8), one shot holding where it stands, where it walks to, whom it
    /// acts on and whom the original's camera follows, turned least from the shot now and the next player's turn's
    /// (<see cref="ShotFinder.Planned"/>). It's kept the whole turn, planned again only if one of those leaves it. While the AI thinks,
    /// the shot now stays.
    /// </summary>
    private Shot AiTurn(Battle battle, Combatant current, Shot originalShot)
    {
        _framing = null;
        _subjects.Clear();
        _subjects.Add(Sphere(current.Body));
        if (Destination(current) is { } to)
            _subjects.Add(new ShotSubject(to, current.Body.ScaledRadius));
        var aimedAt = current.Ai is { Target: >= 0 } ai && ai.Target < battle.Slots.Count ? battle.Slots[ai.Target] : null;
        if (aimedAt is { Freed: false } && aimedAt != current)
            _subjects.Add(Sphere(aimedAt.Body));
        if (battle.CameraOwner is { Freed: false } owner && owner != current && owner != aimedAt)
            _subjects.Add(Sphere(owner.Body));
        // Planned within the lens's margins, it's kept while each is anywhere on the screen, or while no more are off it than the plan
        // couldn't help (one too far for any shot), and it's planned again at most every look.
        var screen = Tuning.Lens with { Margin = 1 };
        if (_plan is { } p && p.Who == current
            && (_subjects.Count(s => !ShotFraming.Holds(p.Shot, s with { Radius = 0 }, screen)) <= _planMissed || ++_sinceDecide < Tuning.DecideEvery))
        {
            Kind = "an AI's turn, planned";
            return p.Shot;
        }
        if (_plan is null && battle.State == CombatState.AI)
        {
            Kind = "an AI's turn, thinking";
            return _motion?.Current ?? originalShot;
        }
        // Planned again, it keeps what the turn has shown in view (where the body stood and was going), so the shot widens rather than swings.
        if (_plan is null)
            _shown.Clear();
        _shown.AddRange(_subjects.Take(Destination(current) is null ? 1 : 2));
        if (_finder!.Planned([.. _subjects, .. _shown], _from, NextYaw(battle)) is not var (shot, _))
            return originalShot;
        _plan = (current, shot);
        _sinceDecide = 0;
        _planMissed = _subjects.Count(s => !ShotFraming.Holds(shot, s with { Radius = 0 }, screen));
        Kind = "an AI's turn, planned";
        return shot;
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

    // ---------------------------------------------------------------------------------------------------------------------------
    // Aiming: the reticle in the middle, followed

    /// <summary>An aim's shot (<see cref="ShotFinder.Aimed"/>), from the yaw the view had as the aim began.</summary>
    private Shot Aimed(Battle battle, Combatant c, (CombatState State, Combatant Who, float Yaw)? aimed, Vector3 reticle, float radius, float pitch)
    {
        var yaw = aimed is { } a && a.State == battle.State && a.Who == c ? a.Yaw : _from;
        _aiming = (battle.State, c, yaw);
        return _finder!.Aimed(reticle, radius, yaw, pitch);
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
