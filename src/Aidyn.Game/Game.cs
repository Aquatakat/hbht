using System.Globalization;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The runtime (roadmap M1): loads the installed plugins and walks the player through a map set.
/// It never reads the ROM; <c>aidyn import &lt;rom&gt;</c> writes the base plugin it loads (ADR 0001).
/// </summary>
/// <remarks>
/// Command-line options, after <c>--</c>: <c>--set &lt;n&gt;</c> (default 0, the overworld),
/// <c>--chunk &lt;name&gt;</c> and <c>--at &lt;x&gt;,&lt;z&gt;</c> in the chunk's coordinates
/// (default I08 at 16, 62, where a new game starts), <c>--yaw</c>, <c>--pitch</c> and
/// <c>--distance</c> for the camera, <c>--fly &lt;x&gt;,&lt;y&gt;,&lt;z&gt;</c> to start with the free
/// camera there in world coordinates, looking along <c>--yaw</c> and <c>--pitch</c>, and
/// <c>--hold &lt;action&gt;,&lt;seconds&gt;</c>, which holds an input such as <c>move_forward</c>,
/// and <c>--screenshot &lt;png&gt;</c>, which saves the window after that and quits.
/// F toggles the free camera, and F3 the debug overlay.
/// </remarks>
public partial class Game : Node3D
{
    private readonly Label _overlay = new() { Position = new Vector2(8, 8) };
    private readonly Label _message = new() { Position = new Vector2(16, 16) };
    private Models? _models;
    private ChunkStreamer? _chunks;
    private Player? _player;
    private CameraRig? _camera;

    public override void _Ready()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        AddInputs();
        var hud = new CanvasLayer();
        _overlay.AddThemeColorOverride("font_outline_color", Colors.Black);
        _overlay.AddThemeConstantOverride("outline_size", 4);
        hud.AddChild(_overlay);
        hud.AddChild(_message);
        AddChild(hud);

        var args = Options(OS.GetCmdlineUserArgs());
        try
        {
            Start(args);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or KeyNotFoundException or System.Text.Json.JsonException)
        {
            GD.PrintErr(e.ToString());
            _message.Text = e.Message;
            if (args.ContainsKey("screenshot"))
                GetTree().Quit(1);
        }
    }

    private void Start(Dictionary<string, string> args)
    {
        var plugins = LoadPlugins();
        var set = plugins.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, Int(args, "set", 0)));

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                // Placeholders until the sky, fog and time of day are ported.
                BackgroundColor = new Color(0.55f, 0.7f, 0.9f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.5f, 0.5f, 0.5f),
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, 30, 0), LightEnergy = 0.9f });

        var models = _models = new Models(plugins);
        _chunks = new ChunkStreamer(plugins, models, set);
        AddChild(_chunks);

        var chunkName = args.GetValueOrDefault("chunk", "I08");
        var chunk = _chunks.Find(chunkName) ?? throw new KeyNotFoundException($"{set.Label} has no chunk {chunkName}.");
        var at = Floats(args.GetValueOrDefault("at", "16,62"));
        _player = new Player { Position = _chunks.Origin(chunk) + new Vector3(at[0], 10, at[1]) };
        AddChild(_player);
        // Alaron, as a new game sets the player's form (BORG7_Alaron).
        var alaron = plugins.Get<ActorRecord>(RecordId.Hex(RecordTypes.Actor, 0x25F9));
        if (models.InstantiateActor(alaron) is var (model, animator))
        {
            _player.SetModel(model);
            _player.Animator = animator;
        }

        _camera = new CameraRig
        {
            Target = _player,
            Yaw = Mathf.DegToRad(Float(args, "yaw", 0)),
            Pitch = Mathf.DegToRad(Float(args, "pitch", -17)),
        };
        AddChild(_camera);
        if (args.TryGetValue("distance", out var distance))
            _camera.SetDistance(float.Parse(distance, CultureInfo.InvariantCulture));
        _player.CameraYaw = () => _camera.Yaw;
        _chunks.Update(_player.GlobalPosition);
        _ = SnapToGround(_player);

        if (args.TryGetValue("fly", out var fly))
        {
            _camera.ToggleFly();
            var p = Floats(fly);
            _camera.Current.GlobalPosition = new Vector3(p[0], p[1], p[2]);
            _camera.Current.Rotation = new Vector3(_camera.Pitch, _camera.Yaw, 0);
            _player.Frozen = true;
        }
        var hold = 0.0;
        if (args.TryGetValue("hold", out var held) && held.Split(',') is [var action, var seconds])
        {
            hold = double.Parse(seconds, CultureInfo.InvariantCulture);
            Input.ActionPress(action);
            GetTree().CreateTimer(hold).Timeout += () => Input.ActionRelease(action);
        }
        if (args.TryGetValue("screenshot", out var png))
            _ = Screenshot(png, hold);
    }

    /// <summary>The installed plugins, after checking the base plugin is there and up to date.</summary>
    private static PluginSet LoadPlugins()
    {
        var manifest = Path.Combine(DataPaths.Base, PluginManifest.FileName);
        if (!File.Exists(manifest))
            throw new IOException($"The game hasn't been imported. Run: aidyn import <rom>\n(looked in {DataPaths.Base})");
        var plugins = PluginSet.LoadInstalled(DataPaths.Plugins);
        var import = plugins.Plugins[0].Manifest.Import;
        if (import?.Importer != BasePlugin.ImporterVersion)
            throw new IOException($"The import is out of date (importer {import?.Importer}, now {BasePlugin.ImporterVersion}). Run: aidyn import <rom>");
        return plugins;
    }

    public override void _ExitTree() => _models?.Dispose();

    public override void _Process(double delta)
    {
        if (_chunks is null || _player is null || _camera is null)
            return;
        var focus = _camera.Flying ? _camera.Current.GlobalPosition : _player.GlobalPosition;
        _chunks.Update(focus);

        if (_overlay.Visible)
        {
            var chunk = _chunks.ChunkAt(focus);
            var local = chunk is null ? focus : focus - _chunks.Origin(chunk);
            _overlay.Text = $"{_chunks.MapSet.Label} {chunk?.Name ?? "(no chunk)"}  " +
                $"({local.X:F1}, {local.Y:F1}, {local.Z:F1})  {Engine.GetFramesPerSecond()} fps" +
                (_camera.Flying ? "  [free camera]" : "") + "\n" + Ground(focus);
        }
    }

    /// <summary>
    /// Puts the player on the ground below the spawn point, once the chunks' collision is in the
    /// physics world. The original spawns at a height of 10 and resolves collision straight away.
    /// </summary>
    private async Task SnapToGround(Player player)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var from = player.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 1000, Collision.PlayerLayer);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            player.GlobalPosition = (Vector3)hit["position"];
            player.Velocity = Vector3.Zero;
        }
    }

    /// <summary>What collision is below a point, for the overlay.</summary>
    private string Ground(Vector3 at)
    {
        var query = PhysicsRayQueryParameters3D.Create(at + Vector3.Up, at + Vector3.Down * 100, Collision.PlayerLayer);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return "no ground below";
        var body = (Node)hit["collider"];
        return $"ground at {((Vector3)hit["position"]).Y:F2}: {body.GetParent().GetParent().Name} {body.Name}";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("toggle_fly") && _camera is not null && _player is not null)
        {
            _camera.ToggleFly();
            _player.Frozen = _camera.Flying;
        }
        else if (e.IsActionPressed("toggle_overlay"))
        {
            _overlay.Visible = !_overlay.Visible;
        }
    }

    private async Task Screenshot(string path, double after)
    {
        var watchdog = GetTree().CreateTimer(60 + after);
        watchdog.Timeout += () =>
        {
            GD.PrintErr("Timed out before the screenshot.");
            GetTree().Quit(2);
        };
        // Time for the player to land and the frame to settle.
        await ToSignal(GetTree().CreateTimer(1.5 + after), SceneTreeTimer.SignalName.Timeout);
        for (var i = 0; i < 3; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(error == Error.Ok ? $"Saved {path}" : $"Couldn't save {path}: {error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private static void AddInputs()
    {
        Add("move_forward", Key.W, Key.Up, JoyAxis.LeftY, -1);
        Add("move_back", Key.S, Key.Down, JoyAxis.LeftY, 1);
        Add("move_left", Key.A, Key.Left, JoyAxis.LeftX, -1);
        Add("move_right", Key.D, Key.Right, JoyAxis.LeftX, 1);
        Add("camera_left", Key.Q, null, JoyAxis.RightX, -1);
        Add("camera_right", Key.E, null, JoyAxis.RightX, 1);
        Add("walk", Key.Shift);
        Add("fly_up", Key.Space);
        Add("fly_down", Key.C);
        Add("toggle_fly", Key.F);
        Add("toggle_overlay", Key.F3);

        static void Add(string action, Key key, Key? alternative = null, JoyAxis? axis = null, float direction = 0)
        {
            if (InputMap.HasAction(action))
                return;
            InputMap.AddAction(action, 0.1f);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
            if (alternative is { } a)
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = a });
            if (axis is { } j)
                InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = j, AxisValue = direction });
        }
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

    private static int Int(Dictionary<string, string> args, string name, int fallback) =>
        args.TryGetValue(name, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    private static float Float(Dictionary<string, string> args, string name, float fallback) =>
        args.TryGetValue(name, out var v) ? float.Parse(v, CultureInfo.InvariantCulture) : fallback;

    private static float[] Floats(string text) =>
        [.. text.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture))];
}
