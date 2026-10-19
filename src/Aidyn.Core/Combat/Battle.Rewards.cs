using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>
/// What a won battle gives (<c>Combat.TallyRewards</c>, <c>0x8005E2AC</c>): the gold, the experience each living member gained, the
/// loot, and who gained a level, for the post-battle window and the loot window (pause states 5 and 6).
/// </summary>
/// <param name="Gold">The gold pool, doubled, which the party has already been given.</param>
/// <param name="Experience">The experience pool, before the award's multiplier: each living member gained it times 1.5.</param>
/// <param name="Loot">The loot, a pile the loot window offers.</param>
/// <param name="Levelled">The members whose level the award raised, in the party's order.</param>
public sealed record BattleSpoils(int Gold, int Experience, ItemPile Loot, IReadOnlyList<Character> Levelled);

/// <summary>The rewards of a won battle (<c>Combat.TallyRewards</c> and the loot it rolls). docs/systems/combat.md's "Rewards".</summary>
public sealed partial class Battle
{
    /// <summary>The gold pool (<c>+0x20</c>): every chest's gold, doubled once all are rolled.</summary>
    public int GoldPool { get; private set; }

    /// <summary>The experience pool (<c>+0x24</c>): every enemy's experience and what its loot is worth.</summary>
    public int ExperiencePool { get; private set; }

    /// <summary>The loot (<c>+0x4FB4</c>), made as the battle's won.</summary>
    public ItemPile? Loot { get; private set; }

    private readonly List<Character> _levelled = [];

    /// <summary>The members whose level the award raised, in the party's order.</summary>
    public IReadOnlyList<Character> Levelled => _levelled;

    /// <summary>
    /// The rewards (<c>Combat.TallyRewards</c>, <c>0x8005E2AC</c>, as the battle's won): for every entry of the enemy list, killed,
    /// fled or never come alike, its experience and its chest (<see cref="AddChestLoot"/>); then the bonus loot; then each living
    /// member gains the whole experience pool (<see cref="Character.AddExp"/>); and last the gold pool is doubled. The draws are on
    /// the loot stream.
    /// </summary>
    internal void TallyRewards()
    {
        Loot = new ItemPile(Game.Party.Inventory.Catalog);
        GoldPool = ExperiencePool = 0;
        foreach (var id in Enemies.Take(MaxEnemies))
        {
            if (Game.Plugins.TryGet<EntityRecord>(id) is not { } entity)
                continue;
            ExperiencePool += entity.Experience;
            if (entity.LootCategory != 0 && Game.Plugins.TryGet<LootRecord>(ChestOf(entity.LootCategory)) is { } chest)
                AddChestLoot(chest, entity);
        }
        // Combat.AddBonusLoot (0x8005E238): a chest rolled with no entity, or one of an item, which is worth no experience.
        if (Setup.BonusLoot is { } bonus)
        {
            if (bonus.Type == RecordTypes.Loot && Game.Plugins.TryGet<LootRecord>(bonus) is { } chest)
                AddChestLoot(chest, null);
            else
                Add(bonus, 1);
        }
        _levelled.Clear();
        foreach (var member in Game.Party.Slots)
        {
            if (member is null || member.IsDead)
                continue;
            var level = member.Stats.Base(Stat.Level);
            member.AddExp(ExperiencePool);
            if (member.Stats.Base(Stat.Level) > level)
                _levelled.Add(member);
        }
        GoldPool <<= 1;
    }

    /// <summary>An entity's chest byte as its loot table: the chest DB's ID <c>0x7FF</c> + the byte.</summary>
    public static RecordId ChestOf(int lootCategory) => RecordId.Hex(RecordTypes.Loot, 0x7FF + lootCategory);

    /// <summary>
    /// An enemy's chest (<c>Combat.AddChestLoot</c>, <c>0x8005E0F0</c>), rolled in this order: its gold; the enemy's own armor,
    /// shield and weapons, each by its chance (<c>Combat.RollEquipmentDrops</c>, <c>0x8005DC78</c>, not for the bonus loot's,
    /// which has no enemy); a reagent, by its chance, then which (spice, herb or gemstone) and how many; then the six drops
    /// (<c>Combat.RollChestItem</c>, <c>0x8005DF38</c>), the first two between their counts. What each item is worth goes on the
    /// experience pool, a reagent's once whatever the count.
    /// </summary>
    /// <remarks>
    /// A container's roll (<see cref="Containers.Roll"/>) is the same table, read otherwise: it picks the reagent before rolling
    /// its chance, and never reads the equipment chances.
    /// </remarks>
    private void AddChestLoot(LootRecord chest, EntityRecord? entity)
    {
        var random = Game.Random.Loot;
        GoldPool += random.Range(chest.GoldMin, chest.GoldMax);
        if (entity is not null)
            RollEquipmentDrops(chest, entity, random);
        if (chest.ReagentChance != 0 && random.RollD(1, 100) <= chest.ReagentChance)
        {
            var rules = Game.Plugins.Singleton<InventoryRecord>(RecordTypes.Inventory);
            RecordId[] reagents = [rules.Spice, rules.Herb, rules.Gemstone];
            var reagent = reagents[random.Range(0, 2)];
            var count = random.Range(chest.ReagentMin, chest.ReagentMax);
            Add(reagent, count);
            ExperiencePool += ExperienceValue(reagent);
        }
        for (var i = 0; i < chest.Drops.Count; i++)
        {
            var drop = chest.Drops[i];
            if (drop.Chance == 0 || random.RollD(1, 100) > drop.Chance)
                continue;
            var count = i < 2 ? random.Range(drop.Min, drop.Max) : 1;
            if (drop.Item is not { } item)
                continue;
            Add(item, count);
            ExperiencePool += count * ExperienceValue(item);
        }
    }

    /// <summary>
    /// The enemy's own gear (<c>Combat.RollEquipmentDrops</c>, <c>0x8005DC78</c>): its armor and shield, then its three weapons, each
    /// rolled by the chest's chance if it has one and the enemy the item, a weapon only of a class that drops
    /// (<see cref="CombatRecord.DroppedClasses"/>).
    /// </summary>
    private void RollEquipmentDrops(LootRecord chest, EntityRecord entity, IRandom random)
    {
        Roll(entity.Armor, chest.ArmorChance);
        Roll(entity.Shield, chest.ShieldChance);
        for (var i = 0; i < 3; i++)
        {
            var weapon = i < entity.Weapons.Count ? entity.Weapons[i] : null;
            var chance = i < chest.WeaponChances.Count ? chest.WeaponChances[i] : 0;
            if (weapon is { } w && Game.Plugins.TryGet<WeaponRecord>(w) is { } record && _rules?.DroppedClasses?.Contains(record.Class) != true)
                continue;
            Roll(weapon, chance);
        }

        void Roll(RecordId? item, int chance)
        {
            if (chance == 0 || item is not { } given || random.RollD(1, 100) > chance)
                return;
            Add(given, 1);
            ExperiencePool += ExperienceValue(given);
        }
    }

    /// <summary>
    /// What an item adds to the experience pool as it's looted: a weapon's <see cref="WeaponRecord.ExpModifier"/>, armor's
    /// <see cref="ArmorRecord.ExpBonus"/>, and another item's <see cref="ItemRecord.ExpMultiplier"/>; a potion's nothing.
    /// </summary>
    public int ExperienceValue(RecordId item) => item.Type switch
    {
        RecordTypes.Weapon => Game.Plugins.TryGet<WeaponRecord>(item)?.ExpModifier ?? 0,
        RecordTypes.Armor => Game.Plugins.TryGet<ArmorRecord>(item)?.ExpBonus ?? 0,
        RecordTypes.Item => Game.Plugins.TryGet<ItemRecord>(item) is { Kind: not ItemKind.Potion } other ? other.ExpMultiplier : 0,
        _ => 0,
    };

    /// <summary>Puts <paramref name="count"/> of an item in the loot, if a plugin has it and the pile has room.</summary>
    private void Add(RecordId item, int count)
    {
        if (count > 0 && Game.Items.TryGet(item) is not null)
            Loot!.Add(item, count);
    }

    /// <summary>
    /// The bosses' flags (<c>Combat.SetKillFlags</c>, <c>0x80067860</c>): for each of the enemy list's first four that's a boss
    /// (<see cref="CombatRecord.Bosses"/>), its flag, whether it fell or not. A win sets them twice, as the battle's won and once
    /// it has faded out.
    /// </summary>
    internal void SetKillFlags()
    {
        if (_rules?.Bosses is not { } bosses)
            return;
        foreach (var id in Enemies.Take(OnField))
        {
            if (bosses.FirstOrDefault(b => b.Entity == id) is { } boss)
                Game.Flags.TrySet(boss.Flag, true);
        }
    }
}
