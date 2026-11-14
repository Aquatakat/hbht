using CharacterParty = HBHT.Core.Characters.Party;
using HBHT.Core.Items;
using HBHT.Core.Playthrough;
using HBHT.Core.Ui;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>
/// The playthrough bot's loot policy giving an item up (docs/systems/playthrough.md, "Stage 10: giving an item up, and near starts"): a
/// step that opens a container leaving an item (<see cref="PlayerAction.Leaves"/>) takes the rest of the pile, never that item, and
/// closes the window with it left on the pile, by the loot window's own presses.
/// </summary>
public sealed class LootLeavingTests(BarterPlugins fixture) : IClassFixture<BarterPlugins>
{
    /// <summary>Plays the policy's presses on a loot window holding the trinket and the cheap item until the window closes.</summary>
    private static (ItemPile Loot, CharacterParty Party, BarterMenu Menu) Play(BarterPlugins fixture, ItemRecord? leave)
    {
        var catalog = TradeData.Catalog(8, []);
        var party = TradeData.PartyOf(catalog, ItemData.Flags(), TradeData.Member());
        var loot = new ItemPile(catalog);
        loot.Add(TradeData.Trinket.Id);
        loot.Add(TradeData.Cheap.Id, 2);
        var menu = BarterMenu.ForLoot(fixture.Plugins, party, loot, 0, null);
        var policy = new TakeLootLeaveTheRest { Leave = leave?.Id };
        for (var presses = 0; presses < 40 && !menu.Closed; presses++)
        {
            switch (policy.Loot(menu))
            {
                case MenuAction.Accept:
                    menu.Accept();
                    break;
                case MenuAction.Back:
                    if (!menu.Back())
                        return (loot, party, menu);
                    break;
                case MenuAction.Left:
                    menu.ShowPile();
                    break;
                case MenuAction.Down:
                    menu.MoveCursor(1);
                    break;
                case MenuAction.Up:
                    menu.MoveCursor(-1);
                    break;
            }
        }
        Assert.True(menu.Closed, "the loot window didn't close");
        return (loot, party, menu);
    }

    [Fact]
    public void WithNothingToLeaveTheWholePileIsTaken()
    {
        var (loot, party, _) = Play(fixture, null);
        Assert.Empty(loot.Stacks);
        Assert.Equal(1, party.Inventory.Count(TradeData.Trinket.Id));
        Assert.Equal(2, party.Inventory.Count(TradeData.Cheap.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnItemToLeaveStaysOnThePileWhateverItsRowAndTheRestIsTaken(bool trinket)
    {
        // Each item in turn, so the one left is the pile's first row once and its last once.
        var (left, taken, count) = trinket ? (TradeData.Trinket, TradeData.Cheap, 2) : (TradeData.Cheap, TradeData.Trinket, 1);
        var (loot, party, menu) = Play(fixture, left);
        Assert.True(menu.Closed);
        Assert.Equal(0, party.Inventory.Count(left.Id));
        Assert.NotNull(loot.Find(left.Id));
        Assert.Equal(count, party.Inventory.Count(taken.Id));
        Assert.Null(loot.Find(taken.Id));
    }
}
