using System.Numerics;
using System.Text.Json;
using Aidyn.Core.Characters;
using CharacterParty = Aidyn.Core.Characters.Party;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>
/// A plugin whose common strings (<c>aidyn:strings/5</c>) read "L" and their line, so a test can say which
/// line a popup showed. The lines the barter window formats keep their conversion after it.
/// </summary>
public sealed class BarterPlugins : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aidyn-barter-" + Guid.NewGuid().ToString("N"));

    public BarterPlugins()
    {
        var dir = Path.Combine(_root, "aidyn");
        Directory.CreateDirectory(Path.Combine(dir, "records"));
        File.WriteAllText(Path.Combine(dir, "plugin.json"), """{ "id": "aidyn", "name": "aidyn", "version": "1.0.0" }""");
        var lines = Enumerable.Range(0, 560).Select(i => i switch
        {
            MenuText.XpCost or MenuText.XpRemainingLine or MenuText.GoldCost or MenuText.SkillNeedsXp or MenuText.TrainSkillForXp
                or MenuText.SpellNeedsXp or MenuText.TrainSpellForXp => $"L{i} %lu",
            MenuText.SkillNeeds or MenuText.TrainSkillFor or MenuText.SpellNeeds or MenuText.TrainSpellFor => $"L{i} %ld %ld",
            MenuText.DropConfirm => $"L{i} %s",
            MenuText.DropSome => $"L{i} %d",
            MenuText.FoundGold => $"L{i} %lu",
            MenuText.Value => $"L{i} %lu",
            MenuText.SellConfirm => $"L{i} %ld %s",
            MenuText.SellSome => $"L{i} %d",
            _ => $"L{i}",
        });
        var strings = new { id = "aidyn:strings/5", name = "Common", lines };
        File.WriteAllText(Path.Combine(dir, "records", "strings.json"), JsonSerializer.Serialize(new[] { strings }));
        Plugins = PluginSet.Load([dir]);
    }

    public PluginSet Plugins { get; }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

public class BarterMenuTests(BarterPlugins fixture) : IClassFixture<BarterPlugins>
{
    private readonly PluginSet _plugins = fixture.Plugins;

    private static string Line(int line) => $"L{line}";

    private (BarterMenu Menu, CharacterParty Party, ItemPile Loot, FlagNetwork Flags) Open(Action<ItemPile>? fill = null,
        Action<CharacterParty>? stock = null, int generalSlots = 8, ItemRecord[]? more = null, int gold = 0)
    {
        var catalog = TradeData.Catalog(generalSlots, more ?? []);
        var flags = ItemData.Flags();
        var party = TradeData.PartyOf(catalog, flags, TradeData.Member());
        stock?.Invoke(party);
        var loot = new ItemPile(catalog);
        fill?.Invoke(loot);
        return (BarterMenu.ForLoot(_plugins, party, loot, gold, flags), party, loot, flags);
    }

    private static int PartyRow(BarterMenu menu, RecordId item) =>
        menu.Party.Inventory.Rows.Select((r, i) => (r, i)).First(e => e.r.Wearer is null && e.r.Item.Id == item).i;

    private static int WornRow(BarterMenu menu, RecordId item) =>
        menu.Party.Inventory.Rows.Select((r, i) => (r, i)).First(e => e.r.Wearer is not null && e.r.Item.Id == item).i;

    /// <summary>
    /// The loot window's details fade as the Party tab's do, and while they show, A takes nothing and B neither asks
    /// nor closes the window.
    /// </summary>
    [Fact]
    public void TheDetailsFadeAndTakeThePressesWhileTheyShow()
    {
        var (menu, _, loot, _) = Open(fill: p => p.Add(TradeData.Leather.Id, 2));

        menu.PressDetails();
        menu.ReleaseDetails();
        var alphas = new List<int>();
        while (menu.DetailsPane.Showing)
        {
            menu.Step();
            alphas.Add(menu.DetailsPane.Alpha);
            if (!menu.DetailsPane.Showing)
                break;
            menu.Accept();
            Assert.True(menu.Back());
            Assert.Null(menu.Choice);
        }

        Assert.Equal([60, 120, 180, 240, 255, 195, 135, 75, 15, 0], alphas);
        Assert.Equal(2, loot.Stacks.Sum(s => s.Quantity));
    }

    [Fact]
    public void ThePilesRowsAreSortedAsTheInventoryIsWithNoWearer()
    {
        var (menu, _, _, _) = Open(fill: p =>
        {
            p.Add(TradeData.Cheap.Id, 2);
            p.Add(TradeData.Potion.Id, 4);
            p.Add(TradeData.Trinket.Id, 7);
        });

        Assert.Equal([TradeData.Potion.Id, TradeData.Trinket.Id, TradeData.Cheap.Id], menu.Pile.Rows.Select(r => r.Item.Id));
        Assert.Equal([4, 7, 2], menu.Pile.Rows.Select(r => r.Quantity));
        Assert.All(menu.Pile.Rows, r => Assert.Null(r.Wearer));
        Assert.True(menu.OnPile);
    }

    [Fact]
    public void AnEmptyPileStartsWithThePartysListActive()
    {
        var (menu, _, _, _) = Open();

        Assert.Empty(menu.Pile.Rows);
        Assert.False(menu.OnPile);
    }

    [Fact]
    public void TakingMovesTheWholeStackToThePartyAndRemovesItsRow()
    {
        var (menu, party, loot, _) = Open(fill: p =>
        {
            p.Add(TradeData.Cheap.Id, 3);
            p.Add(TradeData.Spice.Id, 2);
        });

        menu.Accept();

        Assert.Equal(3, party.Inventory!.Count(TradeData.Cheap.Id));
        Assert.Null(loot.Find(TradeData.Cheap.Id));
        Assert.Equal([TradeData.Spice.Id], menu.Pile.Rows.Select(r => r.Item.Id));
        Assert.Null(menu.Message);
        Assert.True(menu.OnPile);
    }

    [Fact]
    public void ThePilesCursorStaysOnItsIndexAsTheNextRowMovesUpAndClampsAtTheLast()
    {
        var (menu, _, _, _) = Open(fill: p =>
        {
            p.Add(TradeData.Cheap.Id);
            p.Add(TradeData.Spice.Id);
            p.Add(TradeData.Gem.Id);
        });
        menu.Point(pile: true, 1);

        menu.Accept();
        Assert.Equal([TradeData.Cheap.Id, TradeData.Gem.Id], menu.Pile.Rows.Select(r => r.Item.Id));
        Assert.Equal(1, menu.Pile.Cursor);
        Assert.Equal(TradeData.Gem.Id, menu.Pile.Current!.Item.Id);

        menu.Accept();
        Assert.Equal([TradeData.Cheap.Id], menu.Pile.Rows.Select(r => r.Item.Id));
        Assert.Equal(0, menu.Pile.Cursor);
    }

    [Fact]
    public void ThePartysCursorGoesToTheCarriedRowOfWhatWasTaken()
    {
        var (menu, _, _, _) = Open(
            fill: p => p.Add(TradeData.Cheap.Id, 3),
            stock: party =>
            {
                party.Inventory!.Add(TradeData.Trinket.Id);
                party.Inventory.Add(TradeData.Hide.Id);
            });

        menu.Accept();

        var row = menu.Party.Inventory.Current!;
        Assert.Equal((TradeData.Cheap.Id, (int?)null), (row.Item.Id, row.Wearer));
        Assert.Equal(PartyRow(menu, TradeData.Cheap.Id), menu.Party.Inventory.Cursor);
        Assert.Equal(3, row.Quantity);
    }

    [Fact]
    public void TakingWhenThePartysStackCanTakeOnlySomeLeavesTheRestInThePile()
    {
        var (menu, party, loot, _) = Open(
            fill: p => p.Add(TradeData.Trinket.Id, 5),
            stock: party => party.Inventory!.Add(TradeData.Trinket.Id, 97));

        menu.Accept();

        Assert.Equal(99, party.Inventory!.Count(TradeData.Trinket.Id));
        Assert.Equal(3, loot.Find(TradeData.Trinket.Id)?.Quantity);
        Assert.Equal(3, Assert.Single(menu.Pile.Rows).Quantity);
        Assert.Null(menu.Message);
        Assert.True(menu.OnPile);
    }

    [Fact]
    public void TakingWhenThePartyHoldsNinetyNineSaysTheStackIsFullAndChangesNothing()
    {
        var (menu, party, loot, _) = Open(
            fill: p => p.Add(TradeData.Trinket.Id, 5),
            stock: party => party.Inventory!.Add(TradeData.Trinket.Id, 99));

        menu.Accept();

        Assert.Equal(Line(MenuText.StackFull), menu.Message?.Text);
        Assert.Equal(99, party.Inventory!.Count(TradeData.Trinket.Id));
        Assert.Equal(5, loot.Find(TradeData.Trinket.Id)?.Quantity);
        Assert.Equal(5, Assert.Single(menu.Pile.Rows).Quantity);
        Assert.True(menu.OnPile);
    }

    [Fact]
    public void TakingANewStackWithNoGeneralSlotFreeSaysTheInventoryIsFullAndChangesNothing()
    {
        var (menu, party, loot, _) = Open(
            fill: p => p.Add(TradeData.Trinket.Id, 5),
            stock: party => party.Inventory!.Add(TradeData.Cheap.Id),
            generalSlots: 1);

        menu.Accept();

        Assert.Equal(Line(MenuText.InventoryFull), menu.Message?.Text);
        Assert.False(party.Inventory!.Has(TradeData.Trinket.Id));
        Assert.Equal(5, loot.Find(TradeData.Trinket.Id)?.Quantity);
        Assert.Equal(5, Assert.Single(menu.Pile.Rows).Quantity);
        Assert.True(menu.OnPile);
    }

    [Fact]
    public void TakingTheLastStackMakesThePartysListActive()
    {
        var (menu, _, _, _) = Open(fill: p => p.Add(TradeData.Trinket.Id, 2));

        menu.Accept();

        Assert.Empty(menu.Pile.Rows);
        Assert.False(menu.OnPile);
    }

    [Fact]
    public void ShowingThePileDoesNothingWhileThePileIsEmpty()
    {
        var (menu, _, _, _) = Open(fill: p => p.Add(TradeData.Trinket.Id, 2));
        menu.Accept();

        menu.ShowPile();
        Assert.False(menu.OnPile);
        menu.Point(pile: true, 0);
        Assert.False(menu.OnPile);
    }

    [Fact]
    public void ShowingThePileAndThePartySwitchesTheActiveList()
    {
        var (menu, _, _, _) = Open(fill: p => p.Add(TradeData.Trinket.Id));

        menu.ShowParty();
        Assert.False(menu.OnPile);
        menu.ShowPile();
        Assert.True(menu.OnPile);
    }

    [Fact]
    public void TakingTheFirstPotionWithTheFlagClearShowsHelpAndSetsTheFlagButASecondDoesnt()
    {
        var second = TradeData.Priced(30, 40, ItemKind.Potion);
        var (menu, _, _, flags) = Open(fill: p =>
        {
            p.Add(TradeData.Potion.Id);
            p.Add(second.Id);
        }, more: [second]);
        Assert.False(flags.Get(TradeData.PotionFlag));

        menu.Accept();

        Assert.Equal(Line(MenuText.FirstPotion), menu.Help);
        Assert.True(flags.Get(TradeData.PotionFlag));

        menu.Accept();
        Assert.Null(menu.Help);
        menu.Accept();

        Assert.Null(menu.Help);
        Assert.Empty(menu.Pile.Rows);
    }

    [Fact]
    public void TakingSomethingThatIsNotAPotionShowsNoHelpAndLeavesTheFlagClear()
    {
        var (menu, _, _, flags) = Open(fill: p => p.Add(TradeData.Trinket.Id));

        menu.Accept();

        Assert.Null(menu.Help);
        Assert.False(flags.Get(TradeData.PotionFlag));
    }

    [Fact]
    public void ARefusedPotionShowsNoHelp()
    {
        var (menu, _, _, flags) = Open(
            fill: p => p.Add(TradeData.Potion.Id),
            stock: party => party.Inventory!.Add(TradeData.Potion.Id, 99));

        menu.Accept();

        Assert.Null(menu.Help);
        Assert.False(flags.Get(TradeData.PotionFlag));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ACarriedItemWorthThreeGoldOrLessCantBeDropped(int price)
    {
        var cheap = TradeData.Priced(30, price);
        var (menu, _, _, _) = Open(stock: party => party.Inventory!.Add(cheap.Id), more: [cheap]);
        menu.Point(pile: false, PartyRow(menu, cheap.Id));

        menu.Accept();

        Assert.Equal(Line(MenuText.CantDrop), menu.Message?.Text);
        Assert.Null(menu.Confirm);
    }

    [Fact]
    public void AnItemWorthFourGoldCanBeDropped()
    {
        var four = TradeData.Priced(30, 4);
        var (menu, _, _, _) = Open(stock: party => party.Inventory!.Add(four.Id), more: [four]);
        menu.Point(pile: false, PartyRow(menu, four.Id));

        menu.Accept();

        Assert.Null(menu.Message);
        Assert.NotNull(menu.Confirm);
    }

    [Fact]
    public void ACheapWornItemCantBeDroppedAndTheMessageIsTheCantDropNotTheUnequip()
    {
        var ring = TradeData.Priced(30, 3, ItemKind.Ring);
        var (menu, _, _, _) = Open(stock: party =>
        {
            party.Inventory!.Add(ring.Id);
            Assert.Equal(EquipRefusal.None, party.Equip(party.Slots[0]!, ring.Id));
        }, more: [ring]);
        menu.Point(pile: false, WornRow(menu, ring.Id));

        menu.Accept();

        Assert.Equal(Line(MenuText.CantDrop), menu.Message?.Text);
        Assert.Null(menu.Confirm);
    }

    [Fact]
    public void AWornItemWorthMoreMustBeUnequippedToBeDropped()
    {
        var ring = TradeData.Priced(30, 100, ItemKind.Ring);
        var (menu, party, _, _) = Open(stock: party =>
        {
            party.Inventory!.Add(ring.Id);
            Assert.Equal(EquipRefusal.None, party.Equip(party.Slots[0]!, ring.Id));
        }, more: [ring]);
        menu.Point(pile: false, WornRow(menu, ring.Id));

        menu.Accept();

        Assert.Equal(Line(MenuText.UnequipToDrop), menu.Message?.Text);
        Assert.Null(menu.Confirm);
        Assert.Contains(party.Slots[0]!.Equipment.All, i => i.Id == ring.Id);
    }

    [Theory]
    [InlineData(1, new[] { 0, 1 })]
    [InlineData(7, new[] { 0, 1, 5, 7 })]
    [InlineData(25, new[] { 0, 1, 5, 10, 20, 25 })]
    [InlineData(20, new[] { 0, 1, 5, 10, 20 })]
    public void DroppingACarriedItemAsksHowManyFromNoneToAll(int held, int[] options)
    {
        var (menu, _, _, _) = Open(stock: party => party.Inventory!.Add(TradeData.Trinket.Id, held));
        menu.Point(pile: false, PartyRow(menu, TradeData.Trinket.Id));

        menu.Accept();

        var confirm = menu.Confirm!;
        Assert.Equal(options, confirm.Options.Rows.Select(o => o.Quantity));
        Assert.Equal(TradeData.Trinket.Id, confirm.Item);
        Assert.Equal(0, confirm.Price);
        Assert.Equal(0, confirm.Options.Cursor);
        Assert.Null(menu.Message);
    }

    [Fact]
    public void TheDropQuestionNamesTheItemAndItsOptionsReadNoneOneSomeAndAll()
    {
        var (menu, _, _, _) = Open(stock: party => party.Inventory!.Add(TradeData.Trinket.Id, 7));
        menu.Point(pile: false, PartyRow(menu, TradeData.Trinket.Id));

        menu.Accept();

        Assert.Equal($"{Line(MenuText.DropConfirm)} {TradeData.Trinket.Name}", menu.Confirm!.Text);
        Assert.Equal([Line(MenuText.DropNo), $"{Line(MenuText.DropSome)} 1", $"{Line(MenuText.DropSome)} 5", Line(MenuText.DropAll)],
            menu.Confirm.Options.Rows.Select(o => o.Text));
    }

    [Fact]
    public void ASingleItemsOptionsReadNoneAndDropIt()
    {
        var (menu, _, _, _) = Open(stock: party => party.Inventory!.Add(TradeData.Trinket.Id));
        menu.Point(pile: false, PartyRow(menu, TradeData.Trinket.Id));

        menu.Accept();

        Assert.Equal([Line(MenuText.DropNo), Line(MenuText.DropYes)], menu.Confirm!.Options.Rows.Select(o => o.Text));
    }

    [Fact]
    public void AnsweringNoneClosesTheQuestionAndDropsNothing()
    {
        var (menu, party, _, _) = Open(stock: party => party.Inventory!.Add(TradeData.Trinket.Id, 7));
        menu.Point(pile: false, PartyRow(menu, TradeData.Trinket.Id));
        menu.Accept();

        menu.Accept();

        Assert.Null(menu.Confirm);
        Assert.Equal(7, party.Inventory!.Count(TradeData.Trinket.Id));
    }

    [Fact]
    public void AnsweringAQuantityLosesThatManyAndTheCursorStaysOnItsIndex()
    {
        var (menu, party, loot, _) = Open(
            fill: p => p.Add(TradeData.Spice.Id),
            stock: party =>
            {
                party.Inventory!.Add(TradeData.Trinket.Id, 7);
                party.Inventory.Add(TradeData.Hide.Id);
            });
        menu.ShowParty();
        var row = PartyRow(menu, TradeData.Trinket.Id);
        menu.Point(pile: false, row);
        menu.Accept();
        menu.MoveCursor(2);

        menu.Accept();

        Assert.Null(menu.Confirm);
        Assert.Equal(2, party.Inventory!.Count(TradeData.Trinket.Id));
        Assert.Null(loot.Find(TradeData.Trinket.Id));
        Assert.Equal([TradeData.Spice.Id], menu.Pile.Rows.Select(r => r.Item.Id));
        Assert.Equal(row, menu.Party.Inventory.Cursor);
        Assert.Equal(TradeData.Trinket.Id, menu.Party.Inventory.Current!.Item.Id);
        Assert.Equal(2, menu.Party.Inventory.Current.Quantity);
    }

    [Fact]
    public void DroppingAllRemovesTheRowAndTheCursorStaysOnItsIndexOnTheNext()
    {
        var (menu, party, _, _) = Open(stock: party =>
        {
            party.Inventory!.Add(TradeData.Trinket.Id, 7);
            party.Inventory.Add(TradeData.Hide.Id);
        });
        var row = PartyRow(menu, TradeData.Trinket.Id);
        menu.Point(pile: false, row);
        menu.Accept();
        menu.MoveCursor(3);

        menu.Accept();

        Assert.False(party.Inventory!.Has(TradeData.Trinket.Id));
        Assert.Equal(row, menu.Party.Inventory.Cursor);
        Assert.Equal(TradeData.Hide.Id, menu.Party.Inventory.Current!.Item.Id);
    }

    [Fact]
    public void BackWhileTheDropQuestionIsOpenDoesNothingAndTrue()
    {
        var (menu, party, _, _) = Open(stock: party => party.Inventory!.Add(TradeData.Trinket.Id, 7));
        menu.Point(pile: false, PartyRow(menu, TradeData.Trinket.Id));
        menu.Accept();

        Assert.True(menu.Back());

        Assert.NotNull(menu.Confirm);
        Assert.Equal(7, party.Inventory!.Count(TradeData.Trinket.Id));
        Assert.False(menu.Closed);
    }

    [Fact]
    public void MovingTheCursorWhileTheDropQuestionIsOpenMovesItsOptionsNotTheLists()
    {
        var (menu, _, _, _) = Open(
            fill: p =>
            {
                p.Add(TradeData.Cheap.Id);
                p.Add(TradeData.Spice.Id);
            },
            stock: party =>
            {
                party.Inventory!.Add(TradeData.Trinket.Id, 7);
                party.Inventory.Add(TradeData.Hide.Id);
            });
        menu.Point(pile: false, PartyRow(menu, TradeData.Trinket.Id));
        menu.Accept();
        var (partyCursor, pileCursor) = (menu.Party.Inventory.Cursor, menu.Pile.Cursor);

        menu.MoveCursor(1);

        Assert.Equal(1, menu.Confirm!.Options.Cursor);
        Assert.Equal((partyCursor, pileCursor), (menu.Party.Inventory.Cursor, menu.Pile.Cursor));
    }

    [Fact]
    public void BackWithTheLootEmptyReturnsFalseSoTheWindowCloses()
    {
        var (menu, _, _, _) = Open();

        Assert.False(menu.Back());
        Assert.False(menu.Closed);
    }

    [Fact]
    public void BackWithItemsLeftAsksWhetherToLeaveThemAndYesCloses()
    {
        var (menu, _, _, _) = Open(fill: p => p.Add(TradeData.Trinket.Id));

        Assert.True(menu.Back());

        var choice = menu.Choice!;
        Assert.Equal(2, choice.Choices.Rows.Count);
        Assert.Equal(Line(MenuText.LeaveItems), choice.Text);
        Assert.Equal([Line(MenuText.LeaveItemsYes), Line(MenuText.LeaveItemsNo)], choice.Choices.Rows);
        Assert.Equal(0, choice.Choices.Cursor);
        Assert.False(menu.Closed);

        menu.Accept();

        Assert.True(menu.Closed);
        Assert.Null(menu.Choice);
    }

    [Fact]
    public void ChoosingNotToLeaveTheItemsClosesTheChoiceAndLeavesTheWindowOpen()
    {
        var (menu, _, loot, _) = Open(fill: p => p.Add(TradeData.Trinket.Id));
        menu.Back();
        menu.MoveCursor(1);

        menu.Accept();

        Assert.Null(menu.Choice);
        Assert.False(menu.Closed);
        Assert.NotNull(loot.Find(TradeData.Trinket.Id));
    }

    [Fact]
    public void BackWhileTheLeaveChoiceIsOpenDoesNothingAndTrue()
    {
        var (menu, _, _, _) = Open(fill: p => p.Add(TradeData.Trinket.Id));
        menu.Back();

        Assert.True(menu.Back());

        Assert.NotNull(menu.Choice);
        Assert.False(menu.Closed);
    }

    [Fact]
    public void BackWithAMustTakeItemLeftSaysSoInASizedBoxAndTrue()
    {
        var (menu, _, _, _) = Open(fill: p =>
        {
            p.Add(TradeData.Trinket.Id);
            p.Add(TradeData.Quest.Id);
        });

        Assert.True(menu.Back());

        var message = menu.Message!;
        Assert.Equal(Line(MenuText.MustTake), message.Text);
        Assert.Equal((155, 40), message.Box);
        Assert.Null(menu.Choice);
        Assert.False(menu.Closed);
    }

    [Fact]
    public void AMessageClosesOnAOnBOrOnCloseMessage()
    {
        var (menu, _, _, _) = Open(fill: p => p.Add(TradeData.Quest.Id));

        menu.Back();
        Assert.NotNull(menu.Message);
        menu.Accept();
        Assert.Null(menu.Message);

        menu.Back();
        Assert.True(menu.Back());
        Assert.Null(menu.Message);

        menu.Back();
        Assert.True(menu.CloseMessage());
        Assert.Null(menu.Message);
        Assert.False(menu.CloseMessage());
    }

    [Fact]
    public void TheHelpClosesOnAOrB()
    {
        var (menu, _, _, _) = Open(fill: p => p.Add(TradeData.Potion.Id, 2));
        menu.Accept();
        Assert.NotNull(menu.Help);
        Assert.True(menu.HasPopup);

        Assert.True(menu.Back());

        Assert.Null(menu.Help);
        Assert.False(menu.HasPopup);
    }

    [Fact]
    public void WhileAnyPopupIsOpenShowingAListOrPointingDoesntChangeTheActiveList()
    {
        var (menu, _, _, _) = Open(
            fill: p => p.Add(TradeData.Trinket.Id),
            stock: party =>
            {
                party.Inventory!.Add(TradeData.Cheap.Id);
                party.Inventory.Add(TradeData.Hide.Id);
            });
        menu.ShowParty();
        var cursor = menu.Party.Inventory.Cursor;
        menu.Back();
        Assert.NotNull(menu.Choice);

        menu.ShowPile();
        menu.Point(pile: true, 0);
        Assert.False(menu.OnPile);

        menu.MoveCursor(1);
        Assert.Equal(cursor, menu.Party.Inventory.Cursor);
        Assert.Equal(1, menu.Choice!.Choices.Cursor);
    }

    [Fact]
    public void ShowingThePartyDoesNothingWhileAMessageIsUp()
    {
        var (menu, _, _, _) = Open(fill: p =>
        {
            p.Add(TradeData.Trinket.Id);
            p.Add(TradeData.Quest.Id);
        });
        menu.Back();
        Assert.NotNull(menu.Message);

        menu.ShowParty();
        menu.Point(pile: false, 0);

        Assert.True(menu.OnPile);
        menu.CloseMessage();
        menu.ShowParty();
        Assert.False(menu.OnPile);
    }

    [Fact]
    public void PointingAtThePartysListMakesItActiveAndMovesItsCursor()
    {
        var (menu, _, _, _) = Open(
            fill: p => p.Add(TradeData.Trinket.Id),
            stock: party =>
            {
                party.Inventory!.Add(TradeData.Cheap.Id);
                party.Inventory.Add(TradeData.Hide.Id);
            });
        Assert.True(menu.OnPile);
        var row = PartyRow(menu, TradeData.Hide.Id);

        menu.Point(pile: false, row);

        Assert.False(menu.OnPile);
        Assert.Equal(row, menu.Party.Inventory.Cursor);
        Assert.Equal(TradeData.Hide.Id, menu.Party.Inventory.Current!.Item.Id);
    }

    [Fact]
    public void MovingTheCursorMovesTheActiveList()
    {
        var (menu, _, _, _) = Open(
            fill: p =>
            {
                p.Add(TradeData.Trinket.Id);
                p.Add(TradeData.Cheap.Id);
            },
            stock: party =>
            {
                party.Inventory!.Add(TradeData.Spice.Id);
                party.Inventory.Add(TradeData.Hide.Id);
            });
        var partyCursor = menu.Party.Inventory.Cursor;

        menu.MoveCursor(1);
        Assert.Equal((1, partyCursor), (menu.Pile.Cursor, menu.Party.Inventory.Cursor));

        menu.ShowParty();
        menu.MoveCursor(1);
        Assert.Equal((1, (partyCursor + 1) % menu.Party.Inventory.Rows.Count), (menu.Pile.Cursor, menu.Party.Inventory.Cursor));
    }

    [Fact]
    public void DetailsDescribeTheActiveListsCurrentRow()
    {
        var ring = TradeData.Priced(30, 100, ItemKind.Ring);
        var (menu, _, _, _) = Open(
            fill: p => p.Add(TradeData.Leather.Id),
            stock: party => party.Inventory!.Add(ring.Id),
            more: [ring]);

        Assert.True(menu.OnPile);
        Assert.Equal(TradeData.Leather.Name, menu.Details()?.Title);
        menu.Point(pile: false, PartyRow(menu, ring.Id));
        Assert.Equal(ring.Name, menu.Details()?.Title);
    }

    [Fact]
    public void DetailsOfAnEmptyListAreNull()
    {
        var (menu, _, _, _) = Open();
        menu.Party.Inventory.Replace([], resetPastEnd: true);

        Assert.Null(menu.Details());
    }

    [Fact]
    public void ThePartysGoldIsUnchangedByShowingTheLootAndTheGoldLineSaysHowMuchWasFound()
    {
        var (menu, party, _, _) = Open(stock: party => party.Gold = 10, gold: 50);

        Assert.Equal(10u, party.Gold);
        Assert.Equal($"{Line(MenuText.FoundGold)} 50", menu.PileLine);
        Assert.Equal(50, menu.Loot!.Gold);
    }

    [Fact]
    public void ShowingALootWindowDoesntGiveTheGoldButOpeningDoes()
    {
        var catalog = TradeData.Catalog();
        var party = TradeData.PartyOf(catalog, null, TradeData.Member());
        party.Gold = 10;

        var showing = LootWindow.Showing(new ItemPile(catalog), 50, party, catalog);
        Assert.Equal(10u, party.Gold);
        Assert.Equal(50, showing.Gold);

        LootWindow.Open(new ItemPile(catalog), 50, party, catalog);
        Assert.Equal(60u, party.Gold);
    }

    [Fact]
    public void ThePileStacksBatteryAndChargesComeWithItIntoTheInventory()
    {
        var amulet = ItemData.NewItem(31, ItemKind.Amulet, bonuses: ItemData.Raises(Stat.Stamina, 12));
        var wand = ItemData.NewItem(32, ItemKind.Wand, bonuses: ItemData.CastsSpell(Characters.Fireball.Id, charges: 7));
        var (menu, party, loot, _) = Open(fill: p =>
        {
            p.Add(amulet.Id);
            p.Add(wand.Id);
        }, more: [amulet, wand]);
        loot.Find(amulet.Id)!.Instance.Battery = 5;
        loot.Find(wand.Id)!.Instance.Charges = 2;

        menu.Accept();
        menu.Accept();

        Assert.Equal(5, party.Inventory!.Find(amulet.Id)!.Instance.Battery);
        Assert.Equal(2, party.Inventory.Find(wand.Id)!.Instance.Charges);
    }

    [Fact]
    public void TheLootWindowsTakeCarriesTheStacksState()
    {
        var amulet = ItemData.NewItem(31, ItemKind.Amulet, bonuses: ItemData.Raises(Stat.Stamina, 12));
        var catalog = TradeData.Catalog(more: amulet);
        var party = TradeData.PartyOf(catalog, null, TradeData.Member());
        var loot = new ItemPile(catalog);
        loot.Add(amulet.Id);
        loot.Find(amulet.Id)!.Instance.Battery = 3;
        var window = LootWindow.Showing(loot, 0, party, catalog);

        Assert.Equal(TradeResult.Done, window.Take(amulet.Id));

        Assert.Equal(3, party.Inventory!.Find(amulet.Id)!.Instance.Battery);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(100, true)]
    public void AnItemIsDroppableWhenItsHalfPriceReadsMoreThanOne(int price, bool droppable)
    {
        var catalog = TradeData.Catalog();
        var party = TradeData.PartyOf(catalog, null, TradeData.Member());
        var window = LootWindow.Showing(new ItemPile(catalog), 0, party, catalog);
        var instance = new ItemInstance(Item.From(TradeData.Priced(30, price)));

        Assert.Equal(droppable, window.Droppable(instance));
    }
}

public class MenuPulseTests
{
    private static readonly Rgba From = new(100, 100, 100, 100), To = new(255, 255, 255, 255);

    /// <summary>A pulse a step a 30 fps frame, as trek's popups step it.</summary>
    private static MenuPulse Stepped(int frames, int ticksOver = 0)
    {
        var pulse = new MenuPulse(Cadence.FrameTicks);
        for (var i = 0; i < frames * Cadence.FrameTicks + ticksOver; i++)
            pulse.Step();
        return pulse;
    }

    private static Rgba Expected(Rgba from, Rgba to, int step) => new(
        (byte)(from.R + (to.R - from.R) / MenuPulse.Frames * step), (byte)(from.G + (to.G - from.G) / MenuPulse.Frames * step),
        (byte)(from.B + (to.B - from.B) / MenuPulse.Frames * step), (byte)(from.A + (to.A - from.A) / MenuPulse.Frames * step));

    private static int StepAfter(int frames) => frames % (2 * (MenuPulse.Frames - 1)) is var m && m <= MenuPulse.Frames - 1 ? m : 2 * (MenuPulse.Frames - 1) - m;

    [Fact]
    public void AMenusPulseStepsEveryTick()
    {
        var pulse = new MenuPulse();
        pulse.Step();
        Assert.Equal(new Rgba(107, 107, 107, 107), pulse.Now(From, To));
        for (var i = 1; i < 2 * (MenuPulse.Frames - 1); i++)
            pulse.Step();
        Assert.Equal(From, pulse.Now(From, To));
    }

    [Fact]
    public void APulseThatTurnsAt20ShowsItsTwentiethStepAndTakes40Frames()
    {
        // The options screen's and the World tab's: 7 a step, up to 100 + 140.
        var pulse = new MenuPulse(top: MenuPulse.Frames);
        for (var i = 0; i < 20; i++)
            pulse.Step();
        Assert.Equal(new Rgba(240, 240, 240, 240), pulse.Now(From, To));
        for (var i = 0; i < 20; i++)
            pulse.Step();
        Assert.Equal(From, pulse.Now(From, To));
    }

    [Fact]
    public void TheSaveScreensActionsPulseOverFifteenSteps()
    {
        // 155 / 15 is 10 a step, turning at 14: 28 frames a cycle.
        var pulse = new MenuPulse(frames: 15);
        for (var i = 0; i < 14; i++)
            pulse.Step();
        Assert.Equal(new Rgba(240, 240, 240, 240), pulse.Now(From, To));
        for (var i = 0; i < 14; i++)
            pulse.Step();
        Assert.Equal(From, pulse.Now(From, To));
    }

    [Fact]
    public void TheNameEntrysGreyStartsAt150AndGoes5AStepBetween95And230()
    {
        var entry = new NameEntry("Alaron");
        var greys = new List<byte>();
        for (var i = 0; i < 54; i++)
        {
            greys.Add(entry.Pulse.Now(NameEntry.PulseFrom, NameEntry.PulseTo).R);
            entry.Step();
        }
        Assert.Equal(150, greys[0]);
        Assert.Equal((95, 230), (greys.Min(), greys.Max()));
        Assert.Equal(150, entry.Pulse.Now(NameEntry.PulseFrom, NameEntry.PulseTo).R);
        Assert.All(greys.Zip(greys.Skip(1)), p => Assert.Equal(5, Math.Abs(p.First - p.Second)));
    }

    [Fact]
    public void ThePulseStartsAtItsFirstColour()
    {
        Assert.Equal(From, new MenuPulse().Now(From, To));
    }

    [Fact]
    public void ItDoesntMoveUntilAWholeFrameHasPassed()
    {
        Assert.Equal(From, Stepped(0, Cadence.FrameTicks - 1).Now(From, To));
        Assert.NotEqual(From, Stepped(1).Now(From, To));
    }

    [Fact]
    public void EachFrameMovesAWholeTwentiethOfTheWayRoundedTowardZero()
    {
        // 155 / 20 is 7 a step.
        Assert.Equal(new Rgba(107, 107, 107, 107), Stepped(1).Now(From, To));
        Assert.Equal(new Rgba(114, 114, 114, 114), Stepped(2).Now(From, To));
        Assert.Equal(new Rgba(100 + 7 * 19, 100 + 7 * 19, 100 + 7 * 19, 100 + 7 * 19), Stepped(19).Now(From, To));
    }

    [Fact]
    public void ItClimbsToStepNineteenAndComesBackDownAndUpAgain()
    {
        for (var frames = 0; frames <= 90; frames++)
            Assert.Equal(Expected(From, To, StepAfter(frames)), Stepped(frames).Now(From, To));

        Assert.Equal(19, StepAfter(19));
        Assert.Equal(18, StepAfter(20));
        Assert.Equal(0, StepAfter(38));
        Assert.Equal(1, StepAfter(39));
    }

    [Fact]
    public void AColourFallingTowardALowerOneMovesDownByWholeSteps()
    {
        var from = new Rgba(200, 10, 0, 255);
        var to = new Rgba(100, 250, 0, 55);

        // -100 / 20 is -5, 240 / 20 is 12, and -200 / 20 is -10.
        Assert.Equal(new Rgba(195, 22, 0, 245), Stepped(1).Now(from, to));
        Assert.Equal(new Rgba(105, 238, 0, 65), Stepped(19).Now(from, to));
        for (var frames = 0; frames <= 60; frames++)
            Assert.Equal(Expected(from, to, StepAfter(frames)), Stepped(frames).Now(from, to));
    }

    [Fact]
    public void ResetReturnsToTheFirstColourAndClimbsAgain()
    {
        var pulse = Stepped(25);
        Assert.NotEqual(From, pulse.Now(From, To));

        pulse.Reset();

        Assert.Equal(From, pulse.Now(From, To));
        for (var i = 0; i < 3 * Cadence.FrameTicks; i++)
            pulse.Step();
        Assert.Equal(Expected(From, To, 3), pulse.Now(From, To));
    }

    [Fact]
    public void ResetPartWayThroughAFrameStartsTheFrameCountAgain()
    {
        var pulse = Stepped(0, Cadence.FrameTicks - 1);

        pulse.Reset();
        pulse.Step();

        Assert.Equal(From, pulse.Now(From, To));
    }
}

public class ContainerContentsTests
{
    private static readonly RecordId Opened = ItemData.Flag(4);
    private static readonly RecordId Used = ItemData.Flag(7);
    private static readonly RecordId Table = RecordId.Hex(RecordTypes.Loot, 0x0800);

    private static ContainerObject Chest(ContainerKind kind = ContainerKind.Chest, RecordId? loot = null, int gold = 0, RecordId? item = null) => new(
        0, Vector3.Zero, 1f, PakVariant.Any, null, kind, Opened, ItemData.Flag(6), 0, 0, loot, gold, item)
    {
        Used = new FlagWrite(Used),
    };

    [Fact]
    public void ContentsGiveWhatOpenGivesWithTheItemItAlwaysHoldsAndSetNoFlags()
    {
        var catalog = TradeData.Catalog();
        var table = TradeData.Table(10, 20, 0, 0, 0, new LootDrop(TradeData.Trinket.Id, 100));
        RecordId? asked = null;
        LootRecord Load(RecordId id)
        {
            asked = id;
            return table;
        }
        ScriptedRandom Dice() => new(RandomFor.Range(10, 20, 15), RandomFor.Range(0, 2, 0), RandomFor.D(100, 1));

        var opened = new Container(Chest(loot: Table, item: TradeData.Cheap.Id)).Open(ItemData.Flags(), catalog, Load, Dice());
        var flags = ItemData.Flags();
        var contents = new Container(Chest(loot: Table, item: TradeData.Cheap.Id)).Contents(catalog, Load, Dice());

        Assert.Equal(Table, asked);
        Assert.Equal(opened.Gold, contents.Gold);
        Assert.Equal(opened.Items.Stacks.Select(s => (s.Item.Id, s.Quantity)), contents.Items.Stacks.Select(s => (s.Item.Id, s.Quantity)));
        Assert.Equal([(TradeData.Trinket.Id, 1), (TradeData.Cheap.Id, 1)], contents.Items.Stacks.Select(s => (s.Item.Id, s.Quantity)));
        Assert.False(flags.Get(Opened));
        Assert.False(flags.Get(Used));
    }

    [Fact]
    public void OpeningSetsTheFlagsThatContentsLeaveAlone()
    {
        var flags = ItemData.Flags();
        var chest = new Container(Chest(gold: 25, item: TradeData.Trinket.Id));

        var contents = chest.Contents(TradeData.Catalog(), _ => throw new InvalidOperationException(), new ScriptedRandom());

        Assert.Equal(25, contents.Gold);
        Assert.Equal([TradeData.Trinket.Id], contents.Items.Stacks.Select(s => s.Item.Id));
        Assert.False(flags.Get(Opened));
        Assert.False(flags.Get(Used));

        chest.Open(flags, TradeData.Catalog(), _ => throw new InvalidOperationException(), new ScriptedRandom());
        Assert.True(flags.Get(Opened));
        Assert.True(flags.Get(Used));
    }

    [Fact]
    public void ReagentGivesWhatHarvestGivesAndSetsNoFlag()
    {
        var catalog = TradeData.Catalog();
        var harvested = new Container(Chest(ContainerKind.Herb, gold: 10)).Harvest(0.5f, ItemData.Flags(), catalog);
        var flags = ItemData.Flags();

        var reagent = new Container(Chest(ContainerKind.Herb, gold: 10)).Reagent(0.5f, catalog);

        Assert.Equal(harvested.Stacks.Select(s => (s.Item.Id, s.Quantity)), reagent.Stacks.Select(s => (s.Item.Id, s.Quantity)));
        Assert.Equal([(TradeData.Herb.Id, 5)], reagent.Stacks.Select(s => (s.Item.Id, s.Quantity)));
        Assert.False(flags.Get(Used));
    }

    [Fact]
    public void HarvestSetsTheUsedFlagThatReagentLeavesAlone()
    {
        var flags = ItemData.Flags();
        var source = new Container(Chest(ContainerKind.Spice, gold: 10));

        source.Reagent(1f, TradeData.Catalog());
        Assert.False(flags.Get(Used));

        var pile = source.Harvest(1f, flags, TradeData.Catalog());
        Assert.True(flags.Get(Used));
        Assert.Equal([(TradeData.Spice.Id, 10)], pile.Stacks.Select(s => (s.Item.Id, s.Quantity)));
    }
}
