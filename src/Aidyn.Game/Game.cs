using System.Globalization;
using Aidyn.Core.Cameras;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Inspector;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The runtime (roadmap M1): loads the installed plugins and walks the player through a map set.
/// It never reads the ROM; <c>aidyn import &lt;rom&gt;</c> writes the base plugin it loads (ADR 0001).
/// </summary>
/// <remarks>
/// The player and camera are the core's port of the original, stepped once per 60 Hz tick in the
/// physics process; Godot interpolates between ticks for display.
/// Command-line options, after <c>--</c>: <c>--set &lt;n&gt;</c> (default 0, the overworld),
/// <c>--chunk &lt;name&gt;</c> and <c>--at &lt;x&gt;,&lt;z&gt;</c> in the chunk's coordinates
/// (default I08 at 16, 62, where a new game starts), <c>--yaw</c> in degrees for the way the
/// player and camera face (0 looks along −Z), <c>--pitch</c> (negative looks down, default −15)
/// and <c>--distance</c> for the camera, <c>--pace</c> for the player's speed (1 is the
/// original's), <c>--fly &lt;x&gt;,&lt;y&gt;,&lt;z&gt;</c> to start with the free camera there in
/// world coordinates, looking along <c>--yaw</c> and <c>--pitch</c>, <c>--time &lt;hours&gt;</c>
/// for the time of day (a new game starts at 12), and
/// <c>--hold &lt;action&gt;,&lt;seconds&gt;</c>, which holds an input such as <c>move_forward</c>
/// (join several with <c>+</c>, as in <c>move_forward+walk</c>),
/// and <c>--screenshot &lt;png&gt;</c>, which saves the window after that and quits.
/// <c>--sky original</c> places the sky's sprites as the original does rather than projected.
/// F toggles the free camera, F3 the debug overlay, F5 the pace and F6 the sky's placement.
/// </remarks>
public partial class Game : Node3D
{
    /// <summary>A faster pace than the original's, which feels slow on a PC (ADR 0006's fidelity settings).</summary>
    private const float BriskPace = 1.3f;

    /// <summary>Mouse drag in pixels to stick units: about 0.3° of orbit, and 0.02 of height, a pixel.</summary>
    private static readonly Vector2 DragToStick = new(0.2f, 0.4f);

    private static readonly string[] Surfaces =
    [
        "Default", "Player and camera", "Player", "Snow", "Grass", "Sand", "Mucky", "Sizzle", "Cave", "Wood",
        "Wood (echo)", "Stone", "Stone (echo)", "Stone (huge echo)", "Water", "Trans snow", "Trans grass",
        "Trans sand", "Trans mucky", "Trans sizzle", "Trans cave", "Trans wood", "Trans wood (echo)",
        "Trans stone", "Trans stone (echo)", "Trans stone (huge echo)",
    ];

    private readonly Label _overlay = new() { Position = new Vector2(8, 8) };
    private readonly Label _message = new() { Position = new Vector2(16, 16) };
    private readonly CollisionWorld _world = new();
    private Models? _models;
    private ChunkStreamer? _chunks;
    private Player? _player;
    private CameraRig? _camera;
    private ShadowDecal? _shadow;
    private SkyView? _sky;
    private FollowCamera? _follow;
    private IRootMotion _animation = new Still();
    /// <summary>The player's last second of positions, for the overlay's speed.</summary>
    private readonly Queue<System.Numerics.Vector3> _track = new();
    private long _ticks;

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
                // The sky's backdrop, a canvas layer behind the scene.
                BackgroundMode = Godot.Environment.BGMode.Canvas,
                BackgroundCanvasMaxLayer = SkyBackdropView.Layer,
            },
        });
        // Lit meshes are lit as the RSP lights them, by the sky and the sun, not by Godot's lights.
        N64Material.RspLighting = true;

        _sky = new SkyView(plugins);
        AddChild(_sky);
        AddChild(SkyBackdropView.Create(_sky));
        AddChild(LensFlareView.Create(_sky));
        _sky.SkyShows = SkyShows;
        _sky.Placement = args.GetValueOrDefault("sky") == "original" ? Core.Sky.SkyPlacement.Original : Core.Sky.SkyPlacement.Projected;
        if (args.TryGetValue("time", out var time))
            _sky.Clock.SetTimeOfDay(float.Parse(time, CultureInfo.InvariantCulture));

        var models = _models = new Models(plugins);
        _chunks = new ChunkStreamer(plugins, models, set, _world);
        AddChild(_chunks);

        var chunkName = args.GetValueOrDefault("chunk", "I08");
        var chunk = _chunks.Find(chunkName) ?? throw new KeyNotFoundException($"{set.Label} has no chunk {chunkName}.");
        var at = Floats(args.GetValueOrDefault("at", "16,62"));
        // The original drops the player in from a height of 10 at a new game's spawn. This puts him
        // on the highest ground below 1000, so --at works anywhere.
        var spawn = _chunks.Origin(chunk) + new Vector3(at[0], 10, at[1]);
        _chunks.Update(spawn);
        _sky.Enter(chunk);
        _sky.Apply();
        var centre = spawn.ToNumerics();
        var above = centre with { Y = 1000 };
        if (SpherePhysics.Sweep(_world, above, above - new System.Numerics.Vector3(0, 2000, 0), 0.5f, false, out var ground, out var normal))
            centre = ground + normal * 0.5f;

        var yaw = Mathf.DegToRad(Float(args, "yaw", 0));
        var look = new System.Numerics.Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));
        var motion = new PlayerMotion(centre, look) { Pace = Float(args, "pace", 1) };
        _player = new Player(motion);
        AddChild(_player);
        // Alaron, as a new game sets the player's form (BORG7_Alaron).
        var alaron = plugins.Get<ActorRecord>(RecordId.Hex(RecordTypes.Actor, 0x25F9));
        if (models.InstantiateActor(alaron) is var (model, animator))
        {
            _player.SetModel(model);
            ShadowDecal.MarkBody(model);
            if (animator is not null)
            {
                animator.Driven = true;
                _animation = animator;
            }
        }

        // The texture engine code gives bodies' shadows (BORG1_PlayerShadow).
        if (plugins.FindAsset(RecordId.Hex(RecordTypes.Texture, 0x0003), "png") is { } shadowPng)
        {
            var image = new Image();
            image.LoadPngFromBuffer(File.ReadAllBytes(shadowPng));
            _shadow = new ShadowDecal(ImageTexture.CreateFromImage(image));
            AddChild(_shadow);
        }

        _follow = new FollowCamera(centre, look);
        var pitch = -Float(args, "pitch", -15);
        _follow.Pitch = _follow.DefaultPitch = pitch;
        if (args.TryGetValue("distance", out var distance))
            _follow.Distance = _follow.DefaultDistance = float.Parse(distance, CultureInfo.InvariantCulture);
        _camera = new CameraRig { Follow = _follow, Yaw = yaw, Pitch = Mathf.DegToRad(-pitch) };
        AddChild(_camera);
        _camera.Sync();

        if (args.TryGetValue("fly", out var fly))
        {
            _camera.ToggleFly();
            var p = Floats(fly);
            _camera.Current.GlobalPosition = new Vector3(p[0], p[1], p[2]);
            _camera.Current.Rotation = new Vector3(_camera.Pitch, _camera.Yaw, 0);
        }
        var hold = 0.0;
        if (args.TryGetValue("hold", out var held) && held.Split(',') is [var action, var seconds])
        {
            hold = double.Parse(seconds, CultureInfo.InvariantCulture);
            var actions = action.Split('+');
            foreach (var a in actions)
                Input.ActionPress(a);
            GetTree().CreateTimer(hold).Timeout += () =>
            {
                foreach (var a in actions)
                    Input.ActionRelease(a);
            };
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

    /// <summary>One 60 Hz tick: the camera, then the player, as the original orders them.</summary>
    public override void _PhysicsProcess(double delta)
    {
        if (_player is null || _camera is null || _follow is null)
            return;
        _sky!.Enter(_chunks!.ChunkAt(_player.Motion.Sphere.Position.ToGodot()));
        _sky.Step();
        ScrollTextures();
        if (_camera.Flying)
            return;
        var input = ReadInput();
        var motion = _player.Motion;
        var before = motion.Sphere.Position;
        _follow.Step(_world, motion.Sphere.Position, motion.Heading);
        motion.Step(_world, input, _follow, _animation);
        _player.Sync();
        _camera.Sync();
        PlaceShadow();
        _track.Enqueue(motion.Sphere.Position);
        if (_track.Count > Engine.PhysicsTicksPerSecond)
            _track.Dequeue();
        // A fall that sends him back to safe ground is a jump, not a move.
        if (System.Numerics.Vector3.Distance(before, motion.Sphere.Position) > 2)
        {
            _player.ResetPhysicsInterpolation();
            _camera.ResetPhysicsInterpolation();
            _shadow?.ResetPhysicsInterpolation();
        }
    }

    /// <summary>
    /// Once a frame at the port's cadence, the scenes the original would draw load their own copies
    /// of their scrolling textures, which moves them on.
    /// </summary>
    private void ScrollTextures()
    {
        if (++_ticks % Core.Cadence.FrameTicks != 0)
            return;
        var camera = _camera!.Current.GlobalTransform;
        var focus = _camera.Flying ? camera.Origin : _player!.Motion.Sphere.Position.ToGodot();
        foreach (var scene in _chunks!.DrawnScenes(focus, camera.Origin, -camera.Basis.Z))
            scene.Draw();
    }

    /// <summary>
    /// Whether the sky shows at a point on the original's 320 × 240 screen, for the lens flare. The
    /// original reads the depth buffer there; this sends a ray through it from the camera to the far
    /// plane, against the collision, which has the ground and buildings but not every leaf.
    /// </summary>
    private bool SkyShows(System.Numerics.Vector2 at)
    {
        if (_camera is null)
            return false;
        var camera = _camera.Current;
        var size = GetViewport().GetVisibleRect().Size;
        var scale = size.Y / Core.Sky.SkyScreen.Height;
        var pixel = new Vector2((size.X - Core.Sky.SkyScreen.Width * scale) / 2, 0) + at.ToGodot() * scale;
        var from = camera.ProjectRayOrigin(pixel);
        var to = from + camera.ProjectRayNormal(pixel) * SkyView.Far;
        return !SpherePhysics.Sweep(_world, from.ToNumerics(), to.ToNumerics(), 0.05f, false, out _, out _);
    }

    /// <summary>The player's shadow, placed after he and the camera move, as the original draws it.</summary>
    private void PlaceShadow()
    {
        if (_shadow is null)
            return;
        var sphere = _player!.Motion.Sphere;
        var alpha = BodyShadow.DrawAlpha(BodyShadow.TrekDistance, System.Numerics.Vector3.Distance(_follow!.Aim, sphere.Position), player: true);
        if (alpha is { } a)
            _shadow.Shadow.Place(_world, sphere.Position, sphere.Radius, a);
        _shadow.Sync();
        _shadow.Visible &= alpha is not null;
    }

    /// <summary>
    /// The controls as the original reads them: the stick walks under 80% tilt and runs above,
    /// and R with the stick orbits the camera. On a PC, Shift walks, Ctrl sneaks, and dragging
    /// with the right mouse button, the right stick, or Q and E orbit it.
    /// </summary>
    private PlayerInput ReadInput()
    {
        var move = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var stick = new System.Numerics.Vector2(move.X, -move.Y);
        if (Input.IsActionPressed("walk") && stick.Length() > 0.5f)
            stick = System.Numerics.Vector2.Normalize(stick) * 0.5f;

        var orbit = false;
        var orbitStick = System.Numerics.Vector2.Zero;
        var look = Input.GetVector("camera_left", "camera_right", "camera_down", "camera_up");
        if (look != Vector2.Zero)
        {
            orbit = true;
            orbitStick += new System.Numerics.Vector2(look.X, look.Y);
        }
        if (Input.IsActionPressed("orbit"))
        {
            orbit = true;
            orbitStick += stick;
        }
        var drag = _camera!.TakeDrag();
        if (_camera.Dragging)
        {
            orbit = true;
            orbitStick += new System.Numerics.Vector2(drag.X * DragToStick.X, drag.Y * DragToStick.Y);
        }
        return new PlayerInput(stick, Input.IsActionPressed("sneak"), orbit, orbitStick);
    }

    public override void _Process(double delta)
    {
        if (_chunks is null || _player is null || _camera is null)
            return;
        var focus = _camera.Flying ? _camera.Current.GlobalPosition : _player.GlobalPosition;
        _chunks.Update(focus);
        // From where the scenery is drawn this frame: between ticks, the camera interpolated.
        Billboards.Face(_chunks.BillboardFacings, _camera.Current.GetGlobalTransformInterpolated().Origin);

        if (_overlay.Visible)
        {
            var chunk = _chunks.ChunkAt(focus);
            var local = chunk is null ? focus : focus - _chunks.Origin(chunk);
            var motion = _player.Motion;
            var sphere = motion.Sphere;
            _overlay.Text = $"{_chunks.MapSet.Label} {chunk?.Name ?? "(no chunk)"}  " +
                $"({local.X:F1}, {local.Y:F1}, {local.Z:F1})  {Engine.GetFramesPerSecond()} fps" +
                (_camera.Flying ? "  [free camera]" : "") + "\n" +
                (sphere.Touching ? $"on {Surfaces.ElementAtOrDefault(sphere.Surface) ?? sphere.Surface.ToString()}" : "in the air") +
                $"  speed {Speed():F2}/s  request {motion.Request}  pace {motion.Pace:0.##} (F5)" +
                $"  {Clock(_sky!.Clock.TimeOfDay)}  sky {_sky.Placement.ToString().ToLowerInvariant()} (F6)";
        }
    }

    private static string Clock(float timeOfDay)
    {
        var minutes = (int)(timeOfDay * 24 * 60);
        return $"{minutes / 60:D2}:{minutes % 60:D2}";
    }

    /// <summary>How far the player went in X and Z over the last second, per second.</summary>
    private float Speed()
    {
        if (_track.Count < 2)
            return 0;
        var span = _track.Last() - _track.Peek();
        return new System.Numerics.Vector2(span.X, span.Z).Length() * Engine.PhysicsTicksPerSecond / (_track.Count - 1);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("toggle_fly") && _camera is not null)
        {
            _camera.ToggleFly();
        }
        else if (e.IsActionPressed("toggle_overlay"))
        {
            _overlay.Visible = !_overlay.Visible;
        }
        else if (e.IsActionPressed("toggle_pace") && _player is not null)
        {
            _player.Motion.Pace = _player.Motion.Pace == 1 ? BriskPace : 1;
        }
        else if (e.IsActionPressed("toggle_sky_placement") && _sky is not null)
        {
            _sky.Placement = _sky.Placement == Core.Sky.SkyPlacement.Original ? Core.Sky.SkyPlacement.Projected : Core.Sky.SkyPlacement.Original;
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
        // Time for the frame to settle.
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
        Add("camera_up", null, null, JoyAxis.RightY, -1);
        Add("camera_down", null, null, JoyAxis.RightY, 1);
        Add("walk", Key.Shift);
        Add("sneak", Key.Ctrl, null, JoyAxis.TriggerLeft, 1);
        Add("orbit", null, null, button: JoyButton.RightShoulder);
        Add("fly_up", Key.Space);
        Add("fly_down", Key.C);
        Add("toggle_fly", Key.F);
        Add("toggle_overlay", Key.F3);
        Add("toggle_pace", Key.F5);
        Add("toggle_sky_placement", Key.F6);

        static void Add(string action, Key? key, Key? alternative = null, JoyAxis? axis = null, float direction = 0, JoyButton? button = null)
        {
            if (InputMap.HasAction(action))
                return;
            InputMap.AddAction(action, 0.1f);
            foreach (var k in new[] { key, alternative }.OfType<Key>())
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
            if (axis is { } j)
                InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = j, AxisValue = direction });
            if (button is { } b)
                InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = b });
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

    /// <summary>For a player with no animations: he stands still.</summary>
    private sealed class Still : IRootMotion
    {
        public float Tick(int request) => 0;
    }
}
