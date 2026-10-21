using System.Text.Json;
using System.Text.Json.Nodes;
using Aidyn.Plugins;
using Json.Schema;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The exported JSON Schema (<see cref="RecordSchema.JsonSchema"/>, <c>aidyn schema</c>) against the base plugin's records:
/// what the importer writes validates, and what strict reading refuses the schema refuses too.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class SchemaRoundTripTests(Na10Import fixture)
{
    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.List };

    private static JsonSchema Schema(Type type, bool patch = false) =>
        JsonSchema.FromText(RecordSchema.JsonSchema(type, patch).ToJsonString());

    [Fact]
    public void OneRealRecordOfEachTypeValidatesAndReadsBackTheSame()
    {
        var set = fixture.Data.Set;
        var checkedTypes = 0;
        foreach (var type in RecordTypes.Names)
        {
            if (RecordTypes.ClassOf(type) is not { } cls)
                continue;
            var id = set.Ids.Where(i => i.Type == type).OrderBy(i => i.ToString(), StringComparer.Ordinal).FirstOrDefault();
            if (id == default)
                continue;
            var json = set.GetJson(id)!;
            var result = Schema(cls).Evaluate(Element(json), Options);
            Assert.True(result.IsValid, $"{id}: {string.Join("; ", Failures(result))}");

            // Read strictly and written again, the record is the same JSON, so the schema describes what the type holds.
            var read = json.Deserialize(cls, PluginJson.Strict)!;
            var written = JsonSerializer.SerializeToNode(read, cls, PluginJson.Options)!;
            Assert.True(JsonNode.DeepEquals(json, written), $"{id} changes when read and written");
            checkedTypes++;
        }
        Assert.True(checkedTypes >= 35, $"{checkedTypes} types checked");
    }

    [Fact]
    public void EveryMapAndScriptValidates()
    {
        var set = fixture.Data.Set;
        foreach (var (type, cls) in new[] { (RecordTypes.Map, typeof(Plugins.Records.MapRecord)), (RecordTypes.Dialogue, typeof(Plugins.Records.DialogueRecord)) })
        {
            var schema = Schema(cls);
            foreach (var id in set.Ids.Where(i => i.Type == type))
            {
                var result = schema.Evaluate(Element(set.GetJson(id)), Options);
                Assert.True(result.IsValid, $"{id}: {string.Join("; ", Failures(result))}");
            }
        }
    }

    [Fact]
    public void TheSchemaRefusesWhatStrictReadingRefuses()
    {
        var set = fixture.Data.Set;
        var map = (JsonObject)set.GetJson(set.Ids.First(i => i.Type == RecordTypes.Map))!.DeepClone();
        var schema = Schema(typeof(Plugins.Records.MapRecord));
        Assert.True(schema.Evaluate(Element(map), Options).IsValid);

        var first = (JsonObject)map["objects"]![0]!;
        first["radious"] = 3;
        Assert.False(schema.Evaluate(Element(map), Options).IsValid);
        Assert.Throws<JsonException>(() => map.Deserialize<Plugins.Records.MapRecord>(PluginJson.Strict));
        first.Remove("radious");

        first["type"] = "chest";
        Assert.False(schema.Evaluate(Element(map), Options).IsValid);
        Assert.Throws<JsonException>(() => map.Deserialize<Plugins.Records.MapRecord>(PluginJson.Strict));
    }

    [Fact]
    public void APatchValidatesByThePatchSchema()
    {
        var schema = Schema(typeof(Plugins.Records.MapRecord), patch: true);
        var patch = JsonNode.Parse("""{ "patch": "aidyn:map/0x0E87", "$schema": "map.patch.schema.json", "objects": [{ "index": 3, "radius": 2 }, { "index": 4, "$remove": true }], "grid": null }""");
        var result = schema.Evaluate(Element(patch), Options);
        Assert.True(result.IsValid, string.Join("; ", Failures(result)));
        Assert.False(schema.Evaluate(Element(JsonNode.Parse("""{ "id": "aidyn:map/0x0E87" }""")), Options).IsValid);
        Assert.False(schema.Evaluate(Element(JsonNode.Parse("""{ "patch": "aidyn:map/0x0E87", "objects": [{ "index": 3, "radious": 2 }] }""")), Options).IsValid);
    }

    private static JsonElement Element(JsonNode? node) => JsonSerializer.SerializeToElement(node);

    private static IEnumerable<string> Failures(EvaluationResults result) =>
        (result.Details ?? []).Where(d => !d.IsValid && d.Errors is { Count: > 0 }).Select(d => $"{d.InstanceLocation}: {string.Join(", ", d.Errors!.Values)}");
}
