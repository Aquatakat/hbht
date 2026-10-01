using Aidyn.Core.Characters;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>Shops, containers and recipes run against the records imported from 1.0.</summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class TradeImportTests(MapObjectFixture fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private Party NewParty(ItemCatalog items, FlagNetwork flags)
    {
        var rules = CharacterRules.From(Set);
        var party = new Party(items, flags);
        party.Add(Character.Create(Set.Get<EntityRecord>(RecordId.Hex(RecordTypes.Entity, 0x023F)), rules, Set.Get<SpellRecord>, items: items));
        return party;
    }

    [Fact]
    public void EveryShopOpensWithItsStockAtItsPrices()
    {
        var items = ItemCatalog.From(Set);
        var flags = FlagNetwork.Load(Set);

        foreach (var record in Set.All<ShopRecord>(RecordTypes.Shop))
        {
            var shopkeeper = Set.Get<EntityRecord>(record.Shopkeeper);
            var shop = Shop.Open(record, shopkeeper, NewParty(items, flags), items, flags);
            Assert.Equal(record.Stock.Select(s => s.Item).Distinct().Count(), shop.Stock.Stacks.Count);
            foreach (var slot in record.Stock.Where(s => record.Stock.Count(o => o.Item == s.Item) == 1))
                Assert.Equal((ushort)(uint)(items[slot.Item].Price * slot.PriceMultiplier), shop.Stock.Find(slot.Item)!.Instance.Price);
            // Alaron starts with no Merchant, so the shopkeeper's level sets the factor alone.
            Assert.Equal(shopkeeper.Skills.Merchant!.Value * items.Rules.MerchantStep, shop.Barter);
        }
    }

    [Fact]
    public void EveryLootTableRollsRealItemsAndGoldInItsBounds()
    {
        var items = ItemCatalog.From(Set);
        var random = new Lcg();

        foreach (var loot in Set.All<LootRecord>(RecordTypes.Loot))
        {
            for (var i = 0; i < 20; i++)
            {
                var roll = Containers.Roll(loot, items.Rules, random);
                Assert.InRange(roll.Gold, loot.GoldMin * 2, loot.GoldMax * 2);
                Assert.All(roll.Items, x => Assert.NotNull(items.TryGet(x.Item)));
            }
        }
    }

    [Fact]
    public void EveryContainerOpensOrHarvests()
    {
        var items = ItemCatalog.From(Set);
        var flags = FlagNetwork.Load(Set);
        var random = new Lcg();

        var containers = fixture.Data.Objects.Values.OfType<ContainerObject>().ToList();
        Assert.NotEmpty(containers);
        foreach (var record in containers)
        {
            var container = new Container(record);
            if (container.Next(flags) == ContainerStep.Harvest)
                Assert.InRange(container.Harvest(1f, flags, items).Stacks.Single().Quantity, 1, 255);
            else
                container.Open(flags, items, Set.Get<LootRecord>, random);
        }
    }

    [Fact]
    public void AlaronIsOfferedTheRecipesHisSkillsReach()
    {
        var items = ItemCatalog.From(Set);
        var party = NewParty(items, FlagNetwork.Load(Set));
        var alaron = party.Members.Single();
        var recipes = Set.All<RecipeRecord>(RecordTypes.Recipe).OrderBy(r => int.Parse(r.Id.Key)).ToList();

        var offered = Crafting.Offered(party, alaron, recipes).ToList();
        Assert.Equal(recipes.Where(r => r.Skill == Crafting.Alchemist && r.Level <= alaron.Skills.Modified(Skill.Alchemist)), offered);
        // Hides make the Mechanic's recipes offered, to a member whose base Mechanic reaches them.
        party.Inventory!.Add(RecordId.Hex(RecordTypes.Item, 0x0123));
        Assert.Equal(alaron.Skills.Base(Skill.Mechanic) >= 1, Crafting.Offered(party, alaron, recipes).Any(r => r.Skill == Crafting.Mechanic));
    }

    [Fact]
    public void AlaronStartsWieldingHisWeaponAndWearingHisArmor()
    {
        var items = ItemCatalog.From(Set);
        var entity = Set.Get<EntityRecord>(RecordId.Hex(RecordTypes.Entity, 0x023F));
        var alaron = NewParty(items, FlagNetwork.Load(Set)).Members.Single();

        Assert.Equal(entity.Armor, alaron.Equipment.Armor?.Id);
        Assert.Equal(entity.Weapons.OfType<RecordId>().First(), alaron.Equipment.Weapon?.Id);
    }
}
