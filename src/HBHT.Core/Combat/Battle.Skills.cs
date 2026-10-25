using System.Globalization;
using HBHT.Core.Characters;
using HBHT.Core.Items;
using HBHT.Core.Magic;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Combat;

// Potions, flasks and skills in a turn: drinking (Combatant.DrinkPotion), throwing a flask and its blast (ThrowFlask, FlaskExplosion),
// the Troubadour's performance (StartTroubadour, states 25 and 26), the Healer's Lay On Hands and Heal With Herbs (ResolveHealer), and
// Sense Aura's popup. docs/systems/combat.md's "Spells, potions and skills in a turn" has the original's.
public sealed partial class Battle
{
    /// <summary>What a flask flies as (Borg5 <c>0x34C5</c>, <c>Combatant.LoadWeaponScene</c>), its muzzle number and its spin a tick (<c>0x800D2B40</c>).</summary>
    public static readonly RecordId FlaskScene = RecordId.Hex(RecordTypes.Scene, 0x34C5);

    public const float FlaskMuzzle = 4, FlaskSpin = 8;

    /// <summary>How far a flask's free cursor may go from the thrower (<c>Combatant.WeaponReach</c>): the grid alone.</summary>
    public const float FlaskReach = 400;

    /// <summary>How long a flask's corrosion lasts (<c>0x9E340</c>): the battle.</summary>
    public const uint CorrosionTicks = 648000;

    /// <summary>The throw's request, the thrower's body (15).</summary>
    public const int ThrowRequest = 15;

    private const int HealsLine = 0x10, GainsLine = 0x14, GainsOneLine = 0x18, HealingFailsLine = 0x1C, PotionOnLine = 0x3C,
        PotionAppliedLine = 0x40, PotionFailsLine = 0x44, FullStaminaLine = 0x48, ThrowMissesLine = 0x4C, PerformingLine = 0xC0,
        TooWeakLine = 0xC4, FailsToPerformLine = 0xC8, PerformsLine = 0xCC;

    /// <summary>The cell a flask lands at (<c>+0x5444</c>, <c>+0x5445</c>), its blast's centre; a miss leaves the last.</summary>
    public (int X, int Z) BlastCell { get; internal set; }

    /// <summary>Sense Aura's popup (<c>+0x4FB8</c>), its lines, while it's up; any press closes it and ends the turn.</summary>
    public string? Aura { get; private set; }

    // ---- Potions and flasks ----

    /// <summary>
    /// A potion drunk or a flask thrown (<c>Combatant.ThrowFlask</c>, <c>0x80065600</c>): a potion is drunk (<see cref="DrinkPotion"/>);
    /// a flask's chance (<see cref="ThrowChance"/>) is rolled, Warrior turning the roll; a hit draws every flask's dice, shows and queues
    /// the attack, lands at the target's cell and keeps its damage on the thrower for the blast, and the Thrown skill may train; a miss
    /// says so and trains on half; either way the body throws (request 15) and the flask is used up. No damage is the action's own.
    /// </summary>
    private int ThrowFlask(Combatant a, Combatant t)
    {
        if (a.Potion is not { } potion)
            return 0;
        if (!Potions.IsFlask(potion))
        {
            DrinkPotion(a, potion);
            return 0;
        }
        var random = Game.Random.Combat;
        var distance = (byte)(float)System.Numerics.Vector2.Distance(a.Cell, t.Cell);
        var chance = ThrowChance(a, t, distance);
        var roll = Casting.AdjustRoll(random.RollD(1, 100), chance, a.Sheet.Skills.Modified(Skill.Warrior), random);
        var skill = a.Sheet.Skills.Modified(WeaponClass.Thrown);
        var points = skill < 6 ? (byte)((8 - skill) * (8 - skill)) : 5;
        if (chance <= roll)
        {
            // Combatant.ThrowMiss (0x80065120).
            GainExperience(a, points >> 1, WeaponClass.Thrown);
            Animate(a, ThrowRequest);
            ConsumeFlask(a, potion);
            Write(Line2(AttacksLine, a, t));
            Lines.Queue(Line(ThrowMissesLine, a));
            return 0;
        }
        var damage = FlaskDamage(potion.Id);
        Write(Line2(AttacksLine, a, t));
        Lines.Queue(Line2(AttacksLine, a, t));
        BlastCell = (ArenaGrid.CellOf(t.Cell.X), ArenaGrid.CellOf(t.Cell.Y));
        a.Damage = (byte)damage;
        GainExperience(a, points, WeaponClass.Thrown);
        Animate(a, ThrowRequest);
        ConsumeFlask(a, potion);
        return 0;
    }

    /// <summary>
    /// A flask's chance (<c>Combatant.ThrowChance</c>, <c>0x80064EFC</c>): the thrower's modified Intelligence and Dexterity, base Level,
    /// 6 × Thrown and 2 × Warrior, and 40, less half the target's shield defence, its modified Dexterity and base Level; less a point a
    /// cell past 6 away (less 5); 20 more from behind; 10 more at a target with no Stamina, 20 less at one that has moved or acted, 25
    /// less having moved; the night's 30 and fog's 10 or a storm's 20, and the distance again in those (<c>Combat.WeatherPenalty</c>,
    /// <c>0x80062D78</c>); the aspect and the Troubadour; at least 5.
    /// </summary>
    public int ThrowChance(Combatant a, Combatant t, int distance)
    {
        var (sheet, target) = (a.Sheet, t.Sheet);
        var s = (short)(sheet.Stats.Modified(Stat.Intelligence) + sheet.Stats.Modified(Stat.Dexterity) + sheet.Stats.Base(Stat.Level));
        s = (short)(s + sheet.Skills.Modified(WeaponClass.Thrown) * 6 + sheet.Skills.Modified(Skill.Warrior) * 2 + 40);
        s = (short)(s - (((ShieldDefence(target) >> 1) & 0xFF) + target.Stats.Modified(Stat.Dexterity) + target.Stats.Base(Stat.Level)));
        if (distance > 6)
            s = (short)(s - (distance - 5));
        int c = (short)(s + 20 * (IsBehind(a, t) ? 1 : 0));
        if (target.Stats.Modified(Stat.Stamina) == 0)
            c = (short)(c + 10);
        if (t.Moves != 0)
            c = (short)(c - 20);
        if (a.Flags.HasFlag(CombatantFlags.Moved))
            c = (short)(c - 25);
        // Combat.WeatherPenalty: the night, then fog or a storm (World+4) with the distance once more.
        if (PartOfDay == Calendar.PartOfDay.Night)
            c = (short)(c - 30);
        if (WeatherClass is 1 or 2)
        {
            c = (short)(c - (WeatherClass == 2 ? 20 : 10));
            if (distance > 6)
                c = (short)(c - (short)(distance - 5));
        }
        c = AspectScale(sheet, c);
        c = Casting.TroubadourScale((short)c, TroubadourBonus, a.Hostile);
        return Math.Max(LeastChance, (int)(short)c);
    }

    /// <summary>
    /// A flask's damage (<c>Combatant.FlaskDamage</c>, <c>0x80065270</c>): the original rolls every flask's dice, in their order, and
    /// takes its own, so the port rolls the blast of every potion that's thrown, in the records' order, and keeps this one's.
    /// </summary>
    private int FlaskDamage(RecordId potion)
    {
        var random = Game.Random.Combat;
        var mine = 0;
        foreach (var flask in Game.Rules.Magic.Potions.Where(Potions.IsFlask))
        {
            if (flask.Effects?.OfType<BlastEffect>().FirstOrDefault() is not { } blast)
                continue;
            var rolled = EffectLanding.Roll(blast.Dice, random);
            if (flask.Id == potion)
                mine = rolled;
        }
        return mine;
    }

    /// <summary>
    /// The flask used up (<c>Combatant.ConsumeFlask</c>, <c>0x80064AB0</c>): one from the party's inventory, unless the AI plays it. The
    /// original puts the action back at once, which empties the hand as the last of a stack is thrown, so it never flies or bursts; the
    /// port leaves it in hand until the turn's end puts it back (<see cref="EndTurn"/>), so the last one flies and bursts as the rest
    /// do: the user's choice.
    /// </summary>
    private void ConsumeFlask(Combatant a, Item potion)
    {
        if (a.Controlled)
            return;
        Game.Party.Inventory.Take(potion.Id);
    }

    /// <summary>
    /// A flask lands (<c>Combatant.FlaskExplosion</c>, <c>0x800652F8</c>): its effects land on the target through
    /// <see cref="EffectLanding"/> at <see cref="Potions.Margin"/>, its blast and corrosion bursting about the cell it landed at with the
    /// damage its throw rolled (<see cref="IMagicHost.Burst"/>), and any damage of its own adding to the target's share.
    /// </summary>
    internal void FlaskExplosion(Combatant a, Combatant t, int damage)
    {
        if (a.Potion is not { } potion || Game.Rules.Magic.Potion(potion.Id) is not { } record)
            return;
        var landing = new Landing(record.Id, MagicSchool.None, 0, Potions.Margin, new EffectTarget(a.Sheet, a, this),
            new EffectTarget(t.Sheet, t, this), Game.Random.Combat) { RecordMargin = 0, Centre = BlastCell, Blast = damage };
        var result = EffectLanding.Land(record.Effects ?? [], record.Duration, landing);
        if (result.Dice is { } dice)
            t.Damage += Game.Random.Combat.RollD(dice & 0xFF, 6);
    }

    /// <summary>
    /// A blast bursts (<c>Combatant.FlaskExplosion</c>, <c>0x800652F8</c>): about the centre, each living combatant in play at a squared
    /// distance d (kept in a byte, as the original's, and at least 1) within the damage takes the damage / d, from hit points, or
    /// Stamina with Sleep Gas, others left with at least 1; the target's own loss is queued and its death checked; each it hit
    /// recoils, and the others' numbers show at once. Acid's corrosion draws a radius first, and those within it lose protection for
    /// the battle (radius / d) instead of taking damage.
    /// </summary>
    void IMagicHost.Burst(Combatant t, RecordId source, int damage, bool stamina, Dice? corrode, (int X, int Z)? centre)
    {
        var potion = Game.Rules.Magic.Potion(source);
        var radius = corrode is null ? 0 : (byte)(Game.Random.Combat.RollD(corrode.Count, corrode.Sides) + corrode.Flat);
        var (cx, cz) = centre ?? (ArenaGrid.CellOf(t.Cell.X), ArenaGrid.CellOf(t.Cell.Y));
        for (var slot = 0; slot < InPlay; slot++)
        {
            if (_slots[slot] is not { } e || e.Sheet.IsDead)
                continue;
            var (ex, ez) = (ArenaGrid.CellOf(e.Cell.X), ArenaGrid.CellOf(e.Cell.Y));
            var squared = (byte)(short)((ex - cx) * (ex - cx) + (ez - cz) * (ez - cz));
            var d = squared > 1 ? squared : 1;
            if (corrode is not null && d <= radius)
            {
                e.Sheet.AddEffect(source, [new ProtectionEffect(new Magnitude(PerLevel: -1))], radius / d, MagicSchool.None, 0,
                    CorrosionTicks, e, this, null);
                if (potion is not null)
                    AttachPotionVisual(e, potion);
                continue;
            }
            if (d > damage)
                continue;
            var n = damage / d;
            if (e == t)
                LogHpLoss(t, n);
            if (stamina)
            {
                if (e != t && e.Sheet.Stats.Modified(Stat.Stamina) <= n)
                    n = (short)(e.Sheet.Stats.Modified(Stat.Stamina) - 1);
                if (n >= 1)
                {
                    e.Sheet.DrainStamina(n);
                    if (potion is not null)
                        AttachPotionVisual(e, potion);
                }
            }
            else
            {
                if (e != t && e.Sheet.Hp <= n)
                    n = (short)(e.Sheet.Hp - 1);
                if (n > 0)
                {
                    e.Sheet.TakeDamage(n, e);
                    if (potion is not null)
                        AttachPotionVisual(e, potion);
                }
            }
            e.Damage = (byte)n;
            if (e == t)
                CheckDeath(e, n);
            if (e.Freed)
                continue;
            Animate(e, HitRequest);
            if (e != t)
            {
                Numbers.Add(e, e.Damage, healing: false);
                e.Damage = 0;
            }
        }
    }

    /// <summary>
    /// A potion drunk in a battle (<c>Combatant.DrinkPotion</c>, <c>0x80064C84</c>), through <see cref="Potions.Drink"/>: a heal says what
    /// it gained, shown at once, or that it failed; Stamina says it's full; the rest that the potion took. Unless it failed, the
    /// potion's visual plays on the body, which plays the cast's request (<see cref="AttachPotionVisual"/>), and the action goes back
    /// to what it was. No number shows.
    /// </summary>
    private void DrinkPotion(Combatant a, Item item)
    {
        var effects = item.Gear?.Effects ?? [];
        var before = a.Sheet.Hp;
        var drunk = Potions.Drink(Game.Party, a.Sheet, item, Game.Random.Combat, a, this);
        a.RevertAction(Game.Party.Inventory, _rules);
        if (drunk != DrinkResult.Drunk)
        {
            SayBoth(PotionOnLine, a.Sheet.Name);
            return;
        }
        var record = Game.Rules.Magic.Potion(item.Id);
        if (effects.Any(e => e is HealEffect))
        {
            var gained = a.Sheet.Hp - before;
            if (gained <= 0)
            {
                SayBoth(PotionFailsLine, a.Sheet.Name);
                return;
            }
            // Combatant.HealByPotion: a heal that took shows the potion's visual (and the body's cast); one that failed shows nothing.
            if (record is not null)
                AttachPotionVisual(a, record);
            // LogHpGain, then FlushQueuedLog: shown and queued.
            var line = Gains(a, gained);
            Write(line);
            Lines.Queue(line);
            return;
        }
        if (record is not null)
            AttachPotionVisual(a, record);
        SayBoth(effects.Any(e => e is StaminaEffect { ToBase: true }) ? FullStaminaLine : PotionAppliedLine, a.Sheet.Name);
    }

    /// <summary>"%s gains %d hit points", or "gains 1 hit point" (<c>Combatant.LogHpGain</c>, <c>0x8006723C</c>).</summary>
    private string Gains(Combatant t, int amount) => amount == 1
        ? Line(GainsOneLine, t)
        : Line(GainsLine, t).Replace("%d", amount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    // ---- The Troubadour ----

    /// <summary>
    /// A in the Troubadour's prompt (<c>Combatant.StartTroubadour</c>, <c>0x800662F4</c>): with no performance in the battle and
    /// Stamina left, (Troubadour × 5 + Intelligence) × 2 against a d100; made, it performs: the battle's bonus is the margin's step
    /// (at least 1), 3 Stamina are spent and the skill may train; failed, it may train on half. Either way the body plays and the turn
    /// rests (state 26). Too weak, or with a performance already going, the line says so and the turn goes on.
    /// </summary>
    internal void StartTroubadour(Combatant c)
    {
        var sheet = c.Sheet;
        if (TroubadourBonus != 0)
        {
            var performer = Enumerable.Range(0, Math.Min(4, SlotCount)).Select(i => _slots[i]).FirstOrDefault(p => p is { Performing: true });
            SayBoth(performer is null ? FailsToPerformLine : PerformingLine, (performer ?? c).Sheet.Name);
            State = CombatState.Turn;
            return;
        }
        if (sheet.Stats.Modified(Stat.Stamina) == 0)
        {
            SayBoth(TooWeakLine, sheet.Name);
            State = CombatState.Turn;
            return;
        }
        var random = Game.Random.Combat;
        var skill = sheet.Skills.Modified(Skill.Troubadour);
        var chance = (short)((skill * 5 + sheet.Stats.Modified(Stat.Intelligence)) * 2);
        var roll = (short)random.RollD(1, 100);
        var points = skill < 6 ? (byte)((8 - skill) * (8 - skill)) : 8;
        if (chance < roll)
        {
            SayBoth(FailsToPerformLine, sheet.Name);
            GainPerformance(c, points >> 1);
        }
        else
        {
            var step = Party.SkillCheckStep((short)(chance - roll));
            c.Flags = (c.Flags | CombatantFlags.Performing) & ~(CombatantFlags.LayOnHands | CombatantFlags.HealWithHerbs);
            TroubadourBonus = step < 2 ? 1 : step;
            c.HoldNotes(true);
            sheet.DrainStamina(3);
            SayBoth(PerformsLine, sheet.Name);
            GainPerformance(c, points);
        }
        Animate(c, CastRequest);
        // Combat.EnterRestState (0x80023FEC): 120 ticks, then the turn ends.
        State = CombatState.Perform;
        _rest = RestTicks;
    }

    // Combatant.GainSmallExperience (0x8006620C): a d100 at or under a tenth of the points trains the Troubadour.
    private void GainPerformance(Combatant c, int points)
    {
        if (Game.Random.Combat.RollD(1, 100) <= (byte)points / 10)
            c.Sheet.Skills.Train(Skill.Troubadour, 1);
    }

    // ---- The Healer ----

    /// <summary>
    /// Lay On Hands or Heal With Herbs at an ally (<c>Combatant.ResolveHealer</c>, <c>0x8006591C</c>, through the Party tab's own rules,
    /// <see cref="Crafting.LayOnHands"/> and <see cref="Crafting.HerbHeal"/>): healed, the healer's body plays and the lines say who
    /// healed whom and what it gained, the number waiting on the target as healing; barred in the Shadow's battle, that the Shadow
    /// prevents it; else that the healing failed. No damage.
    /// </summary>
    internal int ResolveHealer(Combatant a, Combatant t)
    {
        var before = t.Sheet.Hp;
        var result = a.Flags.HasFlag(CombatantFlags.LayOnHands)
            ? Crafting.LayOnHands(a.Sheet, t.Sheet, HealingBarred)
            : Crafting.HerbHeal(Game.Party, a.Sheet, t.Sheet, Game.Random.Combat, HealingBarred);
        var gained = t.Sheet.Hp - before;
        if (result.Outcome == HealOutcome.Healed && gained > 0)
        {
            Animate(a, CastRequest);
            SayBoth(HealsLine, a.Sheet.Name, t.Sheet.Name);
            Lines.Queue(Gains(t, gained));
            t.Healed = (byte)gained;
        }
        else if (result.Outcome == HealOutcome.Barred)
        {
            // Party.ShowHealResult shows the Common line, which 1.0's ResolveHealer overwrites at once with "fails to perform
            // healing"; the port keeps it, shown and queued, the user's choice.
            Write(CommonText(HealingBarredLine));
            Lines.Queue(Lines.Shown);
        }
        else
        {
            SayBoth(HealingFailsLine, a.Sheet.Name);
        }
        t.Damage = 0;
        return 0;
    }

    /// <summary>The Common strings' "The Shadow's presence prevents all healing." (line 437, <c>DAT_800DA0D8</c> + 0x6D4).</summary>
    public const int HealingBarredLine = 437;

    // ---- The pause menu's Party tab ----

    /// <summary>
    /// A flask taken in hand from the pause menu's Party tab (<c>Party.EquipFlaskInCombat</c>, <c>0x800753F0</c>): it's the action, and
    /// the turn ends as the menu closes (state 19).
    /// </summary>
    internal void HoldFlaskFromMenu(Item potion)
    {
        if (State != CombatState.Pause || Current is not { Controlled: false } c)
            return;
        c.HoldPotion(potion, this);
        NextState = CombatState.EndTurn;
    }

    /// <summary>
    /// The weapon changed from the pause menu's Party tab (<c>Combatant.AfterEquipChange</c>, <c>0x800610C0</c>): a performance stops,
    /// healing is put away, the action is the weapon's, it has moved, and a spell of the weapon's is no longer selected; equipped, the
    /// turn ends as the menu closes (state 19). Taken off, the member is left without one, and the turn goes on.
    /// </summary>
    internal void WeaponChangedFromMenu(bool endsTurn)
    {
        if (State != CombatState.Pause || Current is not { Controlled: false } c)
            return;
        if (c.Performing)
            c.StopPerforming(this);
        c.Flags &= ~(CombatantFlags.LayOnHands | CombatantFlags.HealWithHerbs);
        c.SetDefaultAction(_rules);
        c.Potion = null;
        c.Moves = 1;
        if (c.Spell.Source == SpellSourceKind.Weapon)
            c.Spell = SpellChoice.None;
        if (endsTurn)
            NextState = CombatState.EndTurn;
    }

    // ---- Sense Aura ----

    /// <summary>
    /// Sense Aura's popup (<c>Entity.SenseAura</c>, <c>0x80070070</c>), by the cast's level: the target's name; from 1 whether it leads
    /// the enemies; from 3 its maximum hit points; from 5 its Level; from 7 its resistances, immune, resistant or vulnerable; from 9 its
    /// protection. A line each, in the original's printf forms.
    /// </summary>
    private void ShowAura(Combatant target, int level)
    {
        var text = _rules.Text?.Aura;
        var lines = new List<string> { target.Sheet.Name };
        if (level >= 1)
            lines.Add(target.Slot == LeaderSlot && !LeaderGone ? text?.Leader ?? "" : text?.Follower ?? "");
        if (level >= 3)
            lines.Add(Printf(text?.Hitpoints ?? "", target.Sheet.MaxHp));
        if (level >= 5)
            lines.Add(Printf(text?.Level ?? "", target.Sheet.Stats.Base(Stat.Level)));
        if (level >= 7)
        {
            foreach (var resistance in target.Sheet.Record.Resistances.Take(2).Where(r => r.Element != Element.None))
            {
                var form = resistance.Damage <= 0 ? text?.Immune : resistance.Damage < (text?.ResistantBelow ?? 2) ? text?.Resistant : text?.Vulnerable;
                var name = text?.Elements.GetValueOrDefault(resistance.Element) ?? resistance.Element.ToString();
                lines.Add((form ?? "").Replace("%s", name, StringComparison.Ordinal));
            }
        }
        if (level >= 9)
            lines.Add(Printf(text?.Protection ?? "", Protection(target.Sheet, behind: false)));
        Aura = string.Join('\n', lines);
    }

    // The popup's numbers, the original's %d and %lu.
    private static string Printf(string form, int value) =>
        form.Replace("%lu", value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("%d", value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>
    /// Any press while Sense Aura's popup is up (<c>Combat.DismissPopup</c>, <c>0x80083E30</c>, before every state's input): it closes,
    /// and the turn ends at once, cutting the action short. Whether there was one.
    /// </summary>
    public bool DismissAura()
    {
        if (Aura is null)
            return false;
        Aura = null;
        if (Current is { } c && State is CombatState.Action or CombatState.Result)
            EndTurn(c);
        return true;
    }
}
