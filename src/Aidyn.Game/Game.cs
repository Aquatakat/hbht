using System.Globalization;
using Aidyn.Core;
using Aidyn.Core.Audio;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
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
/// <c>--menu 1</c> opens the pause menu on the tab it last showed, <c>--menu system</c>, <c>party</c> or <c>world</c>
/// on that tab, and <c>--menu loot</c> an empty loot window; <c>--ui-lines &lt;n&gt;</c> sets the UI screen's height, and
/// <c>--press &lt;action&gt;,…</c> presses each action in turn, such as <c>pause,menu_member_left</c>.
/// <c>--cutscene &lt;id&gt;</c> plays a cut-scene at once, as what starts it would.
/// F toggles the free camera, F3 the debug overlay, F5 the pace, F6 the sky's placement and F7 the
/// UI screen's height, and F8 saves to <c>quick.json</c> in the saves folder.
/// </remarks>
public partial class Game : Node3D, ITrekHost
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
    private readonly Label _message = new() { Position = new Vector2(8, 64) };
    private readonly CollisionWorld _world = new();
    private GameState? _game;
    private PauseMenu? _menu;
    /// <summary>The stick in menus, shared by the pause menu and trek's popups, conversations and cut-scenes.</summary>
    private readonly MenuStick _stick = new();
    private Models? _models;
    /// <summary>Trek: the core's loop, which steps the player, the camera, the chunks and what's in them.</summary>
    private TrekLoop? _trek;
    /// <summary>Trek's chunks as they're drawn.</summary>
    private ChunkStreamerView? _chunks;
    private Player? _player;
    private CameraRig? _camera;
    private ShadowDecal? _shadow;
    /// <summary>The texture of bodies' shadows (<c>BORG1_PlayerShadow</c>).</summary>
    private Texture2D? _shadowTexture;
    private SkyView? _sky;
    private FollowCamera? _follow;
    /// <summary>The player's last second of positions, for the overlay's speed.</summary>
    private readonly Queue<System.Numerics.Vector3> _track = new();
    private long _ticks;
    private DrawDistance _drawDistance = DrawDistance.Original;
    private PluginSet? _plugins;
    private readonly UiScreen _trekScreen = new();
    private TrekPopupView? _popupView;
    /// <summary>Trek's HUD: the sundial, the portraits, and the compass or the minimap.</summary>
    private HudView? _hud;
    private readonly ColorRect _fade = new() { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
    /// <summary>Chunks whose scenery changed this tick, built again before the frame is drawn.</summary>
    private readonly HashSet<LoadedMap> _changed = [];
    private ConversationBoxView? _box;
    /// <summary>A backdrop's or the help menu's page, over the world darkened, with the game paused.</summary>
    private BackdropLayer? _backdrop;
    /// <summary>The player's settings, which the options menu changes and <see cref="ApplySettings"/> carries out.</summary>
    private readonly Settings _settings = Settings.Load(DataPaths.Settings);

    /// <summary>The plugins, kept loaded when the System tab loads a save and the scene starts again.</summary>
    private static PluginSet? _loadedPlugins;

    /// <summary>The sequences played so far, kept with the plugins.</summary>
    private static SoundBank? _bank;

    /// <summary>What plays: the music, map objects' sounds and one-shots, mixed by the core.</summary>
    private GameAudio? _audio;

    /// <summary>The save the System tab loaded, which the scene starts again from.</summary>
    private static SaveGame? _pendingSave;

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
        // A save loaded from the menu starts where it stands, and the command line's presses aren't made again.
        if (_pendingSave is not null)
            args = args.Where(a => a.Key == "screenshot").ToDictionary();
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
        var plugins = _plugins = _loadedPlugins ??= LoadPlugins();
        if (_bank is null)
        {
            _bank = new SoundBank(plugins);
            _bank.Missing += (id, why) => GD.PushWarning($"{id} can't play: {why}");
        }
        _audio = new GameAudio(_bank, _settings, AudioOutput.MixRate);
        AddChild(new AudioOutput(_audio));
        var game = _game = _pendingSave is { } pending ? GameState.Load(plugins, pending)
            : args.TryGetValue("save", out var savePath) ? GameState.Load(plugins, SaveFile.Read(savePath))
            : GameState.NewGame(plugins);
        _pendingSave = null;
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

        var environment = new Godot.Environment
        {
            // The sky's backdrop, a canvas layer behind the scene.
            BackgroundMode = Godot.Environment.BGMode.Canvas,
            BackgroundCanvasMaxLayer = SkyBackdropView.Layer,
        };
        // The scene is drawn in on-screen values, and blended there as the N64 blends.
        OnScreenOutput.Apply(environment);
        AddChild(new WorldEnvironment { Environment = environment });
        // Lit meshes are lit as the RSP lights them, by the sky and the sun, not by Godot's lights.
        N64Material.RspLighting = true;
        N64Material.OnScreenFramebuffer = true;

        _sky = new SkyView(plugins, game.Clock);
        AddChild(_sky);
        AddChild(SkyBackdropView.Create(_sky));
        AddChild(LensFlareView.Create(_sky));
        _sky.SkyShows = SkyShows;
        if (args.TryGetValue("sky", out var sky))
            _settings.SkyPlacement = sky == "original" ? Core.Sky.SkyPlacement.Original : Core.Sky.SkyPlacement.Projected;
        if (args.TryGetValue("time", out var time))
            game.Clock.SetTimeOfDay(float.Parse(time, CultureInfo.InvariantCulture));

        // The time's written into the flags, and any plain flags --flags sets, such as --flags 5,11
        // for after the Kendall and ambush cut-scenes.
        game.StateFlags.SetTime(game.Clock);
        foreach (var flag in args.GetValueOrDefault("flags", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            game.Flags.Set(int.Parse(flag, CultureInfo.InvariantCulture), true);

        // The command line's settings are for this run, and aren't kept unless the options menu changes something.
        if (args.ContainsKey("draw-distance"))
            _settings.DrawDistance = Float(args, "draw-distance", 1);
        if (args.TryGetValue("detail", out var detail))
            _settings.NearestDetail = detail == "nearest";
        var models = _models = new Models(plugins);
        // The texture engine code gives bodies' shadows (BORG1_PlayerShadow).
        if (plugins.FindAsset(RecordId.Hex(RecordTypes.Texture, 0x0003), "png") is { } shadowPng)
        {
            var image = new Image();
            image.LoadPngFromBuffer(File.ReadAllBytes(shadowPng));
            _shadowTexture = ImageTexture.CreateFromImage(image);
        }
        var streamer = new ChunkStreamer(plugins, game, _world, set);
        var trek = _trek = new TrekLoop(game, this, streamer, _audio);
        _chunks = new ChunkStreamerView(plugins, models, streamer, trek.Wanderers, _shadowTexture);
        AddChild(_chunks);

        var chunkName = args.GetValueOrDefault("chunk", place?.Chunk ?? "I08");
        var chunk = streamer.Find(chunkName) ?? throw new KeyNotFoundException($"{set.Label} has no chunk {chunkName}.");
        var at = Floats(args.GetValueOrDefault("at", "16,62"));
        // The original drops the player in from a height of 10 at a new game's spawn. This puts him
        // on the highest ground below 1000, so --at works anywhere. A save puts him where he stood.
        var spawn = _chunks.Origin(chunk) + (place is not null ? place.Position.ToGodot() : new Vector3(at[0], 10, at[1]));
        // The terrain's flags are written before the chunks' objects are gated by them.
        trek.Enter(chunk);
        streamer.Update(spawn.ToNumerics());
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
        var motion = new PlayerMotion(centre, look);
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
                trek.PlayerAnimation = animator;
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
        trek.Begin(motion, _follow);
        _camera = new CameraRig { Follow = _follow, Yaw = yaw, Pitch = Mathf.DegToRad(-pitch) };
        AddChild(_camera);
        _camera.Sync();

        // Trek's popups, over the world and under the pause menu, and the fade over them.
        var trekUi = new CanvasLayer { Name = "Trek UI", Layer = PauseMenu.MenuLayer - 1 };
        _trekScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _trekScreen.MouseFilter = Control.MouseFilterEnum.Ignore;
        trekUi.AddChild(_trekScreen);
        _hud = new HudView(plugins) { Trek = trek };
        _trekScreen.Canvas.AddChild(_hud);
        _popupView = new TrekPopupView(plugins);
        _trekScreen.Canvas.AddChild(_popupView);
        _box = new ConversationBoxView(plugins) { Visible = false };
        _trekScreen.Canvas.AddChild(_box);
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _fade.Visible = false;
        trekUi.AddChild(_fade);
        AddChild(trekUi);

        _menu = new PauseMenu(plugins, game, _settings, _stick) { SaveGame = SaveTo, LoadGame = LoadFrom, Where = Where, Audio = _audio };
        _menu.CampChosen += Camp;
        // Help: the menu has rolled up, and the help menu's script starts.
        _menu.HelpChosen += () => _trek!.ShowBackdrop(plugins.Get<DialogueRecord>(SystemMenu.HelpScript));
        // The original reloads the map as the menu closes, so what changed shows.
        _menu.Closed += () => _chunks!.Regate();
        if (args.TryGetValue("ui-lines", out var lines))
            _settings.UiLines = int.Parse(lines, CultureInfo.InvariantCulture);
        AddChild(_menu);
        _backdrop = new BackdropLayer(plugins);
        AddChild(_backdrop);
        ApplySettings();
        if (args.ContainsKey("pace"))
            _player.Motion.Pace = Float(args, "pace", 1);
        _settings.Changed += ApplySettings;
        _settings.Changed += () => _settings.Save(DataPaths.Settings);
        GetViewport().SizeChanged += ApplyWorldLines;
        switch (args.GetValueOrDefault("menu"))
        {
            case "1":
                _menu.Open();
                break;
            case "loot":
                _menu.OpenLoot(new ItemPile(game.Party.Inventory!.Catalog), 0);
                break;
            case { } tab when Enum.TryParse<PauseTab>(tab, ignoreCase: true, out var opening):
                _menu.Open(opening);
                break;
        }

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

    /// <summary>Carries out the settings: the window, the 3D view, the draw distance, the sky, the pace and the UI screens.</summary>
    private void ApplySettings()
    {
        var s = _settings;
        var mode = s.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if ((DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen) != s.Fullscreen)
            DisplayServer.WindowSetMode(mode);
        DisplayServer.WindowSetVsyncMode(s.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        GetViewport().Msaa3D = s.Antialiasing switch
        {
            >= 8 => Viewport.Msaa.Msaa8X,
            >= 4 => Viewport.Msaa.Msaa4X,
            >= 2 => Viewport.Msaa.Msaa2X,
            _ => Viewport.Msaa.Disabled,
        };
        ApplyWorldLines();
        _drawDistance = new DrawDistance(s.DrawDistance, s.NearestDetail);
        _sky?.Placement = s.SkyPlacement;
        _player?.Motion.Pace = s.BriskPace ? BriskPace : 1;
        foreach (var screen in new[] { _menu?.Screen, _backdrop?.Screen, _trekScreen })
        {
            if (screen is null)
                continue;
            screen.Lines = s.UiLines;
            screen.Filter = s.UiFilter;
        }
    }

    /// <summary>Draws the 3D world at the lines the settings ask for, scaled up to the window, or at the window's own.</summary>
    private void ApplyWorldLines()
    {
        var viewport = GetViewport();
        var height = viewport.GetVisibleRect().Size.Y;
        viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = _settings.WorldLines > 0 && height > 0 ? Math.Min(1, _settings.WorldLines / height) : 1;
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
    /// One 60 Hz tick: the time played, which counts in every mode, then trek (<see cref="TrekLoop.Tick"/>)
    /// or a cut-scene, and what draws them. The pause menu stops both.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (_player is null || _camera is null || _follow is null || _game is null || _trek is not { } trek)
            return;
        _game.PlayTime++;
        _audio!.Step();
        // The HUD is trek's: not in a cut-scene. Under the pause menu and a backdrop's page it's drawn as it stands,
        // as the world is, so that it lays itself out again as the window or the menu size changes.
        _hud!.Visible = _scene is null;
        _hud.QueueRedraw();
        // The menu's frame keeps the map's sounds going, with everything held still (PauseMenu.Frame), and a
        // backdrop's page holds the world still the same way.
        if (Paused)
        {
            if (_scene is null)
                trek.StepSounds();
            return;
        }
        if (_scene is not null)
        {
            StepCutScene();
            return;
        }
        _popupView!.Step();
        var before = _player.Motion.Sphere.Position;
        var step = trek.Tick(ReadInput, frozen: _camera.Flying);
        ShowFade(trek.Brightness);
        if (step == TrekStep.CutScene)
        {
            BeginCutScene();
            return;
        }
        _sky!.Step();
        ScrollTextures();
        _hud.Outdoors = Where().Outdoors;
        if (step == TrekStep.Frozen)
            return;
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
        var motion = _player.Motion;
        _track.Enqueue(motion.Sphere.Position);
        if (_track.Count > Aidyn.Core.Calendar.GameClock.TicksPerSecond)
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

    /// <summary>Shows the popup using map objects has up, if it changed.</summary>
    private void ShowPopup()
    {
        if (_popupView!.Popup != _trek!.Use.Popup)
            _popupView.Popup = _trek.Use.Popup;
    }

    /// <summary>Darkens the screen to a brightness, from 0 for black to 1.</summary>
    private void ShowFade(float brightness)
    {
        _fade.Visible = brightness < 1;
        _fade.Color = new Color(0, 0, 0, 1 - brightness);
    }

    // What trek asks of the runtime.

    void ITrekHost.Entered(ChunkEntry? chunk) => _sky!.Enter(chunk);

    /// <summary>The player has come through a teleporter: the sky, the camera and his body are where he is now.</summary>
    void ITrekHost.Arrived()
    {
        var motion = _player!.Motion;
        _sky!.Apply();
        _camera!.Yaw = MathF.Atan2(-motion.Heading.X, -motion.Heading.Y);
        _player.Sync();
        _camera.Sync();
        PlaceShadow();
        ResetInterpolation();
    }

    IRootMotion ITrekHost.Motion(Wanderer wanderer) => _chunks!.View(wanderer)?.Animation ?? Core.Scripts.IActorMotion.Still;

    void ITrekHost.Say(string text) => Say(text);

    void ITrekHost.Log(string text) => GD.Print(text);

    (int Width, int Height) ITrekHost.ImageSize(RecordId image) => _hud?.ImageSize(image) ?? (0, 0);

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
        var centre = _chunks!.Chunks.MapAt(_player!.Motion.Sphere.Position);
        var ownAim = centre is null ? aim : aim - centre.Origin;
        _chunks.SyncWanderers();
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
        // The right stick, Q and E, and the mouse turn the camera while the left stick and WASD still move him; only
        // R with the left stick, as on the N64, holds him still.
        var shoulder = Input.IsActionPressed("orbit");
        var look = Input.GetVector("camera_left", "camera_right", "camera_down", "camera_up");
        if (look != Vector2.Zero)
        {
            orbit = true;
            orbitStick += new System.Numerics.Vector2(look.X, look.Y);
        }
        if (shoulder)
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
        return new PlayerInput(stick, Input.IsActionPressed("sneak"), orbit, orbitStick, Free: !shoulder);
    }

    /// <summary>Debug: the tick rate while a thumbstick is held down, to cross the map quickly (3×).</summary>
    private const int FastTicksPerSecond = 180;

    /// <summary>
    /// Clicking in either thumbstick unlocks the tick rate to <see cref="FastTicksPerSecond"/>, as far as the machine
    /// keeps up. The core steps per tick whatever its length, so more ticks a second is the game running faster. The
    /// pause menu stays at the original's pace.
    /// </summary>
    private void SetTickRate()
    {
        var fast = Input.IsActionPressed("fast_forward") && _menu is not { IsOpen: true };
        var rate = fast ? FastTicksPerSecond : Aidyn.Core.Calendar.GameClock.TicksPerSecond;
        if (Engine.PhysicsTicksPerSecond != rate)
            Engine.PhysicsTicksPerSecond = rate;
    }

    /// <summary>Whether the pause menu or a backdrop's page holds the world still.</summary>
    private bool Paused => _menu is { IsOpen: true } || _backdrop is { Showing: true };

    public override void _Process(double delta)
    {
        SetTickRate();
        if (Streamer is not { } chunks || _player is null || _camera is null)
            return;
        // The world goes on being drawn behind the pause menu and a backdrop's page, still: its scenery's actors,
        // which animate with the frame, stop, and so does the free camera. The parent processes first, so this
        // holds from the frame the menu opens.
        var still = Paused ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        _chunks!.ProcessMode = still;
        _camera.ProcessMode = still;
        foreach (var map in _changed)
            chunks.Rebuild(map);
        _changed.Clear();
        var focus = _camera.Flying ? _camera.Current.GlobalPosition : _scene is not null ? _scene.Camera.Aim.ToGodot() : _player.GlobalPosition;
        // A cut-scene's 3×3 stays as it loaded.
        if (_scene is null)
            chunks.Chunks.Update(focus.ToNumerics());
        var view = View();
        chunks.Fade(view, _drawDistance);
        // Oriana's path lights go out as they're reached; not for the free camera, which the original hasn't.
        if (!_camera.Flying)
            chunks.PutOut(view);
        RepeatStick(delta);
        // From where the scenery is drawn this frame: between ticks, the camera interpolated.
        Billboards.Face(chunks.BillboardFacings, _camera.Current.GetGlobalTransformInterpolated().Origin);

        if (_overlay.Visible)
        {
            var chunk = chunks.Chunks.ChunkAt(focus.ToNumerics());
            var local = chunk is null ? focus : focus - chunks.Origin(chunk);
            var motion = _player.Motion;
            var sphere = motion.Sphere;
            _overlay.Text = (_scene is { } scene ? $"cut-scene {scene.Runner.Script.Id}, waiting for {scene.Runner.Wait?.ToString() ?? "nothing"}, {scene.Actors.Count} actors, {scene.Cameras.Count} camera moves\n" : "") +
                $"{chunks.Chunks.MapSet.Label} {chunk?.Name ?? "(no chunk)"}  " +
                $"({local.X:F1}, {local.Y:F1}, {local.Z:F1})  {Engine.GetFramesPerSecond()} fps" +
                (_camera.Flying ? "  [free camera]" : "") + "\n" +
                (sphere.Touching ? $"on {Surfaces.ElementAtOrDefault(sphere.Surface) ?? sphere.Surface.ToString()}" : "in the air") +
                $"  speed {Speed():F2}/s  request {motion.Request}  pace {motion.Pace:0.##} (F5)" +
                $"  camera {_follow!.Mode.ToString().ToLowerInvariant()}{(_trek!.Use.CurrentCamera is { } held ? $" (object {held.Index})" : "")}" +
                $"  {Clock(_sky!.Clock.TimeOfDay)}  sky {_sky.Placement.ToString().ToLowerInvariant()} (F6)" +
                $"  UI {_menu!.Screen.Lines} lines (F7)  wanderers {_trek.Wanderers.All.Count}\n" +
                $"music {_audio!.MusicPlaying?.Key ?? "none"}  sounds {_trek.Sounds.Playing} of the map's, {_audio.SoundsPlaying} one-shots";
        }
    }

    /// <summary>
    /// The stick held in trek's popups, conversations and cut-scenes moves their cursor on as the D-pad would. With
    /// none up, what was held is forgotten, so the next doesn't start by repeating. The pause menu steps its own.
    /// </summary>
    private void RepeatStick(double delta)
    {
        if (_menu is { IsOpen: true })
            return;
        if (_trek?.Use is { Popup: not null } || _trek?.Conversation is not null || _scene is not null || _backdrop is { Showing: true })
            _stick.Step(delta, direction => _UnhandledInput(new InputEventAction { Action = MenuStick.Dpad(direction), Pressed = true }));
        else
            _stick.Clear();
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
        return new System.Numerics.Vector2(span.X, span.Z).Length() * Aidyn.Core.Calendar.GameClock.TicksPerSecond / (_track.Count - 1);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        _stick.Track(e);
        if (_menu is { IsOpen: true } menu)
        {
            if (menu.Handle(e))
                return;
        }
        else if (_backdrop is { Showing: true })
        {
            Browse(e);
            return;
        }
        else if (_trek?.Use is { Popup: not null } use)
        {
            if (e.IsActionPressed("menu_accept"))
                use.Accept();
            else if (e.IsActionPressed("menu_back"))
                use.Back();
            else if (_stick.Pressed(e, "move_forward") || e.IsActionPressed("menu_up", allowEcho: true))
                use.MoveCursor(-1);
            else if (_stick.Pressed(e, "move_back") || e.IsActionPressed("menu_down", allowEcho: true))
                use.MoveCursor(1);
            else
                return;
            ShowPopup();
            _popupView!.QueueRedraw();
            return;
        }
        else if (_trek?.Conversation is not null)
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
        else if (_trek is { Cued: not null } or { CutSceneFade.Fading: true })
        {
            // Trek's input is dead while it fades into or out of a cut-scene.
            if (e.IsActionPressed("pause") || e.IsActionPressed("menu_accept"))
                return;
        }
        else if (e.IsActionPressed("pause") && _menu is not null && _trek is { Teleporting: false })
        {
            _menu.Open();
            return;
        }
        else if (e.IsActionPressed("menu_accept") && _camera is { Flying: false } && _trek is { TeleportFade.Fading: false } && UseNearest())
        {
            return;
        }
        else if (e.IsActionPressed("menu_back") && _camera is { Flying: false } && _trek is { } trek)
        {
            // B raises or lowers the party's portraits, and the minimap with them.
            trek.PressB();
            return;
        }

        if (e.IsActionPressed("cycle_ui_lines") && _menu is not null)
        {
            _settings.UiLines = _settings.UiLines switch { < 360 => 360, < 480 => 480, _ => 240 };
            _settings.Notify();
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
            _settings.BriskPace = !_settings.BriskPace;
            _settings.Notify();
        }
        else if (e.IsActionPressed("toggle_sky_placement") && _sky is not null)
        {
            _settings.SkyPlacement = _sky.Placement == Core.Sky.SkyPlacement.Original ? Core.Sky.SkyPlacement.Projected : Core.Sky.SkyPlacement.Original;
            _settings.Notify();
        }
    }

    /// <summary>The chunk the player is in, and whether its terrain has the outdoor sky, for the World tab.</summary>
    private (ChunkRef? Place, bool Outdoors) Where()
    {
        if (_trek is not { } trek || trek.Chunks.ChunkAt(trek.Player.Sphere.Position) is not { } chunk)
            return (null, false);
        var terrain = _plugins!.TryGet<TerrainRecord>(RecordId.Decimal(RecordTypes.Terrain, chunk.Terrain));
        return (new ChunkRef(trek.Chunks.MapSet.Id, chunk.Row, chunk.Column), terrain?.SkyDrawn ?? false);
    }

    /// <summary>The World tab's Camp: the party camps where the player stands (<see cref="TrekLoop.Camp"/>).</summary>
    private void Camp()
    {
        var outcome = _trek!.Camp();
        GD.Print($"Camping: {outcome}.");
    }

    /// <summary>A in trek: uses the map object the player reaches, and shows any popup it brings up.</summary>
    private bool UseNearest()
    {
        var used = _trek!.UseNearest();
        ShowPopup();
        return used;
    }

    /// <summary>The loot window: the pause menu in its barter state, which stops trek until it closes.</summary>
    void ITrekHost.OpenLoot(ItemPile loot, int gold) => _menu!.OpenLoot(loot, gold);

    // Conversations.

    /// <summary>
    /// The buttons in a conversation: A moves it on, bringing up the choices behind a line that showed
    /// alone or taking the cursor's choice, and up and down move the cursor. Start does nothing.
    /// </summary>
    private void Converse(InputEvent e)
    {
        var conversation = _trek!.Conversation!;
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
            _trek.Refresh();
        }
        else if (_stick.Pressed(e, "move_forward") || e.IsActionPressed("menu_up", allowEcho: true))
        {
            _box.Point(box.Highlight - 1);
        }
        else if (_stick.Pressed(e, "move_back") || e.IsActionPressed("menu_down", allowEcho: true))
        {
            _box.Point(box.Highlight + 1);
        }
    }

    /// <summary>
    /// The buttons on a backdrop's or the help menu's page: A takes the cursor's choice, or with none moves on, and
    /// up and down move the cursor. In the help menu, B takes the last choice, Exit Help or back, or with none moves on
    /// as A does: only a help script's page gets a B (<c>Dialogue.ShowNode</c>, 0x80047714, by its start mode, 4) and
    /// it returns the last row, else the text, which carries the node's link (<c>Dialogue.OnBHelp</c>, 0x80048354).
    /// </summary>
    private void Browse(InputEvent e)
    {
        if (!_backdrop!.Taking || _trek!.Conversation?.Runner is not { Status: Core.Scripts.ScriptStatus.Showing, Page: { } page } runner)
            return;
        var choosing = page.Choices.Count > 0 && runner.Wait != Core.Scripts.ScriptWait.Player;
        var back = e.IsActionPressed("menu_back") && runner.Script.Mode == DialogueMode.Help;
        if (e.IsActionPressed("menu_accept") || back)
        {
            if (choosing)
                runner.Choose(back ? page.Choices.Count - 1 : _backdrop.Page!.Highlight);
            else
                runner.Continue();
            _trek.Refresh();
        }
        else if (_stick.Pressed(e, "move_forward") || e.IsActionPressed("menu_up", allowEcho: true))
        {
            _backdrop.MoveCursor(-1);
        }
        else if (_stick.Pressed(e, "move_back") || e.IsActionPressed("menu_down", allowEcho: true))
        {
            _backdrop.MoveCursor(1);
        }
    }

    /// <summary>
    /// Shows the conversation's page in the box, or a backdrop's on its picture, or hides the box once there's none
    /// and lets a backdrop's page fade out.
    /// </summary>
    void ITrekHost.ConversationChanged()
    {
        if (_trek!.Conversation is { ShowsBackdrop: true } backdrop)
        {
            if (!_backdrop!.Showing)
            {
                _backdrop.PlayerName = _box!.PlayerName;
                _backdrop.Open(BackdropStyle.For(backdrop.Runner.Script.Backdrop));
            }
            if (backdrop.Runner is { Status: Core.Scripts.ScriptStatus.Showing, Page: { } shown })
                _backdrop.Show(shown);
            return;
        }
        _backdrop!.Close();
        if (_trek!.Conversation?.Runner is { Status: Core.Scripts.ScriptStatus.Showing, Page: { } page })
            _box!.Show(page);
        else
            _box!.Visible = false;
    }

    /// <summary>A conversation has ended: any fault, and the shop, training, battle or camp it ends in.</summary>
    void ITrekHost.ScriptEnded(RecordId script, Core.Scripts.ScriptEnding ending, RecordId? keeper)
    {
        if (ending.Fault is { } fault)
            GD.PushWarning($"{script}: {fault}");
        CarryOut(ending, keeper);
    }

    /// <summary>
    /// Passes the hours a camp heal or a camp scene takes, and opens the shop or training a script ends in, kept by
    /// <paramref name="keeper"/>, or says what the stand-in for a battle would do.
    /// </summary>
    private void CarryOut(Core.Scripts.ScriptEnding ending, RecordId? keeper)
    {
        if (ending.RestHours > 0)
        {
            _game!.Lapse(ending.RestHours);
            GD.Print($"{ending.RestHours} hours pass.");
        }
        if (ending.Kind == Core.Scripts.ScriptEndKind.Shop)
        {
            OpenShop(keeper);
            return;
        }
        if (ending.Kind == Core.Scripts.ScriptEndKind.Train)
        {
            OpenTraining(keeper);
            return;
        }
        var what = ending.Kind switch
        {
            Core.Scripts.ScriptEndKind.Battle => "A battle would start; battles come with combat (2.5).",
            _ => null,
        };
        if (what is not null)
            Say(what);
    }

    /// <summary>
    /// Opens the keeper's first shop (<c>InitShop</c>). The original crashes when the keeper runs no shop,
    /// which no script in the game does; the port says so and stays in trek.
    /// </summary>
    private void OpenShop(RecordId? keeper)
    {
        var entity = keeper is { } id ? _plugins!.TryGet<EntityRecord>(id) : null;
        if (entity is null || Core.Items.Shop.For(_plugins!.All<ShopRecord>(RecordTypes.Shop), entity.Id) is not { } record)
        {
            Say($"{entity?.Name ?? keeper?.ToString() ?? "No one"} keeps no shop.");
            return;
        }
        var party = _game!.Party;
        _menu!.OpenShop(Core.Items.Shop.Open(record, entity, party, party.Inventory!.Catalog, _game.Flags));
    }

    /// <summary>
    /// Opens training with the keeper as the trainer (<c>init_skill_trainer</c>). The original opens it with
    /// empty lists when there's no keeper; the port says so and stays in trek.
    /// </summary>
    private void OpenTraining(RecordId? keeper)
    {
        if (keeper is not { } id || _plugins!.TryGet<EntityRecord>(id) is not { } entity)
        {
            Say($"{keeper?.ToString() ?? "No one"} teaches nothing.");
            return;
        }
        _menu!.OpenTraining(Core.Characters.Trainer.From(entity, _game!.Rules, _plugins.Get<SpellRecord>));
    }

    /// <summary>A script's sounds. The cut-scene carries out the rest of its cues.</summary>
    void Core.Scripts.IScriptHost.Cue(Core.Scripts.ScriptCue cue)
    {
        if (cue is Core.Scripts.SoundCue sound && _game?.Sounds is { } sounds && ScriptSounds.For(sounds, sound) is { } play)
            _audio!.Play(play);
    }

    void ITrekHost.Animate(LoadedMap map, SceneObject actor, int request)
    {
        if (_chunks!.Animator(map, actor) is { } animator)
            animator.Request = request;
    }

    void ITrekHost.Changed(LoadedMap map, SceneObject scene) => _changed.Add(map);

    /// <summary>
    /// The System tab's Save: writes the game to a save file with where the player stands, and beside it the world as
    /// the menu opened over it, the centre half of the view as the original's is (<c>QuestData::TakeSnapshot</c>), at
    /// twice the size the screen shows it rather than the original's 64 × 32. Returns why it couldn't, or null.
    /// </summary>
    private string? SaveTo(string path, Image? snapshot)
    {
        if (_game is null || _player is null || _chunks is null)
            return "There's no game to save.";
        try
        {
            if (!NotePlace())
                return "The player isn't in a chunk.";
            _game.Label = null;
            SaveFile.Write(path, _game.Save());
            if (snapshot is not null)
                Screenshot(snapshot).SavePng(SaveSlot.ScreenshotPath(path));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning(e.ToString());
            return e.Message;
        }
    }

    /// <summary>The centre half of a view, cut to 4:3 and scaled to 208 × 156.</summary>
    private static Image Screenshot(Image view)
    {
        var height = view.GetHeight() / 2;
        var width = Math.Min(view.GetWidth() / 2, height * 4 / 3);
        var shot = view.GetRegion(new Rect2I((view.GetWidth() - width) / 2, (view.GetHeight() - height) / 2, width, height));
        shot.Resize(208, 156, Image.Interpolation.Lanczos);
        return shot;
    }

    /// <summary>
    /// The System tab's Load: starts the game again from a save, as the original's teleports there. The plugins stay
    /// loaded.
    /// </summary>
    private void LoadFrom(string path)
    {
        try
        {
            _pendingSave = SaveFile.Read(path);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            Say(e.Message);
            return;
        }
        GetTree().ReloadCurrentScene();
    }

    /// <summary>Notes where the player stands, for a save. False if he isn't in a chunk.</summary>
    private bool NotePlace()
    {
        var position = _player!.Motion.Sphere.Position.ToGodot();
        if (_chunks!.Chunks.ChunkAt(position.ToNumerics()) is not { } chunk)
            return false;
        _game!.Place = new SavePlace(_chunks.Chunks.MapSet.Id, chunk.Name, (position - _chunks.Origin(chunk)).ToNumerics(), _player.Motion.Heading);
        return true;
    }

    /// <summary>Saves the game to <c>quick.json</c> in the saves folder, with where the player stands.</summary>
    private void QuickSave()
    {
        if (!NotePlace())
        {
            Say("Can't save: the player isn't in a chunk.");
            return;
        }
        var path = Path.Combine(DataPaths.Saves, "quick" + SaveFile.Extension);
        SaveFile.Write(path, _game!.Save());
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
        Add("fast_forward", null, button: JoyButton.LeftStick);
        InputMap.ActionAddEvent("fast_forward", new InputEventJoypadButton { ButtonIndex = JoyButton.RightStick });
        Add("toggle_overlay", Key.F3);
        Add("toggle_pace", Key.F5);
        Add("toggle_sky_placement", Key.F6);
        Add("cycle_ui_lines", Key.F7);
        Add("quick_save", Key.F8);
        // The pause menu (PauseMenu).
        Add("pause", Key.Enter, Key.Escape, button: JoyButton.Start);
        Add("menu_back", Key.Backspace, button: JoyButton.B);
        Add("menu_accept", Key.Space, button: JoyButton.A);
        Add("menu_member_left", Key.Q, button: JoyButton.LeftShoulder);
        Add("menu_member_right", Key.E, button: JoyButton.RightShoulder);
        Add("menu_details", Key.Tab, button: JoyButton.Y);
        Add("menu_appraise", Key.R, button: JoyButton.X);
        // Z or L, and R, change the tab.
        Add("menu_tab_previous", Key.Z, null, JoyAxis.TriggerLeft, 1);
        Add("menu_tab_next", Key.X, null, JoyAxis.TriggerRight, 1);
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
}
