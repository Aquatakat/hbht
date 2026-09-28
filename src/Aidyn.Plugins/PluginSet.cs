using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aidyn.Plugins;

/// <summary>A plugin folder and its manifest.</summary>
public sealed record Plugin(string Directory, PluginManifest Manifest)
{
    public static Plugin Open(string directory)
    {
        var path = Path.Combine(directory, PluginManifest.FileName);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllBytes(path), PluginJson.Options)
            ?? throw new InvalidDataException($"{path} is empty.");
        if (!RecordId.TryParse($"{manifest.Id}:flag/0", out _))
            throw new InvalidDataException($"{path}: \"{manifest.Id}\" isn't a valid plugin ID ([a-z0-9_]+).");
        return new Plugin(directory, manifest);
    }
}

/// <summary>A field that more than one plugin set, and which one won (ADR 0005's conflict report).</summary>
public sealed record FieldConflict(RecordId Record, string Field, IReadOnlyList<string> Plugins)
{
    public string Winner => Plugins[^1];
}

/// <summary>
/// The records and assets of plugins loaded in order, merged field by field (ADR 0005).
/// </summary>
/// <remarks>
/// A record file under <c>records/</c> holds one object or an array of them. An object with an
/// <c>id</c> is a new record in its plugin's namespace; one with a <c>patch</c> merges into an
/// earlier plugin's record as a JSON Merge Patch (RFC 7386). Lists are replaced whole for now:
/// element-wise merging of keyed lists needs the record schema, which comes with Phase 5.
/// </remarks>
public sealed class PluginSet
{
    private readonly Dictionary<RecordId, JsonObject> _records = [];
    private readonly Dictionary<(RecordId, string), List<string>> _setters = [];

    private PluginSet(IReadOnlyList<Plugin> plugins) => Plugins = plugins;

    /// <summary>In load order.</summary>
    public IReadOnlyList<Plugin> Plugins { get; }

    public IEnumerable<RecordId> Ids => _records.Keys;

    /// <summary>Every top-level field that more than one plugin set.</summary>
    public IEnumerable<FieldConflict> Conflicts =>
        _setters.Where(s => s.Value.Count > 1).Select(s => new FieldConflict(s.Key.Item1, s.Key.Item2, s.Value));

    /// <summary>Loads plugin folders in the order given.</summary>
    public static PluginSet Load(IEnumerable<string> directories)
    {
        var set = new PluginSet([.. directories.Select(Plugin.Open)]);
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in set.Plugins)
        {
            foreach (var dependency in plugin.Manifest.Dependencies ?? [])
            {
                if (!loaded.Contains(dependency))
                    throw new InvalidDataException($"Plugin {plugin.Manifest.Id} needs {dependency} to load before it.");
            }
            if (!loaded.Add(plugin.Manifest.Id))
                throw new InvalidDataException($"Plugin {plugin.Manifest.Id} is loaded twice.");
            set.AddRecords(plugin);
        }
        return set;
    }

    /// <summary>
    /// Loads the base plugin and every other plugin in <paramref name="pluginsDirectory"/>: the base
    /// first, then the rest by ID. Load-order rules beyond that come with Phase 5.
    /// </summary>
    public static PluginSet LoadInstalled(string pluginsDirectory)
    {
        var baseDir = Path.Combine(pluginsDirectory, BasePlugin.Folder);
        var others = Directory.EnumerateDirectories(pluginsDirectory)
            .Where(d => !string.Equals(Path.GetFileName(d), BasePlugin.Folder, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(d, PluginManifest.FileName)))
            .Order(StringComparer.Ordinal);
        return Load([baseDir, .. others]);
    }

    public bool Contains(RecordId id) => _records.ContainsKey(id);

    /// <summary>The merged record as JSON.</summary>
    public JsonObject? GetJson(RecordId id) => _records.GetValueOrDefault(id);

    /// <summary>The merged record, read as <typeparamref name="T"/>.</summary>
    public T Get<T>(RecordId id) where T : class =>
        TryGet<T>(id) ?? throw new KeyNotFoundException($"No record {id}.");

    public T? TryGet<T>(RecordId id) where T : class =>
        _records.TryGetValue(id, out var json) ? json.Deserialize<T>(PluginJson.Options) : null;

    /// <summary>Every merged record of a type.</summary>
    public IEnumerable<T> All<T>(string type) where T : class =>
        _records.Where(r => r.Key.Type == type).OrderBy(r => r.Key.ToString(), StringComparer.Ordinal)
            .Select(r => r.Value.Deserialize<T>(PluginJson.Options)!);

    /// <summary>
    /// The file that provides <paramref name="id"/>'s asset, from the last plugin that has one:
    /// <c>assets/&lt;namespace&gt;/&lt;type&gt;/&lt;key&gt;.&lt;extension&gt;</c>.
    /// </summary>
    public string? FindAsset(RecordId id, string extension)
    {
        var relative = Path.Combine([id.Namespace, id.Type, .. id.Key.Split('/')]) + "." + extension;
        for (var i = Plugins.Count - 1; i >= 0; i--)
        {
            var path = Path.Combine(Plugins[i].Directory, "assets", relative);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private void AddRecords(Plugin plugin)
    {
        var records = Path.Combine(plugin.Directory, "records");
        if (!Directory.Exists(records))
            return;
        var files = Directory.EnumerateFiles(records, "*.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var node = JsonNode.Parse(File.ReadAllBytes(file), documentOptions: PluginJson.DocumentOptions);
            var objects = node switch
            {
                JsonArray array => array.Select(n => n as JsonObject),
                JsonObject o => [o],
                _ => [null],
            };
            foreach (var o in objects)
            {
                if (o is null)
                    throw new InvalidDataException($"{file}: a record file holds objects, or an array of them.");
                Add(plugin.Manifest.Id, file, o);
            }
        }
    }

    private void Add(string pluginId, string file, JsonObject o)
    {
        if (o["id"] is JsonValue idValue && o["patch"] is null)
        {
            var id = ParseId(file, idValue);
            if (id.Namespace != pluginId)
                throw new InvalidDataException($"{file}: {pluginId} can only add records in its own namespace, not {id}. Patch it instead.");
            if (!_records.TryAdd(id, (JsonObject)o.DeepClone()))
                throw new InvalidDataException($"{file}: {id} is defined twice.");
            foreach (var (field, _) in o)
            {
                if (field != "id")
                    Setters(id, field).Add(pluginId);
            }
        }
        else if (o["patch"] is JsonValue patchValue && o["id"] is null)
        {
            var id = ParseId(file, patchValue);
            if (!_records.TryGetValue(id, out var target))
                throw new InvalidDataException($"{file}: patches {id}, which no earlier plugin defines.");
            foreach (var (field, value) in o)
            {
                if (field == "patch")
                    continue;
                MergePatch.Apply(target, field, value);
                var setters = Setters(id, field);
                if (setters.Count == 0 || setters[^1] != pluginId)
                    setters.Add(pluginId);
            }
        }
        else
        {
            throw new InvalidDataException($"{file}: each object needs exactly one of \"id\" and \"patch\".");
        }
    }

    private List<string> Setters(RecordId id, string field)
    {
        if (!_setters.TryGetValue((id, field), out var list))
            _setters[(id, field)] = list = [];
        return list;
    }

    private static RecordId ParseId(string file, JsonValue value) =>
        value.TryGetValue(out string? text) && RecordId.TryParse(text, out var id, out var error)
            ? id
            : throw new InvalidDataException($"{file}: \"{value}\" isn't a record ID.");
}

/// <summary>JSON Merge Patch (RFC 7386).</summary>
public static class MergePatch
{
    /// <summary>Merges <paramref name="patch"/> into <paramref name="target"/>'s field <paramref name="field"/>.</summary>
    public static void Apply(JsonObject target, string field, JsonNode? patch)
    {
        if (patch is null)
        {
            target.Remove(field);
        }
        else if (patch is JsonObject patchObject)
        {
            if (target[field] is not JsonObject existing)
                target[field] = existing = [];
            foreach (var (key, value) in patchObject)
                Apply(existing, key, value);
        }
        else
        {
            target[field] = patch.DeepClone();
        }
    }
}
