using HBHT.Plugins;
using HBHT.Plugins.Saves;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

/// <summary>
/// Checks the original's saves against the four on the user's 1.0 pak: each round-trips byte for byte, reads as the
/// checkpoint captured at the same place reads, and comes back to the same bytes through the port's save. The tests
/// skip without the pak or the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class PakSaveTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private PakSaves Saves => new(Set, TestRoms.Get(RomVersionId.Na10));

    /// <summary>The checkpoint captured where each slot was saved.</summary>
    public static TheoryData<char, string> Slots() => new()
    {
        { 'A', "Before final encounter" },
        { 'B', "Before Shamsuk's Tower" },
        { 'C', "Before Rooughah" },
        { 'D', "Before Rooughah, in Terminor" },
    };

    private (byte[] Note, PakSave Save) Slot(char slot)
    {
        var pak = ControllerPak.Read(ControllerPakTests.Pak(ControllerPakTests.Na10Pak));
        var note = pak.Notes.Single(n => n.Label == $"CHRONICLES.{slot}");
        var bytes = pak.ReadNote(note);
        return (bytes, Saves.Decode(bytes));
    }

    [Theory]
    [MemberData(nameof(Slots))]
    public void RoundTripsByteForByte(char slot, string _)
    {
        var (note, save) = Slot(slot);
        Assert.Equal(note, Saves.Encode(save));
    }

    [Fact]
    public void ACorruptSaveIsRefused()
    {
        var (note, _) = Slot('A');
        note[0x1340] ^= 1;
        Assert.Throws<InvalidDataException>(() => Saves.Decode(note));
    }

    [Theory]
    [MemberData(nameof(Slots))]
    public void ReadsAsItsCheckpointDoes(char slot, string label)
    {
        var (_, pak) = Slot(slot);
        var problems = new List<string>();
        var fromPak = Saves.ToSave(pak, label, problems);
        Assert.Empty(problems);
        var fromCheckpoint = CheckpointSave.Convert(TestRoms.Checkpoint($"Aidyn Chronicles - The First Mage (U) (V1.0) [!] - {label}.bs1"), Set, label);

        // The checkpoints were captured a little after the saves, and hold the weather in full and the True Name, which the
        // save leaves to the rules. The rest is the same, in the order of each one's own lists.
        static string Comparable(SaveGame save) => SaveFile.Serialize(save with
        {
            PlayTime = 0,
            Time = 0,
            Weather = null,
            Minimaps = [],
            Party = save.Party with
            {
                Members = [.. save.Party.Members.Select(Character)],
                Kept = [.. save.Party.Kept.Select(Character)],
                Inventory = [.. save.Party.Inventory.OrderBy(s => s.Item.ToString(), StringComparer.Ordinal)],
            },
        });
        static SaveCharacter Character(SaveCharacter c) => c with { TrueName = null, Spells = [.. c.Spells.OrderBy(s => s.Spell.ToString(), StringComparer.Ordinal)] };
        Assert.Equal(Comparable(fromCheckpoint), Comparable(fromPak));
        Assert.True(fromPak.PlayTime <= fromCheckpoint.PlayTime);
    }

    [Theory]
    [MemberData(nameof(Slots))]
    public void ThePortsSaveWritesTheSameSave(char slot, string label)
    {
        var (_, pak) = Slot(slot);
        var problems = new List<string>();
        var written = Saves.FromSave(Saves.ToSave(pak, label), problems);
        Assert.Empty(problems);

        // What the port's save doesn't hold comes back as a fresh save has it: the picture black, the movement timers at 0,
        // the camera behind the player, and the weather, which the port's save takes from the checkpoints only. The memories
        // are in slots 0 on, in the order they were made: which slots a chart used and freed before isn't kept.
        var facing = pak.Place.Facing;
        var expected = pak with
        {
            Chart = PakChart.Of([.. pak.Chart.Used]),
            Thumbnail = [.. Enumerable.Repeat((ushort)1, PakSave.ThumbnailWidth * PakSave.ThumbnailHeight)],
            Timers = [0, 0, 0],
            Place = pak.Place with { Camera = pak.Place.Position + new System.Numerics.Vector3(facing.X * 5, 2.3f, facing.Y * 5) },
            Weather = new PakWeather(0, 0, 0, 0),
        };
        Assert.Equal(Saves.Encode(expected), Saves.Encode(written));
    }

    [Fact]
    public void ANewGameIsWrittenWhereItArrives()
    {
        var problems = new List<string>();
        var pak = Saves.FromSave(HBHT.Core.GameState.NewGame(Set).Save(), problems);
        Assert.Contains("where a new game arrives", Assert.Single(problems), StringComparison.Ordinal);
        var save = Saves.ToSave(Saves.Decode(Saves.Encode(pak)));
        Assert.Equal(("I08", 1u), (save.Place!.Chunk, save.Party.Gold));
        Assert.Equal([Ids.Entity(0x023F)], save.Party.Members.Select(m => m.Entity));
    }

    [Fact]
    public void WritesASlotThatReadsBack()
    {
        var (_, pak) = Slot('B');
        var target = ControllerPak.Blank();
        Saves.Write(target, 'C', pak);
        var read = Assert.Single(Saves.Read(ControllerPak.Read(target.ToArray())));
        Assert.Equal('C', read.Slot);
        Assert.Equal(Saves.Encode(pak), Saves.Encode(read.Save));
        Assert.Equal(28, target.Notes.Single().Pages);
    }
}
