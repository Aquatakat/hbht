using HBHT.Core.Characters;
using CharacterParty = HBHT.Core.Characters.Party;
using HBHT.Core.Flags;
using HBHT.Core.Items;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>
/// A shop's window: the barter state with a shopkeeper (<c>WidgetBarter</c> with <c>Shopkeep</c> set),
/// checked against 1.0's code. A Merchant of 3 against a party with none gives a factor of 0.75, so a
/// price of 100 buys for 175 and sells for 12.
/// </summary>
public class ShopWindowTests(BarterPlugins fixture) : IClassFixture<BarterPlugins>
{
    private readonly PluginSet _plugins = fixture.Plugins;

    private static string Line(int line) => $"L{line}";

    private (BarterMenu Menu, CharacterParty Party, FlagNetwork Flags) Open(Action<CharacterParty>? stock = null, uint gold = 1000,
        int generalSlots = 8, ItemRecord[]? more = null)
    {
        var catalog = TradeData.Catalog(generalSlots, more ?? []);
        var flags = ItemData.Flags();
        var party = TradeData.PartyOf(catalog, flags, TradeData.Member());
        party.Gold = gold;
        stock?.Invoke(party);
        var keeper = TradeData.Keeper(3);
        var shop = Shop.Open(TradeData.ShopOf(keeper, new ShopStock(TradeData.Trinket.Id), new ShopStock(TradeData.Potion.Id)), keeper, party,
            catalog, flags);
        return (BarterMenu.ForShop(_plugins, shop), party, flags);
    }

    private static int StockRow(BarterMenu menu, RecordId item) => menu.Pile.Rows.ToList().FindIndex(r => r.Item.Id == item);

    private static int PartyRow(BarterMenu menu, RecordId item) =>
        menu.Party.Inventory.Rows.Select((r, i) => (r, i)).First(e => e.r.Wearer is null && e.r.Item.Id == item).i;

    [Fact]
    public void TheStockIsTitledBuyBesideTheSellListAndHasNoCounts()
    {
        var (menu, _, _) = Open();

        Assert.Equal(MenuImages.BuyTitle, menu.Title);
        Assert.Equal(MenuImages.SellTitle, menu.PartyTitle);
        Assert.Equal([TradeData.Potion.Id, TradeData.Trinket.Id], menu.Pile.Rows.Select(r => r.Item.Id));
        Assert.All(menu.Pile.Rows, r => Assert.Equal(0, r.Quantity));
        Assert.True(menu.OnPile);
    }

    [Fact]
    public void TheLineUnderTheStockIsTheActiveRowsPriceToBuyOrSell()
    {
        var (menu, party, _) = Open(stock: p => p.Inventory.Add(TradeData.Trinket.Id));
        menu.Point(pile: true, StockRow(menu, TradeData.Trinket.Id));

        Assert.Equal("L531 175", menu.PileLine);

        menu.Point(pile: false, PartyRow(menu, TradeData.Trinket.Id));
        Assert.Equal("L531 12", menu.PileLine);

        party.Inventory.Take(TradeData.Trinket.Id, 1);
        menu.Party.Refresh();
        Assert.Equal(" ", menu.PileLine);
    }

    [Fact]
    public void AOnTheStockBuysOneAtOnceAndTheStockStaysAsItWas()
    {
        var (menu, party, _) = Open();
        var row = StockRow(menu, TradeData.Trinket.Id);
        menu.Point(pile: true, row);

        menu.Accept();
        menu.Accept();

        Assert.Null(menu.Message);
        Assert.Null(menu.Confirm);
        Assert.Equal(2, party.Inventory.Count(TradeData.Trinket.Id));
        Assert.Equal(1000u - 2 * 175, party.Gold);
        Assert.Equal(2, menu.Pile.Rows.Count);
        Assert.Equal(row, menu.Pile.Cursor);
        Assert.True(menu.OnPile);
        Assert.Equal(PartyRow(menu, TradeData.Trinket.Id), menu.Party.Inventory.Cursor);
    }

    [Fact]
    public void BuyingIsRefusedForGoldThenTheStackThenTheInventory()
    {
        var (poor, _, _) = Open(gold: 174);
        poor.Point(pile: true, StockRow(poor, TradeData.Trinket.Id));
        poor.Accept();
        Assert.Equal(Line(MenuText.CantAfford), poor.Message?.Text);

        var (full, _, _) = Open(stock: p => p.Inventory.Add(TradeData.Trinket.Id, 99), gold: 100_000);
        full.Point(pile: true, StockRow(full, TradeData.Trinket.Id));
        full.Accept();
        Assert.Equal(Line(MenuText.StackFull), full.Message?.Text);

        var (packed, _, _) = Open(stock: p => p.Inventory.Add(TradeData.Cheap.Id), generalSlots: 1);
        packed.Point(pile: true, StockRow(packed, TradeData.Trinket.Id));
        packed.Accept();
        Assert.Equal(Line(MenuText.InventoryFull), packed.Message?.Text);
    }

    [Fact]
    public void TheFirstPotionBoughtShowsTheHelp()
    {
        var (menu, _, flags) = Open();
        menu.Point(pile: true, StockRow(menu, TradeData.Potion.Id));

        menu.Accept();

        Assert.Equal(Line(MenuText.FirstPotion), menu.Help);
        Assert.True(flags.Get(TradeData.PotionFlag));
        menu.Accept();
        Assert.Null(menu.Help);
        menu.Accept();
        Assert.Null(menu.Help);
    }

    [Fact]
    public void TheShopkeeperWontBuyWhatSellsFor1GoldOrLessAndAWornItemMustComeOffFirst()
    {
        var ring = TradeData.Priced(30, 100, ItemKind.Ring);
        var (menu, party, _) = Open(stock: p =>
        {
            p.Inventory.Add(TradeData.Cheap.Id);
            p.Inventory.Add(ring.Id);
            Assert.Equal(EquipRefusal.None, p.Equip(p.Slots[0]!, ring.Id));
        }, more: [ring]);

        menu.Point(pile: false, PartyRow(menu, TradeData.Cheap.Id));
        menu.Accept();
        Assert.Equal(Line(MenuText.MerchantRefuses), menu.Message?.Text);

        menu.Accept();
        menu.Point(pile: false, menu.Party.Inventory.Rows.ToList().FindIndex(r => r.Wearer is not null && r.Item.Id == ring.Id));
        menu.Accept();
        Assert.Equal(Line(MenuText.UnequipToSell), menu.Message?.Text);
        Assert.Null(menu.Confirm);
        Assert.Equal(1000u, party.Gold);
    }

    [Fact]
    public void SellingAsksHowManyAtThePriceAndPaysForThem()
    {
        var (menu, party, _) = Open(stock: p => p.Inventory.Add(TradeData.Trinket.Id, 7));
        var row = PartyRow(menu, TradeData.Trinket.Id);
        menu.Point(pile: false, row);

        menu.Accept();

        var confirm = Assert.IsType<BarterConfirm>(menu.Confirm);
        Assert.Equal($"L471 12 {TradeData.Trinket.Name}", confirm.Text);
        Assert.Equal(12, confirm.Price);
        Assert.Equal([Line(MenuText.SellNo), "L477 1", "L477 5", Line(MenuText.SellAll)], confirm.Options.Rows.Select(o => o.Text));
        Assert.True(menu.Back());
        Assert.NotNull(menu.Confirm);

        menu.MoveCursor(2);
        menu.Accept();

        Assert.Null(menu.Confirm);
        Assert.Equal(2, party.Inventory.Count(TradeData.Trinket.Id));
        Assert.Equal(1000u + 5 * 12, party.Gold);
        Assert.Equal(row, menu.Party.Inventory.Cursor);
        Assert.Equal(2, menu.Pile.Rows.Count);
    }

    [Fact]
    public void SellingTheLastOneOffersOnlyNoOrYes()
    {
        var (menu, _, _) = Open(stock: p => p.Inventory.Add(TradeData.Trinket.Id));
        menu.Point(pile: false, PartyRow(menu, TradeData.Trinket.Id));

        menu.Accept();

        Assert.Equal([Line(MenuText.SellNo), Line(MenuText.SellYes)], menu.Confirm!.Options.Rows.Select(o => o.Text));
    }

    [Fact]
    public void BClosesTheShopAtOnce()
    {
        var (menu, _, _) = Open();

        Assert.False(menu.Back());
        Assert.Null(menu.Choice);
        Assert.Null(menu.Message);
    }

    [Fact]
    public void CUpAppraisesTheStocksRowAndClosesAMessage()
    {
        var (menu, _, _) = Open(stock: p => p.Inventory.Add(TradeData.Trinket.Id));
        var trinket = StockRow(menu, TradeData.Trinket.Id);
        menu.Point(pile: true, trinket);

        menu.Appraise();

        Assert.Equal(Line(MenuText.Appraisal + menu.Shop!.Appraise(menu.Pile.Rows[trinket].Instance)), menu.Message?.Text);
        menu.Appraise();
        Assert.Null(menu.Message);

        menu.Point(pile: false, PartyRow(menu, TradeData.Trinket.Id));
        menu.Appraise();
        Assert.Null(menu.Message);
    }

    [Fact]
    public void CUpDoesNothingInLoot()
    {
        var catalog = TradeData.Catalog();
        var party = TradeData.PartyOf(catalog, null, TradeData.Member());
        var loot = new ItemPile(catalog);
        loot.Add(TradeData.Trinket.Id);
        var menu = BarterMenu.ForLoot(_plugins, party, loot, 0, null);

        menu.Appraise();

        Assert.Null(menu.Message);
    }
}
