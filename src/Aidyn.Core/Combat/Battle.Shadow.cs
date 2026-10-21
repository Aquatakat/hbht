using System.Globalization;
using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.Movement;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

// The special battles' rules (gamecombat.cpp's Shadow functions, Combatant.ShadowFightTrick): the Goblin Ambush's hold on Alaron, and
// the Shadow's aura, slowing, vanishing and merging, with the combat record's rules for both (EncounterRule.KeelOver, ShadowRule).
// docs/systems/combat.md's "The special battles" has the original's.
public sealed partial class Battle
{
    // ---- The Goblin Ambush's hold ----

    /// <summary>
    /// Hits the battle's own rules still let its member take before keeling over (<c>Combat.ShadowTurnsLeft</c>, <c>0x800D94CE</c>,
    /// which <c>Combat.Init</c> sets for the Goblin Ambush): <see cref="EncounterRule.KeelOver"/>'s.
    /// </summary>
    public int KeelOverHits { get; private set; }

    /// <summary>
    /// The battle's own hold on a member (<c>Combatant.ShadowFightTrick</c>, <c>0x80065DCC</c>, from <c>Combatant.ExecuteAction</c>
    /// before the death is found, for an attack, a flask thrown or a spell's first target alike): in a battle whose rules have one
    /// (<see cref="EncounterRule.KeelOver"/>, the Goblin Ambush's), a blow that would hurt its member takes 1 hit point instead,
    /// which replaces the queued line; and the last of its hits takes the member's Strength to 0, so it can't act, and at the next
    /// turn's change the party has fallen, though the member lives. Returns the damage.
    /// </summary>
    internal int KeelOver(Combatant t, int damage)
    {
        if (damage == 0 || Rule?.KeelOver is not { } hold || t.Entity != hold.Member)
            return damage;
        LogHpLoss(t, 1);
        // A byte: after the last hit it wraps and counts on down, never 0 again in a battle.
        KeelOverHits = (byte)(KeelOverHits - 1);
        if (KeelOverHits == 0)
        {
            t.Sheet.Stats.ChangeCurrent(Stat.Strength, -t.Sheet.Stats.Modified(Stat.Strength));
            t.UpdateActive(this);
        }
        return 1;
    }

    /// <summary>
    /// Whether a hit on <paramref name="t"/> is the keel-over's own, which plays its fall (<c>Combat.ApplyHit</c>, <c>0x80021930</c>:
    /// slot 0, while the count is at 0).
    /// </summary>
    internal bool KeelsOver(Combatant t) => Rule?.KeelOver is not null && t.Slot == 0 && KeelOverHits == 0;

    // ---- The Shadow ----

    /// <summary>The combat strings' "%s caught in the vortex of the Shadow" (offset 0x80).</summary>
    public const int VortexLine = 0x80;

    /// <summary>The combat strings' "The Shadow dissipates" (offset 0xF0).</summary>
    public const int DissipatesLine = 0xF0;

    /// <summary>Frames state 29 counts its line held to, from where the round count stood (<c>Combat.StateShadowDissipates</c>).</summary>
    public const int VanishFrames = 60;

    /// <summary>The Shadow's rules (<see cref="CombatRecord.Shadow"/>), or null in a plugin without them.</summary>
    public ShadowRule? ShadowRules => _rules.Shadow;

    /// <summary>
    /// The Shadow, while it's in the battle (<c>Combat.ShadowSlot</c>, <c>0x800D94CC</c>: set as its combatant is made, cleared as its
    /// body is freed).
    /// </summary>
    public Combatant? Shadow => ShadowRules is { } rules ? Combatants.FirstOrDefault(c => c.Entity == rules.Entity && !c.Freed) : null;

    /// <summary>Whom the Shadow merges with, while in the battle (<c>Combat.AlaronSlot</c>, <c>0x800D94CD</c>).</summary>
    public Combatant? Merger => ShadowRules is { } rules ? Combatants.FirstOrDefault(c => c.Entity == rules.Merges && !c.Freed) : null;

    /// <summary>
    /// Round ends the Shadow has stayed without the party carrying its item, then state 29's frames (<c>+0x52C8</c>, which the
    /// reinforcements' hold shares in the original).
    /// </summary>
    public int ShadowRounds { get; private set; }

    /// <summary>
    /// Whether the party carries the Shadow's item, the Horn of Kynon (<c>Combat.PartyHasHorn</c>, <c>0x80023AA4</c>, through
    /// <c>Party.HasItem</c>): in the inventory or among any member's things.
    /// </summary>
    public bool PartyHasShadowItem => ShadowRules is { } rules && Game.Party.HasItem(rules.Item);

    /// <summary>
    /// The Shadow made while the party carries its item has its base Dexterity raised to the rule's (<c>Combatant.Init</c>,
    /// <c>0x8005EAE0</c>), so it has the turns to reach its merger.
    /// </summary>
    private void QuickenShadow(Character sheet)
    {
        if (ShadowRules is not { } rules || sheet.Entity != rules.Entity || !PartyHasShadowItem)
            return;
        var raise = rules.Dexterity - sheet.Stats.Base(Stat.Dexterity);
        if (raise > 0)
            sheet.Stats.Train(Stat.Dexterity, raise);
    }

    /// <summary>
    /// Whether <paramref name="combatant"/> is near the Shadow, which holds its field to 1 (<c>Combat.NearShadow</c>, <c>0x80023F34</c>):
    /// within its radius of it, and for the Shadow anyone within its radius (<c>Combat.AnyoneWithin10</c>, <c>0x80023E9C</c>), the dead
    /// and gone too.
    /// </summary>
    public bool NearShadow(Combatant combatant)
    {
        if (Shadow is not { } shadow)
            return false;
        var radius = ShadowRules!.Radius;
        if (combatant != shadow)
            return Within(combatant, shadow, radius);
        return Combatants.Any(other => other != shadow && Within(other, shadow, radius));
    }

    /// <summary>
    /// Whether two combatants' places are within <paramref name="radius"/> (<c>Combat.WithinRadius</c>, <c>0x80023D44</c>): each axis
    /// at most the radius apart, and the distance across the ground strictly under it.
    /// </summary>
    private static bool Within(Combatant a, Combatant b, float radius)
    {
        var dx = a.Cell.X - b.Cell.X;
        var dz = a.Cell.Y - b.Cell.Y;
        return Math.Abs(dx) <= radius && Math.Abs(dz) <= radius && dx * dx + dz * dz < radius * radius;
    }

    /// <summary>
    /// A combatant near the Shadow is caught in its vortex, as its radius is worked out (<c>Combatant.UpdateRadius</c>): the current one,
    /// but the Shadow, says so, shown and queued.
    /// </summary>
    internal void CaughtInVortex(Combatant combatant)
    {
        if (combatant == Current && combatant != Shadow)
            SayBoth(VortexLine, combatant.Sheet.Name);
    }

    /// <summary>
    /// In the Shadow's battle, the field follows a combatant walking near it (<c>Combat.MovePlayerBody</c>, <c>0x80084CE0</c>): unless
    /// its radius is 1 already, its radius is worked out again and its field and ring laid round where its body is now, the ring as
    /// though its turn began there. In every other battle, nothing; the AI's walk never does it.
    /// </summary>
    private void FollowNearShadow(Combatant current)
    {
        if (current.Radius == 1 || Shadow is null || !NearShadow(current))
            return;
        current.UpdateRadius(this);
        current.Body.Field = (current.Body.Sphere.Position, current.Radius);
        var start = current.TurnStart;
        current.TurnStart = current.Cell;
        Ring.Update(this, current);
        current.TurnStart = start;
    }

    /// <summary>
    /// The Shadow walks at its merger while the party carries its item (<c>EnemyAI.RunStyle</c>, <c>0x80059628</c>): its goal is
    /// combat slot 0's cell, whatever its style chose.
    /// </summary>
    private (int X, int Z) ShadowGoal(EnemyAI ai, (int X, int Z) goal) =>
        ai.Combatant == Shadow && PartyHasShadowItem && _slots[0] is { } first ? Cell8(first) : goal;

    /// <summary>
    /// A path is cut where it comes within the Shadow's radius of it (<c>EnemyAI.ShadowSmoothPath</c>, <c>0x8005A734</c>, from
    /// <c>EnemyAI.PlanPath</c>): unless the walker's field is 1 (near the Shadow, or the Shadow itself), the first leg that crosses the
    /// circle ends where it does (<see cref="ClipToCircle"/>), the point rounded to its cell.
    /// </summary>
    private void SmoothPathRoundShadow(EnemyAI ai)
    {
        if (ai.Combatant.Radius == 1 || Shadow is not { } shadow)
            return;
        var from = new Vector2((sbyte)_path[ai.PathPlace].X, (sbyte)_path[ai.PathPlace].Z);
        for (var i = ai.PathPlace + 1; i < ai.PathLength; i++)
        {
            var to = new Vector2((sbyte)_path[i].X, (sbyte)_path[i].Z);
            if (ClipToCircle(shadow.Cell, ShadowRules!.Radius, from, to) is { } edge)
            {
                ai.PathLength = (byte)(i + 1);
                _path[i] = (ArenaGrid.CellOf(edge.X) & 0xFF, ArenaGrid.CellOf(edge.Y) & 0xFF);
                return;
            }
            from = to;
        }
    }

    /// <summary>
    /// The Shadow's aura at a round's end, first (<c>Combat.ShadowAura</c>, <c>0x80023AD0</c>, from <c>TurnOrder.EndRound</c>). Without
    /// the item it stays so many round ends and vanishes at the next, before its aura. Then each other combatant in play, in slot
    /// order, not gone or dead or of chaos, under the radius from it takes the radius less the distance, squared without the item
    /// (a float, truncated), shown over its head; a blow that would take every hit point it has ends the battle instead, as the
    /// Shadow reaching its merger would (<see cref="ShadowDissipates"/>). Returns whether the battle ended.
    /// </summary>
    internal bool ShadowAura()
    {
        if (Shadow is not { } shadow)
            return false;
        var rules = ShadowRules!;
        var item = PartyHasShadowItem;
        if (!item)
        {
            if (ShadowRounds >= rules.VanishRounds)
            {
                ShadowDissipates();
                return true;
            }
            ShadowRounds++;
        }
        for (var i = 0; i < InPlay; i++)
        {
            if (_slots[i] is not { } c || c == shadow || c.Gone || c.Sheet.IsDead || c.Sheet.Record.Category == EntityCategory.Chaos)
                continue;
            var distance = Vector2.Distance(c.Cell, shadow.Cell);
            if (rules.Radius <= distance)
                continue;
            var aura = rules.Radius - distance;
            if (!item)
                aura *= aura;
            if (c.Sheet.Hp <= aura)
            {
                ShadowDissipates();
                return true;
            }
            var damage = (short)((int)aura & 0xFFFF);
            // Character.TakeDamage ends a performance first, even for nothing.
            if (c.Performing)
                c.StopPerforming(this);
            c.Sheet.TakeDamage(damage, c);
            if (!c.Freed)
                Numbers.Add(c, damage, healing: false);
        }
        return false;
    }

    /// <summary>
    /// The Shadow's battle ends (<c>Combat.ShadowDissipates</c>, <c>0x800239B4</c>): while the party carries its item, it merges (state
    /// 17); without, it vanishes: state 29, "The Shadow dissipates" shown, and every body back to its idle.
    /// </summary>
    private void ShadowDissipates()
    {
        if (PartyHasShadowItem)
        {
            State = CombatState.ShadowMerge;
            return;
        }
        State = CombatState.ShadowVanish;
        Write(CombatText(DissipatesLine));
        _vanishFrame.Reset();
        foreach (var c in Combatants.Where(c => !c.Freed))
            c.Body.Request = Body.Idle;
    }

    /// <summary>The ticks of state 29 so far, counted to frames.</summary>
    private readonly FrameGate _vanishFrame = new(Cadence.FrameTicks);

    /// <summary>
    /// State 29 (<c>Combat.StateShadowDissipates</c>, <c>0x800229D8</c>): the field goes on being drawn, the count going on a frame at a
    /// time from where the rounds left it, to 60; then the screen fades out (state 30).
    /// </summary>
    private void StepShadowVanish()
    {
        if (!_vanishFrame.Tick())
            return;
        if (++ShadowRounds < VanishFrames)
            return;
        ShadowRounds = 0;
        Fade.Out(FadeOutPerTick);
        State = CombatState.ShadowVanishLeave;
    }

    /// <summary>
    /// State 30 once the screen has faded out (<c>Combat.StateLeaveAfterShadow</c>, <c>0x80022A2C</c>): the battle leaves for trek where
    /// it began, with nothing won, no flag set and the monster party not remembered.
    /// </summary>
    private void StepShadowVanishLeave()
    {
        EndAs(BattleOutcome.Vanished, CombatState.Leave);
    }

    /// <summary>
    /// The Shadow reaching its merger (<c>Combat.CheckShadowMerge</c>, <c>0x80023884</c>, the original's
    /// <c>alaron_shadow_merge_attempt</c>, at every drawn frame's end but in states 17, 29 and 30): once the two bodies' centres are no
    /// farther apart than their scaled radii and the rule's reach, with the item it merges (state 17), and without it vanishes. Whose
    /// turn it is, who walked and whether the merger lives don't matter.
    /// </summary>
    private void CheckShadowMerge()
    {
        if (State is CombatState.ShadowMerge or CombatState.ShadowVanish or CombatState.ShadowVanishLeave
            or CombatState.Pause or CombatState.PauseRequested || Over)
            return;
        if (Shadow is not { } shadow || Merger is not { } merger)
            return;
        var reach = shadow.Body.ScaledRadius + merger.Body.ScaledRadius + ShadowRules!.MergeReach;
        if (reach < Vector3.Distance(shadow.Body.Sphere.Position, merger.Body.Sphere.Position))
            return;
        ShadowDissipates();
    }

    /// <summary>
    /// State 17 (<c>Combat.StateShadowMerge</c>, <c>0x800226F8</c>), its first frame: the merge's boost (<see cref="MergeShadow"/>), and
    /// the battle leaves for the Shadow's cinematic (game mode <c>0xE</c>), which plays once the battle has gone, and then trek where
    /// the battle began. No rewards, no encounter or boss flags.
    /// </summary>
    private void StepShadowMerge()
    {
        if (Shadow is { } shadow && Game.Party.Slots[0] is { } leader)
            MergeShadow(leader, shadow.Sheet);
        EndAs(BattleOutcome.Merged, CombatState.Leave);
    }

    /// <summary>
    /// The merge's boost (<c>Combat.MergeShadow</c>, <c>0x800675C0</c>), to the party's first member from the Shadow's sheet as it stands:
    /// each stat raised as training does (<see cref="CharacterStats.Train"/>: trained, base, current and modified, up to the base cap),
    /// protection raised; each skill and weapon skill the Shadow has (its modified level not 0) raised by the difference of their base
    /// levels (each read to 10 at most), or by the least if that isn't positive, as training does; the shield skill the same way but only
    /// when the Shadow has none, the original's test turned round; each of the Shadow's spells raised the same way to at most the cap, or
    /// learnt at the Shadow's own level; then the flag.
    /// </summary>
    internal void MergeShadow(Character member, Character shadow)
    {
        var boost = ShadowRules!.Boost;
        foreach (var gain in boost.Stats)
            member.Stats.Train((Stat)int.Parse(gain.Stat.Key, CultureInfo.InvariantCulture), gain.Amount);
        member.Protection = (byte)(member.Protection + boost.Protection);
        int Raise(int to, int from)
        {
            var by = (sbyte)(to - from);
            return by < 1 ? boost.Least : by;
        }
        static int BaseLevel(Character c, SkillId skill) => (sbyte)Math.Min(c.Skills.RawBase(skill), 10);
        foreach (var skill in SkillId.All)
        {
            var has = skill.IsShield ? shadow.Skills.Modified(skill) == 0 : shadow.Skills.Modified(skill) != 0;
            if (has)
                member.Skills.Train(skill, Raise(BaseLevel(shadow, skill), BaseLevel(member, skill)));
        }
        foreach (var spell in shadow.Spells)
        {
            if (member.FindSpell(spell.Spell.Id) is { } known)
                known.Level = Math.Min(known.Level + Raise(spell.Level, known.Level), boost.SpellCap);
            else
                member.LearnSpell(spell.Spell, spell.Level);
        }
        Game.Flags.TrySet(boost.Flag, true);
    }
}
