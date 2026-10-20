using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.Movement;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

// The enemy AI's turn (EnemyAI.Step, 0x8005B314, each frame of state 8; states 5, 6 and 24 for its walk, its turn to the target and
// its rest): it decides once, walks at most one leg, and acts or rests. docs/systems/combat.md's "The AI" has the original's.
public sealed partial class Battle
{
    /// <summary>The combat strings' "%s changes weapons" (0x88).</summary>
    private const int ChangesWeaponsLine = 0x88;

    /// <summary>The ticks a walk may not move before the AI gives it up (<c>+0x4FBC</c>, set by every walk).</summary>
    public const int StuckTicks = 240;

    /// <summary>How little a body may move in a frame and count as stuck: 0.03, in hundredths (<c>Combat.StateMoving</c>).</summary>
    private const int StuckMove = 3;

    /// <summary>The ticks the AI rests with nothing to do, or after a weapon swap or a flight's leg (<c>Combat.RestTicks</c>, <c>0x800DB0D0</c>).</summary>
    public const int RestTicks = 120;

    /// <summary>How far past touching a target the AI walks straight at it when it has no path (<c>0x800D2488</c>).</summary>
    private const float ApproachReach = 2.25f;

    /// <summary>The path's cells (<c>+0x5320</c>): up to 144, the corners and the goal last.</summary>
    private readonly (int X, int Z)[] _path = new (int X, int Z)[144];

    /// <summary>The stuck timer (<c>+0x4FBC</c>), in ticks.</summary>
    public int Stuck { get; private set; }

    /// <summary>The rest's ticks left (<c>Combat.RestTicks</c>).</summary>
    private int _rest;

    /// <summary>Where the walking body was a tick ago (<c>0x800E6850</c>).</summary>
    private Vector3 _walkedFrom;

    /// <summary>The AI's path as it planned it: its corners, the goal last.</summary>
    public IReadOnlyList<(int X, int Z)> PathOf(EnemyAI ai) => ai.PathLength is > 0 and < 144 ? _path[..ai.PathLength] : [];

    /// <summary>
    /// The party's danger to bunched enemies (<c>+0x12</c>): its flask's radius, or twice its best Fireball's casting level, a radius
    /// the AI spreads out by (<c>EnemyAI.AreaDanger</c>), set as the battle begins (<see cref="PartyDanger"/>). 0 for the
    /// checkpoints' party.
    /// </summary>
    public int Danger { get; internal set; }

    /// <summary>A combatant's cell as the AI's grid functions take it: each coordinate rounded half away from zero, a byte.</summary>
    private static (int X, int Z) Cell8(Combatant c) => (ArenaGrid.CellOf(c.Cell.X) & 0xFF, ArenaGrid.CellOf(c.Cell.Y) & 0xFF);

    /// <summary>
    /// The AI's turn (<c>EnemyAI.Step</c>, <c>0x8005B314</c>, the first frame of state 8, every branch of which leaves it): the
    /// combatant reads its body and keeps where it starts; then it chooses its target and goal, or for a fleeing one the nearest
    /// flee point; plans a path there and walks its first leg (state 5), or failing that walks straight at a target close by, or
    /// acts from where it stands (state 6, then the attack), or rests (state 24).
    /// </summary>
    internal void StepAI(Combatant c)
    {
        if (c.Ai is not { } ai)
        {
            EndTurn(c);
            return;
        }
        c.SyncFromBody(this);
        c.SaveStartPosition();
        int x, z;
        bool done;
        if (!ai.IsFleeing)
        {
            done = ChooseGoal(ai, out x, out z);
        }
        else
        {
            FleeOrigin = c.Cell;
            done = BeginFleeWalk(ai, out x, out z);
        }
        if (done || Act(ai, x, z) || WalkPath(ai))
            return;
        if (!ai.IsFleeing)
        {
            if (ApproachTarget(ai))
                return;
        }
        else
        {
            if (FleeStep(ai))
                return;
            FleeAdvance(ai);
            // The original goes on to Execute even for one that has just left, its body gone, which the port doesn't follow.
            if (c.Gone)
                return;
        }
        Execute(ai);
    }

    /// <summary>
    /// The choice of a combatant that isn't fleeing (<c>EnemyAI.ChooseGoal</c>, <c>0x8005B144</c>): its style's
    /// (<see cref="RunStyle"/>); at its goal already, it acts from there (<see cref="Execute"/>). Whether it did.
    /// </summary>
    private bool ChooseGoal(EnemyAI ai, out int x, out int z)
    {
        var (cx, cz) = Cell8(ai.Combatant);
        (x, z) = RunStyle(ai);
        if (cx != x || cz != z)
            return false;
        Execute(ai);
        return true;
    }

    /// <summary>
    /// Plans a path to the goal (<c>EnemyAI.Act</c>, <c>0x8005AD9C</c>, through <see cref="PlanPath"/>), or with none, walks straight
    /// at a target close by or acts where it stands, or for a fleeing one walks straight to a flee point close by or takes its flight's
    /// step. Whether it found no path, and so has done what it does.
    /// </summary>
    private bool Act(EnemyAI ai, int x, int z)
    {
        if (PlanPath(ai, x, z))
            return false;
        if (!ai.IsFleeing)
        {
            if (!ApproachTarget(ai))
                Execute(ai);
        }
        else if (!FleeStep(ai))
        {
            FleeAdvance(ai);
        }
        return true;
    }

    /// <summary>
    /// A path from the combatant's cell to the goal over the grid (<c>EnemyAI.PlanPath</c>, <c>0x8005A974</c>,
    /// <see cref="ArenaGrid.FindPath"/>), its place at its start. Whether there is one. The Shadow's battle cuts it at the Shadow's
    /// radius (<see cref="SmoothPathRoundShadow"/>).
    /// </summary>
    private bool PlanPath(EnemyAI ai, int x, int z)
    {
        var c = ai.Combatant;
        var (cx, cz) = Cell8(c);
        var bodies = Combatants.Where(b => !b.Sheet.IsDead && !b.Gone).Select(b => (Cell8(b).X, Cell8(b).Z, b.Footprint)).ToList();
        ai.PathLength = (byte)Grid.FindPath(cx, cz, x, z, c.Footprint, _path, bodies);
        if (ai.PathLength >= Grid.Diagonal)
            return false;
        ai.PathPlace = 0;
        SmoothPathRoundShadow(ai);
        return true;
    }

    /// <summary>
    /// The leg the combatant walks of its path (<c>EnemyAI.WalkPath</c>, <c>0x8005A0D8</c>): corner by corner while the field holds
    /// it, measured from where its turn began. A taken or too steep corner ends it at the first cell on the straight line there that's
    /// blocked (<see cref="WalkIsClear"/>); one past the field ends it where the line leaves the field (<see cref="ClipToCircle"/>).
    /// Each corner is queued for the body to run to, facing its way, and the combatant stands on the last at once, on the grid too.
    /// Whether it walks (state 5, with the stuck timer); otherwise it only turns toward where it would have gone.
    /// </summary>
    private bool WalkPath(EnemyAI ai)
    {
        var c = ai.Combatant;
        if (c.Freed)
            return false;
        var body = c.Body;
        var (moved, blocked) = (false, false);
        var (cx, cz) = Cell8(c);
        var footprint = c.Footprint;
        Grid.Vacate(cx, cz, footprint);
        if ((sbyte)ai.PathPlace < ai.PathLength)
        {
            while (true)
            {
                var (nx, nz) = _path[(sbyte)ai.PathPlace];
                bool clear;
                if (!Grid.IsFree(nx, nz, footprint))
                {
                    blocked = true;
                    (clear, nx, nz) = WalkIsClear(cx, cz, nx, nz, footprint);
                    if (!clear)
                        break;
                }
                if (!Grid.IsGentle(cx, cz, nx, nz))
                {
                    blocked = true;
                    (clear, nx, nz) = WalkIsClear(cx, cz, nx, nz, footprint);
                    if (!clear)
                        break;
                }
                var start = (ArenaGrid.CellOf(c.TurnStart.X) & 0xFF, ArenaGrid.CellOf(c.TurnStart.Y) & 0xFF);
                var d = ArenaGrid.Distance(start.Item1, start.Item2, nx, nz) & 0xFF;
                if (c.Radius < d)
                {
                    blocked = true;
                    if (ClipToCircle(c.TurnStart, c.Radius, new Vector2(cx, cz), new Vector2(nx, nz)) is not { } edge)
                        break;
                    (nx, nz) = (RoundHalfAway(edge.X), RoundHalfAway(edge.Y));
                }
                var direction = ArenaGrid.DirectionTo(cx, cz, nx, nz);
                c.Facing = Directions[direction];
                c.Direction = direction;
                moved = true;
                (cx, cz) = (nx, nz);
                body.Queue(new Vector2(Origin.X + nx, Origin.Z + nz), body.ScaledRadius);
                c.Flags = d >= 3 ? c.Flags | CombatantFlags.Moved : c.Flags & ~CombatantFlags.Moved;
                ai.PathPlace++;
                if ((sbyte)ai.PathPlace >= ai.PathLength || blocked)
                    break;
            }
        }
        Grid.Occupy(cx, cz, footprint);
        if (moved)
        {
            c.Cell = new Vector2(cx, cz);
            StartWalk(c);
            return true;
        }
        FaceStep(ai);
        return false;

        static int RoundHalfAway(float v) => v > 0 ? (int)(v + 0.5f) & 0xFF : -(int)(0.5f - v) & 0xFF;
    }

    /// <summary>State 5, with the stuck timer started and the state to go on to the one the walk began in (the AI's, 8).</summary>
    private void StartWalk(Combatant c)
    {
        NextState = State;
        State = CombatState.Moving;
        Stuck = StuckTicks;
        _walkedFrom = c.Body.Sphere.Position;
    }

    /// <summary>Turns the combatant toward its path's next corner (<c>EnemyAI.FaceStep</c>, <c>0x8005A048</c>).</summary>
    private void FaceStep(EnemyAI ai)
    {
        var c = ai.Combatant;
        var (cx, cz) = Cell8(c);
        var (px, pz) = _path[Math.Max(0, (int)(sbyte)ai.PathPlace)];
        var direction = ArenaGrid.DirectionTo(cx, cz, px, pz);
        c.Facing = Directions[direction];
        c.Direction = direction;
    }

    /// <summary>
    /// How far a straight walk from (x0, z0) toward (tx, tz) gets (<c>EnemyAI.WalkIsClear</c>, <c>0x80059C94</c>), a Bresenham line
    /// cell by cell: at the first that's taken or too steep from the last, that cell, and whether a step was made before it; all
    /// clear, the end, and whether it differs from the start in both X and Z.
    /// </summary>
    internal (bool Clear, int X, int Z) WalkIsClear(int x0, int z0, int tx, int tz, int footprint)
    {
        (x0, z0) = (x0 & 0xFF, z0 & 0xFF);
        var (dx, dz) = (tx - x0, tz - z0);
        var (adx, adz) = ((short)Math.Abs(dx), (short)Math.Abs(dz));
        var (sx, sz) = (Math.Sign(dx), Math.Sign(dz));
        var (cx, cz) = (x0, z0);
        if (adx < adz)
        {
            if (adz > 0)
            {
                var (px, pz, xa, za, k, error) = (x0, z0, x0 + sx, z0 + sz, 1, adz >> 1);
                while (true)
                {
                    error = (short)(error + adx);
                    cx = px;
                    if (adz <= error)
                    {
                        error = (short)(error - adz);
                        cx = xa;
                        xa += sx;
                    }
                    cz = za;
                    if (!Grid.IsGentle(px, pz, cx, cz) || !Grid.IsFree(cx & 0xFF, cz & 0xFF, footprint))
                        return (pz != z0, cx & 0xFF, cz & 0xFF);
                    var last = k++;
                    za += sz;
                    (px, pz) = (cx, cz);
                    if (last >= adz)
                        break;
                }
            }
        }
        else if (adx > 0)
        {
            var (px, pz, xa, za, k, error) = (x0, z0, x0 + sx, z0 + sz, 1, adx >> 1);
            while (true)
            {
                error = (short)(error + adz);
                cz = pz;
                if (adx <= error)
                {
                    error = (short)(error - adx);
                    cz = za;
                    za += sz;
                }
                cx = xa;
                if (!Grid.IsGentle(px, pz, cx, cz) || !Grid.IsFree(cx & 0xFF, cz & 0xFF, footprint))
                    return (px != x0, cx & 0xFF, cz & 0xFF);
                var last = k++;
                xa += sx;
                (px, pz) = (cx, cz);
                if (last >= adx)
                    break;
            }
        }
        var (rx, rz) = (cx & 0xFF, cz & 0xFF);
        return (rx != x0 && rz != z0, rx, rz);
    }

    /// <summary>
    /// Where the segment <paramref name="a"/>–<paramref name="b"/> leaves the circle of <paramref name="radius"/> about
    /// <paramref name="centre"/> (<c>EnemyAI.ClipToCircle</c>, <c>0x80059A84</c>), or null. The original's quadratic has b where 2b
    /// belongs, which the port keeps: it decides where a leg stops. From the centre itself, a radius along the segment.
    /// </summary>
    internal static Vector2? ClipToCircle(Vector2 centre, float radius, Vector2 a, Vector2 b)
    {
        var e = b - a;
        if (centre == a)
        {
            var length = MathF.Max(MathF.Sqrt(e.X * e.X + e.Y * e.Y), 1e-6f);
            return new Vector2(e.X / length * radius + a.X, e.Y / length * radius + a.Y);
        }
        var d = a - centre;
        var qa = e.X * e.X + e.Y * e.Y;
        var qb = d.X * e.X + d.Y * e.Y;
        var qc = d.X * d.X + d.Y * d.Y;
        var discriminant = qb * qb - qa * (qc - radius * radius) * 4f;
        if (discriminant < 0)
            return null;
        var root = MathF.Sqrt(discriminant);
        var k = 1f / (qa + qa);
        var t1 = (root - qb) * k;
        var t2 = (-root - qb) * k;
        var t = t1;
        if (t1 < 0 || t1 > 1)
        {
            if (t2 < 0 || t2 > 1)
                return null;
            t = t2;
        }
        else if (t2 >= 0 && t2 <= 1 && t2 <= t1)
        {
            t = t2;
        }
        return new Vector2(e.X * t + a.X, e.Y * t + a.Y);
    }

    /// <summary>
    /// With no path, a walk straight at the target when it's within <see cref="ApproachReach"/> of touching
    /// (<c>EnemyAI.ApproachTarget</c>, <c>0x8005AB14</c>): to the point its bodies' radii short of it, or on the spot at once the
    /// action. Whether it did either.
    /// </summary>
    private bool ApproachTarget(EnemyAI ai)
    {
        var c = ai.Combatant;
        if (ai.Target < 0 || _slots[ai.Target] is not { } t || c.Freed || t.Freed)
            return false;
        var distance = Vector2.Distance(c.Cell, t.Cell);
        var touching = c.Body.ScaledRadius + t.Body.ScaledRadius;
        if (distance > touching + ApproachReach)
            return false;
        if (distance == 0)
        {
            Execute(ai);
            return true;
        }
        StartWalkTo(ai, Local(c.Body.Sphere.Position), Local(t.Body.Sphere.Position), (distance - touching) / distance);
        return true;
    }

    /// <summary>A place in the world in the arena chunk's own coordinates, across the ground.</summary>
    private Vector2 Local(Vector3 at) => new(at.X - Origin.X, at.Z - Origin.Z);

    /// <summary>
    /// A straight walk (<c>EnemyAI.StartWalkTo</c>, <c>0x8005AC30</c>), <paramref name="fraction"/> of the way from
    /// <paramref name="from"/> to <paramref name="to"/>: the body runs there, and the combatant stands there at once, on the grid's
    /// cell it rounds to; state 5.
    /// </summary>
    private void StartWalkTo(EnemyAI ai, Vector2 from, Vector2 to, float fraction)
    {
        var c = ai.Combatant;
        var p = (to - from) * fraction + from;
        c.Body.Queue(new Vector2(Origin.X + p.X, Origin.Z + p.Y), c.Body.ScaledRadius);
        var (cx, cz) = Cell8(c);
        Grid.Vacate(cx, cz, c.Footprint);
        c.Cell = p;
        (cx, cz) = Cell8(c);
        Grid.Occupy(cx, cz, c.Footprint);
        StartWalk(c);
    }

    /// <summary>
    /// The action (<c>EnemyAI.Execute</c>, <c>0x8005B594</c>): a weapon swapped first if its style wants the other kind
    /// (<see cref="ChooseWeapon"/>, which spends the turn); then with a target the sets offer (<see cref="PrepareTargetSets"/>) that it
    /// can act on, it turns to it (state 6) and attacks or casts; otherwise it rests (state 24). Fireball's centre is aimed first
    /// (<see cref="AimAreaSpell"/>).
    /// </summary>
    private void Execute(EnemyAI ai)
    {
        if (ai.Wanted != ai.Kind && ChooseWeapon(ai))
            return;
        // EnemyAI.PrepareAction (0x8005B1D8): Fireball's centre, then the sets.
        AimAreaSpell(ai);
        if (!PrepareTargetSets(ai) || !CanActOnTarget(ai))
        {
            EnterRest();
            return;
        }
        FaceCombatant(ai.Combatant, _slots[ai.Target]!);
    }

    /// <summary>
    /// The target sets from where the combatant stands (<c>EnemyAI.PrepareTargetSets</c>, <c>0x8005B8F8</c>): both built again, then
    /// the weapon's target first (<see cref="SelectWeaponTarget"/>), or for one that fights as a caster the spell's
    /// (<see cref="SelectSpellTarget"/>); so a caster that has once fought with its weapon is weapon-first. Whether either gave one.
    /// </summary>
    private bool PrepareTargetSets(EnemyAI ai)
    {
        var c = ai.Combatant;
        c.SyncFromBody(this);
        RebuildTargets(c);
        return ai.Kind == AiKind.Caster
            ? SelectSpellTarget(ai) || SelectWeaponTarget(ai)
            : SelectWeaponTarget(ai) || SelectSpellTarget(ai);
    }

    /// <summary>
    /// The weapon's target (<c>EnemyAI.SelectWeaponTarget</c>, <c>0x8005B7C8</c>): the chosen one if set A has it, selected alone;
    /// else whoever set A selected, the first or the last target. Set B is emptied. Whether there was one.
    /// </summary>
    private bool SelectWeaponTarget(EnemyAI ai)
    {
        if (ai.Target >= 0 && TargetsA.IsCandidate(ai.Target))
        {
            TargetsA.ClearSelection();
            TargetsA.Select(ai.Target);
        }
        else if (TargetsA.Candidates != 0 && TargetsA.Selected != 0)
        {
            ai.Target = TargetsA.Cursor;
        }
        else
        {
            return false;
        }
        TargetsB.Clear();
        return true;
    }

    /// <summary>
    /// The spell's target (<c>EnemyAI.SelectSpellTarget</c>, <c>0x8005B878</c>), as <see cref="SelectWeaponTarget"/> over set B; set
    /// A is emptied.
    /// </summary>
    private bool SelectSpellTarget(EnemyAI ai)
    {
        if (ai.Target < 0 || !TargetsB.IsCandidate(ai.Target) || !TargetsB.IsSelected(ai.Target))
        {
            if (TargetsB.Candidates == 0 || TargetsB.Selected == 0)
                return false;
            ai.Target = TargetsB.Cursor;
        }
        TargetsA.Clear();
        return true;
    }

    /// <summary>
    /// Whether the combatant can act on its target (<c>EnemyAI.CanActOnTarget</c>, <c>0x8005B218</c>), which becomes the action's
    /// (<see cref="Target"/>): cast at it (<see cref="CanCast"/>) or attack it (<see cref="CanAttack"/>), a caster trying the spell
    /// first.
    /// </summary>
    private bool CanActOnTarget(EnemyAI ai)
    {
        Target = ai.Target;
        if (ai.Target < 0 || _slots[ai.Target] is not { } t)
            return false;
        var c = ai.Combatant;
        return ai.Kind == AiKind.Caster ? CanCast(c, t) || CanAttack(c, t) : CanAttack(c, t) || CanCast(c, t);
    }

    /// <summary>
    /// Turns <paramref name="c"/> to face <paramref name="t"/> for its action (<c>Combatant.FaceCombatant</c>, <c>0x8005FD28</c>):
    /// its facing at the target, its body's walk forgotten and its turn begun, state 6.
    /// </summary>
    private void FaceCombatant(Combatant c, Combatant t)
    {
        State = NextState = CombatState.Facing;
        var way = t.Cell - c.Cell;
        if (way.LengthSquared() > 0)
            c.Facing = Vector2.Normalize(way);
        c.Body.ClearPath();
        _turning = true;
    }

    /// <summary>
    /// Rests (<c>Combat.EnterRest</c>, <c>0x80023418</c>): state 24, which ends the turn after <see cref="RestTicks"/>.
    /// </summary>
    private void EnterRest()
    {
        State = CombatState.Rest;
        _rest = RestTicks;
    }

    /// <summary>A tick of state 24 (<c>Combat.StateRest</c>, <c>0x80022898</c>): once rested, the turn ends.</summary>
    private void StepRest()
    {
        if (--_rest <= 0 && Current is { } c)
            EndTurn(c);
    }

    /// <summary>
    /// A in state 5 (<c>Combat.InputMoving</c>, <c>0x80084F98</c>): the walk ends at once, its body put at the leg's end
    /// (<see cref="Body.SkipToEnd"/>), where the combatant already stands, even if the body would have stuck on the way; the state's next
    /// step then goes on as on arriving. It asks only that the body is walking a path, not whose side it's on, so Sholeh's walks skip
    /// too. Whether it skipped.
    /// </summary>
    public bool SkipWalk()
    {
        if (State != CombatState.Moving || Fade.Fading || Lines.Hold is not null || Current is not { Freed: false } c)
            return false;
        return c.Body.SkipToEnd(World);
    }

    /// <summary>
    /// A tick of state 5 (<c>Combat.StateMoving</c>, <c>0x80021FD0</c>): a body that hasn't moved 0.03 in a frame counts down the stuck
    /// timer. Once it has no more points to run to, its leg is over: the AI turns it the way it walked (state 6, then
    /// <see cref="FleeAdvance"/>'s action). If the timer runs out first, the AI gives the walk up where it is, as though it had
    /// arrived; a combatant without one ends its turn.
    /// </summary>
    /// <remarks>
    /// The original measures a frame's move; the port measures a tick's, times a 30 fps frame's ticks. Its stuck test also counts an
    /// actor that stands, which moves under 0.03 too.
    /// </remarks>
    private void StepMoving()
    {
        if (Current is not { } c)
            return;
        if (c.Freed)
        {
            EndTurn(c);
            return;
        }
        var body = c.Body;
        var at = body.Sphere.Position;
        var moved = (int)(Vector3.Distance(at, _walkedFrom) * Cadence.FrameTicks * 100f);
        if ((moved & 0xFF) < StuckMove)
            Stuck--;
        // The body's own giving up (30 ticks without coming nearer) clears its path in combat, and the timer with it.
        if (body.GaveUp)
        {
            body.GaveUp = false;
            Stuck = 0;
        }
        _walkedFrom = at;
        if (!body.Moving && body.Queued.Count == 0)
        {
            State = NextState;
            if (NextState == CombatState.AI)
            {
                // Combatant.FaceAndSettle: the body turns the way its last step went, then state 6.
                State = CombatState.Facing;
                _turning = true;
            }
            else
            {
                EndTurn(c);
            }
        }
        else if (Stuck < 1)
        {
            body.ClearPath();
            if (c.Ai is { } ai)
                FleeAdvance(ai);
            else
                EndTurn(c);
        }
    }

    /// <summary>
    /// What follows the AI's walk, or its leg's turn (<c>EnemyAI.FleeAdvance</c>, <c>0x8005B434</c>): the combatant reads its body;
    /// not fleeing, it acts (<see cref="Execute"/>); fleeing, the flight's turns count down, and on the last, near a flee point, or not
    /// a unit from where its turn began, it leaves (<see cref="Leave"/>); either way it rests.
    /// </summary>
    private void FleeAdvance(EnemyAI ai)
    {
        var c = ai.Combatant;
        c.SyncFromBody(this);
        if (!ai.IsFleeing)
        {
            Execute(ai);
            return;
        }
        var left = false;
        ai.FlightLeft--;
        if (ai.FlightLeft == 0)
        {
            Leave(c);
            left = true;
        }
        else if (NearestFleePoint(c) is { Distance: <= FleePointNear })
        {
            Leave(c);
            left = true;
        }
        if (!left && Vector2.Distance(c.Cell, FleeOrigin) < 1f)
            Leave(c);
        EnterRest();
    }

    /// <summary>
    /// The weapon the AI's style wants (<c>EnemyAI.ChooseWeapon</c>, <c>0x8005A620</c>): what it fights as becomes what it wants, its
    /// selected spell goes, and its action is melee or ranged; if the held weapon is the wrong kind and the spare the right one, it swaps them
    /// (<see cref="SwapWeapon"/>), which spends the turn. The target sets are built again. Whether it swapped.
    /// </summary>
    private bool ChooseWeapon(EnemyAI ai)
    {
        var c = ai.Combatant;
        var held = WeaponOf(c);
        var spare = ai.Spare?.Item.Weapon;
        ai.Kind = ai.Wanted;
        // The selected spell's source goes (ce+0x3D), so set B is empty this turn.
        c.Spell = SpellChoice.None;
        var swapped = false;
        if (ai.Kind == AiKind.Melee)
        {
            c.Action = CombatAction.Melee;
            if (held is not null && !AllowsMelee(held) && spare is not null && AllowsMelee(spare))
                swapped = SwapWeapon(ai);
        }
        if (ai.Kind == AiKind.Ranged)
        {
            c.Action = CombatAction.Ranged;
            if (held is { Range: 0 } && spare is { Range: not 0 })
                swapped = SwapWeapon(ai);
        }
        RebuildTargets(c);
        return swapped;
    }

    /// <summary>Whether a weapon may strike in melee (<c>Weapon.AllowsMelee</c>, <c>0x8007B730</c>): no range, a Breath or a Spit, or Spikes.</summary>
    private bool AllowsMelee(WeaponRecord w) =>
        w.Range == 0 || w.Class is WeaponClass.Breath or WeaponClass.Spit || _rules?.CloseRange?.Contains(w.Id) == true;

    /// <summary>
    /// Trades the held weapon for the spare (<c>EnemyAI.SwapWeapon</c>, <c>0x8005A560</c>): "changes weapons", and the turn's spent
    /// resting. A weapon that strikes at range and in melee too makes its action both.
    /// </summary>
    private bool SwapWeapon(EnemyAI ai)
    {
        if (ai.Spare is not { } spare)
            return false;
        var c = ai.Combatant;
        var equipment = c.Sheet.Equipment;
        var held = equipment.RemoveWeapon();
        if (equipment.EquipWeapon(spare) != Items.EquipRefusal.None)
            equipment.Restore(spare);
        ai.Spare = held;
        c.Drawn = _rules?.Missiles?.Any(m => m.Weapon == spare.Id) == true;
        Write(Line(ChangesWeaponsLine, c));
        EnterRest();
        if (spare.Item.Weapon is { } w && AllowsMelee(w) && w.Range != 0)
            c.Action = CombatAction.MeleeOrRanged;
        c.EquipVisuals(_rules);
        return true;
    }
}
