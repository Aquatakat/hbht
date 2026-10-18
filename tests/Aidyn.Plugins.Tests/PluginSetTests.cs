using System.Text.Json;
using Aidyn.Plugins.Records;

namespace Aidyn.Plugins.Tests;

public sealed class PluginSetTests : IDisposable
{
    private readonly TempPlugins _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Plugin(string id, string[]? dependencies, params (string File, string Json)[] files) => _temp.Add(id, dependencies, files);

    private string Base() => Plugin("aidyn", null, ("weapons.json", """
        [
          // Comments and trailing commas are allowed.
          { "id": "aidyn:weapon/0x0712", "name": "Club", "damage": 3, "stats": { "hit": 5, "range": 1 }, },
          { "id": "aidyn:weapon/0x0713", "name": "Mace", "damage": 5 },
        ]
        """));

    [Fact]
    public void PatchesMergeFieldByField()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("club.json", """{ "patch": "aidyn:weapon/0x0712", "damage": 9, "stats": { "range": null } }""")),
        ]);

        var club = set.GetJson(RecordId.Parse("aidyn:weapon/0x0712"))!;
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
            Plugin("one", ["aidyn"], ("a.json", """{ "patch": "aidyn:weapon/0x0712", "damage": 9 }""")),
            Plugin("two", ["aidyn"], ("b.json", """{ "patch": "aidyn:weapon/0x0712", "damage": 7, "name": "Big club" }""")),
        ]);

        var conflicts = set.Conflicts.OrderBy(c => c.Field, StringComparer.Ordinal).ToList();
        Assert.Equal(2, conflicts.Count);
        Assert.Equal("damage", conflicts[0].Field);
        Assert.Equal(["aidyn", "one", "two"], conflicts[0].Plugins);
        Assert.Equal("two", conflicts[0].Winner);
        Assert.Equal("name", conflicts[1].Field);
        Assert.Equal(["aidyn", "two"], conflicts[1].Plugins);
        Assert.Equal(7, (int)set.GetJson(RecordId.Parse("aidyn:weapon/0x0712"))!["damage"]!);
    }

    [Fact]
    public void ModsCanOnlyAddRecordsInTheirOwnNamespace()
    {
        var error = Assert.Throws<InvalidDataException>(() => PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("new.json", """{ "id": "aidyn:weapon/0x0799", "damage": 1 }""")),
        ]));
        Assert.Contains("own namespace", error.Message, StringComparison.Ordinal);

        Directory.Delete(_temp.Root, recursive: true);
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("new.json", """{ "id": "mymod:weapon/sword_of_tests", "damage": 1 }""")),
        ]);
        Assert.True(set.Contains(RecordId.Parse("mymod:weapon/sword_of_tests")));
    }

    [Fact]
    public void PatchesNeedATarget()
    {
        Assert.Throws<InvalidDataException>(() => PluginSet.Load([
            Base(), Plugin("mymod", ["aidyn"], ("p.json", """{ "patch": "aidyn:weapon/0x0799", "damage": 1 }""")),
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
        Directory.CreateDirectory(Path.Combine(baseDir, "assets", "aidyn", "voxel", "0x1A20"));
        File.WriteAllText(Path.Combine(baseDir, "assets", "aidyn", "voxel", "0x1A20", "12.png"), "");

        var set = PluginSet.Load([baseDir, modDir]);
        Assert.StartsWith(modDir, set.FindAsset(RecordId.Parse("aidyn:scene/0x0A0E"), "glb"), StringComparison.Ordinal);
        Assert.StartsWith(baseDir, set.FindAsset(RecordId.Parse("aidyn:voxel/0x1A20/12"), "png"), StringComparison.Ordinal);
        Assert.Null(set.FindAsset(RecordId.Parse("aidyn:scene/0x0A0F"), "glb"));
    }

    [Fact]
    public void FieldsRememberWhichPluginsSetThem()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("club.json", """{ "patch": "aidyn:weapon/0x0712", "damage": 9 }""")),
        ]);
        var club = RecordId.Parse("aidyn:weapon/0x0712");
        Assert.Equal("aidyn", set.DefinedBy(club));
        Assert.Equal(["aidyn", "mymod"], set.Setters(club, "damage"));
        Assert.Equal(["aidyn"], set.Setters(club, "name"));
        Assert.Empty(set.Setters(club, "weight"));
        Assert.Null(set.DefinedBy(RecordId.Parse("aidyn:weapon/0x0799")));
    }

    [Fact]
    public void TheCatalogListsRecordsAndAssetsAndWhatNamesThem()
    {
        var baseDir = Plugin("aidyn", null,
            ("mapsets.json", """[{ "id": "aidyn:mapset/0", "label": "Overworld", "cellSize": 100, "chunks": [{ "row": 9, "column": 8, "map": "aidyn:map/0x0E87" }] }]"""),
            ("maps.json", """
                [{ "id": "aidyn:map/0x0E87", "objects": [
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
        Asset(baseDir, "aidyn/map/0x0E87.glb", []);
        Asset(modDir, "aidyn/scene/0x0B48.glb", []);
        Asset(baseDir, "aidyn/scene/0x0B49.glb", []);
        Asset(baseDir, "aidyn/image/0x019F.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8, 0, 0, 0, 12, 0, 0, 0, 37]);
        Asset(baseDir, "aidyn/scene/not-an-id.glb", []);

        var catalog = PluginCatalog.Build(PluginSet.Load([baseDir, modDir]));

        Assert.Equal(["aidyn:image/0x019F", "aidyn:map/0x0E87", "aidyn:mapset/0", "aidyn:scene/0x0B48", "aidyn:scene/0x0B49"],
            catalog.Entries.Select(e => e.Id.ToString()));
        var map = catalog.Find(RecordId.Parse("aidyn:map/0x0E87"))!;
        Assert.True(map.HasRecord);
        Assert.Equal(0x0E87, map.Borg);
        Assert.Equal("Overworld I08, 2 scene objects", map.Summary);
        Assert.Equal("aidyn", map.Asset("glb")!.Plugin);
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

    /// <summary>A partial read of a weapon, as <see cref="DbRecord"/> is one, to read one record as two types.</summary>
    private sealed record Damage(RecordId Id, int Value);

    [Fact]
    public void TypedRecordsAreReadOnceAndHoldTheMerge()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("club.json", """{ "patch": "aidyn:weapon/0x0712", "name": "Big club", "value": 9 }""")),
        ]);
        var club = RecordId.Parse("aidyn:weapon/0x0712");

        var read = set.Get<DbRecord>(club);
        Assert.Equal("Big club", read.Name);
        Assert.Same(read, set.Get<DbRecord>(club));
        Assert.Same(read, set.TryGet<DbRecord>(club));
        Assert.Same(read, set.All<DbRecord>(RecordTypes.Weapon)[0]);

        // The same record read as another type is that type's own object.
        var damage = set.Get<Damage>(club);
        Assert.Equal(9, damage.Value);
        Assert.Same(damage, set.Get<Damage>(club));
        Assert.Null(set.TryGet<DbRecord>(RecordId.Parse("aidyn:weapon/0x0799")));
    }

    [Fact]
    public void AllOfATypeIsInIdOrder()
    {
        var set = PluginSet.Load([
            Plugin("aidyn", null,
                ("b.json", """[{ "id": "aidyn:weapon/0x0713", "name": "Mace" }, { "id": "aidyn:flag/1" }]"""),
                ("a.json", """{ "id": "aidyn:weapon/0x0712", "name": "Club" }""")),
            Plugin("mymod", ["aidyn"], ("sword.json", """{ "id": "mymod:weapon/sword", "name": "Sword" }""")),
        ]);

        var weapons = set.All<DbRecord>(RecordTypes.Weapon);
        Assert.Equal(["aidyn:weapon/0x0712", "aidyn:weapon/0x0713", "mymod:weapon/sword"], weapons.Select(w => w.Id.ToString()));
        Assert.Same(weapons, set.All<DbRecord>(RecordTypes.Weapon));
        Assert.Same(weapons[2], set.Get<DbRecord>(RecordId.Parse("mymod:weapon/sword")));
        Assert.Empty(set.All<DbRecord>(RecordTypes.Armor));
    }

    [Fact]
    public void ASingletonIsTheBaseGamesRecordZero()
    {
        var set = PluginSet.Load([
            Plugin("aidyn", null, ("camping.json", """{ "id": "aidyn:camping/0", "name": "Camping" }""")),
            Plugin("mymod", ["aidyn"],
                ("camping.json", """[{ "patch": "aidyn:camping/0", "name": "Better camping" }, { "id": "mymod:camping/0", "name": "Another" }]""")),
        ]);

        var camping = set.Singleton<DbRecord>(RecordTypes.Camping);
        Assert.Equal("Better camping", camping.Name);
        Assert.Same(camping, set.TrySingleton<DbRecord>(RecordTypes.Camping));
        Assert.Null(set.TrySingleton<DbRecord>(RecordTypes.Sky));
        Assert.Throws<KeyNotFoundException>(() => set.Singleton<DbRecord>(RecordTypes.Sky));
    }

    [Fact]
    public void TypedRecordsRoundTrip()
    {
        var original = new SceneObject(3, new(1.5f, 2, -3), 45, PakVariant.With, new FlagGate(RecordId.Parse("aidyn:flag/0"), true),
            [new SceneModel(RecordId.Parse("aidyn:scene/0x0B48"), 15)], 1, new(0, 0.5f, 0), new(1, 1, 1),
            new Rgba(1, 2, 3, 4), SceneObjectFlags.Solid | SceneObjectFlags.Tint);
        MapRecord map = new(RecordId.Parse("aidyn:map/0x0E87"), [original]);
        var json = JsonSerializer.Serialize(map, PluginJson.Options);
        Assert.Contains("\"type\": \"scene\"", json, StringComparison.Ordinal);
        Assert.Contains("[1.5, 2, -3]", json, StringComparison.Ordinal);

        var read = PluginSet.Load([Plugin("aidyn", null, ("maps.json", json))]).Get<MapRecord>(map.Id);
        var scene = Assert.IsType<SceneObject>(Assert.Single(read.Objects));
        Assert.Equal(original.Models, scene.Models);
        Assert.Equal(original with { Models = scene.Models }, scene);
    }
}
