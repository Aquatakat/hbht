using System.Globalization;
using HBHT.Plugins;
using Godot;

namespace HBHT.Editor;

/// <summary>
/// The editor (roadmap 0.6, ADR 0010): the installed plugins in <see cref="RecordBrowser"/>.
/// </summary>
/// <remarks>
/// Command-line options, after <c>--</c>: <c>--data &lt;plugins folder&gt;</c> (default: the
/// data folder's), <c>--record &lt;id&gt;</c>, <c>--tab preview|fields|used-by|atlas</c>, and
/// <c>--screenshot &lt;png&gt;</c>, which saves the window once the record is shown and quits. For a record's 3D preview,
/// <c>--drive &lt;request&gt;</c> runs an actor's state machine, <c>--wireframe 1</c> and
/// <c>--scene-camera 1</c> set those switches, <c>--collision surface|role</c> colours a map's
/// collision, and <c>--wait &lt;seconds&gt;</c> waits before the
/// screenshot. <c>--checkpoint &lt;state&gt;</c> and <c>--save &lt;save&gt;</c> load a checkpoint's or a
/// save's game, which flag pages, scripts and the party's pages show, and
/// <c>--play &lt;moves&gt;</c> plays a script's page that many moves, with its conversation box shown at
/// <c>--zoom &lt;1-3&gt;</c>, <c>--ui-filter pixels|videoout|smooth</c>, <c>--aspect 4:3|16:9</c> and
/// <c>--ui-lines 240|360|480</c>. The inventory record's Party tab takes the same, and
/// <c>--member &lt;slot&gt;</c>, <c>--list inventory|spells|skills|stats</c>, <c>--row &lt;n&gt;</c> and
/// <c>--details 1</c>, and <c>--press &lt;a,b,up,down&gt;</c> presses those buttons in turn after. <c>--roll 1</c> rolls a
/// loot table's page, or opens the container a map object's page picks, in the loot window, whose buttons
/// <c>--press</c> also takes, with <c>left</c> and <c>right</c>. <c>--look 1</c>, after the wait, looks through a camera object's page's camera. <c>--ui-screenshot &lt;png&gt;</c> saves the UI screen too, in its own pixels,
/// before it's scaled up. With no current import it shows the import page (<see cref="ImportPage"/>) instead: <c>--from-game 1</c>,
/// which the game passes, ends it with Play, and <c>--rom &lt;path&gt;</c>, <c>--keep-mushroom 1</c>, <c>--keep-seams 1</c> and <c>--import 1</c> fill it in
/// and press Import.
/// </remarks>
public partial class Editor : Control
{
    private readonly RecordBrowser _records = new() { Name = "Plugins" };
    private string? _uiShot;

    /// <summary>A portable install's data folder is found before any field reads a path, such as the browser's plugins folder.</summary>
    static Editor() => AppLauncher.UseAppFolder();

    public override void _Ready()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        // Lit meshes are lit as the RSP lights them, by the game's sky, as in the runtime (ADR 0010), not by Godot's lights.
        // The editor keeps linear light, so its own overlays draw as they are: no on-screen framebuffer.
        N64Material.RspLighting = true;
        _records.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_records);
        _ = BackgroundTask.Report(StartAsync(CommandLine.Parse(OS.GetCmdlineUserArgs())));
    }

    private async Task StartAsync(Dictionary<string, string> args)
    {
        var png = args.GetValueOrDefault("screenshot");
        _uiShot = args.GetValueOrDefault("ui-screenshot");
        // An import takes a few minutes.
        if (png is not null)
            CommandLine.Watchdog(GetTree(), args.GetValueOrDefault("import") == "1" ? 900 : 60);

        if (args.TryGetValue("tab", out var tab))
            _records.SelectTab(tab);
        var plugins = args.GetValueOrDefault("data") ?? DataPaths.Plugins;
        if (BasePlugin.Diagnose(plugins) is not null && !await ImportFirstAsync(plugins, args, png))
            return;
        await _records.LoadAsync(plugins);
        // A bad checkpoint or save shouldn't stop the record opening, or a script waits for the watchdog.
        if (args.TryGetValue("checkpoint", out var checkpoint))
        {
            try
            {
                _records.LoadCheckpoint(checkpoint);
            }
            catch (Exception e)
            {
                GD.PrintErr($"Couldn't load the checkpoint {checkpoint}: {e.Message}");
            }
        }
        if (args.TryGetValue("save", out var save))
        {
            try
            {
                _records.LoadSave(save);
            }
            catch (Exception e)
            {
                GD.PrintErr($"Couldn't load the save {save}: {e.Message}");
            }
        }
        if (args.TryGetValue("record", out var record))
            await _records.OpenAsync(RecordId.Parse(record));
        if (args.TryGetValue("play", out var moves))
            _records.PlayScript(int.Parse(moves, CultureInfo.InvariantCulture));
        if (args.GetValueOrDefault("roll") == "1")
            _records.Roll();
        _records.SetUiScreen(args.TryGetValue("zoom", out var zoom) ? int.Parse(zoom, CultureInfo.InvariantCulture) : null,
            args.TryGetValue("ui-filter", out var filter) ? Enum.Parse<HBHT.Core.Ui.UiFilter>(filter, ignoreCase: true) : null,
            args.TryGetValue("aspect", out var aspect) ? aspect == "16:9" : null,
            args.TryGetValue("ui-lines", out var lines) ? int.Parse(lines, CultureInfo.InvariantCulture) : null);
        _records.ShowPartyTab(args.TryGetValue("member", out var member) ? int.Parse(member, CultureInfo.InvariantCulture) : null,
            args.TryGetValue("list", out var list) ? Enum.Parse<HBHT.Core.Ui.PartyList>(list, ignoreCase: true) : null,
            args.TryGetValue("row", out var row) ? int.Parse(row, CultureInfo.InvariantCulture) : null,
            args.TryGetValue("details", out var details) ? details == "1" : null);
        if (args.TryGetValue("press", out var presses))
            _records.PressPartyTab(presses.Split(','));
        if (args.TryGetValue("drive", out var request))
            _records.Model.Drive(int.Parse(request, CultureInfo.InvariantCulture));
        _records.Model.SetWireframe(args.GetValueOrDefault("wireframe") == "1");
        _records.Model.SetSceneCamera(args.GetValueOrDefault("scene-camera") == "1");
        if (args.TryGetValue("collision", out var collision))
            _records.Map.SetCollision(collision);
        if (args.TryGetValue("wait", out var wait))
            await ToSignal(GetTree().CreateTimer(double.Parse(wait, CultureInfo.InvariantCulture)), SceneTreeTimer.SignalName.Timeout);
        if (args.GetValueOrDefault("look") == "1")
            _records.Map.LookThrough();
        if (png is not null)
            await Screenshot(png);
    }

    /// <summary>
    /// With no current import, the import page in the browser's place (editor.md#the-import-page): true once it's imported and the
    /// editor is to go on, false if it stays on the page, as it does when the game started it (Play ends it) or a screenshot was taken.
    /// <c>--rom</c>, <c>--keep-mushroom 1</c>, <c>--keep-seams 1</c> and <c>--import 1</c> fill it in and press Import, for scripts.
    /// </summary>
    private async Task<bool> ImportFirstAsync(string plugins, Dictionary<string, string> args, string? png)
    {
        var page = new ImportPage(plugins, AppLauncher.StartedFromGame(OS.GetCmdlineUserArgs()));
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.AddChild(page);
        _records.Visible = false;
        AddChild(center);
        var imported = new TaskCompletionSource();
        page.Imported += () => imported.TrySetResult();
        if (args.TryGetValue("rom", out var rom))
            page.RomPath = rom;
        if (args.GetValueOrDefault("keep-mushroom") == "1")
            page.FixMushroom = false;
        if (args.GetValueOrDefault("keep-seams") == "1")
            page.WeldSeams = false;
        if (args.GetValueOrDefault("import") == "1")
            await page.ImportAsync();
        if (png is not null && !imported.Task.IsCompleted)
        {
            await Screenshot(png);
            return false;
        }
        await imported.Task;
        center.QueueFree();
        _records.Visible = true;
        return true;
    }

    private async Task Screenshot(string path)
    {
        // A few frames for the layout to settle and the preview to draw.
        await CommandLine.Frames(this, 5);
        var error = CommandLine.Save(GetViewport().GetTexture().GetImage(), path);
        if (_uiShot is { } ui && Descendants(this).OfType<UiScreen>().FirstOrDefault(s => s.IsVisibleInTree()) is { } screen)
            CommandLine.Save(screen.Capture(), ui);
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(c => Descendants(c).Prepend(c));
}
