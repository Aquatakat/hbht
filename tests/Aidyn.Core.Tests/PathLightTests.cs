using System.Numerics;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Rng;
using Aidyn.Core.Scenes;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using CharacterParty = Aidyn.Core.Characters.Party;

namespace Aidyn.Core.Tests;

/// <summary>Oriana's path lights go out as the player reaches them (<c>RenderVoxelScenes</c>).</summary>
public class PathLightTests
{
    private static readonly RecordId MapId = RecordId.Hex(RecordTypes.Map, 0x0FDB);
    private static readonly RecordId Other = RecordId.Hex(RecordTypes.Map, 0x100D);

    /// <summary>A light as I08's are: an empty scene to 5, the billboard to 24, radius 24.</summary>
    private static SceneObject Light(int index, Vector3 position, bool goesOut = true) =>
        new(index, position, 24, PakVariant.Any, new FlagGate(RecordId.Decimal(RecordTypes.Flag, 0), Invert: true),
            [new SceneModel(RecordId.Hex(RecordTypes.Scene, 0x1910), 5), new SceneModel(RecordId.Hex(RecordTypes.Scene, 0x374A), 24)],
            2, Vector3.Zero, Vector3.One, new Rgba(153, 236, 255, 255), SceneObjectFlags.Solid | SceneObjectFlags.Tint)
        {
            GoesOut = goesOut,
        };

    private static LoadedMap Load(MapObjectMemory memory, RecordId? id = null, params MapObject[] objects) =>
        LoadedMap.Activate(new MapRecord(id ?? MapId, objects), "chunk", ItemData.Flags(), memory, ItemData.NewParty(), new Lcg());

    /// <summary>The player at <paramref name="focus"/>, the camera 5 behind him along +Z, looking along −Z.</summary>
    private static SceneView View(Vector3 focus) =>
        new(focus, new Vector2(focus.X, focus.Z + 5), new Vector2(0, -1), 5, ViewCone.Original);

    [Fact]
    public void ALightGoesOutWithin3AndIsRememberedFor16MapLoads()
    {
        var memory = new MapObjectMemory();
        var map = Load(memory, null, Light(0, new Vector3(0, 0, 40)), Light(1, new Vector3(10, 0, 40)), Light(2, new Vector3(20, 0, 40), goesOut: false));

        // 3.5 away: the billboard is drawn, fading in, so it stays.
        Assert.Empty(map.PutOut(View(new Vector3(0, 0, 43.5f)), memory, MapId));
        // 2.5 away: only the empty near model is drawn, so it goes out.
        var putOut = map.PutOut(View(new Vector3(0, 0, 42.5f)), memory, MapId);
        Assert.Equal([0], putOut.Select(o => o.Index));
        Assert.False(map.IsActive(0));
        Assert.True(map.IsActive(1));
        var entry = Assert.Single(memory.Entries);
        Assert.Equal((MapRecord.VoxelId(MapId, 0), MemoryKind.PutOut, 16), (entry.Key.MapObject, entry.Key.Kind, entry.Lifetime));

        // Walking away doesn't bring it back, and an object that doesn't go out never does.
        Assert.Empty(map.PutOut(View(new Vector3(0, 0, 50)), memory, MapId));
        Assert.False(map.IsActive(0));
        Assert.Empty(map.PutOut(View(new Vector3(20, 0, 42.5f)), memory, MapId));
        Assert.True(map.IsActive(2));
    }

    [Fact]
    public void ALightOutOfViewOrBeyondItsRadiusStays()
    {
        var memory = new MapObjectMemory();
        var map = Load(memory, null, Light(0, new Vector3(0, 0, 40)));
        // Behind the camera, which looks away from it.
        var behind = new SceneView(new Vector3(0, 0, 42), new Vector2(0, 37), new Vector2(0, -1), 5, ViewCone.Original);
        Assert.Empty(map.PutOut(behind, memory, MapId));
        Assert.True(map.IsActive(0));
    }

    [Fact]
    public void ARememberedLightStaysOutWhenItsChunkLoadsAgainUntilItsMemoryAges()
    {
        var memory = new MapObjectMemory();
        var light = Light(0, new Vector3(0, 0, 40));
        Load(memory, null, light).PutOut(View(new Vector3(0, 0, 42)), memory, MapId);

        var again = Load(memory, null, light);
        Assert.False(again.IsActive(0));
        Assert.False(again.Regate(ItemData.Flags(), memory));
        Assert.False(again.IsActive(0));

        for (var i = 0; i < MapObjectMemory.PutOutLoads - 1; i++)
            memory.Age();
        Assert.False(Load(memory, null, light).IsActive(0));
        memory.Age();
        Assert.True(Load(memory, null, light).IsActive(0));
    }

    [Fact]
    public void TheMemoryIsKeyedUnderThePlayersChunkWhereverTheLightIs()
    {
        // As the original keys it: a light in a neighbour, reached from the player's chunk, is remembered under
        // the player's chunk with the light's own index, so its own chunk brings it back when it loads again.
        var memory = new MapObjectMemory();
        var light = Light(3, new Vector3(0, 0, 40));
        Load(memory, Other, light).PutOut(View(new Vector3(0, 0, 42)), memory, MapId);

        Assert.Equal(MapRecord.VoxelId(MapId, 3), Assert.Single(memory.Entries).Key.MapObject);
        Assert.True(Load(memory, Other, light).IsActive(3));
        Assert.False(Load(memory, MapId, Light(3, new Vector3(50, 0, 50), goesOut: false)).IsActive(3));
    }
}
