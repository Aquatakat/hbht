using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Items;

/// <summary>
/// A weapon, piece of armor or other item, whichever record it comes from, with what the rules read
/// of it (the original's <c>ItemInstance</c> and its kinds).
/// </summary>
public sealed class Item
{
    private Item(RecordId id, string name, RecordId icon, int price, ItemKind kind, GearBonuses? bonuses, ItemTraits? traits)
    {
        Id = id;
        Name = name;
        Icon = icon;
        Price = price;
        Kind = kind;
        Bonuses = bonuses ?? new GearBonuses();
        Traits = traits ?? new ItemTraits();
    }

    public RecordId Id { get; }

    public string Name { get; }

    public RecordId Icon { get; }

    /// <summary>Its base price, before a shop's multiplier or the Merchant skill.</summary>
    public int Price { get; }

    public ItemKind Kind { get; }

    public GearBonuses Bonuses { get; }

    public ItemTraits Traits { get; }

    public WeaponRecord? Weapon { get; private init; }

    /// <summary>Body armor or a shield.</summary>
    public ArmorRecord? Armor { get; private init; }

    /// <summary>Any other item.</summary>
    public ItemRecord? Gear { get; private init; }

    /// <summary>The flags a key item sets.</summary>
    public KeyItemFlags? KeyItem => Gear?.KeyItem;

    /// <summary>What wearing it adds to the wearer's protection: armor's, or a worn item's.</summary>
    public int Protection => Armor?.Protection ?? Gear?.Protection ?? 0;

    /// <summary>What a worn item adds to the wearer's damage. A weapon's own damage is combat's (2.5).</summary>
    public int Damage => Gear?.Damage ?? 0;

    /// <summary>The base Strength wearing or wielding it needs.</summary>
    public int RequiredStrength => Weapon?.RequiredStrength ?? Gear?.RequiredStrength ?? 0;

    /// <summary>The base Intelligence wearing it needs.</summary>
    public int RequiredIntelligence => Gear?.RequiredIntelligence ?? 0;

    /// <summary>
    /// How much Stamina its battery holds when full: its stat bonus, if that's to Stamina
    /// (<c>get_equip_stamMod</c>). Null for an item without one.
    /// </summary>
    public int? BatteryCapacity => Bonuses.Stat is { } stat && stat.Stat == StaminaStat ? stat.Amount : null;

    /// <summary>Whether it's worn in the gear slots rather than as the weapon, armor or shield.</summary>
    public bool IsGear => Kind is ItemKind.Helmet or ItemKind.Cloak or ItemKind.Gloves or ItemKind.Ring or ItemKind.Wand
        or ItemKind.Belt or ItemKind.Boots or ItemKind.Amulet or ItemKind.Scroll;

    internal static readonly RecordId StaminaStat = RecordId.Decimal(RecordTypes.Stat, 5);

    public static Item From(WeaponRecord w) =>
        new(w.Id, w.Name, w.Icon, w.Price, ItemKind.Weapon, w.Bonuses, w.Traits) { Weapon = w };

    public static Item From(ArmorRecord a) =>
        new(a.Id, a.Name, a.Icon, a.Price, a.Slot == ArmorSlot.Shield ? ItemKind.Shield : ItemKind.Armor, a.Bonuses, a.Traits) { Armor = a };

    public static Item From(ItemRecord i) => new(i.Id, i.Name, i.Icon, i.Price, i.Kind, i.Bonuses, i.Traits) { Gear = i };

    public override string ToString() => Name;
}

/// <summary>Every item the rules can name, and the inventory's rules.</summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<RecordId, Item> _items;

    public ItemCatalog(IEnumerable<WeaponRecord> weapons, IEnumerable<ArmorRecord> armor, IEnumerable<ItemRecord> items, InventoryRecord rules)
    {
        _items = weapons.Select(Item.From).Concat(armor.Select(Item.From)).Concat(items.Select(Item.From)).ToDictionary(i => i.Id);
        Rules = rules;
    }

    public InventoryRecord Rules { get; }

    public IEnumerable<Item> All => _items.Values;

    public Item this[RecordId id] => _items.TryGetValue(id, out var item) ? item : throw new KeyNotFoundException($"No item {id}.");

    public Item? TryGet(RecordId id) => _items.GetValueOrDefault(id);

    /// <summary>The installed plugins' items.</summary>
    public static ItemCatalog From(PluginSet plugins) => new(
        plugins.All<WeaponRecord>(RecordTypes.Weapon), plugins.All<ArmorRecord>(RecordTypes.Armor), plugins.All<ItemRecord>(RecordTypes.Item),
        plugins.Singleton<InventoryRecord>(RecordTypes.Inventory));
}
