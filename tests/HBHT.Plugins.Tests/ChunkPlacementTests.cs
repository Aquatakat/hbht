using System.Reflection;
using System.Text.Json.Serialization;
using HBHT.Plugins.Records;

namespace HBHT.Plugins.Tests;

/// <summary>Where a chunk sits and what a map object is, as every tool says them (ADR 0035).</summary>
public sealed class ChunkPlacementTests : IDisposable
{
    private static readonly RecordId Patio = RecordId.Parse("aidyn:chunk/0/I08");
    private static readonly RecordId Battle = RecordId.Parse("aidyn:chunk/1/C03");
    private static readonly RecordId Far = RecordId.Parse("aidyn:chunk/1/27_4");
    private static readonly RecordId Lonely = RecordId.Parse("aidyn:chunk/0/A01");
    private static readonly RecordId Absent = RecordId.Parse("aidyn:chunk/0/Z26");

    private readonly TempPlugins _temp = new();
    private readonly PluginSet _set;
    private readonly PluginCatalog _catalog;

    public ChunkPlacementTests()
    {
        var dir = _temp.Add("aidyn", null,
            ("mapsets.json", """
                [
                  { "id": "aidyn:mapset/0", "label": "Overworld", "cellSize": 100 },
                  { "id": "aidyn:mapset/1", "label": "Battlefields", "cellSize": 100 },
                ]
                """),
            ("entities.json", """[{ "id": "aidyn:entity/0x023F", "name": "Alaron" }]"""),
            ("chunks.json", """
                [
                  { "id": "aidyn:chunk/0/I08", "mapSet": "aidyn:mapset/0", "row": 9, "column": 8, "terrain": "aidyn:terrain/1", "objects": [
                    { "type": "teleporter", "index": 0, "name": "patioL2", "arrival": "aidyn:mapobject/1/C03/0" },
                    { "type": "encounter", "index": 1, "entity": "aidyn:entity/0x023F" },
                    { "type": "script", "index": 2, "name": "stagenorth", "plays": "aidyn:script/0x0364" },
                    { "type": "savePoint", "index": 3 },
                    { "type": "wanderNode", "index": 4 },
                    { "type": "wanderNode", "index": 5 },
                    { "type": "scene", "index": 12, "models": [{ "model": "aidyn:scene/0x0B48" }] },
                  ] },
                  { "id": "aidyn:chunk/0/A01", "mapSet": "aidyn:mapset/0", "row": 1, "column": 1, "terrain": "aidyn:terrain/1", "objects": [] },
                  { "id": "aidyn:chunk/1/C03", "mapSet": "aidyn:mapset/1", "row": 3, "column": 3, "terrain": "aidyn:terrain/2", "objects": [
                    { "type": "npc", "index": 0, "entity": "aidyn:entity/0x023F", "script": "aidyn:script/0x0364" },
                  ] },
                  { "id": "aidyn:chunk/1/27_4", "mapSet": "aidyn:mapset/1", "row": 27, "column": 4, "terrain": "aidyn:terrain/2", "objects": [] },
                ]
                """));
        _set = PluginSet.Load([dir]);
        _catalog = PluginCatalog.Build(_set);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AMapObjectIdNamesItsChunkAndIndex()
    {
        var id = ChunkRecord.ObjectId(Patio, 12);
        Assert.Equal("aidyn:mapobject/0/I08/12", id.ToString());
        Assert.Equal(Patio, ChunkRecord.ChunkOf(id));
        Assert.Equal(12, ChunkRecord.IndexOf(id));
        Assert.Equal(0, ChunkRecord.IndexOf(ChunkRecord.ObjectId(Patio, 0)));

        Assert.IsType<SceneObject>(ChunkRecord.Find(_set, id));
        Assert.Null(ChunkRecord.Find(_set, ChunkRecord.ObjectId(Patio, 13)));
        Assert.Null(ChunkRecord.Find(_set, ChunkRecord.ObjectId(Absent, 0)));
    }

    [Fact]
    public void AChunkKnowsItsPlace()
    {
        Assert.Equal("Overworld I08", ChunkPlace.Of(_set, Patio)!.ToString());
        Assert.Equal("Battlefields C03", ChunkPlace.Of(_set, Battle)!.ToString());
        // A row past Z is named by its numbers.
        Assert.Equal("Battlefields 27_4", ChunkPlace.Of(_set, Far)!.ToString());
        Assert.Null(ChunkPlace.Of(_set, Absent));

        var index = ChunkIndex.For(_set);
        Assert.Same(index, ChunkIndex.For(_set));
        Assert.Equal(Patio, index.At(RecordId.Parse("aidyn:mapset/0"), 9, 8));
        Assert.Null(index.At(RecordId.Parse("aidyn:mapset/0"), 9, 9));
        Assert.Equal([Lonely, Patio], index.In(RecordId.Parse("aidyn:mapset/0")));
        Assert.Equal([Battle, Far], index.In(RecordId.Parse("aidyn:mapset/1")));
        Assert.Equal(new ChunkCell(RecordId.Parse("aidyn:mapset/1"), 27, 4), index.CellOf(Far));
        Assert.Equal(Patio, _set.ChunkAt(RecordId.Parse("aidyn:mapset/0"), 9, 8)!.Id);
        Assert.Equal([Lonely, Patio], _set.ChunksIn(RecordId.Parse("aidyn:mapset/0")).Select(c => c.Id));
    }

    [Fact]
    public void AMapObjectIsDescribedTheSameWayEverywhere()
    {
        string Object(RecordId chunk, int index) => _catalog.Describe(ChunkRecord.ObjectId(chunk, index));

        Assert.Equal("Teleporter patioL2, in Overworld I08", Object(Patio, 0));
        Assert.Equal("Encounter: Alaron, in Overworld I08", Object(Patio, 1));
        Assert.Equal("Script object stagenorth, in Overworld I08", Object(Patio, 2));
        Assert.Equal("Save point, in Overworld I08", Object(Patio, 3));
        Assert.Equal("Wander node, in Overworld I08", Object(Patio, 4));
        Assert.Equal("Scene object: aidyn:scene/0x0B48, in Overworld I08", Object(Patio, 12));
        Assert.Equal("Object, in Overworld I08", Object(Patio, 13));
        Assert.Equal("Npc: Alaron, in Battlefields C03", Object(Battle, 0));
        Assert.Equal("Object, in chunk 0/Z26", Object(Absent, 0));

        // The chunk list counts the same kinds by the same names.
        Assert.Equal("Overworld I08, 1 teleporter, 1 encounter, 1 script, 1 save point, 2 wander nodes, 1 scene",
            _catalog.Find(Patio)!.Summary);
        Assert.Equal("Overworld A01, no objects", _catalog.Find(Lonely)!.Summary);
    }

    [Fact]
    public void EveryKindOfMapObjectHasAName()
    {
        var derived = typeof(MapObject).GetCustomAttributes<JsonDerivedTypeAttribute>().ToList();
        Assert.Equal(derived.Select(d => (d.DerivedType, (string?)d.TypeDiscriminator)),
            MapObjectKinds.All.Select(k => (k.Type, (string?)k.Discriminator)));
        Assert.Equal(typeof(ChunkRecord).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(MapObject))).Order(Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name))),
            derived.Select(d => d.DerivedType).Order(Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name))));
        Assert.Equal(MapObjectKinds.All.Count, MapObjectKinds.All.Select(k => k.Name).Distinct().Count());
        Assert.Equal("Encounter", MapObjectKinds.Name("encounter"));
        Assert.Equal("Npc", MapObjectKinds.Name("npc"));
        Assert.Equal("somethingNew", MapObjectKinds.Name("somethingNew"));
    }
}
