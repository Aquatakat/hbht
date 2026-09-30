using System.Globalization;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Inspector;

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
/// screenshot. <c>--checkpoint &lt;state&gt;</c> shows a checkpoint's flags on flag pages, and
/// <c>--play &lt;moves&gt;</c> plays a script's page that many moves, with its conversation box shown at
/// <c>--zoom &lt;1-3&gt;</c> and <c>--ui-filter pixels|videoout|smooth</c>.
/// </remarks>
public partial class Editor : Control
{
    private readonly RecordBrowser _records = new() { Name = "Plugins" };

    public override void _Ready()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        _records.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_records);
        _ = Report(StartAsync(Options(OS.GetCmdlineUserArgs())));
    }

    private async Task StartAsync(Dictionary<string, string> args)
    {
        var png = args.GetValueOrDefault("screenshot");
        if (png is not null)
            _ = Watchdog();

        if (args.TryGetValue("tab", out var tab))
            _records.SelectTab(tab);
        await _records.LoadAsync(args.GetValueOrDefault("data") ?? DataPaths.Plugins);
        if (args.TryGetValue("checkpoint", out var checkpoint))
        {
            // A bad checkpoint shouldn't stop the record opening, or a script waits for the watchdog.
            try
            {
                _records.LoadCheckpoint(checkpoint);
            }
            catch (Exception e)
            {
                GD.PrintErr($"Couldn't load the checkpoint {checkpoint}: {e.Message}");
            }
        }
        if (args.TryGetValue("record", out var record))
            await _records.OpenAsync(RecordId.Parse(record));
        if (args.TryGetValue("play", out var moves))
            _records.PlayScript(int.Parse(moves, CultureInfo.InvariantCulture));
        if (args.ContainsKey("zoom") || args.ContainsKey("ui-filter"))
        {
            _records.SetScriptScreen(args.TryGetValue("zoom", out var zoom) ? int.Parse(zoom, CultureInfo.InvariantCulture) : null,
                args.TryGetValue("ui-filter", out var filter) ? Enum.Parse<Aidyn.Game.UiFilter>(filter, ignoreCase: true) : null);
        }
        if (args.TryGetValue("drive", out var request))
            _records.Model.Drive(int.Parse(request, CultureInfo.InvariantCulture));
        _records.Model.SetWireframe(args.GetValueOrDefault("wireframe") == "1");
        _records.Model.SetSceneCamera(args.GetValueOrDefault("scene-camera") == "1");
        if (args.TryGetValue("collision", out var collision))
            _records.Map.SetCollision(collision);
        if (args.TryGetValue("wait", out var wait))
            await ToSignal(GetTree().CreateTimer(double.Parse(wait, CultureInfo.InvariantCulture)), SceneTreeTimer.SignalName.Timeout);
        if (png is not null)
            await Screenshot(png);
    }

    /// <summary>Prints what a fire-and-forget task threw, which would otherwise vanish.</summary>
    private static async Task Report(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            GD.PrintErr(e.ToString());
        }
    }

    /// <summary>In screenshot mode, quits with an error if the screenshot hasn't been taken within a minute.</summary>
    private async Task Watchdog()
    {
        await ToSignal(GetTree().CreateTimer(60), SceneTreeTimer.SignalName.Timeout);
        GD.PrintErr("Timed out before the screenshot.");
        GetTree().Quit(2);
    }

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
