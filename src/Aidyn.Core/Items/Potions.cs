using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
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

/// <summary>
/// A potion's effect on a character while it lasts (<c>PotionEffect</c>): Strength or Dexterity +10, or
/// Clarity, Charisma or Stealth +3 to Loremaster, Diplomat or Stealth. Defence's is combat's to read.
/// </summary>
/// <param name="Timer">What's left, in the game clock's ticks, which run <see cref="GameClock.DaySpeed"/> to a tick of play.</param>
public sealed class PotionEffect(Potion potion, int power, uint timer)
{
    public Potion Potion { get; } = potion;

    /// <summary>A byte the save keeps. Drinking sets 0, and nothing reads it.</summary>
    public int Power { get; } = (byte)power;

    public uint Timer { get; internal set; } = timer;
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
    /// Whether drinking a potion would do anything now (<c>Entity::CanUsePotion</c>): a healing one below
    /// full health, Stamina below its base, a stat or skill one not already working. The Antidote needs
    /// poison and Restore a weakening spell, which come with combat (2.5), so neither can be drunk yet.
    /// </summary>
    /// <remarks>The decomp reads the Stamina potion as drinkable at full Stamina; the machine code tests that it's below.</remarks>
    public static DrinkResult CanDrink(Character member, Potion potion) => potion switch
    {
        Potion.Healing or Potion.Curing => member.Hp != member.MaxHp ? DrinkResult.Drunk : DrinkResult.NotNow,
        Potion.Stamina => member.Stats.Modified(Stat.Stamina) < member.Stats.Base(Stat.Stamina) ? DrinkResult.Drunk : DrinkResult.NotNow,
        Potion.Antidote or Potion.Restore => DrinkResult.NotNow,
        Potion.Strength or Potion.Dexterity or Potion.Clarity or Potion.Charisma or Potion.Defence or Potion.Stealth =>
            member.HasPotionEffect(potion) ? DrinkResult.NotNow : DrinkResult.Drunk,
        _ => DrinkResult.NotInTrek,
    };

    /// <summary>
    /// A member drinks a potion from the party's inventory (<c>Entity::UsePotion</c>): Healing heals 15–25% of
    /// their maximum hit points and Curing 40–60%, Stamina restores Stamina to its base, and the stat and skill
    /// potions work for <see cref="Duration"/>. Then one is used up.
    /// </summary>
    public static DrinkResult Drink(Party party, Character member, Item item, IRandom random)
    {
        if (Of(item) is not { } potion || !party.Inventory.Has(item.Id))
            return DrinkResult.NotInTrek;
        var can = CanDrink(member, potion);
        if (can != DrinkResult.Drunk)
            return can;
        switch (potion)
        {
            case Potion.Healing:
                HealByPotion(member, 15, 25, random);
                break;
            case Potion.Curing:
                HealByPotion(member, 40, 60, random);
                break;
            case Potion.Stamina:
                member.RestoreStamina();
                break;
            default:
                member.ApplyPotionEffect(potion, 0, Duration);
                break;
        }
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
