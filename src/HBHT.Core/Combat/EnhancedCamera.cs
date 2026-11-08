using System.Globalization;
using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Magic;
using HBHT.Core.Movement;
using HBHT.Plugins;

namespace HBHT.Core.Combat;

/// <summary>
/// Camera: Enhanced, a battle's camera for turns played the original's way (docs/systems/cameras.md, "The enhanced battle camera"). It
/// runs on top of the original's (<see cref="CombatCamera"/>), which goes on moving its own camera underneath, and shows a shot of its
/// own: the original's for events, kept clear of walls and bodies; in the player's turn the character with the body they point at; in
/// aiming the whole reach, or from above for an area; and the original's again once the player turns the camera themselves. It moves to
/// each shot with its acceleration capped, not its speed (<see cref="CappedMotion"/>).
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

    /// <summary>The framing of the character and the target held: its yaw and pitch in the world.</summary>
    private (float Yaw, float Pitch)? _framing;

    private int _sinceDecide;

    /// <summary>What's put on the original's shot to clear its sight lines, and how many looks in a row it has been clear without it.</summary>
    private (float Yaw, float Pitch, float Scale)? _correction;

    private int _clear;

    /// <summary>The aim being framed: its state and who aims, and its shot.</summary>
    private (CombatState State, Combatant Who, AimFraming Framing)? _aiming;

    /// <summary>What it shows: the battle's camera while it's on.</summary>
    public FollowCamera Camera { get; } = new(default, Vector2.UnitY) { Mode = CameraMode.Interior };

    public CameraTuning Tuning { get; set; } = CameraTuning.Default;

    /// <summary>The motion its shot moves by, once it has started.</summary>
    public CappedMotion? Motion => _motion;

    /// <summary>What finds its shots, once it has started.</summary>
    public ShotFinder? Finder => _finder;

    /// <summary>The shot it moved toward this tick.</summary>
    public Shot Goal { get; private set; }

    /// <summary>The player turned the camera in this turn: it shows the original's until the turn ends.</summary>
    public bool Released { get; private set; }

    /// <summary>Whom the player points at in their turn, its representative target.</summary>
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
    /// The player turns the camera in their turn: it lets go, and the original's camera takes over from the shot shown until the turn
    /// ends, its leash swooping it in as it pulls.
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

    /// <summary>The turn ends: the director has the camera again, and forgets the target and the aim.</summary>
    internal void TurnEnded()
    {
        // The first tick after looks at once.
        _sinceDecide = int.MaxValue / 2;
        Released = false;
        _cone.Clear();
        _framing = null;
        _aiming = null;
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
        _motion ??= new CappedMotion(originalShot, t.Caps);
        _motion.Caps = t.Caps;
        _motion.GoalJump = t.GoalJump;
        if (battle.Current != _turnOf)
        {
            _turnOf = battle.Current;
            TurnEnded();
        }

        var goal = Decide(battle, originalShot, glide);
        goal = goal with { Pitch = Math.Clamp(goal.Pitch, Rad(-60), Rad(89)), Distance = MathF.Max(goal.Distance, 1) };
        Goal = goal;
        if (Vector3.Distance(goal.Aim, _motion.Current.Aim) > t.CutDistance)
        {
            _motion.Cut(goal);
            Camera.Cut();
            Cuts++;
        }
        else
        {
            _motion.Step(goal);
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

    private Shot Decide(Battle battle, Shot originalShot, (Body From, Body To)? glide)
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
        var t = Tuning;
        switch (battle.State)
        {
            case CombatState.PlaceArea when current.CurrentSpell(battle) is { } source:
            {
                // Its centre moves within the spell's range times its level of the caster, and the area reaches its level beyond.
                var level = Casting.Level(current.Sheet, source);
                var marker = battle.Aim ?? new AimMarker(battle.AreaAim, level, Spell: true);
                Kind = "placing an area, from above";
                return Aiming(battle, current, source.Spell.Range * level + marker.Radius, Rad(t.TopDownPitch))
                    .Step(Ground(battle, marker.Centre, current), marker.Radius, t);
            }
            case CombatState.TargetThrown:
            {
                var reach = current.HoldsFlask ? Battle.FlaskReach : Battle.WeaponOf(current)?.Range ?? 0;
                Kind = "aiming a throw";
                return Aiming(battle, current, reach, Rad(t.AimPitch)).Step(Ground(battle, battle.ThrownAim, current), 1, t);
            }
            case CombatState.TargetSpell when battle.Aim is { } marker:
                Kind = "aiming a spell";
                return Aiming(battle, current, marker.Radius, Rad(t.AimPitch)).Step(Reticle(battle, current), 1, t);
            case CombatState.TargetAttack when RangedReach(current) is { } reach:
                Kind = "aiming a ranged attack";
                return Aiming(battle, current, reach, Rad(t.AimPitch)).Step(Reticle(battle, current), 1, t);
            case CombatState.Turn when !current.Controlled:
                _aiming = null;
                return TurnShot(battle, current, originalShot);
        }
        _aiming = null;
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
    // The player's turn: the character and whom they point at

    private Shot TurnShot(Battle battle, Combatant current, Shot originalShot)
    {
        var t = Tuning;
        var finder = _finder!;
        var me = current.Body.Sphere.Position;
        _candidates.Clear();
        foreach (var c in battle.Combatants)
        {
            if (c != current && !c.Freed && !c.Sheet.IsDead && c.Active)
                _candidates.Add(new TargetCandidate(c, c.Body.Sphere.Position, c.Hostile != current.Hostile));
        }
        if (_cone.Pick(me, current.Body.Heading, PrefersFar(current), _candidates, t))
            _sinceDecide = int.MaxValue / 2;
        _subjects.Clear();
        _subjects.Add(Sphere(current.Body));
        if (Target is not { } target)
        {
            _framing = null;
            return Cleared(originalShot, _subjects, "following, no one pointed at");
        }
        _subjects.Add(Sphere(target.Body));
        var away = me - target.Body.Sphere.Position;
        var behind = MathF.Atan2(away.X, away.Z);
        if (_framing is null || ++_sinceDecide >= t.DecideEvery)
        {
            _sinceDecide = 0;
            var from = _motion!.Current.Yaw;
            var heldScore = _framing is { } h ? finder.Score(finder.Framed(_subjects, h.Yaw, h.Pitch), _subjects, from, behind) : float.NegativeInfinity;
            if (finder.Best(_subjects, from, behind) is var (best, score) && score > heldScore + (_framing is null ? 0 : t.SwitchMargin))
                _framing = (best.Yaw, best.Pitch);
        }
        Kind = "the character and the target";
        var (yaw, pitch) = _framing ?? (_motion!.Current.Yaw, Rad(t.PreferredPitch));
        return finder.Framed(_subjects, yaw, pitch);
    }

    /// <summary>A ranged weapon's reach, for a combatant attacking from afar with it; null for melee.</summary>
    private static float? RangedReach(Combatant c) =>
        Battle.WeaponOf(c) is { } w && (w.Range > 0 || w.Missile is not null) && c.Action is CombatAction.Ranged or CombatAction.MeleeOrRanged
            ? w.Range
            : null;

    /// <summary>Whether a combatant's weapon is a ranged one or a staff, whose wielder likely aims far.</summary>
    private bool PrefersFar(Combatant c) =>
        c.HoldsFlask || Battle.WeaponOf(c) is { } w
            && (w.Range > 0 || w.Missile is not null || w.Skill == RecordId.Decimal(RecordTypes.WeaponSkill, Tuning.StaffSkill));

    // ---------------------------------------------------------------------------------------------------------------------------
    // Aiming: the whole reach, the reticle free within it

    /// <summary>The aim's framing, begun afresh as an aim begins: its reach round the caster, at the yaw the view has then.</summary>
    private AimFraming Aiming(Battle battle, Combatant c, float reach, float pitch)
    {
        if (_aiming is { } a && a.State == battle.State && a.Who == c)
            return a.Framing;
        var framing = new AimFraming(ReachPoints(battle, c, reach), _motion!.Current.Yaw, pitch);
        _aiming = (battle.State, c, framing);
        return framing;
    }

    /// <summary>The caster and points round its reach, kept on the grid, on the ground where it's found.</summary>
    private List<ShotSubject> ReachPoints(Battle battle, Combatant c, float reach)
    {
        var points = new List<ShotSubject> { Sphere(c.Body) };
        var at = c.Body.Sphere.Position;
        var origin = battle.Origin;
        var (minX, maxX) = (origin.X, origin.X + battle.Grid.Width);
        var (minZ, maxZ) = (origin.Z, origin.Z + battle.Grid.Depth);
        var n = Math.Max(Tuning.ReachPoints, 4);
        for (var i = 0; i < n; i++)
        {
            var (s, k) = MathF.SinCos(MathF.Tau * i / n);
            Add(Math.Clamp(at.X + reach * k, minX, maxX), Math.Clamp(at.Z + reach * s, minZ, maxZ));
        }
        foreach (var (x, z) in new[] { (minX, minZ), (minX, maxZ), (maxX, minZ), (maxX, maxZ) })
        {
            if (Vector2.Distance(new Vector2(x, z), new Vector2(at.X, at.Z)) <= reach)
                Add(x, z);
        }
        return points;

        void Add(float x, float z) => points.Add(new ShotSubject(new Vector3(x, Height(x, z, at.Y), z), 0.5f));
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
