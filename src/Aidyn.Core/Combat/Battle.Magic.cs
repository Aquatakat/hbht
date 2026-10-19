using System.Numerics;
using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.Magic;
using Aidyn.Core.Rng;
using Aidyn.Core.Sky;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

// Spells in a battle (docs/systems/magic.md): the battle as the effects' host, a cast at its targets (Combatant.CastCurrentSpell,
// 0x80065C08), a weapon's spell as it strikes, and a round's ageing. Sessions 3, 5 and 9 call these from the states.
public sealed partial class Battle : IMagicHost
{
    private const int CastsLine = 0x6C, CastOnLine = 0x70, ResistsLine = 0x68, FailsToCastLine = 0x64, MissingComponentLine = 0x5C,
        NotEnoughStaminaLine = 0x60, UnavailableLine = 0x58, DispelFailsLine = 0xE4, BanishedLine = 0xE8, FleesLine = 0x78;

    private PartOfDay? _heldPartOfDay;

    private MagicRules Magic => Game.Rules.Magic ?? throw new InvalidOperationException("No plugin has the magic record (aidyn:magic/0).");

    /// <summary>The weather class Wind's end set (<c>World+4</c>), for combat's penalties; null if no Wind has ended.</summary>
    public int? WeatherClass { get; private set; }

    /// <summary>Whether Detect Moon Phase or Detect Sun Phase was cast: the HUD shows the moon's or the sun's phase.</summary>
    public (bool Moon, bool Sun) PhasesShown { get; private set; }

    /// <summary>The combatant whose details Sense Aura shows, at the cast's level; session 9 draws the popup.</summary>
    public (Combatant Target, int Level)? AuraShown { get; private set; }

    IEnumerable<Combatant> IMagicHost.Combatants => Combatants;

    /// <summary>The battle's part of the day (<c>World+2</c>): what Darkness or Light holds, or the world's.</summary>
    public PartOfDay PartOfDay => _heldPartOfDay ?? Game.Weather?.PartOfDay ?? Game.Clock.Date.PartOfDay;

    void IMagicHost.HoldPartOfDay(PartOfDay partOfDay) => _heldPartOfDay = partOfDay;

    void IMagicHost.ReleasePartOfDay() => _heldPartOfDay = null;

    /// <summary>Whether Darkness or Light holds the part of the day (<c>World+5</c>), so it doesn't change and the weather doesn't roll.</summary>
    public bool PartOfDayHeld => _heldPartOfDay is not null;

    int IMagicHost.MoonPhase => SkyObjects.MoonPhase(Game.Clock.Time).Phase;

    void IMagicHost.SetWeatherClass(int weatherClass) => WeatherClass = weatherClass;

    /// <summary>The Troubadour's bonus (<c>+0x13</c>), which session 9 sets.</summary>
    public int TroubadourBonus { get; internal set; }

    bool IMagicHost.ReservesLeft => NextReserve < Enemies.Count;

    Combatant? IMagicHost.CombatantOf(Character sheet) => Combatants.FirstOrDefault(c => c.Sheet == sheet);

    bool IMagicHost.Flee(Combatant combatant)
    {
        if (!CanFlee)
            return false;
        // Combatant.Leave (0x80061480): it leaves as a fleeing enemy does, a party member too, its body fading and emptied at once.
        Leave(combatant);
        return true;
    }

    bool IMagicHost.Teleport(Combatant caster)
    {
        if (_placed is not { } point || Grid.Inside((int)point.X, (int)point.Y) && (Grid[(int)point.X, (int)point.Y] & ArenaGrid.Taken) != 0)
            return false;
        caster.Cell = point;
        return true;
    }

    /// <summary>Whether the Shadow is in the battle (<see cref="Shadow"/>), which bars Teleportation.</summary>
    public bool TeleportBarred => Shadow is not null;

    /// <summary>
    /// Whether healing is barred: the Shadow is in the battle, so potions drunk heal nothing (<c>Entity.HealPercent</c>,
    /// <c>0x800715DC</c>) and the Healer's two are refused (<c>Party.LayOnHands</c>, <c>Party.HealWithHerbs</c>). The pause menu's Party
    /// tab, game mode 3, isn't barred.
    /// </summary>
    public bool HealingBarred => Shadow is not null;

    void IMagicHost.SenseAura(Combatant target, int level)
    {
        AuraShown = (target, level);
        ShowAura(target, level);
    }

    void IMagicHost.RevealPhase(bool sun) => PhasesShown = sun ? PhasesShown with { Sun = true } : PhasesShown with { Moon = true };

    void IMagicHost.Log(string line) => Write(line);

    private Vector2? _placed;

    /// <summary>
    /// A combatant casts a spell at its targets (<c>Combatant.CastCurrentSpell</c>, <c>0x80065C08</c>, each through
    /// <c>Combatant.CastSpell</c>, <c>0x800648CC</c>): for each, the spell must be available against it; the first target the
    /// cast reaches prepares it, taking the reagent and the Stamina and rolling, or spending an item's charge (a failed roll leaves
    /// the next target to try again, with another reagent, as the original does); then the target may resist, and the spell
    /// takes effect. Damage isn't dealt: the outcome holds it, for the state that shows the numbers to deal it.
    /// </summary>
    /// <param name="placed">The cell a placed area or Teleportation was put at (state 11).</param>
    public IReadOnlyList<CastOutcome> Cast(Combatant caster, SpellSource source, IReadOnlyList<Combatant> targets, Vector2? placed = null)
    {
        ReachMagic();
        _placed = placed;
        var outcomes = new List<CastOutcome>();
        var prepared = false;
        var castMargin = -1;
        foreach (var target in targets)
            outcomes.Add(CastAt(caster, source, target, ref prepared, ref castMargin));
        return outcomes;
    }

    /// <summary>
    /// A weapon's spell as it strikes (<c>Combatant.WeaponSpellProc</c>, <c>0x800627D0</c>), after a hit: if the spell may be cast at
    /// the target, it is, as an item's spell at the enchantment's level, with a margin of a d100's step. Its damage adds to the hit's.
    /// </summary>
    public CastOutcome? Strike(Combatant attacker, Combatant target)
    {
        if (attacker.Sheet.Equipment.Weapon is not { } weapon || weapon.Item.Bonuses.Enchantment is not { } enchantment
            || Magic.Spell(enchantment.Spell) is not { } spell || !Casting.CanTarget(attacker, spell, target))
            return null;
        ReachMagic();
        // Combatant.WeaponSpellProc (0x800627D0) casts it as any spell: PrepareCast (0x8006406C) keeps its margin on the attacker
        // (+0x3C, 0x80064184), unless the attacker has already acted this turn.
        var prepared = attacker.Flags.HasFlag(CombatantFlags.Acted);
        var castMargin = prepared ? attacker.CastMargin : -1;
        var outcome = CastAt(attacker, new SpellSource(spell, Item: weapon, ItemLevel: enchantment.Level) { Proc = true }, target, ref prepared, ref castMargin);
        attacker.CastMargin = castMargin;
        return outcome;
    }

    /// <summary>
    /// A round's end for every combatant's effects (<c>TurnOrder.EndRound</c>, <c>0x8006A62C</c>, through <c>Entity.Age</c>): each
    /// combatant in play ages its effects a round. <see cref="EndRound"/> calls it; session 5 deals with any who died of them.
    /// </summary>
    public IReadOnlyList<(Combatant Combatant, EffectAgeing Ageing)> AgeEffects()
    {
        ReachMagic();
        var aged = new List<(Combatant, EffectAgeing)>();
        foreach (var combatant in Combatants.Where(c => !c.Flags.HasFlag(CombatantFlags.Gone)))
        {
            var ageing = combatant.Sheet.AgeRound(combatant, this, Game.Clock.Date.PartOfDay, Game.Random.Combat);
            // Combatant.Death (0x80062080) takes off what holds it; the rest of a death is session 5's.
            if (ageing.Died)
            {
                foreach (var held in combatant.Sheet.Effects.Where(e => e.Effects.Any(x => x is ImmobiliseEffect)).ToList())
                    combatant.Sheet.RemoveEffect(held, combatant, this);
            }
            aged.Add((combatant, ageing));
        }
        return aged;
    }

    // Every combatant reaches the battle for the effects that end as it dies.
    private void ReachMagic()
    {
        foreach (var combatant in Combatants)
            combatant.MagicHost = this;
    }

    private CastOutcome CastAt(Combatant caster, SpellSource source, Combatant target, ref bool prepared, ref int castMargin)
    {
        var spell = source.Spell;
        var sheet = caster.Sheet;
        var level = Casting.Level(sheet, source);
        // Combatant.CheckSpellAvailable (0x800635B8).
        if (spell.TargetType == SpellTargetType.Field)
        {
            SayBoth(UnavailableLine);
            return new(target, CastResult.NotCast, CastRefusal.Unavailable);
        }
        var refusal = Casting.Requirement(spell, target.Sheet);
        if (refusal == CastRefusal.None && !Casting.TimeAllows(spell, PartOfDay))
            refusal = CastRefusal.WrongTime;
        if (refusal != CastRefusal.None)
        {
            // Combatant.LogMissingComponent (0x800638C8): "%s" with the requirement's line, for a player's cast.
            if (!caster.Controlled)
                SayBoth(SpellRequirementLine, CommonText(CastMessagesLine + RequirementMessage(refusal)));
            return new(target, CastResult.NotCast, refusal);
        }
        if (!prepared)
        {
            refusal = source.IsItem ? PrepareItem(caster, source, ref castMargin) : PrepareKnown(caster, source, level, ref castMargin);
            if (refusal != CastRefusal.None)
                return new(target, CastResult.NotCast, refusal);
            prepared = true;
            caster.Flags |= CombatantFlags.Acted;
        }
        // Combatant.ResistRoll (0x80064298): Teleportation fails while the Shadow is here.
        if (spell.Effects?.Any(e => e is TeleportEffect) == true && TeleportBarred)
        {
            SayBoth(FailsToCastLine, sheet.Name);
            return new(target, CastResult.Resisted, CastRefusal.Barred);
        }
        var margin = castMargin & 0xFF;
        if (Casting.Resistable(spell, caster, target))
        {
            if (Casting.Resist(caster, spell, target, castMargin, margin, Magic.Record.Resist, Game.Random.Combat) is not { } left)
            {
                Say(CastsLine, sheet.Name, spell.Name);
                SayQueued(ResistsLine, target.Sheet.Name);
                return new(target, CastResult.Resisted, Margin: margin);
            }
            margin = left;
        }
        var result = Apply(caster, spell, level, margin, target);
        if (result < 0)
            return new(target, CastResult.Effect, Margin: margin);
        // (margin + the dice) d6, through the resistances.
        var dice = Game.Random.Combat.RollD((margin + result) & 0xFF, 6);
        var elements = spell.Effects?.OfType<DamageEffect>().SelectMany(d => d.Elements).ToList() ?? [];
        var damage = Casting.Damage(dice, Casting.ResistanceMultiplier(spell, elements, target.Sheet));
        return new(target, CastResult.Damage, Damage: damage, Margin: margin);
    }

    // AttemptCast (0x80063F7C): the reagent, the cost, the chance and the roll (ResolveCastRoll, 0x80063DF4).
    private CastRefusal PrepareKnown(Combatant caster, SpellSource source, int level, ref int castMargin)
    {
        var spell = source.Spell;
        var sheet = caster.Sheet;
        var random = Game.Random.Combat;
        // CheckReagent (0x80063BFC): the party's own pay one from the inventory.
        var reagent = Magic.ReagentItem(spell.Reagent);
        var pays = !caster.Flags.HasFlag(CombatantFlags.FromEnemyList) && !caster.Controlled && reagent is not null;
        if (pays && !Game.Party.Inventory.Take(reagent!.Value))
        {
            SayBoth(MissingComponentLine);
            return CastRefusal.NoReagent;
        }
        if (!sheet.Equipment.PayStamina(Casting.Cost(spell, level)))
        {
            SayBoth(NotEnoughStaminaLine);
            return CastRefusal.NoStamina;
        }
        var chance = Casting.Chance(sheet, level, Magic.Record.Chance, PartOfDay, ((IMagicHost)this).MoonPhase, TroubadourBonus, caster.Hostile);
        var roll = Casting.AdjustRoll(random.RollD(1, 100), chance, sheet.Skills.Modified(Skill.Wizard), random);
        var known = source.Known!;
        var improvement = known.Level < 4 ? (byte)((5 - known.Level) * (5 - known.Level)) : 3;
        var success = roll < chance;
        if (success)
        {
            castMargin = Party.SkillCheckStep((short)(chance - roll));
        }
        else
        {
            if (pays)
                Game.Party.Inventory.Add(reagent!.Value);
            SayBoth(FailsToCastLine, sheet.Name);
            improvement >>= 1;
            castMargin = -1;
        }
        // A low spell may rise a level, cast or not.
        if (random.RollD(1, 100) <= (byte)(int)(improvement * Magic.Record.Improvement))
            known.Level = Math.Min(known.Level + 1, Magic.Record.MaxLevel);
        return success ? CastRefusal.None : CastRefusal.Failed;
    }

    // PrepareCast's item spells (0x8006406C): a charge, which only a wand or scroll worn as gear spends, and a d100's step.
    private CastRefusal PrepareItem(Combatant caster, SpellSource source, ref int castMargin)
    {
        var item = source.Item!;
        if (!source.Proc)
        {
            if (item.Charges is not > 0)
            {
                SayBoth(FailsToCastLine, caster.Sheet.Name);
                castMargin = -2;
                return CastRefusal.NoCharges;
            }
            if (item.Item.Kind is ItemKind.Wand or ItemKind.Scroll && caster.Sheet.Equipment.Gear.Contains(item))
                item.Charges--;
        }
        castMargin = Party.SkillCheckStep((short)Game.Random.Combat.RollD(1, 100));
        return CastRefusal.None;
    }

    // Combatant.ApplySpellEffect (0x800646E0): -1 for an effect, or the dice over the margin for damage.
    private int Apply(Combatant caster, SpellRecord spell, int level, int margin, Combatant target)
    {
        var sameSide = caster.Hostile == target.Hostile;
        var effects = (spell.Effects ?? []).Where(e => e.For switch
        {
            EffectFor.Allies => sameSide,
            EffectFor.Opponents => !sameSide,
            _ => true,
        }).ToList();
        var random = Game.Random.Combat;
        if (effects.OfType<BanishEffect>().Any())
        {
            SayCasts(caster, spell, target);
            // QueueLog2: the third line, which takes the queued one's place as it's shown.
            var banished = Format(CombatText(BanishedLine), [target.Sheet.Name]);
            _log.Add(banished);
            Lines.QueueThird(banished);
            return target.Sheet.Hp;
        }
        if (effects.OfType<DispelEffect>().ToList() is [_, ..] dispels)
        {
            if (dispels.All(d => !Dispel(target, d.School, margin)))
            {
                Say(CastsLine, caster.Sheet.Name, spell.Name);
                SayQueued(DispelFailsLine, spell.Name);
            }
            else
            {
                SayCasts(caster, spell, target);
            }
            return -1;
        }
        if (effects.Any(e => e is CharmEffect or ImmobiliseEffect) && TooManyDisabled(caster))
        {
            // The resists line, and 0 for the dice: the original's blocked charm or hold still rolls the margin's d6.
            Say(CastsLine, caster.Sheet.Name, spell.Name);
            SayQueued(ResistsLine, target.Sheet.Name);
            return 0;
        }
        var damage = effects.OfType<DamageEffect>().FirstOrDefault();
        var host = (IMagicHost)this;
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case CureEffect cure:
                    foreach (var cured in target.Sheet.Effects.Where(e => cure.Spells.Contains(e.Source)
                                 && (!cure.BeatsMargin || e.Margin < (byte)margin)).ToList())
                        target.Sheet.RemoveEffect(cured, target, this);
                    break;
                case FleeEffect:
                    host.Flee(target);
                    break;
                case TeleportEffect:
                    host.Teleport(caster);
                    break;
                case SenseAuraEffect:
                    host.SenseAura(target, level);
                    break;
                case RevealPhaseEffect phase:
                    host.RevealPhase(phase.Sun);
                    break;
            }
        }
        var lasting = effects.Where(e => EffectRules.Lasting(e) && e is not StaminaEffect { ToCaster: true }).ToList();
        if (spell.Duration is { } duration)
        {
            var timer = duration.At(level);
            if (lasting.Count > 0)
                target.Sheet.AddEffect(spell.Id, lasting, level, spell.School, margin, timer, target, this, random);
            if (effects.OfType<StaminaEffect>().Where(s => s.ToCaster).ToList() is [_, ..] toCaster)
                caster.Sheet.AddEffect(spell.Id, toCaster, level, spell.School, 0, timer, caster, this, random);
        }
        SayCasts(caster, spell, target);
        return damage is null ? -1 : damage.Dice.At(level);
    }

    // Combatant.CheckDisableLimit (0x80063A00): the party's side may not charm or hold another while more than the most are, and
    // reserves are still to come. The enemies' side has no limit.
    private bool TooManyDisabled(Combatant caster)
    {
        if (caster.Hostile || !((IMagicHost)this).ReservesLeft)
            return false;
        var disabled = Slots.Skip(PartySize).OfType<Combatant>().Count(c => !c.Sheet.IsDead && (!c.Hostile || c.IsImmobilised));
        return disabled > Magic.Record.MostDisabled;
    }

    // Entity.Dispel (0x80071CD8): every effect of the school comes off, if the margin times their number reaches their margins.
    private bool Dispel(Combatant target, MagicSchool school, int margin)
    {
        var effects = target.Sheet.Effects.Where(e => e.School == school).ToList();
        if (effects.Count == 0 || (uint)(byte)margin * (uint)effects.Count < (uint)effects.Sum(e => e.Margin))
            return false;
        foreach (var effect in effects)
            target.Sheet.RemoveEffect(effect, target, this);
        return true;
    }

    // Combatant.LogCasts (0x80063718): "%s casts %s" shown, then "%s cast on %s" queued, which shows as the action's state ends.
    private void SayCasts(Combatant caster, SpellRecord spell, Combatant target)
    {
        Say(CastsLine, caster.Sheet.Name, spell.Name);
        SayQueued(CastOnLine, spell.Name, target.Sheet.Name);
    }

    // The requirement refusals' place in Spell.CastMessages (Casting.Requirement's codes, 0x800712C0, and TimeAllows', 0x80071260).
    private static int RequirementMessage(CastRefusal refusal) => refusal switch
    {
        CastRefusal.NeedsTrueName => 7,
        CastRefusal.TooLarge => 8,
        CastRefusal.NeedsSolar => 9,
        CastRefusal.NeedsLunar => 10,
        CastRefusal.WrongTime => 3,
        _ => 1,
    };

    private void Say(int offset, params string[] values) => Write(Format(CombatText(offset), values));
}
