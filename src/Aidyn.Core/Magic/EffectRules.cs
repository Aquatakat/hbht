using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using Aidyn.Core.Combat;
using Aidyn.Core.Rng;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Magic;

/// <summary>Whom effects land on: a character, its combatant if it's in a battle, and the battle.</summary>
internal readonly record struct EffectTarget(Character Sheet, Combatant? Combatant, IMagicHost? Host);

/// <summary>
/// What each effect type does as it starts, each round it lasts and as it ends: the original's switches by spell kind in
/// <c>Entity.AddEffect</c> (<c>0x800702F0</c>), <c>Entity.AgeEffects</c> (<c>0x80070A3C</c>) and <c>Entity.UndoEffect</c>
/// (<c>0x800707EC</c>), with the kinds that share code made one type (ADR 0020).
/// </summary>
internal static class EffectRules
{
    /// <summary>
    /// Whether an effect leaves something on its target that lasts, so a spell or potion with a duration leaves a record for it.
    /// The others happen once, as the cast lands.
    /// </summary>
    public static bool Lasting(Effect effect) => effect is StatEffect or SkillEffect or ProtectionEffect or WardEffect
        or DamagePerRoundEffect or PoisonEffect or HealPerRoundEffect or ImmobiliseEffect or CharmEffect or FieldRadiusEffect
        or MoraleEffect or StaminaEffect or WeatherEffect or PartOfDayEffect or CheatDeathEffect or ReflectEffect or DrainStatEffect;

    /// <summary>Whether an effect at <paramref name="power"/> is a debuff (the record's <c>+0x04</c>, by kind in <c>0x800702F0</c>).</summary>
    public static bool Harmful(Effect effect, int power) => effect switch
    {
        StatEffect s => s.Amount.At(power) < 0,
        FieldRadiusEffect r => r.Amount.At(power) < 0,
        MoraleEffect m => m.Amount.At(power) < 0,
        StaminaEffect s => !s.ToCaster && s.Amount.At(power) < 0,
        ProtectionEffect p => p.Amount.At(power) < 0,
        CharmEffect or ImmobiliseEffect or DamagePerRoundEffect or PoisonEffect or DrainStatEffect => true,
        _ => false,
    };

    /// <summary>Whether the effect refuses its target, so nothing goes on: the Shadow held (<c>0x800702F0</c> returns <c>0xFFFF</c>).</summary>
    public static bool Refuses(Effect effect, EffectTarget target) =>
        effect is ImmobiliseEffect { Immune: { } immune } && target.Combatant is not null && immune.Contains(target.Sheet.Entity);

    /// <summary>An effect starting on its target, at the record's power.</summary>
    public static void Start(ActiveEffect record, int index, EffectTarget target, IRandom random)
    {
        var sheet = target.Sheet;
        var power = record.Power;
        switch (record.Effects[index])
        {
            case StatEffect s:
                sheet.AddStat(StatId.FromRecord(s.Stat), s.Amount.At(power));
                break;
            case SkillEffect s:
                sheet.Skills.Modify(SkillId.FromRecord(s.Skill), s.Amount.At(power));
                break;
            case ProtectionEffect p:
                sheet.Protection = (byte)(sheet.Protection + p.Amount.At(power));
                break;
            case ImmobiliseEffect when target.Combatant is { } held:
                held.Flags &= ~CombatantFlags.Active;
                break;
            case CharmEffect when target.Combatant is { } charmed:
                charmed.Flags ^= CombatantFlags.Hostile;
                break;
            case MoraleEffect m when target.Combatant is { Morale: { } morale } shaken:
            {
                // The AI record's +6, by no more than it has; the record keeps what it took (+1).
                var amount = m.Amount.At(power);
                var taken = amount < 0 ? -Math.Min(-amount, morale) : amount;
                shaken.Morale = morale + taken;
                shaken.MoraleCheckDue = true;
                record.Amounts[index] = taken;
                break;
            }
            case StaminaEffect s:
                StartStamina(s, power, sheet);
                break;
            case PartOfDayEffect d when target.Host is { } host:
                // Entity.SetLight: the other's records come off everyone, then the part of the day is held.
                foreach (var combatant in host.Combatants)
                {
                    foreach (var other in combatant.Sheet.Effects.Where(e => e.Effects.OfType<PartOfDayEffect>().Any(o => o.Night != d.Night)).ToList())
                        combatant.Sheet.RemoveEffect(other, combatant, host);
                }
                host.HoldPartOfDay(d.Night ? PartOfDay.Night : PartOfDay.Midday);
                break;
            case ReflectEffect r when target.Combatant is { } mirrored:
                mirrored.Mirror = (byte)(mirrored.Mirror + r.Amount.At(power));
                break;
            case DrainStatEffect d:
            {
                // 0x80071B0C: a stat drawn, then the dice, off the base and modified values, unbounded.
                var stat = StatId.FromRecord(d.Stats[(int)random.RollD(1, d.Stats.Count) - 1]);
                var amount = (byte)(random.RollD(d.Dice.Count, d.Dice.Sides) + d.Dice.Flat);
                sheet.Stats.ChangeBase(stat, -amount);
                record.Amounts[index] = amount;
                record.DrainedStat = (int)stat;
                record.Margin = (int)stat;
                break;
            }
        }
    }

    // Tap Stamina's halves (0x800702F0's case 33, Entity.AddStaminaEffect 0x80072158), Wind's gain and the Stamina potion.
    private static void StartStamina(StaminaEffect s, int power, Character sheet)
    {
        var amount = s.Amount.At(power);
        if (s.RaisesMaximum)
        {
            sheet.AddStat(Stat.Stamina, amount);
        }
        else if (s.ToBase)
        {
            sheet.RestoreStamina();
        }
        else if (amount < 0)
        {
            sheet.Stats.ChangeCurrent(Stat.Stamina, -Math.Min(-amount, sheet.Stats.Modified(Stat.Stamina)));
        }
        else
        {
            var room = sheet.Stats.Base(Stat.Stamina) - sheet.Stats.Modified(Stat.Stamina);
            sheet.Stats.ChangeCurrent(Stat.Stamina, Math.Min(amount, room));
        }
    }

    /// <summary>An effect ending, undoing what it did (<c>Entity.UndoEffect</c>, <c>0x800707EC</c>).</summary>
    public static void End(ActiveEffect record, int index, EffectTarget target)
    {
        var sheet = target.Sheet;
        var power = record.Power;
        switch (record.Effects[index])
        {
            case StatEffect s:
                sheet.SubStat(StatId.FromRecord(s.Stat), s.Amount.At(power));
                break;
            case SkillEffect s:
                sheet.Skills.Modify(SkillId.FromRecord(s.Skill), -s.Amount.At(power));
                break;
            case ProtectionEffect p:
                sheet.Protection = (byte)(sheet.Protection - p.Amount.At(power) + (p.Leaves?.At(power) ?? 0));
                break;
            case ImmobiliseEffect when target.Combatant is { } held:
                held.Flags |= CombatantFlags.Active;
                break;
            case CharmEffect when target.Combatant is { } charmed:
                charmed.Flags = charmed.Flags.HasFlag(CombatantFlags.FromEnemyList)
                    ? charmed.Flags | CombatantFlags.Hostile
                    : charmed.Flags & ~CombatantFlags.Hostile;
                break;
            case MoraleEffect when target.Combatant is { Morale: { } morale } shaken:
                shaken.Morale = morale - record.Amounts[index];
                break;
            case WeatherEffect w:
                target.Host?.SetWeatherClass(w.AtEnd);
                break;
            case PartOfDayEffect:
                target.Host?.ReleasePartOfDay();
                break;
            case DrainStatEffect:
                sheet.SubStat((Stat)record.DrainedStat, -record.Amounts[index]);
                break;
        }
    }

    /// <summary>
    /// What an effect does each round it lasts, and each time trek ages it (<c>Entity.AgeEffects</c>): its damage and healing are
    /// added to <paramref name="damage"/> and <paramref name="healed"/>.
    /// </summary>
    /// <returns>False if the effect ended its record: a web escaped.</returns>
    public static bool Round(ActiveEffect record, int index, EffectTarget target, PartOfDay partOfDay, IRandom random,
        ref int damage, ref int healed)
    {
        var sheet = target.Sheet;
        switch (record.Effects[index])
        {
            case DamagePerRoundEffect d:
            {
                var amount = random.RollD(d.Dice.Count, d.Dice.Sides) + d.Dice.Flat;
                // No more than its hit points, so it can't kill by overshooting.
                if (amount != 0 && amount <= sheet.Hp)
                {
                    sheet.TakeDamage(amount, target.Combatant);
                    damage += amount;
                }
                break;
            }
            case PoisonEffect p:
            {
                var amount = Math.Min(p.Amount.At(record.Power), sheet.Stats.Modified(Stat.Endurance));
                if (sheet.Hp < amount)
                    break;
                damage += amount;
                sheet.Stats.ChangeCurrent(Stat.Endurance, -amount);
                break;
            }
            case HealPerRoundEffect h:
                // The clock's own part of the day (World+1), not the one Darkness holds.
                if (h.DayOnly && partOfDay == PartOfDay.Night)
                    break;
                sheet.Heal(record.Margin);
                healed += record.Margin;
                break;
            case ImmobiliseEffect { Escape: > 0 } i:
            {
                var strength = i.Escape * sheet.Stats.Modified(Stat.Strength);
                var roll = random.RollD(1, 100);
                if (roll < strength && Party.SkillCheckStep((short)(strength - roll)) != 0)
                    return false;
                break;
            }
        }
        return true;
    }
}
