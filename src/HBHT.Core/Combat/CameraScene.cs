using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Magic;
using HBHT.Core.Physics;

namespace HBHT.Core.Combat;

/// <summary>
/// What a battle's camera reads of a tick (<see cref="EnhancedCamera"/>): where each body is and what it can do, whose turn it is, its
/// state, whom it acts on, its walk, its missile and its aim. Camera: Enhanced reads it from the live battle as it steps
/// (<see cref="Read"/>); Start Early's director from the drawing, each body where it's drawn and the rest from the tick of the action it
/// films (<see cref="BattleDirector"/>). So the same camera films either, and under Original reads just what it read of the battle.
/// </summary>
public sealed class CameraScene
{
    private readonly List<SceneBody> _bodies = [];
    private readonly Dictionary<Combatant, int> _index = [];

    /// <summary>The arena's collision, which hides what's behind it.</summary>
    public CollisionWorld World { get; private set; } = null!;

    /// <summary>The battlefield across the ground, its least and most corners, or null for none.</summary>
    public (Vector2 Min, Vector2 Max)? Field { get; private set; }

    /// <summary>The arena's origin, which the grid's cells are counted from.</summary>
    public Vector3 Origin { get; private set; }

    /// <summary>Everything else the camera reads of the tick.</summary>
    public SceneFacts Facts { get; private set; } = SceneFacts.None;

    /// <summary>The bodies, in the battle's order (<see cref="Battle.Combatants"/>), or the drawing's (slot order).</summary>
    public IReadOnlyList<SceneBody> Bodies => _bodies;

    /// <summary>The live battle as it steps: Camera: Enhanced's.</summary>
    public void Read(Battle battle)
    {
        Begin(battle.World, FieldOf(battle), battle.Origin, SceneFacts.Of(battle));
        foreach (var c in battle.Combatants)
            Add(new SceneBody(c, c.Body.Sphere.Position, c.Body.ScaledRadius, c.Body.Heading, BodyStateOf(c)));
    }

    /// <summary>Starts a scene afresh: its arena, the tick's facts, and no bodies yet (<see cref="Add"/>).</summary>
    public void Begin(CollisionWorld world, (Vector2 Min, Vector2 Max)? field, Vector3 origin, SceneFacts facts)
    {
        (World, Field, Origin, Facts) = (world, field, origin, facts);
        _bodies.Clear();
        _index.Clear();
    }

    /// <summary>Adds a body to the scene; a second of the same combatant replaces the first.</summary>
    public void Add(SceneBody body)
    {
        if (_index.TryGetValue(body.Who, out var i))
        {
            _bodies[i] = body;
            return;
        }
        _index[body.Who] = _bodies.Count;
        _bodies.Add(body);
    }

    /// <summary>A battle's field across the ground, from its arena's origin and grid; null with no arena.</summary>
    public static (Vector2 Min, Vector2 Max)? FieldOf(Battle battle) => battle.Arena is null ? null
        : (new Vector2(battle.Origin.X, battle.Origin.Z), new Vector2(battle.Origin.X + battle.Grid.Width, battle.Origin.Z + battle.Grid.Depth));

    /// <summary>What a combatant can do now, as the camera reads it.</summary>
    public static BodyState BodyStateOf(Combatant c) =>
        (c.Freed ? BodyState.Freed : 0) | (c.Sheet.IsDead ? BodyState.Dead : 0) | (c.Active ? BodyState.Active : 0)
        | (c.Hostile ? BodyState.Hostile : 0) | (c.Controlled ? BodyState.Controlled : 0);

    /// <summary>A combatant's body in the scene, or null if it isn't in it (gone from the field).</summary>
    public SceneBody? Of(Combatant? c) => c is not null && _index.TryGetValue(c, out var i) ? _bodies[i] : null;

    /// <summary>Whether a combatant is freed: its body fading or gone, or not in the scene.</summary>
    public bool Freed(Combatant c) => Of(c) is not { } b || b.Freed;

    public bool Dead(Combatant c) => Of(c) is { } b ? b.Dead : c.Sheet.IsDead;

    public bool Active(Combatant c) => Of(c) is { } b && b.Active;

    public bool Hostile(Combatant c) => Of(c) is { } b ? b.Hostile : c.Hostile;

    public bool Controlled(Combatant c) => Of(c) is { } b ? b.Controlled : c.Controlled;

    /// <summary>Where a combatant's sphere's centre is: in the scene, else its body's live place.</summary>
    public Vector3 Centre(Combatant c) => Of(c) is { } b ? b.Centre : c.Body.Sphere.Position;

    public Vector2 Heading(Combatant c) => Of(c) is { } b ? b.Heading : c.Body.Heading;

    public float Radius(Combatant c) => Of(c) is { } b ? b.Radius : c.Body.ScaledRadius;

    /// <summary>A combatant's body as a shot holds it.</summary>
    public ShotSubject Sphere(Combatant c) => new(Centre(c), Radius(c));

    // The facts, by name.
    public CombatState State => Facts.State;

    public Combatant? Current => Facts.Current;

    public Combatant? CameraOwner => Facts.CameraOwner;
}

/// <summary>What a combatant can do on a tick, as the camera reads it.</summary>
[Flags]
public enum BodyState : byte
{
    None = 0,

    /// <summary>Its body fading, dead or fled (<see cref="Combatant.Freed"/>).</summary>
    Freed = 1,

    /// <summary>No hit points left.</summary>
    Dead = 2,

    /// <summary>It can act (<see cref="Combatant.Active"/>).</summary>
    Active = 4,

    /// <summary>An enemy of the party.</summary>
    Hostile = 8,

    /// <summary>The AI plays it (<see cref="Combatant.Controlled"/>).</summary>
    Controlled = 16,
}

/// <summary>A body as the camera reads it on a tick: where its sphere's centre is, its radius, the way it heads, and what it can do.</summary>
public readonly record struct SceneBody(Combatant Who, Vector3 Centre, float Radius, Vector2 Heading, BodyState State)
{
    public bool Freed => State.HasFlag(BodyState.Freed);

    public bool Dead => State.HasFlag(BodyState.Dead);

    public bool Active => State.HasFlag(BodyState.Active);

    public bool Hostile => State.HasFlag(BodyState.Hostile);

    public bool Controlled => State.HasFlag(BodyState.Controlled);

    public ShotSubject Sphere => new(Centre, Radius);
}

/// <summary>
/// What a battle's camera reads of a tick beyond its bodies (<see cref="CameraScene"/>): read from the live battle as Camera: Enhanced
/// steps it, and recorded with each frame (<see cref="BattleFrame.Facts"/>) for Start Early's director to read as it films that tick.
/// </summary>
public sealed record SceneFacts
{
    /// <summary>A tick with nothing to say: no turn, no target.</summary>
    public static SceneFacts None { get; } = new();

    public CombatState State { get; init; }

    /// <summary>Whose turn it is.</summary>
    public Combatant? Current { get; init; }

    /// <summary>Whom the original's camera follows (<see cref="Battle.CameraOwner"/>).</summary>
    public Combatant? CameraOwner { get; init; }

    /// <summary>The combatant in the battle's target slot (<see cref="Battle.Target"/>), or null.</summary>
    public Combatant? Target { get; init; }

    /// <summary>Whether the target slot is selected in the attack's set, and in the spell's.</summary>
    public bool TargetInA { get; init; }

    public bool TargetInB { get; init; }

    /// <summary>How many are selected in the attack's set and in the spell's.</summary>
    public int SelectedA { get; init; }

    public int SelectedB { get; init; }

    /// <summary>Those selected in the spell's set, in slot order (<see cref="TargetSet.SelectedSlots"/>).</summary>
    public IReadOnlyList<Combatant> ChosenB { get; init; } = [];

    /// <summary>Whom the current combatant's AI means to act on, or null.</summary>
    public Combatant? AiTarget { get; init; }

    /// <summary>Whether the current combatant flees.</summary>
    public bool Fleeing { get; init; }

    /// <summary>Whether it attacks from afar: a ranged weapon and a ranged action.</summary>
    public bool Ranged { get; init; }

    /// <summary>Whether its action is a flask.</summary>
    public bool Flask { get; init; }

    /// <summary>How many a spell on the current combatant itself takes (<see cref="Battle.SpellTargetCount(Combatant)"/>), where that's asked.</summary>
    public int SpellTargets { get; init; }

    /// <summary>The missile in flight: where it is, where it left, and how far it flies.</summary>
    public (Vector3 Position, Vector3 From, float Length)? Flight { get; init; }

    /// <summary>Whether a spell's visual has started at its caster's strike (<see cref="Battle.CastLoosed"/>).</summary>
    public bool CastLoosed { get; init; }

    /// <summary>The marker on the ground while a spell, its area or a flask is aimed.</summary>
    public AimMarker? Aim { get; init; }

    /// <summary>The area placed, while one is: the aim's marker, or one made of the area's aim at the spell's level.</summary>
    public AimMarker? AreaMarker { get; init; }

    /// <summary>A throw's free cursor, on the grid.</summary>
    public Vector2 ThrownAim { get; init; }

    /// <summary>The current combatant's walk: whether it heads for a corner, which, and the corners queued after.</summary>
    public bool Moving { get; init; }

    public Vector2 Goal { get; init; }

    public IReadOnlyList<Vector2> Queued { get; init; } = [];

    /// <summary>The combatants whose turns come after this one's in the round, in order.</summary>
    public IReadOnlyList<Combatant> Upcoming { get; init; } = [];

    /// <summary>The original's camera (<see cref="CombatCamera.Camera"/>).</summary>
    public DrawnCamera Original { get; init; }

    /// <summary>Reads them from the live battle.</summary>
    public static SceneFacts Of(Battle battle)
    {
        var current = battle.Current;
        var state = battle.State;
        var slots = battle.Slots;
        var target = battle.Target;
        var inRange = target >= 0 && target < slots.Count;
        var (inA, inB) = inRange ? (battle.TargetsA.IsSelected(target), battle.TargetsB.IsSelected(target)) : (false, false);
        var chosen = new List<Combatant>();
        foreach (var slot in battle.TargetsB.SelectedSlots)
        {
            if (Slot(slot) is { } c)
                chosen.Add(c);
        }
        var upcoming = new List<Combatant>();
        var order = battle.Order;
        for (var i = order.Now + 1; i < order.Turns.Count; i++)
        {
            if (Slot(order.Turns[i]) is { } c)
                upcoming.Add(c);
        }
        var body = current?.Body;
        var queued = new List<Vector2>();
        if (body is not null)
        {
            foreach (var (goal, _) in body.Queued)
                queued.Add(goal);
        }
        var spellTargets = 0;
        if (current is not null && inRange && slots[target] == current && state is CombatState.Facing or CombatState.Action or CombatState.Cast
            or CombatState.Result && battle.TargetsA.Selected == 0 && inB)
            spellTargets = battle.SpellTargetCount(current);
        AimMarker? area = null;
        if (state == CombatState.PlaceArea && current?.CurrentSpell(battle) is { } source)
            area = battle.Aim ?? new AimMarker(battle.AreaAim, Casting.Level(current.Sheet, source), Spell: true);
        var original = battle.CombatCamera.Camera;
        return new SceneFacts
        {
            State = state,
            Current = current,
            CameraOwner = battle.CameraOwner,
            Target = inRange ? slots[target] : null,
            TargetInA = inA,
            TargetInB = inB,
            SelectedA = battle.TargetsA.Selected,
            SelectedB = battle.TargetsB.Selected,
            ChosenB = chosen,
            AiTarget = current?.Ai is { } ai ? Slot(ai.Target) : null,
            Fleeing = current?.Fleeing ?? false,
            Ranged = current is not null && EnhancedCamera.IsRanged(current),
            Flask = current?.Action == CombatAction.Flask,
            SpellTargets = spellTargets,
            Flight = battle.Flight is { } f ? (f.Position, f.From, f.Length) : null,
            CastLoosed = battle.CastLoosed,
            Aim = battle.Aim,
            AreaMarker = area,
            ThrownAim = battle.ThrownAim,
            Moving = body?.Moving ?? false,
            Goal = body?.Goal ?? default,
            Queued = queued,
            Upcoming = upcoming,
            Original = new DrawnCamera(original.Position, original.Aim, original.LookXZ),
        };

        Combatant? Slot(int slot) => slot >= 0 && slot < slots.Count ? slots[slot] : null;
    }
}
