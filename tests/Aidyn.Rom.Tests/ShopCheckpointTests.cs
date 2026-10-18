using Aidyn.Core;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// A shop's window against NA 1.0's imported data: the common strings it reads, and the prices the
/// user's captures of STAI01's shop show (Oct 3, 2026), from the "Before final encounter" checkpoint.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class ShopCheckpointTests(Na10Import fixture)
{
    private const string Checkpoint = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - Before final encounter.bs1";

    private PluginSet Set => fixture.Data.Set;

    [Fact]
    public void TheShopsLinesOfTheCommonStringsReadAsTheWindowNeedsThem()
    {
        var text = MenuText.From(Set);

        Assert.Equal(
            [
                "I'll give you %ld gold for that %s.", "No, thanks.", "Sure, here it is.", "I will sell %d of them.", "I will sell all of them.",
                "You cannot afford this item.", "I do not want that item.", "You must unequip that to sell it.", "Value: %lu", "This is highway robbery!",
            ],
            new[]
            {
                MenuText.SellConfirm, MenuText.SellNo, MenuText.SellYes, MenuText.SellSome, MenuText.SellAll, MenuText.CantAfford,
                MenuText.MerchantRefuses, MenuText.UnequipToSell, MenuText.Value, MenuText.Appraisal,
            }.Select(line => TestRoms.Plain(text[line])));
    }

    [Fact]
    public void STAI01sShopShowsTheCapturedPricesToTheCheckpointsParty()
    {
        var game = GameState.Load(Set, CheckpointSave.Convert(TestRoms.Checkpoint(Checkpoint), Set, CheckpointSave.Label(Checkpoint)));
        var record = Set.Get<ShopRecord>(RecordId.Decimal(RecordTypes.Shop, 30));
        Assert.Equal("STAI01", Set.Get<EntityRecord>(record.Shopkeeper).Name);

        var shop = Shop.Open(record, Set.Get<EntityRecord>(record.Shopkeeper), game.Party, game.Party.Inventory!.Catalog);
        var menu = BarterMenu.ForShop(Set, shop);
        string ValueOf(string name)
        {
            menu.Point(pile: true, menu.Pile.Rows.ToList().FindIndex(r => r.Item.Name == name));
            return menu.PileLine;
        }

        // Rheda's Merchant 1 against the keeper's 3.
        Assert.Equal(0.1f, shop.Barter, 1e-6f);
        Assert.Equal(20, menu.Pile.Rows.Count);
        Assert.Equal("Value: 55", ValueOf("Healing Potion"));
        Assert.Equal("Value: 330", ValueOf("Spear"));
        Assert.Equal("Value: 11", ValueOf("Herb"));
    }
}
