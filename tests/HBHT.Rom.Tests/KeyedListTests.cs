using System.Text.Json.Nodes;
using HBHT.Plugins;

namespace HBHT.Rom.Tests;

/// <summary>
/// The keyed lists of the base plugin's records (<see cref="KeyedListAttribute"/>, ADR 0005): every element carries its key
/// and no two share one, so a patch that names an element names exactly one.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class KeyedListTests(Na10Import fixture)
{
    [Fact]
    public void EveryKeyedListOfTheBasePluginHasUniqueKeys()
    {
        var set = fixture.Data.Set;
        var lists = 0;
        var problems = new List<string>();
        foreach (var id in set.Ids)
        {
            if (RecordTypes.ClassOf(id.Type) is not { } type)
                continue;
            Check(id, set.GetJson(id)!, RecordSchema.Concrete(type, set.GetJson(id)!), "", ref lists, problems);
        }
        Assert.Empty(problems);
        // Maps, scripts, map sets, entities, shops, recipes, the combat record and the world map have them.
        Assert.True(lists > 1500, $"{lists} keyed lists");
    }

    private static void Check(RecordId id, JsonObject json, Type type, string prefix, ref int lists, List<string> problems)
    {
        foreach (var (field, value) in json)
        {
            if (RecordSchema.Find(type, field) is not { } property)
                continue;
            if (property.Keyed is { } keyed && value is JsonArray elements)
            {
                lists++;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var element in elements)
                {
                    var key = keyed.KeyOf(element);
                    if (key is null)
                        problems.Add($"{id} {prefix}{field}: an element lacks its {keyed.Describe}");
                    else if (!seen.Add(key))
                        problems.Add($"{id} {prefix}{field}: two elements are keyed {key}");
                    else if (element is JsonObject o)
                        Check(id, o, RecordSchema.Concrete(keyed.ElementType, o), $"{prefix}{field}[{key}].", ref lists, problems);
                }
            }
            else if (value is JsonObject nested)
            {
                Check(id, nested, RecordSchema.Concrete(property.Type, nested), $"{prefix}{field}.", ref lists, problems);
            }
        }
    }
}
