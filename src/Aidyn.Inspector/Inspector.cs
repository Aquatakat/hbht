using System.Globalization;
using Aidyn.Rom;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Graphics;
using Aidyn.Rom.Inspection;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// The asset inspector (roadmap 0.6): browse Borgs by type and index, with a preview, the decoded
/// fields and a hex dump. The decoding is in <see cref="BorgInspector"/>; this is only the UI.
/// </summary>
/// <remarks>
/// Command-line options, after <c>--</c>: <c>--rom &lt;path&gt;</c>, <c>--borg &lt;index&gt;</c>,
/// <c>--tab preview|fields|hex</c>, and <c>--screenshot &lt;png&gt;</c>, which saves the window
/// once the Borg is shown and quits.
/// </remarks>
public partial class Inspector : Control
{
    private const string Preview = "Preview", Fields = "Fields", Hex = "Hex";
    private const int AllTypes = 1000;

    private readonly OptionButton _roms = new() { TooltipText = "ROM" };
    private readonly Button _open = new() { Text = "Open ROM…" };
    private readonly OptionButton _types = new() { TooltipText = "Type" };
    private readonly LineEdit _search = new() { PlaceholderText = "Index or summary text", CustomMinimumSize = new Vector2(220, 0) };
    private readonly Label _status = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Tree _list = new();
    private readonly Label _header = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly TabContainer _tabs = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Control _preview = new MarginContainer { Name = Preview };
    private readonly FieldsView _fields = new() { Name = Fields };
    private readonly CodeEdit _hex = new() { Name = Hex, Editable = false };
    private readonly ModelView _model = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly List<(string Label, string Path)> _romFiles = [];
    private readonly Dictionary<int, TreeItem> _rows = [];

    private BorgInspector? _inspector;
    private string[] _summaries = [];
    private int _selected = -1;
    private int _generation;
    private Task _showing = Task.CompletedTask;

    public override void _Ready()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        BuildUi();
        _ = Report(StartAsync(Options(OS.GetCmdlineUserArgs())));
    }

    /// <summary>Prints what a fire-and-forget task threw, which would otherwise vanish.</summary>
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

    /// <summary>Finds and loads the ROMs off the main thread, so the window draws straight away.</summary>
    private async Task StartAsync(Dictionary<string, string> args)
    {
        var png = args.GetValueOrDefault("screenshot");
        if (png is not null)
            _ = Watchdog();
        if (args.TryGetValue("tab", out var tab))
            _tabs.CurrentTab = tab.ToLowerInvariant() switch { "fields" => 1, "hex" => 2, _ => 0 };

        _status.Text = "Looking for ROMs…";
        var first = args.GetValueOrDefault("rom");
        SetRoms(await Task.Run(() => FindRoms(first)));
        if (_romFiles.Count == 0)
        {
            _status.Text = "No ROM found in roms/. Open one to start.";
            return;
        }
        await LoadRomAsync(0);

        if (args.TryGetValue("borg", out var borg) && int.TryParse(borg, CultureInfo.InvariantCulture, out var index))
            await SelectAsync(index);
        if (png is not null)
            await Screenshot(png);
    }

    private void BuildUi()
    {
        var mono = new SystemFont { FontNames = ["Consolas", "Cascadia Mono", "Courier New", "monospace"] };
        _hex.AddThemeFontOverride("font", mono);
        _hex.AddThemeFontSizeOverride("font_size", 14);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        var bar = new HBoxContainer();
        bar.AddChild(_roms);
        bar.AddChild(_open);
        bar.AddChild(new VSeparator());
        bar.AddChild(_types);
        bar.AddChild(_search);
        bar.AddChild(_status);
        root.AddChild(bar);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [460] };
        root.AddChild(split);

        _list.Columns = 4;
        _list.HideRoot = true;
        _list.ColumnTitlesVisible = true;
        _list.SelectMode = Tree.SelectModeEnum.Row;
        string[] titles = ["Index", "Type", "Size", "Summary"];
        for (var c = 0; c < titles.Length; c++)
        {
            _list.SetColumnTitle(c, titles[c]);
            _list.SetColumnExpand(c, c == 3);
            _list.SetColumnClipContent(c, true);
        }
        _list.SetColumnCustomMinimumWidth(0, 64);
        _list.SetColumnCustomMinimumWidth(1, 110);
        _list.SetColumnCustomMinimumWidth(2, 90);
        _list.ItemSelected += () => Show(_list.GetSelected()?.GetMetadata(0).AsInt32() ?? -1);
        split.AddChild(_list);

        var right = new VBoxContainer();
        right.AddChild(_header);
        right.AddChild(_tabs);
        split.AddChild(right);
        _tabs.AddChild(_preview);
        _tabs.AddChild(_fields);
        _tabs.AddChild(_hex);

        // Not -1: to AddItem, that means "pick an ID".
        _types.AddItem("All types", AllTypes);
        foreach (var type in Enum.GetValues<BorgType>())
            _types.AddItem($"{(int)type} {type}", (int)type);
        _types.ItemSelected += _ => Populate();
        _search.TextChanged += _ => Populate();
        _roms.ItemSelected += i => _ = Report(LoadRomAsync((int)i));

        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.z64, *.v64, *.n64 ; N64 ROMs"],
            UseNativeDialog = true,
        };
        dialog.FileSelected += path => _ = Report(OpenAsync(path));
        AddChild(dialog);
        _open.Pressed += () => dialog.PopupCentered(new Vector2I(800, 500));
    }

    /// <summary>
    /// The ROMs in <c>roms/</c> (or <c>AIDYN_ROMS</c>), identified by hash: <paramref name="first"/>
    /// first, then the primary target (1.0) before the others. Runs off the main thread.
    /// </summary>
    private static List<(string Label, string Path, string? Error)> FindRoms(string? first)
    {
        var folder = System.Environment.GetEnvironmentVariable("AIDYN_ROMS")
            ?? Path.GetFullPath(Path.Combine(ProjectPath, "..", "..", "roms"));
        var paths = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".z64" or ".v64" or ".n64")
            : [];
        if (first is not null)
            paths = paths.Where(p => !string.Equals(Path.GetFullPath(p), Path.GetFullPath(first), StringComparison.OrdinalIgnoreCase)).Prepend(first);

        var found = new List<(RomVersion? Version, string Path, string? Error)>();
        foreach (var path in paths)
        {
            try
            {
                found.Add((RomImage.Load(path).Version, path, null));
            }
            catch (Exception e) when (e is UnsupportedRomException or InvalidDataException or IOException)
            {
                if (path == first)
                    found.Add((null, path, e.Message));
            }
        }
        return [.. found.OrderByDescending(f => f.Path == first).ThenBy(f => f.Version?.Id)
            .Select(f => (f.Version?.Name ?? "", f.Path, f.Error))];
    }

    private void SetRoms(List<(string Label, string Path, string? Error)> found)
    {
        _romFiles.Clear();
        _roms.Clear();
        foreach (var (label, path, error) in found)
        {
            if (error is not null)
            {
                _status.Text = error;
                continue;
            }
            _romFiles.Add((label, path));
            _roms.AddItem(label);
        }
    }

    private async Task OpenAsync(string path)
    {
        SetRoms(await Task.Run(() => FindRoms(path)));
        await LoadRomAsync(0);
    }

    private async Task LoadRomAsync(int i)
    {
        if (i < 0 || i >= _romFiles.Count)
            return;
        _roms.Select(i);
        _status.Text = $"Loading {_romFiles[i].Label}…";
        var path = _romFiles[i].Path;
        _inspector = await Task.Run(() => new BorgInspector(RomImage.Load(path)));
        _summaries = new string[_inspector.Archive.Count];
        _selected = -1;
        Populate();
        _ = Report(SummariseAsync(_inspector, ++_generation));
    }

    /// <summary>Decodes every Borg in the background, filling in the summary column as it goes.</summary>
    private async Task SummariseAsync(BorgInspector inspector, int generation)
    {
        var summaries = _summaries;
        const int Batch = 500;
        for (var start = 0; start < summaries.Length; start += Batch)
        {
            var end = Math.Min(start + Batch, summaries.Length);
            var from = start;
            await Task.Run(() =>
            {
                for (var i = from; i < end; i++)
                {
                    try
                    {
                        summaries[i] = BorgInspector.CanDecode(inspector.Archive.Listings[i].Type)
                            ? BorgInspector.Summary(inspector.Decode(i))
                            : "";
                    }
                    catch (Exception e)
                    {
                        summaries[i] = $"‹{e.Message}›";
                    }
                }
            });
            if (generation != _generation)
                return;
            for (var i = from; i < end; i++)
            {
                if (_rows.TryGetValue(i, out var row))
                    row.SetText(3, summaries[i]);
            }
            _status.Text = end < summaries.Length ? $"Decoding… {end:N0}/{summaries.Length:N0}" : Status();
        }
        if (_search.Text.Length > 0)
            Populate();
    }

    private string Status() => _inspector is null ? "" :
        $"{_inspector.Rom.Version.Name}: {_rows.Count:N0} of {_inspector.Archive.Count:N0} Borgs";

    /// <summary>Fills the list with the Borgs that match the type and search filters.</summary>
    private void Populate()
    {
        _list.Clear();
        _rows.Clear();
        if (_inspector is null)
            return;

        var type = _types.GetSelectedId();
        var search = _search.Text.Trim();
        var root = _list.CreateItem();
        foreach (var listing in _inspector.Archive.Listings)
        {
            if (type != AllTypes && (int)listing.Type != type)
                continue;
            var summary = _summaries[listing.Index] ?? "";
            if (search.Length > 0 && !listing.Index.ToString(CultureInfo.InvariantCulture).StartsWith(search, StringComparison.Ordinal)
                && !summary.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;

            var row = _list.CreateItem(root);
            row.SetText(0, listing.Index.ToString(CultureInfo.InvariantCulture));
            row.SetText(1, $"{(int)listing.Type} {listing.Type}");
            row.SetText(2, $"{listing.UncompressedSize:N0}");
            row.SetTextAlignment(2, HorizontalAlignment.Right);
            row.SetText(3, summary);
            row.SetMetadata(0, listing.Index);
            _rows[listing.Index] = row;
            if (listing.Index == _selected)
                row.Select(0);
        }
        _status.Text = Status();
    }

    /// <summary>Selects a Borg in the list, clearing the filters if they hide it.</summary>
    private async Task SelectAsync(int index)
    {
        if (_inspector is null || index < 0 || index >= _inspector.Archive.Count)
            return;
        if (!_rows.ContainsKey(index))
        {
            _types.Select(0);
            _search.Text = "";
            Populate();
        }
        _rows[index].Select(0);
        _list.ScrollToItem(_rows[index], true);
        // Selecting the row usually starts showing it already, through ItemSelected.
        if (index != _selected)
            Show(index);
        await _showing;
    }

    /// <summary>Starts showing a Borg. <see cref="_showing"/> completes when it's shown.</summary>
    private void Show(int index) => _showing = Report(ShowAsync(index));

    private async Task ShowAsync(int index)
    {
        if (_inspector is null || index < 0 || index == _selected)
            return;
        _selected = index;
        var inspector = _inspector;
        var listing = inspector.Archive.Listings[index];
        var romOffset = inspector.Rom.Layout.BorgArchive + listing.Offset;
        _header.Text = $"Borg {index} · {(int)listing.Type} {listing.Type} · {listing.Compression}, " +
                       $"{listing.CompressedSize:N0} bytes at ROM 0x{romOffset:X7} · {listing.UncompressedSize:N0} bytes";
        var header = _header.Text;
        SetPreview(new Label { Text = "Loading…" });

        // Decoding and building the preview can take a second (a scene with many animations), so
        // it's done off the main thread. Only the Godot objects are made here.
        var shown = await Task.Run(() => Build(inspector, index));
        if (index != _selected || inspector != _inspector)
            return;

        if (shown.Summary.Length > 0)
            _header.Text = $"{header}\n{shown.Summary}";
        _hex.Text = shown.Hex;
        _fields.Show(shown.Fields);
        switch (shown.Preview)
        {
            case IReadOnlyList<RgbaImage> images:
                SetPreview(ImagesView(images));
                break;
            case string text:
                var edit = new CodeEdit { Text = text, Editable = false, WrapMode = TextEdit.LineWrappingMode.Boundary };
                edit.AddThemeFontOverride("font", _hex.GetThemeFont("font"));
                SetPreview(edit);
                break;
            case ModelPreview model:
                SetPreview(_model);
                _model.Show(model);
                break;
            case Exception e:
                SetPreview(new Label { Text = $"No preview: {e.Message}", AutowrapMode = TextServer.AutowrapMode.WordSmart });
                break;
            default:
                SetPreview(new Label
                {
                    Text = BorgInspector.CanDecode(listing.Type) ? "No preview for this type. See the fields." : "Not decoded yet. See the hex.",
                });
                break;
        }
    }

    /// <summary>What <see cref="ShowAsync"/> shows: the hex, the fields, and images, text, a model or an error.</summary>
    private static (string Hex, string Summary, FieldNode[] Fields, object? Preview) Build(BorgInspector inspector, int index)
    {
        byte[] data;
        try
        {
            data = inspector.Archive.Read(index);
        }
        catch (Exception e)
        {
            return ("", "", [FieldTree.Of("error", e.Message), inspector.Listing(index)], e);
        }

        FieldNode fields;
        var summary = "";
        try
        {
            var decoded = BorgInspector.Decode(inspector.Archive.Listings[index].Type, data);
            summary = BorgInspector.Summary(decoded);
            fields = FieldTree.Of($"borg {index}", decoded);
        }
        catch (Exception e)
        {
            fields = FieldTree.Of("error", e.Message);
        }

        object? preview;
        try
        {
            preview = (object?)inspector.Images(index) ?? (object?)inspector.Text(index) ?? inspector.Model(index);
        }
        catch (Exception e)
        {
            preview = e;
        }
        return (BorgInspector.Hex(data), summary, [fields, inspector.Listing(index)], preview);
    }

    private static ScrollContainer ImagesView(IReadOnlyList<RgbaImage> images)
    {
        var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 16);
        // Small textures are blown up, in whole steps, to about 256 pixels.
        var zoom = Math.Max(1, 256 / Math.Max(images[0].Width, images[0].Height));
        for (var level = 0; level < images.Count; level++)
        {
            var image = images[level];
            var texture = ImageTexture.CreateFromImage(Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Pixels));
            var box = new VBoxContainer();
            box.AddChild(new Label { Text = images.Count > 1 ? $"Level {level}: {image.Width}×{image.Height}" : $"{image.Width}×{image.Height}" });
            var backdrop = new Panel { CustomMinimumSize = new Vector2(image.Width, image.Height) * zoom };
            backdrop.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.3f, 0.3f, 0.3f) });
            backdrop.AddChild(new TextureRect
            {
                Texture = texture,
                TextureFilter = TextureFilterEnum.Nearest,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Size = new Vector2(image.Width, image.Height) * zoom,
            });
            box.AddChild(backdrop);
            flow.AddChild(box);
        }
        var scroll = new ScrollContainer();
        scroll.AddChild(flow);
        return scroll;
    }

    private void SetPreview(Control content)
    {
        foreach (var child in _preview.GetChildren())
        {
            _preview.RemoveChild(child);
            if (child != _model)
                child.QueueFree();
        }
        if (content != _model)
            _model.Clear();
        _preview.AddChild(content);
    }

    /// <summary>In screenshot mode, quits with an error if the screenshot hasn't been taken within a minute.</summary>
    private async Task Watchdog()
    {
        await ToSignal(GetTree().CreateTimer(60), SceneTreeTimer.SignalName.Timeout);
        GD.PrintErr($"Timed out: {_status.Text}");
        GetTree().Quit(2);
    }

    private static readonly string ProjectPath = ProjectSettings.GlobalizePath("res://");

    private async Task Screenshot(string path)
    {
        // A few frames for the layout to settle and the preview to draw.
        for (var i = 0; i < 5; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(error == Error.Ok ? $"Saved {path}" : $"Couldn't save {path}: {error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private static Dictionary<string, string> Options(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                options[args[i][2..]] = args[i + 1];
        }
        return options;
    }
}
