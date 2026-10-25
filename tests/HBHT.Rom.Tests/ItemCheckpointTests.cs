using HBHT.Core.Characters;
using HBHT.Core.Items;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

/// <summary>
/// Checks equipment and the inventory against the party in the user's checkpoints, played on 1.0 and
/// the debug ROM, with the records imported from 1.0.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class ItemCheckpointTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private static ItemInstance Instance(ItemCatalog items, CheckpointItem held) => new(items[TestRoms.ItemId(held.Id)])
    {
        Price = held.Price,
        Battery = held.StatMod is { Stat: 5, Amount: var left } ? left : null,
        Charges = held.Charges,
    };

    /// <summary>A member as the checkpoint holds them, wearing what they wear without applying it again.</summary>
    private Character Restore(CheckpointCharacter sheet, CharacterRules rules, ItemCatalog items)
    {
        var entity = Set.Get<EntityRecord>(RecordId.Hex(RecordTypes.Entity, sheet.Id));
        var character = Character.Restore(entity, rules, new CharacterStats(rules, sheet.Trained, sheet.Current, sheet.Base, sheet.Modified),
            new CharacterSkills(rules, sheet.SkillBase, sheet.SkillModified), []);
        character.Protection = sheet.Protection;
        character.Damage = sheet.Damage;
        foreach (var worn in new[] { sheet.Weapon, sheet.Armor, sheet.Shield }.OfType<CheckpointItem>())
            character.Equipment.Restore(Instance(items, worn));
        for (var slot = 0; slot < sheet.Gear.Count; slot++)
        {
            if (sheet.Gear[slot] is { } gear)
                character.Equipment.Restore(Instance(items, gear), slot);
        }
        return character;
    }

    [Theory]
    [MemberData(nameof(TestRoms.Checkpoints), MemberType = typeof(TestRoms))]
    public void EverythingWornIsAnItemTheMemberCanWear(string name)
    {
        var checkpoint = TestRoms.Checkpoint(name);
        var items = ItemCatalog.From(Set);

        foreach (var sheet in checkpoint.Party)
        {
            Assert.Equal(Equipment.GearSlots, sheet.Gear.Count);
            Assert.Equal(sheet.Gear.Count(g => g is not null), sheet.GearCount);
            Assert.Equal(ItemKind.Weapon, items[TestRoms.ItemId(sheet.Weapon!.Id)].Kind);
            if (sheet.Armor is { } armor)
                Assert.Equal(ItemKind.Armor, items[TestRoms.ItemId(armor.Id)].Kind);
            if (sheet.Shield is { } shield)
                Assert.Equal(ItemKind.Shield, items[TestRoms.ItemId(shield.Id)].Kind);
            Assert.All(sheet.Gear.OfType<CheckpointItem>(), g => Assert.True(items[TestRoms.ItemId(g.Id)].IsGear));
        }
    }

    /// <summary>
    /// Taking off everything a member wears, by the port's rules, leaves the sheet as the console
    /// holds it unmodified: each stat at its current value and each skill at its base level, and
    /// protection and damage at the entity's. So equipping changes the sheet as the console's does.
    /// </summary>
    [Theory]
    [MemberData(nameof(TestRoms.Checkpoints), MemberType = typeof(TestRoms))]
    public void TakingOffWhatAMemberWearsUndoesWhatItDid(string name)
    {
        var checkpoint = TestRoms.Checkpoint(name);
        var rules = CharacterRules.From(Set);
        var items = ItemCatalog.From(Set);

        foreach (var sheet in checkpoint.Party)
        {
            var entity = Set.Get<EntityRecord>(RecordId.Hex(RecordTypes.Entity, sheet.Id));
            var character = Restore(sheet, rules, items);
            character.Equipment.RemoveWeapon();
            character.Equipment.RemoveArmor();
            character.Equipment.RemoveShield();
            for (var slot = 0; slot < Equipment.GearSlots; slot++)
                character.Equipment.RemoveGear(slot);

            foreach (var stat in Enum.GetValues<Stat>())
            {
                Assert.Equal(character.Stats.Current(stat), character.Stats.RawModified(stat));
                Assert.Equal(character.Stats.Trained(stat), character.Stats.RawBase(stat));
            }
            foreach (var skill in SkillId.All.Where(s => character.Skills.RawBase(s) >= 0))
                Assert.Equal(character.Skills.RawBase(skill), character.Skills.RawModified(skill));
            Assert.Equal(entity.Protection, character.Protection);
            Assert.Equal(entity.Damage, character.Damage);
        }
    }

    [Theory]
    [MemberData(nameof(TestRoms.Checkpoints), MemberType = typeof(TestRoms))]
    public void ABatteryHoldsNoMoreThanItsItemsStaminaBonus(string name)
    {
        var checkpoint = TestRoms.Checkpoint(name);
        var items = ItemCatalog.From(Set);

        var held = checkpoint.Party.SelectMany(s => new[] { s.Weapon, s.Armor, s.Shield }.Concat(s.Gear))
            .Concat(checkpoint.Inventory.Select(s => s.Item)).OfType<CheckpointItem>();
        foreach (var item in held)
        {
            var record = items[TestRoms.ItemId(item.Id)];
            // Only a battery's StatMod changes; any other is the item's bonus as it is.
            if (record.BatteryCapacity is { } capacity)
                Assert.InRange(item.StatMod!.Value.Amount, 0, capacity);
            else if (record.Bonuses.Stat is { } bonus)
                Assert.Equal((int.Parse(bonus.Stat.Key), bonus.Amount), item.StatMod);
            else
                Assert.Null(item.StatMod);
            Assert.Equal(record.Bonuses.Spell is not null, item.Charges is not null);
        }
    }

    [Theory]
    [MemberData(nameof(TestRoms.Checkpoints), MemberType = typeof(TestRoms))]
    public void TheInventoryHoldsStacksAtTheirItemsPrices(string name)
    {
        var checkpoint = TestRoms.Checkpoint(name);
        var items = ItemCatalog.From(Set);

        Assert.NotEmpty(checkpoint.Inventory);
        foreach (var stack in checkpoint.Inventory)
        {
            var item = items[TestRoms.ItemId(stack.Item.Id)];
            Assert.InRange(stack.Quantity, 1, items.Rules.StackLimit);
            Assert.Equal(item.Price, stack.Item.Price);
            // The general slots come first; items with slots of their own are after them.
            Assert.Equal(item.Traits.OwnSlot, stack.Slot >= items.Rules.GeneralSlots);
        }
        Assert.Equal(checkpoint.Inventory.Count, checkpoint.Inventory.Select(s => s.Item.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(TestRoms.Checkpoints), MemberType = typeof(TestRoms))]
    public void AKeyItemIsHeldWhileItsFlagIsSet(string name)
    {
        var checkpoint = TestRoms.Checkpoint(name);
        var items = ItemCatalog.From(Set);

        var held = checkpoint.Inventory.Select(s => TestRoms.ItemId(s.Item.Id)).ToHashSet();
        foreach (var item in items.All.Where(i => i.KeyItem is not null))
            Assert.Equal(checkpoint.Flags[int.Parse(item.KeyItem!.Held.Key)], held.Contains(item.Id));
    }
}
