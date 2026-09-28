using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aidyn.Plugins.Records;

namespace Aidyn.Plugins;

/// <summary>
/// How plugin source is read and written: JSON with comments and trailing commas (ADR 0005),
/// camelCase fields, enums as names, IDs as strings and vectors as arrays. The importer writes
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
        Converters = { new RecordIdConverter(), new Vector3Converter(), new RgbaConverter(), new JsonStringEnumConverter() },
    };

    public static JsonDocumentOptions DocumentOptions { get; } = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class RecordIdConverter : JsonConverter<RecordId>
    {
        public override RecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            RecordId.TryParse(reader.GetString(), out var id, out var error) ? id : throw new JsonException(error);

        public override void Write(Utf8JsonWriter writer, RecordId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
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
