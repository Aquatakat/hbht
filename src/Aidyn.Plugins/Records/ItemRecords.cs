namespace Aidyn.Plugins.Records;

/// <summary>
/// A weapon (<c>aidyn:weapon/0x07…</c>), from the weapon DB (<c>WeaponDB::Load</c>, <c>createTempWeapon</c>).
/// Monsters' natural weapons, such as bites and breath, are weapons too.
/// </summary>
/// <param name="Class">Its weapon skill: equipping it needs that skill above 0 (<c>Entity::canEquipWeapon</c>).</param>
/// <param name="RequiredStrength">Equipping it needs at least this base Strength.</param>
/// <param name="Hit">Combat reads it (2.5).</param>
/// <param name="Range">Its reach in combat, the ROM's byte × 5 / 3 up to 255 (<c>WeaponDB::Load</c>).</param>
/// <param name="Animation">Which attack animation it uses.</param>
/// <param name="ExpModifier">0x20 on the Lodin sword. What reads it isn't known.</param>
/// <param name="DamageType">The element of its damage.</param>
/// <param name="Fixed">
/// Once equipped, it can't be taken off (<c>Party::RemoveWeaponsFrom</c>): the Archmage Staff, which
/// is Niesen's <see cref="EntityRecord.SoleWeapon"/>.
/// </param>
public sealed record WeaponRecord(
    RecordId Id,
    string Name,
    RecordId Icon,
    int Price,
    WeaponClass Class,
    int RequiredStrength,
    int Hit,
    int Damage,
    int Range,
    int Animation,
    int ExpModifier,
    Element DamageType,
    GearBonuses Bonuses,
    ItemTraits? Traits = null,
    bool Fixed = false);

/// <summary>
/// Body armor or a shield (<c>aidyn:armor/0x05…</c> and <c>0x06…</c>), from the armor DB
/// (<c>ArmorDB::Load</c>, <c>make_temp_armor_3</c>).
/// </summary>
/// <param name="Defense">A shield's defense, which combat reads (2.5). 0 on body armor.</param>
/// <param name="Protection">What wearing it adds to protection from damage (<c>Entity::GetArmorProtect</c>).</param>
/// <param name="Dexterity">What wearing it adds to modified Dexterity: a penalty, or 0.</param>
/// <param name="Stealth">What wearing it adds to modified Stealth.</param>
/// <param name="Unk1D">Read into RAM with the rest, but nothing reads it there.</param>
/// <param name="ExpBonus">Read into RAM; what reads it isn't known.</param>
public sealed record ArmorRecord(
    RecordId Id,
    string Name,
    RecordId Icon,
    int Price,
    ArmorSlot Slot,
    int Defense,
    int Protection,
    int Dexterity,
    int Stealth,
    int Unk1D,
    int ExpBonus,
    GearBonuses Bonuses,
    ItemTraits? Traits = null);

/// <summary>
/// Any other item (<c>aidyn:item/…</c>): worn gear, wands, scrolls, keys and other key items, potions,
/// reagents and the rest. All but potions are from the item DB (<c>ItemDB::LoadItem</c>,
/// <c>make_GearInstance_2</c>); a potion is named by its own string table and priced by a table in code
/// (<c>ItemInstance::InitPotion</c>). What potions do comes with combat (2.5).
/// </summary>
/// <param name="Kind">What it is, which decides where it's worn. The original takes it from the ID's high byte.</param>
/// <param name="ExpMultiplier">Read into RAM; what reads it isn't known.</param>
/// <param name="Damage">What wearing it adds to the wearer's damage (<c>CharExp::damage</c>).</param>
/// <param name="Protection">What wearing it adds to the wearer's protection (<c>CharExp::protection</c>).</param>
/// <param name="RequiredStrength">Wearing it needs at least this base Strength (<c>Entity::GearMinStatCheck</c>).</param>
/// <param name="RequiredIntelligence">Wearing it needs at least this base Intelligence.</param>
/// <param name="KeyItem">The flags a key item sets while the party has it.</param>
/// <param name="Description">
/// What C-down's details say of it: an item's line in <c>aidyn:strings/0</c>, or a potion's in
/// <c>aidyn:strings/4</c>. Weapons and armor show their numbers instead.
/// </param>
public sealed record ItemRecord(
    RecordId Id,
    string Name,
    RecordId Icon,
    int Price,
    ItemKind Kind,
    int ExpMultiplier = 0,
    int Damage = 0,
    int Protection = 0,
    int RequiredStrength = 0,
    int RequiredIntelligence = 0,
    GearBonuses? Bonuses = null,
    ItemTraits? Traits = null,
    KeyItemFlags? KeyItem = null,
    string? Description = null);

/// <summary>
/// What an item is (the original's <c>DBTypeEnum</c>, the high byte of its ID). Weapons and armor have
/// records of their own; their kinds are here for rules that take any item.
/// </summary>
public enum ItemKind
{
    /// <summary>Reagents, hides, letters, maps and other things that aren't worn.</summary>
    Misc = 1,
    /// <summary>Body armor.</summary>
    Armor = 5,
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

/// <summary>Where a piece of armor is worn.</summary>
public enum ArmorSlot
{
    Body,
    Shield,
}

/// <summary>Weapon classes, which are also the weapon skills (<c>aidyn:weaponskill/0</c>–<c>10</c>).</summary>
public enum WeaponClass
{
    Bite,
    Breath,
    Claw,
    Hafted,
    Missile,
    Pole,
    Spit,
    Sting,
    Sword,
    Thrown,
    Tusk,
}

/// <summary>
/// What a weapon, piece of armor or worn item does for whoever wears it. Each part is optional.
/// </summary>
/// <param name="Aspect">Only a character of this aspect can equip it; <see cref="Aspect.None"/> is anyone (<c>Party::GetEquipError</c>).</param>
/// <param name="Stat">An attribute it raises or lowers while worn.</param>
/// <param name="Skill">A skill it raises or lowers while worn, if the wearer's base level is above 0.</param>
/// <param name="Spell">A spell its wearer can cast from it.</param>
/// <param name="Enchantment">
/// A spell that's on the wearer while armor or a worn item is (<c>Entity::ApplyEquipEffect</c>), such
/// as the Haste Amulet's. A weapon's is cast when it strikes. Spell effects come with combat (2.5).
/// </param>
/// <param name="Resistance">An element whose damage is multiplied for the wearer. Combat reads it (2.5).</param>
public sealed record GearBonuses(
    Aspect Aspect = Aspect.None,
    StatBonus? Stat = null,
    SkillBonus? Skill = null,
    ItemSpell? Spell = null,
    ItemEnchantment? Enchantment = null,
    Resistance? Resistance = null);

/// <summary>
/// An attribute an item changes while worn (<c>Entity::ApplyEquipment</c>).
/// </summary>
/// <param name="Stat">One of <c>aidyn:stat/0</c>–<c>5</c>.</param>
/// <param name="Amount">
/// What it adds to the modified value; for Endurance, to the base value too, so it changes maximum hit
/// points. Stamina isn't raised: it's a battery that casting spells draws on before the caster's
/// Stamina, and it's used up (<c>Entity::EquipStamina</c>).
/// </param>
public sealed record StatBonus(RecordId Stat, int Amount);

/// <param name="Skill">A skill, <c>aidyn:skill/0</c>–<c>11</c>.</param>
public sealed record SkillBonus(RecordId Skill, int Amount);

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
/// <param name="GoldMin">Gold is rolled from here to <paramref name="GoldMax"/> and doubled.</param>
/// <param name="ArmorChance">
/// Percent chance that a beaten enemy drops its body armor; <paramref name="ShieldChance"/> its shield,
/// and <paramref name="WeaponChances"/> its three weapons. Combat's rewards read them (2.5).
/// </param>
/// <param name="ReagentChance">Percent chance of a reagent, picked from spice, herb and gemstone, between the two counts.</param>
/// <param name="Drops">
/// Six rolls, each a percent chance of an item. Only the first two give a count between their bounds;
/// the rest give one. An empty roll still rolls its chance.
/// </param>
public sealed record LootRecord(
    RecordId Id,
    string Name,
    int GoldMin,
    int GoldMax,
    int ArmorChance,
    int ShieldChance,
    IReadOnlyList<int> WeaponChances,
    int ReagentChance,
    int ReagentMin,
    int ReagentMax,
    IReadOnlyList<LootDrop> Drops);

/// <param name="Item">Null for an empty roll.</param>
public sealed record LootDrop(RecordId? Item, int Chance, int Min = 1, int Max = 1);

/// <summary>
/// A shop (<c>aidyn:shop/&lt;n&gt;</c>, by its position in the original's <c>ShopDB</c>): what a
/// shopkeeper sells (<c>InitShop</c>). A shop has every item in stock without end.
/// </summary>
/// <param name="Shopkeeper">The entity who runs it. A script's shop command opens the shop of whoever is speaking; the first shop of theirs counts.</param>
/// <param name="Stock">
/// What it sells, in order, each at its price times a multiplier. When an item is listed twice, the
/// original multiplies the one entry's price again.
/// </param>
public sealed record ShopRecord(RecordId Id, RecordId Shopkeeper, IReadOnlyList<ShopStock> Stock);

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
/// <param name="Unk5">A potion recipe's sixth byte, 1 to 10. Nothing reads it.</param>
public sealed record RecipeRecord(
    RecordId Id,
    RecordId Skill,
    RecordId Product,
    IReadOnlyList<Ingredient> Ingredients,
    int Level,
    int Step,
    int? Unk5 = null);

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
/// <param name="Refused">Items no container, shop or loot window takes: the Cyclops Club (<c>GenericInventory::AddItem</c>).</param>
/// <param name="Replaced">Items that become another when gained: the Aspect potion, which becomes a Healing potion.</param>
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
    IReadOnlyList<RecordId> Refused,
    IReadOnlyList<ItemReplacement> Replaced,
    RecordId FirstPotionFlag,
    InventoryMessages? Messages = null);

public sealed record ItemReplacement(RecordId Item, RecordId With);

/// <param name="PotionNotNow">A potion that would do nothing now: one that heals at full health, and so on (<c>Entity::CanUsePotion</c>).</param>
/// <param name="PotionNotInTrek">A flask, or a potion with no effect, drunk outside combat.</param>
/// <param name="ScrollNoSpell">A scroll without a spell, read (<c>Party::UseScroll</c>): the Lighthouse scroll.</param>
public sealed record InventoryMessages(string PotionNotNow, string PotionNotInTrek, string ScrollNoSpell);
