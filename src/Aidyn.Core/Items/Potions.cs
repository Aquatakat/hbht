using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using Aidyn.Core.Combat;
using Aidyn.Core.Magic;
using Aidyn.Core.Rng;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Items;

/// <summary>What drinking a potion did (<c>Entity::UsePotion</c>).</summary>
public enum DrinkResult
{
    Drunk,
    /// <summary>It would do nothing now: a healing potion at full health, and so on. Nothing is used.</summary>
    NotNow,
    /// <summary>It isn't drunk here: a flask, a potion with no effect, or one its record keeps from trek or from a battle. Nothing is used.</summary>
    NotInTrek,
}

/// <summary>
/// Potions and flasks, which are data (ADR 0021): an item of kind <see cref="ItemKind.Potion"/> whose record says how it's used
/// (<see cref="ItemRecord.Use"/>) and what it does (its effects, which land through <see cref="EffectLanding"/> as a spell's do).
/// Drinking from the pause menu is <c>Entity::UsePotion</c> and <c>Entity::CanUsePotion</c>, from C-up's menu
/// <c>Combatant.DrinkPotion</c>.
/// </summary>
public static class Potions
{
    /// <summary>How long the base game's stat and skill potions last: 3 hours of game time (<c>HOURS(3)</c>).</summary>
    public const uint Duration = 3 * GameClock.Hour;

    /// <summary>
    /// The margin a potion's or flask's effects land at, which a dispel and a cure that beats margins weigh: the highest, as the
    /// original's Antidote adds its Remove Poison at 255. Its lasting record keeps 0, as the original's potion record has no margin.
    /// </summary>
    public const int Margin = 255;

    /// <summary>How a potion is used, by its record; nothing for an item that isn't a potion.</summary>
    public static PotionUse Use(Item item) => item.Kind == ItemKind.Potion ? item.Gear?.Use ?? PotionUse.None : PotionUse.None;

    /// <summary>Whether the item is a flask, thrown in a battle rather than drunk, which the crafting messages call so.</summary>
    public static bool IsFlask(Item item) => Use(item).HasFlag(PotionUse.Throw);

    /// <inheritdoc cref="IsFlask(Item)"/>
    public static bool IsFlask(ItemRecord potion) => potion.Kind == ItemKind.Potion && (potion.Use ?? PotionUse.None).HasFlag(PotionUse.Throw);

    /// <summary>
    /// Whether drinking a potion would do anything now (<c>Entity::CanUsePotion</c>, <c>0x8006FBE8</c>), by its record (ADR 0021):
    /// one its record keeps from here, a flask, or one with no effects isn't drunk here; one that lasts, if it isn't working already;
    /// otherwise an effect that would do something: a heal below full health, a cure or a dispel with something to take off, Stamina
    /// below its base, or any other.
    /// </summary>
    /// <param name="inBattle">Drunk from C-up's menu in a battle (<see cref="PotionUse.DrinkInBattle"/>), not the pause menu in trek.</param>
    public static DrinkResult CanDrink(Character member, Item potion, bool inBattle = false)
    {
        var drunkHere = inBattle ? PotionUse.DrinkInBattle : PotionUse.DrinkInTrek;
        if (potion.Gear is not { Effects: [_, ..] effects } record || IsFlask(potion) || !Use(potion).HasFlag(drunkHere))
            return DrinkResult.NotInTrek;
        if (record.Duration is not null && effects.Any(EffectRules.Lasting))
            return member.HasEffect(record.Id) ? DrinkResult.NotNow : DrinkResult.Drunk;
        return effects.Any(e => WouldDo(member, e)) ? DrinkResult.Drunk : DrinkResult.NotNow;
    }

    // What drinking finds to do now: the Stamina potion is drinkable with Stamina below its base (slt), or exhausted.
    private static bool WouldDo(Character member, Effect effect) => effect switch
    {
        HealEffect => member.Hp != member.MaxHp,
        CureEffect cure => member.Effects.Any(e => cure.Spells.Contains(e.Source)),
        DispelEffect dispel => member.Effects.Any(e => e.School == dispel.School),
        StaminaEffect => member.Stats.Modified(Stat.Stamina) < member.Stats.Base(Stat.Stamina),
        _ => true,
    };

    /// <summary>
    /// A member drinks a potion from the party's inventory (<c>Entity::UsePotion</c>, and in a battle <c>Combatant.DrinkPotion</c>,
    /// <c>0x80064C84</c>), if <see cref="CanDrink"/> allows: its effects land on the member, in order, at <see cref="Margin"/>
    /// (<see cref="EffectLanding"/>), any damage among them rolled as that many d6; then one is used up. The base game's Healing heals
    /// 15–25% of the maximum hit points and Curing 40–60%; the Stamina potion ends Exhaustion and restores Stamina to its base; the
    /// Antidote ends poison and Restore the weakening spells; the rest work for <see cref="Duration"/>.
    /// </summary>
    /// <param name="on">The member's combatant, drinking in a battle.</param>
    public static DrinkResult Drink(Party party, Character member, Item item, IRandom random, Combatant? on = null, IMagicHost? host = null)
    {
        if (item.Kind != ItemKind.Potion || !party.Inventory.Has(item.Id))
            return DrinkResult.NotInTrek;
        var can = CanDrink(member, item, inBattle: on is not null);
        if (can != DrinkResult.Drunk)
            return can;
        var record = item.Gear!;
        var drinker = new EffectTarget(member, on, host);
        var landing = new Landing(record.Id, MagicSchool.None, 0, Margin, drinker, drinker, random) { RecordMargin = 0 };
        var result = EffectLanding.Land(record.Effects!, record.Duration, landing);
        if (result.Dice is { } dice)
            member.TakeDamage(random.RollD(dice & 0xFF, 6), on);
        party.Inventory.Take(item.Id);
        return DrinkResult.Drunk;
    }
}
