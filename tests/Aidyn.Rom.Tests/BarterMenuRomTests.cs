using System.Buffers.Binary;
using Aidyn.Core;
using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The loot window against NA 1.0's imported data: the common strings it reads, the titles it draws, and
/// opening the chest a new game starts beside, with 4 Healing Potions.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class BarterMenuRomTests(Na10Import fixture)
{
    private static readonly RecordId Overworld = RecordId.Parse("aidyn:mapset/0");

    private PluginSet Set => fixture.Data.Set;

    /// <summary>A line as the user reads it: without the button glyphs the font draws as C-down, and trimmed.</summary>
    [Fact]
    public void TheBarterLinesOfTheCommonStringsReadAsTheWindowNeedsThem()
    {
        var text = MenuText.From(Set);
        var wrong = new List<string>();
        void Is(int line, string expected)
        {
            if (TestRoms.Plain(text[line]) != expected)
                wrong.Add($"{line}: [{TestRoms.Plain(text[line])}]");
        }
        void Starts(int line, string expected)
        {
            if (!TestRoms.Plain(text[line]).StartsWith(expected, StringComparison.Ordinal))
                wrong.Add($"{line}: [{TestRoms.Plain(text[line])}]");
        }

        Is(MenuText.DropConfirm, "Are you sure you wish to drop %s?");
        Is(MenuText.DropNo, "Oops!  No way.");
        Is(MenuText.DropYes, "Drop it.");
        Is(MenuText.DropSome, "Drop %d of them.");
        Is(MenuText.DropAll, "Drop all of them.");
        Starts(MenuText.LeaveItems, "There are still items that you have not picked up!");
        Is(MenuText.LeaveItemsNo, "No, I changed my mind.");
        Is(MenuText.LeaveItemsYes, "Yes, leave them.");
        Starts(MenuText.FirstPotion, "You've acquired a potion!");
        Is(MenuText.NothingThere, "Nothing there.");
        Is(MenuText.StackFull, "You cannot hold any more of that item.");
        Is(MenuText.InventoryFull, "The party's inventory is full.");
        Is(MenuText.CantDrop, "You cannot drop that item.");
        Is(MenuText.UnequipToDrop, "You must unequip that to drop it.");
        Is(MenuText.FoundGold, "Found: %lu gold.");
        Is(MenuText.MustTake, "You have a feeling that there's something you really need in here.");

        Assert.Empty(wrong);
    }

    [Fact]
    public void TheSellingCounterpartsSitBesideTheDroppingLines()
    {
        var text = MenuText.From(Set);

        Assert.Equal("I'll give you %ld gold for that %s.", TestRoms.Plain(text[MenuText.SellConfirm]));
        Assert.Equal("No, thanks.", TestRoms.Plain(text[MenuText.SellNo]));
        Assert.Equal("Sure, here it is.", TestRoms.Plain(text[MenuText.SellYes]));
        Assert.Equal("I will sell %d of them.", TestRoms.Plain(text[MenuText.SellSome]));
        Assert.Equal("I will sell all of them.", TestRoms.Plain(text[MenuText.SellAll]));
        Assert.Equal("You cannot afford this item.", TestRoms.Plain(text[MenuText.CantAfford]));
        Assert.Equal("I do not want that item.", TestRoms.Plain(text[MenuText.MerchantRefuses]));
        Assert.Equal("You must unequip that to sell it.", TestRoms.Plain(text[MenuText.UnequipToSell]));
        Assert.Equal("Value: %lu", TestRoms.Plain(text[MenuText.Value]));
    }

    [Fact]
    public void TheLeaveItemsQuestionAsksWhetherToExit()
    {
        Assert.Equal("There are still items that you have not picked up!  Are you sure you wish to exit?", TestRoms.Plain(MenuText.From(Set)[MenuText.LeaveItems]));
    }

    [Fact]
    public void TheWindowsTitlesAreImportedAsPngs()
    {
        var sizes = new[] { MenuImages.TreasureTitle, MenuImages.BuyTitle, MenuImages.SellTitle }.Select(id =>
        {
            var path = Set.FindAsset(id, "png");
            Assert.NotNull(path);
            var png = File.ReadAllBytes(path);
            return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        });

        Assert.Equal([(84, 17), (33, 17), (39, 17)], sizes);
    }

    /// <summary>The chest 16 units from where a new game puts the player in Overworld I08, whose loot table is the Minor healthpak: 1 to 4 Healing Potions.</summary>
    private (GameState Game, ItemPile Loot, int Gold) OpenTheStartChest()
    {
        var data = fixture.Data;
        var map = Set.Get<MapRecord>(data.Chunk(new ChunkRef(Overworld, 9, 8))!.Map);
        var chest = map.Objects.OfType<ContainerObject>().Single(c => c.Index == 111);
        Assert.Equal(RecordId.Parse("aidyn:loot/0x0839"), chest.Loot);
        var game = GameState.NewGame(Set);
        var host = new LootHost();
        var use = new Aidyn.Core.MapObjects.MapObjectUse(game, MenuText.From(Set), host);
        var loaded = Aidyn.Core.MapObjects.LoadedMap.Activate(map, map.Id, game.Flags, game.Memory, game.Party, new Lcg());

        use.Use(loaded, chest);

        var (loot, gold) = Assert.Single(host.Loots);
        return (game, loot, gold);
    }

    private sealed class LootHost : Aidyn.Core.MapObjects.IMapObjectHost
    {
        public List<(ItemPile Loot, int Gold)> Loots { get; } = [];

        public void Teleport(Aidyn.Core.MapObjects.LoadedMap from, TeleporterObject teleporter) { }

        public void OpenLoot(ItemPile loot, int gold) => Loots.Add((loot, gold));

        public void StartScript(Aidyn.Core.MapObjects.LoadedMap map, DialogueObject dialogue) { }

        public void Animate(Aidyn.Core.MapObjects.LoadedMap map, SceneObject actor, int request) { }

        public void Changed(Aidyn.Core.MapObjects.LoadedMap map, SceneObject scene) { }

        public void Sound(Aidyn.Core.Audio.OneShot sound) { }
    }

    [Fact]
    public void ANewGamesStartChestOpensALootWindowOfFourHealingPotions()
    {
        var (game, loot, gold) = OpenTheStartChest();
        Assert.False(game.Flags.Get(game.Items.Rules.FirstPotionFlag));

        var menu = BarterMenu.ForLoot(Set, game.Party, loot, gold, game.Flags);

        var row = Assert.Single(menu.Pile.Rows);
        Assert.Equal((RecordId.Hex(RecordTypes.Item, 0x1004), 4), (row.Item.Id, row.Quantity));
        Assert.Equal(ItemKind.Potion, row.Item.Kind);
        Assert.True(menu.OnPile);
        Assert.Equal(MenuImages.TreasureTitle, menu.Title);
        Assert.Equal(0, gold);
        Assert.Equal($"Found: {gold} gold.", menu.PileLine);
        Assert.Equal(GameState.NewGameGold, game.Party.Gold);
    }

    [Fact]
    public void TakingTheStartChestsPotionsGivesHelpEmptiesThePileAndTheBackButtonThenClosesTheWindow()
    {
        var (game, loot, gold) = OpenTheStartChest();
        var menu = BarterMenu.ForLoot(Set, game.Party, loot, gold, game.Flags);
        var healing = RecordId.Hex(RecordTypes.Item, 0x1004);

        menu.Accept();

        Assert.Equal(4, game.Party.Inventory!.Count(healing));
        Assert.Empty(menu.Pile.Rows);
        Assert.False(menu.OnPile);
        Assert.Equal(MenuText.From(Set)[MenuText.FirstPotion], menu.Help);
        Assert.StartsWith("You've acquired a potion!", TestRoms.Plain(menu.Help!), StringComparison.Ordinal);
        Assert.True(game.Flags.Get(game.Items.Rules.FirstPotionFlag));

        // The help takes B first, then B closes the window as nothing is left.
        Assert.True(menu.Back());
        Assert.Null(menu.Help);
        Assert.False(menu.Back());
        Assert.False(menu.Closed);
    }
}
