using HBHT.Plugins;
using HBHT.Rom.Import;
using Godot;

namespace HBHT.Editor;

/// <summary>
/// The import page (docs/systems/editor.md#the-import-page): the ROM to import, the player's choices about the Roog Cave's mushroom
/// and the cracks between meshes (<see cref="ImportOptions"/>), and the importer run off the main thread with its steps shown. The editor shows it in place of the
/// record browser when there's no current import, and in a window from the browser's Import ROM…. Started by the game
/// (<see cref="AppLauncher.FromGame"/>), it ends with Play; otherwise with <see cref="Imported"/>.
/// </summary>
public partial class ImportPage : VBoxContainer
{
    private static readonly Color Dim = new(0.65f, 0.65f, 0.65f);
    private static readonly Color Problem = new(1f, 0.5f, 0.45f);

    private readonly string _pluginsDirectory;
    private readonly bool _fromGame;
    private readonly LineEdit _rom = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = "The ROM file: .z64, .n64 or .v64" };
    private readonly Button _browse = new() { Text = "Browse..." };
    private readonly CheckBox _fix = new() { Text = "Fix the mushroom (recommended)" };
    private readonly CheckBox _keep = new() { Text = "Don't fix the mushroom" };
    private readonly CheckBox _weld = new() { Text = "Close the cracks in the scenery (recommended)" };
    private readonly CheckBox _cracks = new() { Text = "Keep the cracks" };
    private readonly Button _import = new() { Text = "Import", CustomMinimumSize = new Vector2(120, 0) };
    private readonly Button _play = new() { Text = "Play", CustomMinimumSize = new Vector2(120, 0), Visible = false };
    private readonly Label _status = Wrapped("");
    private readonly FileDialog _dialog = new()
    {
        FileMode = FileDialog.FileModeEnum.OpenFile,
        Access = FileDialog.AccessEnum.Filesystem,
        Filters = ["*.z64, *.n64, *.v64 ; N64 ROMs"],
        UseNativeDialog = true,
        Title = "A North American 1.0 ROM of Aidyn Chronicles: The First Mage",
    };

    /// <summary>The import finished, when the game didn't start the editor: the editor goes on with the new plugins.</summary>
    public event Action? Imported;

    /// <summary>Whether the importer is running, when the page can't be closed.</summary>
    public bool Busy { get; private set; }

    /// <param name="pluginsDirectory">The plugins folder the base plugin goes in.</param>
    /// <param name="fromGame">The game started the editor for this: it ends with Play, which starts the game and closes the editor.</param>
    public ImportPage(string pluginsDirectory, bool fromGame)
    {
        (_pluginsDirectory, _fromGame) = (pluginsDirectory, fromGame);
        CustomMinimumSize = new Vector2(600, 0);
        AddThemeConstantOverride("separation", 10);

        // Out of date rather than missing: the import has to be done again.
        if (BasePlugin.IsImported(pluginsDirectory) && BasePlugin.Diagnose(pluginsDirectory) is not null)
            AddChild(Wrapped("Your import is from an older version of HBHT and has to be done again."));
        AddChild(Wrapped("A North American 1.0 ROM is required."));

        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "ROM:" });
        row.AddChild(_rom);
        row.AddChild(_browse);
        AddChild(row);

        var group = new ButtonGroup();
        _fix.ButtonGroup = _keep.ButtonGroup = group;
        var choices = new VBoxContainer();
        choices.AddChild(_fix);
        choices.AddChild(_keep);
        AddChild(choices);
        var about = Wrapped("Moves the mushroom slightly so you can walk past it. If you don't know what this is, select fix the mushroom. " +
            "If you select don't fix, it's still possible to get by, but it's more difficult than in the original.");
        about.AddThemeColorOverride("font_color", Dim);
        AddChild(about);

        var seams = new ButtonGroup();
        _weld.ButtonGroup = _cracks.ButtonGroup = seams;
        var seamChoices = new VBoxContainer();
        seamChoices.AddChild(_weld);
        seamChoices.AddChild(_cracks);
        AddChild(seamChoices);
        var aboutSeams = Wrapped("Where pieces of the scenery almost meet, the original shows thin cracks of sky between them. " +
            "Closing them joins the pieces. Keep the cracks to see the scenery exactly as the original has it.");
        aboutSeams.AddThemeColorOverride("font_color", Dim);
        AddChild(aboutSeams);

        var buttons = new HBoxContainer();
        buttons.AddChild(_import);
        buttons.AddChild(_play);
        AddChild(buttons);
        AddChild(_status);
        AddChild(_dialog);

        // What the last import was made from and with, to do again.
        if (File.Exists(DataPaths.LastRomFor(pluginsDirectory)))
            _rom.Text = File.ReadAllText(DataPaths.LastRomFor(pluginsDirectory)).Trim();
        var last = BasePlugin.ImportOf(pluginsDirectory)?.Options ?? ImportOptions.Default;
        FixMushroom = last.FixMushroom;
        WeldSeams = last.WeldSeams;

        _browse.Pressed += () =>
        {
            if (Path.GetDirectoryName(RomPath) is { Length: > 0 } folder && Directory.Exists(folder))
                _dialog.CurrentDir = folder;
            _dialog.PopupCentered(new Vector2I(800, 500));
        };
        _dialog.FileSelected += path => RomPath = path;
        _rom.TextSubmitted += text => _ = BackgroundTask.Report(ImportAsync());
        _import.Pressed += () => _ = BackgroundTask.Report(ImportAsync());
        _play.Pressed += Play;
    }

    /// <summary>The ROM's path, as typed or picked (<c>--rom</c>).</summary>
    public string RomPath
    {
        get => _rom.Text.Trim().Trim('"');
        set
        {
            _rom.Text = value;
            Say("");
        }
    }

    /// <summary>The mushroom choice (<c>--keep-mushroom</c>).</summary>
    public bool FixMushroom
    {
        get => _fix.ButtonPressed;
        set => (value ? _fix : _keep).ButtonPressed = true;
    }

    /// <summary>The cracks choice (<c>--keep-seams</c>).</summary>
    public bool WeldSeams
    {
        get => _weld.ButtonPressed;
        set => (value ? _weld : _cracks).ButtonPressed = true;
    }

    /// <summary>
    /// What Import does (and <c>--import 1</c>): checks the ROM is NA 1.0 and runs the importer into the plugins folder off the main
    /// thread, showing its steps, then remembers the ROM for next time. What's wrong is shown on the page, and the controls come back.
    /// </summary>
    public async Task ImportAsync()
    {
        if (Busy)
            return;
        var path = RomPath;
        var options = new ImportOptions(FixMushroom, WeldSeams);
        SetBusy(true);
        Say("Reading the ROM…");
        try
        {
            var progress = new Progress<string>(step => Say($"Importing: {step}…"));
            var directory = Path.Combine(_pluginsDirectory, BasePlugin.Folder);
            await Task.Run(() => BaseImporter.Import(ImportableRom.Open(path), directory, progress, options));
        }
        catch (Exception e)
        {
            // A bad ROM or a file that can't be read is the player's to fix, and worded for them; anything else comes with its stack.
            GD.PrintErr(e is InvalidDataException or IOException ? e.Message : e.ToString());
            Say(e.Message, problem: true);
            SetBusy(false);
            return;
        }
        try
        {
            File.WriteAllText(DataPaths.LastRomFor(_pluginsDirectory), Path.GetFullPath(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Couldn't remember the ROM: {e.Message}");
        }
        GD.Print($"Imported {path} to {_pluginsDirectory}.");
        if (_fromGame)
        {
            Busy = false;
            Say("The import is done.");
            _import.Visible = false;
            _play.Visible = true;
            _play.GrabFocus();
            return;
        }
        SetBusy(false);
        Say("");
        Imported?.Invoke();
    }

    /// <summary>Starts the game and closes the editor, or says to start it if it can't.</summary>
    private void Play()
    {
        if (AppLauncher.LaunchGame())
            GetTree().Quit();
        else
            Say("The game couldn't be started. Start HBHT yourself: the import is done.", problem: true);
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        _rom.Editable = !busy;
        _browse.Disabled = _fix.Disabled = _keep.Disabled = _weld.Disabled = _cracks.Disabled = _import.Disabled = busy;
    }

    private void Say(string text, bool problem = false)
    {
        _status.Text = text;
        if (problem)
            _status.AddThemeColorOverride("font_color", Problem);
        else
            _status.RemoveThemeColorOverride("font_color");
    }

    private static Label Wrapped(string text) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
}
