using Aidyn.Core.MapObjects;
using Aidyn.Plugins;

namespace Aidyn.Core.Tests;

public class MapObjectMemoryTests
{
    private static Memory Key(int index, MemoryKind kind = MemoryKind.RandomLine, int skill = 0) =>
        new(RecordId.Parse($"aidyn:voxel/0x1063/{index}"), kind, skill);

    /// <summary>A full chart: objects 0 to 18, of the kinds in turn.</summary>
    private static MapObjectMemory Full(params MemoryKind[] kinds)
    {
        var memory = new MapObjectMemory();
        for (var i = 0; i < MapObjectMemory.Slots; i++)
            memory.Remember(Key(i, kinds[i % kinds.Length]), i);
        Assert.Equal(MapObjectMemory.Slots, memory.Entries.Count);
        return memory;
    }

    [Fact]
    public void ARememberedValueIsRecalledAndRecallingKeepsItForTenMoreMapLoads()
    {
        var memory = new MapObjectMemory();
        var key = Key(2);
        memory.Remember(key, 99, lifetime: 4);

        Assert.False(memory.TryRecall(Key(3), out _));
        Assert.Equal(4, memory.Find(key)!.Lifetime);

        Assert.True(memory.TryRecall(key, out var value));
        Assert.Equal(99, value);
        Assert.Equal(10, memory.Find(key)!.Lifetime);
        Assert.Equal(10, MapObjectMemory.Long);
    }

    [Fact]
    public void AMemoryIsKeyedByItsObjectItsKindAndItsSkill()
    {
        var memory = new MapObjectMemory();
        memory.Remember(Key(2), 50);
        memory.Remember(Key(2, MemoryKind.SkillCheck, skill: 1), 2);
        memory.Remember(Key(2, MemoryKind.SkillCheck, skill: 3), 4);

        Assert.True(memory.TryRecall(Key(2, MemoryKind.SkillCheck, skill: 3), out var value));
        Assert.Equal(4, value);
        Assert.False(memory.TryRecall(Key(2, MemoryKind.SkillCheck, skill: 2), out _));
        Assert.False(memory.TryRecall(Key(2, MemoryKind.Opened), out _));
        Assert.Equal(3, memory.Entries.Count);
    }

    [Fact]
    public void AgingForgetsAMemoryOnTheLastMapLoadItLasts()
    {
        var memory = new MapObjectMemory();
        memory.Remember(Key(0, MemoryKind.Beaten), 0, MapObjectMemory.Short);
        memory.Remember(Key(1), 40);

        for (var load = 1; load <= 2; load++)
        {
            memory.Age();
            Assert.NotNull(memory.Find(Key(0, MemoryKind.Beaten)));
        }
        memory.Age();
        Assert.Null(memory.Find(Key(0, MemoryKind.Beaten)));
        Assert.Equal(7, memory.Find(Key(1))!.Lifetime);

        for (var load = 4; load <= 9; load++)
            memory.Age();
        Assert.Equal(1, memory.Find(Key(1))!.Lifetime);
        memory.Age();
        Assert.Empty(memory.Entries);
    }

    [Fact]
    public void RecallingBetweenMapLoadsRestartsTheCount()
    {
        var memory = new MapObjectMemory();
        memory.Remember(Key(1), 40);
        for (var load = 0; load < 9; load++)
            memory.Age();
        Assert.Equal(1, memory.Find(Key(1))!.Lifetime);

        Assert.True(memory.TryRecall(Key(1), out _));
        for (var load = 0; load < 9; load++)
            memory.Age();
        Assert.NotNull(memory.Find(Key(1)));
    }

    [Fact]
    public void AFullChartForgetsTheBeatenPartiesFirstToMakeRoom()
    {
        var memory = Full(MemoryKind.Beaten, MemoryKind.RandomLine, MemoryKind.SkillCheck);
        var beaten = memory.Entries.Count(e => e.Key.Kind == MemoryKind.Beaten);
        Assert.True(beaten > 0);

        var added = memory.Remember(Key(30, MemoryKind.Opened), 1);

        Assert.DoesNotContain(memory.Entries, e => e.Key.Kind == MemoryKind.Beaten);
        Assert.Equal(MapObjectMemory.Slots - beaten + 1, memory.Entries.Count);
        Assert.Same(added, memory.Entries[^1]);
        Assert.Contains(memory.Entries, e => e.Key.Kind == MemoryKind.RandomLine);
        Assert.Contains(memory.Entries, e => e.Key.Kind == MemoryKind.SkillCheck);
    }

    [Fact]
    public void AFullChartWithNoBeatenPartiesForgetsTheRandomLinesToMakeRoom()
    {
        var memory = Full(MemoryKind.RandomLine, MemoryKind.SkillCheck, MemoryKind.Opened);
        var lines = memory.Entries.Count(e => e.Key.Kind == MemoryKind.RandomLine);
        Assert.True(lines > 0);

        memory.Remember(Key(30, MemoryKind.Beaten), 0);

        Assert.DoesNotContain(memory.Entries, e => e.Key.Kind == MemoryKind.RandomLine);
        Assert.Equal(MapObjectMemory.Slots - lines + 1, memory.Entries.Count);
        Assert.Contains(memory.Entries, e => e.Key.Kind == MemoryKind.SkillCheck);
        Assert.Contains(memory.Entries, e => e.Key.Kind == MemoryKind.Opened);
    }

    [Fact]
    public void AFullChartOfNeitherForgetsEverythingToMakeRoom()
    {
        var memory = Full(MemoryKind.SkillCheck, MemoryKind.Opened);

        var added = memory.Remember(Key(30, MemoryKind.Beaten), 0);

        Assert.Same(added, Assert.Single(memory.Entries));
    }

    [Fact]
    public void ForgettingTheBeatenPartiesLeavesRoomForTheNextMemoryToo()
    {
        var memory = Full(MemoryKind.Beaten, MemoryKind.Opened);
        var beaten = memory.Entries.Count(e => e.Key.Kind == MemoryKind.Beaten);
        memory.Remember(Key(30), 1);
        memory.Remember(Key(31), 1);

        Assert.Equal(MapObjectMemory.Slots - beaten + 2, memory.Entries.Count);
        Assert.Contains(memory.Entries, e => e.Key == Key(30));
        Assert.Contains(memory.Entries, e => e.Key == Key(31));
        Assert.DoesNotContain(memory.Entries, e => e.Key.Kind == MemoryKind.Beaten);
    }

    [Fact]
    public void ForgettingOneMemoryLeavesTheRest()
    {
        var memory = new MapObjectMemory();
        memory.Remember(Key(1), 1);
        memory.Remember(Key(2), 2);

        Assert.True(memory.Forget(Key(1)));
        Assert.False(memory.Forget(Key(1)));
        Assert.Equal([Key(2)], memory.Entries.Select(e => e.Key));
    }

    [Fact]
    public void ClearingForgetsEverythingAndStartsTheMissesAgain()
    {
        var memory = Full(MemoryKind.SkillCheck, MemoryKind.Opened);
        memory.Remember(Key(30), 1);
        Assert.Single(memory.Entries);

        memory.Clear();
        Assert.Empty(memory.Entries);

        // A chart that was cleared fills again from empty, and a miss there starts from the beaten parties again.
        for (var i = 0; i < MapObjectMemory.Slots; i++)
            memory.Remember(Key(i, i == 0 ? MemoryKind.Beaten : MemoryKind.SkillCheck), i);
        memory.Remember(Key(30), 1);
        Assert.Equal(MapObjectMemory.Slots, memory.Entries.Count);
        Assert.DoesNotContain(memory.Entries, e => e.Key.Kind == MemoryKind.Beaten);
        Assert.Contains(memory.Entries, e => e.Key == Key(30));
    }
}
