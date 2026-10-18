using Aidyn.Core;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Saves;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The party's sheets, kept sheets and inventory save and load back to the same save, for a new game and for each
/// checkpoint's game, once as it stands and once with a member renamed, potions working, and the others gone from
/// the party with their sheets kept. Set <c>AIDYN_SAVE_DUMP</c> to a folder to write the saves' JSON there, to
/// compare a change to the mapping with the one before it.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class SaveMappingTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private static string Dump(string name, string json)
    {
        if (Environment.GetEnvironmentVariable("AIDYN_SAVE_DUMP") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name.Replace('.', '_') + ".json"), json);
        }
        return json;
    }

    private void AssertRoundTrips(string name, GameState game)
    {
        var first = Dump(name, SaveFile.Serialize(game.Save()));
        var again = GameState.Load(Set, SaveFile.Parse(first));
        Assert.Empty(again.Problems);
        Assert.Equal(first, SaveFile.Serialize(again.Save()));
    }

    // Renamed, potions working, and everyone but the first out of the party with their sheets kept.
    private static void Disturb(GameState game)
    {
        var first = game.Party.Members.First();
        first.Name = "Bob";
        first.ApplyPotionEffect(Potion.Strength, 0, 5000);
        first.ApplyPotionEffect(Potion.Clarity, 1, 900);
        foreach (var other in game.Party.Members.Skip(1).Select(m => m.Entity).ToList())
            game.RemoveMember(other);
    }

    [Fact]
    public void ANewGameSavesAndLoadsBackTheSame()
    {
        var game = GameState.NewGame(Set);
        AssertRoundTrips("new game", game);
        Disturb(game);
        AssertRoundTrips("new game, disturbed", game);
    }

    [Theory]
    [MemberData(nameof(TestRoms.Checkpoints), MemberType = typeof(TestRoms))]
    public void ACheckpointsGameSavesAndLoadsBackTheSameAsItStandsAndDisturbed(string name)
    {
        var problems = new List<string>();
        var save = CheckpointSave.Convert(TestRoms.Checkpoint(name), Set, CheckpointSave.Label(name), problems);
        var game = GameState.Load(Set, save);
        AssertRoundTrips(name, game);
        Disturb(game);
        AssertRoundTrips(name + " disturbed", game);
    }
}
