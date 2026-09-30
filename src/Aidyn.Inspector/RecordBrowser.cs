using System.Globalization;
using System.Text.Json.Nodes;
using Aidyn.Core.Flags;
using Aidyn.Game;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom;
using Aidyn.Rom.Import;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// The editor's view of the installed plugins (ADR 0010): every record and asset by type, with a
/// preview, the merged fields and what names it. Record IDs are links, with back and forward
/// (the buttons, Alt+Left and Alt+Right, or the mouse's side buttons).
/// </summary>
/// <remarks>
/// It reads plugins only. Its one use of the ROM is running the importer, as the game's first-run
/// screen will (ADR 0001).
/// </remarks>
public partial class RecordBrowser : Control
{
    private const string Preview = "Preview", Fields = "Fields", UsedBy = "Used by", Atlas = "Atlas";

    private readonly Button _back = new() { Text = "◀", TooltipText = "Back (Alt+Left)", Disabled = true };
    private readonly Button _forward = new() { Text = "▶", TooltipText = "Forward (Alt+Right)", Disabled = true };
    private readonly Button _folder = new() { Text = "Plugins folder…" };
    private readonly Button _import = new() { Text = "Import ROM…" };
    private readonly OptionButton _types = new() { TooltipText = "Type" };
    private readonly LineEdit _search = new()
    {
        PlaceholderText = "Record ID, Borg index, summary or script text",
        CustomMinimumSize = new Vector2(280, 0),
        TooltipText = "Filters the list. Enter opens a record ID, or the only match.",
    };
    private readonly Label _status = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Tree _list = new();
    private readonly Label _header = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly TabContainer _tabs = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Control _preview = new MarginContainer { Name = Preview };
    private readonly RecordFieldsView _fields = new() { Name = Fields };
    private readonly Tree _usedBy = new() { Name = UsedBy };
    private readonly AtlasView _atlas = new() { Name = Atlas };
    private readonly ModelView _model = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly MapSetView _map = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Dictionary<RecordId, TreeItem> _rows = [];
    private readonly List<RecordId> _history = [];
    private readonly List<string> _typeNames = [];

    private PluginCatalog? _catalog;
    private Models? _models;
    private QuestIndex? _quests;
    private FlagState? _flags;
    private string _pluginsDirectory = DataPaths.Plugins;
    private int _position = -1;
    private RecordId? _current;
    private bool _selecting;
    private int _generation;
    private Task _showing = Task.CompletedTask;

    public override void _Ready() => BuildUi();

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete)
            return;
        // The preview not showing is out of the tree, and the models' templates never were in
        // it, so nothing else frees them. Godot reports them leaked at exit, one by one.
        foreach (var view in new Control[] { _model, _map })
        {
            if (view.GetParent() is null)
                view.Free();
        }
        _models?.Dispose();
    }

    /// <summary>The 3D preview, for scripted screenshots.</summary>
    public ModelView Model => _model;

    /// <summary>The map set preview, for scripted screenshots.</summary>
    public MapSetView Map => _map;

    /// <summary>Shows the Preview, Fields or Used by tab.</summary>
    public void SelectTab(string tab) => _tabs.CurrentTab = tab.ToLowerInvariant() switch { "fields" => 1, "used-by" => 2, "atlas" => 3, _ => 0 };

    /// <summary>Loads the plugins in a folder, as the runtime does: <c>aidyn.base</c> first, then the rest by ID.</summary>
    public async Task LoadAsync(string pluginsDirectory)
    {
        _pluginsDirectory = pluginsDirectory;
        _catalog = null;
        _map.Clear();
        _models?.Dispose();
        _models = null;
        _quests = null;
        _current = null;
        _history.Clear();
        _position = -1;
        UpdateHistoryButtons();
        Populate();
        ShowNothing("");
        if (!File.Exists(Path.Combine(pluginsDirectory, BasePlugin.Folder, PluginManifest.FileName)))
        {
            _status.Text = $"No {BasePlugin.Folder} in {pluginsDirectory}. Import a ROM to make one.";
            return;
        }

        _status.Text = $"Loading {pluginsDirectory}…";
        var generation = ++_generation;
        var (catalog, quests, flags) = await Task.Run(() =>
        {
            var plugins = PluginSet.LoadInstalled(pluginsDirectory);
            FlagState? flags = null;
            try
            {
                flags = new FlagState(FlagNetwork.Load(plugins));
            }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException or KeyNotFoundException)
            {
                GD.PrintErr($"No flag network: {e.Message}");
            }
            return (PluginCatalog.Build(plugins), QuestIndex.Build(plugins), flags);
        });
        if (generation != _generation)
            return;
        _catalog = catalog;
        _models = new Models(catalog.Plugins);
        _quests = quests;
        _flags = flags;

        _typeNames.Clear();
        _typeNames.AddRange(catalog.Types);
        _types.Clear();
        _types.AddItem("All types", 0);
        for (var i = 0; i < _typeNames.Count; i++)
            _types.AddItem($"{_typeNames[i]} ({catalog.Entries.Count(e => e.Id.Type == _typeNames[i]):N0})", i + 1);
        Populate();
        ShowNothing(Welcome(catalog));
    }

    /// <summary>Shows a checkpoint's flags on flag pages, for scripts (<c>--checkpoint</c>).</summary>
    public void LoadCheckpoint(string path) => _flags?.LoadCheckpoint(path);

    /// <summary>Plays the script on the preview, if it's showing one, for a number of moves (<c>--play</c>).</summary>
    public void PlayScript(int moves)
    {
        if (_preview.GetChildren().OfType<ScriptPanel>().FirstOrDefault() is { } panel)
            panel.Play(moves);
    }

    /// <summary>Opens a record, adding it to the history. The task completes when its preview is shown.</summary>
    public async Task OpenAsync(RecordId id)
    {
        Navigate(id);
        await _showing;
    }

    private void BuildUi()
    {
        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        var bar = new HBoxContainer();
        bar.AddChild(_back);
        bar.AddChild(_forward);
        bar.AddChild(new VSeparator());
        bar.AddChild(_folder);
        bar.AddChild(_import);
        bar.AddChild(new VSeparator());
        bar.AddChild(_types);
        bar.AddChild(_search);
        bar.AddChild(_status);
        root.AddChild(bar);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [520] };
        root.AddChild(split);

        _list.Columns = 3;
        _list.HideRoot = true;
        _list.ColumnTitlesVisible = true;
        _list.SelectMode = Tree.SelectModeEnum.Row;
        string[] titles = ["Record", "Borg", "Summary"];
        for (var c = 0; c < titles.Length; c++)
        {
            _list.SetColumnTitle(c, titles[c]);
            _list.SetColumnExpand(c, c == 2);
            _list.SetColumnClipContent(c, true);
        }
        _list.SetColumnCustomMinimumWidth(0, 190);
        _list.SetColumnCustomMinimumWidth(1, 60);
        _list.ItemSelected += () =>
        {
            if (!_selecting && _list.GetSelected()?.GetMetadata(0).AsString() is { } text && RecordId.TryParse(text, out var id))
                Navigate(id);
        };
        split.AddChild(_list);

        var right = new VBoxContainer();
        right.AddChild(_header);
        right.AddChild(_tabs);
        split.AddChild(right);
        _tabs.AddChild(_preview);
        _tabs.AddChild(_fields);
        _tabs.AddChild(_usedBy);
        _tabs.AddChild(_atlas);

        _usedBy.Columns = 3;
        _usedBy.HideRoot = true;
        _usedBy.ColumnTitlesVisible = true;
        _usedBy.SelectMode = Tree.SelectModeEnum.Row;
        string[] usedByTitles = ["Record", "Fields", "Summary"];
        for (var c = 0; c < usedByTitles.Length; c++)
        {
            _usedBy.SetColumnTitle(c, usedByTitles[c]);
            _usedBy.SetColumnClipContent(c, true);
        }
        _usedBy.SetColumnExpand(0, false);
        _usedBy.SetColumnCustomMinimumWidth(0, 190);
        _usedBy.ItemActivated += () =>
        {
            if (_usedBy.GetSelected()?.GetMetadata(0).AsString() is { } text && RecordId.TryParse(text, out var id))
                FollowLink(id);
        };
        _fields.Opened += FollowLink;
        _map.Opened += FollowLink;

        _back.Pressed += () => Step(-1);
        _forward.Pressed += () => Step(1);
        _types.ItemSelected += _ => Populate();
        _search.TextChanged += _ => Populate();
        _search.TextSubmitted += Submit;

        var folderDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = "The plugins folder, which holds aidyn.base",
        };
        folderDialog.DirSelected += dir =>
        {
            // Picking aidyn.base itself means the folder it's in.
            if (string.Equals(Path.GetFileName(dir.TrimEnd('/', '\\')), BasePlugin.Folder, StringComparison.OrdinalIgnoreCase))
                dir = Path.GetDirectoryName(dir.TrimEnd('/', '\\'))!;
            _ = Report(LoadAsync(dir));
        };
        AddChild(folderDialog);
        _folder.Pressed += () =>
        {
            folderDialog.CurrentDir = _pluginsDirectory;
            folderDialog.PopupCentered(new Vector2I(800, 500));
        };

        var romDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.z64, *.v64, *.n64 ; N64 ROMs"],
            UseNativeDialog = true,
            Title = "The ROM to import as aidyn.base",
        };
        romDialog.FileSelected += path => _ = Report(ImportAsync(path));
        AddChild(romDialog);
        _import.Pressed += () => romDialog.PopupCentered(new Vector2I(800, 500));
    }

    /// <summary>Runs the importer into the plugins folder, then loads it.</summary>
    private async Task ImportAsync(string rom)
    {
        _import.Disabled = _folder.Disabled = true;
        try
        {
            var progress = new Progress<string>(step => _status.Text = $"Importing: {step}…");
            var directory = Path.Combine(_pluginsDirectory, BasePlugin.Folder);
            await Task.Run(() => BaseImporter.Import(RomImage.Load(rom), directory, progress));
            await LoadAsync(_pluginsDirectory);
        }
        finally
        {
            _import.Disabled = _folder.Disabled = false;
        }
    }

    private async Task Report(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            GD.PrintErr(e.ToString());
            _status.Text = e.Message;
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!IsVisibleInTree())
            return;
        var step = e switch
        {
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Xbutton1 } => -1,
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Xbutton2 } => 1,
            InputEventKey { Pressed: true, AltPressed: true, Keycode: Key.Left } => -1,
            InputEventKey { Pressed: true, AltPressed: true, Keycode: Key.Right } => 1,
            _ => 0,
        };
        if (step == 0)
            return;
        Step(step);
        GetViewport().SetInputAsHandled();
    }

    // ---- The list -------------------------------------------------------------------------------

    /// <summary>Fills the list with the entries that match the type and search filters.</summary>
    private void Populate()
    {
        _list.Clear();
        _rows.Clear();
        if (_catalog is null)
            return;
        var type = _types.GetSelectedId() is > 0 and var t ? _typeNames[t - 1] : null;
        var search = _search.Text.Trim();
        // Scripts whose text holds the search, with the first line that does.
        var said = new Dictionary<RecordId, string>();
        if (search.Length >= 3 && _quests is { } quests)
        {
            foreach (var (script, node) in quests.FindText(search))
            {
                if (!said.ContainsKey(script) && _catalog.Plugins.TryGet<DialogueRecord>(script) is { } record)
                    said[script] = $"node {node}: {record.Nodes[node].Text.Replace('\n', ' ')}";
            }
        }
        var root = _list.CreateItem();
        foreach (var entry in _catalog.Entries)
        {
            var line = said.GetValueOrDefault(entry.Id);
            if ((type is not null && entry.Id.Type != type) || (line is null && !Matches(entry, search)))
                continue;
            var row = _list.CreateItem(root);
            var id = entry.Id.ToString();
            row.SetText(0, id);
            row.SetTooltipText(0, id);
            row.SetText(1, entry.Borg?.ToString(CultureInfo.InvariantCulture) ?? "");
            row.SetTextAlignment(1, HorizontalAlignment.Right);
            row.SetText(2, line ?? entry.Summary);
            row.SetTooltipText(2, line ?? entry.Summary);
            row.SetMetadata(0, id);
            _rows[entry.Id] = row;
        }
        if (_current is { } current && _rows.TryGetValue(current, out var selected))
            SelectRow(selected);
        _status.Text = $"{_rows.Count:N0} of {_catalog.Entries.Count:N0} records and assets";
    }

    private static bool Matches(CatalogEntry entry, string search) =>
        search.Length == 0
        || entry.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
        || entry.Summary.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (entry.Borg is { } borg && borg.ToString(CultureInfo.InvariantCulture).StartsWith(search, StringComparison.Ordinal));

    /// <summary>Enter in the search box: open the record ID typed, or the only match.</summary>
    private void Submit(string text)
    {
        if (RecordId.TryParse(text.Trim(), out var id))
            Navigate(id);
        else if (_rows.Count == 1)
            Navigate(_rows.Keys.First());
    }

    private void SelectRow(TreeItem row)
    {
        _selecting = true;
        row.Select(0);
        _list.ScrollToItem(row, true);
        _selecting = false;
    }

    // ---- Navigation -----------------------------------------------------------------------------

    /// <summary>Opens a record from a link, on its preview.</summary>
    private void FollowLink(RecordId id)
    {
        _tabs.CurrentTab = 0;
        Navigate(id);
    }

    /// <summary>Opens a record from the atlas, staying on its tab.</summary>
    private void Explore(RecordId id) => Navigate(id);

    private void Navigate(RecordId id)
    {
        if (_current == id)
            return;
        _history.RemoveRange(_position + 1, _history.Count - _position - 1);
        _history.Add(id);
        _position = _history.Count - 1;
        Go(id);
    }

    private void Step(int by)
    {
        var to = _position + by;
        if (to < 0 || to >= _history.Count)
            return;
        _position = to;
        Go(_history[to]);
    }

    private void Go(RecordId id)
    {
        UpdateHistoryButtons();
        if (_rows.TryGetValue(id, out var row))
            SelectRow(row);
        else
            _list.DeselectAll();
        _showing = Report(ShowAsync(id));
    }

    private void UpdateHistoryButtons()
    {
        _back.Disabled = _position <= 0;
        _forward.Disabled = _position >= _history.Count - 1;
    }

    // ---- Showing a record -----------------------------------------------------------------------

    private void ShowNothing(string text)
    {
        _header.Text = text;
        _fields.Show(null, _ => [], _ => false);
        _usedBy.Clear();
        _tabs.SetTabTitle(2, UsedBy);
        SetPreview(new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private static string Welcome(PluginCatalog catalog)
    {
        var plugins = string.Join(", ", catalog.Plugins.Plugins.Select(p =>
            p.Manifest.Import is { } import ? $"{p.Manifest.Id} ({p.Manifest.Name}, imported from {import.RomVersion} by importer {import.Importer})" : p.Manifest.Id));
        return $"Loaded {plugins}. Pick a record, or type an ID such as aidyn:mapset/0 and press Enter.";
    }

    private async Task ShowAsync(RecordId id)
    {
        if (_catalog is not { } catalog)
            return;
        _current = id;
        var plugins = catalog.Plugins;
        var entry = catalog.Find(id);
        var voxel = id.Type == RecordTypes.Voxel;
        // A map object's fields are its map record's.
        var json = voxel ? MapObjects.Json(plugins, id) : plugins.GetJson(id);
        var references = catalog.ReferencesTo(id);

        _header.Text = voxel ? ObjectHeader(catalog, id, json) : Header(catalog, id, entry, json, references.Count);
        _fields.Show(json, field => voxel ? plugins.Setters(MapRecord.MapOf(id), "objects") : plugins.Setters(id, field),
            other => catalog.Find(other) is not null || other.Type == RecordTypes.Voxel);
        ShowUsedBy(catalog, references);
        if (_quests is { } quests)
        {
            _atlas.Show(catalog, quests, id, Explore);
            _tabs.SetTabTitle(3, _atlas.Count == 0 ? Atlas : $"{Atlas} ({_atlas.Count:N0})");
        }
        SetPreview(new Label { Text = "Loading…" });

        var preview = await BuildPreviewAsync(catalog, id, entry, json);
        if (_current != id || _catalog != catalog)
            return;
        switch (preview)
        {
            case Control control:
                SetPreview(control);
                break;
            case MapSetView.Focus focus when _models is { } models:
                SetPreview(_map);
                await _map.ShowAsync(catalog, models, _flags, focus);
                break;
            case (byte[] glb, string animation, string note):
                SetPreview(_model);
                _model.Show(glb, animation.Length > 0 ? animation : null, note.Length > 0 ? note : null,
                    id.Type == RecordTypes.Actor ? plugins.TryGet<ActorRecord>(id) : null);
                break;
        }
    }

    private static string Header(PluginCatalog catalog, RecordId id, CatalogEntry? entry, JsonObject? json, int usedBy)
    {
        var lines = new List<string> { id + (entry?.Borg is { } borg ? $" · Borg {borg}" : "") };
        if (json is not null)
        {
            var definer = catalog.Plugins.DefinedBy(id);
            var patchers = json.Select(f => catalog.Plugins.Setters(id, f.Key)).SelectMany(s => s).Distinct().Where(p => p != definer).ToList();
            lines.Add($"Defined by {definer}" + (patchers.Count > 0 ? $", patched by {string.Join(", ", patchers)}" : ""));
        }
        foreach (var asset in entry?.Assets ?? [])
            lines.Add($"{asset.Extension} asset from {asset.Plugin}, {new FileInfo(asset.Path).Length:N0} bytes");
        if (entry is null)
            lines.Add(usedBy > 0 ? "No plugin has this record yet, but records name it." : "No plugin has this record.");
        else if (entry.Summary.Length > 0)
            lines.Add(entry.Summary);
        return string.Join("\n", lines);
    }

    private static string ObjectHeader(PluginCatalog catalog, RecordId id, JsonObject? json)
    {
        var map = MapRecord.MapOf(id);
        if (json is null)
            return $"{id}\nNo plugin has this map object.";
        return $"{id}\n{MapObjects.Describe(catalog, id)}\nObject {MapObjects.Index(id)} of {map}, defined by {catalog.Plugins.DefinedBy(map)}";
    }

    private void ShowUsedBy(PluginCatalog catalog, IReadOnlyList<RecordReference> references)
    {
        _usedBy.Clear();
        var root = _usedBy.CreateItem();
        var byRecord = references.GroupBy(r => r.From).ToList();
        foreach (var group in byRecord)
        {
            var paths = group.Select(r => r.Path).ToList();
            var row = _usedBy.CreateItem(root);
            row.SetText(0, group.Key.ToString());
            row.SetMetadata(0, group.Key.ToString());
            row.SetText(1, paths.Count <= 3 ? string.Join(", ", paths) : $"{string.Join(", ", paths.Take(3))} and {paths.Count - 3:N0} more");
            row.SetTooltipText(1, string.Join("\n", paths.Take(40)) + (paths.Count > 40 ? "\n…" : ""));
            row.SetText(2, catalog.Find(group.Key)?.Summary ?? "");
        }
        _tabs.SetTabTitle(2, byRecord.Count == 0 ? UsedBy : $"{UsedBy} ({byRecord.Count:N0})");
    }

    /// <summary>
    /// A Control, or a glTF to show in the model view with an animation and a note ("" for none).
    /// Files are read off the main thread.
    /// </summary>
    private async Task<object> BuildPreviewAsync(PluginCatalog catalog, RecordId id, CatalogEntry? entry, JsonObject? json)
    {
        var plugins = catalog.Plugins;
        if (id.Type is RecordTypes.Flag or RecordTypes.Journal)
            return _flags is null ? Message("These plugins have no flag network.") : new FlagPanel(catalog, _flags, id, FollowLink);
        if (id.Type == RecordTypes.Dialogue && plugins.TryGet<DialogueRecord>(id) is { } script)
            return new ScriptPanel(catalog, _flags, script, FollowLink);
        if (id.Type == RecordTypes.Actor && json is not null)
        {
            // An actor is its scene, playing its initial state's animation.
            if ((string?)json["scene"] is not { } sceneText || !RecordId.TryParse(sceneText, out var scene) || plugins.FindAsset(scene, "glb") is not { } path)
                return Message("The actor's scene has no asset.");
            var initial = (int?)json["initialState"] ?? 0;
            var animation = (string?)json["states"]?[initial]?["animation"] is { } a && RecordId.TryParse(a, out var animationId)
                ? $"borg6 {Convert.ToInt32(animationId.Key, 16)}"
                : "";
            var note = $"Scene {scene.Key}, in its initial state ({initial})." + (json["head"] is not null ? " Its head isn't drawn here yet." : "");
            return (await File.ReadAllBytesAsync(path), animation, note);
        }
        if (MapFocus(plugins, id) is { } focus)
            return focus;
        if (entry?.Asset("glb") is { } glb)
        {
            var note = id.Type == RecordTypes.Map ? "Collision, in the chunk's own coordinates." : "";
            return (await File.ReadAllBytesAsync(glb.Path), "", note);
        }
        if (entry?.Asset("png") is { } png)
        {
            var image = await LoadPngAsync(png.Path);
            return image is null ? Message("Godot couldn't read the PNG.")
                : ImageGallery.Create([new ImageGallery.Item($"{image.GetWidth()}×{image.GetHeight()}", image)]);
        }
        if (json is not null && await ImagesNamedAsync(catalog, json) is { Count: > 0 } images)
            return ImageGallery.Create(images);
        return Message(json is null ? "" : "No preview for this type. See the fields.");
    }

    private static Label Message(string text) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };

    private static Task<Image?> LoadPngAsync(string path) => Task.Run(() =>
    {
        var image = new Image();
        return image.LoadPngFromBuffer(File.ReadAllBytes(path)) == Error.Ok ? image : null;
    });

    /// <summary>The images and textures a record names, such as a terrain's sky gradient, once each, in the order first named.</summary>
    private async Task<List<ImageGallery.Item>> ImagesNamedAsync(PluginCatalog catalog, JsonObject json)
    {
        var named = new List<(RecordId Id, string Path, int Count)>();
        foreach (var (path, target) in Links(json, ""))
        {
            if (target.Type is not (RecordTypes.Image or RecordTypes.Texture) || catalog.Find(target)?.Asset("png") is null)
                continue;
            var i = named.FindIndex(n => n.Id == target);
            if (i < 0)
                named.Add((target, path, 1));
            else
                named[i] = named[i] with { Count = named[i].Count + 1 };
        }
        var items = new List<ImageGallery.Item>();
        foreach (var (target, path, count) in named)
        {
            if (await LoadPngAsync(catalog.Find(target)!.Asset("png")!.Path) is not { } image)
                continue;
            var label = count > 1 ? $"{path} and {count - 1} more" : path;
            items.Add(new ImageGallery.Item(label, image, () => FollowLink(target), $"{target}. Click to open."));
        }
        return items;
    }

    /// <summary>Every field naming a record, with its path.</summary>
    private static IEnumerable<(string Path, RecordId Id)> Links(JsonNode? node, string path)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o)
                {
                    if (key == "id" && path.Length == 0)
                        continue;
                    foreach (var link in Links(value, path.Length == 0 ? key : $"{path}.{key}"))
                        yield return link;
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    foreach (var link in Links(a[i], $"{path}[{i}]"))
                        yield return link;
                }
                break;
            case JsonValue v when v.TryGetValue(out string? text) && RecordId.TryParse(text, out var id):
                yield return (path, id);
                break;
        }
    }

    /// <summary>
    /// A map set shows whole. A map shows the 3×3 chunks around it, as the game loads them, and a
    /// map object its map's, picked, unless more of its map set is loaded already.
    /// </summary>
    private static MapSetView.Focus? MapFocus(PluginSet plugins, RecordId id)
    {
        if (id.Type == RecordTypes.MapSet)
            return plugins.TryGet<MapSetRecord>(id) is { } set ? new MapSetView.Focus(set, set.Chunks) : null;
        if (id.Type is not (RecordTypes.Map or RecordTypes.Voxel))
            return null;
        var map = id.Type == RecordTypes.Map ? id : MapRecord.MapOf(id);
        if (MapObjects.Places(plugins).GetValueOrDefault(map) is not { } place)
            return null;
        var around = place.Set.Chunks.Where(c => Math.Abs(c.Row - place.Chunk.Row) <= 1 && Math.Abs(c.Column - place.Chunk.Column) <= 1).ToList();
        return new MapSetView.Focus(place.Set, around, place.Chunk, id.Type == RecordTypes.Voxel ? id : null);
    }

    private void SetPreview(Control content)
    {
        foreach (var child in _preview.GetChildren())
        {
            _preview.RemoveChild(child);
            if (child != _model && child != _map)
                child.QueueFree();
        }
        if (content != _model)
            _model.Clear();
        _preview.AddChild(content);
    }
}
