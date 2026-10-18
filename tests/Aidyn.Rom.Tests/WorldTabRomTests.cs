using Aidyn.Core;
using Aidyn.Core.Rng;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The World tab, the journal and camping against NA 1.0's imported data: the strings they read, and the tab as the
/// user's captures show it at the "Before final encounter" checkpoint (day 13 of the Earth season, chunk M06, every
/// region found). Skips without the checkpoint.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class WorldTabRomTests(Na10Import fixture)
{
    private const string FinalEncounter = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - Before final encounter.bs1";

    private PluginSet Set => fixture.Data.Set;

    private TextLayout MainFont => new(Set.Get<FontRecord>(RecordId.Decimal(RecordTypes.Font, 0)));

    private GameState Checkpoint()
    {
        var problems = new List<string>();
        var save = CheckpointSave.Convert(TestRoms.Checkpoint(FinalEncounter), Set, CheckpointSave.Label(FinalEncounter), problems);
        Assert.Empty(problems);
        return GameState.Load(Set, save);
    }

    [Fact]
    public void TheSeasonsNamesAndTheEmptyJournalsNoteAreTheCommonLines()
    {
        var text = MenuText.From(Set);
        Assert.Equal("THE BLOOMING SEASON", text[WorldMenu.SeasonNames].Trim());
        Assert.Equal("THE EARTH SEASON", text[WorldMenu.SeasonNames + 2].Trim());
        Assert.Equal("LATE EARTH SEASON", text[WorldMenu.SeasonNames + 3].Trim());
        Assert.Equal("No entries have been made in the journal.", text[WorldMenu.NoEntries].Trim());
    }

    [Fact]
    public void BeforeTheFinalEncounterTheTabShowsTheEarthSeasonsThirteenthAndTheWholeMap()
    {
        var game = Checkpoint();
        var menu = new WorldMenu(Set, game, MainFont, new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 0), 13, 6), outdoors: true);

        Assert.Equal("THE EARTH SEASON", menu.SeasonName.Trim());
        Assert.Equal((173, 93), menu.Today);
        Assert.Equal(18, menu.Pieces.Count);
        // The blue ring in the capture, its centre at (112.5, 46.5).
        Assert.Equal((108, 42), menu.Marker);
        Assert.Null(new WorldMenu(Set, game, MainFont, new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 6), 3, 5), outdoors: true).Marker);
        Assert.Null(new WorldMenu(Set, game, MainFont, new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 0), 13, 6), outdoors: false).Marker);
    }

    [Fact]
    public void TheJournalListsTheCheckpointsEntriesEndingWithTheDarkerSelfAndOpensAtTheFoot()
    {
        var game = Checkpoint();
        var menu = new WorldMenu(Set, game, MainFont, null, outdoors: false);
        menu.MoveCursor(1);
        menu.Accept();
        var journal = menu.Journal!;

        // The captures show entries 64, 65, 67 and 68 at the foot; 66 isn't set.
        Assert.Equal(32 + 2, journal.Items.Count);
        Assert.StartsWith("At last I have joined with my darker self.", journal.Items[^2].Text);
        Assert.Equal(JournalMenu.View - journal.ListHeight, journal.Scroll);
    }

    [Fact]
    public void CampingIsRefusedWhereTheTerrainAllowsNoneAndSaysSo()
    {
        var game = GameState.NewGame(Set);
        // MiscInteriors' chunks, but I05, allow no camping.
        var menu = new WorldMenu(Set, game, MainFont, new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 6), 3, 5), outdoors: false);
        menu.Accept();
        Assert.Equal(("The party cannot camp here.", false), (menu.Message?.Text, menu.CampChosen));
        menu.Accept();
        Assert.Null(menu.Message);

        // Nor does Gwernia's country, terrain 19, where the checkpoint stands; G14, terrain 0, does.
        var gwernia = new WorldMenu(Set, game, MainFont, new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 0), 13, 6), outdoors: true);
        gwernia.Accept();
        Assert.NotNull(gwernia.Message);
        var outside = new WorldMenu(Set, game, MainFont, new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 0), 7, 14), outdoors: true);
        outside.Accept();
        Assert.True(outside.CampChosen);
    }

    [Fact]
    public void TheAmbushNeedsTwoOfTheGwerniaRecruitsAndRangerCutsItToAThird()
    {
        var game = GameState.NewGame(Set);
        var rules = game.Camping;
        // A new game's Alaron has no Ranger: terrain 0's factor of 20, in percent.
        Assert.Equal(20, Camping.AmbushChance(rules, 20, game.Party));
        Assert.Equal(50, Camping.AmbushChance(rules, 50, game.Party));
        var always = new Lcg();
        Assert.False(Camping.Ambushed(rules, 50, game.Party, game.Flags.Get, always));

        // Two of Abrecan, Godric and Rheda joined (89–91).
        game.Flags.Set(89, true);
        Assert.False(game.Flags.Get(rules.Ambushes));
        game.Flags.Set(91, true);
        Assert.True(game.Flags.Get(rules.Ambushes));

        // The party's best Ranger, at most 10, before the final encounter.
        var later = Checkpoint();
        var ranger = Math.Clamp(later.Party.MostSkilled(Core.Characters.Skill.Ranger)!.Skills.Modified(Core.Characters.Skill.Ranger), 0, 10);
        Assert.True(ranger > 0);
        Assert.Equal((int)((15 - ranger) * 0.0666667f * 20f), Camping.AmbushChance(rules, 20, later.Party));
    }
}
