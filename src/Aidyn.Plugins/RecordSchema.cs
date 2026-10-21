using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
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
}
