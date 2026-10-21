using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.Movement;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>
/// A battle's turns (<c>turn.cpp</c> and <c>Combat.InputTurn</c>): a turn's start, moving in the field, skipping, a turn's
/// end, the round's end and the battle's, by turn. docs/systems/combat.md's "A turn" has the original's.
/// </summary>
public sealed partial class Battle
{
    /// <summary>The combat strings' "%s joins the battle" (offset 0x7C), a newcomer's turn's line.</summary>
    public const int JoinsTheBattle = 0x7C;

    /// <summary>The combat strings' performing line (offset 0xD0), a performer's turn's line.</summary>
    public const int KeepsPerforming = 0xD0;

    /// <summary>The combat strings' "%s is victorious" (offset 0x08).</summary>
    public const int Victorious = 0x08;

    /// <summary>The combat strings' party-incapacitated line (offset 0xD4).</summary>
    public const int PartyIncapacitated = 0xD4;

    /// <summary>The fade as a battle is won or lost (<c>ScreenFade.State</c> 1), a sixtieth a tick as it fades in.</summary>
    public const float FadeOutPerTick = FadePerTick;

    /// <summary>The ring of teardrops round the current combatant's field.</summary>
    public CombatMarkers Ring { get; } = new();

    /// <summary>
    /// Whether an action is playing (<c>Combat.ActionActive</c>, <c>0x800DB0CC</c>), from <c>Combatant.ExecuteAction</c> to
    /// the turn's end, which hides the ring. Session 5's attacks set it.
    /// </summary>
    public bool ActionActive { get; internal set; }

    /// <summary>Where the arena chunk's coordinates start, which the combatants' cells are in.</summary>
    public Vector3 Origin => Arena?.Origin ?? Vector3.Zero;

    /// <summary>Whether the stick was centred as the turn last read it, which Z needs to skip and A to attack.</summary>
    private bool _stickCentred = true;

    /// <summary>Whether R was held as the turn last read it, which keeps A from attacking.</summary>
    private bool _orbiting;

    /// <summary>Whether an entity never moves in its field, which is 0 (Pochanargat, <c>0x800E28EB</c>).</summary>
    public bool Rooted(RecordId entity) => _rooted.Contains(entity);

    private IReadOnlyList<RecordId> _rooted = [];

    /// <summary>
    /// Whether no opponent is within reach of <paramref name="combatant"/> (<c>Combatant.IsFreeOfOpponents</c>,
    /// <c>0x8005F1B8</c>): one on the other side, alive and able to act.
    /// </summary>
    public bool FreeOfOpponents(Combatant combatant)
    {
        foreach (var other in Combatants)
        {
            if (other == combatant || other.Sheet.IsDead || !other.Active || other.Hostile == combatant.Hostile)
                continue;
            if (combatant.IsWithinReach(this, other))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The first turn (<c>Combat.StartFirstTurn</c>, <c>0x80022C9C</c>, the end of <c>Combat.Init</c>): its line, the
    /// ring, and the field. It doesn't move the clock on, and the intro (state 31) chooses its state.
    /// </summary>
    private void StartFirstTurn()
    {
        if (Current is not { } first)
            return;
        CameraOwner = first;
        Write(Line(BeginsAction, first));
        Ring.Update(this, first);
        first.PlaceField(this);
        // The target sets, as the first idle poll builds them (Combat.Controls): the red tints of a caster's selected spell.
        if (!first.Controlled)
            RebuildTargets(first);
    }

    /// <summary>
    /// A tick of a turn the player plays (<c>Combat.InputTurn</c>, <c>0x80084DB0</c>, state 1, before the bodies move): one
    /// the AI plays goes to state 8; otherwise R orbits the camera as in trek, the stick drives the body
    /// (<see cref="Body.Drive"/>), and the combatant reads it back (<see cref="Combatant.UpdateFromBody"/>).
    /// </summary>
    private void InputTurn(PlayerInput input)
    {
        if (Current is not { } current)
            return;
        if (current.Controlled)
        {
            State = CombatState.AI;
            return;
        }
        var body = current.Body;
        if (input.Orbit)
            Camera.Orbit(body.Sphere.Position, input.OrbitStick, moving: input.Free && input.Pushed);
        else
            Camera.Follow();
        if (!input.Locked && !input.Pushed)
            Camera.Anchor(body.Sphere.Position);
        body.Drive(input.Move, Camera.LookXZ);
        current.UpdateFromBody(this);
        _stickCentred = input.Stick == Vector2.Zero;
        _orbiting = input.Orbit;
        FollowNearShadow(current);
    }

    /// <summary>
    /// Z or L in a turn the player plays, with the stick centred (<c>Combat.SkipTurn</c>, <c>0x80083E90</c>): the turn
    /// ends at once, with no line. Returns whether it did.
    /// </summary>
    public bool Skip()
    {
        // While an attack is aimed, Z cancels it (Combat.ReturnToTurn).
        if (State is CombatState.TargetAttack or CombatState.TargetThrown or CombatState.TargetSpell or CombatState.PlaceArea
            or CombatState.TroubadourPrompt)
            return Cancel();
        if (State != CombatState.Turn || Fade.Fading || Current is not { Controlled: false } current || !_stickCentred)
            return false;
        EndTurn(current);
        return true;
    }

    /// <summary>
    /// The turn ends (<c>Combatant.EndTurn</c>, <c>0x80061300</c>): the acted flag cleared, the state back to a turn's with
    /// the ring shown again, the body asked to stand, a last target forgotten if it's dead, and the next turn
    /// (<see cref="Advance"/>).
    /// </summary>
    /// <remarks>
    /// The AI's target and path are forgotten (<see cref="EnemyAI.ClearTarget"/>), the cast chosen is put away (its margin −1), a
    /// scroll or wand worn as gear whose charges ran out comes off with its spell no longer selected, the action is put back
    /// (<see cref="Combatant.RevertAction"/>), and a flask in hand stows the weapon (<c>Party.StowHeldItem</c>, <c>0x800763B0</c>).
    /// </remarks>
    public void EndTurn(Combatant combatant)
    {
        // CombatCamera.Reset: a glide stops.
        CombatCamera.Reset();
        Menu = null;
        Aura = null;
        // EnemyAI.ClearTarget, and a walk the AI gave up forgotten.
        combatant.Ai?.ClearTarget();
        combatant.Body.ClearPath();
        combatant.CastMargin = -1;
        combatant.Flags &= ~CombatantFlags.Acted;
        SpendItem(combatant);
        combatant.RevertAction(Game?.Party.Inventory, _rules);
        StowHeldItem(combatant);
        State = CombatState.Turn;
        ActionActive = false;
        if (!combatant.Sheet.IsDead && !combatant.Gone && !combatant.Freed)
            combatant.Body.Request = Body.Idle;
        if (combatant.LastTarget >= 0 && _slots[combatant.LastTarget] is { Sheet.IsDead: true })
            combatant.LastTarget = -1;
        Advance();
    }

    /// <summary>
    /// The next turn (<c>TurnOrder.Advance</c>, <c>0x8006AC4C</c>): unless the battle's won or lost already, Alaron dead
    /// loses it; the last combatant's body leaves its field; then on through the round, building the next at its end
    /// (<see cref="EndRound"/>), each candidate checking for the battle's end first (with a reserve still to come counted
    /// as an enemy), and taking its radius, its place and whether it can act, until one alive, able and in play. Then the
    /// aims are on it (<see cref="FocusOn"/>), every combatant reads its body, its turn begins (<see cref="BeginTurn"/>) and the clock moves on
    /// (<see cref="GameState.AdvanceTurn"/>).
    /// </summary>
    private void Advance()
    {
        if (State is CombatState.Victory or CombatState.Defeat)
            return;
        if (Game.Party.Slots[0] is { IsDead: true })
        {
            Lose();
            return;
        }
        if (Current is { } last && !last.Sheet.IsDead && !last.Gone && !last.Freed)
            last.Body.Field = null;
        Combatant next;
        while (true)
        {
            Order.Now++;
            if (Order.Now >= Order.Turns.Count && EndRound())
                return;
            var counts = CountActive();
            ActiveCounts = counts;
            // Escape took every member able to fight off the field: the party has fled (the port's fix of the original's softlock).
            if (counts.Party == 0 && Combatants.Any(c => !c.Hostile && c.Gone && !c.Sheet.IsDead))
            {
                EndAs(BattleOutcome.Fled, CombatState.LeaveFled);
                return;
            }
            // A reserve still to come counts as an enemy for the battle's end.
            if (ReserveWaits)
                counts = (counts.All + 1, counts.Party, counts.Enemies + 1);
            if (CheckEnd(counts) || Order.Now >= Order.Turns.Count)
                return;
            if (_slots[Order.Turns[Order.Now]] is not { } candidate)
                continue;
            Current = candidate;
            candidate.UpdateRadius(this);
            candidate.SaveStartPosition();
            candidate.UpdateActive(this);
            if (candidate.Sheet.IsDead || !candidate.Active || candidate.Gone)
                continue;
            next = candidate;
            break;
        }
        FocusOn(next);
        // TurnOrder.ClearActions: every combatant reads its body, and the damage and healing waiting to show are cleared.
        foreach (var combatant in Combatants.Where(c => !c.Freed))
        {
            combatant.SyncFromBody(this);
            combatant.Damage = combatant.Healed = 0;
        }
        BeginTurn(next);
        Game.AdvanceTurn();
        // A reserve that fled as it came in says so now, holding the battle (Combat.HoldForMessage).
        if (_reinforcementsFled)
        {
            _reinforcementsFled = false;
            Lines.HoldOn(CombatText(ReinforcementsFleeLine));
        }
    }

    /// <summary>Whether a reserve is still to come (<c>+0x07</c> short of the list's end).</summary>
    private bool ReserveWaits => NextReserve < Math.Min(Enemies.Count, MaxEnemies);

    /// <summary>
    /// The combatants able to act, all, the party's side and the enemies' (<c>TurnOrder.CountActive</c>,
    /// <c>0x8006A880</c>): in play, not gone, able and alive.
    /// </summary>
    private (int All, int Party, int Enemies) CountActive()
    {
        int all = 0, party = 0, enemies = 0;
        foreach (var c in Combatants)
        {
            if (c.Gone || !c.Active || c.Sheet.IsDead)
                continue;
            all++;
            if (c.Hostile)
                enemies++;
            else
                party++;
        }
        return (all, party, enemies);
    }

    /// <summary>
    /// The battle's end, by turn (<c>TurnOrder.CheckEnd</c>, <c>0x8006A97C</c>): none of the party able to act loses it,
    /// and no enemy wins it. Returns whether it ended.
    /// </summary>
    private bool CheckEnd((int All, int Party, int Enemies) counts)
    {
        if (counts.Party == 0)
        {
            Defeat();
            return true;
        }
        if (counts.Enemies == 0)
        {
            Win();
            return true;
        }
        return false;
    }

    /// <summary>
    /// The party has fallen (<c>TurnOrder.Defeat</c>, <c>0x8006A780</c>): state 13, and the battle's lost (<see cref="Lose"/>); then
    /// the party's line, and in a non-lethal battle the speaker's own over it, for good, under the speaker's portrait.
    /// </summary>
    private void Defeat()
    {
        State = CombatState.Defeat;
        Lose();
        if (Current is null)
            return;
        Write(CombatText(PartyIncapacitated));
        if (NonLethal)
            Lines.Lock(CombatText(SparedLine), Rule!.Speaker);
    }

    /// <summary>
    /// The battle is won (<c>TurnOrder.Victory</c>, <c>0x8006A804</c>): the bosses' flags and the rewards (<see cref="TallyRewards"/>)
    /// at once, state 12, the screen fading out, and the victory line but in a battle that keeps it quiet (the Goblin Ambush).
    /// </summary>
    private void Win()
    {
        SetKillFlags();
        TallyRewards();
        State = CombatState.Victory;
        Fade.Out(FadeOutPerTick);
        if (Current is not null && Rule is not { QuietVictory: true })
            Write(CombatText(Victorious));
    }

    /// <summary>
    /// The round's end (<c>TurnOrder.EndRound</c>, <c>0x8006A62C</c>): each performer pays a Stamina point, or stops with
    /// none left; every combatant's moves are forgotten and its effects aged; the toggle flips and the next round is built. Returns whether the
    /// battle ended, which only the Shadow's aura does (<see cref="ShadowAura"/>, first).
    /// </summary>
    /// <remarks>
    /// Every combatant's effects age a round (<see cref="AgeEffects"/>): their damage and healing show over their heads, and one
    /// they kill dies and its body goes at once, without a fall or a line (<c>Entity.ShowAgeResult</c>, <c>0x8007138C</c>). Then the
    /// reserves come in (<see cref="SpawnReserves"/>).
    /// </remarks>
    private bool EndRound()
    {
        if (ShadowAura())
            return true;
        for (var i = 0; i < InPlay; i++)
        {
            if (_slots[i] is not { } c)
                continue;
            if (c.Performing)
            {
                if (c.Sheet.Stats.Modified(Stat.Stamina) <= 0)
                    c.StopPerforming(this);
                else
                    c.Sheet.DrainStamina(1);
            }
            c.Moves = 0;
            c.Flags &= ~CombatantFlags.Moved;
        }
        foreach (var (combatant, ageing) in AgeEffects())
        {
            if (combatant.Freed)
                continue;
            Numbers.Add(combatant, ageing.Damage, healing: false);
            Numbers.Add(combatant, ageing.Healed, healing: true);
            if (ageing.Died)
            {
                Death(combatant);
                FreeBody(combatant);
            }
        }
        SpawnReserves();
        Order.Toggle = !Order.Toggle;
        Order.Build(this, Game.Random.Combat);
        Current = Order.Current(this);
        return false;
    }

    /// <summary>
    /// A turn begins (<c>TurnOrder.BeginTurn</c>, <c>0x8006A9C8</c>): the camera's given to its body, which drops an
    /// orbit, and anchored there; its line, a newcomer's "joins the battle" or a performer's own; it reads its body and
    /// keeps where it starts; the ring round it (which works its radius out again); its field; and a turn's state, the
    /// player's (1) or the AI's (8), as the state to go on to too; and unless it's fleeing, its target sets.
    /// </summary>
    /// <remarks>
    /// The AI checks whether it flees first (<see cref="CheckFlee"/>); a flask or spell carried over is session 9's.
    /// </remarks>
    private void BeginTurn(Combatant combatant)
    {
        CameraOwner = combatant;
        Camera.Follow();
        Camera.Anchor(combatant.Body.Sphere.Position);
        if (combatant.Flags.HasFlag(CombatantFlags.Newcomer))
        {
            combatant.Flags &= ~CombatantFlags.Newcomer;
            Write(Line(combatant.Performing ? KeepsPerforming : JoinsTheBattle, combatant));
        }
        else
        {
            Write(Line(combatant.Performing ? KeepsPerforming : BeginsAction, combatant));
        }
        combatant.SyncFromBody(this);
        combatant.SaveStartPosition();
        Ring.Update(this, combatant);
        combatant.PlaceField(this);
        // EnemyAI.CheckFlee: every AI's turn but the leader's while it lives.
        if (combatant.Ai is { } ai && ai.Flags.HasFlag(AiFlags.Plays))
            CheckFlee(combatant, LeaderGone || combatant.Slot != LeaderSlot);
        // A flask carried over is looked up: gone from the inventory, the hand is emptied.
        if (combatant.HoldsFlask && Game?.Party.Inventory.Has(combatant.Potion!.Id) != true)
        {
            combatant.Action = CombatAction.None;
            combatant.Potion = null;
            combatant.Drawn = false;
            combatant.EquipVisuals(_rules);
        }
        State = NextState = combatant.Controlled ? CombatState.AI : CombatState.Turn;
        _stickCentred = true;
        if (!combatant.Fleeing)
            RebuildTargets(combatant);
    }

    /// <summary>
    /// A spent scroll or wand (<c>Combatant.EndTurn</c>'s, <c>Entity.UnequipGear</c>, <c>0x8006F5C4</c>): a gear slot's whose spell was
    /// selected and has no charges left is gone, used up, and no spell is selected.
    /// </summary>
    private static void SpendItem(Combatant c)
    {
        if (c.Spell.Source != SpellSourceKind.Gear || c.Spell.Index >= c.Sheet.Equipment.Gear.Count
            || c.Sheet.Equipment.Gear[c.Spell.Index] is not { Charges: 0 } item || item.Item.Kind is not (Plugins.Records.ItemKind.Scroll or Plugins.Records.ItemKind.Wand))
            return;
        c.Sheet.Equipment.RemoveGear(c.Spell.Index);
        c.Spell = SpellChoice.None;
    }

    /// <summary>
    /// A flask in hand puts the weapon away (<c>Party.StowHeldItem</c>, <c>0x800763B0</c>): the weapon goes to the party's inventory,
    /// and the member is left without it until it's equipped again.
    /// </summary>
    private void StowHeldItem(Combatant c)
    {
        if (!c.HoldsFlask || c.Sheet.Equipment.Weapon is null || Game is null)
            return;
        if (c.Sheet.Equipment.RemoveWeapon() is { } weapon)
            Game.Party.Inventory.Add(weapon.Item, 1, weapon);
        c.EquipVisuals(_rules);
    }

    /// <summary>A line of the combat strings with a combatant's name.</summary>
    private string Line(int offset, Combatant combatant) =>
        CombatText(offset).Replace("%s", combatant.Sheet.Name, StringComparison.Ordinal);
}
