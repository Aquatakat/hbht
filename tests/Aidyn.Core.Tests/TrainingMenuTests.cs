using Aidyn.Core.Characters;
using CharacterParty = Aidyn.Core.Characters.Party;
using Aidyn.Core.Flags;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>
/// The training window at a trainer (<c>WidgetMenuSpells</c>), checked against 1.0's code. Alaron has
/// Stealth 1, Troubadour 1, Warrior 3 and Wizard 1; the trainer teaches Alchemist 2, Warrior 5 and the
/// sword at 6.
/// </summary>
public class TrainingMenuTests(BarterPlugins fixture) : IClassFixture<BarterPlugins>
{
    private static readonly RecordId Keeper = RecordId.Hex(RecordTypes.Entity, 0x02D8);

    private static readonly SpellRecord Spark = Characters.Spell(0x0306, MagicSchool.Elemental, wizard: 1, cost: 200);

    private readonly PluginSet _plugins = fixture.Plugins;

    private static string Line(int line) => $"L{line}";

    private (TrainingMenu Menu, CharacterParty Party, Character Alaron, FlagNetwork Flags) Open(SkillLevels? teaches = null, RecordId? keeper = null,
        KnownSpell[]? spells = null, uint exp = 100_000, uint gold = 1000, Action<Character>? edit = null)
    {
        var alaron = Characters.Create(Characters.Alaron());
        alaron.SpendableExp = exp;
        edit?.Invoke(alaron);
        var flags = ItemData.Flags();
        var party = TradeData.PartyOf(TradeData.Catalog(), flags, alaron);
        party.Gold = gold;
        var trainer = Trainer.From(Characters.Trainer(keeper ?? Keeper, teaches ?? new SkillLevels(Alchemist: 2, Warrior: 5), spells),
            Characters.Rules, id => id == Spark.Id ? Spark : Characters.Fireball);
        return (TrainingMenu.For(_plugins, party, trainer, flags), party, alaron, flags);
    }

    private static void Select(TrainingMenu menu, SkillId skill) => menu.Point(menu.Skills.Rows.ToList().FindIndex(r => r.Skill == skill));

    [Fact]
    public void TheTrainersListIsWhatTheyTeachAtTheirLevelsAndStartsOnSkills()
    {
        var (menu, _, _, _) = Open();

        Assert.False(menu.OnSpells);
        Assert.Equal(PartyList.Skills, menu.Party.List);
        Assert.Equal(MenuImages.TrainTitle, menu.Title);
        Assert.Equal([(SkillId)Skill.Alchemist, Skill.Warrior, WeaponClass.Sword], menu.Skills.Rows.Select(r => r.Skill));
        Assert.Equal([2, 5, 6], menu.Skills.Rows.Select(r => r.Value));
        Assert.All(menu.Skills.Rows, r => Assert.Equal(0, r.Change));
    }

    [Fact]
    public void ATrainersSkillAbove10ReadsAs10WithTheRestAsADifference()
    {
        var (menu, _, _, _) = Open(teaches: new SkillLevels(Warrior: 12));

        var warrior = menu.Skills.Rows.Single(r => r.Skill == Skill.Warrior);
        Assert.Equal((10, 2), (warrior.Value, warrior.Change));
    }

    [Fact]
    public void TheMembersListFollowsTheTrainersRowOrIsPlainWithoutIt()
    {
        var (menu, _, _, _) = Open();

        Assert.Equal(Skill.Alchemist, menu.CurrentSkill);
        Assert.False(menu.Followed);

        menu.MoveCursor(1);

        Assert.True(menu.Followed);
        Assert.Equal(Skill.Warrior, menu.Party.Skills.Current!.Skill);
    }

    [Fact]
    public void ThePriceLinesAreTheMembersPricesAt80PercentAndTheGold()
    {
        var (menu, _, _, _) = Open();

        // Alchemist from 0: 1 × 1500 at 80%, and 200 gold.
        Assert.Equal(["L494 1200", "L495 100000", "L520 200"], menu.PriceLines);

        // Warrior from 3: 16 × 1000 at 80%, and 4 × 40 gold.
        Select(menu, Skill.Warrior);
        Assert.Equal(["L494 12800", "L495 100000", "L520 160"], menu.PriceLines);
    }

    [Fact]
    public void ASkillAt10CostsMaxButItsGoldStillShows()
    {
        var (menu, _, _, _) = Open(edit: a => a.Skills.Train(Skill.Warrior, 7));
        Select(menu, Skill.Warrior);

        Assert.Equal([Line(MenuText.XpCostMax), "L495 100000", "L520 440"], menu.PriceLines);
    }

    [Fact]
    public void OrianaShowsNoGoldAndAsksForExperienceAlone()
    {
        var (menu, party, alaron, _) = Open(keeper: Characters.Oriana);
        Select(menu, Skill.Warrior);

        Assert.Equal(" ", menu.PriceLines[2]);
        menu.Accept();
        Assert.Equal("L519 12800", menu.Choice?.Text);

        menu.Accept();

        Assert.Equal(4, alaron.Skills.Base(Skill.Warrior));
        Assert.Equal(1000u, party.Gold);
    }

    [Fact]
    public void ARefusalSaysWhyInTheOriginalsOrder()
    {
        var (knows, _, _, _) = Open(teaches: new SkillLevels(Warrior: 3));
        Select(knows, Skill.Warrior);
        knows.Accept();
        Assert.Equal(Line(MenuText.SkillCantTeach), knows.Message?.Text);
        Assert.Null(knows.Choice);

        var (poor, _, _, _) = Open(gold: 100);
        Select(poor, Skill.Warrior);
        poor.Accept();
        Assert.Equal("L516 12800 160", poor.Message?.Text);
        poor.Accept();
        Assert.Null(poor.Message);
    }

    [Fact]
    public void YesTrainsTheRowSilentlyAndNoLeavesItBeAndBDoesNothing()
    {
        var (menu, party, alaron, _) = Open();
        Select(menu, Skill.Warrior);

        menu.Accept();
        var choice = Assert.IsType<MenuChoice>(menu.Choice);
        Assert.Equal("L518 12800 160", choice.Text);
        Assert.Equal([Line(TrainingMenu.YesLine), Line(TrainingMenu.NoLine)], choice.Choices.Rows);
        Assert.True(menu.Back());
        Assert.NotNull(menu.Choice);

        menu.MoveCursor(1);
        menu.Accept();
        Assert.Equal(3, alaron.Skills.Base(Skill.Warrior));
        Assert.Null(menu.Party.PreviousNumbers);

        // The purchase crossfades the header's numbers, as every list's purchase ends (PartyMenu.HealthGoldChange).
        var before = menu.Party.Numbers;
        menu.Accept();
        menu.Accept();
        Assert.Equal(before, menu.Party.PreviousNumbers);

        Assert.Null(menu.Choice);
        Assert.Null(menu.Message);
        Assert.Equal(4, alaron.Skills.Base(Skill.Warrior));
        Assert.Equal(100_000u - 12_800, alaron.SpendableExp);
        Assert.Equal(1000u - 160, party.Gold);
        Assert.Equal(4, menu.Party.Skills.Current!.Value);
        Assert.Equal(Skill.Warrior, menu.CurrentSkill);
    }

    [Fact]
    public void ANewSkillJoinsTheMembersListAndTheirListFollowsIt()
    {
        var (menu, _, alaron, _) = Open();

        menu.Accept();
        menu.Accept();

        Assert.Equal(1, alaron.Skills.Base(Skill.Alchemist));
        Assert.True(menu.Followed);
        Assert.Equal(Skill.Alchemist, menu.Party.Skills.Current!.Skill);
    }

    [Fact]
    public void TheStickSwitchesBothListsToSpellsAndTheFirstSpellShowsHelp()
    {
        var (menu, _, alaron, flags) = Open(spells: [new KnownSpell(Spark.Id, 4)]);

        menu.Switch();

        Assert.True(menu.OnSpells);
        Assert.Equal(PartyList.Spells, menu.Party.List);
        Assert.Equal(4, Assert.Single(menu.Spells.Rows).Rank);
        Assert.False(menu.Followed);
        // A spell Alaron doesn't know: its own cost at 80%, and 200 gold.
        Assert.Equal(["L494 160", "L495 100000", "L520 200"], menu.PriceLines);

        menu.Accept();
        Assert.Equal("L504 160 200", menu.Choice?.Text);
        menu.Accept();

        Assert.Equal(1, alaron.FindSpell(Spark.Id)?.Level);
        Assert.Equal(Line(MenuText.FirstSpell), menu.Help);
        Assert.True(flags.Get(Characters.FirstSpellFlag));
        Assert.True(menu.Followed);
        menu.Accept();
        Assert.Null(menu.Help);

        menu.Accept();
        menu.Accept();
        Assert.Equal(2, alaron.FindSpell(Spark.Id)?.Level);
        Assert.Null(menu.Help);
    }

    [Fact]
    public void ASpellsRefusalsAreTheSpellLines()
    {
        var (menu, _, _, _) = Open(spells: [new KnownSpell(Characters.Fireball.Id, 4)]);
        menu.Switch();

        menu.Accept();

        Assert.Equal(Line(MenuText.SpellWizardTooLow), menu.Message?.Text);
    }

    [Fact]
    public void AnEmptyListShowsOnlyTheExperienceAndADoesNothing()
    {
        var (menu, _, _, _) = Open();
        menu.Switch();

        Assert.Empty(menu.Spells.Rows);
        Assert.Equal([" ", "L495 100000", " "], menu.PriceLines);
        menu.Accept();
        Assert.False(menu.HasPopup);
        Assert.False(menu.Followed);
    }

    [Fact]
    public void BClosesTheWindowAndClosesAMessageFirst()
    {
        var (menu, _, _, _) = Open(teaches: new SkillLevels(Warrior: 3));
        Select(menu, Skill.Warrior);
        menu.Accept();

        Assert.True(menu.Back());
        Assert.Null(menu.Message);
        Assert.False(menu.Back());
    }

    [Fact]
    public void CDownShowsTheSkillsDescription()
    {
        var (menu, _, _, _) = Open();

        Assert.NotNull(menu.Details());
    }
}
