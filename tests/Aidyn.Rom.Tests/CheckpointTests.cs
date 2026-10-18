using Aidyn.Core.Calendar;
using Aidyn.Core.Flags;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Db;
using Aidyn.Rom.Import;
using Aidyn.Rom.Quests;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Checks the flag network against the user's checkpoints (ares save states next to the ROMs). Each
/// state holds the console's flags, which the port has to reproduce. The tests skip without them.
/// </summary>
public class CheckpointTests
{
    // The flags are the same in 1.0 and the debug ROM, so both are checked against 1.0's records.
    private static readonly Lazy<List<FlagRecord>> Records =
        new(() =>
        {
            var rom = TestRoms.Get(RomVersionId.Na10);
            var db = GameDb.Read(rom);
            return FlagImporter.BuildFlags(db, FlagLabels.Read(rom, db));
        });

    private static FlagNetwork Network() => new(Records.Value, RecordId.Decimal(RecordTypes.Flag, EngineFlags.NewJournalEntry));

    [Theory]
    [MemberData(nameof(TestRoms.Checkpoints), MemberType = typeof(TestRoms))]
    public void TheNetworkReproducesTheDerivedFlags(string name)
    {
        var checkpoint = TestRoms.Checkpoint(name);
        var network = Network();
        Assert.Equal(checkpoint.Flags.Count, network.Count);

        network.Restore(h => checkpoint.Flags[h]);

        var wrong = Enumerable.Range(0, network.Count)
            .Where(h => network.IsDerived(h) && network.Peek(h) != checkpoint.Flags[h])
            .Select(h => $"{h} (port {network.Peek(h)}, console {checkpoint.Flags[h]})").ToList();
        Assert.True(wrong.Count == 0,
            $"{wrong.Count} of {Enumerable.Range(0, network.Count).Count(network.IsDerived)} derived flags differ in {name}: {string.Join(", ", wrong)}");
    }

    [Theory]
    [MemberData(nameof(TestRoms.Checkpoints), MemberType = typeof(TestRoms))]
    public void TheCalendarFlagsMatchTheClock(string name)
    {
        var checkpoint = TestRoms.Checkpoint(name);
        // The TerrainPointer: see docs/reference-rig.md.
        var terrain = checkpoint.ReadWord(name.StartsWith("AidynDebug", StringComparison.Ordinal) ? 0x800F1BBC : 0x800E303C);
        var partOfDay = checkpoint.ReadByte(terrain + 2);
        var moonPhases = checkpoint.ReadByte(terrain + 3);
        var dayNightMagic = checkpoint.ReadByte(terrain + 5);
        var rain = checkpoint.ReadByte(terrain + 6);
        var time = checkpoint.ReadWord(terrain + 0x20);
        var terrainType = checkpoint.ReadByte(terrain + 0x24);

        var network = Network();
        var state = new StateFlags(network, FlagImporter.BuildStateFlags());
        state.SetTime(new GameClock { Time = time });
        state.SetRain(rain);
        state.SetTerrain(terrainType);

        var first = EngineFlags.MoonPhase.First;
        var last = EngineFlags.Terrain.Last;
        var wrong = Enumerable.Range(first, last - first + 1)
            .Where(h => network.Peek(h) != checkpoint.Flags[h])
            .Select(h => $"{h} (port {network.Peek(h)}, console {checkpoint.Flags[h]})").ToList();
        Assert.True(wrong.Count == 0,
            $"Flags differ in {name} (time {time}, part of the day {partOfDay}, moon {moonPhases}, magic {dayNightMagic}, rain {rain}, terrain {terrainType}): {string.Join(", ", wrong)}");

        Assert.Equal(partOfDay, Value(checkpoint, EngineFlags.TimeOfDay));
        Assert.Equal(moonPhases, Value(checkpoint, EngineFlags.MoonPhase));
        Assert.Equal(0, dayNightMagic);
    }

    private static int Value(AresCheckpoint checkpoint, EngineFlagRange range) =>
        Enumerable.Range(0, range.Bits).Sum(i => checkpoint.Flags[range.First + i] ? 1 << i : 0);
}
