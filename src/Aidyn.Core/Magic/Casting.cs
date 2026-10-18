using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Magic;

/// <summary>Why a spell can't be cast at a target, or wasn't (the strings at <c>0x800E2C00</c> and combat's).</summary>
public enum CastRefusal
{
    None,
    /// <summary>"Spell Unavailable": a spell for outside combat, which nothing casts.</summary>
    Unavailable,
    /// <summary>"Target must have a True Name!"</summary>
    NeedsTrueName,
    /// <summary>"The target is too large!": the spell takes only those the record calls heavy, the party's size or smaller.</summary>
    TooLarge,
    /// <summary>"Target must have Solar aspect!"</summary>
    NeedsSolar,
    /// <summary>"Target must have Lunar aspect!"</summary>
    NeedsLunar,
    /// <summary>"can only be cast in the day!" or "at night!"</summary>
    WrongTime,
    /// <summary>"Missing spell component"</summary>
    NoReagent,
    /// <summary>"Not enough ST to cast spell"</summary>
    NoStamina,
    /// <summary>"%s fails to cast the spell": an item spell with no charges left.</summary>
    NoCharges,
    /// <summary>"%s fails to cast the spell": the roll failed.</summary>
    Failed,
    /// <summary>Teleportation in the Shadow's battle.</summary>
    Barred,
}

/// <summary>What a cast did to one target (<c>Combatant.CastSpell</c>'s result).</summary>
public enum CastResult
{
    /// <summary>It wasn't cast (<c>0xFFFE</c>): see <see cref="CastOutcome.Refusal"/>.</summary>
    NotCast,
    /// <summary>The target resisted it (<c>0xFFFF</c>).</summary>
    Resisted,
    /// <summary>It took effect, with no number to show (<c>0xFFFD</c>).</summary>
    Effect,
    /// <summary>It dealt <see cref="CastOutcome.Damage"/>.</summary>
    Damage,
}

/// <summary>A cast's result at one target.</summary>
/// <param name="Margin">The cast's margin after the target's resist step: the cast's step, less what the resist took.</param>
/// <param name="CheatedDeath">The damage would have killed it, and a Cheat Death saved it.</param>
public sealed record CastOutcome(Combatant Target, CastResult Result, CastRefusal Refusal = CastRefusal.None, int Damage = 0,
    int Margin = 0, bool CheatedDeath = false);

/// <summary>Where a cast's spell comes from: a spell the caster knows, or an item's (<c>entity+0x3D</c>).</summary>
/// <param name="Known">The spellbook's entry, which a cast may raise a level.</param>
/// <param name="Item">The worn or carried item whose spell it is, with its charges.</param>
public sealed record SpellSource(SpellRecord Spell, LearnedSpell? Known = null, ItemInstance? Item = null, int ItemLevel = 0)
{
    public static SpellSource From(LearnedSpell known) => new(known.Spell, known);

    /// <summary>It's a weapon's spell as the weapon strikes, which spends no charge.</summary>
    public bool Proc { get; init; }

    public bool IsItem => Item is not null;
}

/// <summary>
/// Casting's rules, which combat runs (<c>combatEntity.cpp</c>, docs/systems/magic.md): a spell's level and cost, who it may take,
/// the chance and the roll, the resist roll, and what each target gets.
/// </summary>
public static class Casting
{
    /// <summary>
    /// The cast's effective level (<c>Entity.SpellLevel</c>, <c>0x80070E68</c>): a known spell's level, no higher than the caster's
    /// modified Wizard; an item's spell its own.
    /// </summary>
    public static int Level(Character caster, SpellSource source) =>
        source.IsItem ? source.ItemLevel : Math.Min(source.Known?.Level ?? 0, caster.Skills.Modified(Skill.Wizard));

    /// <summary>What a cast costs in Stamina (<c>0x80070EFC</c>): the spell's, less a point a level, at least 1.</summary>
    public static int Cost(SpellRecord spell, int level) => Math.Max(1, spell.Stamina - level);

    /// <summary>
    /// How many targets a spell takes (<c>Combatant.SpellTargetCount</c>, <c>0x80060818</c>): every one it may, one, or half
    /// the level rounded up.
    /// </summary>
    public static int TargetCount(SpellRecord spell, int level, int all) => spell.TargetCount switch
    {
        SpellTargetCount.All => all,
        SpellTargetCount.ByLevel => (level + 1) >> 1,
        _ => 1,
    };

    /// <summary>
    /// A spell's reach in cells (<c>Combatant.CanCast</c>, <c>0x80060C38</c>): its level times its range, from the caster, or for a
    /// placed area from the point placed, which is within the level. A target's body's radius adds to it.
    /// </summary>
    public static float Reach(SpellRecord spell, int level) => spell.Placed ? level : level * spell.Range;

    /// <summary>
    /// What a spell needs of its target (<c>Spell.TargetRequirement</c>, <c>0x800712C0</c>): a True Name, to be no bigger than the
    /// party (the record's heavy flag, set on the small), or an aspect.
    /// </summary>
    public static CastRefusal Requirement(SpellRecord spell, Character target) => spell.Special switch
    {
        SpellSpecial.TrueName when !target.TrueName => CastRefusal.NeedsTrueName,
        SpellSpecial.Heavy when !target.Heavy => CastRefusal.TooLarge,
        SpellSpecial.SolarAspect when target.Aspect != Aspect.Solar => CastRefusal.NeedsSolar,
        SpellSpecial.LunarAspect when target.Aspect != Aspect.Lunar => CastRefusal.NeedsLunar,
        _ => CastRefusal.None,
    };

    /// <summary>Whether the battle's part of the day allows the spell (<c>Spell.TimeRequirement</c>, <c>0x80071260</c>). No spell of the base game has one.</summary>
    public static bool TimeAllows(SpellRecord spell, PartOfDay partOfDay) => spell.Special switch
    {
        SpellSpecial.Day => partOfDay != PartOfDay.Night,
        SpellSpecial.Night => partOfDay == PartOfDay.Night,
        _ => true,
    };

    /// <summary>
    /// Whether a spell may be cast at <paramref name="target"/> (<c>Combatant.SpellCanTarget</c>, <c>0x80060988</c>, and
    /// <c>SpellKindAllowsTarget</c>, <c>0x8006088C</c>): it's alive and in play, on the side the spell wants, its filter allows it,
    /// and the spell isn't on it already. Range and sight are the battle's.
    /// </summary>
    public static bool CanTarget(Combatant caster, SpellRecord spell, Combatant target)
    {
        if (target.Flags.HasFlag(CombatantFlags.Gone) || target.Sheet.IsDead)
            return false;
        var sameSide = target.Hostile == caster.Hostile;
        if (spell.TargetType == SpellTargetType.Party && !sameSide || spell.TargetType == SpellTargetType.Enemy && sameSide)
            return false;
        if (spell.TargetCount == SpellTargetCount.Self && target != caster)
            return false;
        if (target.Sheet.HasEffect(spell.Id))
            return false;
        return Allows(spell.Filter, target.Sheet);
    }

    /// <summary>Whether a spell's filter allows a character: one of its entities, of none of its categories.</summary>
    public static bool Allows(TargetFilter? filter, Character target) =>
        filter is null
        || (filter.Entities is not { } entities || entities.Contains(target.Entity))
        && (filter.NotCategories is not { } categories || !categories.Contains(target.Record.Category));

    /// <summary>
    /// The chance to cast (<c>Combatant.CastChance</c>, <c>0x80063CA0</c>): (modified Intelligence × 4 + the level × 6 + the base
    /// Level) × a factor, truncated, scaled by the Troubadour, at least 5. A Lunar caster's factor is by the moon's phase, anyone
    /// else's by the battle's part of the day.
    /// </summary>
    public static int Chance(Character caster, int level, CastChance rules, PartOfDay partOfDay, int moonPhase, int troubadour, bool hostile)
    {
        var factor = caster.Aspect == Aspect.Lunar
            ? rules.ByMoon[Math.Clamp(moonPhase, 0, rules.ByMoon.Count - 1)]
            : rules.ByPartOfDay[(int)partOfDay];
        var sum = (ushort)(caster.Stats.Modified(Stat.Intelligence) * rules.Intelligence + (byte)level * rules.Level
            + caster.Stats.Base(Stat.Level) * rules.CharacterLevel);
        var chance = TroubadourScale((short)(int)(sum * factor), troubadour, hostile);
        return (ushort)chance < rules.Minimum ? rules.Minimum : (ushort)chance;
    }

    /// <summary>
    /// The Troubadour's song on a chance (<c>Combatant.TroubadourScale</c>, <c>0x80062C58</c>): the party's side's × (1.05 + the
    /// bonus × 0.01), the enemies' × (0.95 − the bonus × 0.01).
    /// </summary>
    public static int TroubadourScale(short chance, int troubadour, bool hostile)
    {
        if ((byte)troubadour == 0)
            return chance;
        var share = (byte)troubadour * 0.01f;
        var factor = hostile ? 0.95f - share : share + 1.05f;
        return (short)(int)(chance * factor);
    }

    /// <summary>
    /// A roll against a chance, which a skill can turn (<c>Combatant.AdjustRoll</c>, <c>0x80062594</c>): a roll at or under the
    /// skill, no more than a fifth of the chance, has a second d100 taken off it.
    /// </summary>
    public static int AdjustRoll(int roll, int chance, int skill, IRandom random)
    {
        var threshold = MathF.Min(skill, (short)chance * 0.2f);
        return (short)roll <= threshold ? (short)(roll - (byte)random.RollD(1, 100)) : (short)roll;
    }

    /// <summary>
    /// Whether a spell is resisted at all by this target (<c>Combatant.IsResistable</c>, <c>0x800641B8</c>): by everyone, by the other
    /// side, or by no one, and never by the entities it names.
    /// </summary>
    public static bool Resistable(SpellRecord spell, Combatant caster, Combatant target) =>
        !(spell.NotResistedBy?.Contains(target.Entity) ?? false) && spell.Resisted switch
        {
            Resisted.Always => true,
            Resisted.Opponents => target.Hostile != caster.Hostile,
            _ => false,
        };

    /// <summary>
    /// The target's resist roll (<c>Combatant.ResistRoll</c>, <c>0x80064298</c>), which takes its step off the cast's margin: (modified
    /// Willpower × 3 + modified Endurance + base Level) plus its ward against the spell's school, times a factor for a Solar or Lunar
    /// spell by whether the two share an aspect. A d100 over it doesn't resist. A step of the margin or more resists, and so does
    /// one less while Mirror's counter lasts, which spends the difference.
    /// </summary>
    /// <returns>The margin left, or null if the target resisted.</returns>
    public static int? Resist(Combatant caster, SpellRecord spell, Combatant target, int castMargin, int margin, ResistRoll rules, IRandom random)
    {
        var sheet = target.Sheet;
        var score = (short)(sheet.Stats.Modified(Stat.Willpower) * rules.Willpower + sheet.Stats.Modified(Stat.Endurance) * rules.Endurance
            + sheet.Stats.Base(Stat.Level) * rules.Level);
        var ward = (ushort)Ward(sheet, spell.School);
        if (ward != 0)
            score = (short)(ward + score);
        if (spell.Aspect != Aspect.None)
            score = (short)(int)(score * (sheet.Aspect == caster.Sheet.Aspect ? rules.SameAspect : rules.OtherAspect));
        var roll = (short)random.RollD(1, 100);
        if (roll > score)
            return margin;
        var step = Party.SkillCheckStep((short)(score - roll));
        castMargin = (sbyte)castMargin;
        if (step < castMargin && target.Mirror != 0)
        {
            var spent = (short)(castMargin - step);
            target.Mirror = spent < (byte)target.Mirror ? (byte)(target.Mirror - spent) : 0;
            return null;
        }
        return step < castMargin ? (byte)(margin - step) : null;
    }

    /// <summary>
    /// A character's ward against a school (<c>Combatant.ResistBonusFromEffects</c>, <c>0x80061F3C</c>): its first effect, by slot,
    /// that wards the school.
    /// </summary>
    public static int Ward(Character target, MagicSchool school)
    {
        foreach (var effect in target.Effects)
        {
            if (effect.Effects.OfType<WardEffect>().FirstOrDefault(w => w.School == school) is { } ward)
                return ward.Amount.At(effect.Power);
        }
        return 0;
    }

    /// <summary>
    /// The resistances that meet a spell's damage, multiplied (<c>Combatant.SpellResistanceMultiplier</c>, <c>0x80061D84</c>, and
    /// <c>SpellResistanceApplies</c>, <c>0x80061C5C</c>): the target's own and its worn items', each that matches the damage's
    /// elements, the spell's school's element, its aspect's, or Magic.
    /// </summary>
    /// <returns>The product, or null if none applies.</returns>
    public static float? ResistanceMultiplier(SpellRecord spell, IReadOnlyList<Element> elements, Character target)
    {
        float? product = null;
        foreach (var resistance in target.Record.Resistances.Concat(target.Equipment.Resistances))
        {
            if (Meets(resistance.Element, spell, elements))
                product = (product ?? 1) * resistance.Damage;
        }
        return product;
    }

    private static bool Meets(Element element, SpellRecord spell, IReadOnlyList<Element> elements) => element switch
    {
        Element.Magic => true,
        Element.Solar => spell.Aspect == Aspect.Solar,
        Element.Lunar => spell.Aspect == Aspect.Lunar,
        Element.Necromancy => spell.School == MagicSchool.Necromancy,
        Element.Naming => spell.School == MagicSchool.Naming,
        Element.Star => spell.School == MagicSchool.Star,
        Element.Elemental => spell.School == MagicSchool.Elemental,
        Element.Chaos => spell.School == MagicSchool.Chaos,
        Element.Earth or Element.Fire or Element.Water or Element.Air => elements.Contains(element),
        _ => false,
    };

    /// <summary>
    /// A spell's damage after resistances (<c>Combatant.SpellDamage</c>, <c>0x800649E4</c>): the dice times the product, truncated,
    /// at least 1 unless it's 0, when any resistance applies.
    /// </summary>
    public static int Damage(int dice, float? multiplier) => multiplier switch
    {
        null => dice,
        0 => 0,
        { } m => Math.Max(1, (int)(short)(int)(dice * m)),
    };
}
