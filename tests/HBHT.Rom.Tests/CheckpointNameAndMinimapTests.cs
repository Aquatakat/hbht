using HBHT.Plugins;
using HBHT.Plugins.Saves;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

/// <summary>
/// Alaron's name and the minimaps' pieces, as the checkpoints' RAM holds them (<c>AresCheckpoint.Minimaps</c>, the sheets' name
/// pointer) and as <c>CheckpointSave.Convert</c> carries them. The tests skip without the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class CheckpointNameAndMinimapTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private const string V10 = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - ";

    /// <summary>How many of the 188 pieces each 1.0 checkpoint has seen: the start, then the story's.</summary>
    public static TheoryData<string, int> Seen() => new()
    {
        { V10 + "Wanderers around H13.bs1", 8 },
        { V10 + "Before Shamsuk's Tower.bs1", 26 },
        { V10 + "Before Rooughah.bs1", 96 },
        { V10 + "Before Rooughah, in Terminor.bs1", 97 },
        { V10 + "Before final encounter.bs1", 97 },
    };

    [Theory]
    [MemberData(nameof(Seen))]
    public void TheMinimapsPiecesSeenAreCountedAcrossTheEightLists(string name, int seen)
    {
        var checkpoint = TestRoms.Checkpoint(name);
        Assert.Equal(seen, checkpoint.Minimaps.Count);
        Assert.All(checkpoint.Minimaps, bit => Assert.InRange(bit, 0, 187));
    }

    [Theory]
    [MemberData(nameof(Seen))]
    public void TheSaveHoldsThePiecesAsItsMinimapsListThem(string name, int seen)
    {
        var checkpoint = TestRoms.Checkpoint(name);
        var save = CheckpointSave.Convert(checkpoint, Set, CheckpointSave.Label(name));

        // The save lists the minimaps that aren't as they start, and loading sets each piece from its bit: the bits that come back
        // from the save are the checkpoint's.
        var saved = save.Minimaps.ToDictionary(m => m.Minimap, m => m.Seen);
        var at = 0;
        var back = new HashSet<int>();
        foreach (var set in HBHT.Rom.Import.MinimapImporter.MapSets)
        {
            var minimap = Set.Get<HBHT.Plugins.Records.MinimapRecord>(RecordId.Decimal(RecordTypes.Minimap, set));
            var pieces = saved.TryGetValue(minimap.Id, out var s) ? s : [.. Enumerable.Range(0, minimap.Sections.Count).Where(i => minimap.Sections[i].Seen)];
            back.UnionWith(pieces.Select(i => at + i));
            at += minimap.Sections.Count;
        }
        Assert.Equal(188, at);
        Assert.Equal(seen, back.Count);
        Assert.Equal([.. checkpoint.Minimaps.Order()], [.. back.Order()]);
    }

    /// <summary>The story saves on the 1.0 pak and the checkpoints captured where each was saved, a little after.</summary>
    [Theory]
    [InlineData('A', "Before final encounter")]
    [InlineData('B', "Before Shamsuk's Tower")]
    [InlineData('C', "Before Rooughah")]
    [InlineData('D', "Before Rooughah, in Terminor")]
    public void ThePiecesAreThoseTheOriginalSavedThen(char slot, string label)
    {
        var checkpoint = TestRoms.Checkpoint($"{V10}{label}.bs1");
        var pak = ControllerPak.Read(ControllerPakTests.Pak(ControllerPakTests.Na10Pak));
        var saved = new PakSaves(Set, TestRoms.Get(RomVersionId.Na10)).Decode(pak.ReadNote(pak.Notes.Single(n => n.Label == $"CHRONICLES.{slot}")));

        // Pieces are only ever seen, never forgotten, so what the save held the checkpoint holds too.
        Assert.True(saved.Minimaps.IsSubsetOf(checkpoint.Minimaps));
        Assert.Equal(saved.Minimaps.Count, checkpoint.Minimaps.Count);
    }

    [Fact]
    public void TheDebugBuildKeepsThemToo()
    {
        var checkpoint = TestRoms.Checkpoint("AidynDebug - Before Shamsuk's Tower.bs2");
        Assert.Equal(26, checkpoint.Minimaps.Count);
    }

    [Theory]
    [MemberData(nameof(TestRoms.Checkpoints), MemberType = typeof(TestRoms))]
    public void AlaronsNameIsTheOneHeKeepsAndNeedsNoSaveFieldWhenItsTheRecords(string name)
    {
        var checkpoint = TestRoms.Checkpoint(name);
        Assert.Equal("Alaron", checkpoint.Party[0].Name);

        var save = CheckpointSave.Convert(checkpoint, Set, CheckpointSave.Label(name));
        Assert.Equal("Alaron", Set.Get<HBHT.Plugins.Records.EntityRecord>(save.Party.Members[0].Entity).Name);
        Assert.All(save.Party.Members.Concat(save.Party.Kept), m => Assert.Null(m.Name));
    }
}
