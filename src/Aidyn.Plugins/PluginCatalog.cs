using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aidyn.Plugins.Records;

namespace Aidyn.Plugins;

/// <summary>A record or asset in a <see cref="PluginCatalog"/>.</summary>
/// <param name="HasRecord">Whether a plugin defines the record. Scenes, textures and UI images are assets alone.</param>
/// <param name="Assets">Its asset files, by extension.</param>
/// <param name="Summary">A line about it for the editor's list, such as a map set's name and size.</param>
public sealed record CatalogEntry(RecordId Id, bool HasRecord, IReadOnlyList<AssetFile> Assets, string Summary)
{
    /// <summary>The Borg a base-game record or asset was imported from, for those keyed by archive index.</summary>
    public int? Borg => Id.Namespace == RecordId.BaseNamespace && IsBorgType(Id.Type) && Id.Key.StartsWith("0x", StringComparison.Ordinal)
        ? int.Parse(Id.Key.AsSpan(2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
        : null;

    public AssetFile? Asset(string extension) => Assets.FirstOrDefault(a => a.Extension == extension);

    private static bool IsBorgType(string type) => type is RecordTypes.Texture or RecordTypes.Geometry or RecordTypes.Camera
        or RecordTypes.Light or RecordTypes.Scene or RecordTypes.Animation or RecordTypes.Actor or RecordTypes.Image
        or RecordTypes.Map or RecordTypes.Sample or RecordTypes.Sequence or RecordTypes.Dialogue;
}

/// <summary>Where a record names another: the naming record, and the field's path in it, such as <c>objects[3].models[0].model</c>.</summary>
public sealed record RecordReference(RecordId From, string Path);

/// <summary>
/// What the editor browses in a <see cref="PluginSet"/>: every record and asset, with a summary
/// line each, and which records name which (Bethesda's "use info"). Any string field that parses
/// as a record ID counts as a reference, so new record types need no code here.
/// </summary>
public sealed class PluginCatalog
{
    private readonly Dictionary<RecordId, CatalogEntry> _entries;
    private readonly Dictionary<RecordId, List<RecordReference>> _references;

    private PluginCatalog(PluginSet plugins, Dictionary<RecordId, CatalogEntry> entries, Dictionary<RecordId, List<RecordReference>> references)
    {
        Plugins = plugins;
        _entries = entries;
        _references = references;
        Entries = [.. entries.Values.OrderBy(e => e.Id.Type, StringComparer.Ordinal).ThenBy(e => e.Id.Namespace, StringComparer.Ordinal)
            .ThenBy(e => e.Id.Key.Length).ThenBy(e => e.Id.Key, StringComparer.Ordinal)];
    }

    public PluginSet Plugins { get; }

    /// <summary>By type, then namespace, then key.</summary>
    public IReadOnlyList<CatalogEntry> Entries { get; }

    public IEnumerable<string> Types => Entries.Select(e => e.Id.Type).Distinct();

    public CatalogEntry? Find(RecordId id) => _entries.GetValueOrDefault(id);

    /// <summary>The records that name <paramref name="id"/>, whether or not it exists.</summary>
    public IReadOnlyList<RecordReference> ReferencesTo(RecordId id) => _references.GetValueOrDefault(id) ?? [];

    /// <summary>Reads every record and lists every asset. The base plugin takes a second or two.</summary>
    public static PluginCatalog Build(PluginSet plugins)
    {
        var references = new Dictionary<RecordId, List<RecordReference>>();
        foreach (var id in plugins.Ids)
        {
            foreach (var (field, value) in plugins.GetJson(id)!)
            {
                if (field != "id")
                    FindReferences(id, field, value, references);
            }
        }

        var assets = plugins.Assets().ToLookup(a => a.Id);
        var ids = plugins.Ids.Concat(assets.Select(g => g.Key)).ToHashSet();
        var placements = MapPlacements.For(plugins);
        var entries = new Dictionary<RecordId, CatalogEntry>();
        foreach (var id in ids)
        {
            var files = assets[id].OrderBy(a => a.Extension, StringComparer.Ordinal).ToList();
            var json = plugins.GetJson(id);
            string summary;
            try
            {
                summary = Summary(id, json, files, placements, references.GetValueOrDefault(id)?.Count ?? 0);
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException or IOException)
            {
                summary = $"‹{e.Message}›";
            }
            entries[id] = new CatalogEntry(id, json is not null, files, summary);
        }
        return new PluginCatalog(plugins, entries, references);
    }

    private static void FindReferences(RecordId from, string path, JsonNode? node, Dictionary<RecordId, List<RecordReference>> references)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o)
                    FindReferences(from, $"{path}.{key}", value, references);
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                    FindReferences(from, $"{path}[{i}]", a[i], references);
                break;
            case JsonValue v when v.TryGetValue(out string? text) && text.Contains(':', StringComparison.Ordinal) && RecordId.TryParse(text, out var to):
                if (!references.TryGetValue(to, out var list))
                    references[to] = list = [];
                list.Add(new RecordReference(from, path));
                break;
        }
    }

    /// <summary>
    /// What a map object is, such as "Monster party: Male Commoner" or "Teleporter patioL2": its kind's name
    /// (<see cref="MapObjectKinds"/>), and the one detail that tells it from others of its kind.
    /// </summary>
    public string Describe(MapObject? o) => o switch
    {
        DialogueObject d => $"Dialogue object {d.Name}",
        MonsterPartyObject m => $"Monster party: {Find(m.Entity)?.Summary ?? m.Entity.ToString()}",
        TeleporterObject t => $"Teleporter {t.Name}",
        ContainerObject c => c.Kind.ToString(),
        TriggerObject t => $"{t.Kind} trigger",
        ReferencePointObject r => $"Reference point {r.Name}",
        SceneObject s => s.Models.Count > 0 ? $"Scene object: {s.Models[0].Model}" : "Scene object",
        { } other => MapObjectKinds.Name(other),
        null => "Object",
    };

    /// <summary>
    /// What a voxel ID names and where, such as "Teleporter patioL2, in JundarInteriors K07". Where is every chunk that
    /// places its map, joined by " / ": the battle arenas and a few overworld maps are placed by several.
    /// </summary>
    public string Describe(RecordId voxel)
    {
        var map = MapRecord.MapOf(voxel);
        var places = MapPlacements.For(Plugins).All(map);
        var where = places.Count > 0 ? string.Join(" / ", places) : $"map {map.Key}";
        return $"{Describe(MapRecord.Find(Plugins, voxel))}, in {where}";
    }

    private static string Summary(RecordId id, JsonObject? json, List<AssetFile> files, MapPlacements placements, int usedBy)
    {
        var parts = new List<string>();
        switch (id.Type)
        {
            case RecordTypes.MapSet when json is not null:
                parts.Add($"{(string?)json["label"]}, {Count(json["chunks"], "chunk")}");
                break;
            case RecordTypes.Map:
                parts.AddRange(placements.All(id).Select(p => p.ToString()));
                if (json?["objects"] is JsonArray objects)
                {
                    parts.Add(objects.Count == 0 ? "no objects" : string.Join(", ", objects.OfType<JsonObject>()
                        .GroupBy(o => (string?)o["type"] ?? "?").Select(g => Plural(g.Count(), MapObjectKinds.Name(g.Key).ToLowerInvariant()))));
                }
                break;
            case RecordTypes.Actor when json is not null:
                parts.Add($"{Count(json["states"], "state")}, scene {Key(json["scene"])}");
                if (json["head"] is JsonObject head)
                    parts.Add($"head {Key(head["scene"])}");
                break;
            case RecordTypes.Terrain when json is not null:
                parts.Add($"sky {Key(json["sky"])}" + ((bool?)json["skyDrawn"] == false ? ", not drawn" : ""));
                break;
            case RecordTypes.Flag when json is not null:
                if (json["formula"] is JsonObject formula)
                    parts.Add("derived: " + formula.Deserialize<Records.FlagFormula>(PluginJson.Options)!.Describe());
                else if ((bool?)json["initial"] == true)
                    parts.Add("starts set");
                if ((string?)json["label"] is { } label)
                    parts.Add(label);
                if (json["journal"] is { } journal)
                    parts.Add($"journal entry {Key(journal)}");
                break;
            case RecordTypes.Journal when json is not null:
                parts.Add($"flag {Key(json["flag"])}: {Truncate((string?)json["text"] ?? "", 80)}");
                break;
            case RecordTypes.Dialogue when json is not null:
                var nodes = json["nodes"] as JsonArray ?? [];
                var first = nodes.OfType<JsonObject>().Select(n => (string?)n["text"]).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                parts.Add($"{(string?)json["mode"]}, {Count(nodes, "node")}" + (first is null ? "" : $": {Truncate(first.Replace("\n", " ", StringComparison.Ordinal), 60)}"));
                break;
            case RecordTypes.Entity or RecordTypes.DialogEntity or RecordTypes.Weapon or RecordTypes.Armor or RecordTypes.Spell
                or RecordTypes.Item or RecordTypes.Loot when json is not null:
                parts.Add((string?)json["name"] ?? "");
                break;
            case RecordTypes.Stat when json is not null:
                parts.Add($"{(string?)json["name"]}, cap {(int?)json["baseCap"]}/{(int?)json["cap"]}");
                break;
            case RecordTypes.Skill or RecordTypes.WeaponSkill when json is not null:
                parts.Add($"{(string?)json["name"]}, training cost {(int?)json["trainingCost"]}");
                break;
            case RecordTypes.Progression when json is not null:
                parts.Add(Count(json["levels"], "level"));
                break;
            case RecordTypes.Sky when json is not null:
                parts.Add($"{Count(json["stars"], "star")}, {Count(json["clouds"], "cloud")}, {Count(json["lensFlare"], "flare piece")}");
                break;
        }
        if (files.FirstOrDefault(f => f.Extension == "png") is { } png && PngSize(png.Path) is var (w, h))
            parts.Add($"{w}×{h}");
        if (json is null && parts.Count == 0)
            parts.Add(usedBy == 0 ? "used by nothing" : $"used by {Plural(usedBy, "record")}");
        return string.Join(", ", parts);
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    private static string Key(JsonNode? id) => (string?)id is { } s && RecordId.TryParse(s, out var r) ? r.Key : "?";

    private static string Count(JsonNode? list, string one) => Plural((list as JsonArray)?.Count ?? 0, one);

    private static string Plural(int n, string one) => string.Create(CultureInfo.InvariantCulture,
        $"{n:N0} {(n == 1 ? one : one.EndsWith('y') ? one[..^1] + "ies" : one + "s")}");

    /// <summary>A PNG's size, from its header.</summary>
    private static (int Width, int Height)? PngSize(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var file = File.OpenRead(path);
        if (file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length || header[12..16].SequenceCompareTo("IHDR"u8) != 0)
            return null;
        return (BinaryPrimitives.ReadInt32BigEndian(header[16..]), BinaryPrimitives.ReadInt32BigEndian(header[20..]));
    }
}
