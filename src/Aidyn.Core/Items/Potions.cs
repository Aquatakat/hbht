using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using Aidyn.Core.Combat;
using Aidyn.Core.Magic;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Items;

/// <summary>The potions and flasks, by the low byte of their item ID (<c>PotionEnum</c>): potion <c>n</c> is <c>aidyn:item/0x10nn</c>.</summary>
public enum Potion
{
    FireFlask,
    InfernoFlask,
    SleepGas,
    AcidFlask,
    Healing,
    Stamina,
    Curing,
    Antidote,
    Restore,
    Strength,
    Dexterity,
    /// <summary>Left out of the game: gaining one gives a Healing potion.</summary>
    Aspect,
    /// <summary>Left out of the game.</summary>
    Jumping,
    Clarity,
    Charisma,
    Defence,
    Stealth,
}

/// <summary>What drinking a potion did (<c>Entity::UsePotion</c>).</summary>
public enum DrinkResult
{
    Drunk,
    /// <summary>It would do nothing now: a healing potion at full health, and so on. Nothing is used.</summary>
    NotNow,
    /// <summary>A flask, or a potion with no effect, outside combat. Nothing is used.</summary>
    NotInTrek,
}

/// <summary>Drinking potions from the pause menu (<c>Entity::UsePotion</c>, <c>Entity::CanUsePotion</c>).</summary>
public static class Potions
{
    /// <summary>How long a stat or skill potion lasts: 3 hours of game time (<c>HOURS(3)</c>).</summary>
    public const uint Duration = 3 * GameClock.Hour;

    /// <summary>A potion's item record.</summary>
    public static RecordId Id(Potion potion) => RecordId.Hex(RecordTypes.Item, 0x1000 + (int)potion);

    /// <summary>The potion an item record is, or null if it isn't one of the base game's.</summary>
    public static Potion? Of(RecordId item) => Enum.GetValues<Potion>().Cast<Potion?>().FirstOrDefault(p => Id(p!.Value) == item);

    /// <summary>The potion an item is, or null if it isn't one of the base game's.</summary>
    public static Potion? Of(Item item) => item.Kind == ItemKind.Potion ? Of(item.Id) : null;

    /// <summary>Whether the potion is a flask, thrown in combat, which the crafting messages call so.</summary>
    public static bool IsFlask(Potion potion) => potion < Potion.Healing;

    /// <summary>
    /// Whether drinking a potion would do anything now (<c>Entity::CanUsePotion</c>, <c>0x8006FBE8</c>), by its effects (ADR 0020):
    /// one that lasts, if it isn't working already; otherwise a heal below full health, a cure with something to cure, or
    /// Stamina below its base. A flask, or a potion with no effects, can't be drunk in trek.
    /// </summary>
    public static DrinkResult CanDrink(Character member, Item potion)
    {
        if (potion.Gear is not { Effects: [_, ..] effects } record || effects.Any(e => e is BlastEffect or CorrodeEffect))
            return DrinkResult.NotInTrek;
        if (record.Duration is not null && effects.Any(EffectRules.Lasting))
            return member.HasEffect(record.Id) ? DrinkResult.NotNow : DrinkResult.Drunk;
        return effects.Any(e => WouldDo(member, e)) ? DrinkResult.Drunk : DrinkResult.NotNow;
    }

    /// <inheritdoc cref="CanDrink(Character, Item)"/>
    public static DrinkResult CanDrink(Character member, Potion potion, ItemCatalog items) => CanDrink(member, items[Id(potion)]);

    // What drinking finds to do now: the Stamina potion is drinkable with Stamina below its base (slt), or exhausted.
    private static bool WouldDo(Character member, Effect effect) => effect switch
    {
        HealEffect => member.Hp != member.MaxHp,
        CureEffect cure => member.Effects.Any(e => cure.Spells.Contains(e.Source)),
        StaminaEffect => member.Stats.Modified(Stat.Stamina) < member.Stats.Base(Stat.Stamina),
        _ => false,
    };

    /// <summary>
    /// A member drinks a potion from the party's inventory (<c>Entity::UsePotion</c>, and in combat <c>Combatant.DrinkPotion</c>,
    /// <c>0x80064C84</c>): its effects in order, then one is used up. Healing heals 15–25% of the maximum hit points and Curing
    /// 40–60%; the Stamina potion ends Exhaustion and restores Stamina to its base; the Antidote ends poison and Restore the
    /// weakening spells; the rest work for <see cref="Duration"/>.
    /// </summary>
    /// <param name="on">The member's combatant, drinking in a battle.</param>
    public static DrinkResult Drink(Party party, Character member, Item item, IRandom random, Combatant? on = null, IMagicHost? host = null)
    {
        if (item.Kind != ItemKind.Potion || !party.Inventory.Has(item.Id))
            return DrinkResult.NotInTrek;
        var can = CanDrink(member, item);
        if (can != DrinkResult.Drunk)
            return can;
        var record = item.Gear!;
        foreach (var effect in record.Effects!)
        {
            switch (effect)
            {
                case HealEffect heal:
                    HealByPotion(member, heal.Low, heal.High, random);
                    break;
                case CureEffect cure:
                    foreach (var cured in member.Effects.Where(e => cure.Spells.Contains(e.Source)).ToList())
                        member.RemoveEffect(cured, on, host);
                    break;
                case StaminaEffect { ToBase: true }:
                    member.RestoreStamina();
                    break;
            }
        }
        if (record.Duration is { } duration)
            member.ApplyPotionEffect(record.Id, 0, duration.At(0));
        party.Inventory.Take(item.Id);
        return DrinkResult.Drunk;
    }

    // Entity::HealByPotion: a share of the maximum, worked in single precision and truncated.
    private static void HealByPotion(Character member, int low, int high, IRandom random)
    {
        var share = random.Range(low, high) / 100f;
        member.Heal((ushort)(share * member.MaxHp));
    }
}
