using Aidyn.Core.Calendar;
using Aidyn.Core.Combat;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Magic;

/// <summary>
/// What the enemies' AI makes of a spell (<c>EnemyAI.SpellValue</c>, <c>0x80057498</c>, ADR 0020): the spell's own value, or with
/// none its first effect's type's, the highest the loaded spells give that type; and 0 when its type finds nothing to do now.
/// Session 11's AI scores spells by it.
/// </summary>
public static class SpellValues
{
    /// <param name="candidates">Those the spell may be cast at (target set B's candidates).</param>
    /// <param name="inPlay">Every combatant in play, which Darkness and Light weigh.</param>
    public static int Value(MagicRules rules, SpellRecord spell, Combatant caster, IReadOnlyList<Combatant> candidates,
        IEnumerable<Combatant> inPlay, PartOfDay partOfDay)
    {
        var effects = spell.Effects ?? [];
        var value = spell.AiValue ?? (effects.Count > 0 ? Default(rules, effects[0].GetType()) : 0);
        foreach (var effect in effects)
        {
            var worth = effect switch
            {
                // 0x8005B734: Remove Poison only when a candidate is poisoned.
                CureEffect cure => candidates.Any(c => c.Sheet.Effects.Any(e => cure.Spells.Contains(e.Source))),
                // 0x8005B60C, 0x8005B6AC: Control Elem and Banishing only at an elemental, Control Zombies at a zombie.
                CharmEffect or BanishEffect => spell.Filter is null || candidates.Any(c => Casting.Allows(spell.Filter, c.Sheet)),
                PartOfDayEffect light => (partOfDay == PartOfDay.Night) != light.Night && Balance(light, caster, inPlay, partOfDay) > 0,
                DispelEffect dispel => Harm(dispel.School, caster, candidates) > 0,
                _ => true,
            };
            if (!worth)
                return 0;
        }
        return value;
    }

    /// <summary>The highest value the loaded spells give a type, by their first effect, for a spell with none of its own.</summary>
    public static int Default(MagicRules rules, Type effect) =>
        rules.Spells.Where(s => s.AiValue is not null && s.Effects is [var first, ..] && first.GetType() == effect)
            .Select(s => s.AiValue!.Value).DefaultIfEmpty(0).Max();

    // Darkness by day and Light at night: +1 for each combatant the light now hinders on the caster's side, or helps on the
    // other's, and −1 for the rest (FUN_800615B4: a Lunar one is hindered by day, a Solar one at night).
    private static int Balance(PartOfDayEffect light, Combatant caster, IEnumerable<Combatant> inPlay, PartOfDay partOfDay)
    {
        var night = partOfDay == PartOfDay.Night;
        return inPlay.Sum(c =>
        {
            var hindered = c.Sheet.Aspect switch
            {
                Aspect.Lunar => !night,
                Aspect.Solar => night,
                _ => false,
            };
            return hindered == (c.Hostile == caster.Hostile) ? 1 : -1;
        });
    }

    // A dispel: +1 for each of the school's timed effects that harms the caster's side or helps the other's, −1 for the rest.
    private static int Harm(MagicSchool school, Combatant caster, IReadOnlyList<Combatant> candidates) =>
        candidates.Sum(c => c.Sheet.Effects.Where(e => e.School == school && e.Timer != ActiveEffect.Never)
            .Sum(e => e.Harmful == (c.Hostile == caster.Hostile) ? 1 : -1));
}
