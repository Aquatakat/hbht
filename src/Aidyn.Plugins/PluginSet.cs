using System.Collections.Concurrent;
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

/// <summary>A record's asset file: the one <see cref="PluginSet.FindAsset"/> picks.</summary>
/// <param name="Plugin">The ID of the plugin it comes from.</param>
public sealed record AssetFile(RecordId Id, string Extension, string Path, string Plugin);

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
    private readonly Dictionary<RecordId, string> _definedBy = [];

    // Records read as types, once each and after the merge: nothing changes the merged JSON once the set has loaded.
    // The runtime's audio thread and the editor's loading tasks read records as well as the main thread.
    private readonly ConcurrentDictionary<(RecordId, Type), object> _typed = new();
    private readonly ConcurrentDictionary<(string Type, Type), object> _typedAll = new();

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

    /// <summary>The ID of the plugin that defines the record, or null if none does.</summary>
    public string? DefinedBy(RecordId id) => _definedBy.GetValueOrDefault(id);

    /// <summary>The plugins that set one of the record's top-level fields, in load order. The last one's value holds.</summary>
    public IReadOnlyList<string> Setters(RecordId id, string field) => _setters.GetValueOrDefault((id, field)) ?? [];

    /// <summary>
    /// Every asset file, from the plugin that <see cref="FindAsset"/> would take it from. Files whose
    /// path isn't a record ID are left out.
    /// </summary>
    public IEnumerable<AssetFile> Assets()
    {
        var found = new Dictionary<(RecordId, string), AssetFile>();
        foreach (var plugin in Plugins)
        {
            var root = Path.Combine(plugin.Directory, "assets");
            if (!Directory.Exists(root))
                continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                // assets/<namespace>/<type>/<key>.<extension>, with nested keys as folders.
                var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var extension = Path.GetExtension(path);
                if (parts.Length < 3 || extension.Length < 2)
                    continue;
                parts[^1] = Path.GetFileNameWithoutExtension(parts[^1]);
                if (!RecordId.TryParse($"{parts[0]}:{parts[1]}/{string.Join('/', parts[2..])}", out var id))
                    continue;
                found[(id, extension[1..])] = new AssetFile(id, extension[1..], path, plugin.Manifest.Id);
            }
        }
        return found.Values;
    }

    /// <summary>The merged record as JSON. It's the set's own: read it, don't change it.</summary>
    public JsonObject? GetJson(RecordId id) => _records.GetValueOrDefault(id);

    /// <summary>The merged record, read as <typeparamref name="T"/>.</summary>
    public T Get<T>(RecordId id) where T : class =>
        TryGet<T>(id) ?? throw new KeyNotFoundException($"No record {id}.");

    /// <summary>
    /// The merged record, read as <typeparamref name="T"/>, or null if no plugin has it. It's read once: every call for
    /// the same record and type returns the same object, which <see cref="All{T}"/> holds too.
    /// </summary>
    public T? TryGet<T>(RecordId id) where T : class =>
        _records.TryGetValue(id, out var json)
            ? (T)_typed.GetOrAdd((id, typeof(T)), static (_, json) => json.Deserialize<T>(PluginJson.Options)!, json)
            : null;

    /// <summary>Every merged record of a type, in the order of their IDs' spelling, each the object <see cref="TryGet{T}"/> returns.</summary>
    public IReadOnlyList<T> All<T>(string type) where T : class =>
        (IReadOnlyList<T>)_typedAll.GetOrAdd((type, typeof(T)), static (key, set) => set._records.Keys
            .Where(id => id.Type == key.Type).OrderBy(id => id.ToString(), StringComparer.Ordinal)
            .Select(id => set.TryGet<T>(id)!).ToList().AsReadOnly(), this);

    /// <summary>
    /// The record of a type there's only one of, <c>aidyn:&lt;type&gt;/0</c>, such as <c>aidyn:camping/0</c>. Plugins
    /// patch it rather than add another.
    /// </summary>
    public T Singleton<T>(string type) where T : class => Get<T>(RecordId.Decimal(type, 0));

    /// <summary>As <see cref="Singleton{T}"/>, or null if no plugin has it.</summary>
    public T? TrySingleton<T>(string type) where T : class => TryGet<T>(RecordId.Decimal(type, 0));

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
            _definedBy[id] = pluginId;
            foreach (var (field, _) in o)
            {
                if (field != "id")
                    SetterList(id, field).Add(pluginId);
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
                var setters = SetterList(id, field);
                if (setters.Count == 0 || setters[^1] != pluginId)
                    setters.Add(pluginId);
            }
        }
        else
        {
            throw new InvalidDataException($"{file}: each object needs exactly one of \"id\" and \"patch\".");
        }
    }

    private List<string> SetterList(RecordId id, string field)
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
