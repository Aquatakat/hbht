using Aidyn.Core.Characters;
using Aidyn.Core.Flags;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Items;

/// <summary>What a loot table gave: gold, and items with their counts, in the order rolled.</summary>
public sealed record LootRoll(int Gold, IReadOnlyList<(RecordId Item, int Count)> Items);

/// <summary>How picking a lock went (<c>Party::Lockpicking</c>), and the message it shows.</summary>
public enum LockResult
{
    /// <summary>Picked: it cost the best thief 10 less their level in Stamina, at least 1.</summary>
    Picked,
    /// <summary>The party is empty.</summary>
    NoOne,
    /// <summary>The lock is 1 or 2 levels past the best thief's Thief (up to 10).</summary>
    JustBeyond,
    /// <summary>The lock is 3 to 5 levels past.</summary>
    Beyond,
    /// <summary>The lock is 6 or more levels past, or 10 or more.</summary>
    FarBeyond,
    /// <summary>The best thief hasn't the Stamina it costs.</summary>
    TooTired,
}

/// <summary>
/// Rules for containers, locks and traps (<c>loot_func</c>, <c>get_chest_loot</c>, <c>Party::Lockpicking</c>,
/// <c>Party::DisarmSkill</c>, <c>TrapMenu</c>). Locked doors use the same locks and traps.
/// </summary>
public static class Containers
{
    /// <summary>
    /// Rolls a loot table (<c>get_chest_loot</c>): gold between its bounds, doubled; a reagent, its kind
    /// picked before its chance is rolled; then the six drops, each rolling its chance if it has one. An
    /// empty drop that hits gives nothing.
    /// </summary>
    public static LootRoll Roll(LootRecord loot, InventoryRecord rules, IRandom random)
    {
        var gold = (ushort)(random.Range(loot.GoldMin, loot.GoldMax) << 1);
        var items = new List<(RecordId, int)>();
        RecordId[] reagents = [rules.Spice, rules.Herb, rules.Gemstone];
        var reagent = reagents[random.Range(0, 2)];
        Drop(reagent, loot.ReagentChance, loot.ReagentMin, loot.ReagentMax, counted: true);
        for (var i = 0; i < loot.Drops.Count; i++)
            Drop(loot.Drops[i].Item, loot.Drops[i].Chance, loot.Drops[i].Min, loot.Drops[i].Max, counted: i < 2);
        return new LootRoll(gold, items);

        // item_chances: a percent roll, then the count between the bounds for a counted drop.
        void Drop(RecordId? item, int chance, int min, int max, bool counted)
        {
            if (chance == 0 || random.RollD(1, 100) > chance)
                return;
            var count = counted ? random.Range(min, max) : 1;
            if (item is { } given && count != 0)
                items.Add((given, count));
        }
    }

    /// <summary>
    /// The Ranger's share of a herb, spice or gem source (<c>Party::HarvestSkill</c>), rolled as a chunk's
    /// objects are set up: the best Ranger's Intelligence + Ranger × 7, times d100 / 100, from 0 to 1.
    /// The best Ranger is the member with the highest modified level, the later on a tie, dead or not.
    /// </summary>
    public static float HarvestShare(Party party, IRandom random)
    {
        if (party.MostSkilled(Skill.Ranger) is not { } ranger)
            return 0;
        float skill = ranger.Stats.Modified(Stat.Intelligence) + ranger.Skills.Modified(Skill.Ranger) * 7;
        float roll = random.RollD(1, 100);
        skill *= roll / 100f;
        return skill > 1f ? 1f : skill < 0f ? 0f : skill;
    }

    /// <summary>How many a source of <paramref name="count"/> gives with the Ranger's share: truncated to a byte, and at least 1 (<c>get_loot_reagent</c>).</summary>
    public static int Harvest(int count, float share)
    {
        float given = count;
        given *= share;
        var whole = (byte)(uint)given;
        return whole == 0 ? 1 : whole;
    }

    /// <summary>
    /// Picks a lock (<c>Party::Lockpicking</c>): the best thief can pick a lock below their Thief level,
    /// counted up to 10, for 10 less that in Stamina, at least 1. Their Stamina pays it as damage would,
    /// without touching Endurance. The best thief is the member with the highest modified Thief, the later
    /// on a tie, dead or not.
    /// </summary>
    public static LockResult PickLock(Party party, int level)
    {
        if (party.MostSkilled(Skill.Thief) is not { } thief)
            return LockResult.NoOne;
        if (level >= 10)
            return LockResult.FarBeyond;
        var skill = Math.Min(thief.Skills.Modified(Skill.Thief), 10);
        if (skill - 1 < level)
        {
            var past = (sbyte)(level - skill);
            return past < 3 ? LockResult.JustBeyond : past < 6 ? LockResult.Beyond : LockResult.FarBeyond;
        }
        var cost = Math.Max(10 - skill, 1);
        if (thief.Stats.Modified(Stat.Stamina) < cost)
            return LockResult.TooTired;
        thief.Stats.ChangeCurrent(Stat.Stamina, -cost);
        return LockResult.Picked;
    }

    /// <summary>
    /// Tries to disarm a trap (<c>Party::DisarmSkill</c>). The mechanic is the member with the highest
    /// modified Mechanic above 0, the first on a tie, who has the Stamina it costs: 10 less their level, at
    /// least 1. They roll d100 against Intelligence × 4 + Mechanic × 10, and pay the Stamina if they pass;
    /// the step must reach the trap's level. With no mechanic, it fails.
    /// </summary>
    public static bool Disarm(Party party, int level, IRandom random)
    {
        Character? mechanic = null;
        var best = 0;
        foreach (var member in party.Members)
        {
            var skill = member.Skills.Modified(Skill.Mechanic);
            var cost = Math.Max(10 - skill, 1);
            if (best < skill && cost <= member.Stats.Modified(Stat.Stamina))
                (mechanic, best) = (member, skill);
        }
        if (mechanic is null)
            return false;
        var step = CheckAndPay(mechanic, mechanic.Stats.Modified(Stat.Intelligence) * 4 + best * 10, Math.Max(10 - best, 1), random);
        return level == 0 || step != 0 && step >= level;
    }

    /// <summary>
    /// A trap going off (<c>TrapMenu::TakeDamage</c>): each living member takes the trap's level × d4, at
    /// least 1, from Endurance alone, down to 1. A first d4 that gives less than 2 gives 1 without a
    /// second roll; otherwise a second d4 is the one that counts.
    /// </summary>
    public static IReadOnlyList<(Character Member, int Damage)> TrapDamage(Party party, int level, IRandom random)
    {
        var hurt = new List<(Character, int)>();
        foreach (var member in party.Members.Where(m => !m.IsDead))
        {
            var damage = level * random.RollD(1, 4) < 2 ? 1 : (short)(level * random.RollD(1, 4));
            var endurance = member.Stats.Modified(Stat.Endurance);
            member.Stats.ChangeCurrent(Stat.Endurance, damage < endurance ? -damage : 1 - endurance);
            hurt.Add((member, damage));
        }
        return hurt;
    }

    /// <summary>
    /// A skill check that costs Stamina (<c>Party::DisarmDamage</c>, <c>ArmorCraftCheck</c> and others): d100
    /// against <paramref name="bonus"/>; below it, the step for what's left, if the member can pay
    /// <paramref name="cost"/>, which they then do. Otherwise 0.
    /// </summary>
    internal static int CheckAndPay(Character member, int bonus, int cost, IRandom random)
    {
        var total = (ushort)bonus;
        var roll = random.RollD(1, 100);
        if (roll >= total)
            return 0;
        var step = Party.SkillCheckStep((short)(total - roll));
        if (cost > member.Stats.Modified(Stat.Stamina))
            return 0;
        member.DrainStamina(cost);
        return step;
    }
}

/// <summary>What using a container leads to next.</summary>
public enum ContainerStep
{
    /// <summary>It's been opened or destroyed: there's nothing to use.</summary>
    Gone,
    /// <summary>A herb, spice or gem source: <see cref="Container.Harvest"/>.</summary>
    Harvest,
    /// <summary>Its trap must be disarmed first: <see cref="Container.Disarm"/>.</summary>
    Trapped,
    /// <summary>Its lock must be picked first: <see cref="Container.PickLock"/>.</summary>
    Locked,
    /// <summary>It can be opened: <see cref="Container.Open"/>.</summary>
    Open,
}

/// <summary>
/// A container as it is in the world (<c>loot_func</c>): its trap and lock, which disarming and picking
/// clear until its chunk is loaded again, and its flags.
/// </summary>
public sealed class Container(ContainerObject record)
{
    public ContainerObject Record { get; } = record;

    public int TrapLevel { get; private set; } = record.TrapLevel;

    public int LockLevel { get; private set; } = record.LockLevel;

    /// <summary>Its gold, or a source's count, which a trap going off or the chest found empty clears.</summary>
    public int Gold { get; private set; } = record.Gold;

    /// <summary>Its loot table, which a trap going off clears.</summary>
    public RecordId? Loot { get; private set; } = record.Loot;

    /// <summary>
    /// Whether the player can use it from within its radius (<c>container_obj_check</c>): a herb, spice,
    /// gem or custom container always; a chest, sack, crate or barrel until it's opened, and only if it
    /// holds gold, a loot table or an item.
    /// </summary>
    public bool Usable(FlagNetwork flags) => Record.Kind >= ContainerKind.Herb
        || !(Record.Opened is { } opened && flags.IsSet(opened)) && (Gold != 0 || Loot is not null || Record.Item is not null);

    /// <summary>Found opened as its chunk loads: no lock, trap or gold (<c>exploding_container_check</c>).</summary>
    internal void Empty()
    {
        LockLevel = 0;
        TrapLevel = 0;
        Gold = 0;
    }

    /// <summary>
    /// What using it leads to: nothing once its opened or exploded flag is set; a source is harvested
    /// whatever its trap and lock; otherwise its trap comes before its lock.
    /// </summary>
    public ContainerStep Next(FlagNetwork flags)
    {
        if (Record.Opened is { } opened && flags.IsSet(opened) || Record.Exploded is { } exploded && flags.IsSet(exploded))
            return ContainerStep.Gone;
        if (Record.Kind is ContainerKind.Herb or ContainerKind.Spice or ContainerKind.Gem)
            return ContainerStep.Harvest;
        return TrapLevel != 0 ? ContainerStep.Trapped : LockLevel != 0 ? ContainerStep.Locked : ContainerStep.Open;
    }

    /// <summary>Picks its lock; picked, it's unlocked until its chunk is loaded again.</summary>
    public LockResult PickLock(Party party)
    {
        var result = Containers.PickLock(party, LockLevel);
        if (result == LockResult.Picked)
            LockLevel = 0;
        return result;
    }

    /// <summary>
    /// Tries to disarm its trap. Disarmed, the trap is gone until its chunk is loaded again. Failing sets
    /// it off: the container is destroyed with what it held, its exploded flag is set, and the living
    /// members are hurt (<see cref="Containers.TrapDamage"/>), which this returns.
    /// </summary>
    public IReadOnlyList<(Character Member, int Damage)>? Disarm(Party party, FlagNetwork flags, IRandom random)
    {
        var level = TrapLevel;
        TrapLevel = 0;
        if (Containers.Disarm(party, level, random))
            return null;
        // open_explosive_chest.
        Gold = 0;
        Loot = null;
        flags.TrySetAndProcess(Record.Exploded, true);
        return Containers.TrapDamage(party, level, random);
    }

    /// <summary>
    /// Opens it (<c>loot_func</c>): sets its opened and used flags, and rolls its loot table, then adds
    /// the item it always holds. With no items it gives its gold alone; otherwise the gold and items open
    /// the loot window.
    /// </summary>
    public (ItemPile Items, int Gold) Open(FlagNetwork flags, ItemCatalog items, Func<RecordId, LootRecord> loot, IRandom random)
    {
        // MapObjectUse.Loot (0x800105ec) writes the opened flag and reads 5626, a flag in the slot or not; then the used flag,
        // plainly (0x80017acc), which the network sees when something next reads a derived flag.
        flags.TrySetAndProcess(Record.Opened, true);
        if (Record.Used is { } used)
            flags.TrySet(used.Flag, !used.Clear);
        return Contents(items, loot, random);
    }

    /// <summary>What opening it gives, without setting its flags: its loot table rolled, or its gold, and the item it always holds.</summary>
    public (ItemPile Items, int Gold) Contents(ItemCatalog items, Func<RecordId, LootRecord> loot, IRandom random)
    {
        var pile = new ItemPile(items);
        var gold = Gold;
        if (Loot is { } table)
        {
            var roll = Containers.Roll(loot(table), items.Rules, random);
            gold = roll.Gold;
            foreach (var (item, count) in roll.Items)
                pile.Add(item, count);
        }
        if (Record.Item is { } always)
            pile.Add(always);
        return (pile, gold);
    }

    /// <summary>
    /// Harvests a herb, spice or gem source (<c>get_loot_reagent</c>): its count, which the record keeps
    /// as gold, times the Ranger's share (<see cref="Containers.HarvestShare"/>), at least 1. It sets its
    /// used flag and is gone; it gives no gold.
    /// </summary>
    public ItemPile Harvest(float share, FlagNetwork flags, ItemCatalog items)
    {
        // get_loot_reagent writes it through MapObjects.WriteUsed (0x80011760), which reads 5626 with a flag in the slot or not.
        flags.TrySetAndProcess(Record.Used?.Flag, Record.Used is not { Clear: true });
        return Reagent(share, items);
    }

    /// <summary>What harvesting a source gives, without setting its flag.</summary>
    public ItemPile Reagent(float share, ItemCatalog items)
    {
        var reagent = Record.Kind switch
        {
            ContainerKind.Herb => items.Rules.Herb,
            ContainerKind.Spice => items.Rules.Spice,
            _ => items.Rules.Gemstone,
        };
        var pile = new ItemPile(items);
        pile.Add(reagent, Containers.Harvest(Gold, share));
        return pile;
    }
}
