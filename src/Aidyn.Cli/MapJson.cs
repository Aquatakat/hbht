using System.Numerics;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Aidyn.Rom.Db;
using Aidyn.Rom.Maps;

/// <summary>
/// A map set's chunks and map objects as JSON, for <c>tools/map-viewer.html</c>. Its collision
/// goes in a glTF file beside it (<see cref="CollisionGltf"/>).
/// </summary>
internal static class MapJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new Vector3Converter(), new ItemIdConverter(), new JsonStringEnumConverter(), new TupleConverterFactory() },
    };

    /// <param name="actorScenes">The Borg5 each Borg7 animates, to draw actors placed as scene objects.</param>
    /// <param name="names">Names of the entities that monster parties name.</param>
    public static byte[] Serialize(string version, MapSet set, IEnumerable<(MapChunk Chunk, Borg9Map Map)> chunks,
        IReadOnlyDictionary<int, int> actorScenes, IReadOnlyDictionary<ItemId, string> names)
    {
        var usedNames = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var chunkArray = new JsonArray();
        foreach (var (chunk, map) in chunks)
        {
            var objects = new JsonArray();
            foreach (var o in map.Objects)
            {
                var json = new JsonObject
                {
                    ["index"] = o.Index,
                    ["type"] = o.Type.ToString(),
                    ["position"] = Vector(o.Position),
                    ["radius"] = o.Radius,
                    ["flags"] = o.Flags.ToString(),
                    ["links"] = new JsonArray(o.Links.Where(l => l >= 0).Select(l => (JsonNode)(int)l).ToArray()),
                    ["flagA"] = o.FlagA,
                    ["flagB"] = o.FlagB,
                    ["flagC"] = o.FlagC,
                    ["visible"] = o.Visible.ToString(),
                    ["enabled"] = o.Enabled.ToString(),
                };
                if (o.Data is { } data)
                    json["data"] = JsonSerializer.SerializeToNode(data, data.GetType(), Options);
                if (o.Data is SceneData scene && scene.Models.Count > 0)
                {
                    // What the viewer draws: the nearest level of detail, or the actor's scene.
                    var model = scene.Models[0].Borg;
                    json["draw"] = scene.SceneFlags.HasFlag(SceneObjectFlags.Actor) ? actorScenes[model] : model;
                    var q = scene.Orientation;
                    json["orientation"] = new JsonArray(q.X, q.Y, q.Z, q.W);
                    json["scale"] = Vector(scene.Scale);
                }
                if (o.Data is MonsterPartyData party)
                {
                    foreach (var id in party.Enemies.Select(e => e.Enemy).Append(party.Entity))
                        if (names.TryGetValue(id, out var name))
                            usedNames[id.ToString()] = name;
                }
                objects.Add(json);
            }

            chunkArray.Add(new JsonObject
            {
                ["name"] = chunk.Name,
                ["row"] = chunk.Row,
                ["column"] = chunk.Column,
                ["origin"] = Vector(MapSets.ChunkOrigin(chunk)),
                ["scene"] = chunk.Scene,
                ["altScene"] = chunk.AltScene,
                ["borg9"] = chunk.Collision,
                ["terrain"] = chunk.Terrain,
                ["triangles"] = map.Triangles.Count,
                ["grid"] = new JsonObject
                {
                    ["origin"] = Vector(map.Origin),
                    ["cellSize"] = new JsonArray(map.CellSize.X, map.CellSize.Y),
                    ["columns"] = map.Columns,
                    ["rows"] = map.Rows,
                },
                ["objects"] = objects,
            });
        }

        var root = new JsonObject
        {
            ["version"] = version,
            ["set"] = set.ToString(),
            ["cellSize"] = MapSets.CellSize(set),
            ["names"] = new JsonObject(usedNames.Select(n => KeyValuePair.Create(n.Key, (JsonNode?)n.Value))),
            ["chunks"] = chunkArray,
        };
        return JsonSerializer.SerializeToUtf8Bytes(root, Options);
    }

    private static JsonArray Vector(Vector3 v) => new(v.X, v.Y, v.Z);

    private sealed class Vector3Converter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteNumberValue(value.Z);
            writer.WriteEndArray();
        }
    }

    private sealed class ItemIdConverter : JsonConverter<ItemId>
    {
        public override ItemId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, ItemId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    /// <summary>Writes value tuples, such as a teleporter's destination, as arrays.</summary>
    private sealed class TupleConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type) =>
            type.IsValueType && type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal);

        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(TupleConverter<>).MakeGenericType(type))!;

        private sealed class TupleConverter<T> : JsonConverter<T>
        {
            private static readonly FieldInfo[] Items = [.. typeof(T).GetFields().OrderBy(f => f.Name, StringComparer.Ordinal)];

            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                throw new NotSupportedException();

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                foreach (var item in Items)
                    JsonSerializer.Serialize(writer, item.GetValue(value), item.FieldType, options);
                writer.WriteEndArray();
            }
        }
    }
}
