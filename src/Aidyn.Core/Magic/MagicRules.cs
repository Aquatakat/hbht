using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Magic;

/// <summary>
/// What spells, potions and enchantments are made of (ADR 0020): the magic record's numbers, and the spell and potion records
/// by ID. Characters reach it through their rules.
/// </summary>
public sealed class MagicRules
{
    private readonly Dictionary<RecordId, SpellRecord> _spells;
    private readonly Dictionary<RecordId, ItemRecord> _potions;

    public MagicRules(MagicRecord record, IEnumerable<SpellRecord> spells, IEnumerable<ItemRecord> potions, InventoryRecord? inventory = null)
    {
        Record = record;
        _spells = spells.ToDictionary(s => s.Id);
        _potions = potions.Where(p => p.Kind == ItemKind.Potion).ToDictionary(p => p.Id);
        Inventory = inventory;
    }

    public MagicRecord Record { get; }

    /// <summary>The inventory's rules, which name the reagents' items.</summary>
    public InventoryRecord? Inventory { get; }

    public IEnumerable<SpellRecord> Spells => _spells.Values;

    public SpellRecord? Spell(RecordId id) => _spells.GetValueOrDefault(id);

    public ItemRecord? Potion(RecordId id) => _potions.GetValueOrDefault(id);

    /// <summary>The item a reagent is, or null for none (<c>0x800E29CC</c>).</summary>
    public RecordId? ReagentItem(Reagent reagent) => reagent switch
    {
        Reagent.Spice => Inventory?.Spice,
        Reagent.Herb => Inventory?.Herb,
        Reagent.Gemstone => Inventory?.Gemstone,
        _ => null,
    };

    /// <summary>The rules from the loaded plugins, or null if none has the magic record.</summary>
    public static MagicRules? From(PluginSet plugins)
    {
        if (plugins.TryGet<MagicRecord>(RecordId.Decimal(RecordTypes.Magic, 0)) is not { } record)
            return null;
        return new MagicRules(record, plugins.All<SpellRecord>(RecordTypes.Spell), plugins.All<ItemRecord>(RecordTypes.Item),
            plugins.TryGet<InventoryRecord>(RecordId.Decimal(RecordTypes.Inventory, 0)));
    }
}
