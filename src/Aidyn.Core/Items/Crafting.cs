using Aidyn.Core.Characters;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Items;

/// <summary>What making something from a recipe did.</summary>
public enum CraftResult
{
    Made,
    /// <summary>The check failed: the ingredients are used up all the same.</summary>
    Failed,
    /// <summary>The party lacks an ingredient. Nothing is used.</summary>
    MissingIngredients,
    /// <summary>The party's stack of the potion is full. Nothing is used.</summary>
    StackFull,
    /// <summary>The member's skill is below the recipe's level.</summary>
    TooLow,
}

/// <summary>How a Healer's task went (<c>Party::PrintHeal</c>, <c>HerbHeal</c>, <c>HealingFunc2</c>).</summary>
public enum HealOutcome
{
    /// <summary>The target gained <see cref="HealResult.Amount"/>.</summary>
    Healed,
    /// <summary>The target's hit points are full, or the stat isn't hurt. Nothing is used.</summary>
    AlreadyWell,
    /// <summary>The Healer lacked the Stamina, or the check failed, or it healed nothing. A herb is used up all the same.</summary>
    Failed,
    /// <summary>There's no herb in the inventory.</summary>
    NoHerb,
    /// <summary>
    /// Healing is barred in a battle with the Shadow (Common line 437, "The Shadow's presence prevents all healing."): before Lay On
    /// Hands' cost, and after Heal With Herbs has used its herb up.
    /// </summary>
    Barred,
}

public readonly record struct HealResult(HealOutcome Outcome, int Amount = 0);

/// <summary>
/// The skills used from the pause menu (<c>potionCraft.cpp</c>, <c>Armorcraft.cpp</c>, <c>HealMenu.cpp</c>): the
/// Alchemist brews potions from reagents, the Mechanic makes armor from hides, and the Healer heals by
/// hand or with herbs, and restores a hurt attribute.
/// </summary>
public static class Crafting
{
    public static readonly RecordId Alchemist = ((SkillId)Skill.Alchemist).Record;
    public static readonly RecordId Mechanic = ((SkillId)Skill.Mechanic).Record;

    /// <summary>
    /// The recipes a member is offered: the Alchemist's whose level their modified Alchemist reaches, in
    /// order (<c>WidgetPotionCraft</c>), and the Mechanic's whose hide the party has and whose level their
    /// base Mechanic reaches (<c>WidgetArmorCraft</c>).
    /// </summary>
    public static IEnumerable<RecipeRecord> Offered(Party party, Character member, IEnumerable<RecipeRecord> recipes) => recipes.Where(r =>
        r.Skill == Alchemist ? r.Level <= member.Skills.Modified(Skill.Alchemist)
        : r.Skill == Mechanic && r.Level <= member.Skills.Base(Skill.Mechanic) && r.Ingredients.All(i => party.Inventory.Has(i.Item)));

    /// <summary>Makes something from a recipe by its skill.</summary>
    public static CraftResult Make(Party party, Character member, RecipeRecord recipe, IRandom random) =>
        recipe.Skill == Mechanic ? MakeArmor(party, member, recipe, random) : Brew(party, member, recipe, random);

    /// <summary>
    /// Brews a potion (<c>WidgetPotionCraft::AFunc</c>, <c>Party::CraftPotion</c>): the reagents are used up, then
    /// the Alchemist rolls d100 against Intelligence × 3 + Alchemist × 6, and makes one if the step reaches the
    /// recipe's. Brewing costs no Stamina.
    /// </summary>
    public static CraftResult Brew(Party party, Character member, RecipeRecord recipe, IRandom random)
    {
        var inventory = party.Inventory;
        if (recipe.Ingredients.Any(i => inventory.Count(i.Item) < i.Count))
            return CraftResult.MissingIngredients;
        if (inventory.Count(recipe.Product) >= inventory.Catalog.Rules.StackLimit)
            return CraftResult.StackFull;
        foreach (var ingredient in recipe.Ingredients)
            inventory.Take(ingredient.Item, ingredient.Count);
        var alchemist = member.Skills.Modified(Skill.Alchemist);
        if (alchemist < recipe.Level)
            return CraftResult.TooLow;
        var total = (ushort)Party.AlchemistBonus(member);
        var roll = random.RollD(1, 100);
        if (roll >= total || Party.SkillCheckStep((short)(total - roll)) < recipe.Step)
            return CraftResult.Failed;
        return inventory.Add(recipe.Product) ? CraftResult.Made : CraftResult.Failed;
    }

    /// <summary>
    /// Makes armor from a hide (<c>WidgetArmorCraft::AFunc</c>, <c>Party::CraftArmor</c>): the Mechanic first rolls
    /// d100 against (Mechanic × 5 + Intelligence + Dexterity) × 2 and pays 5 less their level in Stamina, at
    /// least 1, if they pass and can (<c>ArmorCraftCheck</c>); then the hide is used up, and the armor is made
    /// if the step reaches the recipe's.
    /// </summary>
    public static CraftResult MakeArmor(Party party, Character member, RecipeRecord recipe, IRandom random)
    {
        var inventory = party.Inventory;
        var mechanic = member.Skills.Modified(Skill.Mechanic);
        // The sum is masked to 15 bits before it's doubled. Party.SkillCheck's Mechanic check casts the doubled sum to
        // 16 bits instead; they agree for any sum a character can reach, and are ported apart, each from its own code.
        var bonus = ((mechanic * 5 + member.Stats.Modified(Stat.Intelligence) + member.Stats.Modified(Stat.Dexterity)) & 0x7FFF) * 2;
        var step = Containers.CheckAndPay(member, bonus, Math.Max(5 - mechanic, 1), random);
        var hide = recipe.Ingredients[0].Item;
        if (!inventory.Has(hide))
            return CraftResult.MissingIngredients;
        inventory.Take(hide);
        if (step < recipe.Step)
            return CraftResult.Failed;
        inventory.Add(recipe.Product);
        return CraftResult.Made;
    }

    /// <summary>
    /// Lays on hands (<c>Party::PrintHeal</c>): for 3 Stamina, the Healer heals another member Healer × 2 + 5.
    /// No check is rolled.
    /// </summary>
    /// <param name="barred">Whether healing is barred (<c>Game.Mode</c> 2 with the Shadow in the battle).</param>
    public static HealResult LayOnHands(Character healer, Character target, bool barred = false)
    {
        var before = target.Hp;
        if (before == target.MaxHp)
            return new(HealOutcome.AlreadyWell);
        if (barred)
            return new(HealOutcome.Barred);
        if (healer.Stats.Modified(Stat.Stamina) < 3)
            return new(HealOutcome.Failed);
        healer.DrainStamina(3);
        target.Heal((ushort)(healer.Skills.Modified(Skill.Healer) * 2 + 5));
        var healed = (short)(target.Hp - before);
        return healed > 0 ? new(HealOutcome.Healed, healed) : new(HealOutcome.Failed);
    }

    /// <summary>
    /// Heals with a herb (<c>Party::HerbHeal</c>): the herb is used up, then for 5 less their level in Stamina,
    /// at least 1, the Healer rolls d100 against Intelligence × 4 + Healer × 11 and heals four times the step.
    /// </summary>
    /// <param name="barred">
    /// Whether healing is barred (<c>Game.Mode</c> 2 with the Shadow in the battle): tested after the herb is taken and the Stamina is
    /// found, so the herb is lost and nothing is paid or rolled (<c>Party.HealWithHerbs</c>, <c>0x8007798C</c>).
    /// </param>
    public static HealResult HerbHeal(Party party, Character healer, Character target, IRandom random, bool barred = false)
    {
        var level = healer.Skills.Modified(Skill.Healer);
        var cost = (short)Math.Max(5 - level, 1);
        var before = target.Hp;
        if (before == target.MaxHp)
            return new(HealOutcome.AlreadyWell);
        if (!TakeHerb(party))
            return new(HealOutcome.NoHerb);
        if (healer.Stats.Modified(Stat.Stamina) < cost)
            return new(HealOutcome.Failed);
        if (barred)
            return new(HealOutcome.Barred);
        healer.DrainStamina(cost);
        var total = (short)(healer.Stats.Modified(Stat.Intelligence) * 4 + level * 11);
        var roll = (short)random.RollD(1, 100);
        if (roll >= total)
            return new(HealOutcome.Failed);
        target.Heal((ushort)(Party.SkillCheckStep((short)(total - roll)) * 4));
        var healed = (short)(target.Hp - before);
        return healed != 0 ? new(HealOutcome.Healed, healed) : new(HealOutcome.Failed);
    }

    /// <summary>
    /// Restores an attribute damage has taken below its trained value (<c>Party::HealingFunc2</c>): the herb is
    /// used up, then for 15 less their level in Stamina, at least 0, the Healer rolls d100 against Intelligence
    /// × 2 + Healer × 5 and restores twice the step, up to the base value.
    /// </summary>
    public static HealResult Restore(Party party, Character healer, Character target, Stat stat, IRandom random)
    {
        if (!target.Stats.IsHurt(stat))
            return new(HealOutcome.AlreadyWell);
        var level = healer.Skills.Modified(Skill.Healer);
        var cost = (short)Math.Max(15 - level, 0);
        if (!TakeHerb(party))
            return new(HealOutcome.NoHerb);
        if (healer.Stats.Modified(Stat.Stamina) < cost)
            return new(HealOutcome.Failed);
        healer.DrainStamina(cost);
        var total = (short)(healer.Stats.Modified(Stat.Intelligence) * 2 + level * 5);
        var roll = random.RollD(1, 100);
        if (roll >= total)
            return new(HealOutcome.Failed);
        var amount = (short)(Party.SkillCheckStep((short)(total - roll)) * 2);
        var before = target.Stats.Modified(stat);
        if (target.Stats.Base(stat) < before + amount)
            amount = (short)(target.Stats.Base(stat) - before);
        target.Stats.ChangeCurrent(stat, (sbyte)amount);
        var restored = target.Stats.Modified(stat) - before;
        return restored > 0 ? new(HealOutcome.Healed, restored) : new(HealOutcome.Failed);
    }

    private static bool TakeHerb(Party party) => party.Inventory.Take(party.Inventory.Catalog.Rules.Herb);
}
