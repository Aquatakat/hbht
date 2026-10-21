using System.Text.Json.Nodes;
using Aidyn.Plugins.Records;

namespace Aidyn.Plugins.Tests;

/// <summary>The JSON Schema <see cref="RecordSchema.JsonSchema"/> exports: its shape, which the real-data tests validate records by.</summary>
public sealed class SchemaTests
{
    [Fact]
    public void ARecordsSchemaRequiresWhatHasNoDefaultAndRefusesOtherFields()
    {
        var schema = RecordSchema.JsonSchema(typeof(ShopRecord));
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string)schema["$schema"]!);
        Assert.Equal(["id", "shopkeeper", "stock"], schema["required"]!.AsArray().Select(r => (string)r!));
        Assert.False((bool)schema["additionalProperties"]!);
        var properties = schema["properties"]!.AsObject();
        Assert.Equal(["$schema", "id", "shopkeeper", "stock"], properties.Select(p => p.Key));

        // IDs are strings with the ID's pattern, not the "anything" a custom converter exports as.
        Assert.Equal("string", (string)properties["id"]!["type"]!);
        Assert.Equal(RecordSchema.IdPattern, (string)properties["id"]!["pattern"]!);
        var stock = properties["stock"]!["items"]!;
        Assert.Equal(["item"], stock["required"]!.AsArray().Select(r => (string)r!));
        Assert.Equal(1, (int)stock["properties"]!["priceMultiplier"]!["default"]!);
        Assert.Null(stock["properties"]!["$remove"]);
    }

    [Fact]
    public void VectorsColoursAndKindsAreSpelledOut()
    {
        var schema = RecordSchema.JsonSchema(typeof(MapRecord));
        var kinds = schema["properties"]!["objects"]!["items"]!["anyOf"]!.AsArray();
        Assert.Equal(MapObjectKinds.All.Select(k => k.Discriminator), kinds.Select(k => (string)k!["properties"]!["type"]!["const"]!));
        var light = kinds.First(k => (string)k!["properties"]!["type"]!["const"]! == "light")!["properties"]!;
        Assert.Equal("array", (string)light["position"]!["type"]!);
        Assert.Equal(3, (int)light["position"]!["maxItems"]!);
        Assert.Equal("number", (string)light["position"]!["items"]!["type"]!);
        Assert.Equal(4, (int)light["from"]!["maxItems"]!);
        Assert.Equal(255, (int)light["from"]!["items"]!["maximum"]!);
        Assert.Equal(["Static", "Alternate", "Sine", "Random"], light["kind"]!["enum"]!.AsArray().Select(e => (string)e!));
        // An ID may be null only where the type allows it.
        Assert.Equal("string", (string)light["visible"]!["properties"]!["flag"]!["type"]!);
        var container = kinds.First(k => (string)k!["properties"]!["type"]!["const"]! == "container")!["properties"]!;
        Assert.Equal(["string", "null"], container["loot"]!["type"]!.AsArray().Select(t => (string)t!));
    }

    [Fact]
    public void APatchsSchemaRequiresOnlyThePatchAndAllowsRemovals()
    {
        var schema = RecordSchema.JsonSchema(typeof(MapRecord), patch: true);
        Assert.Equal(["patch"], schema["required"]!.AsArray().Select(r => (string)r!));
        var properties = schema["properties"]!.AsObject();
        Assert.False(properties.ContainsKey("id"));
        Assert.Equal("string", (string)properties["patch"]!["type"]!);
        Assert.Equal(["array", "null"], properties["objects"]!["type"]!.AsArray().Select(t => (string)t!));
        foreach (var kind in properties["objects"]!["items"]!["anyOf"]!.AsArray())
        {
            Assert.Null(kind!["required"]);
            Assert.Equal("boolean", (string)kind["properties"]!["$remove"]!["type"]!);
            Assert.Equal(["number", "null"], kind["properties"]!["radius"]!["type"]!.AsArray().Select(t => (string)t!));
        }
        // An unkeyed list's elements have no $remove, and its own fields stay as they are but for null.
        var models = properties["objects"]!["items"]!["anyOf"]!.AsArray().First(k => (string)k!["properties"]!["type"]!["const"]! == "scene")!["properties"]!["models"]!;
        Assert.Null(models["items"]!["properties"]!["$remove"]);
        Assert.Null(models["items"]!["required"]);
    }
}
