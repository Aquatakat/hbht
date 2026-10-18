using System.Numerics;
using Aidyn.Core.Characters;
using CharacterParty = Aidyn.Core.Characters.Party;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>Raw values that make the original's dice come out as chosen.</summary>
internal static class RandomFor
{
    /// <summary>The smallest raw value whose <c>Below(n)</c> is <paramref name="result"/>.</summary>
    public static uint Below(uint n, uint result) => (uint)((((ulong)result << 32) + n - 1) / n);

    /// <summary>A raw value for which <c>RollD(1, sides)</c> is <paramref name="value"/>.</summary>
    public static uint D(int sides, int value) => Below((uint)sides, (uint)(value - 1));

    /// <summary>A raw value for which <c>Range(a, b)</c> is <paramref name="value"/>, for unequal bounds.</summary>
    public static uint Range(int a, int b, int value) => Below((uint)(b - a + 1), (uint)(value - a));
}

/// <summary>Hand-made items, members and parties for the shop, container and crafting tests.</summary>
internal static class TradeData
{
    public static readonly ItemRecord Spice = ItemData.NewItem(900);
    public static readonly ItemRecord Herb = ItemData.NewItem(901);
    public static readonly ItemRecord Gem = ItemData.NewItem(902);
    public static readonly ItemRecord Trinket = ItemData.NewItem(1) with { Price = 100 };
    public static readonly ItemRecord Cheap = ItemData.NewItem(2) with { Price = 10 };
    public static readonly ItemRecord Potion = ItemData.NewItem(3, ItemKind.Potion) with { Price = 40 };
    public static readonly ItemRecord Hide = ItemData.NewItem(4);
    public static readonly ItemRecord Quest = ItemData.NewItem(5, traits: new ItemTraits(MustTake: true));
    public static readonly ArmorRecord Leather = ItemData.NewArmor(1, protection: 3);

    /// <summary>Flags 0 and 1 are the constants false and true, which can't be set.</summary>
    public static readonly RecordId PotionFlag = ItemData.Flag(5);

    /// <summary>The item rules, with a Merchant step of a quarter so prices come out exact.</summary>
    public static InventoryRecord Rules(int generalSlots = 8) => ItemData.Rules(generalSlots) with { MerchantStep = 0.25f, FirstPotionFlag = PotionFlag };

    public static ItemCatalog Catalog(int generalSlots = 8, params ItemRecord[] more) =>
        ItemData.CatalogWith(Rules(generalSlots), [Spice, Herb, Gem, Trinket, Cheap, Potion, Hide, Quest, Leather, .. more]);

    public static ItemRecord Priced(int n, int price, ItemKind kind = ItemKind.Misc) => ItemData.NewItem(n, kind) with { Price = price };

    /// <summary>A member with the given skills (all others unlearnable, so 0) and attributes, with a distinct entity.</summary>
    public static Character Member(int n = 0, SkillLevels? skills = null, int intelligence = 18, int stamina = 30, int endurance = 15, int dexterity = 15) =>
        ItemData.Alaron(e => e with
        {
            Id = RecordId.Hex(RecordTypes.Entity, 0x0300 + n),
            Level = 1,
            Attributes = new Attributes(intelligence, 12, dexterity, endurance, 15, stamina),
            Skills = skills ?? new SkillLevels(),
        });

    public static Character Dead(Character member)
    {
        member.TakeDamage(1000);
        Assert.True(member.IsDead);
        return member;
    }

    public static CharacterParty PartyOf(ItemCatalog catalog, FlagNetwork? flags, params Character[] members)
    {
        var party = new CharacterParty(catalog, flags);
        foreach (var member in members)
            Assert.True(party.Add(member));
        return party;
    }

    public static EntityRecord Keeper(int? merchant) => Characters.Alaron() with { Skills = new SkillLevels(Merchant: merchant) };

    public static ShopRecord ShopOf(EntityRecord keeper, params ShopStock[] stock) => new(RecordId.Decimal(RecordTypes.Shop, 0), keeper.Id, stock);

    public static LootRecord Table(int goldMin, int goldMax, int reagentChance, int reagentMin, int reagentMax, params LootDrop[] drops) => new(
        RecordId.Hex(RecordTypes.Loot, 0x0800), "Loot", goldMin, goldMax, 0, 0, [0, 0, 0], reagentChance, reagentMin, reagentMax,
        [.. drops, .. Enumerable.Repeat(new LootDrop(null, 0), 6 - drops.Length)]);

    public static RecipeRecord Recipe(int n, RecordId skill, RecordId product, int level, int step, params Ingredient[] ingredients) =>
        new(RecordId.Decimal(RecordTypes.Recipe, n), skill, product, ingredients, level, step);

    public static void AssertNear(double expected, double actual) => Assert.InRange(actual, expected - 0.0001, expected + 0.0001);
}

public class DiceHelperTests
{
    [Fact]
    public void TheDiceHelpersGiveExactlyTheValuesTheyWereAskedFor()
    {
        for (var value = 1; value <= 100; value++)
            Assert.Equal(value, new ScriptedRandom(RandomFor.D(100, value)).RollD(1, 100));
        for (var value = 1; value <= 4; value++)
            Assert.Equal(value, new ScriptedRandom(RandomFor.D(4, value)).RollD(1, 4));
        for (var value = 10; value <= 20; value++)
            Assert.Equal(value, new ScriptedRandom(RandomFor.Range(10, 20, value)).Range(10, 20));
        for (var value = 0; value <= 2; value++)
            Assert.Equal(value, new ScriptedRandom(RandomFor.Range(0, 2, value)).Range(0, 2));
    }

    [Fact]
    public void AScriptedStreamCountsWhatIsLeftAndRefusesToDrawMore()
    {
        var random = new ScriptedRandom(1, 2);
        random.Next();

        Assert.Equal(1, random.Remaining);
        random.Next();
        Assert.Throws<InvalidOperationException>(() => random.Next());
    }
}

public class ShopTests
{
    private static readonly ItemRecord Gold1 = ItemData.NewItem(10) with { Price = 100 };
    private static readonly ItemRecord Gold2 = ItemData.NewItem(11) with { Price = 100 };
    private static readonly ItemRecord Gold3 = ItemData.NewItem(12) with { Price = 100 };
    private static readonly ItemRecord Gold4 = ItemData.NewItem(13) with { Price = 100 };
    private static readonly ItemRecord Gold5 = ItemData.NewItem(14) with { Price = 100 };

    private static SkillLevels Merchant(int level) => new(Merchant: level);

    private static ShopStock Sells(ItemRecord item, float multiplier = 1) => new(item.Id, multiplier);

    private static Shop OpenShop(int? keeperMerchant, CharacterParty party, ItemCatalog catalog, FlagNetwork? flags, params ShopStock[] stock)
    {
        var keeper = TradeData.Keeper(keeperMerchant);
        return Shop.Open(TradeData.ShopOf(keeper, stock), keeper, party, catalog, flags);
    }

    // The stock's own multipliers apply to base prices of 100; a Merchant of 3 against none gives a factor of 0.75.
    private static (Shop Shop, CharacterParty Party, ItemCatalog Catalog, FlagNetwork Flags) Standard(int generalSlots = 8, uint gold = 1000, int? keeper = 3, params ShopStock[] stock)
    {
        var catalog = TradeData.Catalog(generalSlots);
        var flags = ItemData.Flags();
        var party = TradeData.PartyOf(catalog, flags, TradeData.Member());
        party.Gold = gold;
        var shop = OpenShop(keeper, party, catalog, flags, stock.Length == 0 ? [Sells(TradeData.Trinket), Sells(TradeData.Cheap), Sells(TradeData.Potion)] : stock);
        return (shop, party, catalog, flags);
    }

    [Fact]
    public void EachListedItemIsOneStackAtItsPriceTimesTheMultiplier()
    {
        var (shop, _, _, _) = Standard(stock: [Sells(TradeData.Trinket), Sells(TradeData.Potion, 1.5f), Sells(TradeData.Cheap, 0.5f)]);

        Assert.Equal([TradeData.Trinket.Id, TradeData.Potion.Id, TradeData.Cheap.Id], shop.Stock.Stacks.Select(s => s.Item.Id));
        Assert.Equal([100, 60, 5], shop.Stock.Stacks.Select(s => s.Instance.Price));
        Assert.All(shop.Stock.Stacks, s => Assert.Equal(1, s.Quantity));
    }

    [Fact]
    public void AStockPriceIsTruncatedTo16Bits()
    {
        var pricey = TradeData.Priced(20, 40000);
        var catalog = TradeData.Catalog(more: pricey);
        var party = TradeData.PartyOf(catalog, null, TradeData.Member());

        var shop = OpenShop(3, party, catalog, null, Sells(pricey, 2));

        Assert.Equal(80000 % 65536, shop.Stock.Find(pricey.Id)!.Instance.Price);
    }

    [Fact]
    public void AnItemListedTwiceIsOneStackWhoseSecondListingMultipliesItsPriceAgain()
    {
        var (shop, _, _, _) = Standard(stock: [Sells(TradeData.Trinket, 1.5f), Sells(TradeData.Trinket, 2)]);

        var stack = Assert.Single(shop.Stock.Stacks);
        Assert.Equal(2, stack.Quantity);
        Assert.Equal(300, stack.Instance.Price);
    }

    [Fact]
    public void AnItemListedTwiceKeepsItsPriceWhenTheSecondMultiplierIsOne()
    {
        var (shop, _, _, _) = Standard(stock: [Sells(TradeData.Trinket, 1.5f), Sells(TradeData.Trinket, 1)]);

        var stack = Assert.Single(shop.Stock.Stacks);
        Assert.Equal(2, stack.Quantity);
        Assert.Equal(150, stack.Instance.Price);
    }

    [Fact]
    public void TheBarterFactorIsTheShopkeepersMerchantLessThePartysBestTimesTheStepAndCostsTheBestStamina()
    {
        var catalog = TradeData.Catalog();
        var haggler = TradeData.Member(0, Merchant(4));
        var other = TradeData.Member(1);
        var party = TradeData.PartyOf(catalog, null, haggler, other);

        var shop = OpenShop(6, party, catalog, null);

        Assert.Equal((6 - 4) * 0.25f, shop.Barter);
        Assert.Equal(30 - (15 - 4), haggler.Stats.Modified(Stat.Stamina));
        Assert.Equal(30, other.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void TheBestMerchantIsTheHighestLevelEvenWhenALaterMemberHasLess()
    {
        var catalog = TradeData.Catalog();
        var first = TradeData.Member(0, Merchant(5));
        var second = TradeData.Member(1, Merchant(3));
        var party = TradeData.PartyOf(catalog, null, first, second);

        var shop = OpenShop(6, party, catalog, null);

        Assert.Equal((6 - 5) * 0.25f, shop.Barter);
        Assert.Equal(30 - 10, first.Stats.Modified(Stat.Stamina));
        Assert.Equal(30, second.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void OnATieInMerchantTheLaterMemberBarters()
    {
        var catalog = TradeData.Catalog();
        var first = TradeData.Member(0, Merchant(4));
        var second = TradeData.Member(1, Merchant(4));
        var party = TradeData.PartyOf(catalog, null, first, second);

        var shop = OpenShop(6, party, catalog, null);

        Assert.Equal(0.5f, shop.Barter);
        Assert.Equal(30, first.Stats.Modified(Stat.Stamina));
        Assert.Equal(30 - 11, second.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ABartererWhoCantPayTheStaminaLeavesTheFactorAtTheShopkeepersMerchantAndKeepsTheirStamina()
    {
        var catalog = TradeData.Catalog();
        // Merchant 1 costs 14 Stamina, and this member has 10.
        var tired = TradeData.Member(0, Merchant(1), stamina: 10);
        var party = TradeData.PartyOf(catalog, null, tired);

        var shop = OpenShop(6, party, catalog, null);

        Assert.Equal(6 * 0.25f, shop.Barter);
        Assert.Equal(10, tired.Stats.Modified(Stat.Stamina));
        Assert.Equal(15, tired.Stats.Modified(Stat.Endurance));
    }

    [Fact]
    public void ABartererWithJustTheStaminaItCostsPaysItAll()
    {
        var catalog = TradeData.Catalog();
        var spent = TradeData.Member(0, Merchant(1), stamina: 14);
        var party = TradeData.PartyOf(catalog, null, spent);

        var shop = OpenShop(6, party, catalog, null);

        Assert.Equal(5 * 0.25f, shop.Barter);
        Assert.Equal(0, spent.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void WithNoMerchantInThePartyTheFactorIsTheShopkeepersMerchantTimesTheStepAndNoStaminaIsTaken()
    {
        var catalog = TradeData.Catalog();
        var plain = TradeData.Member(0, Merchant(0));
        var party = TradeData.PartyOf(catalog, null, plain);

        Assert.Equal(3 * 0.25f, OpenShop(3, party, catalog, null).Barter);
        Assert.Equal(30, plain.Stats.Modified(Stat.Stamina));
        Assert.Equal(3 * 0.25f, OpenShop(3, TradeData.PartyOf(catalog, null), catalog, null).Barter);
    }

    [Fact]
    public void ADeadMemberDoesntBarterButALivingOneStillDoes()
    {
        var catalog = TradeData.Catalog();
        var dead = TradeData.Dead(TradeData.Member(0, Merchant(10)));
        var alive = TradeData.Member(1, Merchant(2));
        var party = TradeData.PartyOf(catalog, null, dead, alive);

        var shop = OpenShop(5, party, catalog, null);

        Assert.Equal((5 - 2) * 0.25f, shop.Barter);
        Assert.Equal(30 - 13, alive.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void AShopkeeperWhoseMerchantIsUnknownCountsAs255()
    {
        var catalog = TradeData.Catalog();
        var plain = TradeData.Member(0, Merchant(0));
        var skilled = TradeData.Member(1, Merchant(3));

        Assert.Equal(255 * 0.25f, OpenShop(null, TradeData.PartyOf(catalog, null, plain), catalog, null).Barter);
        Assert.Equal((255 - 3) * 0.25f, OpenShop(null, TradeData.PartyOf(catalog, null, skilled), catalog, null).Barter);
    }

    [Fact]
    public void ShopForFindsTheFirstShopTheShopkeeperRuns()
    {
        var a = TradeData.Keeper(1);
        var b = a with { Id = RecordId.Hex(RecordTypes.Entity, 0x0777) };
        var shops = new[]
        {
            new ShopRecord(RecordId.Decimal(RecordTypes.Shop, 0), a.Id, []),
            new ShopRecord(RecordId.Decimal(RecordTypes.Shop, 1), b.Id, []),
            new ShopRecord(RecordId.Decimal(RecordTypes.Shop, 2), b.Id, []),
        };

        Assert.Equal(shops[1], Shop.For(shops, b.Id));
        Assert.Null(Shop.For(shops, RecordId.Hex(RecordTypes.Entity, 0x0999)));
    }

    [Fact]
    public void BuyingCostsThePriceTimesOnePlusTheFactorTruncated()
    {
        // Factor 0.75: 100 costs 175, and 10 costs 17.5, which reads 17.
        var (shop, _, _, _) = Standard();

        Assert.Equal(175, shop.BuyPrice(shop.Stock.Find(TradeData.Trinket.Id)!.Instance));
        Assert.Equal(17, shop.BuyPrice(shop.Stock.Find(TradeData.Cheap.Id)!.Instance));
    }

    [Fact]
    public void ABetterMerchantInThePartyMakesBuyingCheaperAndSellingDearer()
    {
        var catalog = TradeData.Catalog();
        var party = TradeData.PartyOf(catalog, null, TradeData.Member(0, Merchant(4)));
        var shop = OpenShop(3, party, catalog, null, Sells(TradeData.Trinket));
        var trinket = shop.Stock.Find(TradeData.Trinket.Id)!.Instance;

        Assert.Equal(-0.25f, shop.Barter);
        Assert.Equal(75, shop.BuyPrice(trinket));
        // 100 × 1.25 × 0.5 is 62.5.
        Assert.Equal(62, shop.SellPrice(trinket));
    }

    [Fact]
    public void SellingPaysThePriceTimesOneLessTheFactorTimesTheSellShareTruncated()
    {
        var (shop, _, catalog, _) = Standard();

        // Factor 0.75: 100 × 0.25 × 0.5 is 12.5, and 10 is 1.25.
        Assert.Equal(12, shop.SellPrice(new ItemInstance(catalog[TradeData.Trinket.Id])));
        Assert.Equal(1, shop.SellPrice(new ItemInstance(catalog[TradeData.Cheap.Id])));
    }

    [Fact]
    public void BuyingTakesTheGoldAddsOneAndTheStockNeverRunsOut()
    {
        var (shop, party, _, _) = Standard();

        Assert.Equal(TradeResult.Done, shop.Buy(TradeData.Trinket.Id));
        Assert.Equal(TradeResult.Done, shop.Buy(TradeData.Trinket.Id));

        Assert.Equal(2, party.Inventory.Count(TradeData.Trinket.Id));
        Assert.Equal(1000u - 2 * 175, party.Gold);
        Assert.Equal(1, shop.Stock.Find(TradeData.Trinket.Id)?.Quantity);
    }

    [Fact]
    public void BuyingSomethingNotStockedIsNoneLeft()
    {
        var (shop, party, _, _) = Standard();

        Assert.Equal(TradeResult.NoneLeft, shop.Buy(TradeData.Hide.Id));
        Assert.Equal(1000u, party.Gold);
    }

    [Fact]
    public void BuyingWithTooLittleGoldIsCantAffordAndChangesNothing()
    {
        var (shop, party, _, _) = Standard(gold: 174);

        Assert.Equal(TradeResult.CantAfford, shop.Buy(TradeData.Trinket.Id));

        Assert.Equal(174u, party.Gold);
        Assert.False(party.Inventory.Has(TradeData.Trinket.Id));
    }

    [Fact]
    public void BuyingWithExactlyEnoughGoldWorks()
    {
        var (shop, party, _, _) = Standard(gold: 175);

        Assert.Equal(TradeResult.Done, shop.Buy(TradeData.Trinket.Id));

        Assert.Equal(0u, party.Gold);
    }

    [Fact]
    public void BuyingWhenTheStackIsFullIsStackFullButAnUnaffordablePriceIsReportedFirst()
    {
        var (shop, party, _, _) = Standard();
        party.Inventory.Add(TradeData.Trinket.Id, 99);

        Assert.Equal(TradeResult.StackFull, shop.Buy(TradeData.Trinket.Id));
        Assert.Equal(1000u, party.Gold);

        party.Gold = 0;
        Assert.Equal(TradeResult.CantAfford, shop.Buy(TradeData.Trinket.Id));
        Assert.Equal(99, party.Inventory.Count(TradeData.Trinket.Id));
    }

    [Fact]
    public void BuyingANewItemWithNoGeneralSlotFreeIsInventoryFullButOneAlreadyCarriedStillFits()
    {
        var (shop, party, _, _) = Standard(generalSlots: 1);
        party.Inventory.Add(TradeData.Cheap.Id);

        Assert.Equal(TradeResult.InventoryFull, shop.Buy(TradeData.Trinket.Id));
        Assert.Equal(1000u, party.Gold);

        Assert.Equal(TradeResult.Done, shop.Buy(TradeData.Cheap.Id));
        Assert.Equal(2, party.Inventory.Count(TradeData.Cheap.Id));
    }

    [Fact]
    public void BuyingAPotionSetsTheFirstPotionFlagAndBuyingAnythingElseDoesnt()
    {
        var (shop, _, _, flags) = Standard();
        var flag = TradeData.PotionFlag;

        shop.Buy(TradeData.Trinket.Id);
        Assert.False(flags.Get(flag));

        Assert.Equal(TradeResult.Done, shop.Buy(TradeData.Potion.Id));
        Assert.True(flags.Get(flag));
    }

    [Fact]
    public void AShopOpenedWithoutFlagsSellsPotionsAllTheSame()
    {
        var catalog = TradeData.Catalog();
        var party = TradeData.PartyOf(catalog, null, TradeData.Member());
        party.Gold = 1000;
        var shop = OpenShop(3, party, catalog, null, Sells(TradeData.Potion));

        Assert.Equal(TradeResult.Done, shop.Buy(TradeData.Potion.Id));
    }

    [Fact]
    public void SellingTakesTheQuantityAndPaysThePriceTimesItWithoutAddingToTheStock()
    {
        // The shop pays 12 for a thing priced 100, and doesn't stock the trinket here.
        var (shop, party, _, _) = Standard(stock: [Sells(TradeData.Cheap)]);
        party.Inventory.Add(TradeData.Trinket.Id, 3);
        party.Gold = 0;

        Assert.Equal(TradeResult.Done, shop.Sell(TradeData.Trinket.Id, 2));

        Assert.Equal(1, party.Inventory.Count(TradeData.Trinket.Id));
        Assert.Equal(24u, party.Gold);
        Assert.Null(shop.Stock.Find(TradeData.Trinket.Id));
    }

    [Fact]
    public void SellingTheWholeStackEmptiesIt()
    {
        var (shop, party, _, _) = Standard(stock: [Sells(TradeData.Cheap)]);
        party.Inventory.Add(TradeData.Trinket.Id, 3);

        Assert.Equal(TradeResult.Done, shop.Sell(TradeData.Trinket.Id, 3));

        Assert.False(party.Inventory.Has(TradeData.Trinket.Id));
        Assert.Equal(1000u + 36, party.Gold);
    }

    [Fact]
    public void SellingSomethingNotCarriedIsNoneLeft()
    {
        var (shop, party, _, _) = Standard();

        Assert.Equal(TradeResult.NoneLeft, shop.Sell(TradeData.Hide.Id, 1));
        Assert.Equal(1000u, party.Gold);
    }

    [Fact]
    public void ThingsThatWouldSellForOneGoldOrLessAreRefusedAndKept()
    {
        var (shop, party, _, _) = Standard();
        // 10 × 0.25 × 0.5 is 1.25, which reads 1.
        party.Inventory.Add(TradeData.Cheap.Id, 2);

        Assert.Equal(TradeResult.Refused, shop.Sell(TradeData.Cheap.Id, 1));

        Assert.Equal(2, party.Inventory.Count(TradeData.Cheap.Id));
        Assert.Equal(1000u, party.Gold);
    }

    [Fact]
    public void QuantitiesAreOneFiveTenAndTwentyBelowWhatIsHeldThenAll()
    {
        Assert.Equal([1], Shop.Quantities(1));
        Assert.Equal([1, 5, 7], Shop.Quantities(7));
        Assert.Equal([1, 5, 10, 20, 30], Shop.Quantities(30));
        Assert.Equal([1, 5, 10, 20], Shop.Quantities(20));
        Assert.Equal([1, 5], Shop.Quantities(5));
        Assert.Equal([1, 5, 10, 20, 99], Shop.Quantities(99));
    }

    // Keeper Merchant 2 against a party's best of 2 makes the factor 0, so buying costs the stock's price.
    private static Shop Appraiser(CharacterParty party, ItemCatalog catalog, int keeper, params ShopStock[] stock) =>
        OpenShop(keeper, party, catalog, null, stock);

    [Fact]
    public void AppraisingGradesTheBaseWorthAgainstThePriceByTheBestMembersJudgement()
    {
        var catalog = TradeData.Catalog(more: [Gold1, Gold2, Gold3, Gold4, Gold5]);
        var party = TradeData.PartyOf(catalog, null, TradeData.Member(0, Merchant(2)));
        var shop = Appraiser(party, catalog, 2, Sells(Gold1), Sells(Gold2, 2), Sells(Gold3, 0.2f), Sells(Gold4, 10), Sells(Gold5, 5));
        Index(shop);

        // Intelligence 18 + Merchant 2 × 6 is 30, which the curve reads as 1.04186. The base worth is 100.
        Assert.Equal(0, shop.Barter);
        Assert.Equal(3, shop.Appraise(Instance(shop, Gold1)));
        Assert.Equal(2, shop.Appraise(Instance(shop, Gold2)));
        Assert.Equal(1, shop.Appraise(Instance(shop, Gold5)));
        Assert.Equal(0, shop.Appraise(Instance(shop, Gold4)));
        Assert.Equal(6, shop.Appraise(Instance(shop, Gold3)));
    }

    // Keeps the stock's prices in view for the reader: ×1, ×2, ×0.2, ×10 and ×5 of 100.
    private static void Index(Shop shop) => Assert.Equal([100, 200, 20, 1000, 500], shop.Stock.Stacks.Select(s => s.Instance.Price));

    private static ItemInstance Instance(Shop shop, ItemRecord item) => shop.Stock.Find(item.Id)!.Instance;

    [Fact]
    public void AppraisingWithAnEmptyPartyUsesAJudgementOfZeroWhichIsHalfTheWorth()
    {
        var catalog = TradeData.Catalog(more: Gold1);
        var shop = Appraiser(TradeData.PartyOf(catalog, null), catalog, 0, Sells(Gold1));

        // 100 × 0.5 × 3 / 100 is 1.5, which rounds to 2.
        Assert.Equal(2, shop.Appraise(Instance(shop, Gold1)));
    }

    [Fact]
    public void AppraisingCountsTheBestMemberWhetherOrNotTheyAreDead()
    {
        var catalog = TradeData.Catalog(more: Gold1);
        // The dead member's 18 + 5 × 6 is 48, which judges better than the living one's 18.
        var dead = TradeData.Dead(TradeData.Member(0, Merchant(5)));
        var alive = TradeData.Member(1);
        var shop = Appraiser(TradeData.PartyOf(catalog, null, dead, alive), catalog, 0, Sells(Gold1));
        var withoutDead = Appraiser(TradeData.PartyOf(catalog, null, TradeData.Member(2)), catalog, 0, Sells(Gold1));

        Assert.Equal(0, shop.Barter);
        Assert.Equal(4, shop.Appraise(Instance(shop, Gold1)));
        Assert.Equal(3, withoutDead.Appraise(Instance(withoutDead, Gold1)));
    }
}

public class LootWindowTests
{
    private static (LootWindow Window, ItemPile Loot, CharacterParty Party, FlagNetwork Flags) Open(int generalSlots = 8, int gold = 0, Action<ItemPile>? fill = null)
    {
        var catalog = TradeData.Catalog(generalSlots);
        var flags = ItemData.Flags();
        var party = TradeData.PartyOf(catalog, flags, TradeData.Member());
        var loot = new ItemPile(catalog);
        fill?.Invoke(loot);
        return (LootWindow.Open(loot, gold, party, catalog, flags), loot, party, flags);
    }

    [Fact]
    public void OpeningAddsTheGoldToThePartyAtOnce()
    {
        var catalog = TradeData.Catalog();
        var party = TradeData.PartyOf(catalog, null, TradeData.Member());
        party.Gold = 10;

        var window = LootWindow.Open(new ItemPile(catalog), 50, party, catalog);

        Assert.Equal(60u, party.Gold);
        Assert.Equal(50, window.Gold);
    }

    [Fact]
    public void TakingMovesAWholeStackToThePartyAndOutOfTheLoot()
    {
        var (window, loot, party, _) = Open(fill: p => p.Add(TradeData.Trinket.Id, 5));

        Assert.Equal(TradeResult.Done, window.Take(TradeData.Trinket.Id));

        Assert.Equal(5, party.Inventory.Count(TradeData.Trinket.Id));
        Assert.Null(loot.Find(TradeData.Trinket.Id));
    }

    [Fact]
    public void TakingFillsThePartysStackToTheLimitAndLeavesTheRestInTheLoot()
    {
        var (window, loot, party, _) = Open(fill: p => p.Add(TradeData.Trinket.Id, 5));
        party.Inventory.Add(TradeData.Trinket.Id, 97);

        Assert.Equal(TradeResult.Done, window.Take(TradeData.Trinket.Id));

        Assert.Equal(99, party.Inventory.Count(TradeData.Trinket.Id));
        Assert.Equal(3, loot.Find(TradeData.Trinket.Id)?.Quantity);
    }

    [Fact]
    public void TakingWhenThePartysStackIsFullIsStackFullAndTakesNothing()
    {
        var (window, loot, party, _) = Open(fill: p => p.Add(TradeData.Trinket.Id, 5));
        party.Inventory.Add(TradeData.Trinket.Id, 99);

        Assert.Equal(TradeResult.StackFull, window.Take(TradeData.Trinket.Id));

        Assert.Equal(5, loot.Find(TradeData.Trinket.Id)?.Quantity);
    }

    [Fact]
    public void TakingSomethingNotInTheLootIsNoneLeft()
    {
        var (window, _, _, _) = Open();

        Assert.Equal(TradeResult.NoneLeft, window.Take(TradeData.Trinket.Id));
    }

    [Fact]
    public void TakingANewItemWithNoGeneralSlotFreeIsInventoryFullAndLeavesTheLoot()
    {
        var (window, loot, party, _) = Open(generalSlots: 1, fill: p => p.Add(TradeData.Trinket.Id, 5));
        party.Inventory.Add(TradeData.Cheap.Id);

        Assert.Equal(TradeResult.InventoryFull, window.Take(TradeData.Trinket.Id));

        Assert.Equal(5, loot.Find(TradeData.Trinket.Id)?.Quantity);
        Assert.False(party.Inventory.Has(TradeData.Trinket.Id));
    }

    [Fact]
    public void TakingAPotionSetsTheFirstPotionFlagAndTakingAnythingElseDoesnt()
    {
        var (window, _, _, flags) = Open(fill: p =>
        {
            p.Add(TradeData.Trinket.Id);
            p.Add(TradeData.Potion.Id);
        });

        window.Take(TradeData.Trinket.Id);
        Assert.False(flags.Get(TradeData.PotionFlag));

        window.Take(TradeData.Potion.Id);
        Assert.True(flags.Get(TradeData.PotionFlag));
    }

    [Fact]
    public void DroppingDestroysTheItemsWithoutGivingThemToTheLoot()
    {
        var (window, loot, party, _) = Open();
        party.Inventory.Add(TradeData.Trinket.Id, 4);

        Assert.Equal(TradeResult.Done, window.Drop(TradeData.Trinket.Id, 3));

        Assert.Equal(1, party.Inventory.Count(TradeData.Trinket.Id));
        Assert.Null(loot.Find(TradeData.Trinket.Id));
    }

    [Fact]
    public void DroppingSomethingNotCarriedIsNoneLeft()
    {
        var (window, _, _, _) = Open();

        Assert.Equal(TradeResult.NoneLeft, window.Drop(TradeData.Trinket.Id, 1));
    }

    [Fact]
    public void DroppingIsRefusedWhenTheItemIsWorthOneGoldOrLessAtHalfItsPrice()
    {
        var three = TradeData.Priced(30, 3);
        var four = TradeData.Priced(31, 4);
        var catalog = TradeData.Catalog(more: [three, four]);
        var party = TradeData.PartyOf(catalog, null, TradeData.Member());
        var window = LootWindow.Open(new ItemPile(catalog), 0, party, catalog);
        party.Inventory.Add(three.Id);
        party.Inventory.Add(four.Id);

        // 3 × 0.5 is 1.5, which reads 1; 4 × 0.5 is 2.
        Assert.Equal(TradeResult.Refused, window.Drop(three.Id, 1));
        Assert.True(party.Inventory.Has(three.Id));
        Assert.Equal(TradeResult.Done, window.Drop(four.Id, 1));
        Assert.False(party.Inventory.Has(four.Id));
    }

    [Fact]
    public void TheWindowCantBeLeftWhileTheLootHoldsAnItemThatMustBeTaken()
    {
        var (window, _, _, _) = Open(fill: p =>
        {
            p.Add(TradeData.Trinket.Id);
            p.Add(TradeData.Quest.Id);
        });
        Assert.False(window.CanLeave);

        Assert.Equal(TradeResult.Done, window.Take(TradeData.Quest.Id));

        Assert.True(window.CanLeave);
    }
}

public class ContainerRollTests
{
    private static readonly ItemRecord A = ItemData.NewItem(41);
    private static readonly ItemRecord B = ItemData.NewItem(42);
    private static readonly ItemRecord C = ItemData.NewItem(43);
    private static readonly ItemRecord D = ItemData.NewItem(44);
    private static readonly ItemRecord F = ItemData.NewItem(46);
    private static readonly InventoryRecord Rules = TradeData.Rules();

    [Fact]
    public void ATableDrawsGoldTheReagentKindTheReagentChanceAndCountThenEachDropInOrder()
    {
        var table = TradeData.Table(10, 20, 50, 2, 4,
            new LootDrop(A.Id, 100, 1, 3), new LootDrop(B.Id, 30, 2, 2), new LootDrop(C.Id, 60), new LootDrop(null, 100),
            new LootDrop(D.Id, 0), new LootDrop(F.Id, 100));
        var random = new ScriptedRandom(
            RandomFor.Range(10, 20, 15),
            // 1 is a herb, between spice and gemstone.
            RandomFor.Range(0, 2, 1),
            RandomFor.D(100, 50), RandomFor.Range(2, 4, 3),
            RandomFor.D(100, 100), RandomFor.Range(1, 3, 2),
            // 31 misses a 30 percent chance, so no count follows.
            RandomFor.D(100, 31),
            RandomFor.D(100, 60),
            RandomFor.D(100, 1),
            // The fifth drop has no chance and rolls nothing.
            RandomFor.D(100, 7));

        var roll = Containers.Roll(table, Rules, random);

        Assert.Equal(30, roll.Gold);
        Assert.Equal([(TradeData.Herb.Id, 3), (A.Id, 2), (C.Id, 1), (F.Id, 1)], roll.Items.Select(i => (i.Item, i.Count)));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void TheReagentKindIsSpiceHerbOrGemstoneByItsDraw()
    {
        var table = TradeData.Table(0, 0, 100, 1, 1);

        RecordId Reagent(int kind) => Assert.Single(Containers.Roll(table, Rules,
            new ScriptedRandom(RandomFor.Range(0, 2, kind), RandomFor.D(100, 1))).Items).Item;

        Assert.Equal(TradeData.Spice.Id, Reagent(0));
        Assert.Equal(TradeData.Herb.Id, Reagent(1));
        Assert.Equal(TradeData.Gem.Id, Reagent(2));
    }

    [Fact]
    public void ARollAtTheChanceHitsAndOneOverMisses()
    {
        var table = TradeData.Table(0, 0, 20, 1, 1);

        var hit = Containers.Roll(table, Rules, new ScriptedRandom(RandomFor.Range(0, 2, 0), RandomFor.D(100, 20)));
        var miss = Containers.Roll(table, Rules, new ScriptedRandom(RandomFor.Range(0, 2, 0), RandomFor.D(100, 21)));

        Assert.Single(hit.Items);
        Assert.Empty(miss.Items);
    }

    [Fact]
    public void AReagentWithNoChanceRollsNothingButItsKindIsStillDrawn()
    {
        var table = TradeData.Table(0, 0, 0, 1, 3);
        var random = new ScriptedRandom(RandomFor.Range(0, 2, 2));

        var roll = Containers.Roll(table, Rules, random);

        Assert.Empty(roll.Items);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void EqualBoundsDrawNothingAndGoldIsDoubled()
    {
        var table = TradeData.Table(5, 5, 0, 0, 0);
        var random = new ScriptedRandom(RandomFor.Range(0, 2, 0));

        var roll = Containers.Roll(table, Rules, random);

        Assert.Equal(10, roll.Gold);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void ADropWithNoItemOrACountOfZeroGivesNothingButStillRollsItsChance()
    {
        var table = TradeData.Table(5, 5, 0, 0, 0, new LootDrop(null, 100, 1, 3), new LootDrop(A.Id, 100, 0, 0));
        var random = new ScriptedRandom(
            RandomFor.Range(0, 2, 0),
            // The empty drop draws its count and throws it away.
            RandomFor.D(100, 10), RandomFor.Range(1, 3, 2),
            // The second has equal bounds of 0 and draws no count.
            RandomFor.D(100, 10));

        var roll = Containers.Roll(table, Rules, random);

        Assert.Empty(roll.Items);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void OnlyTheFirstTwoDropsGiveACountAndTheRestGiveOne()
    {
        var table = TradeData.Table(5, 5, 0, 0, 0,
            new LootDrop(A.Id, 100, 1, 1), new LootDrop(B.Id, 100, 1, 1), new LootDrop(C.Id, 100, 3, 9));
        var random = new ScriptedRandom(RandomFor.Range(0, 2, 0), RandomFor.D(100, 1), RandomFor.D(100, 1), RandomFor.D(100, 1));

        var roll = Containers.Roll(table, Rules, random);

        Assert.Equal([(A.Id, 1), (B.Id, 1), (C.Id, 1)], roll.Items.Select(i => (i.Item, i.Count)));
        Assert.Equal(0, random.Remaining);
    }
}

public class ContainerRulesTests
{
    private static SkillLevels Skills(int? ranger = null, int? thief = null, int? mechanic = null) => new(Mechanic: mechanic, Ranger: ranger, Thief: thief);

    private static CharacterParty PartyOf(params Character[] members) => TradeData.PartyOf(TradeData.Catalog(), null, members);

    [Fact]
    public void TheHarvestShareOfAnEmptyPartyIsZeroAndDrawsNothing()
    {
        var random = new ScriptedRandom();

        Assert.Equal(0f, Containers.HarvestShare(PartyOf(), random));
    }

    [Fact]
    public void TheHarvestShareIsTheBestRangersIntelligencePlusSevenTimesRangerTimesTheRollOverAHundred()
    {
        var party = PartyOf(TradeData.Member(0, Skills(ranger: 2)));

        // 18 + 2 × 7 is 32.
        TradeData.AssertNear(0.96, Containers.HarvestShare(party, new ScriptedRandom(RandomFor.D(100, 3))));
        TradeData.AssertNear(0.32, Containers.HarvestShare(party, new ScriptedRandom(RandomFor.D(100, 1))));
    }

    [Fact]
    public void TheHarvestShareIsClampedToOne()
    {
        var party = PartyOf(TradeData.Member(0, Skills(ranger: 2)));

        Assert.Equal(1f, Containers.HarvestShare(party, new ScriptedRandom(RandomFor.D(100, 4))));
        Assert.Equal(1f, Containers.HarvestShare(party, new ScriptedRandom(RandomFor.D(100, 100))));
    }

    [Fact]
    public void TheBestRangerIsTheLaterMemberOnATie()
    {
        var party = PartyOf(TradeData.Member(0, Skills(ranger: 2), intelligence: 18), TradeData.Member(1, Skills(ranger: 2), intelligence: 10));

        // The second's 10 + 14 is 24, not the first's 32.
        TradeData.AssertNear(0.24, Containers.HarvestShare(party, new ScriptedRandom(RandomFor.D(100, 1))));
    }

    [Fact]
    public void ADeadRangerStillCounts()
    {
        var party = PartyOf(TradeData.Member(0, Skills(ranger: 2)), TradeData.Dead(TradeData.Member(1, Skills(ranger: 3), intelligence: 10)));

        // 10 + 3 × 7 is 31.
        TradeData.AssertNear(0.31, Containers.HarvestShare(party, new ScriptedRandom(RandomFor.D(100, 1))));
    }

    [Fact]
    public void HarvestingGivesTheCountTimesTheShareToAByteAndAtLeastOne()
    {
        Assert.Equal(5, Containers.Harvest(10, 0.5f));
        Assert.Equal(10, Containers.Harvest(10, 1f));
        Assert.Equal(1, Containers.Harvest(10, 0f));
        Assert.Equal(1, Containers.Harvest(10, 0.09f));
        // 300 as a byte is 44.
        Assert.Equal(44, Containers.Harvest(300, 1f));
    }

    [Fact]
    public void PickingALockWithNoOneInThePartyIsNoOne()
    {
        Assert.Equal(LockResult.NoOne, Containers.PickLock(PartyOf(), 1));
        Assert.Equal(LockResult.NoOne, Containers.PickLock(PartyOf(), 10));
    }

    [Fact]
    public void ALockOfTenOrMoreIsFarBeyondEvenTheBestThief()
    {
        var party = PartyOf(TradeData.Member(0, Skills(thief: 15)));

        Assert.Equal(LockResult.FarBeyond, Containers.PickLock(party, 10));
        Assert.Equal(LockResult.FarBeyond, Containers.PickLock(party, 12));
        Assert.Equal(30, party.Members.Single().Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ALockAtOrAboveTheThiefsLevelIsBeyondByHowFarAbove()
    {
        var party = PartyOf(TradeData.Member(0, Skills(thief: 4)));

        Assert.Equal(LockResult.JustBeyond, Containers.PickLock(party, 4));
        Assert.Equal(LockResult.JustBeyond, Containers.PickLock(party, 6));
        Assert.Equal(LockResult.Beyond, Containers.PickLock(party, 7));
        Assert.Equal(LockResult.Beyond, Containers.PickLock(party, 9));
        Assert.Equal(30, party.Members.Single().Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ALockSixOrMoreLevelsAboveTheThiefIsFarBeyond()
    {
        var party = PartyOf(TradeData.Member(0, Skills(thief: 1)));

        Assert.Equal(LockResult.Beyond, Containers.PickLock(party, 6));
        Assert.Equal(LockResult.FarBeyond, Containers.PickLock(party, 7));
    }

    [Fact]
    public void ALockBelowTheThiefsLevelIsPickedForTenLessTheLevelInStaminaAndEnduranceIsUntouched()
    {
        var thief = TradeData.Member(0, Skills(thief: 4));
        var party = PartyOf(thief);

        Assert.Equal(LockResult.Picked, Containers.PickLock(party, 3));

        Assert.Equal(30 - 6, thief.Stats.Modified(Stat.Stamina));
        Assert.Equal(15, thief.Stats.Modified(Stat.Endurance));
        Assert.Equal(24, thief.Stats.Current(Stat.Stamina));
    }

    [Fact]
    public void AThiefsLevelCountsOnlyUpToTenAndTheCostIsAtLeastOne()
    {
        var thief = TradeData.Member(0, Skills(thief: 12));
        var party = PartyOf(thief);

        Assert.Equal(LockResult.Picked, Containers.PickLock(party, 9));

        Assert.Equal(29, thief.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void AThiefWithoutTheStaminaItCostsIsTooTiredAndWithJustEnoughPicksIt()
    {
        var tired = TradeData.Member(0, Skills(thief: 3), stamina: 6);
        Assert.Equal(LockResult.TooTired, Containers.PickLock(PartyOf(tired), 1));
        Assert.Equal(6, tired.Stats.Modified(Stat.Stamina));

        var ready = TradeData.Member(1, Skills(thief: 3), stamina: 7);
        Assert.Equal(LockResult.Picked, Containers.PickLock(PartyOf(ready), 1));
        Assert.Equal(0, ready.Stats.Modified(Stat.Stamina));
        Assert.Equal(15, ready.Stats.Modified(Stat.Endurance));
    }

    [Fact]
    public void TheBestThiefIsTheLaterMemberOnATie()
    {
        var first = TradeData.Member(0, Skills(thief: 4));
        var second = TradeData.Member(1, Skills(thief: 4));

        Assert.Equal(LockResult.Picked, Containers.PickLock(PartyOf(first, second), 2));

        Assert.Equal(30, first.Stats.Modified(Stat.Stamina));
        Assert.Equal(24, second.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ADeadThiefStillCountsAndHasNoStaminaToPickWithSoTheLockIsTooTiring()
    {
        var alive = TradeData.Member(0, Skills(thief: 4));
        var dead = TradeData.Dead(TradeData.Member(1, Skills(thief: 4)));

        Assert.Equal(LockResult.TooTired, Containers.PickLock(PartyOf(alive, dead), 2));

        Assert.Equal(30, alive.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void DisarmingWithNoOneWhoCanTryFailsWithoutRollingDice()
    {
        var untrained = TradeData.Member(0, Skills(mechanic: 0));
        Assert.False(Containers.Disarm(PartyOf(untrained), 1, new ScriptedRandom()));

        // A Mechanic of 2 costs 8 Stamina, and this one has 7.
        var weak = TradeData.Member(1, Skills(mechanic: 2), stamina: 7);
        Assert.False(Containers.Disarm(PartyOf(weak), 1, new ScriptedRandom()));
        Assert.False(Containers.Disarm(PartyOf(), 0, new ScriptedRandom()));
    }

    [Fact]
    public void TheMechanicIsTheMostSkilledMemberWhoCanPayTheStaminaItCosts()
    {
        // The better Mechanic of 5 costs 5 Stamina and has 4, so the Mechanic of 2 tries.
        var exhausted = TradeData.Member(0, Skills(mechanic: 5), stamina: 4);
        var steady = TradeData.Member(1, Skills(mechanic: 2));
        var party = PartyOf(exhausted, steady);

        // 18 × 4 + 2 × 10 is 92; 92 - 10 is 82, which is step 5.
        Assert.True(Containers.Disarm(party, 5, new ScriptedRandom(RandomFor.D(100, 10))));

        Assert.Equal(30 - 8, steady.Stats.Modified(Stat.Stamina));
        Assert.Equal(4, exhausted.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ATrapAboveTheStepIsNotDisarmedButTheStaminaIsStillPaid()
    {
        var mechanic = TradeData.Member(0, Skills(mechanic: 2));

        Assert.False(Containers.Disarm(PartyOf(mechanic), 6, new ScriptedRandom(RandomFor.D(100, 10))));

        Assert.Equal(22, mechanic.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ARollAtOrOverTheTotalIsStepZeroSoOnlyATrapOfLevelZeroIsDisarmedAndNoStaminaIsPaid()
    {
        var mechanic = TradeData.Member(0, Skills(mechanic: 2));
        var party = PartyOf(mechanic);

        Assert.True(Containers.Disarm(party, 0, new ScriptedRandom(RandomFor.D(100, 92))));
        Assert.False(Containers.Disarm(party, 1, new ScriptedRandom(RandomFor.D(100, 100))));

        Assert.Equal(30, mechanic.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void TheFirstMemberWinsATieInMechanic()
    {
        var first = TradeData.Member(0, Skills(mechanic: 3));
        var second = TradeData.Member(1, Skills(mechanic: 3));

        Containers.Disarm(PartyOf(first, second), 1, new ScriptedRandom(RandomFor.D(100, 10)));

        Assert.Equal(30 - 7, first.Stats.Modified(Stat.Stamina));
        Assert.Equal(30, second.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void ATrapHurtsEachLivingMemberByTheLevelTimesAFourSidedDieOnTheirEnduranceAlone()
    {
        var first = TradeData.Member(0);
        var second = TradeData.Member(1);
        var party = PartyOf(first, second);
        var random = new ScriptedRandom(
            // Level 2: a first d4 of 1 makes 2, which isn't below 2, so a second d4 of 3 gives 6.
            RandomFor.D(4, 1), RandomFor.D(4, 3),
            RandomFor.D(4, 4), RandomFor.D(4, 2));

        var hurt = Containers.TrapDamage(party, 2, random);

        Assert.Equal([(first, 6), (second, 4)], hurt.Select(h => (h.Member, h.Damage)));
        Assert.Equal(15 - 6, first.Stats.Modified(Stat.Endurance));
        Assert.Equal(15 - 4, second.Stats.Modified(Stat.Endurance));
        Assert.Equal(30, first.Stats.Modified(Stat.Stamina));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void AFirstDieThatMakesLessThanTwoGivesOneDamageWithNoSecondRoll()
    {
        var member = TradeData.Member(0);
        var random = new ScriptedRandom(RandomFor.D(4, 1));

        var hurt = Containers.TrapDamage(PartyOf(member), 1, random);

        Assert.Equal(1, Assert.Single(hurt).Damage);
        Assert.Equal(14, member.Stats.Modified(Stat.Endurance));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void AFirstDieOfTwoAtLevelOneRollsASecondDieThatCounts()
    {
        var hurt = Containers.TrapDamage(PartyOf(TradeData.Member(0)), 1, new ScriptedRandom(RandomFor.D(4, 2), RandomFor.D(4, 4)));

        Assert.Equal(4, Assert.Single(hurt).Damage);
    }

    [Fact]
    public void TrapDamageNeverTakesEnduranceBelowOneAndASkippedDeadMemberRollsNothing()
    {
        var frail = TradeData.Member(0, endurance: 3);
        var dead = TradeData.Dead(TradeData.Member(1));
        var random = new ScriptedRandom(RandomFor.D(4, 1), RandomFor.D(4, 2));

        var hurt = Containers.TrapDamage(PartyOf(dead, frail), 5, random);

        Assert.Equal([(frail, 10)], hurt.Select(h => (h.Member, h.Damage)));
        Assert.Equal(1, frail.Stats.Modified(Stat.Endurance));
        Assert.Equal(0, random.Remaining);
    }
}

public class ContainerTests
{
    private static readonly RecordId Opened = ItemData.Flag(4);
    private static readonly RecordId Exploded = ItemData.Flag(6);
    private static readonly RecordId Used = ItemData.Flag(7);

    private static ContainerObject Chest(ContainerKind kind = ContainerKind.Chest, int lockLevel = 0, int trapLevel = 0, RecordId? loot = null,
        int gold = 0, RecordId? item = null, FlagWrite? used = null, bool withFlags = true) => new(
        0, Vector3.Zero, 1f, PakVariant.Any, null, kind, withFlags ? Opened : null, withFlags ? Exploded : null, lockLevel, trapLevel, loot, gold, item)
    {
        Used = used,
    };

    private static CharacterParty PartyOf(params Character[] members) => TradeData.PartyOf(TradeData.Catalog(), null, members);

    [Fact]
    public void AContainerOpenedOrExplodedIsGone()
    {
        var flags = ItemData.Flags();
        var chest = new Container(Chest(lockLevel: 2, trapLevel: 1));
        Assert.Equal(ContainerStep.Trapped, chest.Next(flags));

        flags.Set(Opened, true);
        Assert.Equal(ContainerStep.Gone, chest.Next(flags));

        flags.Set(Opened, false);
        flags.Set(Exploded, true);
        Assert.Equal(ContainerStep.Gone, chest.Next(flags));
    }

    [Fact]
    public void AHerbSpiceOrGemSourceIsHarvestedWhateverItsTrapAndLockButNotOnceGone()
    {
        var flags = ItemData.Flags();

        foreach (var kind in new[] { ContainerKind.Herb, ContainerKind.Spice, ContainerKind.Gem })
            Assert.Equal(ContainerStep.Harvest, new Container(Chest(kind, lockLevel: 3, trapLevel: 3)).Next(flags));
        Assert.Equal(ContainerStep.Open, new Container(Chest(ContainerKind.Custom)).Next(flags));

        flags.Set(Opened, true);
        Assert.Equal(ContainerStep.Gone, new Container(Chest(ContainerKind.Herb)).Next(flags));
    }

    [Fact]
    public void ATrapComesBeforeALockWhichComesBeforeOpening()
    {
        var flags = ItemData.Flags();

        Assert.Equal(ContainerStep.Trapped, new Container(Chest(lockLevel: 2, trapLevel: 1)).Next(flags));
        Assert.Equal(ContainerStep.Locked, new Container(Chest(lockLevel: 2)).Next(flags));
        Assert.Equal(ContainerStep.Open, new Container(Chest()).Next(flags));
    }

    [Fact]
    public void AContainerWithNoFlagsIsNeverGone()
    {
        Assert.Equal(ContainerStep.Open, new Container(Chest(withFlags: false)).Next(ItemData.Flags()));
    }

    [Fact]
    public void PickingTheLockSuccessfullyClearsItAndFailingKeepsIt()
    {
        var flags = ItemData.Flags();
        var chest = new Container(Chest(lockLevel: 5));
        var thief = TradeData.Member(0, new SkillLevels(Thief: 3));
        var party = PartyOf(thief);

        Assert.Equal(LockResult.JustBeyond, chest.PickLock(party));
        Assert.Equal(5, chest.LockLevel);
        Assert.Equal(ContainerStep.Locked, chest.Next(flags));

        var better = new Container(Chest(lockLevel: 2));
        Assert.Equal(LockResult.Picked, better.PickLock(party));
        Assert.Equal(0, better.LockLevel);
        Assert.Equal(ContainerStep.Open, better.Next(flags));
    }

    [Fact]
    public void DisarmingATrapClearsItAndGivesNoDamageNorExplosion()
    {
        var flags = ItemData.Flags();
        var chest = new Container(Chest(trapLevel: 3));
        var mechanic = TradeData.Member(0, new SkillLevels(Mechanic: 2));

        // Step 5 reaches level 3.
        var damage = chest.Disarm(PartyOf(mechanic), flags, new ScriptedRandom(RandomFor.D(100, 10)));

        Assert.Null(damage);
        Assert.Equal(0, chest.TrapLevel);
        Assert.False(flags.Get(Exploded));
        Assert.Equal(15, mechanic.Stats.Modified(Stat.Endurance));
    }

    [Fact]
    public void FailingToDisarmClearsTheTrapSetsTheExplodedFlagAndHurtsTheParty()
    {
        var flags = ItemData.Flags();
        var chest = new Container(Chest(lockLevel: 1, trapLevel: 3));
        var member = TradeData.Member(0);
        // No Mechanic: no dice for the disarm. The trap's level 3 × 2 is 6, and a second d4 of 3 makes 9.
        var random = new ScriptedRandom(RandomFor.D(4, 2), RandomFor.D(4, 3));

        var damage = chest.Disarm(PartyOf(member), flags, random);

        Assert.Equal([(member, 9)], damage!.Select(d => (d.Member, d.Damage)));
        Assert.Equal(0, chest.TrapLevel);
        Assert.True(flags.Get(Exploded));
        Assert.Equal(15 - 9, member.Stats.Modified(Stat.Endurance));
        Assert.Equal(ContainerStep.Gone, chest.Next(flags));
    }

    [Fact]
    public void FailingToDisarmALevelThatTheStepFallsShortOfAlsoExplodes()
    {
        var flags = ItemData.Flags();
        var chest = new Container(Chest(trapLevel: 6));
        var mechanic = TradeData.Member(0, new SkillLevels(Mechanic: 2));

        // Step 5 doesn't reach 6, then the trap's d4s: 6 × 1 isn't below 2, and 6 × 1 is 6.
        var damage = chest.Disarm(PartyOf(mechanic), flags, new ScriptedRandom(RandomFor.D(100, 10), RandomFor.D(4, 1), RandomFor.D(4, 1)));

        Assert.Equal(6, Assert.Single(damage!).Damage);
        Assert.True(flags.Get(Exploded));
        Assert.Equal(0, chest.TrapLevel);
    }

    [Fact]
    public void FailingToDisarmWithNoExplodedFlagOnTheRecordStillHurtsTheParty()
    {
        var flags = ItemData.Flags();
        var chest = new Container(Chest(trapLevel: 1, withFlags: false));

        var damage = chest.Disarm(PartyOf(TradeData.Member(0)), flags, new ScriptedRandom(RandomFor.D(4, 1)));

        Assert.Equal(1, Assert.Single(damage!).Damage);
    }

    [Fact]
    public void OpeningSetsTheOpenedAndUsedFlagsAndAddsTheItemItAlwaysHolds()
    {
        var flags = ItemData.Flags();
        var catalog = TradeData.Catalog();
        var chest = new Container(Chest(gold: 7, item: TradeData.Trinket.Id, used: new FlagWrite(Used)));

        var (items, gold) = chest.Open(flags, catalog, _ => throw new InvalidOperationException(), new ScriptedRandom());

        Assert.True(flags.Get(Opened));
        Assert.True(flags.Get(Used));
        Assert.Equal(7, gold);
        Assert.Equal(1, items.Find(TradeData.Trinket.Id)?.Quantity);
        Assert.Single(items.Stacks);
    }

    [Fact]
    public void OpeningWithAClearingFlagWriteClearsTheUsedFlag()
    {
        var flags = ItemData.Flags();
        flags.Set(Used, true);
        var chest = new Container(Chest(used: new FlagWrite(Used, Clear: true)));

        chest.Open(flags, TradeData.Catalog(), _ => throw new InvalidOperationException(), new ScriptedRandom());

        Assert.False(flags.Get(Used));
        Assert.True(flags.Get(Opened));
    }

    [Fact]
    public void OpeningARecordWithNoFlagsAndNothingInItGivesAnEmptyPile()
    {
        var chest = new Container(Chest(withFlags: false));

        var (items, gold) = chest.Open(ItemData.Flags(), TradeData.Catalog(), _ => throw new InvalidOperationException(), new ScriptedRandom());

        Assert.Empty(items.Stacks);
        Assert.Equal(0, gold);
    }

    [Fact]
    public void OpeningRollsTheLootTableWhoseGoldReplacesTheRecordsAndTheItemIsAddedAfter()
    {
        var table = TradeData.Table(20, 20, 0, 0, 0, new LootDrop(TradeData.Cheap.Id, 100, 1, 1));
        var chest = new Container(Chest(gold: 7, loot: table.Id, item: TradeData.Trinket.Id));
        var random = new ScriptedRandom(RandomFor.Range(0, 2, 1), RandomFor.D(100, 5));
        RecordId? asked = null;

        var (items, gold) = chest.Open(ItemData.Flags(), TradeData.Catalog(), id => { asked = id; return table; }, random);

        Assert.Equal(table.Id, asked);
        Assert.Equal(40, gold);
        Assert.Equal([TradeData.Cheap.Id, TradeData.Trinket.Id], items.Stacks.Select(s => s.Item.Id));
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void HarvestingGivesTheKindsReagentInTheCountTimesTheShareAndSetsTheUsedFlag()
    {
        var catalog = TradeData.Catalog();
        var cases = new[] { (ContainerKind.Herb, TradeData.Herb.Id), (ContainerKind.Spice, TradeData.Spice.Id), (ContainerKind.Gem, TradeData.Gem.Id) };

        foreach (var (kind, reagent) in cases)
        {
            var flags = ItemData.Flags();
            var source = new Container(Chest(kind, gold: 10, used: new FlagWrite(Used)));

            var items = source.Harvest(0.5f, flags, catalog);

            var stack = Assert.Single(items.Stacks);
            Assert.Equal((reagent, 5), (stack.Item.Id, stack.Quantity));
            Assert.True(flags.Get(Used));
            Assert.False(flags.Get(Opened));
        }
    }

    [Fact]
    public void HarvestingWithNoShareStillGivesOne()
    {
        var source = new Container(Chest(ContainerKind.Herb, gold: 10));

        var items = source.Harvest(0f, ItemData.Flags(), TradeData.Catalog());

        Assert.Equal(1, Assert.Single(items.Stacks).Quantity);
    }
}

public class CraftingTests
{
    private static readonly ArmorRecord Armor = TradeData.Leather;
    private static readonly RecordId AlchemistSkill = Crafting.Alchemist;
    private static readonly RecordId MechanicSkill = Crafting.Mechanic;

    private static (CharacterParty Party, Character Member) Setup(SkillLevels skills, int stamina = 30, int intelligence = 18)
    {
        var member = TradeData.Member(0, skills, intelligence, stamina);
        return (TradeData.PartyOf(TradeData.Catalog(), null, member), member);
    }

    private static RecipeRecord Brewing(int level = 2, int step = 0) =>
        TradeData.Recipe(1, AlchemistSkill, TradeData.Potion.Id, level, step, new Ingredient(TradeData.Herb.Id, 2), new Ingredient(TradeData.Spice.Id, 1));

    private static RecipeRecord Smithing(int level = 1, int step = 4) =>
        TradeData.Recipe(2, MechanicSkill, Armor.Id, level, step, new Ingredient(TradeData.Hide.Id, 1));

    private static void Stock(CharacterParty party, int herb, int spice)
    {
        party.Inventory.Add(TradeData.Herb.Id, herb);
        party.Inventory.Add(TradeData.Spice.Id, spice);
    }

    [Fact]
    public void TheSkillsAreTheRecordIdsOfAlchemistAndMechanic()
    {
        Assert.Equal(RecordId.Decimal(RecordTypes.Skill, 0), Crafting.Alchemist);
        Assert.Equal(RecordId.Decimal(RecordTypes.Skill, 4), Crafting.Mechanic);
    }

    [Fact]
    public void OfferedGivesAlchemistRecipesUpToTheModifiedLevelWhetherOrNotThePartyHasTheReagents()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 3));
        var recipes = new[]
        {
            TradeData.Recipe(1, AlchemistSkill, TradeData.Potion.Id, 1, 0),
            TradeData.Recipe(2, AlchemistSkill, TradeData.Potion.Id, 3, 0, new Ingredient(TradeData.Herb.Id, 5)),
            TradeData.Recipe(3, AlchemistSkill, TradeData.Potion.Id, 4, 0),
        };

        Assert.Equal([1, 2], Crafting.Offered(party, member, recipes).Select(r => int.Parse(r.Id.ToString().Split('/')[1])));

        // Gear raising the modified level to 5 offers the level 4 recipe too.
        member.Equipment.EquipGear(ItemData.Inst(ItemData.NewItem(50, ItemKind.Helmet, bonuses: ItemData.SkillGives(Skill.Alchemist, 2))));
        Assert.Equal(3, Crafting.Offered(party, member, recipes).Count());
    }

    [Fact]
    public void OfferedGivesMechanicRecipesUpToTheBaseLevelWhoseIngredientsThePartyHas()
    {
        var (party, member) = Setup(new SkillLevels(Mechanic: 2));
        party.Inventory.Add(TradeData.Hide.Id);
        var other = TradeData.Priced(51, 5);
        var recipes = new[]
        {
            Smithing(level: 2),
            TradeData.Recipe(3, MechanicSkill, Armor.Id, 2, 4, new Ingredient(other.Id, 1)),
            TradeData.Recipe(4, MechanicSkill, Armor.Id, 3, 4, new Ingredient(TradeData.Hide.Id, 1)),
        };

        var offered = Crafting.Offered(party, member, recipes).ToList();

        Assert.Equal([recipes[0]], offered);
    }

    [Fact]
    public void OfferedGoesByTheBaseMechanicLevelNotTheModifiedOne()
    {
        var (party, member) = Setup(new SkillLevels(Mechanic: 2));
        party.Inventory.Add(TradeData.Hide.Id);
        member.Equipment.EquipGear(ItemData.Inst(ItemData.NewItem(50, ItemKind.Helmet, bonuses: ItemData.SkillGives(Skill.Mechanic, 3))));
        Assert.Equal(5, member.Skills.Modified(Skill.Mechanic));

        Assert.Empty(Crafting.Offered(party, member, [Smithing(level: 3)]));
        Assert.Single(Crafting.Offered(party, member, [Smithing(level: 2)]));
    }

    [Fact]
    public void OfferedLeavesOutRecipesOfOtherSkills()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 5, Mechanic: 5));
        var recipe = TradeData.Recipe(9, RecordId.Decimal(RecordTypes.Skill, 2), TradeData.Potion.Id, 1, 0);

        Assert.Empty(Crafting.Offered(party, member, [recipe]));
    }

    [Fact]
    public void BrewingWithoutAnIngredientIsMissingIngredientsAndUsesNothing()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 2));
        Stock(party, herb: 1, spice: 1);

        var result = Crafting.Brew(party, member, Brewing(), new ScriptedRandom());

        Assert.Equal(CraftResult.MissingIngredients, result);
        Assert.Equal(1, party.Inventory.Count(TradeData.Herb.Id));
        Assert.Equal(1, party.Inventory.Count(TradeData.Spice.Id));
    }

    [Fact]
    public void BrewingWhenThePartyHoldsAFullStackOfTheProductIsStackFullAndUsesNothing()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 2));
        Stock(party, herb: 2, spice: 1);
        party.Inventory.Add(TradeData.Potion.Id, 99);

        Assert.Equal(CraftResult.StackFull, Crafting.Brew(party, member, Brewing(), new ScriptedRandom()));

        Assert.Equal(2, party.Inventory.Count(TradeData.Herb.Id));
        Assert.Equal(1, party.Inventory.Count(TradeData.Spice.Id));
    }

    [Fact]
    public void MissingIngredientsAreReportedBeforeAFullStack()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 2));
        party.Inventory.Add(TradeData.Potion.Id, 99);

        Assert.Equal(CraftResult.MissingIngredients, Crafting.Brew(party, member, Brewing(), new ScriptedRandom()));
    }

    [Fact]
    public void BrewingBelowTheRecipesLevelUsesTheIngredientsUpAndIsTooLowWithoutRollingDice()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 1));
        Stock(party, herb: 2, spice: 1);

        var result = Crafting.Brew(party, member, Brewing(level: 2), new ScriptedRandom());

        Assert.Equal(CraftResult.TooLow, result);
        Assert.False(party.Inventory.Has(TradeData.Herb.Id));
        Assert.False(party.Inventory.Has(TradeData.Spice.Id));
        Assert.False(party.Inventory.Has(TradeData.Potion.Id));
    }

    [Fact]
    public void BrewingMakesOnePotionWhenTheRollIsBelowTheTotalAndSpendsNoStamina()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 2));
        Stock(party, herb: 3, spice: 1);

        // 18 × 3 + 2 × 6 is 66; 66 - 10 is 56, which is step 3.
        var result = Crafting.Brew(party, member, Brewing(), new ScriptedRandom(RandomFor.D(100, 10)));

        Assert.Equal(CraftResult.Made, result);
        Assert.Equal(1, party.Inventory.Count(TradeData.Potion.Id));
        Assert.Equal(1, party.Inventory.Count(TradeData.Herb.Id));
        Assert.False(party.Inventory.Has(TradeData.Spice.Id));
        Assert.Equal(30, member.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void BrewingFailsWhenTheRollReachesTheTotalAndTheIngredientsAreUsedAll()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 2));

        foreach (var roll in new[] { 66, 80 })
        {
            Stock(party, herb: 2, spice: 1);

            Assert.Equal(CraftResult.Failed, Crafting.Brew(party, member, Brewing(), new ScriptedRandom(RandomFor.D(100, roll))));

            Assert.False(party.Inventory.Has(TradeData.Herb.Id));
            Assert.False(party.Inventory.Has(TradeData.Potion.Id));
        }
    }

    [Fact]
    public void BrewingFailsWhenTheStepFallsShortOfTheRecipesStep()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 8));
        // 18 × 3 + 8 × 6 is 102: a roll of 20 leaves 82, which is step 5, and 25 leaves 77, which is 4.
        Stock(party, herb: 4, spice: 2);

        Assert.Equal(CraftResult.Made, Crafting.Brew(party, member, Brewing(step: 5), new ScriptedRandom(RandomFor.D(100, 20))));
        Assert.Equal(CraftResult.Failed, Crafting.Brew(party, member, Brewing(step: 5), new ScriptedRandom(RandomFor.D(100, 25))));

        Assert.Equal(1, party.Inventory.Count(TradeData.Potion.Id));
    }

    [Fact]
    public void MakeBrewsAnAlchemistsRecipeAndMakesArmorForAMechanics()
    {
        var (party, member) = Setup(new SkillLevels(Alchemist: 2, Mechanic: 2));
        Stock(party, herb: 2, spice: 1);
        party.Inventory.Add(TradeData.Hide.Id);

        Assert.Equal(CraftResult.Made, Crafting.Make(party, member, Brewing(), new ScriptedRandom(RandomFor.D(100, 10))));
        Assert.Equal(CraftResult.Made, Crafting.Make(party, member, Smithing(), new ScriptedRandom(RandomFor.D(100, 10))));

        Assert.True(party.Inventory.Has(TradeData.Potion.Id));
        Assert.True(party.Inventory.Has(Armor.Id));
    }

    [Fact]
    public void MakingArmorUsesTheHideAndMakesItWhenTheStepReachesTheRecipesAndPaysStamina()
    {
        var (party, member) = Setup(new SkillLevels(Mechanic: 2));
        party.Inventory.Add(TradeData.Hide.Id);

        // (2 × 5 + 18 + 15) × 2 is 86; 86 - 10 is 76, which is step 4. A Mechanic of 2 pays 3 Stamina.
        var result = Crafting.MakeArmor(party, member, Smithing(step: 4), new ScriptedRandom(RandomFor.D(100, 10)));

        Assert.Equal(CraftResult.Made, result);
        Assert.Equal(1, party.Inventory.Count(Armor.Id));
        Assert.False(party.Inventory.Has(TradeData.Hide.Id));
        Assert.Equal(27, member.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void MakingArmorFailsWhenTheStepIsBelowTheRecipesAndTheHideIsStillUsed()
    {
        var (party, member) = Setup(new SkillLevels(Mechanic: 2));
        party.Inventory.Add(TradeData.Hide.Id);

        var result = Crafting.MakeArmor(party, member, Smithing(step: 5), new ScriptedRandom(RandomFor.D(100, 10)));

        Assert.Equal(CraftResult.Failed, result);
        Assert.False(party.Inventory.Has(Armor.Id));
        Assert.False(party.Inventory.Has(TradeData.Hide.Id));
        Assert.Equal(27, member.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void MakingArmorWithARollAtOrOverTheTotalFailsAndPaysNoStamina()
    {
        var (party, member) = Setup(new SkillLevels(Mechanic: 2));
        party.Inventory.Add(TradeData.Hide.Id);

        var result = Crafting.MakeArmor(party, member, Smithing(step: 1), new ScriptedRandom(RandomFor.D(100, 86)));

        Assert.Equal(CraftResult.Failed, result);
        Assert.False(party.Inventory.Has(TradeData.Hide.Id));
        Assert.Equal(30, member.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void MakingArmorWithoutTheStaminaItCostsFailsButUsesTheHide()
    {
        var (party, member) = Setup(new SkillLevels(Mechanic: 2), stamina: 2);
        party.Inventory.Add(TradeData.Hide.Id);

        var result = Crafting.MakeArmor(party, member, Smithing(step: 1), new ScriptedRandom(RandomFor.D(100, 10)));

        Assert.Equal(CraftResult.Failed, result);
        Assert.False(party.Inventory.Has(TradeData.Hide.Id));
        Assert.Equal(2, member.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void MakingArmorWithoutTheHideIsMissingIngredientsButTheStaminaIsSpentAnyway()
    {
        var (party, member) = Setup(new SkillLevels(Mechanic: 2));

        var result = Crafting.MakeArmor(party, member, Smithing(step: 4), new ScriptedRandom(RandomFor.D(100, 10)));

        Assert.Equal(CraftResult.MissingIngredients, result);
        Assert.Equal(27, member.Stats.Modified(Stat.Stamina));
        Assert.False(party.Inventory.Has(Armor.Id));
    }

    // A target who has taken 20 damage, which comes off Stamina after the Level's point.
    private static Character Hurt(int n, int damage = 20)
    {
        var target = TradeData.Member(n);
        target.TakeDamage(damage);
        return target;
    }

    [Fact]
    public void LayingOnHandsAtFullHitPointsIsAlreadyWell()
    {
        var healer = TradeData.Member(0, new SkillLevels(Healer: 4));
        var target = TradeData.Member(1);

        Assert.Equal(new HealResult(HealOutcome.AlreadyWell), Crafting.LayOnHands(healer, target));

        Assert.Equal(30, healer.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void LayingOnHandsWithUnderThreeStaminaFailsAndAFullHitPointCheckComesFirst()
    {
        var weary = TradeData.Member(0, new SkillLevels(Healer: 4), stamina: 2);

        Assert.Equal(new HealResult(HealOutcome.Failed), Crafting.LayOnHands(weary, Hurt(1)));
        Assert.Equal(new HealResult(HealOutcome.AlreadyWell), Crafting.LayOnHands(weary, TradeData.Member(2)));
        Assert.Equal(2, weary.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void LayingOnHandsSpendsThreeStaminaAndHealsTwiceTheHealerPlusFive()
    {
        var healer = TradeData.Member(0, new SkillLevels(Healer: 4));
        var target = Hurt(1);
        var before = target.Hp;

        var result = Crafting.LayOnHands(healer, target);

        Assert.Equal(new HealResult(HealOutcome.Healed, 13), result);
        Assert.Equal(before + 13, target.Hp);
        Assert.Equal(27, healer.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void LayingOnHandsReportsOnlyTheHitPointsActuallyGained()
    {
        var healer = TradeData.Member(0, new SkillLevels(Healer: 4));
        var target = Hurt(1, damage: 5);

        var result = Crafting.LayOnHands(healer, target);

        Assert.Equal(new HealResult(HealOutcome.Healed, 5), result);
        Assert.Equal(target.MaxHp, target.Hp);
    }

    private static (CharacterParty Party, Character Healer, Character Target) Healing(int healer, int stamina = 30, int intelligence = 18, int damage = 30, bool herb = true)
    {
        var healerMember = TradeData.Member(0, new SkillLevels(Healer: healer), intelligence, stamina);
        var target = Hurt(1, damage);
        var party = TradeData.PartyOf(TradeData.Catalog(), null, healerMember, target);
        if (herb)
            party.Inventory.Add(TradeData.Herb.Id);
        return (party, healerMember, target);
    }

    [Fact]
    public void HealingWithAHerbAtFullHitPointsIsAlreadyWellAndKeepsTheHerb()
    {
        var (party, healer, target) = Healing(3, damage: 0);

        Assert.Equal(new HealResult(HealOutcome.AlreadyWell), Crafting.HerbHeal(party, healer, target, new ScriptedRandom()));

        Assert.True(party.Inventory.Has(TradeData.Herb.Id));
    }

    [Fact]
    public void HealingWithAHerbWithoutAHerbIsNoHerb()
    {
        var (party, healer, target) = Healing(3, herb: false);

        Assert.Equal(new HealResult(HealOutcome.NoHerb), Crafting.HerbHeal(party, healer, target, new ScriptedRandom()));

        Assert.Equal(30, healer.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void HealingWithAHerbTooTiredToTryFailsAfterTheHerbIsUsed()
    {
        // A Healer of 0 costs 5 Stamina, and this one has 1.
        var (party, healer, target) = Healing(0, stamina: 1);

        Assert.Equal(new HealResult(HealOutcome.Failed), Crafting.HerbHeal(party, healer, target, new ScriptedRandom()));

        Assert.False(party.Inventory.Has(TradeData.Herb.Id));
        Assert.Equal(1, healer.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void HealingWithAHerbPaysStaminaAndHealsFourTimesTheStepWhenTheRollIsBelowTheTotal()
    {
        var (party, healer, target) = Healing(3);
        var before = target.Hp;

        // 18 × 4 + 3 × 11 is 105; 105 - 20 is 85, which is step 5. Healer 3 pays 2 Stamina.
        var result = Crafting.HerbHeal(party, healer, target, new ScriptedRandom(RandomFor.D(100, 20)));

        Assert.Equal(new HealResult(HealOutcome.Healed, 20), result);
        Assert.Equal(before + 20, target.Hp);
        Assert.Equal(28, healer.Stats.Modified(Stat.Stamina));
        Assert.False(party.Inventory.Has(TradeData.Herb.Id));
    }

    [Fact]
    public void HealingWithAHerbFailsWhenTheRollReachesTheTotalButTheHerbAndStaminaAreSpent()
    {
        var (party, healer, target) = Healing(1);
        var before = target.Hp;

        // 18 × 4 + 1 × 11 is 83; Healer 1 pays 4 Stamina.
        var result = Crafting.HerbHeal(party, healer, target, new ScriptedRandom(RandomFor.D(100, 83)));

        Assert.Equal(new HealResult(HealOutcome.Failed), result);
        Assert.Equal(before, target.Hp);
        Assert.Equal(26, healer.Stats.Modified(Stat.Stamina));
        Assert.False(party.Inventory.Has(TradeData.Herb.Id));
    }

    private static Character Weakened(int n, int by)
    {
        var target = TradeData.Member(n);
        target.Stats.ChangeCurrent(Stat.Strength, -by);
        Assert.True(target.Stats.IsHurt(Stat.Strength));
        return target;
    }

    private static (CharacterParty Party, Character Healer) Restorer(int healer, int stamina = 30, bool herb = true, params Character[] others)
    {
        var healerMember = TradeData.Member(0, new SkillLevels(Healer: healer), stamina: stamina);
        var party = TradeData.PartyOf(TradeData.Catalog(), null, [healerMember, .. others]);
        if (herb)
            party.Inventory.Add(TradeData.Herb.Id);
        return (party, healerMember);
    }

    [Fact]
    public void RestoringAStatThatIsntHurtIsAlreadyWellAndKeepsTheHerb()
    {
        var target = TradeData.Member(1);
        var (party, healer) = Restorer(4, others: target);

        Assert.Equal(new HealResult(HealOutcome.AlreadyWell), Crafting.Restore(party, healer, target, Stat.Strength, new ScriptedRandom()));

        Assert.True(party.Inventory.Has(TradeData.Herb.Id));
    }

    [Fact]
    public void RestoringWithNoHerbIsNoHerb()
    {
        var target = Weakened(1, 5);
        var (party, healer) = Restorer(4, herb: false, others: target);

        Assert.Equal(new HealResult(HealOutcome.NoHerb), Crafting.Restore(party, healer, target, Stat.Strength, new ScriptedRandom()));

        Assert.Equal(10, target.Stats.Modified(Stat.Strength));
    }

    [Fact]
    public void RestoringWithTooLittleStaminaFailsAfterTheHerbIsUsed()
    {
        var target = Weakened(1, 5);
        // A Healer of 4 costs 11 Stamina.
        var (party, healer) = Restorer(4, stamina: 10, others: target);

        Assert.Equal(new HealResult(HealOutcome.Failed), Crafting.Restore(party, healer, target, Stat.Strength, new ScriptedRandom()));

        Assert.False(party.Inventory.Has(TradeData.Herb.Id));
        Assert.Equal(10, healer.Stats.Modified(Stat.Stamina));
        Assert.Equal(10, target.Stats.Modified(Stat.Strength));
    }

    [Fact]
    public void RestoringPaysStaminaAndRestoresTwiceTheStepUpToTheBaseValue()
    {
        var target = Weakened(1, 5);
        var (party, healer) = Restorer(4, others: target);

        // 18 × 2 + 4 × 5 is 56; 56 - 10 is 46, which is step 3, so 6, but only 5 is missing.
        var result = Crafting.Restore(party, healer, target, Stat.Strength, new ScriptedRandom(RandomFor.D(100, 10)));

        Assert.Equal(new HealResult(HealOutcome.Healed, 5), result);
        Assert.Equal(15, target.Stats.Modified(Stat.Strength));
        Assert.False(target.Stats.IsHurt(Stat.Strength));
        Assert.Equal(30 - 11, healer.Stats.Modified(Stat.Stamina));
        Assert.False(party.Inventory.Has(TradeData.Herb.Id));
    }

    [Fact]
    public void RestoringLessThanWhatIsMissingLeavesTheStatHurt()
    {
        var target = Weakened(1, 8);
        var (party, healer) = Restorer(4, others: target);

        var result = Crafting.Restore(party, healer, target, Stat.Strength, new ScriptedRandom(RandomFor.D(100, 10)));

        Assert.Equal(new HealResult(HealOutcome.Healed, 6), result);
        Assert.Equal(13, target.Stats.Modified(Stat.Strength));
        Assert.True(target.Stats.IsHurt(Stat.Strength));
    }

    [Fact]
    public void RestoringFailsWhenTheRollReachesTheTotalButTheHerbAndStaminaAreSpent()
    {
        var target = Weakened(1, 5);
        var (party, healer) = Restorer(4, others: target);

        var result = Crafting.Restore(party, healer, target, Stat.Strength, new ScriptedRandom(RandomFor.D(100, 56)));

        Assert.Equal(new HealResult(HealOutcome.Failed), result);
        Assert.Equal(10, target.Stats.Modified(Stat.Strength));
        Assert.Equal(19, healer.Stats.Modified(Stat.Stamina));
        Assert.False(party.Inventory.Has(TradeData.Herb.Id));
    }

    [Fact]
    public void AMasterHealerRestoresForNoStamina()
    {
        var target = Weakened(1, 14);
        var (party, healer) = Restorer(15, others: target);

        // 18 × 2 + 15 × 5 is 111; 111 - 10 is 101, which is step 6, so 12.
        var result = Crafting.Restore(party, healer, target, Stat.Strength, new ScriptedRandom(RandomFor.D(100, 10)));

        Assert.Equal(new HealResult(HealOutcome.Healed, 12), result);
        Assert.Equal(13, target.Stats.Modified(Stat.Strength));
        Assert.Equal(30, healer.Stats.Modified(Stat.Stamina));
    }
}
