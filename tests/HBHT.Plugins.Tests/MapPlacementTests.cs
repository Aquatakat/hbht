using System.Reflection;
using System.Text.Json.Serialization;
using HBHT.Plugins.Records;

namespace HBHT.Plugins.Tests;

/// <summary>Where a map sits and what a map object is, as every tool says them.</summary>
public sealed class MapPlacementTests : IDisposable
{
    private static readonly RecordId Patio = RecordId.Parse("aidyn:map/0x0E87");
    private static readonly RecordId Lonely = RecordId.Parse("aidyn:map/0x0E99");

    private readonly TempPlugins _temp = new();
    private readonly PluginSet _set;
    private readonly PluginCatalog _catalog;

    public MapPlacementTests()
    {
        var dir = _temp.Add("aidyn", null,
            ("mapsets.json", """
                [
                  { "id": "aidyn:mapset/0", "label": "Overworld", "cellSize": 100, "chunks": [
                    { "row": 9, "column": 8, "map": "aidyn:map/0x0E87" },
                    { "row": 1, "column": 1, "map": "aidyn:map/0x0E88" } ] },
                  { "id": "aidyn:mapset/1", "label": "Battlefields", "cellSize": 100, "chunks": [
                    { "row": 3, "column": 3, "map": "aidyn:map/0x0E87" },
                    { "row": 27, "column": 4, "map": "aidyn:map/0x0E87" } ] },
                ]
                """),
            ("entities.json", """[{ "id": "aidyn:entity/0x023F", "name": "Alaron" }]"""),
            ("maps.json", """
                [
                  { "id": "aidyn:map/0x0E87", "objects": [
                    { "type": "teleporter", "index": 0, "name": "patioL2" },
                    { "type": "monsterParty", "index": 1, "entity": "aidyn:entity/0x023F" },
                    { "type": "dialogue", "index": 2, "name": "stagenorth", "script": "aidyn:dialogue/0x0364" },
                    { "type": "savePoint", "index": 3 },
                    { "type": "wanderNode", "index": 4 },
                    { "type": "wanderNode", "index": 5 },
                    { "type": "scene", "index": 12, "models": [{ "model": "aidyn:scene/0x0B48" }] },
                  ] },
                  { "id": "aidyn:map/0x0E88", "objects": [] },
                  { "id": "aidyn:map/0x0E99", "objects": [] },
                ]
                """));
        _set = PluginSet.Load([dir]);
        _catalog = PluginCatalog.Build(_set);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AVoxelIdNamesItsMapAndIndex()
    {
        var voxel = MapRecord.VoxelId(Patio, 12);
        Assert.Equal("aidyn:voxel/0x0E87/12", voxel.ToString());
        Assert.Equal(Patio, MapRecord.MapOf(voxel));
        Assert.Equal(12, MapRecord.IndexOf(voxel));
        Assert.Equal(0, MapRecord.IndexOf(MapRecord.VoxelId(Patio, 0)));

        Assert.IsType<SceneObject>(MapRecord.Find(_set, voxel));
        Assert.Null(MapRecord.Find(_set, MapRecord.VoxelId(Patio, 13)));
        Assert.Null(MapRecord.Find(_set, MapRecord.VoxelId(RecordId.Parse("aidyn:map/0x0001"), 0)));
    }

    [Fact]
    public void AMapIsPlacedByEveryChunkThatNamesIt()
    {
        var placements = MapPlacements.For(_set);
        Assert.Same(placements, MapPlacements.For(_set));

        // By map set, then in the set's chunk order. ChunkEntry.Name spells the row as a letter, or as numbers past Z.
        Assert.Equal(["Overworld I08", "Battlefields C03", "Battlefields 27,4"], placements.All(Patio).Select(p => p.ToString()));
        Assert.Equal("Overworld I08", placements.First(Patio)!.ToString());
        Assert.Equal(_set.Get<MapSetRecord>(RecordId.Parse("aidyn:mapset/0")).Chunks[0], placements.First(Patio)!.Chunk);
        Assert.Equal(["Overworld A01"], placements.All(RecordId.Parse("aidyn:map/0x0E88")).Select(p => p.ToString()));

        Assert.Empty(placements.All(Lonely));
        Assert.Null(placements.First(Lonely));
    }

    [Fact]
    public void AMapObjectIsDescribedTheSameWayEverywhere()
    {
        string Voxel(int index) => _catalog.Describe(MapRecord.VoxelId(Patio, index));

        Assert.Equal("Teleporter patioL2, in Overworld I08 / Battlefields C03 / Battlefields 27,4", Voxel(0));
        Assert.Equal("Monster party: Alaron, in Overworld I08 / Battlefields C03 / Battlefields 27,4", Voxel(1));
        Assert.Equal("Dialogue object stagenorth, in Overworld I08 / Battlefields C03 / Battlefields 27,4", Voxel(2));
        Assert.Equal("Save point, in Overworld I08 / Battlefields C03 / Battlefields 27,4", Voxel(3));
        Assert.Equal("Wander node, in Overworld I08 / Battlefields C03 / Battlefields 27,4", Voxel(4));
        Assert.Equal("Scene object: aidyn:scene/0x0B48, in Overworld I08 / Battlefields C03 / Battlefields 27,4", Voxel(12));
        Assert.Equal("Object, in Overworld I08 / Battlefields C03 / Battlefields 27,4", Voxel(13));
        Assert.Equal("Object, in map 0x0E99", _catalog.Describe(MapRecord.VoxelId(Lonely, 0)));

        // The map list counts the same kinds by the same names.
        Assert.Equal("Overworld I08, Battlefields C03, Battlefields 27,4, 1 teleporter, 1 monster party, 1 dialogue, 1 save point, 2 wander nodes, 1 scene",
            _catalog.Find(Patio)!.Summary);
        Assert.Equal("Overworld A01, no objects", _catalog.Find(RecordId.Parse("aidyn:map/0x0E88"))!.Summary);
    }

    [Fact]
    public void EveryKindOfMapObjectHasAName()
    {
        var derived = typeof(MapObject).GetCustomAttributes<JsonDerivedTypeAttribute>().ToList();
        Assert.Equal(derived.Select(d => (d.DerivedType, (string?)d.TypeDiscriminator)),
            MapObjectKinds.All.Select(k => (k.Type, (string?)k.Discriminator)));
        Assert.Equal(typeof(MapRecord).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(MapObject))).Order(Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name))),
            derived.Select(d => d.DerivedType).Order(Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name))));
        Assert.Equal(MapObjectKinds.All.Count, MapObjectKinds.All.Select(k => k.Name).Distinct().Count());
        Assert.Equal("Monster party", MapObjectKinds.Name("monsterParty"));
        Assert.Equal("somethingNew", MapObjectKinds.Name("somethingNew"));
    }
}
