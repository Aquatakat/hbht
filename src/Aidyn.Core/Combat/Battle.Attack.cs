using System.Numerics;
using Aidyn.Core.Actors;
using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.Movement;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

// Attacking (states 9, 18, 6, 2, 3 and 4): aiming, turning to the target, the action, the swing, the hit and the fallen taken
// off. docs/systems/combat.md's "An attack" has the original's.
public sealed partial class Battle
{
    private const int KilledLine = 0x50, CheatsDeathLine = 0xFC;

    /// <summary>The target's reactions (requests): hit, dodged, helped by its own side, and its fall.</summary>
    public const int HitRequest = 7, DodgeRequest = 8, DeathRequest = 9, HelpedRequest = 10;

    /// <summary>
    /// The ticks the action's timer must pass before the blow lands (<c>Combat.StepActionTimer</c>, <c>0x80021708</c>: above 29), and
    /// before the fallen are taken off (<c>Combat.RemoveFallen</c>: above 59).
    /// </summary>
    public const int SwingTicks = 30, ResultTicks = 60;

    /// <summary>How far a target can be before the camera pulls back behind the attacker while it's aimed at (<c>0x8008D8BC</c>).</summary>
    public const float PullBackDistance = 5;

    /// <summary>How far the free cursor of state 18 moves a frame at the stick's full tilt, and how near a body it picks it.</summary>
    public const float AimStep = 0.8f, AimPick = 1.0f;

    /// <summary>Requests a body keeps after a frame (<c>Combat.AfterFrame</c>, <c>0x80023360</c>); the rest are played once.</summary>
    private static readonly int[] KeptRequests = [0, 1, 2, 3, 0x14, 0x15, 0x16, 0x19];

    /// <summary>Target set A, what the current combatant can attack (<c>+0x4E64</c>).</summary>
    public TargetSet TargetsA { get; } = new(spells: false);

    /// <summary>Target set B, what its selected spell can reach (<c>+0x4F0C</c>), session 9's.</summary>
    public TargetSet TargetsB { get; } = new(spells: true);

    /// <summary>
    /// The combatant whose body the camera follows (<c>Camera.OwnerIndex</c>, <c>0x800D8448</c>, given by <c>GiveCameraToPlayer</c>):
    /// the current one's, or the cursor's target's while an attack is aimed. The radar centres on it.
    /// </summary>
    public Combatant? CameraOwner { get; private set; }

    /// <summary>The slot of the action's target (<c>+0x14</c>), or −1.</summary>
    public int Target { get; private set; } = -1;

    /// <summary>The numbers over heads.</summary>
    public CombatNumbers Numbers { get; } = new();

    /// <summary>An arrow or thrown weapon in flight (state 3), or null.</summary>
    public MissileFlight? Flight { get; private set; }

    /// <summary>Whether the first enemy has died (<c>+0x08</c>).</summary>
    public bool FirstEnemyDied { get; internal set; }

    /// <summary>The action timer (<c>+0x52CC</c>), in ticks.</summary>
    private int _actionTimer;

    /// <summary>Whether the attacker's body is turning to its target in state 6 (the body's flag <c>0x20</c>).</summary>
    private bool _turning;

    /// <summary>The way the attacker's body turns while an attack is aimed (<c>Combat.AimAtCursor</c>), or null.</summary>
    private Vector2? _aimTurn;

    /// <summary>The combat record, for who can't be attacked and the weapons that fly.</summary>
    private CombatRecord? _rules;

    /// <summary>The combat record's rules the battle plays by, if the plugins have one.</summary>
    internal CombatRecord? Rules => _rules;

    /// <summary>
    /// What the target sets say of one slot, which tints its portrait (<see cref="Ui.CombatGui.Tint"/>): A's candidate and
    /// selected, B's candidate and selected.
    /// </summary>
    public TargetMarks Marks(int slot) =>
        (TargetsA.IsCandidate(slot) ? TargetMarks.AttackCandidate : 0)
        | (TargetsA.IsSelected(slot) ? TargetMarks.AttackSelected : 0)
        | (TargetsB.IsCandidate(slot) ? TargetMarks.SpellCandidate : 0)
        | (TargetsB.IsSelected(slot) ? TargetMarks.SpellSelected : 0);

    /// <summary>Builds both target sets again for the current combatant (<c>TargetSet.Rebuild</c>, A and B).</summary>
    internal void RebuildTargets(Combatant c)
    {
        TargetsA.Rebuild(this, c);
        TargetsB.Rebuild(this, c);
    }

    /// <summary>
    /// Whether <paramref name="a"/> can attack <paramref name="t"/> (<c>Combatant.CanAttack</c>, <c>0x80060590</c>): never the Shadow;
    /// one on the other side, alive and in play, while it has an action (or its healing's rules, session 9's); its weapon reaching
    /// (<see cref="Reaches"/>); and a clear walk over the grid between their cells, or failing that a line of sight in 3D
    /// (<see cref="HasSight"/>) from head to head.
    /// </summary>
    public bool CanAttack(Combatant a, Combatant t)
    {
        if (_rules?.Flee.Contains(t.Entity) == true)
            return false;
        if (!a.Healing)
        {
            if (State == CombatState.TroubadourPrompt)
                return t == a;
            // Combatant.CanTargetBySide and ActionAllowsTarget (0x80060420): a flask at the other side, a potion at the drinker alone
            // while it would do something.
            if (t.Gone || t.Sheet.IsDead || a.Action == CombatAction.None)
                return false;
            if (a.Action == CombatAction.Flask && a.Potion is { } potion && !Potions.IsFlask(potion))
            {
                if (t != a || Potions.CanDrink(a.Sheet, Game.Party.Inventory.Catalog[Potions.Id(potion)]) != DrinkResult.Drunk)
                    return false;
            }
            else if (a.Hostile == t.Hostile)
            {
                return false;
            }
        }
        else
        {
            if (t.Sheet.IsDead || t.Gone || a.Hostile != t.Hostile || t.Sheet.Hp == t.Sheet.MaxHp)
                return false;
            if (t == a)
                return !a.Flags.HasFlag(CombatantFlags.LayOnHands);
        }
        // The target's cell as it was last read; reaching reads both bodies again, and then the attacker's.
        var (tx, tz) = (ArenaGrid.CellOf(t.Cell.X), ArenaGrid.CellOf(t.Cell.Y));
        if (t.Freed || !Reaches(a, t))
            return false;
        var (ax, az) = (ArenaGrid.CellOf(a.Cell.X), ArenaGrid.CellOf(a.Cell.Y));
        if (Grid.WalkClear(ax, az, tx, tz))
            return true;
        // Head to head. 1.0 adds the target's lift to the attacker's point too (0x800607DC, a pasted block's stack offset left
        // unchanged), so it looks from above the attacker to the target's middle: the port fixes it, the user's choice.
        return HasSight(Head(a), Head(t), a.Slot, t.Slot);
    }

    /// <summary>The top of a body's head: its feet and its entity's height.</summary>
    private static Vector3 Head(Combatant c) =>
        c.Body.Sphere.Position with { Y = c.Body.Sphere.Position.Y - c.Body.ScaledRadius + c.Look.Height * c.Look.Scale };

    /// <summary>
    /// Whether <paramref name="a"/>'s weapon reaches <paramref name="t"/> (<c>Combatant.CanReachWithWeapon</c>): a flask always; a
    /// melee weapon or healing within reach (<see cref="Combatant.IsWithinReach"/>); a ranged one out to its range, from 3 cells
    /// away with no opponent within reach, or one that strikes in melee too from anywhere in its range. Without a weapon, its action
    /// goes.
    /// </summary>
    private bool Reaches(Combatant a, Combatant t)
    {
        if (a.Action == CombatAction.Flask)
            return true;
        if (a.Healing)
            return a.IsWithinReach(this, t);
        if (a.Action is not (CombatAction.Melee or CombatAction.Ranged or CombatAction.MeleeOrRanged))
            return false;
        if (WeaponOf(a) is not { } weapon)
        {
            a.Action = CombatAction.None;
            return false;
        }
        if (a.Action == CombatAction.Melee)
            return a.IsWithinReach(this, t);
        var distance = (uint)Vector2.Distance(a.Cell, t.Cell);
        return a.Action == CombatAction.MeleeOrRanged
            ? distance <= weapon.Range
            : FreeOfOpponents(a) && distance > 2 && distance <= weapon.Range;
    }

    /// <summary>
    /// Whether a line from <paramref name="from"/> to <paramref name="to"/> is clear (<c>ArenaGrid.HasSight</c>, <c>0x800684C4</c>):
    /// a sphere of 0.1 swept along it meets no collision, see-through triangles included, and no other living body in play is
    /// within its own radius of the line, which runs on past either end.
    /// </summary>
    public bool HasSight(Vector3 from, Vector3 to, int fromSlot, int toSlot)
    {
        var delta = to - from;
        var length = MathF.Max(delta.Length(), 1e-6f);
        if (length <= 1e-6f)
            return true;
        var way = delta / length;
        if (Physics.SpherePhysics.Sweep(World, from, to, 0.1f, skipSeeThrough: false, out _, out _))
            return false;
        for (var i = 0; i < InPlay; i++)
        {
            if (i == fromSlot || i == toSlot || _slots[i] is not { } u || u.Sheet.IsDead || u.Gone || u.Freed)
                continue;
            var at = u.Body.Sphere.Position;
            var t = Vector3.Dot(delta, at - from);
            if (t == 0)
                continue;
            var closest = way * (t / length) + from;
            if (Vector3.Distance(closest, at) <= u.Body.Sphere.Radius)
                return false;
        }
        return true;
    }

    /// <summary>
    /// A in a turn the player plays, with the stick centred and R let go (<c>Combat.InputTurn</c>): set A is built again, and with
    /// a candidate, it's aimed at (state 9, the camera on the cursor's target), or for a bow, a thrown weapon or a flask aimed freely
    /// at (state 18, the focus on it). With none, nothing. Whether it did.
    /// </summary>
    public bool Attack()
    {
        if (State != CombatState.Turn || Fade.Fading || Current is not { Controlled: false } c || !_stickCentred || _orbiting)
            return false;
        TargetsA.Rebuild(this, c);
        if (TargetsA.Candidates == 0)
            return false;
        TargetsA.BeginAttack(this, c);
        if (c.Drawn && !c.Healing)
        {
            // TargetSet.BeginAttackFocused.
            State = CombatState.TargetThrown;
            Focus = _slots[TargetsA.Cursor]?.Cell ?? Focus;
            return true;
        }
        State = CombatState.TargetAttack;
        CameraOwner = _slots[TargetsA.Cursor];
        return true;
    }

    /// <summary>
    /// Steps the cursor through set A: in state 9 the stick or the D-pad, left the next (+1) and right the one before (−1)
    /// (<c>Combat.StepAttackCursor</c>, <c>0x80084420</c>), the camera following it; in state 18 C-left and C-right
    /// (<c>TargetSet.StepSpell</c>), the focus going to it. The attacker's body turns to it (<c>Combat.AimAtCursor</c>,
    /// <c>0x80084334</c>). Whether it stepped.
    /// </summary>
    public bool StepTarget(int by)
    {
        if (State == CombatState.TargetSpell && Current is { } caster)
        {
            // Combat.InputSpellTarget (0x800844BC): the spell's cursor through set B, the camera to it, the caster turning to it.
            if (!TargetsB.StepSpellOne(this, caster, by))
                return false;
            if (_slots[TargetsB.Cursor] is { } aimed)
            {
                CameraOwner = aimed;
                var toward = aimed.Cell - caster.Cell;
                if (TargetsB.Most < 2 && aimed != caster && toward.LengthSquared() > 0)
                    _aimTurn = Vector2.Normalize(toward);
            }
            return true;
        }
        if (State is not (CombatState.TargetAttack or CombatState.TargetThrown) || Current is not { } c || TargetsA.Candidates == 0)
            return false;
        TargetsA.MoveCursor(by);
        if (_slots[TargetsA.Cursor] is not { } target)
            return true;
        if (State == CombatState.TargetAttack)
            CameraOwner = target;
        else
            Focus = target.Cell;
        if (TargetsA.Most < 2 && target != c)
        {
            var way = target.Cell - c.Cell;
            if (way.LengthSquared() > 0)
                _aimTurn = Vector2.Normalize(way);
        }
        return true;
    }

    /// <summary>
    /// A while an attack is aimed (<c>Combat.ConfirmAttackTarget</c>, <c>0x80084078</c>), with a target selected: it's the target
    /// (<see cref="Target"/>), set B is emptied and A keeps only its selection, the camera goes back to the attacker, and the
    /// attacker turns to face it (<c>Combatant.FaceCombatant</c>, <c>0x8005FD28</c>, state 6). Whether it did.
    /// </summary>
    public bool Confirm()
    {
        if (Current is { Controlled: false } caster)
        {
            if (State is CombatState.TargetSpell or CombatState.PlaceArea)
                return ConfirmSpell(caster);
            if (State == CombatState.TroubadourPrompt)
            {
                StartTroubadour(caster);
                return true;
            }
        }
        if (State is not (CombatState.TargetAttack or CombatState.TargetThrown) || Current is not { } c || TargetsA.Selected == 0)
            return false;
        Target = TargetsA.SelectedSlots.Last();
        TargetsB.Clear();
        TargetsA.ClearCandidates();
        CameraOwner = c;
        _aimTurn = null;
        if (_slots[Target] is { } t && t != c)
        {
            FaceCombatant(c, t);
        }
        else
        {
            State = CombatState.Facing;
        }
        return true;
    }

    /// <summary>
    /// B, Z or L while an attack is aimed (<c>Combat.ReturnToTurn</c>, <c>0x80083C70</c>): back to the turn, healing put away, the
    /// focus on the combatant, the target sets built again and the camera given back. The body stays turned where it was.
    /// </summary>
    public bool Cancel()
    {
        if (State is not (CombatState.TargetAttack or CombatState.TargetThrown or CombatState.TargetSpell or CombatState.PlaceArea
            or CombatState.TroubadourPrompt) || Current is not { } c)
            return false;
        // From an attack's or a potion's aim the action is put back too (Combatant.RevertAction).
        var wasAiming = State == CombatState.TargetAttack;
        ReturnToTurn(c);
        if (wasAiming)
            c.RevertAction(Game?.Party.Inventory, _rules);
        return true;
    }

    private void ReturnToTurn(Combatant c)
    {
        State = CombatState.Turn;
        // Combatant.RevertAction: the healing modes go (a flask goes back to the held action, session 9).
        c.Flags &= ~(CombatantFlags.LayOnHands | CombatantFlags.HealWithHerbs);
        Focus = c.Cell;
        RebuildTargets(c);
        CameraOwner = c;
        _aimTurn = null;
    }

    /// <summary>
    /// A tick of state 18's free cursor (<c>Combat.MoveAreaCursor</c>, <c>0x8008465C</c>), the stick turned by the camera moving the
    /// focus, kept on the grid and within the weapon's range of the attacker; then set A selects the candidate whose body is
    /// nearest it, within a unit, or none (<c>0x80084A24</c>).
    /// </summary>
    private void MoveAimCursor(PlayerInput input)
    {
        if (Current is not { } c || input.Stick == Vector2.Zero)
            return;
        var look = Camera.LookXZ;
        var way = Facing.Rotate(new Vector2(-input.Stick.X, input.Stick.Y), look.X, look.Y);
        if (way.LengthSquared() > 0)
            way = Vector2.Normalize(way);
        var focus = Focus + way * Cadence.PerTick(AimStep);
        focus = Vector2.Clamp(focus, Vector2.Zero, new Vector2(Grid.Width, Grid.Depth));
        // Combatant.WeaponReach: a flask's is 400, the grid alone.
        var reach = c.HoldsFlask ? FlaskReach : WeaponOf(c)?.Range ?? 0;
        var off = focus - c.Cell;
        if (off.Length() > reach && off.LengthSquared() > 0)
            focus = c.Cell + Vector2.Normalize(off) * reach;
        Focus = focus;
        Combatant? nearest = null;
        var best = AimPick;
        foreach (var t in Combatants)
        {
            if (!TargetsA.IsCandidate(t.Slot))
                continue;
            var distance = Vector2.Distance(t.Cell, focus);
            if (distance <= best)
            {
                best = distance;
                nearest = t;
            }
        }
        foreach (var slot in TargetsA.SelectedSlots.ToList())
            TargetsA.Deselect(slot);
        if (nearest is not null)
            TargetsA.Select(nearest.Slot);
    }

    /// <summary>
    /// State 6 (<c>Combat.StateFacing</c>, <c>0x800221B0</c>): once the attacker's body has turned (state 6's own input snaps it,
    /// <c>Combat.InputFacing</c>, so it takes a frame or two whatever the angle), its facing is the combatant's, and it carries out
    /// its attack on the target (<see cref="ExecuteAction"/>); after the AI's walk, what follows it (<see cref="FleeAdvance"/>). A
    /// spell's target is session 9's; for now its turn ends.
    /// </summary>
    private void StepFacing()
    {
        // A caster that Escape took off the field still casts at the rest of its targets.
        var casting = TargetsA.Selected == 0 && TargetsB.Selected > 0 && Current is { CastMargin: >= 0 };
        if (Current is not { } c || c.Freed && !casting)
        {
            if (Current is { } gone)
                EndTurn(gone);
            return;
        }
        if (_turning)
        {
            // ProcessPlayers lets the turn end after the poll that snapped it.
            _turning = false;
            return;
        }
        c.Body.Heading = c.Facing;
        State = NextState;
        // The AI's walk has ended: what follows it (EnemyAI.FleeAdvance).
        if (NextState == CombatState.AI && c.Ai is { } ai)
        {
            FleeAdvance(ai);
            return;
        }
        if (Target >= 0 && TargetsA.IsSelected(Target) && _slots[Target] is { } t)
        {
            ExecuteAction(c, t);
        }
        else if (Target >= 0 && TargetsA.Selected == 0 && TargetsB.IsSelected(Target) && _slots[Target] is { } spellTarget
            && (c.CastMargin >= 0 || c.Controlled))
        {
            // A spell's target: the cast carried out, while it has come to a margin; a cast that failed or was refused casts at no
            // more targets, but the AI's goes on.
            ExecuteAction(c, spellTarget, spell: true);
        }
        else
        {
            EndTurn(c);
        }
    }

    /// <summary>
    /// The attack carried out (<c>Combatant.ExecuteAction</c>, <c>0x80065F6C</c>): a performance ends; the target is its last; set B
    /// is emptied and the attack resolved (<see cref="ResolveAction"/>); a death is found at once (<see cref="CheckDeath"/>), before
    /// the blow lands; it has spent its moves; the damage waits on the target for the swing to end; the camera glides from the
    /// attacker to the target; and the action plays (state 2), the ring hidden.
    /// </summary>
    internal void ExecuteAction(Combatant a, Combatant t, bool spell = false)
    {
        if (a.Performing)
            a.StopPerforming(this);
        a.LastTarget = t.Slot;
        int damage;
        if (spell)
        {
            TargetsA.ClearSelection();
            damage = CastCurrentSpell(a, t);
        }
        else
        {
            TargetsB.Clear();
            damage = a.Healing ? ResolveHealer(a, t) : ResolveAction(a, t);
        }
        damage = KeelOver(t, damage);
        CheckDeath(t, damage);
        a.Moves = 1;
        if (spell || a.Action != CombatAction.Flask)
            t.Damage = (byte)damage;
        CombatCamera.StartGlide(a.Body, t.Body, a.Look.Height * a.Look.Scale - 0.2f - a.Body.ScaledRadius);
        State = NextState = CombatState.Action;
        ActionActive = true;
        _actionTimer = 0;
    }

    /// <summary>
    /// The attack by the combatant's action (<c>Combatant.ResolveAction</c>, <c>0x80065ACC</c>): melee or ranged, or for a weapon that
    /// does both, melee if the target is within reach as for melee, else ranged; then its body swings
    /// (<see cref="AttackRequest(int)"/>). A flask is session 9's. The damage.
    /// </summary>
    private int ResolveAction(Combatant a, Combatant t)
    {
        AttackOutcome outcome;
        switch (a.Action)
        {
            case CombatAction.Melee:
                outcome = ResolveMelee(a, t);
                break;
            case CombatAction.Ranged:
                outcome = ResolveRanged(a, t);
                break;
            case CombatAction.MeleeOrRanged:
                a.Action = CombatAction.Melee;
                outcome = CanAttack(a, t) ? ResolveMelee(a, t) : ResolveRanged(a, t);
                a.Action = CombatAction.MeleeOrRanged;
                break;
            case CombatAction.Flask:
                // Combatant.ThrowFlask (0x80065600): a potion drunk, or a flask thrown; no swing of the weapon's.
                return ThrowFlask(a, t);
            default:
                return 0;
        }
        LastAttack = outcome;
        if (WeaponOf(a) is { } weapon)
            a.Body.Request = AttackRequest(weapon.Animation);
        return outcome.Damage;
    }

    /// <summary>What the last attack came to.</summary>
    public AttackOutcome? LastAttack { get; private set; }

    /// <summary>
    /// Whether a blow kills (<c>CheckDeath</c>, <c>0x80065E98</c>): one that takes its hit points, but a Cheat Death on it says so
    /// instead, is the "killed" line queued and the death at once (<see cref="Death"/>).
    /// </summary>
    private void CheckDeath(Combatant t, int damage)
    {
        if (t.Sheet.Hp > damage)
            return;
        if (t.Sheet.Effects.Any(e => e.Effects.Any(x => x is CheatDeathEffect)))
        {
            Lines.Queue(Line(CheatsDeathLine, t));
            return;
        }
        Lines.Queue(Line(KilledLine, t));
        Death(t);
    }

    /// <summary>
    /// A combatant dies (<c>Combatant.Death</c>, <c>0x80062080</c>): the ally's died flag is set, whichever side it's on (Sholeh's
    /// 622); what holds it comes off, it leaves the grid, the counts of those able to act fall, a party member's died flags are
    /// written (<see cref="GameState.WriteDeathFlags"/>), a performer stops, and the first enemy's death is noted. It isn't marked
    /// gone: its hit points at 0 say it's dead, and its body goes once it has fallen (<see cref="FreeBody"/>).
    /// </summary>
    internal void Death(Combatant c)
    {
        if (_rules is { } rules && c.Entity == rules.Ally && Game?.Plugins.TryGet<EntityRecord>(c.Entity)?.Member is { } ally)
            Game.Flags.TrySet(ally.Died, true);
        foreach (var held in c.Sheet.Effects.Where(e => e.Effects.Any(x => x is ImmobiliseEffect)).ToList())
            c.Sheet.RemoveEffect(held, c, this);
        Grid.Vacate(ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y), c.Footprint);
        var (all, party, enemies) = ActiveCounts;
        if (c.Hostile)
        {
            ActiveCounts = (all - 1, party, enemies - 1);
            FirstEnemyDied = true;
        }
        else
        {
            ActiveCounts = (all - 1, party - 1, enemies);
            if (c.Party)
                Game?.WriteDeathFlags();
            if (c.Performing)
                c.StopPerforming(this);
        }
    }

    /// <summary>
    /// A body goes from the field (<c>Combat.FreeBody</c>, <c>0x80021C6C</c>): its effects and potions come off, its numbers go, its
    /// portrait gets the skull, or the fled mark (<see cref="Ui.CombatGui.MarkGone"/>), and the body is no more.
    /// </summary>
    internal void FreeBody(Combatant c, bool fled = false)
    {
        c.Sheet.RemoveAllEffects(c, this);
        Fx.ClearForBody(c);
        Numbers.Remove(c.Slot);
        Gui.MarkGone(c.Slot, fled);
        c.Freed = true;
        c.Mark = null;
        c.Body.Field = null;
        if (CameraOwner == c)
            CameraOwner = Current;
    }

    /// <summary>
    /// A tick of the action's timer (<c>Combat.StepActionTimer</c>, <c>0x80021708</c>): whether the attacker's body no longer counts as
    /// playing its action (at an attack's strike, <see cref="AtStrike"/>, or as a cast's or a drink's animation ends) and
    /// <see cref="SwingTicks"/> have passed, when the timer starts again.
    /// </summary>
    private bool SwingEnded()
    {
        _actionTimer++;
        // A caster that Escape took off the field has no swing to wait for (the port's fix; the original waits for its body for ever).
        if (Current is not { } c || !c.Freed && c.Body.Playing != 0 || _actionTimer < SwingTicks)
            return false;
        _actionTimer = 0;
        return true;
    }

    /// <summary>
    /// State 2 (<c>Combat.StateAction</c>, <c>0x80021774</c>): at the strike, while the swing plays on, an arrow or thrown weapon flies
    /// (state 3); otherwise the blow lands (<see cref="Land"/>).
    /// </summary>
    private void StepAction()
    {
        if (!SwingEnded() || Current is not { } a || Target < 0 || _slots[Target] is not { } t)
            return;
        if (t.Freed)
        {
            // Escape took the target off the field: the original's state 2 waits for its body for ever (a softlock); the port goes on
            // to the result, and the cast to its next target, the user's choice.
            State = CombatState.Result;
            Lines.Flush();
            return;
        }
        if (a.Drawn && TargetsA.Selected != 0 && a.Action is CombatAction.Ranged or CombatAction.Flask or CombatAction.MeleeOrRanged
            && StartFlight(a, t))
        {
            State = CombatState.Cast;
            Focus = a.Cell;
            return;
        }
        Land(t);
    }

    /// <summary>
    /// The arrow or thrown weapon leaves (<c>Combat.StartCastCamera</c>, <c>Combatant.LoadWeaponScene</c>): from the attacker's middle,
    /// as the port has no hand to take it from, to the target's; the arrow for a bow, else the weapon's own model, none for one
    /// without. Whether one flies.
    /// </summary>
    private bool StartFlight(Combatant a, Combatant t)
    {
        if (a.HoldsFlask)
        {
            // Combatant.LoadWeaponScene: the flask's scene, to the target's feet, its muzzle number and spin (0x800D2B40).
            var feet = t.Body.Sphere.Position with { Y = t.Body.Sphere.Position.Y - t.Body.Sphere.Radius };
            Flight = new MissileFlight(FlaskScene, Middle(a), feet, FlaskMuzzle, a.Look.Scale, 0, FlaskSpin);
            a.EmptyHand();
            return true;
        }
        if (WeaponOf(a) is not { } weapon || _rules?.Missiles?.FirstOrDefault(m => m.Weapon == weapon.Id) is not { } missile)
            return false;
        if ((missile.Scene ?? weapon.Model) is not { } scene)
            return false;
        Flight = new MissileFlight(scene, Middle(a), Middle(t), missile.Speed, a.Look.Scale, missile.Turn);
        a.EmptyHand();
        return true;
    }

    /// <summary>The middle of a body: its feet and half its entity's height.</summary>
    private static Vector3 Middle(Combatant c) =>
        c.Body.Sphere.Position with { Y = c.Body.Sphere.Position.Y + c.Look.Height * c.Look.Scale * 0.5f - c.Body.Sphere.Radius };

    /// <summary>
    /// State 3 for an attack (<c>Combat.StateCast</c>, <c>0x80021B24</c>): the arrow flies to the target, and the blow lands once it
    /// has arrived and <see cref="SwingTicks"/> have passed since it left, the timer counting through the flight
    /// (<c>Combat.EndCast</c>, <c>0x800219D8</c>).
    /// </summary>
    private void StepFlight()
    {
        _actionTimer++;
        if (Flight is { Landed: false } flight)
        {
            flight.Tick();
            return;
        }
        if (_actionTimer < SwingTicks)
            return;
        _actionTimer = 0;
        Flight = null;
        // Combat.EndCast (0x800219D8): the arrow, flask or thrown weapon is back in the hand.
        Current?.EquipVisuals(_rules);
        if (Target < 0 || _slots[Target] is not { } t)
            return;
        // A flask bursts first (Combatant.FlaskExplosion), then the target's own share lands as a blow.
        if (Current is { Action: CombatAction.Flask } thrower)
            FlaskExplosion(thrower, t, thrower.Damage);
        Land(t);
    }

    /// <summary>
    /// The blow lands: with damage, the target takes it (<c>Combat.ApplyHit</c>, <c>0x80021930</c>, through
    /// <see cref="Character.TakeDamage"/>), recoils and shows the number; with none, an opponent dodges and an ally is helped; one
    /// it killed falls (<c>Combat.PlayDeathAnimation</c>); and the result shows (state 4), the queued line with it
    /// (<see cref="CombatLog.Flush"/>).
    /// </summary>
    internal void Land(Combatant t)
    {
        var body = t.Body;
        if (t.Damage == 0)
        {
            if (Current is { } a && a.Hostile == t.Hostile)
            {
                body.Request = HelpedRequest;
                Numbers.Add(t, t.Healed, healing: true);
            }
            else
            {
                body.Request = DodgeRequest;
            }
        }
        else
        {
            var damage = t.Damage;
            t.Sheet.TakeDamage(damage, t);
            body.Request = HitRequest;
            // The Goblin Ambush's last hit on Alaron keels him over, alive.
            if (KeelsOver(t))
                body.Request = body.Playing = DeathRequest;
            Numbers.Add(t, damage, healing: false);
            t.Damage = t.Healed = 0;
        }
        if (t.Sheet.IsDead)
            body.Request = body.Playing = DeathRequest;
        State = CombatState.Result;
        Lines.Flush();
    }

    /// <summary>
    /// State 4 (<c>Combat.StateResult</c>, <c>0x80021F30</c>): the fallen are taken off (<see cref="RemoveFallen"/>), and once no target
    /// is left, the turn ends.
    /// </summary>
    private void StepResult()
    {
        _actionTimer++;
        RemoveFallen();
        // Sense Aura's popup holds the turn until a press dismisses it (DismissAura).
        if (TargetsA.Selected + TargetsB.Selected == 0 && Current is { } c && Aura is null && State == CombatState.Result)
            EndTurn(c);
    }

    /// <summary>
    /// The fallen taken off (<c>Combat.RemoveFallen</c>, <c>0x80021D2C</c>): once the target's body has played its reaction or its
    /// fall and <see cref="ResultTicks"/> have passed, a dead one's body goes (<see cref="FreeBody"/>), the camera goes back to the
    /// attacker, and the target is deselected.
    /// </summary>
    private void RemoveFallen()
    {
        if (TargetsA.Selected + TargetsB.Selected == 0 || Target < 0 || _slots[Target] is not { } t)
            return;
        if (!t.Freed && (t.Body.Playing != 0 || _actionTimer < ResultTicks))
            return;
        _actionTimer = 0;
        if (t.Sheet.IsDead && !t.Freed)
            FreeBody(t);
        CameraOwner = Current;
        TargetsA.Deselect(Target);
        TargetsB.Deselect(Target);
        // A spell of several targets goes on to the next (state 6).
        if (TargetsA.Selected + TargetsB.Selected > 0 && Current is { } c)
            NextTarget(c);
    }

    /// <summary>
    /// The attacks' requests (12 to 18), whose blow lands partway through (<see cref="AtStrike"/>), and the two whose share is fixed
    /// rather than the entity's (<c>EntityBody.StrikePointRanged</c>, <c>0x8006C884</c>): a bow's (14) and a thrown weapon's (<see cref="ThrowRequest"/>).
    /// </summary>
    public const int FirstAttackRequest = 12, LastAttackRequest = 18, ShotRequest = 14;

    /// <summary>
    /// Whether a body playing an attack is at the frame its blow lands, before its actor steps this tick (<c>ProcessPlayers</c>,
    /// <c>0x80013108</c>–<c>0x80013210</c>): its frame is <c>(u16)(int)(length × share)</c>, the share the entity's
    /// (<see cref="EntityBody.Strike"/>, <c>EntityBody.StrikePoint</c>, <c>0x8006C7FC</c>), or 0.4 for a bow's or a thrown weapon's
    /// (<c>0x800D2CFC</c>). There the body stops counting as playing it (<see cref="AfterBodies"/>), so the action's timer lets the blow
    /// land or the missile leave while the swing plays on.
    /// </summary>
    private static bool AtStrike(Combatant c, IActorMotion animation)
    {
        var playing = c.Body.Playing;
        if (playing is < FirstAttackRequest or > LastAttackRequest)
            return false;
        var share = playing is ShotRequest or ThrowRequest ? EntityBody.DefaultStrike : c.Look.Strike;
        return animation.Frame == (ushort)(int)(animation.StateLength * share);
    }

    /// <summary>
    /// After each tick's bodies have stepped (<c>Combat.AfterFrame</c>, and <c>ProcessPlayers</c>'s part): a body takes the request its
    /// actor entered a state for, or at an attack's strike (<paramref name="strikes"/>, <see cref="AtStrike"/>) the request it has
    /// now; one-shot requests go once they've been asked for a frame, and a body that has fallen to its last frame holds it, idle.
    /// </summary>
    internal void AfterBodies(Combatant c, IActorMotion animation, bool strikes)
    {
        var body = c.Body;
        if (c.Fallen)
            return;
        if (body.Playing == DeathRequest && body.Request != DeathRequest && animation.Frame >= animation.StateLength)
        {
            c.Fallen = true;
            body.Playing = body.Request;
            return;
        }
        // ProcessPlayers (0x800132B4): as the actor enters a state, or at an attack's strike, the body plays what it's asked for now.
        if (animation.Entered || strikes)
            body.Playing = body.Request;
        // Combat.AfterFrame lets a one-shot request go once a frame, so the body's actor and its keys are asked for it through every tick
        // of the frame it's made in: the port's frame of Cadence.FrameTicks.
        if (Array.IndexOf(KeptRequests, body.Request) < 0)
        {
            var held = c.OneShot.Request == body.Request ? c.OneShot.Ticks + 1 : 1;
            if (held >= Cadence.FrameTicks)
            {
                body.Request = Body.Idle;
                held = 0;
            }
            c.OneShot = (body.Request, held);
        }
        else
        {
            c.OneShot = default;
        }
    }

    /// <summary>
    /// The target marks (<c>CombatTargetMarks.Draw</c>, <c>0x80089830</c>, each tick): every combatant selected in set A (set B in
    /// states 10 and 11), in any state, has its light pulse from dark to full and back once a second.
    /// </summary>
    private void StepMarks()
    {
        var set = State is CombatState.TargetSpell or CombatState.PlaceArea ? TargetsB : TargetsA;
        foreach (var c in Combatants)
        {
            if (!c.Freed && set.IsSelected(c.Slot))
            {
                if (c.Mark is null)
                    c.MarkTicks = 0;
                c.MarkTicks++;
                c.Mark = MathF.Abs(MathF.Sin(c.MarkTicks * 0.05236f));
                // Body.SetTint(body, 1.0, f, f, f): the body's ambient goes to the pulse's grey at the next step.
                c.Tint.Set(1.0f, new Vector3(c.Mark.Value));
            }
            else
            {
                // Body.ClearTint(body, 0.0), as the mark's selection ends: back to the sky's ambient at the next step.
                if (c.Mark is not null)
                    c.Tint.Clear(0);
                c.Mark = null;
            }
        }
    }
}
