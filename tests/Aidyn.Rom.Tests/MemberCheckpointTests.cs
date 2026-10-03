using Aidyn.Core;
using Aidyn.Core.Characters;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Plugins.Saves;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The sheets the console keeps for members who left (<c>gSaveEntity</c>'s slots 4 to 8), as the checkpoints hold them, and that they
/// become the game's kept sheets. In every one the party is Alaron, Rheda, Brenna and Sholeh or Niesen, and Arturo, Becan and Godric
/// have left. The tests skip without the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class MemberCheckpointTests(MapObjectFixture fixture)
{
    private static readonly ushort[] Left = [0x0240, 0x0242, 0x0248];

    private PluginSet Set => fixture.Data.Set;

    public static TheoryData<string> Checkpoints() => CheckpointTests.Checkpoints();

    private static AresCheckpoint Load(string name)
    {
        if (name.Length == 0)
            Assert.Skip($"No checkpoints in {TestRoms.Folder}.");
        return AresCheckpoint.Read(Path.Combine(TestRoms.Folder, name));
    }

    private (SaveGame Save, List<string> Problems) Convert(string name, AresCheckpoint checkpoint)
    {
        var problems = new List<string>();
        return (CheckpointSave.Convert(checkpoint, Set, CheckpointSave.Label(name), problems), problems);
    }

    private static RecordId Entity(int n) => RecordId.Hex(RecordTypes.Entity, n);

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void TheConsoleKeepsSheetsForArturoBecanAndGodricWithNothingWornAndTheStatsTheyLeftWith(string name)
    {
        var checkpoint = Load(name);

        // Alaron, Rheda and Brenna, and Sholeh or Niesen: Shamsuk's Tower comes before Sholeh joins.
        Assert.Equal([0x023F, 0x024C, 0x0243], checkpoint.Party.Take(3).Select(p => (int)p.Id));
        Assert.Contains(checkpoint.Party[3].Id, new ushort[] { 0x024D, 0x024A });

        Assert.Equal(Left, checkpoint.Kept.Select(k => k.Id));
        foreach (var sheet in checkpoint.Kept)
        {
            Assert.Null(sheet.Weapon);
            Assert.Null(sheet.Armor);
            Assert.Null(sheet.Shield);
            Assert.Equal(0, sheet.GearCount);
            Assert.All(sheet.Gear, g => Assert.Null(g));
            // A real sheet: a level, the stats it was trained to, and the experience it had.
            Assert.True(sheet.Trained[(int)Stat.Level] >= 1, $"{sheet.Id:X4} has no level.");
            Assert.True(sheet.TotalExp > 0, $"{sheet.Id:X4} has no experience.");
            Assert.All(sheet.Trained, t => Assert.InRange(t, 1, 90));
        }
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void TheConvertedSaveKeepsThoseThreeAndNoneOfThePartyAndLoadsWithoutProblems(string name)
    {
        var checkpoint = Load(name);
        var (save, problems) = Convert(name, checkpoint);
        Assert.Empty(problems);

        Assert.Equal(Left.Select(l => Entity(l)), save.Party.Kept.Select(k => k.Entity));
        Assert.Empty(save.Party.Kept.Select(k => k.Entity).Intersect(save.Party.Members.Select(m => m.Entity)));
        foreach (var kept in save.Party.Kept)
        {
            var sheet = checkpoint.Kept.Single(k => k.Id == (ushort)System.Convert.ToInt32(kept.Entity.Key[2..], 16));
            Assert.Equal(sheet.TotalExp, kept.TotalExp);
            Assert.Equal(sheet.SpendableExp, kept.SpendableExp);
            Assert.Equal(sheet.Trained, kept.Trained.ToArray());
            Assert.Equal(sheet.Current, kept.Current.ToArray());
            Assert.Null(kept.Weapon);
            Assert.Null(kept.Armor);
            Assert.Null(kept.Shield);
            Assert.Empty(kept.Gear);
            Assert.Empty(kept.Potions);
        }

        var game = GameState.Load(Set, save);
        Assert.Empty(game.Problems);
        Assert.Equal(Left.Select(l => Entity(l)), game.Party.Kept.Keys.OrderBy(k => k.ToString(), StringComparer.Ordinal));
        foreach (var kept in save.Party.Kept)
        {
            var loaded = game.Party.Kept[kept.Entity];
            Assert.Equal((kept.TotalExp, kept.Trained, kept.Skills), (loaded.TotalExp, loaded.Trained, loaded.Skills));
            Assert.Equal(kept.Spells, loaded.Spells);
        }
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void TheKeptSheetsSaveAgainAndRoundTripThroughTheSaveFormat(string name)
    {
        var checkpoint = Load(name);
        var (save, _) = Convert(name, checkpoint);
        var game = GameState.Load(Set, save);

        var json = SaveFile.Serialize(game.Save());
        Assert.Equal(3, game.Save().Party.Kept.Count);
        var again = GameState.Load(Set, SaveFile.Parse(json));

        Assert.Empty(again.Problems);
        Assert.Equal(3, again.Party.Kept.Count);
        Assert.Equal(json, SaveFile.Serialize(again.Save()));
    }

    /// <summary>With Sholeh or Niesen leaving a full party, Godric comes back as he was on the console, with what the console kept.</summary>
    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void GodricRejoinsAFullPartyAfterSomeoneLeavesWithTheExperienceTheConsoleKept(string name)
    {
        var checkpoint = Load(name);
        var game = GameState.Load(Set, Convert(name, checkpoint).Save);
        var godric = Entity(0x0248);
        var sheet = checkpoint.Kept.Single(k => k.Id == 0x0248);
        Assert.Equal(4, game.Party.Count);
        Assert.False(game.AddMember(godric));

        game.RemoveMember(game.Party.Slots[3]!.Entity);
        Assert.True(game.AddMember(godric));

        var back = game.Party.Get(godric)!;
        Assert.Equal(sheet.TotalExp, back.TotalExp);
        Assert.Equal(sheet.SpendableExp, back.SpendableExp);
        foreach (var stat in Enum.GetValues<Stat>())
            Assert.Equal(sheet.Trained[(int)stat], back.Stats.Trained(stat));
        Assert.Equal(sheet.Spells.Select(s => (RecordId.Hex(RecordTypes.Spell, s.Spell), s.Level)).OrderBy(s => s.Item1.ToString(), StringComparer.Ordinal),
            back.Spells.Select(s => (s.Spell.Id, s.Level)).OrderBy(s => s.Item1.ToString(), StringComparer.Ordinal));
        Assert.DoesNotContain(godric, game.Party.Kept.Keys);
        Assert.True(game.Flags.Get(RecordId.Decimal(RecordTypes.Flag, 90)));
        Assert.False(game.Flags.Get(RecordId.Decimal(RecordTypes.Flag, 686)));
    }
}
