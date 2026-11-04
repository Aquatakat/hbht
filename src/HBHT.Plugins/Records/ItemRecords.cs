namespace HBHT.Plugins.Records;

/// <summary>
/// An item (<c>aidyn:item/…</c>, ADR 0031): a weapon, a piece of armor or a shield, worn gear, a wand, a scroll, a key, a potion, a
/// reagent and the rest. The key is the original's <c>ItemID</c>, whose high byte is its kind. Weapons are from the weapon DB
/// (<c>WeaponDB::Load</c>), armor and shields from the armor DB (<c>ArmorDB::Load</c>), and the rest from the item DB
/// (<c>ItemDB::LoadItem</c>), but a potion, which is named by its own string table and priced by a table in code
/// (<c>ItemInstance::InitPotion</c>).
/// </summary>
/// <param name="Kind">What it is, which decides where it's worn. The original takes it from the ID's high byte.</param>
/// <param name="Weapon">What only a weapon has: required on a <see cref="ItemKind.Weapon"/>, and on no other kind.</param>
/// <param name="Requirements">
/// The least base value of each stat (<c>aidyn:stat/n</c>) that wears or wields it (<c>Entity::canEquipWeapon</c>,
/// <c>0x8006EF84</c>, and <c>Entity::GearMinStatCheck</c>, <c>0x8006F050</c>): Strength and Intelligence in the base.
/// </param>
/// <param name="Bonuses">What wearing or wielding it does for the wearer.</param>
/// <param name="LootExperience">
/// The experience it's worth as loot: when a battle's rewards give it, this goes on the experience pool every living member is
/// given, once for each one looted (<c>Combat.RollEquipmentDrops</c>, <c>0x8005DC78</c>, and <c>Combat.RollChestItem</c>,
/// <c>0x8005DF38</c>; <c>Battle.ExperienceValue</c>). 0x20 on the Lodin sword; a potion's is worth nothing.
/// </param>
/// <param name="Damage">What wearing it adds to the wearer's damage (<c>CharExp::damage</c>). A weapon's own is its <see cref="WeaponStats.Damage"/>.</param>
/// <param name="Protection">What wearing it adds to the wearer's protection (<c>Entity::GetArmorProtect</c>, <c>CharExp::protection</c>).</param>
/// <param name="Defense">
/// A shield's defense: times the wearer's shield skill, plus Warrior, it's taken off a melee or ranged hit chance against them
/// (<c>Combatant.ShieldDefence</c>, <c>0x8005F6B0</c>; <c>Battle.ShieldDefence</c>).
/// </param>
/// <param name="Fixed">
/// Once equipped, it can't be taken off (<c>Party::RemoveWeaponsFrom</c>): the Archmage Staff, which is Niesen's
/// <see cref="EntitySheet.SoleWeapon"/>.
/// </param>
/// <param name="KeyItem">The flags a key item sets while the party has it.</param>
/// <param name="Description">
/// What C-down's details say of it: an item's line in <c>aidyn:strings/0</c>, or a potion's in <c>aidyn:strings/4</c>. Weapons and
/// armor show their numbers instead.
/// </param>
/// <param name="Model">
/// The Borg5 scene (<c>aidyn:scene/0x…</c>) the original draws for it in a body's hand or on its arm (<c>get_item_borg5</c>), or
/// null when it has none.
/// </param>
/// <param name="Effects">
/// A potion's: what drinking it does, or for a flask, throwing it (ADR 0020). One with none can't be drunk in trek: the Aspect and
/// Jumping potions.
/// </param>
/// <param name="Duration">How long a potion's lasting effects last: 3 hours of game time.</param>
/// <param name="Presentation">What a potion shows in a battle as it's drunk, or a flask at each one its blast hurts; its caster's actor and its sound are never used.</param>
/// <param name="Use">
/// How a potion is used (ADR 0021): drunk from the pause menu in trek, drunk from C-up's menu in a battle, or thrown in a battle as a
/// flask, its effects landing where it does. With none, it can't be. Its effects decide whether drinking it does anything now.
/// </param>
/// <param name="AiSpread">
/// A flask's: while the party holds it, how far apart the enemies' AI keeps its combatants (<c>Combat.PartyPotionTier</c>,
/// <c>0x8005C750</c>, <c>Combat+0x12</c>), the first such flask held in the order the 1.0 table lists them counting.
/// </param>
/// <param name="AiSpreadOrder">Where the flask comes in that order, lowest first.</param>
/// <param name="Refused">No container, shop or loot window takes it (<c>GenericInventory::AddItem</c>): the Cyclops Club.</param>
/// <param name="Becomes">The item it becomes when it's gained: the Aspect potion becomes a Healing potion.</param>
/// <param name="KeptAfterBattle">
/// A potion's effects outlast the battle it's drunk in, where every other battle effect ends with it.
/// </param>
public sealed record ItemRecord(
    RecordId Id,
    string Name,
    RecordId Icon,
    int Price,
    ItemKind Kind,
    WeaponStats? Weapon = null,
    IReadOnlyDictionary<RecordId, int>? Requirements = null,
    ItemBonuses? Bonuses = null,
    int LootExperience = 0,
    int Damage = 0,
    int Protection = 0,
    int Defense = 0,
    ItemTraits? Traits = null,
    bool Fixed = false,
    KeyItemFlags? KeyItem = null,
    string? Description = null,
    RecordId? Model = null,
    IReadOnlyList<Effect>? Effects = null,
    EffectDuration? Duration = null,
    MagicPresentation? Presentation = null,
    PotionUse? Use = null,
    int AiSpread = 0,
    int AiSpreadOrder = 0,
    bool Refused = false,
    RecordId? Becomes = null,
    bool KeptAfterBattle = false)
{
    /// <summary>The least base value of <paramref name="stat"/> it needs, 0 for none.</summary>
    public int Requires(RecordId stat) => Requirements?.GetValueOrDefault(stat) ?? 0;
}

/// <summary>What only a weapon has (ADR 0031), from the weapon DB (<c>WeaponDB::Load</c>, <c>createTempWeapon</c>). Monsters' natural weapons, such as bites and breath, are weapons too.</summary>
/// <param name="Skill">
/// The weapon skill it uses (<c>aidyn:weaponskill/n</c>, the original's weapon class): wielding it needs that skill above 0
/// (<c>Entity::canEquipWeapon</c>).
/// </param>
/// <param name="Damage">Its damage dice's base.</param>
/// <param name="Range">Its reach in combat, the ROM's byte × 5 / 3 up to 255 (<c>WeaponDB::Load</c>).</param>
/// <param name="Hit">
/// Added to its wielder's melee and ranged hit chances (<c>Combat.RangedChance</c>, <c>0x80062F50</c>, and the melee one;
/// <c>Battle.MeleeChance</c>, <c>RangedChance</c>).
/// </param>
/// <param name="Animation">
/// The request its attack asks of its wielder's actor (<c>aidyn:request/12</c>–<c>18</c>): the importer turns the ROM's animation
/// byte into it by <c>Combat.AnimationByWeaponClass</c> (<c>0x800D27E0</c>, <c>Combatant.AnimationForWeapon</c>, <c>0x8005FEE4</c>).
/// </param>
/// <param name="DamageType">The element of its damage (<c>aidyn:element/n</c>), or none.</param>
/// <param name="Missile">
/// It flies to its target in battle (<c>Combatant.UpdateDrawnFlag</c>, <c>0x80066A2C</c>, through the bow list
/// <c>Combat.BowKindWeapons</c> <c>0x800E0C08</c> and the thrown list <c>Combat.ThrownKindWeapons</c> <c>0x800E0C14</c>): aimed
/// freely rather than at a target in reach, and drawn in flight.
/// </param>
/// <param name="StrikesInMelee">
/// A ranged weapon that may strike in melee too (<c>Weapon.AllowsMelee</c>, <c>0x8007B730</c>, by the weapon's place): Spikes. A
/// Breath's or Spit's always may.
/// </param>
/// <param name="Sounds">What it plays as it strikes, in combat: one at random (<c>AudioKeys.WeaponSound</c>). Null for none.</param>
public sealed record WeaponStats(
    RecordId Skill,
    int Damage,
    int Range,
    int Hit,
    RecordId Animation,
    RecordId? DamageType = null,
    Missile? Missile = null,
    bool StrikesInMelee = false,
    IReadOnlyList<RecordId>? Sounds = null);

/// <summary>
/// A weapon that flies (<c>Combatant.LoadWeaponScene</c>, <c>0x80066CF8</c>, and <c>Combatant.WeaponMuzzle</c>, <c>0x80066DD0</c>).
/// </summary>
/// <param name="Scene">What flies: a bow's arrow (<c>aidyn:scene/0x348F</c>), or null for the weapon's own model (<see cref="ItemRecord.Model"/>), as a thrown one does.</param>
/// <param name="Speed">Its muzzle number, the first of <c>WeaponMuzzle</c>'s: the higher, the faster and flatter it flies.</param>
/// <param name="Turn">Degrees it's rolled about its own X axis as it flies, before it's turned along its flight, <c>WeaponMuzzle</c>'s fourth: 90 for the Javelin, else 0.</param>
public sealed record Missile(RecordId? Scene, float Speed, float Turn = 0);

/// <summary>
/// How a potion is used (ADR 0021). The base game's potions are drunk anywhere but Stealth, which C-up's menu leaves out
/// (<c>CombatMenu.ListPotions</c>, <c>0x80088190</c>, skips kind 16), and the four flasks are thrown.
/// </summary>
[Flags]
public enum PotionUse
{
    None = 0,
    /// <summary>Drunk from the pause menu's Party tab in trek (<c>Entity::UsePotion</c>).</summary>
    DrinkInTrek = 1,
    /// <summary>Drunk from C-up's menu in a battle, by the one whose turn it is (<c>Combatant.DrinkPotion</c>).</summary>
    DrinkInBattle = 2,
    /// <summary>
    /// Thrown in a battle, at the other side, taken in hand from the pause menu (<c>Combatant.ThrowFlask</c>): its effects land on the
    /// one it hits, and its blast and corrosion burst about where it lands. Never drunk.
    /// </summary>
    Throw = 4,
}

/// <summary>What an item is (the original's <c>DBTypeEnum</c>, the high byte of its ID), which decides where it's worn.</summary>
public enum ItemKind
{
    /// <summary>Reagents, hides, letters, maps and other things that aren't worn.</summary>
    Misc = 1,
    /// <summary>Body armor.</summary>
    Body = 5,
    Shield = 6,
    Weapon = 7,
    Helmet = 9,
    Cloak = 10,
    Gloves = 11,
    /// <summary>Two can be worn (<c>itemtype_ring</c>).</summary>
    Ring = 12,
    /// <summary>Wands, and the rope, banner and instruments, which are worn in the same slot.</summary>
    Wand = 13,
    Belt = 14,
    Boots = 15,
    Potion = 16,
    /// <summary>Three that carry a spell can be worn (<c>itemtype_scroll</c>); outside combat, using one learns its spell.</summary>
    Scroll = 17,
    Key = 18,
    Amulet = 19,
}

/// <summary>
/// What an item does for whoever wears or wields it (ADR 0031). Each part is optional.
/// </summary>
/// <param name="Aspect">Only a character of this aspect can equip it; <see cref="Aspect.None"/> is anyone (<c>Party::GetEquipError</c>).</param>
/// <param name="Stats">
/// What it adds to each stat (<c>aidyn:stat/0</c>–<c>5</c>) while worn (<c>Entity::ApplyEquipment</c>; armor's Dexterity penalty
/// <c>Stats.AddModdedOnly</c>, <c>0x8007B498</c>): to the modified value, and for Endurance to the base value too, so it changes
/// maximum hit points. Stamina isn't raised: it's a battery that casting spells draws on before the caster's Stamina, and it's used
/// up (<c>Entity::EquipStamina</c>).
/// </param>
/// <param name="Skills">
/// What it adds to each skill (<c>aidyn:skill/n</c>) while worn, if the wearer's base level is above 0 (<c>Entity.ApplyItem</c>,
/// <c>0x8006F2A8</c>), or whatever it is for the kinds in <see cref="InventoryRecord.SkillBonusesApplyUnlearnt"/>.
/// </param>
/// <param name="Spell">A spell its wearer can cast from it.</param>
/// <param name="Enchantment">
/// A spell that's on the wearer while armor or a worn item is (<c>Entity::ApplyEquipEffect</c>), such as the Haste Amulet's. A
/// weapon's is cast when it strikes.
/// </param>
/// <param name="Resistances">
/// What each element's (<c>aidyn:element/n</c>) damage to the wearer is multiplied by, with their own resistances
/// (<c>Combatant.ApplyResistance</c>, <c>0x80062704</c>; <c>Battle.ApplyResistance</c>).
/// </param>
public sealed record ItemBonuses(
    Aspect Aspect = Aspect.None,
    IReadOnlyDictionary<RecordId, int>? Stats = null,
    IReadOnlyDictionary<RecordId, int>? Skills = null,
    ItemSpell? Spell = null,
    ItemEnchantment? Enchantment = null,
    IReadOnlyDictionary<RecordId, float>? Resistances = null)
{
    /// <summary>What it adds to <paramref name="stat"/>, 0 for none.</summary>
    public int Stat(RecordId stat) => Stats?.GetValueOrDefault(stat) ?? 0;
}

/// <summary>A spell an item lets its wearer cast (<c>SpellCharges</c>).</summary>
/// <param name="Level">The level it's cast at, whatever the caster's Wizard.</param>
/// <param name="Charges">How many casts it has. Wands and scrolls use them up.</param>
public sealed record ItemSpell(RecordId Spell, int Level, int Charges);

/// <summary>A spell always on while an item is worn (<c>Temp_enchant</c> with no timer).</summary>
public sealed record ItemEnchantment(RecordId Spell, int Level);

/// <summary>Where the party's inventory holds an item, and what the loot window does with it.</summary>
/// <param name="OwnSlot">
/// It has a slot of its own and doesn't take one of the inventory's general slots
/// (<see cref="InventoryRecord.GeneralSlots"/>): reagents, hides, scrolls, potions, key items and some
/// unique gear (<c>PartyInventory::AddKeyItem</c>, <c>AddKeyEquipment</c>).
/// </param>
/// <param name="MustTake">Loot that holds it can't be left behind (<c>IInventory::HasNoKeyItem</c>).</param>
public sealed record ItemTraits(bool OwnSlot = false, bool MustTake = false);

/// <summary>The flags a key item sets (<c>PartyInventory::AddKeyItem2</c>).</summary>
/// <param name="Held">Set while the party has it, and cleared when the last is taken. A save restores the item from it (<c>LoadKeyItems</c>).</param>
/// <param name="Region">A world map region it reveals, set when it's gained and never cleared.</param>
public sealed record KeyItemFlags(RecordId Held, RecordId? Region = null);

/// <summary>
/// A loot table (<c>aidyn:loot/0x08…</c>, the original's <c>ChestDB</c>): what a container gives
/// when opened, and what beaten enemies drop. See <c>get_chest_loot</c>.
/// </summary>
/// <param name="GoldMin">
/// Gold is rolled from here to <paramref name="GoldMax"/>, then multiplied by <see cref="CombatRecord.LootGoldMultiplier"/>: a
/// container's at once, a battle's once its chests are all rolled.
/// </param>
/// <param name="ArmorChance">
/// Percent chance that a beaten enemy drops its body armor; <paramref name="ShieldChance"/> its shield,
/// and <paramref name="WeaponChances"/> each of its weapons, by the slot it holds it in (<see cref="EntityWeapons"/>).
/// </param>
/// <param name="ReagentChance">Percent chance of a reagent, picked from spice, herb and gemstone, between the two counts.</param>
/// <param name="Drops">
/// Rolls in order, each a percent chance of an item and then its count. The original has six, of which only the first two have a
/// count of their own; the importer leaves out its empty ones, whose chance is always 0, so they never roll.
/// </param>
public sealed record LootRecord(
    RecordId Id,
    string Name,
    int GoldMin,
    int GoldMax,
    int ArmorChance,
    int ShieldChance,
    WeaponChances WeaponChances,
    int ReagentChance,
    int ReagentMin,
    int ReagentMax,
    IReadOnlyList<LootDrop> Drops);

/// <summary>Percent chances that a beaten enemy drops the weapon in each of its slots (<see cref="EntityWeapons"/>), each rolled apart (<c>Combat.RollEquipmentDrops</c>, <c>0x8005DC78</c>).</summary>
public sealed record WeaponChances(int Joins = 0, int Spare = 0, int Fights = 0);

/// <summary>One of a loot table's rolls: a percent chance of an item, then how many, from <see cref="Min"/> to <see cref="Max"/>.</summary>
/// <param name="Chance">Percent; 0 never rolls.</param>
/// <param name="Min">The fewest it gives, 1 if not given.</param>
/// <param name="Max">The most it gives, <see cref="Min"/> if not given. A count of one number draws nothing from the random numbers.</param>
public sealed record LootDrop(RecordId Item, int Chance, int? Min = null, int? Max = null)
{
    /// <summary>The fewest it gives.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Least => Min ?? 1;

    /// <summary>The most it gives.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Most => Max ?? Least;
}

/// <summary>
/// A shop (<c>aidyn:shop/&lt;n&gt;</c>, by its position in the original's <c>ShopDB</c>): what a
/// shopkeeper sells (<c>InitShop</c>). A shop has every item in stock without end.
/// </summary>
/// <param name="Shopkeeper">The entity who runs it. A script's shop command opens the shop of whoever is speaking; the first shop of theirs counts.</param>
/// <param name="Stock">
/// What it sells, in order, each at its price times a multiplier. When an item is listed twice, the
/// original multiplies the one entry's price again.
/// </param>
public sealed record ShopRecord(RecordId Id, RecordId Shopkeeper, [property: KeyedList(nameof(ShopStock.Item))] IReadOnlyList<ShopStock> Stock);

/// <param name="PriceMultiplier">One of ×0.5, ×0.75, ×1, ×1.25, ×1.5, ×2, ×3 and ×5 in the base game.</param>
public sealed record ShopStock(RecordId Item, float PriceMultiplier = 1);

/// <summary>
/// Something a skill makes from items in the inventory (<c>aidyn:recipe/&lt;n&gt;</c>): a potion the
/// Alchemist brews (<c>potionRecipies</c>), or armor the Mechanic makes from a hide (<c>ArmorRecipies</c>).
/// </summary>
/// <param name="Skill">Alchemist (<c>aidyn:skill/0</c>) or Mechanic (<c>aidyn:skill/4</c>), whose check makes it.</param>
/// <param name="Product">What it makes, one at a time.</param>
/// <param name="Ingredients">What it uses up, whether or not the check succeeds.</param>
/// <param name="Level">
/// The skill level it needs before it's offered: the Alchemist's modified level, or the Mechanic's base
/// level (<c>WidgetPotionCraft</c>, <c>CanMakeArmor</c>).
/// </param>
/// <param name="Step">The least step of the skill's check that makes it (see <c>gSkillCheck</c>). 0 for every potion.</param>
public sealed record RecipeRecord(
    RecordId Id,
    RecordId Skill,
    RecordId Product,
    [property: KeyedList(nameof(Ingredient.Item))] IReadOnlyList<Ingredient> Ingredients,
    int Level,
    int Step);

public sealed record Ingredient(RecordId Item, int Count);

/// <summary>
/// How the party carries, buys and finds items (<c>aidyn:inventory/0</c>, the only one): facts from the
/// original's code (<c>PartyInventory</c>, <c>GenericInventory</c>, <c>WidgetBarter</c>, <c>get_chest_loot</c>).
/// </summary>
/// <param name="StackLimit">The most of one item a stack holds (<c>IncItemQuantity</c>).</param>
/// <param name="GeneralSlots">
/// How many different items the inventory holds, besides those with a slot of their own
/// (<see cref="ItemTraits.OwnSlot"/>).
/// </param>
/// <param name="Spice">The reagents: what loot's reagent roll picks from, what Alchemists and Healers use, and what herb, spice and gem sources give.</param>
/// <param name="DefaultIcon">The icon of an item with none of its own (<c>GetItemImage</c>).</param>
/// <param name="MerchantStep">
/// Each level of Merchant the shopkeeper has over the party's best moves prices by this much: buying
/// costs the price × (1 + steps × this), and selling pays the price × (1 − steps × this) ×
/// <paramref name="SellShare"/> (<c>Party::Barter</c>, <c>WidgetInvShop::GetGoldPrice</c>).
/// </param>
/// <param name="SkillBonusesApplyUnlearnt">
/// The kinds of item whose skill bonuses (<see cref="ItemBonuses.Skills"/>) count even while the wearer's base level in the skill
/// is 0: body armor and shields, whose Stealth the original adds whatever the wearer's (<c>Skills.AddModifiedNotify</c>,
/// <c>0x8007A6EC</c>), where it adds any other item's skill bonus only to a skill above 0 (<c>Entity.ApplyItem</c>, <c>0x8006F2A8</c>).
/// </param>
/// <param name="FirstPotionFlag">Set when the party first gains a potion in a shop or loot, which shows help (a trigger, 2.10).</param>
/// <param name="Messages">What the pause menu says when a potion or scroll can't be used, which the original has in code.</param>
public sealed record InventoryRecord(
    RecordId Id,
    int StackLimit,
    int GeneralSlots,
    RecordId Spice,
    RecordId Herb,
    RecordId Gemstone,
    RecordId DefaultIcon,
    float MerchantStep,
    float SellShare,
    IReadOnlyList<ItemKind> SkillBonusesApplyUnlearnt,
    RecordId FirstPotionFlag,
    InventoryMessages? Messages = null);

/// <param name="PotionNotNow">A potion that would do nothing now: one that heals at full health, and so on (<c>Entity::CanUsePotion</c>).</param>
/// <param name="PotionNotInTrek">A flask, or a potion with no effect, drunk outside combat.</param>
/// <param name="ScrollNoSpell">A scroll without a spell, read (<c>Party::UseScroll</c>): the Lighthouse scroll.</param>
public sealed record InventoryMessages(string PotionNotNow, string PotionNotInTrek, string ScrollNoSpell);
