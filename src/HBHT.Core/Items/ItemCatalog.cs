using HBHT.Core.Characters;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Items;

/// <summary>
/// An item (ADR 0031): a weapon, a piece of armor, a shield or any other, with what the rules read of it (the original's
/// <c>ItemInstance</c> and its kinds).
/// </summary>
public sealed class Item
{
    private Item(ItemRecord record, bool skillBonusesApplyUnlearnt)
    {
        Record = record;
        SkillBonusesApplyUnlearnt = skillBonusesApplyUnlearnt;
        Bonuses = record.Bonuses ?? new ItemBonuses();
        Traits = record.Traits ?? new ItemTraits();
    }

    /// <summary>The record it's made from.</summary>
    public ItemRecord Record { get; }

    public RecordId Id => Record.Id;

    public string Name => Record.Name;

    public RecordId Icon => Record.Icon;

    /// <summary>Its base price, before a shop's multiplier or the Merchant skill.</summary>
    public int Price => Record.Price;

    public ItemKind Kind => Record.Kind;

    public ItemBonuses Bonuses { get; }

    public ItemTraits Traits { get; }

    /// <summary>What only a weapon has, or null for any other item.</summary>
    public WeaponStats? Weapon => Record.Weapon;

    /// <summary>Body armor or a shield.</summary>
    public bool IsArmor => Kind is ItemKind.Body or ItemKind.Shield;

    /// <summary>The flags a key item sets.</summary>
    public KeyItemFlags? KeyItem => Record.KeyItem;

    /// <summary>What wearing it adds to the wearer's protection: armor's, or a worn item's.</summary>
    public int Protection => Record.Protection;

    /// <summary>What a worn item adds to the wearer's damage. A weapon's own damage is combat's.</summary>
    public int Damage => Record.Damage;

    /// <summary>The base Strength wearing or wielding it needs.</summary>
    public int RequiredStrength => Record.Requires(StatId.Record(Stat.Strength));

    /// <summary>The base Intelligence wearing it needs.</summary>
    public int RequiredIntelligence => Record.Requires(StatId.Record(Stat.Intelligence));

    /// <summary>
    /// How much Stamina its battery holds when full: its stat bonus, if that's to Stamina
    /// (<c>get_equip_stamMod</c>). Null for an item without one.
    /// </summary>
    public int? BatteryCapacity => Bonuses.Stats?.TryGetValue(StaminaStat, out var amount) == true ? amount : null;

    /// <summary>Whether it's worn in the gear slots rather than as the weapon, armor or shield.</summary>
    public bool IsGear => Kind is ItemKind.Helmet or ItemKind.Cloak or ItemKind.Gloves or ItemKind.Ring or ItemKind.Wand
        or ItemKind.Belt or ItemKind.Boots or ItemKind.Amulet or ItemKind.Scroll;

    /// <summary>The Stamina stat, whose bonus is a battery.</summary>
    public static readonly RecordId StaminaStat = StatId.Record(Stat.Stamina);

    /// <summary>
    /// Its skill bonuses count even for a skill the wearer's base level is 0 in: its kind is one of the inventory's
    /// <see cref="InventoryRecord.SkillBonusesApplyUnlearnt"/>.
    /// </summary>
    public bool SkillBonusesApplyUnlearnt { get; }

    /// <param name="rules">The inventory's rules, whose kinds say whether its skill bonuses count unlearnt; without them, they don't.</param>
    public static Item From(ItemRecord record, InventoryRecord? rules = null) =>
        new(record, rules?.SkillBonusesApplyUnlearnt.Contains(record.Kind) == true);

    public override string ToString() => Name;
}

/// <summary>Every item the rules can name, and the inventory's rules.</summary>
public sealed class ItemCatalog
{
    private readonly Dictionary<RecordId, Item> _items;

    public ItemCatalog(IEnumerable<ItemRecord> items, InventoryRecord rules)
    {
        _items = items.Select(i => Item.From(i, rules)).ToDictionary(i => i.Id);
        Rules = rules;
    }

    public InventoryRecord Rules { get; }

    public IEnumerable<Item> All => _items.Values;

    public Item this[RecordId id] => _items.TryGetValue(id, out var item) ? item : throw new KeyNotFoundException($"No item {id}.");

    public Item? TryGet(RecordId id) => _items.GetValueOrDefault(id);

    /// <summary>The installed plugins' items.</summary>
    public static ItemCatalog From(PluginSet plugins) => new(plugins.All<ItemRecord>(RecordTypes.Item), plugins.Singleton<InventoryRecord>())
    {
        LootGoldMultiplier = plugins.TrySingleton<CombatRecord>()?.LootGoldMultiplier ?? 1,
    };

    /// <summary>
    /// What a container's loot table's gold is multiplied by (<see cref="CombatRecord.LootGoldMultiplier"/>), which a battle's
    /// rewards read from the combat record too; 1 without one.
    /// </summary>
    public int LootGoldMultiplier { get; init; } = 1;
}
