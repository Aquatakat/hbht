using System.Text.Json;
using System.Text.Json.Nodes;
using HBHT.Plugins.Records;

namespace HBHT.Plugins.Tests;

public sealed class PluginSetTests : IDisposable
{
    private readonly TempPlugins _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Plugin(string id, string[]? dependencies, params (string File, string Json)[] files) => _temp.Add(id, dependencies, files);

    private string Base() => Plugin("aidyn", null, ("weapons.json", """
        [
          // Comments and trailing commas are allowed.
          { "id": "aidyn:item/0x0712", "name": "Club", "damage": 3, "stats": { "hit": 5, "range": 1 }, },
          { "id": "aidyn:item/0x0713", "name": "Mace", "damage": 5 },
        ]
        """));

    [Fact]
    public void PatchesMergeFieldByField()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("club.json", """{ "patch": "aidyn:item/0x0712", "damage": 9, "stats": { "range": null } }""")),
        ]);

        var club = set.GetJson(RecordId.Parse("aidyn:item/0x0712"))!;
        Assert.Equal(9, (int)club["damage"]!);
        Assert.Equal("Club", (string)club["name"]!);
        Assert.Equal(5, (int)club["stats"]!["hit"]!);
        Assert.False(club["stats"]!.AsObject().ContainsKey("range"));
    }

    [Fact]
    public void FieldsSetByTwoPluginsAreReported()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("one", ["aidyn"], ("a.json", """{ "patch": "aidyn:item/0x0712", "damage": 9 }""")),
            Plugin("two", ["aidyn"], ("b.json", """{ "patch": "aidyn:item/0x0712", "damage": 7, "name": "Big club" }""")),
        ]);

        // The base sets every field of its records, and "name" patched by one mod is a patch, not a conflict.
        var conflict = Assert.Single(set.Conflicts);
        Assert.Equal("damage", conflict.Path);
        Assert.Equal(["one", "two"], conflict.Plugins);
        Assert.Equal("two", conflict.Winner);
        Assert.Equal(7, (int)set.GetJson(RecordId.Parse("aidyn:item/0x0712"))!["damage"]!);
    }

    private const string MapWithThreeObjects = """
        [{ "id": "aidyn:chunk/0/I08", "mapSet": "aidyn:mapset/0", "row": 9, "column": 8, "terrain": "aidyn:terrain/1", "objects": [
          { "type": "container", "index": 0, "position": [1, 0, 1], "radius": 1, "kind": "Chest", "loot": "aidyn:loot/0x0401", "reach": 2 },
          { "type": "savePoint", "index": 1, "position": [2, 0, 2], "radius": 1 },
          { "type": "scene", "index": 2, "position": [3, 0, 3], "radius": 1, "models": [{ "model": "aidyn:scene/0x0B48", "drawDistance": 15 }] },
        ] }]
        """;

    [Fact]
    public void KeyedListsMergeElementByElement()
    {
        var set = PluginSet.Load([
            Plugin("aidyn", null, ("chunks.json", MapWithThreeObjects)),
            Plugin("mymod", ["aidyn"], ("map.json", """
                { "patch": "aidyn:chunk/0/I08", "objects": [
                  { "index": 0, "reach": 5 },
                  { "index": 1, "$remove": true },
                  { "type": "savePoint", "index": 7, "position": [9, 0, 9], "radius": 2 },
                ] }
                """)),
        ]);
        var id = RecordId.Parse("aidyn:chunk/0/I08");
        var objects = (JsonArray)set.GetJson(id)!["objects"]!;

        // Object 0 keeps every field but the one patched, 1 is gone, 2 survives untouched and 7 is added after it.
        Assert.Equal([0, 2, 7], objects.Select(o => (int)o!["index"]!));
        Assert.Equal(5, (int)objects[0]!["reach"]!);
        Assert.Equal("Chest", (string)objects[0]!["kind"]!);
        Assert.Equal("aidyn:loot/0x0401", (string)objects[0]!["loot"]!);
        Assert.Equal(15, (int)objects[1]!["models"]![0]!["drawDistance"]!);
        Assert.False(objects[2]!.AsObject().ContainsKey("$remove"));

        var map = set.Get<ChunkRecord>(id);
        Assert.Equal(5, Assert.IsType<ContainerObject>(map.Objects[0]).Reach);
        Assert.IsType<SavePointObject>(map.Objects[2]);

        // The setters are kept per element and field, and a field's include whatever set what encloses it.
        Assert.Equal(["aidyn", "mymod"], set.Setters(id, "objects[0].reach"));
        Assert.Equal(["aidyn"], set.Setters(id, "objects[0].kind"));
        Assert.Equal(["aidyn", "mymod"], set.Setters(id, "objects[1]"));
        Assert.Equal(["aidyn", "mymod"], set.Setters(id, "objects[7].position"));
        Assert.Equal(["aidyn"], set.Setters(id, "objects"));
        Assert.Empty(set.Conflicts);
    }

    [Fact]
    public void KeyedElementsPatchedByTwoModsConflictAndOthersDoNot()
    {
        var set = PluginSet.Load([
            Plugin("aidyn", null, ("chunks.json", MapWithThreeObjects)),
            Plugin("one", ["aidyn"], ("map.json", """{ "patch": "aidyn:chunk/0/I08", "objects": [{ "index": 0, "reach": 5 }, { "index": 2, "radius": 3 }] }""")),
            Plugin("two", ["aidyn"], ("map.json", """{ "patch": "aidyn:chunk/0/I08", "objects": [{ "index": 0, "reach": 6 }, { "index": 1, "$remove": true }] }""")),
        ]);
        var conflict = Assert.Single(set.Conflicts);
        Assert.Equal("objects[0].reach", conflict.Path);
        Assert.Equal(["one", "two"], conflict.Plugins);
        Assert.Equal(6, (int)set.GetJson(RecordId.Parse("aidyn:chunk/0/I08"))!["objects"]![0]!["reach"]!);
    }

    [Fact]
    public void CompositeKeysAndNestedObjectsInKeyedElementsMerge()
    {
        var set = PluginSet.Load([
            Plugin("aidyn", null, ("mapsets.json", """
                [{ "id": "aidyn:mapset/0", "label": "Overworld", "cellSize": 100, "spawns": [
                  { "portal": 0, "row": 9, "column": 8, "position": [1, 2] },
                  { "portal": 1, "row": 9, "column": 9, "position": [3, 4] },
                ] }]
                """), ("chunks.json", """
                [{ "id": "aidyn:chunk/0/I08", "mapSet": "aidyn:mapset/0", "row": 9, "column": 8, "terrain": "aidyn:terrain/1", "music": "aidyn:sequence/0x0001", "objects": [] }]
                """)),
            Plugin("mymod", ["aidyn"], ("mapset.json", """
                { "patch": "aidyn:mapset/0", "spawns": [{ "portal": 0, "column": 9 }, { "portal": 1, "position": [5, 6] }] }
                """), ("chunk.json", """
                { "patch": "aidyn:chunk/0/I08", "music": null, "terrain": "aidyn:terrain/4" }
                """)),
        ]);
        var mapSet = set.Get<MapSetRecord>(RecordId.Parse("aidyn:mapset/0"));
        Assert.Equal(2, mapSet.Spawns!.Count);
        Assert.Equal(9, mapSet.Spawns[0].Column);
        Assert.Equal(new System.Numerics.Vector2(1, 2), mapSet.Spawns[0].Position);
        Assert.Equal(new System.Numerics.Vector2(5, 6), mapSet.Spawns[1].Position);
        Assert.Equal(["aidyn", "mymod"], set.Setters(mapSet.Id, "spawns[0].column"));
        Assert.Equal(["aidyn"], set.Setters(mapSet.Id, "spawns[0].row"));

        var chunk = set.Get<ChunkRecord>(RecordId.Parse("aidyn:chunk/0/I08"));
        Assert.Null(chunk.Music);
        Assert.Equal("aidyn:terrain/4", chunk.Terrain.ToString());
        Assert.Equal(["aidyn", "mymod"], set.Setters(chunk.Id, "music"));
    }

    [Fact]
    public void UnkeyedListsAreReplacedWhole()
    {
        var set = PluginSet.Load([
            Plugin("aidyn", null, ("chunks.json", MapWithThreeObjects)),
            Plugin("mymod", ["aidyn"], ("map.json", """
                { "patch": "aidyn:chunk/0/I08", "objects": [{ "index": 2, "models": [{ "model": "aidyn:scene/0x0B49", "drawDistance": 20 }] }] }
                """)),
        ]);
        var scene = Assert.IsType<SceneObject>(set.Get<ChunkRecord>(RecordId.Parse("aidyn:chunk/0/I08")).Objects[2]);
        var model = Assert.Single(scene.Models);
        Assert.Equal("aidyn:scene/0x0B49", model.Model.ToString());
        Assert.Equal(20, model.DrawDistance);

        // A list without a class, or a keyed list patched with something other than a list, is replaced too.
        var weapons = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("club.json", """{ "patch": "aidyn:item/0x0712", "tags": ["a"] }""")),
            Plugin("other", ["mymod"], ("club.json", """{ "patch": "aidyn:item/0x0712", "tags": ["b", "c"] }""")),
        ]);
        Assert.Equal(["b", "c"], ((JsonArray)weapons.GetJson(RecordId.Parse("aidyn:item/0x0712"))!["tags"]!).Select(t => (string)t!));
    }

    [Fact]
    public void AKeyedElementWithoutItsKeyIsRefused()
    {
        var error = Assert.Throws<InvalidDataException>(() => PluginSet.Load([
            Plugin("aidyn", null, ("chunks.json", MapWithThreeObjects)),
            Plugin("mymod", ["aidyn"], ("map.json", """{ "patch": "aidyn:chunk/0/I08", "objects": [{ "reach": 5 }] }""")),
        ]));
        Assert.Contains("keyed by index", error.Message, StringComparison.Ordinal);
        Assert.Contains("aidyn:chunk/0/I08", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEditorsSchemaPointerIsIgnored()
    {
        var set = PluginSet.Load([
            Plugin("aidyn", null, ("weapons.json", """{ "$schema": "weapon.schema.json", "id": "aidyn:item/0x0712", "name": "Club" }""")),
            Plugin("mymod", ["aidyn"], ("club.json", """{ "$schema": "weapon.patch.schema.json", "patch": "aidyn:item/0x0712", "name": "Big club" }""")),
        ]);
        var club = set.GetJson(RecordId.Parse("aidyn:item/0x0712"))!;
        Assert.False(club.ContainsKey("$schema"));
        Assert.Equal("Big club", (string)club["name"]!);
        Assert.Empty(set.Setters(RecordId.Parse("aidyn:item/0x0712"), "$schema"));
    }

    [Fact]
    public void ModsCanOnlyAddRecordsInTheirOwnNamespace()
    {
        var error = Assert.Throws<InvalidDataException>(() => PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("new.json", """{ "id": "aidyn:item/0x0799", "damage": 1 }""")),
        ]));
        Assert.Contains("own namespace", error.Message, StringComparison.Ordinal);

        Directory.Delete(_temp.Root, recursive: true);
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("new.json", """{ "id": "mymod:item/sword_of_tests", "damage": 1 }""")),
        ]);
        Assert.True(set.Contains(RecordId.Parse("mymod:item/sword_of_tests")));
    }

    [Fact]
    public void PatchesNeedATarget()
    {
        Assert.Throws<InvalidDataException>(() => PluginSet.Load([
            Base(), Plugin("mymod", ["aidyn"], ("p.json", """{ "patch": "aidyn:item/0x0799", "damage": 1 }""")),
        ]));
    }

    [Fact]
    public void DependenciesLoadFirst()
    {
        Assert.Throws<InvalidDataException>(() => PluginSet.Load([Plugin("mymod", ["aidyn"]), Base()]));
    }

    [Fact]
    public void RecordsAreDefinedOnce()
    {
        Assert.Throws<InvalidDataException>(() => PluginSet.Load([
            Plugin("aidyn", null, ("a.json", """[{ "id": "aidyn:flag/1" }, { "id": "aidyn:flag/1" }]""")),
        ]));
    }

    [Fact]
    public void AssetsComeFromTheLastPluginThatHasThem()
    {
        var baseDir = Base();
        var modDir = Plugin("mymod", ["aidyn"]);
        foreach (var dir in new[] { baseDir, modDir })
        {
            Directory.CreateDirectory(Path.Combine(dir, "assets", "aidyn", "scene"));
            File.WriteAllText(Path.Combine(dir, "assets", "aidyn", "scene", "0x0A0E.glb"), "");
        }
        Directory.CreateDirectory(Path.Combine(baseDir, "assets", "aidyn", "mapobject", "0", "I08"));
        File.WriteAllText(Path.Combine(baseDir, "assets", "aidyn", "mapobject", "0", "I08", "12.png"), "");

        var set = PluginSet.Load([baseDir, modDir]);
        Assert.StartsWith(modDir, set.FindAsset(RecordId.Parse("aidyn:scene/0x0A0E"), "glb"), StringComparison.Ordinal);
        Assert.StartsWith(baseDir, set.FindAsset(RecordId.Parse("aidyn:mapobject/0/I08/12"), "png"), StringComparison.Ordinal);
        Assert.Null(set.FindAsset(RecordId.Parse("aidyn:scene/0x0A0F"), "glb"));
    }

    [Fact]
    public void FieldsRememberWhichPluginsSetThem()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("club.json", """{ "patch": "aidyn:item/0x0712", "damage": 9 }""")),
        ]);
        var club = RecordId.Parse("aidyn:item/0x0712");
        Assert.Equal("aidyn", set.DefinedBy(club));
        Assert.Equal(["aidyn", "mymod"], set.Setters(club, "damage"));
        Assert.Equal(["aidyn"], set.Setters(club, "name"));
        Assert.Empty(set.Setters(club, "weight"));
        Assert.Null(set.DefinedBy(RecordId.Parse("aidyn:item/0x0799")));
    }

    [Fact]
    public void TheCatalogListsRecordsAndAssetsAndWhatNamesThem()
    {
        var baseDir = Plugin("aidyn", null,
            ("mapsets.json", """[{ "id": "aidyn:mapset/0", "label": "Overworld", "cellSize": 100 }]"""),
            ("chunks.json", """
                [{ "id": "aidyn:chunk/0/I08", "mapSet": "aidyn:mapset/0", "row": 9, "column": 8, "terrain": "aidyn:terrain/1",
                  "collision": "aidyn:collision/0x0E87", "objects": [
                  { "type": "scene", "index": 0, "visible": { "flag": "aidyn:flag/5" }, "models": [{ "model": "aidyn:scene/0x0B48" }] },
                  { "type": "scene", "index": 1, "visible": { "flag": "aidyn:flag/0" }, "models": [{ "model": "aidyn:scene/0x0B48" }] },
                ] }]
                """));
        var modDir = Plugin("mymod", ["aidyn"]);
        void Asset(string dir, string path, byte[] bytes)
        {
            var full = Path.Combine([dir, "assets", .. path.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        Asset(baseDir, "aidyn/scene/0x0B48.glb", []);
        Asset(baseDir, "aidyn/collision/0x0E87.glb", []);
        Asset(modDir, "aidyn/scene/0x0B48.glb", []);
        Asset(baseDir, "aidyn/scene/0x0B49.glb", []);
        Asset(baseDir, "aidyn/image/0x019F.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8, 0, 0, 0, 12, 0, 0, 0, 37]);
        Asset(baseDir, "aidyn/scene/not-an-id.glb", []);

        var catalog = PluginCatalog.Build(PluginSet.Load([baseDir, modDir]));

        Assert.Equal(["aidyn:chunk/0/I08", "aidyn:collision/0x0E87", "aidyn:image/0x019F", "aidyn:mapset/0", "aidyn:scene/0x0B48", "aidyn:scene/0x0B49"],
            catalog.Entries.Select(e => e.Id.ToString()));
        var chunk = catalog.Find(RecordId.Parse("aidyn:chunk/0/I08"))!;
        Assert.True(chunk.HasRecord);
        Assert.Null(chunk.Borg);
        Assert.Equal("Overworld I08, 2 scenes", chunk.Summary);
        // Its collision is an asset alone, keyed by the Borg9 it was imported from.
        var collision = catalog.Find(RecordId.Parse("aidyn:collision/0x0E87"))!;
        Assert.False(collision.HasRecord);
        Assert.Equal(0x0E87, collision.Borg);
        Assert.Equal("aidyn", collision.Asset("glb")!.Plugin);
        Assert.Equal("Overworld, 1 chunk", catalog.Find(RecordId.Parse("aidyn:mapset/0"))!.Summary);
        Assert.Null(catalog.Find(RecordId.Parse("aidyn:mapset/0"))!.Borg);

        var tree = catalog.Find(RecordId.Parse("aidyn:scene/0x0B48"))!;
        Assert.False(tree.HasRecord);
        Assert.Equal("mymod", tree.Asset("glb")!.Plugin);
        Assert.Equal("used by 2 records", tree.Summary);
        Assert.Equal(["objects[0].models[0].model", "objects[1].models[0].model"],
            catalog.ReferencesTo(tree.Id).Select(r => r.Path));
        Assert.Equal("used by nothing", catalog.Find(RecordId.Parse("aidyn:scene/0x0B49"))!.Summary);
        Assert.Equal("12×37", catalog.Find(RecordId.Parse("aidyn:image/0x019F"))!.Summary);

        // Records that no plugin defines yet, such as flags, still have their references.
        Assert.Null(catalog.Find(RecordId.Parse("aidyn:flag/5")));
        Assert.Equal("objects[0].visible.flag", Assert.Single(catalog.ReferencesTo(RecordId.Parse("aidyn:flag/5"))).Path);
    }

    [Fact]
    public void TypedRecordsAreReadOnceAndHoldTheMerge()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("club.json", """{ "patch": "aidyn:item/0x0712", "name": "Big club", "price": 9 }""")),
        ]);
        var club = RecordId.Parse("aidyn:item/0x0712");

        var read = set.Get<DbRecord>(club);
        Assert.Equal("Big club", read.Name);
        Assert.Same(read, set.Get<DbRecord>(club));
        Assert.Same(read, set.TryGet<DbRecord>(club));
        Assert.Same(read, set.All<DbRecord>(RecordTypes.Item)[0]);

        // The same record read as its class is that type's own object; a type the record doesn't read as is refused.
        var weapon = set.Get<ItemRecord>(club);
        Assert.Equal(9, weapon.Price);
        Assert.Same(weapon, set.Get<ItemRecord>(club));
        Assert.Null(set.TryGet<DbRecord>(RecordId.Parse("aidyn:item/0x0799")));
        Assert.Throws<ArgumentException>(() => set.TryGet<SpellRecord>(club));
        Assert.Throws<ArgumentException>(() => set.All<WeatherRecord>(RecordTypes.Combat));
        Assert.Throws<ArgumentException>(() => set.All<SkillRecord>());
        Assert.Same(set.All<ItemRecord>(RecordTypes.Item), set.All<ItemRecord>());
    }

    [Fact]
    public void AllOfATypeIsInIdOrder()
    {
        var set = PluginSet.Load([
            Plugin("aidyn", null,
                ("b.json", """[{ "id": "aidyn:item/0x0713", "name": "Mace" }, { "id": "aidyn:flag/1" }]"""),
                ("a.json", """{ "id": "aidyn:item/0x0712", "name": "Club" }""")),
            Plugin("mymod", ["aidyn"], ("sword.json", """{ "id": "mymod:item/sword", "name": "Sword" }""")),
        ]);

        var weapons = set.All<DbRecord>(RecordTypes.Item);
        Assert.Equal(["aidyn:item/0x0712", "aidyn:item/0x0713", "mymod:item/sword"], weapons.Select(w => w.Id.ToString()));
        Assert.Same(weapons, set.All<DbRecord>(RecordTypes.Item));
        Assert.Same(weapons[2], set.Get<DbRecord>(RecordId.Parse("mymod:item/sword")));
        Assert.Empty(set.All<DbRecord>(RecordTypes.Spell));
    }

    [Fact]
    public void ASingletonIsTheBaseGamesRecordZero()
    {
        var set = PluginSet.Load([
            Plugin("aidyn", null, ("camping.json", """{ "id": "aidyn:camping/0", "name": "Camping" }""")),
            Plugin("mymod", ["aidyn"],
                ("camping.json", """[{ "patch": "aidyn:camping/0", "name": "Better camping" }, { "id": "mymod:camping/0", "name": "Another" }]""")),
        ]);

        var camping = set.Singleton<CampingRecord>();
        Assert.Equal("aidyn:camping/0", camping.Id.ToString());
        Assert.Equal("Better camping", (string)set.GetJson(camping.Id)!["name"]!);
        Assert.Same(camping, set.TrySingleton<CampingRecord>());
        Assert.Null(set.TrySingleton<SkyRecord>());
        Assert.Throws<KeyNotFoundException>(() => set.Singleton<SkyRecord>());
        // A type with many records, or one that's the class of two types, has no singleton.
        Assert.Throws<ArgumentException>(() => set.Singleton<ItemRecord>());
        Assert.Throws<ArgumentException>(() => set.Singleton<SkillRecord>());
    }

    [Fact]
    public void TheRegistryTiesEachTypeToItsClassKeyAndKinds()
    {
        Assert.Equal(typeof(CombatRecord), RecordTypes.Info(RecordTypes.Combat).Class);
        Assert.True(RecordTypes.Info(RecordTypes.Combat).Singleton);
        Assert.Equal("aidyn:combat/0", RecordTypes.Info(RecordTypes.Combat).SingletonId.ToString());
        Assert.Equal(KeyKind.Db, RecordTypes.Info(RecordTypes.Entity).Key);
        Assert.True(RecordTypes.Info(RecordTypes.Entity).Reads(typeof(DbRecord)));
        Assert.False(RecordTypes.Info(RecordTypes.Entity).Reads(typeof(ItemRecord)));
        Assert.Null(RecordTypes.Info(RecordTypes.Scene).Class);
        Assert.Equal(KeyKind.Chunk, RecordTypes.Info(RecordTypes.Chunk).Key);
        Assert.Equal(KeyKind.MapObject, RecordTypes.Info(RecordTypes.MapObject).Key);
        Assert.Equal("chunks", RecordTypes.Info(RecordTypes.Chunk).LazyFolder);
        Assert.Equal("scripts", RecordTypes.Info(RecordTypes.Script).LazyFolder);
        Assert.Null(RecordTypes.Info(RecordTypes.Entity).LazyFolder);
        Assert.Equal(RecordTypes.Entity, RecordTypes.Of<EntityRecord>().Name);
        Assert.Throws<ArgumentException>(() => RecordTypes.Info("widget"));
        Assert.Equal(RecordTypes.Registry.Select(t => t.Name), RecordTypes.Names);
        Assert.True(RecordTypes.Registry.All(t => t.Class is null || t.Class.Name.EndsWith("Record", StringComparison.Ordinal)));

        // The map object kinds come from MapObject's attributes, each with a name of its own.
        Assert.Equal(["scene", "container", "light", "sound", "wanderNode", "encounter", "npc", "refPoint", "teleporter", "camera", "script", "tripwire", "savePoint"],
            MapObjectKinds.All.Select(k => k.Discriminator));
        Assert.All(MapObjectKinds.All, k => Assert.NotEqual(k.Type.Name, k.Name));
        Assert.Equal("Encounter", MapObjectKinds.Name("encounter"));
    }

    [Fact]
    public void TypedRecordsRoundTrip()
    {
        var original = new SceneObject(3, new(1.5f, 2, -3), 45, new FlagGate(RecordId.Parse("aidyn:flag/0"), true),
            [new SceneModel(RecordId.Parse("aidyn:scene/0x0B48"), 15)], new(0, 0.5f, 0), new(1, 1, 1),
            new Rgba(1, 2, 3, 4), SceneObjectFlags.Solid | SceneObjectFlags.Tint);
        ChunkRecord map = new(RecordId.Parse("aidyn:chunk/0/I08"), RecordId.Parse("aidyn:mapset/0"), 9, 8, RecordId.Parse("aidyn:terrain/1"), [original]);
        var json = JsonSerializer.Serialize(map, PluginJson.Options);
        Assert.Contains("\"type\": \"scene\"", json, StringComparison.Ordinal);
        Assert.Contains("[1.5, 2, -3]", json, StringComparison.Ordinal);

        var read = PluginSet.Load([Plugin("aidyn", null, ("chunks.json", json))]).Get<ChunkRecord>(map.Id);
        var scene = Assert.IsType<SceneObject>(Assert.Single(read.Objects));
        Assert.Equal(original.Models, scene.Models);
        Assert.Equal(original with { Models = scene.Models }, scene);
    }
}
