using Aidyn.Plugins.Records;

namespace Aidyn.Plugins.Tests;

/// <summary><see cref="PluginSet.Validate"/>: what it refuses, and whom it blames.</summary>
public sealed class ValidationTests : IDisposable
{
    private readonly TempPlugins _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Plugin(string id, string[]? dependencies, params (string File, string Json)[] files) => _temp.Add(id, dependencies, files);

    private const string Map = """
        [{ "id": "aidyn:map/0x0E87", "objects": [
          { "type": "savePoint", "index": 0, "position": [1, 0, 1], "radius": 1, "pak": "With" },
          { "type": "savePoint", "index": 1, "position": [2, 0, 2], "radius": 1, "pak": "With" },
        ] }]
        """;

    private string Base() => Plugin("aidyn", null, ("maps.json", Map), ("shops.json", """
        { "id": "aidyn:shop/0", "shopkeeper": "aidyn:entity/0x0201", "stock": [{ "item": "aidyn:item/0x0801", "priceMultiplier": 1.5 }] }
        """));

    [Fact]
    public void ACleanSetHasNoProblemsAndItsRecordsAreReadOnce()
    {
        var set = PluginSet.Load([Base()]);
        var report = set.Validate();
        Assert.True(report.IsClean, report.Describe());
        Assert.Equal("Every record reads.", report.Describe());
        var map = set.Get<MapRecord>(RecordId.Parse("aidyn:map/0x0E87"));
        Assert.Equal(2, map.Objects.Count);
        Assert.Same(map, set.Get<MapRecord>(map.Id));
    }

    [Fact]
    public void AFieldTheTypeLacksIsBlamedOnThePluginThatSetIt()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("shop.json", """{ "patch": "aidyn:shop/0", "shopkeper": "aidyn:entity/0x0202" }""")),
        ]);
        var problem = Assert.Single(set.Validate().Problems);
        Assert.Equal("aidyn:shop/0", problem.Record.ToString());
        Assert.Equal("shopkeper", problem.Path);
        Assert.Equal("mymod", problem.Plugin);
        Assert.Contains("'shopkeper'", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Aidyn.Plugins.Records", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AKindNoDiscriminatorNamesIsBlamedOnWhoeverAddedTheElement()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("map.json", """{ "patch": "aidyn:map/0x0E87", "objects": [{ "type": "chest", "index": 5, "position": [1, 0, 1], "radius": 1, "pak": "With" }] }""")),
        ]);
        var problem = Assert.Single(set.Validate().Problems);
        Assert.Equal("objects[5]", problem.Path);
        Assert.Equal("mymod", problem.Plugin);
        Assert.Contains("'chest'", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldInAKeyedElementIsBlamedByItsKey()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("one", ["aidyn"], ("map.json", """{ "patch": "aidyn:map/0x0E87", "objects": [{ "index": 1, "radius": 2 }] }""")),
            Plugin("two", ["aidyn"], ("map.json", """{ "patch": "aidyn:map/0x0E87", "objects": [{ "index": 1, "radius": "wide" }] }""")),
        ]);
        var problem = Assert.Single(set.Validate().Problems);
        Assert.Equal("objects[1].radius", problem.Path);
        Assert.Equal("two", problem.Plugin);
    }

    [Fact]
    public void MissingFieldsNullsAndDuplicateKeysAreProblemsOfTheDefiner()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"],
                ("map.json", """{ "id": "mymod:map/short", "objects": [{ "type": "savePoint", "index": 0, "position": [1, 0, 1], "pak": "With" }] }"""),
                ("shop.json", """{ "id": "mymod:shop/twice", "shopkeeper": null, "stock": [{ "item": "aidyn:item/0x0801" }, { "item": "aidyn:item/0x0801" }] }""")),
        ]);
        var problems = set.Validate().Problems;
        Assert.Equal(3, problems.Count);
        Assert.All(problems, p => Assert.Equal("mymod", p.Plugin));
        var map = Assert.Single(problems, p => p.Record.ToString() == "mymod:map/short");
        Assert.Equal("objects[0]", map.Path);
        Assert.Contains("'radius'", map.Message, StringComparison.Ordinal);
        Assert.Contains(problems, p => p.Path == "shopkeeper" && p.Message.Contains("null", StringComparison.Ordinal));
        var twice = Assert.Single(problems, p => p.Path == "stock[aidyn:item/0x0801]");
        Assert.Equal("Two elements of stock are keyed aidyn:item/0x0801.", twice.Message);

        var text = set.Validate().Describe(limit: 2);
        Assert.StartsWith("3 records can't be read. Fix or remove the plugin:\nmymod:\n", text, StringComparison.Ordinal);
        Assert.EndsWith("… and 1 more.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectWithoutItsKindIsRefusedWhereverItSits()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"],
                ("map.json", """{ "patch": "aidyn:map/0x0E87", "objects": [{ "index": 9, "position": [1, 0, 1], "radius": 1, "pak": "With" }] }"""),
                ("spell.json", """
                    { "id": "mymod:spell/zap", "name": "Zap", "school": "Star", "symbol": 0, "level": 1, "stamina": 1, "range": 1, "aiValue": 1,
                      "effects": [{ "type": "stamina", "amount": { "flat": 1 } }, { "amount": { "flat": 2 } }] }
                    """)),
        ]);
        var problems = set.Validate().Problems.Where(p => p.Message.Contains("lacks its type", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Record.ToString() == "aidyn:map/0x0E87" && p.Path == "objects[9]" && p.Plugin == "mymod"
            && p.Message.StartsWith("objects[9] lacks its type: one of scene, container, ", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Record.ToString() == "mymod:spell/zap" && p.Path == "effects[1]" && p.Plugin == "mymod");
    }

    [Fact]
    public void RecordsWithoutAClassArentValidated()
    {
        var set = PluginSet.Load([Plugin("aidyn", null, ("scenes.json", """{ "id": "aidyn:scene/0x0A0E", "whatever": 1 }"""))]);
        Assert.True(set.Validate().IsClean);
    }

    [Fact]
    public void LoadInstalledCheckedRefusesASetWithProblems()
    {
        var baseDir = Path.Combine(_temp.Root, BasePlugin.Folder);
        Directory.CreateDirectory(Path.Combine(baseDir, "records"));
        File.WriteAllText(Path.Combine(baseDir, "plugin.json"),
            $$"""{ "id": "aidyn", "name": "Base", "version": "1.0.0", "import": { "importer": {{BasePlugin.ImporterVersion}}, "rom": "x", "romVersion": "NA 1.0" } }""");
        File.WriteAllText(Path.Combine(baseDir, "records", "maps.json"), Map);
        Plugin("mymod", ["aidyn"], ("map.json", """{ "patch": "aidyn:map/0x0E87", "objects": [{ "index": 0, "radious": 3 }] }"""));

        var error = Assert.Throws<InvalidDataException>(() => PluginSet.LoadInstalledChecked(_temp.Root));
        Assert.Contains("mymod:\n  aidyn:map/0x0E87 objects[0].radious:", error.Message, StringComparison.Ordinal);
    }
}
