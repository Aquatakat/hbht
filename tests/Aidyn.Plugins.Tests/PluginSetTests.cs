using System.Text.Json;
using Aidyn.Plugins.Records;

namespace Aidyn.Plugins.Tests;

public sealed class PluginSetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aidyn-plugins-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Plugin(string id, string[]? dependencies, params (string File, string Json)[] files)
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        var deps = dependencies is null ? "" : $", \"dependencies\": [{string.Join(", ", dependencies.Select(d => $"\"{d}\""))}]";
        File.WriteAllText(Path.Combine(dir, "plugin.json"), $$"""{ "id": "{{id}}", "name": "{{id}}", "version": "1.0.0"{{deps}} }""");
        foreach (var (file, json) in files)
        {
            var path = Path.Combine(dir, "records", file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        return dir;
    }

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

        Directory.Delete(_root, recursive: true);
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
