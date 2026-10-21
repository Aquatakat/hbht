using System.Collections.Concurrent;
using System.Globalization;
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

/// <summary>
/// A field that more than one plugin patched, and which one won (ADR 0005's conflict report). The record's definer isn't
/// among them: the base plugin sets every field of its records, and one mod patching a field is a patch, not a conflict.
/// </summary>
/// <param name="Path">
/// A top-level field, such as <c>damage</c>, or through a keyed list (<see cref="KeyedListAttribute"/>) a keyed element or a
/// field in one: <c>objects[12]</c>, <c>objects[12].position</c>, <c>chunks[9,8].music</c>.
/// </param>
/// <param name="Plugins">The plugins that patched it, in load order; the last one's value holds.</param>
public sealed record FieldConflict(RecordId Record, string Path, IReadOnlyList<string> Plugins)
{
    public string Winner => Plugins[^1];
}

/// <summary>
/// A record <see cref="PluginSet.Validate"/> couldn't read: what's wrong, where (a setter path, <see cref="FieldConflict.Path"/>,
/// or the whole record), and the plugin that last set that, which is where to fix it.
/// </summary>
public sealed record RecordProblem(RecordId Record, string Path, string Plugin, string Message)
{
    public override string ToString() => $"{Record}{(Path.Length > 0 ? " " + Path : "")}: {Message}";
}

/// <summary>What <see cref="PluginSet.Validate"/> found: every record it couldn't read, by plugin, in record ID order.</summary>
public sealed class ValidationReport
{
    public ValidationReport(IReadOnlyList<RecordProblem> problems) => Problems = problems;

    public IReadOnlyList<RecordProblem> Problems { get; }

    public bool IsClean => Problems.Count == 0;

    /// <summary>
    /// The report worded for the player, as <see cref="BasePlugin.Diagnose"/> words an import's: a line per problem under its
    /// plugin, up to <paramref name="limit"/> of them, and how many more there are.
    /// </summary>
    public string Describe(int limit = int.MaxValue)
    {
        if (IsClean)
            return "Every record reads.";
        var lines = new List<string> { $"{Problems.Count} {(Problems.Count == 1 ? "record can't" : "records can't")} be read. Fix or remove the plugin:" };
        var shown = 0;
        foreach (var byPlugin in Problems.GroupBy(p => p.Plugin))
        {
            if (shown >= limit)
                break;
            lines.Add($"{byPlugin.Key}:");
            foreach (var problem in byPlugin)
            {
                if (shown++ >= limit)
                    break;
                lines.Add("  " + problem);
            }
        }
        if (Problems.Count > limit)
            lines.Add($"  … and {Problems.Count - limit} more.");
        return string.Join('\n', lines);
    }

    public override string ToString() => Describe();
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
/// earlier plugin's record as a JSON Merge Patch (RFC 7386), except that a keyed list merges element by element
/// (<see cref="MergePatch"/>). An object's <c>$schema</c>, for editors, is ignored.
/// </remarks>
public sealed class PluginSet
{
    private readonly Dictionary<RecordId, JsonObject> _records = [];
    // By record, then setter path (FieldConflict.Path), the plugins that set it, in load order.
    private readonly Dictionary<RecordId, Dictionary<string, List<string>>> _setters = [];
    private readonly Dictionary<RecordId, string> _definedBy = [];
    private readonly Dictionary<string, int> _loadIndex;

    /// <summary>An editor's pointer at the schema, allowed on any record object and ignored.</summary>
    public const string SchemaField = "$schema";

    // Records read as types, once each and after the merge: nothing changes the merged JSON once the set has loaded.
    // The runtime's audio thread and the editor's loading tasks read records as well as the main thread.
    private readonly ConcurrentDictionary<(RecordId, Type), object> _typed = new();
    private readonly ConcurrentDictionary<(string Type, Type), object> _typedAll = new();

    private PluginSet(IReadOnlyList<Plugin> plugins)
    {
        Plugins = plugins;
        _loadIndex = plugins.Select((p, i) => (p.Manifest.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
    }

    /// <summary>In load order.</summary>
    public IReadOnlyList<Plugin> Plugins { get; }

    public IEnumerable<RecordId> Ids => _records.Keys;

    /// <summary>
    /// Every field that more than one plugin patched, by record then path (ADR 0005's conflict report): a top-level field,
    /// or a keyed list's element or a field in one. The record's definer doesn't count.
    /// </summary>
    public IEnumerable<FieldConflict> Conflicts => _setters
        .SelectMany(r => r.Value.Select(s => new FieldConflict(r.Key, s.Key, [.. s.Value.Where(p => p != _definedBy.GetValueOrDefault(r.Key))])))
        .Where(c => c.Plugins.Count > 1)
        .OrderBy(c => c.Record.ToString(), StringComparer.Ordinal).ThenBy(c => c.Path, StringComparer.Ordinal);

    /// <summary>Loads plugin folders in the order given.</summary>
    public static PluginSet Load(IEnumerable<string> directories) => Load([.. directories.Select(Plugin.Open)]);

    private static PluginSet Load(IReadOnlyList<Plugin> plugins)
    {
        var set = new PluginSet(plugins);
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
    /// Loads the base plugin and every other plugin in <paramref name="pluginsDirectory"/>: the base first, then the rest in
    /// <see cref="LoadOrder"/>. A plugin's folder must be named for its ID, as the ID is also its namespace; only the base
    /// plugin's, <see cref="BasePlugin.Folder"/>, differs (it holds <c>aidyn</c>).
    /// </summary>
    public static PluginSet LoadInstalled(string pluginsDirectory)
    {
        var baseDir = Path.Combine(pluginsDirectory, BasePlugin.Folder);
        var others = Directory.EnumerateDirectories(pluginsDirectory)
            .Where(d => !string.Equals(Path.GetFileName(d), BasePlugin.Folder, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(d, PluginManifest.FileName)))
            .Select(Plugin.Open)
            .ToList();
        foreach (var plugin in others)
        {
            var folder = Path.GetFileName(plugin.Directory.TrimEnd('/', '\\'));
            if (!string.Equals(folder, plugin.Manifest.Id, StringComparison.Ordinal))
                throw new InvalidDataException($"The plugin in folder \"{folder}\" says its ID is \"{plugin.Manifest.Id}\"; a plugin's folder must be named for its ID.");
        }
        var basePlugin = Plugin.Open(baseDir);
        return Load([basePlugin, .. LoadOrder(others, basePlugin.Manifest.Id)]);
    }

    /// <summary>
    /// <see cref="LoadInstalled"/> after <see cref="BasePlugin.Require"/>, then <see cref="Validate"/>: it throws an
    /// <see cref="IOException"/> saying to import if the base plugin isn't there, or was written by an importer other than
    /// the current one, and an <see cref="InvalidDataException"/> with the <see cref="ValidationReport"/> if a record can't
    /// be read. The runtime and the CLI load by it, so a mod's mistake stops the game with its name rather than being
    /// played around.
    /// </summary>
    public static PluginSet LoadInstalledChecked(string pluginsDirectory)
    {
        BasePlugin.Require(pluginsDirectory);
        var set = LoadInstalled(pluginsDirectory);
        var report = set.Validate();
        return report.IsClean ? set : throw new InvalidDataException(report.Describe());
    }

    /// <summary>
    /// Reads every record of a type with a class (<see cref="RecordTypes.ClassOf"/>) strictly (<see cref="PluginJson.Strict"/>),
    /// once, and reports every one that can't be read, blaming the plugin that last set the field at fault: a field the type
    /// lacks, a kind no discriminator names, a null or a missing field the type doesn't allow, a value of the wrong shape, and
    /// a keyed list with two elements of one key. The records that read are kept, so later reads of them are what this read;
    /// the base plugin's 8,900 records take under a second. A problem is a problem whichever plugin has it: an unknown field or
    /// kind in a mod is never what its author meant, and is an error rather than a warning so that it's found and not played
    /// around.
    /// </summary>
    public ValidationReport Validate()
    {
        var problems = new List<RecordProblem>();
        foreach (var (id, json) in _records.OrderBy(r => r.Key.ToString(), StringComparer.Ordinal))
        {
            if (RecordTypes.ClassOf(id.Type) is not { } type)
                continue;
            try
            {
                _typed.GetOrAdd((id, type), static (_, arg) => arg.json.Deserialize(arg.type, PluginJson.Strict)!, (json, type));
            }
            catch (JsonException e)
            {
                var path = SetterPath(json, type, e.Path);
                problems.Add(new RecordProblem(id, path, Blame(id, path), TrimMessage(e.Message)));
            }
            CheckShape(id, json, type, "", problems);
        }
        return new ValidationReport(problems);
    }

    /// <summary>The plugin that last set <paramref name="path"/> or what encloses it, else the record's definer.</summary>
    private string Blame(RecordId id, string path) =>
        Setters(id, path) is { Count: > 0 } setters ? setters[^1] : _definedBy.GetValueOrDefault(id) ?? "?";

    /// <summary>
    /// The setter path (<see cref="FieldConflict.Path"/>) that a serializer's path, such as <c>$.objects[3].reach</c>, falls
    /// in: a keyed list's position becomes its element's key, so that the setters of that element are found; an unkeyed
    /// list's position stays, and the setters of the list are.
    /// </summary>
    private static string SetterPath(JsonObject record, Type type, string? jsonPath)
    {
        var path = "";
        JsonNode? node = record;
        Type? current = type;
        RecordSchema.Property? property = null;
        var rest = jsonPath is { Length: > 1 } && jsonPath.StartsWith('$') ? jsonPath.AsSpan(1) : [];
        while (rest.Length > 0)
        {
            if (rest[0] == '.')
            {
                var end = rest[1..].IndexOfAny(".[");
                var field = end < 0 ? rest[1..].ToString() : rest[1..(end + 1)].ToString();
                rest = end < 0 ? [] : rest[(end + 1)..];
                path = path.Length == 0 ? field : path + "." + field;
                property = RecordSchema.Find(current, field);
                node = (node as JsonObject)?[field];
                current = property is null ? null : node is JsonObject o ? RecordSchema.Concrete(property.Type, o) : property.Type;
            }
            else if (rest[0] == '[')
            {
                var close = rest.IndexOf(']');
                if (close < 0 || !int.TryParse(rest[1..close], CultureInfo.InvariantCulture, out var position))
                    break;
                rest = rest[(close + 1)..];
                var list = node as JsonArray;
                node = list is not null && position < list.Count ? list[position] : null;
                var elementType = current is null ? null : RecordSchema.ElementType(current);
                path += $"[{(property?.Keyed?.KeyOf(node) ?? position.ToString(CultureInfo.InvariantCulture))}]";
                current = elementType is null ? null : node is JsonObject element ? RecordSchema.Concrete(elementType, element) : elementType;
                property = null;
            }
            else
            {
                break;
            }
        }
        return path;
    }

    private static string TrimMessage(string message)
    {
        var cut = message.IndexOf(" Path: $", StringComparison.Ordinal);
        if (cut >= 0)
            message = message[..cut];
        return message.Replace("Aidyn.Plugins.Records.", "", StringComparison.Ordinal)
            .Replace(" Consider updating its nullability annotation.", "", StringComparison.Ordinal);
    }

    /// <summary>
    /// What strict reading lets through: an object of a polymorphic type without its discriminator, which reads as the base
    /// kind, and a keyed list with two elements of one key. <paramref name="type"/> is the declared type, before the discriminator.
    /// </summary>
    private void CheckShape(RecordId id, JsonObject json, Type type, string prefix, List<RecordProblem> problems)
    {
        var kinds = RecordSchema.DerivedTypes(type);
        if (kinds.Count > 0 && json["type"] is null)
        {
            var path = prefix.TrimEnd('.');
            problems.Add(new RecordProblem(id, path, Blame(id, path), $"{(path.Length == 0 ? "The record" : path)} lacks its type: one of {string.Join(", ", kinds.Select(k => k.Discriminator))}."));
        }
        type = RecordSchema.Concrete(type, json);
        foreach (var (field, value) in json)
        {
            if (RecordSchema.Find(type, field) is not { } property)
                continue;
            if (value is JsonArray elements && RecordSchema.ElementType(property.Type) is { } elementType)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < elements.Count; i++)
                {
                    var key = property.Keyed?.KeyOf(elements[i]);
                    var path = $"{prefix}{field}[{key ?? i.ToString(CultureInfo.InvariantCulture)}]";
                    if (key is not null && !seen.Add(key))
                        problems.Add(new RecordProblem(id, path, Blame(id, path), $"Two elements of {prefix}{field} are keyed {key}."));
                    else if (elements[i] is JsonObject o)
                        CheckShape(id, o, elementType, path + ".", problems);
                }
            }
            else if (value is JsonObject nested)
            {
                CheckShape(id, nested, property.Type, $"{prefix}{field}.", problems);
            }
        }
    }

    /// <summary>
    /// Puts plugins in the order they load: each after its <see cref="PluginManifest.Dependencies"/> and, when the plugin is
    /// present, its <see cref="PluginManifest.LoadAfter"/>, and otherwise by ID, so the order doesn't depend on the folders'
    /// names. A dependency that isn't among the plugins is left for <see cref="Load(IEnumerable{string})"/> to refuse, and a
    /// <c>LoadAfter</c> that isn't is ignored. The base plugin loads before all of them, so it's named by
    /// <paramref name="loadedFirst"/> rather than ordered. A cycle is an <see cref="InvalidDataException"/> naming the
    /// plugins in it.
    /// </summary>
    public static IReadOnlyList<Plugin> LoadOrder(IEnumerable<Plugin> plugins, string? loadedFirst = null)
    {
        var byId = new SortedDictionary<string, Plugin>(StringComparer.Ordinal);
        foreach (var plugin in plugins)
        {
            if (plugin.Manifest.Id == loadedFirst || !byId.TryAdd(plugin.Manifest.Id, plugin))
                throw new InvalidDataException($"Plugin {plugin.Manifest.Id} is loaded twice.");
        }

        // after[x] holds the plugins x loads after.
        var after = byId.ToDictionary(p => p.Key, p => new SortedSet<string>(
            (p.Value.Manifest.Dependencies ?? []).Concat(p.Value.Manifest.LoadAfter ?? []).Where(byId.ContainsKey),
            StringComparer.Ordinal), StringComparer.Ordinal);

        var ordered = new List<Plugin>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        while (ordered.Count < byId.Count)
        {
            // byId is sorted by ID, so the first plugin that's ready is the least ID of those that are.
            var next = byId.Keys.FirstOrDefault(id => !placed.Contains(id) && after[id].All(placed.Contains));
            if (next is null)
            {
                var stuck = new SortedSet<string>(byId.Keys.Where(id => !placed.Contains(id)), StringComparer.Ordinal);
                // What nothing stuck comes after is only after a cycle, not in one.
                for (var changed = true; changed;)
                {
                    var wanted = stuck.SelectMany(o => after[o]).ToHashSet(StringComparer.Ordinal);
                    changed = stuck.RemoveWhere(id => !wanted.Contains(id)) > 0;
                }
                throw new InvalidDataException($"These plugins depend on or load after each other in a cycle: {string.Join(", ", stuck)}.");
            }
            placed.Add(next);
            ordered.Add(byId[next]);
        }
        return ordered;
    }

    public bool Contains(RecordId id) => _records.ContainsKey(id);

    /// <summary>The ID of the plugin that defines the record, or null if none does.</summary>
    public string? DefinedBy(RecordId id) => _definedBy.GetValueOrDefault(id);

    /// <summary>
    /// The plugins that set a field of the record, in load order, the definer among them. The last one's value holds.
    /// <paramref name="path"/> is a top-level field, or through a keyed list an element or a field in one, as
    /// <see cref="FieldConflict.Path"/> spells them; what set an enclosing path counts too, so <c>objects[12].position</c>
    /// lists whoever set the position, added or removed object 12, or replaced the list.
    /// </summary>
    public IReadOnlyList<string> Setters(RecordId id, string path)
    {
        if (!_setters.TryGetValue(id, out var paths))
            return [];
        var plugins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ancestor in Ancestors(path))
        {
            foreach (var plugin in paths.GetValueOrDefault(ancestor) ?? [])
                plugins.Add(plugin);
        }
        return InLoadOrder(plugins);
    }

    /// <summary>The plugins other than the record's definer that patched any of its fields, in load order.</summary>
    public IReadOnlyList<string> Patchers(RecordId id)
    {
        if (!_setters.TryGetValue(id, out var paths))
            return [];
        var definer = _definedBy.GetValueOrDefault(id);
        return InLoadOrder(paths.Values.SelectMany(p => p).Where(p => p != definer).ToHashSet(StringComparer.Ordinal));
    }

    private List<string> InLoadOrder(IEnumerable<string> plugins) => [.. plugins.OrderBy(p => _loadIndex.GetValueOrDefault(p, int.MaxValue))];

    /// <summary>
    /// A path and the paths that enclose it: <c>objects[12].position</c> is in <c>objects[12]</c>, which is in <c>objects</c>;
    /// <c>grid.origin</c> is in <c>grid</c>, where only <c>grid</c> is ever set.
    /// </summary>
    private static IEnumerable<string> Ancestors(string path)
    {
        var depth = 0;
        for (var i = 0; i < path.Length; i++)
        {
            switch (path[i])
            {
                case '[':
                    if (depth++ == 0)
                        yield return path[..i];
                    break;
                case ']':
                    if (--depth == 0 && i + 1 < path.Length)
                        yield return path[..(i + 1)];
                    break;
                case '.' when depth == 0 && i > 0 && path[i - 1] != ']':
                    yield return path[..i];
                    break;
            }
        }
        yield return path;
    }

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

    /// <summary>The merged record, read as <typeparamref name="T"/>, which must be its type's class or a view of it (<see cref="RecordTypeInfo.Reads"/>).</summary>
    public T Get<T>(RecordId id) where T : class =>
        TryGet<T>(id) ?? throw new KeyNotFoundException($"No record {id}.");

    /// <summary>
    /// The merged record, read as <typeparamref name="T"/>, or null if no plugin has it. It's read once: every call for
    /// the same record and type returns the same object, which <see cref="All{T}(string)"/> holds too. A
    /// <typeparamref name="T"/> that the record's type doesn't read as, such as a weather record asked of the combat ID, is
    /// an <see cref="ArgumentException"/>, whether or not the record exists.
    /// </summary>
    public T? TryGet<T>(RecordId id) where T : class
    {
        CheckReads<T>(id.Type);
        return _records.TryGetValue(id, out var json)
            ? (T)_typed.GetOrAdd((id, typeof(T)), static (_, json) => json.Deserialize<T>(PluginJson.Options)!, json)
            : null;
    }

    /// <summary>Every merged record of a type, in the order of their IDs' spelling, each the object <see cref="TryGet{T}"/> returns.</summary>
    public IReadOnlyList<T> All<T>(string type) where T : class
    {
        CheckReads<T>(type);
        return (IReadOnlyList<T>)_typedAll.GetOrAdd((type, typeof(T)), static (key, set) => set._records.Keys
            .Where(id => id.Type == key.Type).OrderBy(id => id.ToString(), StringComparer.Ordinal)
            .Select(id => set.TryGet<T>(id)!).ToList().AsReadOnly(), this);
    }

    /// <summary>Every merged record of the one type whose class is <typeparamref name="T"/> (<see cref="RecordTypes.Of{T}"/>).</summary>
    public IReadOnlyList<T> All<T>() where T : class => All<T>(RecordTypes.Of<T>().Name);

    /// <summary>
    /// The record of a type there's only one of, <c>aidyn:&lt;type&gt;/0</c>, such as <c>aidyn:camping/0</c>, the type being
    /// the one whose class is <typeparamref name="T"/> (<see cref="RecordTypes.SingletonOf{T}"/>). Plugins patch it rather
    /// than add another.
    /// </summary>
    public T Singleton<T>() where T : class => Get<T>(RecordTypes.SingletonOf<T>().SingletonId);

    /// <summary>As <see cref="Singleton{T}"/>, or null if no plugin has it.</summary>
    public T? TrySingleton<T>() where T : class => TryGet<T>(RecordTypes.SingletonOf<T>().SingletonId);

    private static void CheckReads<T>(string type)
    {
        if (!RecordTypes.Info(type).Reads(typeof(T)))
            throw new ArgumentException($"A {type} record doesn't read as {typeof(T).Name}.", nameof(T));
    }

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

    private readonly ConcurrentDictionary<RecordId, (int Width, int Height)> _imageSizes = new();

    /// <summary>
    /// An image asset's width and height in pixels, from its PNG's header, read once; 0 × 0 if no plugin has it. The
    /// core places sprites and centres the minimap's pieces by it.
    /// </summary>
    public (int Width, int Height) ImageSize(RecordId image) =>
        _imageSizes.GetOrAdd(image, id => FindAsset(id, "png") is { } png && Aidyn.Plugins.Assets.PngHeader.Size(png) is { } size ? size : (0, 0));

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
        o.Remove(SchemaField);
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
                    AddSetter(id, field, pluginId);
            }
        }
        else if (o["patch"] is JsonValue patchValue && o["id"] is null)
        {
            var id = ParseId(file, patchValue);
            if (!_records.TryGetValue(id, out var target))
                throw new InvalidDataException($"{file}: patches {id}, which no earlier plugin defines.");
            o.Remove("patch");
            try
            {
                MergePatch.Apply(target, o, RecordTypes.ClassOf(id.Type), path => AddSetter(id, path, pluginId));
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"{file}: patching {id}: {e.Message}", e);
            }
        }
        else
        {
            throw new InvalidDataException($"{file}: each object needs exactly one of \"id\" and \"patch\".");
        }
    }

    private void AddSetter(RecordId id, string path, string pluginId)
    {
        if (!_setters.TryGetValue(id, out var paths))
            _setters[id] = paths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!paths.TryGetValue(path, out var list))
            paths[path] = list = [];
        if (list.Count == 0 || list[^1] != pluginId)
            list.Add(pluginId);
    }

    private static RecordId ParseId(string file, JsonValue value) =>
        value.TryGetValue(out string? text) && RecordId.TryParse(text, out var id, out var error)
            ? id
            : throw new InvalidDataException($"{file}: \"{value}\" isn't a record ID.");
}

/// <summary>
/// How a patch merges into a record (ADR 0005): JSON Merge Patch (RFC 7386), where a scalar or a list replaces the old
/// value, an object merges field by field and <c>null</c> removes a field, except that a keyed list
/// (<see cref="KeyedListAttribute"/>) merges element by element. Each element of the patch's list names its element by
/// the key fields: one the record has merges into it as an object does, one it lacks is added at the end, and one with
/// <c>"$remove": true</c> is removed. A keyed list patched with something other than a list, or whose record lacks the
/// list, is replaced like any other value.
/// </summary>
public static class MergePatch
{
    /// <summary>The field that removes a keyed list's element: <c>{ "index": 12, "$remove": true }</c>.</summary>
    public const string RemoveField = "$remove";

    /// <summary>
    /// Merges every field of <paramref name="patch"/> into <paramref name="target"/>, a record of class <paramref name="type"/>
    /// (null for a type without one, which merges as plain JSON), and tells <paramref name="touched"/> each path set: a
    /// top-level field, a keyed element added or removed, or a field in one (<see cref="FieldConflict.Path"/>).
    /// </summary>
    public static void Apply(JsonObject target, JsonObject patch, Type? type, Action<string> touched) =>
        Apply(target, patch, type, "", touched, null);

    private static void Apply(JsonObject target, JsonObject patch, Type? type, string prefix, Action<string> touched, IReadOnlyList<string>? skip)
    {
        foreach (var (field, value) in patch)
        {
            if (skip?.Contains(field) == true)
                continue;
            var path = prefix + field;
            var property = RecordSchema.Find(type, field);
            if (property?.Keyed is { } keyed && value is JsonArray elements && target[field] is JsonArray existing)
            {
                foreach (var element in elements)
                    ApplyElement(existing, element, keyed, path, touched);
            }
            else
            {
                if (value is JsonObject nested && target[field] is JsonObject into && property is not null)
                    Apply(into, nested, RecordSchema.Concrete(property.Type, into), path + ".", static _ => { }, null);
                else
                    Merge(target, field, value);
                touched(path);
            }
        }
    }

    private static void ApplyElement(JsonArray existing, JsonNode? element, RecordSchema.KeyedList keyed, string path, Action<string> touched)
    {
        if (element is not JsonObject o || keyed.KeyOf(o) is not { } key)
            throw new InvalidDataException($"{path} is a list keyed by {keyed.Describe}: each element of a patch is an object with {(keyed.Keys.Count == 1 ? "it" : "them")}.");
        var elementPath = $"{path}[{key}]";
        var at = -1;
        for (var i = 0; i < existing.Count && at < 0; i++)
        {
            if (keyed.KeyOf(existing[i]) == key)
                at = i;
        }
        var remove = o[RemoveField] is JsonValue flag && flag.TryGetValue(out bool wanted) && wanted;
        if (remove)
        {
            if (at >= 0)
                existing.RemoveAt(at);
        }
        else if (at < 0)
        {
            var added = (JsonObject)o.DeepClone();
            added.Remove(RemoveField);
            existing.Add(added);
        }
        else
        {
            var current = (JsonObject)existing[at]!;
            Apply(current, o, RecordSchema.Concrete(keyed.ElementType, current), elementPath + ".", touched, [.. keyed.Keys, RemoveField]);
            return;
        }
        touched(elementPath);
    }

    /// <summary>JSON Merge Patch of one field: <c>null</c> removes it, an object merges into an object, anything else replaces.</summary>
    private static void Merge(JsonObject target, string field, JsonNode? patch)
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
                Merge(existing, key, value);
        }
        else
        {
            target[field] = patch.DeepClone();
        }
    }
}
