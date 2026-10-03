using System.Globalization;
using Aidyn.Core;
using Aidyn.Core.Cameras;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Scenes;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Plugins.Saves;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The runtime (roadmap M1, M2): loads the installed plugins and a game, a new one or a save, and
/// walks the player through a map set. It never reads the ROM; <c>aidyn import &lt;rom&gt;</c> writes
/// the base plugin it loads (ADR 0001).
/// </summary>
/// <remarks>
/// The player and camera are the core's port of the original, stepped once per 60 Hz tick in the
/// physics process; Godot interpolates between ticks for display. Start opens the pause menu, and A
/// uses the map object the player reaches (<see cref="MapObjectUse"/>).
/// Command-line options, after <c>--</c>: <c>--save &lt;path&gt;</c> starts from a save (see
/// docs/systems/saves.md), where it stands unless <c>--chunk</c> or <c>--at</c> say otherwise;
/// <c>--set &lt;n&gt;</c> (default 0, the overworld),
/// <c>--chunk &lt;name&gt;</c> and <c>--at &lt;x&gt;,&lt;z&gt;</c> in the chunk's coordinates
/// (default I08 at 16, 62, where a new game starts), <c>--yaw</c> in degrees for the way the
/// player and camera face (0 looks along −Z), <c>--pitch</c> (negative looks down, default −15)
/// and <c>--distance</c> for the camera, <c>--pace</c> for the player's speed (1 is the
/// original's), <c>--fly &lt;x&gt;,&lt;y&gt;,&lt;z&gt;</c> to start with the free camera there in
/// world coordinates, looking along <c>--yaw</c> and <c>--pitch</c>, <c>--time &lt;hours&gt;</c>
/// for the time of day (a new game starts at 12), <c>--camera outdoor|interior</c> for the camera's
/// mode (by default, what going through a door into the chunk gives), and
/// <c>--hold &lt;action&gt;,&lt;seconds&gt;</c>, which holds an input such as <c>move_forward</c>
/// (join several with <c>+</c>, as in <c>move_forward+walk</c>),
/// and <c>--screenshot &lt;png&gt;</c>, which saves the window after that and quits.
/// <c>--sky original</c> places the sky's sprites as the original does rather than projected.
/// <c>--menu 1</c> opens the pause menu, <c>--ui-lines &lt;n&gt;</c> sets the UI screen's height, and
/// <c>--press &lt;action&gt;,…</c> presses each action in turn, such as <c>pause,menu_member_next</c>.
/// <c>--cutscene &lt;id&gt;</c> plays a cut-scene at once, as what starts it would.
/// F toggles the free camera, F3 the debug overlay, F5 the pace, F6 the sky's placement and F7 the
/// UI screen's height, and F8 saves to <c>quick.json</c> in the saves folder.
/// </remarks>
public partial class Game : Node3D, IMapObjectHost, Core.Scripts.IScriptHost
{
    /// <summary>
    /// How a teleport fades (<c>set_teleport_pointer</c>, <c>handleZoneEngineFrame</c>, <c>appState_1</c>):
    /// out at 2/3 of full brightness a tick, black for 3 frames, then in at 1/15 a tick.
    /// </summary>
    private const float FadeOutPerTick = 2f / 3, FadeInPerTick = 1f / 15;

    private const int FadeHoldTicks = 3 * Core.Cadence.FrameTicks;

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
    private readonly Label _message = new() { Position = new Vector2(8, 64) };
    private readonly CollisionWorld _world = new();
    private GameState? _game;
    private PauseMenu? _menu;
    private Models? _models;
    private ChunkStreamer? _chunks;
    private Player? _player;
    private CameraRig? _camera;
    private ShadowDecal? _shadow;
    /// <summary>The texture of bodies' shadows (<c>BORG1_PlayerShadow</c>).</summary>
    private Texture2D? _shadowTexture;
    private Wanderers? _wanderers;
    private SkyView? _sky;
    private ChunkEntry? _chunk;
    private FollowCamera? _follow;
    private IRootMotion _animation = Still.Instance;
    /// <summary>The player's last second of positions, for the overlay's speed.</summary>
    private readonly Queue<System.Numerics.Vector3> _track = new();
    private long _ticks;
    private DrawDistance _drawDistance = DrawDistance.Original;
    private PluginSet? _plugins;
    private MapObjectUse? _use;
    private readonly UiScreen _trekScreen = new();
    private TrekPopupView? _popupView;
    private readonly ColorRect _fade = new() { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
    /// <summary>The screen's brightness, which a teleport fades out and in.</summary>
    private float _brightness = 1;
    private bool _fadingIn;
    private int _blackTicks;
    /// <summary>The teleporter the player is going through once the screen is black.</summary>
    private TeleporterObject? _pending;
    /// <summary>The chunk the player was last in, to age what map objects remember as he crosses into another.</summary>
    private (int Row, int Column)? _centre;
    /// <summary>Chunks whose scenery changed this tick, built again before the frame is drawn.</summary>
    private readonly HashSet<LoadedMap> _changed = [];
    /// <summary>The last script a dialogue object asked to start that the runtime can't run yet, such as a backdrop.</summary>
    private RecordId? _lastScript;
    /// <summary>Trek's map set, kept while a cut-scene has the world.</summary>
    private MapSetRecord? _trekSet;
    /// <summary>The conversation or sign showing, if any.</summary>
    private Conversation? _conversation;
    private ConversationBoxView? _box;
    /// <summary>The player's nearest body and how far, from the last tick's push loop, which A can reach.</summary>
    private (Wanderer? Nearest, float Distance) _nearest;

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
        catch (Exception e) when (e is IOException or InvalidDataException or KeyNotFoundException or System.Text.Json.JsonException
            or ArgumentException)
        {
            GD.PrintErr(e.ToString());
            _message.Text = e.Message;
            if (args.ContainsKey("screenshot"))
                GetTree().Quit(1);
        }
    }

    private void Start(Dictionary<string, string> args)
    {
        var plugins = _plugins = LoadPlugins();
        var game = _game = args.TryGetValue("save", out var savePath)
            ? GameState.Load(plugins, SaveFile.Read(savePath))
            : GameState.NewGame(plugins);
        foreach (var problem in game.Problems)
            GD.PushWarning(problem);
        // Where the save stands, unless the command line says otherwise.
        var place = args.ContainsKey("chunk") || args.ContainsKey("at") ? null : game.Place;
        var setId = args.TryGetValue("set", out var setArg)
            ? RecordId.Decimal(RecordTypes.MapSet, int.Parse(setArg, CultureInfo.InvariantCulture))
            : place?.MapSet ?? RecordId.Decimal(RecordTypes.MapSet, 0);
        if (place?.MapSet != setId)
            place = null;
        var set = plugins.Get<MapSetRecord>(setId);

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

        _sky = new SkyView(plugins, game.Clock);
        AddChild(_sky);
        AddChild(SkyBackdropView.Create(_sky));
        AddChild(LensFlareView.Create(_sky));
        _sky.SkyShows = SkyShows;
        _sky.Placement = args.GetValueOrDefault("sky") == "original" ? Core.Sky.SkyPlacement.Original : Core.Sky.SkyPlacement.Projected;
        if (args.TryGetValue("time", out var time))
            game.Clock.SetTimeOfDay(float.Parse(time, CultureInfo.InvariantCulture));

        // The time's written into the flags, and any plain flags --flags sets, such as --flags 5,11
        // for after the Kendall and ambush cut-scenes.
        game.StateFlags.SetTime(game.Clock);
        foreach (var flag in args.GetValueOrDefault("flags", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            game.Flags.Set(int.Parse(flag, CultureInfo.InvariantCulture), true);

        _drawDistance = new DrawDistance(Float(args, "draw-distance", 1), args.GetValueOrDefault("detail") == "nearest");
        var models = _models = new Models(plugins);
        // The texture engine code gives bodies' shadows (BORG1_PlayerShadow).
        if (plugins.FindAsset(RecordId.Hex(RecordTypes.Texture, 0x0003), "png") is { } shadowPng)
        {
            var image = new Image();
            image.LoadPngFromBuffer(File.ReadAllBytes(shadowPng));
            _shadowTexture = ImageTexture.CreateFromImage(image);
        }
        _wanderers = new Wanderers(plugins);
        _chunks = new ChunkStreamer(plugins, models, set, _world, game, _wanderers, _shadowTexture);
        AddChild(_chunks);

        var chunkName = args.GetValueOrDefault("chunk", place?.Chunk ?? "I08");
        var chunk = _chunks.Find(chunkName) ?? throw new KeyNotFoundException($"{set.Label} has no chunk {chunkName}.");
        var at = Floats(args.GetValueOrDefault("at", "16,62"));
        // The original drops the player in from a height of 10 at a new game's spawn. This puts him
        // on the highest ground below 1000, so --at works anywhere. A save puts him where he stood.
        var spawn = _chunks.Origin(chunk) + (place is not null ? place.Position.ToGodot() : new Vector3(at[0], 10, at[1]));
        Enter(chunk);
        _chunks.Update(spawn);
        _sky.Apply();
        var centre = spawn.ToNumerics();
        var above = centre with { Y = 1000 };
        if (place is null && SpherePhysics.Sweep(_world, above, above - new System.Numerics.Vector3(0, 2000, 0), 0.5f, false, out var ground, out var normal))
            centre = ground + normal * 0.5f;

        // The way he faces: --yaw, or the save's, or along -Z. The look is (-sin yaw, -cos yaw).
        var yaw = Mathf.DegToRad(Float(args, "yaw", 0));
        if (!args.ContainsKey("yaw") && place is { Heading: var heading } && heading != default)
            yaw = MathF.Atan2(-heading.X, -heading.Y);
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

        if (_shadowTexture is not null)
        {
            _shadow = new ShadowDecal(_shadowTexture);
            AddChild(_shadow);
        }

        _follow = new FollowCamera(centre, look);
        var pitch = -Float(args, "pitch", -15);
        _follow.Pitch = _follow.DefaultPitch = pitch;
        if (args.TryGetValue("distance", out var distance))
            _follow.Distance = _follow.DefaultDistance = float.Parse(distance, CultureInfo.InvariantCulture);
        // A save loaded from the game's menu arrives as through a teleporter, which chooses the mode.
        var mode = args.TryGetValue("camera", out var modeArg)
            ? Enum.Parse<CameraMode>(modeArg, ignoreCase: true)
            : FollowCamera.ModeFor(plugins, chunk);
        if (mode == CameraMode.Interior)
            _follow.Arrive(centre, centre, look, mode, null);
        _camera = new CameraRig { Follow = _follow, Yaw = yaw, Pitch = Mathf.DegToRad(-pitch) };
        AddChild(_camera);
        _camera.Sync();

        // Trek's popups, over the world and under the pause menu, and the fade over them.
        var trek = new CanvasLayer { Name = "Trek UI", Layer = PauseMenu.MenuLayer - 1 };
        _trekScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _trekScreen.MouseFilter = Control.MouseFilterEnum.Ignore;
        trek.AddChild(_trekScreen);
        _popupView = new TrekPopupView(plugins);
        _trekScreen.Canvas.AddChild(_popupView);
        _box = new ConversationBoxView(plugins) { Visible = false };
        _trekScreen.Canvas.AddChild(_box);
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _fade.Visible = false;
        trek.AddChild(_fade);
        AddChild(trek);
        _use = new MapObjectUse(game, MenuText.From(plugins), this) { World = _world, Camera = _follow, PlayerRadius = motion.Sphere.Radius };

        _menu = new PauseMenu(plugins, game);
        // The original reloads the map as the menu closes, so what changed shows.
        _menu.Closed += () => _chunks!.Regate();
        if (args.TryGetValue("ui-lines", out var lines))
            _menu.Screen.Lines = int.Parse(lines, CultureInfo.InvariantCulture);
        AddChild(_menu);
        if (args.GetValueOrDefault("menu") == "1")
            _menu.Open();

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
        // Each action pressed and let go in turn, a fifth of a second apart, as input events.
        if (args.TryGetValue("press", out var presses))
        {
            var pressed = presses.Split(',');
            for (var i = 0; i < pressed.Length; i++)
            {
                var name = pressed[i];
                GetTree().CreateTimer(0.5 + 0.2 * i).Timeout += () =>
                {
                    Input.ParseInputEvent(new InputEventAction { Action = name, Pressed = true });
                    Input.ParseInputEvent(new InputEventAction { Action = name, Pressed = false });
                };
            }
            hold = Math.Max(hold, 0.5 + 0.2 * pressed.Length);
        }
        if (args.TryGetValue("cutscene", out var cutscene))
            CueFromCommandLine(RecordId.Parse(cutscene));
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

    /// <summary>
    /// The chunk the player is in: its sky, and its terrain written into the flags. Crossing into
    /// another chunk is a teleport in the original (<c>ConfirmPlayerWithinZone</c>), which ages what map
    /// objects remember.
    /// </summary>
    private void Enter(ChunkEntry? chunk)
    {
        _sky!.Enter(chunk);
        if (chunk is not null && _centre != (chunk.Row, chunk.Column))
        {
            if (_centre is not null)
                _game!.Memory.Age();
            _centre = (chunk.Row, chunk.Column);
        }
        if (chunk is null || chunk == _chunk)
            return;
        _chunk = chunk;
        _game!.StateFlags.SetTerrain(chunk.Terrain);
    }

    /// <summary>
    /// One 60 Hz tick: the time played, which counts in every mode, and in trek the clock, the map
    /// objects, the camera, the player, then a trigger triangle's camera, as the original orders them. The pause menu stops trek. A popup
    /// stops only the clock (<c>TickGameTime</c>, skipped while <c>isPaused</c>), which the user saw
    /// in the original: the sun stops, while bodies, animations and clouds go on, and the player's
    /// input goes to the popup. A teleport's fade takes the player's input too.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (_player is null || _camera is null || _follow is null || _game is null)
            return;
        _game.PlayTime++;
        if (_menu!.IsOpen)
            return;
        if (_scene is not null)
        {
            StepCutScene();
            return;
        }
        _popupView!.Step();
        StepFade();
        if (_cueing is not null && !_trekFade.Fading)
        {
            BeginCutScene();
            return;
        }
        var fading = _pending is not null || _fadingIn || _trekFade.Fading;
        Enter(_chunks!.ChunkAt(_player.Motion.Sphere.Position.ToGodot()));
        // The clock stops under a popup or a conversation.
        var talking = _conversation is not null;
        if (_use!.Popup is null && !talking)
            _game.PassTime(1);
        _sky!.Step();
        ScrollTextures();
        if (_camera.Flying)
            return;
        var motion = _player.Motion;
        // ProcessVoxelObjects comes before the player moves. Through a teleport's fade, only camera
        // objects work, so the camera is where the room's puts it as the screen comes up, and in a
        // conversation too, so one holding the camera keeps it rather than the conversation's.
        var playing = !fading && _pending is null;
        if (_chunks.MapAt(motion.Sphere.Position.ToGodot()) is { } centre)
        {
            var at = Local(centre, motion.Sphere.Position);
            if (playing && !talking)
                _use!.Touch(centre, at);
            else
                _use!.TouchCameras(centre, at);
        }
        var input = fading || _use!.Popup is not null || talking ? new PlayerInput(System.Numerics.Vector2.Zero) : ReadInput();
        var before = motion.Sphere.Position;
        if (_conversation is { With: not null } framing)
            framing.Frame(_follow, _world);
        else
            _follow.Step(_world, motion.Sphere.Position, motion.Heading);
        motion.Step(_world, input, _follow, _animation);
        // WanderTick, which a popup, a conversation, the menu and a fade stop.
        if (playing && _use!.Popup is null && !talking)
        {
            foreach (var w in _wanderers!.Tick(_world, motion.Sphere.Position, motion.Request, _game.Party, _game.Random.Wander))
                GD.Print($"{MapRecord.VoxelId(w.Map.Record.Id, w.Party.Index)} ambushes the party; battles come with combat (2.5).");
        }
        // ProcessPlayers: the player is the first body, then the rest move and push.
        _nearest = _wanderers!.Step(_world, motion.Sphere, before, _chunks.MapAt(motion.Sphere.Position.ToGodot())?.Key, _follow.Aim,
            w => _chunks.View(w)?.Animation ?? Still.Instance);
        if (_conversation is { } conversation && conversation.Tick())
            ShowConversation();
        foreach (var (part, target) in motion.Sphere.Trod)
        {
            if (_chunks.Map(part) is not { } map)
                continue;
            if (playing && !talking)
                _use!.Tread(map, target, Local(map, motion.Sphere.Position));
            else
                _use!.TreadCamera(map, target);
        }
        _use!.StepCameras(motion.Sphere.Position);
        ShowPopup();
        _player.Sync();
        // A camera object cutting jumps the view; it isn't blended from where it was. Godot resets interpolation
        // from where the camera is when it's called, so after it has moved.
        var cut = _follow.TakeCut();
        _camera.Sync();
        if (cut)
            _camera.ResetPhysicsInterpolation();
        PlaceShadow();
        DrawWanderers();
        _track.Enqueue(motion.Sphere.Position);
        if (_track.Count > Engine.PhysicsTicksPerSecond)
            _track.Dequeue();
        // A fall that sends him back to safe ground is a jump, not a move.
        if (System.Numerics.Vector3.Distance(before, motion.Sphere.Position) > 2)
            ResetInterpolation();
    }

    private void ResetInterpolation()
    {
        _player!.ResetPhysicsInterpolation();
        _camera!.ResetPhysicsInterpolation();
        _shadow?.ResetPhysicsInterpolation();
    }

    /// <summary>A point in the world in a loaded chunk's coordinates.</summary>
    private System.Numerics.Vector3 Local(LoadedMap map, System.Numerics.Vector3 world) => world - _chunks!.Origin(map).ToNumerics();

    /// <summary>Shows the popup using map objects has up, if it changed.</summary>
    private void ShowPopup()
    {
        if (_popupView!.Popup != _use!.Popup)
            _popupView.Popup = _use.Popup;
    }

    /// <summary>
    /// Steps a teleport's fade: out, black for a few frames, then the player goes through and it fades
    /// in. The original fades out only if nothing else is fading, and teleports once it's done.
    /// </summary>
    private void StepFade()
    {
        if (_pending is { } teleporter)
        {
            if (_brightness > 0)
            {
                _brightness = Math.Max(0, _brightness - FadeOutPerTick);
            }
            else if (++_blackTicks >= FadeHoldTicks)
            {
                _blackTicks = 0;
                _pending = null;
                _fadingIn = true;
                GoThrough(teleporter);
            }
        }
        else if (_fadingIn)
        {
            _brightness = Math.Min(1, _brightness + FadeInPerTick);
            _fadingIn = _brightness < 1;
        }
        _trekFade.Step();
        var brightness = Math.Min(_brightness, _trekFade.Brightness);
        _fade.Visible = brightness < 1;
        _fade.Color = new Color(0, 0, 0, 1 - brightness);
    }

    /// <summary>
    /// Takes the player through a teleporter (<c>TeleportPlayer</c>): what map objects remember ages, the
    /// destination's chunks load, in another map set if need be, and he's set down at the arrival point
    /// facing its way. The camera goes with him, outdoors or in an interior by the chunk's terrain, to the
    /// teleporter's camera point if it has one (<see cref="FollowCamera.Arrive"/>).
    /// </summary>
    private void GoThrough(TeleporterObject teleporter)
    {
        if (TeleportArrival.For(_plugins!, teleporter) is not { } arrival)
        {
            Say($"{teleporter.Name} leads to a chunk no plugin has.");
            return;
        }
        _game!.Memory.Age();
        if (arrival.Set.Id != _chunks!.MapSet.Id)
        {
            _chunks.QueueFree();
            _world.Clear();
            _changed.Clear();
            _wanderers!.Clear();
            _chunks = new ChunkStreamer(_plugins!, _models!, arrival.Set, _world, _game, _wanderers, _shadowTexture);
            AddChild(_chunks);
        }
        var point = _chunks.Origin(arrival.Chunk) + arrival.Position.ToGodot();
        _chunks.Update(point);
        _use!.ClearCameras();
        var motion = _player!.Motion;
        var from = motion.Sphere.Position;
        motion.Sphere.Position = TeleportArrival.Settle(_world, point.ToNumerics(), motion.Sphere.Radius) ?? point.ToNumerics();
        motion.Sphere.Velocity = System.Numerics.Vector3.Zero;
        if (arrival.Heading is { } heading)
            motion.Heading = heading;
        _centre = (arrival.Chunk.Row, arrival.Chunk.Column);
        Enter(arrival.Chunk);
        _sky!.Apply();

        var camera = arrival.Camera is { } local ? _chunks.Origin(arrival.Chunk).ToNumerics() + local : (System.Numerics.Vector3?)null;
        _follow!.Arrive(from, motion.Sphere.Position, motion.Heading, FollowCamera.ModeFor(_plugins!, arrival.Chunk), camera);
        _camera!.Yaw = MathF.Atan2(-motion.Heading.X, -motion.Heading.Y);
        _player.Sync();
        _camera.Sync();
        PlaceShadow();
        ResetInterpolation();
    }

    /// <summary>
    /// Loads the map set's chunks and their wanderers again where the player stands, as trek starts again
    /// after a cut-scene, a backdrop or the help menu (<c>initGameTrek</c>): every map object is gated
    /// by the flags as they are now, and the wanderers start at their home nodes.
    /// </summary>
    private void RebuildTrek()
    {
        var set = _chunks?.MapSet ?? _trekSet!;
        FreeTrek();
        _chunks = new ChunkStreamer(_plugins!, _models!, set, _world, _game!, _wanderers!, _shadowTexture);
        AddChild(_chunks);
        _chunks.Update(_player!.Motion.Sphere.Position.ToGodot());
        PlaceShadow();
    }

    /// <summary>Frees trek's chunks, their collision and wanderers, and hides the player's body (<c>clear_HUD_elements</c>).</summary>
    private void FreeTrek()
    {
        _chunks?.QueueFree();
        _chunks = null;
        _world.Clear();
        _changed.Clear();
        _wanderers!.Clear();
        _nearest = default;
        _use!.ClearCameras();
        _player!.Visible = false;
        if (_shadow is not null)
            _shadow.Visible = false;
    }

    /// <summary>
    /// Once a frame at the console's average 20 fps, the scenes the original would draw load their
    /// own copies of their scrolling textures, which moves them on.
    /// </summary>
    private void ScrollTextures()
    {
        if (++_ticks % Core.Cadence.TextureFrameTicks != 0)
            return;
        var camera = _camera!.Current.GlobalTransform;
        foreach (var scene in Streamer!.DrawnScenes(camera.Origin, -camera.Basis.Z))
            scene.Draw();
    }

    /// <summary>
    /// Where scene objects are drawn from: the player, and the follow camera's place and aim, or
    /// the free camera alone. The cone of view widens with the window (<see cref="ViewCone.For"/>).
    /// </summary>
    private SceneView View()
    {
        var size = GetViewport().GetVisibleRect().Size;
        var cone = ViewCone.For(size.X / size.Y);
        if (_camera!.Flying)
        {
            var transform = _camera.Current.GlobalTransform;
            var look = -transform.Basis.Z;
            var flat = new System.Numerics.Vector2(look.X, look.Z);
            flat = flat.LengthSquared() < 1e-6f ? default : System.Numerics.Vector2.Normalize(flat);
            return new SceneView(transform.Origin.ToNumerics(), new(transform.Origin.X, transform.Origin.Z), flat, 0, cone);
        }
        // In a cut-scene there's no player: levels of detail go by the camera's aim.
        var follow = _scene?.Camera ?? _follow!;
        var camera = new System.Numerics.Vector2(follow.Position.X, follow.Position.Z);
        var toAim = new System.Numerics.Vector2(follow.Aim.X, follow.Aim.Z) - camera;
        var distance = toAim.Length();
        var from = _scene is not null ? follow.Aim : _player!.Motion.Sphere.Position;
        return new SceneView(from, camera, distance > 0 ? toAim / distance : default, distance, cone);
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

    /// <summary>Moves the wanderers' bodies to where they are, and fades them and their shadows by the camera's aim.</summary>
    private void DrawWanderers()
    {
        var aim = _follow!.Aim;
        var centre = _chunks!.MapAt(_player!.Motion.Sphere.Position.ToGodot());
        var ownAim = centre is null ? aim : aim - centre.Origin;
        foreach (var view in _chunks.Wanderers)
        {
            view.Sync();
            view.Draw(_world, aim, ownAim);
        }
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
        if (Streamer is not { } chunks || _player is null || _camera is null)
            return;
        foreach (var map in _changed)
            chunks.Rebuild(map);
        _changed.Clear();
        var focus = _camera.Flying ? _camera.Current.GlobalPosition : _scene is not null ? _scene.Camera.Aim.ToGodot() : _player.GlobalPosition;
        // A cut-scene's 3×3 stays as it loaded.
        if (_scene is null)
            chunks.Update(focus);
        chunks.Fade(View(), _drawDistance);
        // From where the scenery is drawn this frame: between ticks, the camera interpolated.
        Billboards.Face(chunks.BillboardFacings, _camera.Current.GetGlobalTransformInterpolated().Origin);

        if (_overlay.Visible)
        {
            var chunk = chunks.ChunkAt(focus);
            var local = chunk is null ? focus : focus - chunks.Origin(chunk);
            var motion = _player.Motion;
            var sphere = motion.Sphere;
            _overlay.Text = (_scene is { } scene ? $"cut-scene {scene.Runner.Script.Id}, waiting for {scene.Runner.Wait?.ToString() ?? "nothing"}, {scene.Actors.Count} actors, {scene.Cameras.Count} camera moves\n" : "") +
                $"{chunks.MapSet.Label} {chunk?.Name ?? "(no chunk)"}  " +
                $"({local.X:F1}, {local.Y:F1}, {local.Z:F1})  {Engine.GetFramesPerSecond()} fps" +
                (_camera.Flying ? "  [free camera]" : "") + "\n" +
                (sphere.Touching ? $"on {Surfaces.ElementAtOrDefault(sphere.Surface) ?? sphere.Surface.ToString()}" : "in the air") +
                $"  speed {Speed():F2}/s  request {motion.Request}  pace {motion.Pace:0.##} (F5)" +
                $"  camera {_follow!.Mode.ToString().ToLowerInvariant()}{(_use!.CurrentCamera is { } held ? $" (object {held.Index})" : "")}" +
                $"  {Clock(_sky!.Clock.TimeOfDay)}  sky {_sky.Placement.ToString().ToLowerInvariant()} (F6)" +
                $"  UI {_menu!.Screen.Lines} lines (F7)  wanderers {_wanderers!.All.Count}";
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
        if (_menu is { IsOpen: true } menu)
        {
            if ((e.IsActionPressed("pause") || e.IsActionPressed("menu_back")) && !menu.Back())
            {
                menu.Close();
                return;
            }
            if (e.IsActionPressed("pause") || e.IsActionPressed("menu_back") || menu.Handle(e))
                return;
        }
        else if (_use?.Popup is not null)
        {
            if (e.IsActionPressed("menu_accept"))
                _use.Accept();
            else if (e.IsActionPressed("menu_back"))
                _use.Back();
            else if (e.IsActionPressed("move_forward") || e.IsActionPressed("menu_up"))
                _use.MoveCursor(-1);
            else if (e.IsActionPressed("move_back") || e.IsActionPressed("menu_down"))
                _use.MoveCursor(1);
            else
                return;
            ShowPopup();
            _popupView!.QueueRedraw();
            return;
        }
        else if (_conversation is not null)
        {
            Converse(e);
            return;
        }
        else if (_scene is not null)
        {
            CutSceneInput(e);
            if (!e.IsActionPressed("toggle_fly") && !e.IsActionPressed("toggle_overlay"))
                return;
        }
        else if (_cueing is not null || _trekFade.Fading)
        {
            // Trek's input is dead while it fades into or out of a cut-scene.
            if (e.IsActionPressed("pause") || e.IsActionPressed("menu_accept"))
                return;
        }
        else if (e.IsActionPressed("pause") && _menu is not null && _pending is null)
        {
            _menu.Open();
            return;
        }
        else if (e.IsActionPressed("menu_accept") && _camera is { Flying: false } && _pending is null && !_fadingIn && UseNearest())
        {
            return;
        }

        if (e.IsActionPressed("cycle_ui_lines") && _menu is not null)
        {
            _menu.Screen.Lines = _menu.Screen.Lines switch { < 360 => 360, < 480 => 480, _ => 240 };
        }
        else if (e.IsActionPressed("quick_save") && _game is not null && _scene is null)
        {
            QuickSave();
        }
        else if (_menu?.IsOpen == true && !e.IsActionPressed("toggle_overlay"))
        {
            return;
        }
        else if (e.IsActionPressed("toggle_fly") && _camera is not null)
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

    /// <summary>A in trek: uses the map object the player reaches in the chunk he's in (<c>obj_ref_func</c>).</summary>
    private bool UseNearest()
    {
        var sphere = _player!.Motion.Sphere;
        if (_chunks!.MapAt(sphere.Position.ToGodot()) is not { } map)
            return false;
        (MonsterPartyObject, System.Numerics.Vector3, float)? body = null;
        if (_nearest is ({ } nearest, var distance) && nearest.Map == map && Wanderers.Reaches(nearest, distance, sphere))
            body = (nearest.Party, Local(map, nearest.Body.Sphere.Position), distance);
        var used = _use!.UseNearest(map, Local(map, sphere.Position), _player.Motion.Heading, sphere.Radius, body);
        ShowPopup();
        return used;
    }

    // What using map objects asks of the runtime.

    void IMapObjectHost.Teleport(LoadedMap from, TeleporterObject teleporter)
    {
        // set_teleport_pointer: the fade starts unless one is under way, and the teleport waits for it.
        if (_pending is not null || _fadingIn)
            return;
        _pending = teleporter;
        _blackTicks = 0;
    }

    /// <summary>The loot window: the pause menu in its barter state, which stops trek until it closes.</summary>
    void IMapObjectHost.OpenLoot(ItemPile loot, int gold) => _menu!.OpenLoot(loot, gold);

    void IMapObjectHost.StartScript(LoadedMap map, DialogueObject dialogue)
    {
        // One script at a time (dialoug_func).
        if (_conversation is not null)
            return;
        if (_plugins!.TryGet<DialogueRecord>(dialogue.Script) is { Mode: DialogueMode.Sign } sign)
        {
            _conversation = Conversation.Show(_game!, sign, new Core.Scripts.ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Record.Id, dialogue.Index)),
                this, _player!.Motion);
            ShowConversation();
            return;
        }
        var source = MapRecord.VoxelId(map.Record.Id, dialogue.Index);
        if (_plugins.TryGet<DialogueRecord>(dialogue.Script) is { Mode: DialogueMode.Cutscene } cutscene && dialogue.Stage is { } stage)
        {
            CueCutScene(cutscene, new Core.Scripts.ScriptStart(dialogue.RefPointBase, source), stage);
            return;
        }
        // Backdrops and the help menu come later. A tangible dialogue asks every tick, so say it once.
        if (_lastScript == dialogue.Script)
            return;
        _lastScript = dialogue.Script;
        Say($"{dialogue.Name} would start {dialogue.Script}.");
    }

    void IMapObjectHost.Talk(LoadedMap map, MonsterPartyObject party)
    {
        if (_conversation is not null || _wanderers!.Of(map, party) is not { } wanderer)
            return;
        if (party.Dialogue is null)
        {
            Say($"{MapRecord.VoxelId(map.Record.Id, party.Index)} would fight; battles come with combat (2.5).");
            return;
        }
        // A talker whose script is a cut-scene stages it in the chunk the player is in, from reference
        // point 0 (monsterpary_func), and holds still as trek fades out.
        if (_plugins!.TryGet<DialogueRecord>(party.Dialogue.Value) is { Mode: DialogueMode.Cutscene } cutscene)
        {
            if ((party.Flags & PartyFlags.Silent) != 0 || _chunks!.ChunkAt(_player!.Motion.Sphere.Position.ToGodot()) is not { } chunk)
                return;
            wanderer.Calmed = true;
            wanderer.Body.Held = true;
            CueCutScene(cutscene, new Core.Scripts.ScriptStart(0, MapRecord.VoxelId(map.Record.Id, party.Index)),
                new ChunkRef(_chunks.MapSet.Id, chunk.Row, chunk.Column));
            if (party.Used is { } used)
                _game!.Flags.Set(used.Flag, true);
            return;
        }
        _conversation = Conversation.TalkTo(_game!, wanderer, this, _player!.Motion);
        ShowConversation();
    }

    // Conversations.

    /// <summary>
    /// The buttons in a conversation: A moves it on, bringing up the choices behind a line that showed
    /// alone or taking the cursor's choice, and up and down move the cursor. Start does nothing.
    /// </summary>
    private void Converse(InputEvent e)
    {
        var conversation = _conversation!;
        var runner = conversation.Runner;
        if (runner.Status != Core.Scripts.ScriptStatus.Showing || runner.Page is not { } page)
            return;
        var box = _box!.Box;
        if (e.IsActionPressed("menu_accept"))
        {
            if (box.ShowsLineBeforeChoices)
            {
                conversation.Press(_box.ShowChoices);
                return;
            }
            if (page.Choices.Count > 0 && runner.Wait != Core.Scripts.ScriptWait.Player)
                conversation.Press(() => runner.Choose(box.Highlight));
            else
                conversation.Press(runner.Continue);
            ShowConversation();
        }
        else if (e.IsActionPressed("move_forward") || e.IsActionPressed("menu_up"))
        {
            _box.Point(box.Highlight - 1);
        }
        else if (e.IsActionPressed("move_back") || e.IsActionPressed("menu_down"))
        {
            _box.Point(box.Highlight + 1);
        }
    }

    /// <summary>
    /// Shows what the conversation has come to: its page in the box, or its end. What a script waits
    /// for that the runtime can't do yet (a camera, an actor walking or animating) is taken as done.
    /// </summary>
    private void ShowConversation()
    {
        if (_conversation is not { } conversation)
            return;
        var runner = conversation.Runner;
        while (runner.Status == Core.Scripts.ScriptStatus.Waiting && runner.Wait != Core.Scripts.ScriptWait.Timer)
        {
            GD.Print($"{runner.Script.Id} waits for {runner.Wait}, which a conversation can't show; going on.");
            runner.Resume();
        }
        if (runner.Status == Core.Scripts.ScriptStatus.Showing && runner.Page is { } page)
            _box!.Show(page);
        else
            _box!.Visible = false;
        if (conversation.Ended)
            EndConversation();
    }

    /// <summary>Ends the conversation, saying what the stand-ins for a shop, training, battle or camp would do.</summary>
    private void EndConversation()
    {
        var conversation = _conversation!;
        var ending = conversation.Runner.Ending!;
        conversation.End(_wanderers!, _follow!);
        _conversation = null;
        _box!.Visible = false;
        if (conversation.RebuildsTrek)
            RebuildTrek();
        if (ending.Fault is { } fault)
            GD.PushWarning($"{conversation.Runner.Script.Id}: {fault}");
        SayEnding(ending);
    }

    /// <summary>Says what the stand-ins for a shop, training, battle or camp would do after a script.</summary>
    private void SayEnding(Core.Scripts.ScriptEnding ending)
    {
        var what = ending.Kind switch
        {
            Core.Scripts.ScriptEndKind.Shop => "The shop would open (session 7).",
            Core.Scripts.ScriptEndKind.Train => "Training would open (session 7).",
            Core.Scripts.ScriptEndKind.Battle => "A battle would start; battles come with combat (2.5).",
            Core.Scripts.ScriptEndKind.Camp => $"The party is healed; {ending.RestHours} hours would pass (camping, 2.7).",
            _ => null,
        };
        if (what is not null)
            Say(what);
    }

    /// <summary>A script's sounds, which wait for audio (1D). The cut-scene carries out the rest of its cues.</summary>
    void Core.Scripts.IScriptHost.Cue(Core.Scripts.ScriptCue cue)
    {
        if (cue is Core.Scripts.SoundCue sound)
            GD.Print($"Sound: {sound}");
    }

    void IMapObjectHost.Animate(LoadedMap map, SceneObject actor, int request)
    {
        if (_chunks!.Animator(map, actor) is { } animator)
            animator.Request = request;
    }

    void IMapObjectHost.Changed(LoadedMap map, SceneObject scene) => _changed.Add(map);

    void IMapObjectHost.Sound(string cue) => GD.Print($"Sound: {cue}");

    /// <summary>Saves the game to <c>quick.json</c> in the saves folder, with where the player stands.</summary>
    private void QuickSave()
    {
        var position = _player!.Motion.Sphere.Position.ToGodot();
        if (_chunks!.ChunkAt(position) is not { } chunk)
        {
            Say("Can't save: the player isn't in a chunk.");
            return;
        }
        _game!.Place = new SavePlace(_chunks.MapSet.Id, chunk.Name, (position - _chunks.Origin(chunk)).ToNumerics(), _player.Motion.Heading);
        var path = Path.Combine(DataPaths.Saves, "quick" + SaveFile.Extension);
        SaveFile.Write(path, _game.Save());
        Say($"Saved to {path}");
    }

    /// <summary>Shows a message for a few seconds.</summary>
    private void Say(string text)
    {
        _message.Text = text;
        GetTree().CreateTimer(4).Timeout += () =>
        {
            if (_message.Text == text)
                _message.Text = "";
        };
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
        Add("cycle_ui_lines", Key.F7);
        Add("quick_save", Key.F8);
        // The pause menu (PauseMenu).
        Add("pause", Key.Enter, Key.Escape, button: JoyButton.Start);
        Add("menu_back", Key.Backspace, button: JoyButton.B);
        Add("menu_accept", Key.Space, button: JoyButton.A);
        Add("menu_member_previous", Key.Q, button: JoyButton.LeftShoulder);
        Add("menu_member_next", Key.E, button: JoyButton.RightShoulder);
        Add("menu_details", Key.Tab, button: JoyButton.Y);
        Add("menu_up", null, button: JoyButton.DpadUp);
        Add("menu_down", null, button: JoyButton.DpadDown);
        Add("menu_left", null, button: JoyButton.DpadLeft);
        Add("menu_right", null, button: JoyButton.DpadRight);

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

    /// <summary>For a body with no animations: it stands still.</summary>
    private sealed class Still : IRootMotion
    {
        public static readonly Still Instance = new();

        public float Tick(int request) => 0;
    }
}
