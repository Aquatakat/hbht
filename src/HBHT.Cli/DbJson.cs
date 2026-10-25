using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HBHT.Rom.Db;

/// <summary>JSON for inspecting DB dumps: IDs in their ADR 0004 spelling and enums by name.</summary>
internal static class DbJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new ItemIdConverter(), new JsonStringEnumConverter() },
    };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);

    private sealed class ItemIdConverter : JsonConverter<ItemId>
    {
        public override ItemId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, ItemId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
