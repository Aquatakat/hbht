using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using HBHT.Plugins.Records;

namespace HBHT.Plugins.Saves;

/// <summary>What reading a save from an older format did to it (ADR 0037).</summary>
/// <param name="From">The format it was written in.</param>
/// <param name="Dropped">What couldn't be brought up and was left out, a line each, which loading adds to its problems.</param>
public sealed record SaveUpconversion(int From, IReadOnlyList<string> Dropped);

/// <summary>
/// Brings a format-1 save up to format 2 (ADR 0037) by rewriting its JSON before it's read: format 1 is the save as the port wrote
/// it before the data format overhaul (ADRs 0031–0036), which renamed the IDs it names and one field. Nothing is asked of the
/// player: what can't be brought up is left out and said in <see cref="SaveUpconversion.Dropped"/>.
/// </summary>
public static class SaveUpconverter
{
    /// <summary>The four dialogue entities that merged into the entity of the same name (ADR 0032), by key.</summary>
    public static IReadOnlyDictionary<string, string> Twins { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["0x1404"] = "0x02DD", // Cadme
        ["0x1406"] = "0x0224", // Pochanargat
        ["0x1418"] = "0x0214", // Tamberlain
        ["0x1419"] = "0x02DB", // Xibid
    };

    /// <summary>
    /// Rewrites a format-1 save's JSON as format 2, in place, and says what it left out. The map objects' chunks come from
    /// <paramref name="plugins"/>, which are asked only if the save names a map object; without them, its memories are left out.
    /// </summary>
    public static IReadOnlyList<string> FromFormat1(JsonObject save, PluginSet? plugins)
    {
        var dropped = new List<string>();
        var place = Place(save);

        if (save["memories"] is JsonArray memories)
        {
            for (var i = memories.Count - 1; i >= 0; i--)
            {
                if (memories[i] is not JsonObject memory || memory["mapObject"] is not JsonValue value || !value.TryGetValue(out string? id))
                    continue;
                if (!IsType(id, "voxel"))
                    continue;
                if (MapObject(id, place, plugins, out var why) is { } moved)
                    memory["mapObject"] = moved;
                else
                {
                    dropped.Add($"Its memory of {id} ({memory["kind"]}) was left out: {why}.");
                    memories.RemoveAt(i);
                }
            }
        }

        if (save["minimaps"] is JsonArray minimaps)
        {
            foreach (var minimap in minimaps.OfType<JsonObject>())
            {
                if (minimap["minimap"] is { } old && minimap["mapSet"] is null)
                {
                    minimap.Remove("minimap");
                    minimap["mapSet"] = old;
                }
            }
        }

        if (save["place"] is JsonObject saved && saved["chunk"] is JsonValue chunk && chunk.TryGetValue(out string? name)
            && name.Split(',') is [var r, var c]
            && int.TryParse(r, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var row)
            && int.TryParse(c, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var column))
            saved["chunk"] = ChunkRecord.NameOf(row, column);

        Rename(save);
        save["format"] = 2;
        return dropped;
    }

    /// <summary>A record ID as format 2 names it: the renames of ADRs 0031, 0032, 0034 and 0035 that need nothing but the ID.</summary>
    public static string Rename(string id)
    {
        if (Split(id) is not (var ns, var type, var key))
            return id;
        return type switch
        {
            "weapon" or "armor" => $"{ns}:{RecordTypes.Item}/{key}",
            "dialogentity" => $"{ns}:{RecordTypes.Entity}/{(ns == RecordId.BaseNamespace && Twins.TryGetValue(key, out var twin) ? twin : key)}",
            "dialogue" => $"{ns}:{RecordTypes.Script}/{key}",
            "minimap" => $"{ns}:{RecordTypes.MapSet}/{key}",
            _ => id,
        };
    }

    // Every string value in the save, wherever it is: format 1's IDs are renamed wherever they're named.
    private static void Rename(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var name in o.Select(p => p.Key).ToList())
                {
                    if (o[name] is JsonValue v && v.TryGetValue(out string? text))
                    {
                        var renamed = Rename(text);
                        if (!ReferenceEquals(renamed, text))
                            o[name] = renamed;
                    }
                    else
                        Rename(o[name]);
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    if (a[i] is JsonValue v && v.TryGetValue(out string? text))
                    {
                        var renamed = Rename(text);
                        if (!ReferenceEquals(renamed, text))
                            a[i] = renamed;
                    }
                    else
                        Rename(a[i]);
                }
                break;
        }
    }

    /// <summary>
    /// A format-1 map object's ID, <c>aidyn:voxel/&lt;borg&gt;/&lt;n&gt;</c>, as format 2's, <c>aidyn:mapobject/&lt;set&gt;/&lt;name&gt;/&lt;n&gt;</c>:
    /// the one chunk that came from that Borg9, or, of several, the one the save stands in. Null, with why, for none.
    /// </summary>
    public static string? MapObject(string voxel, RecordId? place, PluginSet? plugins, out string why)
    {
        why = "";
        if (Split(voxel) is not (var ns, _, var key) || key.Split('/') is not [var borg, var index]
            || !int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            why = "it isn't a map object's ID";
            return null;
        }
        if (ns != RecordId.BaseNamespace)
        {
            why = "it's a mod's, which format 1 named by a map no chunk records";
            return null;
        }
        if (plugins is null)
        {
            why = "no plugins were given to find its chunk by";
            return null;
        }
        var chunks = Borg9Chunks.For(plugins).Of(borg);
        RecordId? chunk = chunks.Count switch
        {
            0 => null,
            1 => chunks[0],
            _ => place is { } p && chunks.Contains(p) ? p : null,
        };
        if (chunk is not { } found)
        {
            why = chunks.Count == 0
                ? $"no chunk any plugin has came from Borg9 {borg}"
                : $"Borg9 {borg} is {chunks.Count} chunks', and the save doesn't stand in one of them";
            return null;
        }
        return ChunkRecord.ObjectId(found, n).ToString();
    }

    // The base chunk the save's place is in, if it names one.
    private static RecordId? Place(JsonObject save)
    {
        if (save["place"] is not JsonObject place || place["mapSet"] is not JsonValue set || !set.TryGetValue(out string? mapSet)
            || place["chunk"] is not JsonValue chunk || !chunk.TryGetValue(out string? name))
            return null;
        if (Split(mapSet) is not (RecordId.BaseNamespace, RecordTypes.MapSet, var key)
            || !int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return null;
        var cell = name.Split(',') is [var r, var c]
            && int.TryParse(r, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var row)
            && int.TryParse(c, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var column)
                ? (row, column)
                : ChunkRecord.CellOf(name);
        return cell is (var rr, var cc) ? ChunkRecord.IdOf(number, rr, cc) : null;
    }

    private static bool IsType(string id, string type) => Split(id) is (_, var t, _) && t == type;

    // namespace:type/key, without asking whether the type is one: format 1's types aren't all.
    private static (string Namespace, string Type, string Key)? Split(string text)
    {
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var slash = colon < 0 ? -1 : text.IndexOf('/', colon + 1);
        return colon > 0 && slash > colon + 1 && slash < text.Length - 1 && !text.Contains(' ', StringComparison.Ordinal)
            ? (text[..colon], text[(colon + 1)..slash], text[(slash + 1)..])
            : null;
    }

    /// <summary>
    /// Which base chunks came from each Borg9, by each chunk's <c>borg</c>, made once a plugin set and only when a format-1 save
    /// names a map object, so reading a format-2 save never pays for it. Finding it scans the chunks' files without keeping them.
    /// </summary>
    private sealed class Borg9Chunks
    {
        private static readonly ConditionalWeakTable<PluginSet, Borg9Chunks> Cache = [];

        private readonly Dictionary<int, List<RecordId>> _chunks = [];

        private Borg9Chunks(PluginSet plugins)
        {
            foreach (var id in plugins.IdsOf(RecordTypes.Chunk))
            {
                if (id.Namespace == RecordId.BaseNamespace && Number(plugins.PeekString(id, "borg")) is { } borg)
                {
                    if (!_chunks.TryGetValue(borg, out var list))
                        _chunks[borg] = list = [];
                    list.Add(id);
                }
            }
        }

        public static Borg9Chunks For(PluginSet plugins) => Cache.GetValue(plugins, p => new Borg9Chunks(p));

        public IReadOnlyList<RecordId> Of(string borg) => Number(borg) is { } n && _chunks.TryGetValue(n, out var list) ? list : [];

        private static int? Number(string? hex) =>
            hex is { Length: > 2 } && hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(hex.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)
                ? n
                : null;
    }
}
