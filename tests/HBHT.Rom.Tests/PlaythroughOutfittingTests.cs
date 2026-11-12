using HBHT.Core;
using HBHT.Core.Characters;
using HBHT.Core.Items;
using HBHT.Core.Playthrough;
using HBHT.Plugins;

namespace HBHT.Rom.Tests;

/// <summary>
/// The playthrough bot's dressing (docs/systems/playthrough.md, gear): what an item is worth is weighed on the member as they'd be
/// wearing it, so of two items each can't look better than the other.
/// </summary>
public sealed class PlaythroughOutfittingTests(Na10Import fixture)
{
    private PluginSet Plugins => fixture.Data.Set;

    private static readonly RecordId Warfang = RecordIds.Item(0x0738);      // hit 55, damage 4, Warrior +2, Stamina +15
    private static readonly RecordId ChaosSword = RecordIds.Item(0x0727);   // hit 20, damage 5, no bonuses

    [Fact]
    public void ASwordsWarriorBonusDoesntMakeTheSwordItReplacesLookBetter()
    {
        var game = GameState.NewGame(Plugins);
        var party = game.Party;
        var alaron = party.Slots[0]!;
        // Strong enough for both, as Abrecan was when the dressing swapped them for ever.
        alaron.Stats.Train(Stat.Strength, 26 - alaron.Stats.Base(Stat.Strength));
        party.Inventory.Add(Warfang);
        party.Inventory.Add(ChaosSword);
        var warfang = party.Inventory.Catalog[Warfang];
        var chaos = party.Inventory.Catalog[ChaosSword];

        // Worn either way round, the two weigh the same as each other, so the gain one way is the loss the other.
        Assert.Equal(EquipRefusal.None, party.Equip(alaron, Warfang));
        var overWarfang = Outfitting.Worth(alaron, chaos, warfang)!.Value - Outfitting.Worth(alaron, warfang, warfang)!.Value;
        Assert.Equal(EquipRefusal.None, party.Equip(alaron, ChaosSword));
        var overChaos = Outfitting.Worth(alaron, warfang, chaos)!.Value - Outfitting.Worth(alaron, chaos, chaos)!.Value;
        Assert.Equal(-overWarfang, overChaos, 3);

        // So whichever the dressing puts on, it doesn't put the other back.
        var outfitting = new Outfitting();
        if (outfitting.Best(party) is { } best)
        {
            Assert.Equal(EquipRefusal.None, party.Equip(alaron, best.Item.Id));
        }
        Assert.Null(outfitting.Best(party));
    }
}
