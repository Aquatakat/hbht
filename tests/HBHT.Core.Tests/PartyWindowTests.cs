using HBHT.Core.Characters;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using CharacterParty = HBHT.Core.Characters.Party;

namespace HBHT.Core.Tests;

/// <summary>
/// The Party tab's windows through <see cref="IPartyWindow"/>, as the pause menu sends them presses: the tab, the loot window and
/// the training window each take the stick, C-left and C-right, the cursor and C-up as they did when the pause menu had a
/// handler for each.
/// </summary>
public class PartyWindowTests(BarterPlugins fixture) : IClassFixture<BarterPlugins>
{
    private static readonly MenuText Text = new([.. Enumerable.Range(0, 600).Select(i => $"{i}|%s|%s|%d")]);

    private static CharacterParty PartyOfTwo(Core.Items.ItemCatalog catalog) =>
        TradeData.PartyOf(catalog, ItemData.Flags(), TradeData.Member(0), TradeData.Member(1));

    private BarterMenu Barter(out CharacterParty party)
    {
        var catalog = TradeData.Catalog();
        party = PartyOfTwo(catalog);
        var loot = new Core.Items.ItemPile(catalog);
        loot.Add(TradeData.Spice.Id, 1);
        return BarterMenu.ForLoot(fixture.Plugins, party, loot, 0, ItemData.Flags());
    }

    private TrainingMenu Training()
    {
        var catalog = TradeData.Catalog();
        var party = PartyOfTwo(catalog);
        var trainer = Trainer.From(Characters.Trainer(RecordId.Hex(RecordTypes.Entity, 0x02D8), new SkillLevels(Alchemist: 2, Warrior: 5)),
            Characters.Rules, _ => Characters.Fireball);
        return TrainingMenu.For(fixture.Plugins, party, trainer, ItemData.Flags());
    }

    /// <summary>The tab is its own header; the loot and training windows are built on one.</summary>
    [Fact]
    public void TheTabIsItsOwnMenuAndTheOtherWindowsHaveOneUnderThem()
    {
        var tab = new PartyMenu(PartyOfTwo(TradeData.Catalog()), Text, _ => null, _ => null);
        var barter = Barter(out _);
        var training = Training();

        Assert.Same(tab, ((IPartyWindow)tab).Menu);
        Assert.Same(barter.Party, ((IPartyWindow)barter).Menu);
        Assert.Same(training.Party, ((IPartyWindow)training).Menu);
        Assert.NotSame(barter, ((IPartyWindow)barter).Menu);
    }

    /// <summary>C-left and C-right change the member in the tab and in training, and not in the loot window.</summary>
    [Fact]
    public void OnlyTheLootWindowKeepsTheMember()
    {
        var tab = new PartyMenu(PartyOfTwo(TradeData.Catalog()), Text, _ => null, _ => null);
        var barter = Barter(out _);
        var training = Training();
        IPartyWindow[] windows = [tab, barter, training];

        Assert.Equal([true, false, true], windows.Select(w => w.ChangesMember));

        foreach (var window in windows)
            window.ChangeMember(1);

        Assert.Equal([1, 0, 1], windows.Select(w => w.Menu.Member));
    }

    /// <summary>The stick's left shows the next list of the tab, and the loot window's reaches the pile on its left.</summary>
    [Fact]
    public void TheStickGoesToTheListOnThatSideOrChangesTheLists()
    {
        var tab = new PartyMenu(PartyOfTwo(TradeData.Catalog()), Text, _ => null, _ => null);
        IPartyWindow tabWindow = tab;
        tabWindow.Sideways(-1);
        Assert.Equal(PartyList.Spells, tab.List);
        while (tab.Slide is not null)
            tab.Step();
        tabWindow.Sideways(1);
        Assert.Equal(PartyList.Inventory, tab.List);

        var barter = Barter(out _);
        IPartyWindow barterWindow = barter;
        Assert.True(barter.OnPile);
        barterWindow.Sideways(1);
        Assert.False(barter.OnPile);
        barterWindow.Sideways(-1);
        Assert.True(barter.OnPile);

        var training = Training();
        Assert.False(training.OnSpells);
        ((IPartyWindow)training).Sideways(-1);
        Assert.True(training.OnSpells);
        ((IPartyWindow)training).Sideways(1);
        Assert.False(training.OnSpells);
    }

    /// <summary>Only the loot window and shops take C-up, and only the loot window closes itself.</summary>
    [Fact]
    public void OnlyTheBarterWindowTakesCUpAndClosesItself()
    {
        var tab = new PartyMenu(PartyOfTwo(TradeData.Catalog()), Text, _ => null, _ => null);

        Assert.False(((IPartyWindow)tab).Appraise());
        Assert.False(((IPartyWindow)Training()).Appraise());
        Assert.True(((IPartyWindow)Barter(out _)).Appraise());
        Assert.False(((IPartyWindow)tab).Closed);
        Assert.False(((IPartyWindow)Training()).Closed);
    }

    /// <summary>C-down closes a message and says so, in every window, so it doesn't show the details too.</summary>
    [Fact]
    public void CloseMessageSaysWhetherItClosedOne()
    {
        var tab = new PartyMenu(PartyOfTwo(TradeData.Catalog()), Text, _ => null, _ => null);
        IPartyWindow window = tab;
        Assert.False(window.CloseMessage());

        window.Overlay.Say("A message");
        Assert.True(window.HasPopup);
        Assert.True(window.CloseMessage());
        Assert.False(window.HasPopup);
    }
}
