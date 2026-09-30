using System.Globalization;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// The editor (roadmap 0.6, ADR 0010): the installed plugins in <see cref="RecordBrowser"/>, and
/// the frozen raw <see cref="BorgBrowser"/> in a tab of its own, which reads the ROM only once
/// it's opened.
/// </summary>
/// <remarks>
/// Command-line options, after <c>--</c>: <c>--data &lt;plugins folder&gt;</c> (default: the
/// data folder's), <c>--record &lt;id&gt;</c>, <c>--borg &lt;index&gt;</c> and <c>--rom &lt;path&gt;</c>
/// for the Borg browser, <c>--tab preview|fields|used-by|hex</c>, and <c>--screenshot &lt;png&gt;</c>,
/// which saves the window once the record or Borg is shown and quits. For a record's 3D preview,
/// <c>--drive &lt;request&gt;</c> runs an actor's state machine, <c>--wireframe 1</c> and
/// <c>--scene-camera 1</c> set those switches, and <c>--wait &lt;seconds&gt;</c> waits before the
/// screenshot. <c>--checkpoint &lt;state&gt;</c> shows a checkpoint's flags on flag pages, and
/// <c>--play &lt;moves&gt;</c> plays a script's page that many moves.
/// </remarks>
public partial class Editor : Control
{
    private readonly TabContainer _modes = new();
    private readonly RecordBrowser _records = new() { Name = "Plugins" };
    private readonly BorgBrowser _borgs = new() { Name = "ROM Borgs (frozen)" };

    public override void _Ready()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        _modes.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_modes);
        _modes.AddChild(_records);
        _modes.AddChild(_borgs);
        _modes.TabChanged += tab =>
        {
            if (tab == 1 && !_borgs.Started)
                _ = Report(_borgs.StartAsync(null, null));
        };
        _ = Report(StartAsync(Options(OS.GetCmdlineUserArgs())));
    }

    private async Task StartAsync(Dictionary<string, string> args)
    {
        var png = args.GetValueOrDefault("screenshot");
        if (png is not null)
            _ = Watchdog();

        if (args.ContainsKey("borg") || args.ContainsKey("rom"))
        {
            if (args.TryGetValue("tab", out var tab))
                _borgs.SelectTab(tab);
            var borg = args.TryGetValue("borg", out var text) && int.TryParse(text, CultureInfo.InvariantCulture, out var index) ? index : (int?)null;
            // Started before the tab is shown, so showing it doesn't start it again.
            var starting = _borgs.StartAsync(args.GetValueOrDefault("rom"), borg);
            _modes.CurrentTab = 1;
            await starting;
        }
        else
        {
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
            if (args.TryGetValue("drive", out var request))
                _records.Model.Drive(int.Parse(request, CultureInfo.InvariantCulture));
            _records.Model.SetWireframe(args.GetValueOrDefault("wireframe") == "1");
            _records.Model.SetSceneCamera(args.GetValueOrDefault("scene-camera") == "1");
        }
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
