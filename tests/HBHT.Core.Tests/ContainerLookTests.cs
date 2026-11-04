using System.Numerics;
using HBHT.Core.MapObjects;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>Which model a container's linked scene object shows by the flags (<see cref="LoadedMap.LookOf"/>, <see cref="LoadedMap.Looks"/>).</summary>
public class ContainerLookTests
{
    private static readonly RecordId Map = RecordId.Parse("aidyn:map/0x1063");

    private static Func<RecordId, bool> Set(params int[] flags) => id => flags.Any(f => Flag(f) == id);

    private static SceneObject Scene(int index) =>
        new(index, Vector3.Zero, 1, new FlagGate(Flag(0)), [], Vector3.Zero, Vector3.One, default, SceneObjectFlags.None);

    // A container whose opened flag is 13 and exploded flag 14, linked to the scene object after it.
    private static ContainerObject Container(int index, ContainerKind kind = ContainerKind.Chest) =>
        new(index, Vector3.Zero, 5, null, kind, Flag(13), Flag(14), 0, 0, null, 0, null) { Link = MapRecord.VoxelId(Map, index + 1) };

    [Fact]
    public void AnOpenedFlagShowsTheOpenedModelAndAnExplodedOneTheDestroyed()
    {
        var chest = Container(0);
        var memory = new MapObjectMemory();

        Assert.Equal(ContainerLook.Closed, LoadedMap.LookOf(Map, chest, Set(), memory));
        Assert.Equal(ContainerLook.Opened, LoadedMap.LookOf(Map, chest, Set(13), memory));
        Assert.Equal(ContainerLook.Destroyed, LoadedMap.LookOf(Map, chest, Set(14), memory));
        // Opening is looked at first.
        Assert.Equal(ContainerLook.Opened, LoadedMap.LookOf(Map, chest, Set(13, 14), memory));
    }

    [Fact]
    public void AnEmptiedContainerRememberedByTheMapObjectsShowsOpenedToo()
    {
        var memory = new MapObjectMemory();
        memory.Remember(new Memory(MapRecord.VoxelId(Map, 0), MemoryKind.Opened), 0);

        Assert.Equal(ContainerLook.Opened, LoadedMap.LookOf(Map, Container(0), Set(), memory));
        Assert.Equal(ContainerLook.Closed, LoadedMap.LookOf(Map, Container(2), Set(), memory));
    }

    [Fact]
    public void AHerbSourceHasNoLook()
    {
        Assert.Null(LoadedMap.LookOf(Map, Container(0, ContainerKind.Herb), Set(13, 14), new MapObjectMemory()));
    }

    [Fact]
    public void LooksNameTheLinkedSceneObjectsOfTheChangedContainers()
    {
        var record = new MapRecord(Map, [Container(0), Scene(1), Container(2), Scene(3), Container(4, ContainerKind.Herb), Scene(5)]);

        Assert.Empty(LoadedMap.Looks(record, Set(), new MapObjectMemory()));
        Assert.Equal(new Dictionary<int, ContainerLook> { [1] = ContainerLook.Opened, [3] = ContainerLook.Opened },
            LoadedMap.Looks(record, Set(13), new MapObjectMemory()));
        Assert.Equal(new Dictionary<int, ContainerLook> { [1] = ContainerLook.Destroyed, [3] = ContainerLook.Destroyed },
            LoadedMap.Looks(record, Set(14), new MapObjectMemory()));
        // Container 0 alone is remembered as emptied.
        Assert.Equal(new Dictionary<int, ContainerLook> { [1] = ContainerLook.Opened }, LoadedMap.Looks(record, Set(), Remembered(0)));
    }

    private static MapObjectMemory Remembered(int index)
    {
        var memory = new MapObjectMemory();
        memory.Remember(new Memory(MapRecord.VoxelId(Map, index), MemoryKind.Opened), 0);
        return memory;
    }
}
