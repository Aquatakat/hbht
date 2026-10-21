using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Aidyn.Plugins;

/// <summary>
/// Marks a record's list as keyed (ADR 0005): its elements have identity, given by these members of the element
/// type, and a patch merges them element by element by that identity rather than replacing the list
/// (<see cref="MergePatch"/>). A list without it is replaced whole.
/// </summary>
/// <param name="keys">The element type's members that identify an element, by their C# names, such as <c>nameof(MapObject.Index)</c>.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class KeyedListAttribute(params string[] keys) : Attribute
{
    public IReadOnlyList<string> Keys { get; } = keys;
}

/// <summary>
/// What the record types say about their JSON, read from the serializer's own metadata so that the merge and the
/// record types can't drift: each property's JSON name and type, which lists are keyed and by what, and which
/// class a polymorphic object's discriminator names.
/// </summary>
public static class RecordSchema
{
    /// <summary>A keyed list's element type and the JSON names of the fields that identify an element.</summary>
    public sealed record KeyedList(Type ElementType, IReadOnlyList<string> Keys)
    {
        /// <summary>
        /// An element's key as the setter paths spell it, such as <c>12</c> or <c>9,8</c>: its key fields' values, joined by
        /// commas, strings without their quotes. Null for an element that isn't an object or lacks one of the fields.
        /// </summary>
        public string? KeyOf(JsonNode? element)
        {
            if (element is not JsonObject o)
                return null;
            var parts = new string[Keys.Count];
            for (var i = 0; i < parts.Length; i++)
            {
                if (o[Keys[i]] is not JsonValue value)
                    return null;
                parts[i] = value.TryGetValue(out string? text) ? text : value.ToJsonString();
            }
            return string.Join(',', parts);
        }

        /// <summary>The key fields, worded for an error: "index", or "row and column".</summary>
        public string Describe => Keys.Count == 1 ? Keys[0] : string.Join(", ", Keys.Take(Keys.Count - 1)) + " and " + Keys[^1];
    }

    /// <summary>A property as JSON: its name there, its type, and how its list is keyed, if it's a keyed list.</summary>
    public sealed record Property(string Name, Type Type, KeyedList? Keyed);

    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, Property>> _properties = new();

    /// <summary>The properties of <paramref name="type"/> by JSON name. Empty for a type that isn't an object, such as a list or a dictionary.</summary>
    public static IReadOnlyDictionary<string, Property> Properties(Type type) => _properties.GetOrAdd(type, static t =>
    {
        var info = PluginJson.Options.GetTypeInfo(t);
        if (info.Kind != JsonTypeInfoKind.Object)
            return new Dictionary<string, Property>(StringComparer.Ordinal);
        var properties = new Dictionary<string, Property>(StringComparer.Ordinal);
        foreach (var p in info.Properties)
        {
            var keyed = p.AttributeProvider?.GetCustomAttributes(typeof(KeyedListAttribute), inherit: true).OfType<KeyedListAttribute>().FirstOrDefault();
            KeyedList? list = null;
            if (keyed is not null)
            {
                var element = ElementType(p.PropertyType)
                    ?? throw new InvalidOperationException($"{t.Name}.{p.Name} is marked keyed but isn't a list.");
                list = new KeyedList(element, [.. keyed.Keys.Select(k => JsonName(element, k))]);
            }
            properties[p.Name] = new Property(p.Name, p.PropertyType, list);
        }
        return properties;
    });

    /// <summary>The property of <paramref name="type"/> that JSON field <paramref name="name"/> is, or null for no type or no such property.</summary>
    public static Property? Find(Type? type, string name) => type is null ? null : Properties(type).GetValueOrDefault(name);

    /// <summary>
    /// The class an object is read as: for a polymorphic type, the derived type its <c>type</c> discriminator names,
    /// else <paramref name="type"/> itself.
    /// </summary>
    public static Type Concrete(Type type, JsonObject json)
    {
        var info = PluginJson.Options.GetTypeInfo(type);
        if (info.PolymorphismOptions is not { } polymorphism)
            return type;
        var discriminator = json[polymorphism.TypeDiscriminatorPropertyName] is JsonValue v && v.TryGetValue(out string? text) ? text : null;
        foreach (var derived in polymorphism.DerivedTypes)
        {
            if (derived.TypeDiscriminator as string == discriminator)
                return derived.DerivedType;
        }
        return type;
    }

    /// <summary>The derived types a polymorphic type reads as, by discriminator, or empty for a type that isn't polymorphic.</summary>
    public static IReadOnlyList<(string Discriminator, Type Type)> DerivedTypes(Type type) =>
        PluginJson.Options.GetTypeInfo(type).PolymorphismOptions is { } p
            ? [.. p.DerivedTypes.Select(d => ((string)d.TypeDiscriminator!, d.DerivedType))]
            : [];

    /// <summary>The element type of a list type, or null for a type that isn't one.</summary>
    public static Type? ElementType(Type type)
    {
        if (type == typeof(string))
            return null;
        var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0];
    }

    private static string JsonName(Type type, string member)
    {
        var info = PluginJson.Options.GetTypeInfo(type);
        return info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo m && m.Name == member)?.Name
            ?? throw new InvalidOperationException($"{type.Name} has no member {member} to key a list by.");
    }

    // ---- JSON Schema (ADR 0005) ------------------------------------------------------------------------------------------

    /// <summary>The pattern a record ID matches (ADR 0004), loosely: the parser is the judge of the key's spelling.</summary>
    public const string IdPattern = "^[a-z0-9_]+:[a-z0-9_]+/.+$";

    /// <summary>
    /// The JSON Schema (draft 2020-12) of a record of <paramref name="type"/> as a plugin defines it, exported from
    /// <see cref="PluginJson.Strict"/> so that it says what <see cref="PluginSet.Validate"/> says: no field the type lacks,
    /// every field without a default, the <c>type</c> discriminators a kind has, IDs as strings, vectors and colours as arrays.
    /// A record object may carry <c>$schema</c>, which the loader ignores. With <paramref name="patch"/>, the schema of a
    /// patch to one instead: <c>patch</c> names the record, every field is optional and may be <c>null</c> to remove it, and a
    /// keyed list's elements may carry <c>$remove</c> (<see cref="MergePatch"/>).
    /// </summary>
    public static JsonObject JsonSchema(Type type, bool patch = false)
    {
        var exporter = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) => Transform(context, node, patch),
        };
        var schema = (JsonObject)PluginJson.Strict.GetJsonSchemaAsNode(type, exporter);
        var properties = (JsonObject)schema["properties"]!;
        properties.Insert(0, PluginSet.SchemaField, new JsonObject { ["type"] = "string" });
        if (patch)
        {
            Loosen(schema);
            properties.Remove("id");
            properties.Insert(1, "patch", IdSchema(nullable: false));
            schema["required"] = new JsonArray("patch");
        }
        schema.Insert(0, "$schema", "https://json-schema.org/draft/2020-12/schema");
        return schema;
    }

    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode node, bool patch)
    {
        var type = context.TypeInfo.Type;
        var nullable = Nullable.GetUnderlyingType(type) is { } underlying;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(RecordId))
            return IdSchema(nullable);
        if (type == typeof(System.Numerics.Vector3) || type == typeof(System.Numerics.Vector2))
            return Tuple(type == typeof(System.Numerics.Vector3) ? 3 : 2, new JsonObject { ["type"] = "number" }, nullable);
        if (type == typeof(Records.Rgba))
            return Tuple(4, new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 255 }, nullable);
        if (node is JsonObject array && array["items"] is JsonObject elements && context.PropertyInfo?.AttributeProvider is PropertyInfo member)
        {
            // A list of a nullable reference type, such as the sounds record's kinds, holds nulls; the exporter doesn't read that annotation.
            var nullability = new NullabilityInfoContext().Create(member);
            var element = nullability.ElementType ?? nullability.GenericTypeArguments.FirstOrDefault();
            if (element?.ReadState == NullabilityState.Nullable)
                AllowNull(elements);
        }
        if (context.TypeInfo.Kind == JsonTypeInfoKind.Object && node is JsonObject obj && obj["properties"] is JsonObject)
        {
            // Required is what strict reading requires (PluginJson.Strict), not every constructor parameter without a default:
            // a nullable one is left out of the file when it's null.
            var required = context.TypeInfo.Properties.Where(p => p.IsRequired).Select(p => (JsonNode?)p.Name).ToArray();
            if (required.Length > 0)
                obj["required"] = new JsonArray(required);
            else
                obj.Remove("required");
        }
        if (context.TypeInfo.PolymorphismOptions is { } polymorphism && node is JsonObject polymorphic && polymorphic["anyOf"] is JsonArray branches)
        {
            // The base kind, which has no discriminator, is a kind no record may be (PluginSet.Validate refuses an object without one).
            for (var i = branches.Count - 1; i >= 0; i--)
            {
                if (branches[i]?["properties"]?[polymorphism.TypeDiscriminatorPropertyName]?["const"] is null)
                    branches.RemoveAt(i);
            }
        }
        if (patch && context.PropertyInfo?.AttributeProvider?.IsDefined(typeof(KeyedListAttribute), inherit: true) == true && node is JsonObject list)
        {
            // A keyed list's element may say "$remove": true, in every kind the element may be.
            foreach (var element in list["items"] is JsonObject items ? (items["anyOf"] as JsonArray)?.OfType<JsonObject>() ?? [items] : [])
            {
                if (element["properties"] is JsonObject elementProperties)
                    elementProperties.Insert(0, MergePatch.RemoveField, new JsonObject { ["type"] = "boolean" });
            }
        }
        return node;
    }

    private static JsonObject IdSchema(bool nullable) => new()
    {
        ["type"] = nullable ? new JsonArray("string", "null") : "string",
        ["pattern"] = IdPattern,
    };

    private static JsonObject Tuple(int count, JsonObject item, bool nullable) => new()
    {
        ["type"] = nullable ? new JsonArray("array", "null") : "array",
        ["items"] = item,
        ["minItems"] = count,
        ["maxItems"] = count,
    };

    /// <summary>Makes an object's schema a patch's: nothing required, and every property may be null, all the way down.</summary>
    private static void Loosen(JsonObject schema)
    {
        schema.Remove("required");
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (name, property) in properties)
            {
                if (property is JsonObject p && name != MergePatch.RemoveField)
                {
                    AllowNull(p);
                    Loosen(p);
                }
            }
        }
        foreach (var branch in (schema["anyOf"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [])
            Loosen(branch);
        if (schema["items"] is JsonObject items)
            Loosen(items);
    }

    private static void AllowNull(JsonObject property)
    {
        switch (property["type"])
        {
            case JsonValue value when value.TryGetValue(out string? one) && one != "null":
                property["type"] = new JsonArray(one, "null");
                break;
            case JsonArray types when !types.Any(t => (string?)t == "null"):
                types.Add("null");
                break;
            case null when property["enum"] is JsonArray values && !values.Any(v => v is null):
                values.Add(null);
                break;
        }
    }
}
