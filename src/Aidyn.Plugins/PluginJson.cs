using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Aidyn.Plugins.Records;

namespace Aidyn.Plugins;

/// <summary>
/// How plugin source is read and written: JSON with comments and trailing commas (ADR 0005),
/// camelCase fields, enums as names, IDs as strings and vectors as arrays. Saves use the same. The importer writes
/// indented output so that two imports diff cleanly.
/// </summary>
public static class PluginJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        IndentSize = 1,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new RecordIdConverter(), new Vector2Converter(), new Vector3Converter(), new RgbaConverter(), new JsonStringEnumConverter() },
    };

    public static JsonDocumentOptions DocumentOptions { get; } = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// <see cref="Options"/> that refuse what those overlook: a field the record type lacks, a <c>type</c> discriminator none
    /// of its kinds has, a null where the type allows none, and a missing field with no default (a constructor parameter
    /// that's neither nullable nor defaulted nor left out of the writer's output). <see cref="PluginSet.Validate"/> reads every
    /// record with them, and the JSON Schema is exported from them, so a schema says what validation says. Reads stay with
    /// <see cref="Options"/>: a view such as <see cref="DbRecord"/> reads part of a record, and a save skips what it doesn't know.
    /// </summary>
    public static JsonSerializerOptions Strict { get; } = new(Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { Stricten } },
    };

    private static void Stricten(JsonTypeInfo info)
    {
        if (info.PolymorphismOptions is { } polymorphism)
            polymorphism.IgnoreUnrecognizedTypeDiscriminators = false;
        foreach (var property in info.Properties)
        {
            // ShouldSerialize is set by a JsonIgnore condition: such a field is left out when it's the default, so it isn't required.
            if (property.AssociatedParameter is { HasDefaultValue: false, IsNullable: false } && property.ShouldSerialize is null)
                property.IsRequired = true;
        }
    }

    private sealed class RecordIdConverter : JsonConverter<RecordId>
    {
        public override RecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            RecordId.TryParse(reader.GetString(), out var id, out var error) ? id : throw new JsonException(error);

        public override void Write(Utf8JsonWriter writer, RecordId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class Vector2Converter : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = JsonSerializer.Deserialize<float[]>(ref reader, options);
            return values is [var x, var y] ? new Vector2(x, y) : throw new JsonException("A 2D vector is [x, y].");
        }

        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options) =>
            writer.WriteRawValue($"[{JsonSerializer.Serialize(value.X)}, {JsonSerializer.Serialize(value.Y)}]");
    }

    private sealed class Vector3Converter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = JsonSerializer.Deserialize<float[]>(ref reader, options);
            return values is [var x, var y, var z] ? new Vector3(x, y, z) : throw new JsonException("A vector is [x, y, z].");
        }

        // On one line, even in indented output.
        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) =>
            writer.WriteRawValue($"[{Number(value.X)}, {Number(value.Y)}, {Number(value.Z)}]");

        private static string Number(float value) => JsonSerializer.Serialize(value);
    }

    private sealed class RgbaConverter : JsonConverter<Rgba>
    {
        public override Rgba Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Not byte[], which JSON reads as base64.
            var values = JsonSerializer.Deserialize<int[]>(ref reader, options);
            return values is [var r, var g, var b, var a] && values.All(v => v is >= 0 and <= 255)
                ? new Rgba((byte)r, (byte)g, (byte)b, (byte)a)
                : throw new JsonException("A colour is [r, g, b, a], 0–255.");
        }

        public override void Write(Utf8JsonWriter writer, Rgba value, JsonSerializerOptions options) =>
            writer.WriteRawValue($"[{value.R}, {value.G}, {value.B}, {value.A}]");
    }
}
