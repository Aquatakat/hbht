using System.Globalization;
using Aidyn.Core;
using Aidyn.Core.Audio;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Particles;
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
/// The command line's options are <see cref="LaunchOptions"/>, and what they and the debug keys drive is in Game.Debug.cs.
/// F toggles the free camera, F3 the debug overlay, F5 the pace, F6 the sky's placement and F7 the
/// UI screen's height, and F8 saves to <c>quick.json</c> in the saves folder. In a battle, F9 moves the radar between
/// the log box and the screen's corner.
/// </remarks>
public partial class Game : Node3D, ITrekHost
{
    /// <summary>A faster pace than the original's, which feels slow on a PC (a setting, ADR 0018).</summary>
    private const float BriskPace = 1.3f;

    /// <summary>Mouse drag in pixels to stick units: about 0.3° of orbit, and 0.02 of height, a pixel.</summary>
    private static readonly Vector2 DragToStick = new(0.2f, 0.4f);

    private readonly Label _message = new() { Position = new Vector2(8, 64) };
    private readonly CollisionWorld _world = new();

    // The solid surfaces the loaded chunks draw, which rain and snow land on, trek's and a cut-scene's stage's.
    private readonly VisibleWorld _visible = new();
    private GameState? _game;
    private PauseMenu? _menu;
    /// <summary>The stick in menus, shared by the pause menu and trek's popups, conversations and cut-scenes.</summary>
    private readonly MenuStick _stick = new();
    private Models? _models;
    private ParticlesView? _particles;
    /// <summary>The game being played: what runs, trek or a cut-scene and what's over them, and where each leads (<see cref="GameSession"/>).</summary>
    private GameSession? _session;

    /// <summary>Trek: the core's loop, which steps the player, the camera, the chunks and what's in them.</summary>
    private TrekLoop? Trek => _session?.Trek;
    /// <summary>Trek's chunks as they're drawn.</summary>
    private ChunkStreamerView? _chunks;
    private Player? _player;
    private CameraRig? _camera;
    private ShadowDecal? _shadow;
    /// <summary>The texture of bodies' shadows (<c>BORG1_PlayerShadow</c>).</summary>
    private Texture2D? _shadowTexture;
    private SkyView? _sky;
    private FollowCamera? _follow;
    private readonly FrameGate _textureFrame = new(Core.Cadence.TextureFrameTicks);
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

    /// <summary>The save the game last came from or went to, which the game over loads (<see cref="GameOver"/>).</summary>
    private static string? _lastSave;

    public override void _Ready()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        AddInputs();
        var hud = new CanvasLayer();
        StyleOverlay();
        hud.AddChild(_overlay);
        hud.AddChild(_message);
        AddChild(hud);

        var options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        // A save loaded from the menu starts where it stands, and the command line's presses aren't made again.
        if (_pendingSave is not null)
            options = options.OnlyScreenshot();
        try
        {
            Start(options);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or KeyNotFoundException or System.Text.Json.JsonException
            or ArgumentException)
        {
            GD.PrintErr(e.ToString());
            _message.Text = e.Message;
            if (options.Screenshot is not null)
                GetTree().Quit(1);
        }
    }

    /// <summary>
    /// Builds the game from the command line's options, in the order its parts need each other: the plugins and audio,
    /// the game, the sky, the world trek plays in, the player and camera, the UI over them, and the menus.
    /// </summary>
    private void Start(LaunchOptions options)
    {
        var plugins = _plugins = _loadedPlugins ??= LoadPlugins();
        StartAudio(plugins);
        // Where a new game starts, which plugins from an older import don't have: then this says to import again.
        var newGame = GameSession.NewGame(plugins);
        var game = _game = StartingGame(plugins, options);
        var (set, place) = StartingPlace(plugins, game, newGame, options);

        BuildSky(plugins, game);
        _pointLights = options.PointLights;
        _spellFx = options.SpellFx;
        options.SetTimeOfDay(game);
        // The time's written into the flags, and any plain flags --flags sets, such as --flags 5,11
        // for after the Kendall and ambush cut-scenes.
        game.StateFlags.SetTime(game.Clock);
        options.SetFlags(game);
        // The command line's settings are for this run, and aren't kept unless the options menu changes something.
        options.ApplyTo(_settings);

        var streamer = BuildWorld(plugins, game, set);
        var trek = Trek!;
        var arrival = GameSession.Arrival(plugins, newGame);
        var chunkName = options.Chunk ?? place?.Chunk ?? arrival.Chunk;
        var chunk = streamer.Find(chunkName) ?? throw new KeyNotFoundException($"{set.Label} has no chunk {chunkName}.");
        var at = options.At is { } a ? new Vector3(a[0], arrival.At.Y, a[1]) : arrival.At.ToGodot();
        // The original makes the player's body at a new game's height, 10, and sets it down from on high. This puts him
        // on the highest ground below 1000, so --at works anywhere. A save puts him where he stood.
        var spawn = _chunks!.Origin(chunk) + (place is not null ? place.Position.ToGodot() : at);
        // The terrain's flags are written before the chunks' objects are gated by them.
        trek.Enter(chunk);
        // After the first terrain and the time, which can change the weather.
        if (options.Weather is { } weather)
        {
            LaunchOptions.ForceWeather(game, weather);
            // Its sky too, at once.
            _sky!.ChooseAgain();
        }
        streamer.Update(spawn.ToNumerics());
        _sky!.Apply();

        var (centre, look, yaw) = Stand(spawn, place, options);
        var motion = new PlayerMotion(centre, look);
        BuildPlayer(plugins, motion);
        BuildFollowCamera(plugins, chunk, centre, look, yaw, motion, options);
        BuildTrekUi(plugins, trek);
        BuildMenus(plugins, game);
        ApplySettings();
        if (options.Pace is { } pace)
            _player!.Motion.Pace = pace;
        _settings.Changed += ApplySettings;
        _settings.Changed += () => _settings.Save(DataPaths.Settings);
        GetViewport().SizeChanged += ApplyWorldLines;
        RunLaunchActions(options);
    }

    /// <summary>The sound bank, kept with the plugins, and the audio mixed from it and played.</summary>
    private void StartAudio(PluginSet plugins)
    {
        if (_bank is null)
        {
            _bank = new SoundBank(plugins);
            _bank.Missing += (id, why) => GD.PushWarning($"{id} can't play: {why}");
        }
        _audio = new GameAudio(_bank, _settings, AudioOutput.MixRate);
        AddChild(new AudioOutput(_audio));
    }

    /// <summary>The game the System tab's Load asked for, else the save or the new game the options ask for.</summary>
    private GameState StartingGame(PluginSet plugins, LaunchOptions options)
    {
        if (_pendingSave is null && options.Save is { } started && _lastSave is null)
            _lastSave = started;
        var game = _pendingSave is { } pending ? GameState.Load(plugins, pending)
            : options.Save is { } savePath && !_newGame ? GameState.Load(plugins, SaveFile.Read(savePath))
            : GameState.NewGame(plugins);
        _newGame = false;
        _pendingSave = null;
        foreach (var problem in game.Problems)
            GD.PushWarning(problem);
        return game;
    }

    /// <summary>The map set to start in, and where the save stands in it, unless the options say otherwise: a new game's by default.</summary>
    private static (MapSetRecord Set, SavePlace? Place) StartingPlace(PluginSet plugins, GameState game, NewGameRecord newGame, LaunchOptions options)
    {
        var place = options.PlacesPlayer ? null : game.Place;
        var setId = options.Set is { } number
            ? RecordId.Decimal(RecordTypes.MapSet, number)
            : place?.MapSet ?? newGame.Chunk.MapSet;
        if (place?.MapSet != setId)
            place = null;
        return (plugins.Get<MapSetRecord>(setId), place);
    }

    /// <summary>The environment the scene is drawn in, and the sky behind it with its lens flare.</summary>
    private void BuildSky(PluginSet plugins, GameState game)
    {
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

        _sky = new SkyView(plugins, game.Clock) { Weather = game.Weather };
        AddChild(_sky);
        AddChild(SkyBackdropView.Create(_sky));
        AddChild(LensFlareView.Create(_sky));
        _sky.SkyShows = SkyShows;
    }

    /// <summary>The models, the particles, and trek with its chunks as they're drawn. Returns the chunk streamer.</summary>
    private ChunkStreamer BuildWorld(PluginSet plugins, GameState game, MapSetRecord set)
    {
        var models = _models = new Models(plugins) { BodyRolls = game.Random.Animation };
        // The particles actors' emitters and the weather's rain and snow start in, drawn after everything else. The pool
        // hands out the original's 384 unless the rain is denser, and has room for the densest.
        _particles = new ParticlesView(plugins, new ParticleHead(capacity: WeatherParticles.PoolCapacity));
        models.Particles = _particles;
        AddChild(_particles);
        // The texture engine code gives bodies' shadows (BORG1_PlayerShadow).
        if (plugins.FindAsset(RecordId.Hex(RecordTypes.Texture, 0x0003), "png") is { } shadowPng)
            _shadowTexture = Png.Texture(shadowPng);
        var streamer = new ChunkStreamer(plugins, game, _world, set, _visible);
        var trek = (_session = new GameSession(game, this, new ZoneEngine(game, streamer, _audio!, _particles.Head))).Trek;
        _chunks = new ChunkStreamerView(plugins, models, streamer, trek.Wanderers, _shadowTexture);
        AddChild(_chunks);
        return streamer;
    }

    /// <summary>
    /// Where the player stands and which way he faces: on the highest ground below the spawn unless a save puts him
    /// where he stood, facing <c>--yaw</c>, or the save's heading, or along −Z. The look is (−sin yaw, −cos yaw).
    /// </summary>
    private (System.Numerics.Vector3 Centre, System.Numerics.Vector2 Look, float Yaw) Stand(Vector3 spawn, SavePlace? place, LaunchOptions options)
    {
        var centre = spawn.ToNumerics();
        var above = centre with { Y = 1000 };
        if (place is null && SpherePhysics.Sweep(_world, above, above - new System.Numerics.Vector3(0, 2000, 0), 0.5f, false, out var ground, out var normal))
            centre = ground + normal * 0.5f;

        var yaw = Mathf.DegToRad(options.Yaw ?? 0);
        if (options.Yaw is null && place is { Heading: var heading } && heading != default)
            yaw = MathF.Atan2(-heading.X, -heading.Y);
        return (centre, new System.Numerics.Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw)), yaw);
    }

    /// <summary>The player's body and its shadow.</summary>
    private void BuildPlayer(PluginSet plugins, PlayerMotion motion)
    {
        _player = new Player(motion);
        AddChild(_player);
        // The party leader's actor, Alaron's (BORG7_Alaron), as the title screen sets the player's form.
        var actor = _session!.PlayerActor is { } id ? plugins.TryGet<ActorRecord>(id) : null;
        if (actor is null)
            GD.PushWarning("The party's leader has no actor, so the player has no body.");
        else if (_models!.InstantiateActor(actor, rolls: _models.BodyRolls) is var (model, animator))
        {
            _player.SetModel(model);
            ShadowDecal.MarkBody(model);
            if (animator is not null)
            {
                animator.Driven = true;
                Trek!.PlayerAnimation = animator;
            }
        }

        if (_shadowTexture is not null)
        {
            _shadow = new ShadowDecal(_shadowTexture);
            AddChild(_shadow);
        }
    }

    /// <summary>The core's follow camera, which trek begins with, and the rig that shows it.</summary>
    private void BuildFollowCamera(PluginSet plugins, ChunkEntry chunk, System.Numerics.Vector3 centre, System.Numerics.Vector2 look, float yaw,
        PlayerMotion motion, LaunchOptions options)
    {
        var follow = _follow = new FollowCamera(centre, look);
        var pitch = -options.Pitch;
        follow.Pitch = follow.DefaultPitch = pitch;
        if (options.Distance is { } distance)
            follow.Distance = follow.DefaultDistance = distance;
        // A save loaded from the game's menu arrives as through a teleporter, which chooses the mode.
        var mode = options.Camera ?? FollowCamera.ModeFor(plugins, chunk);
        if (mode == CameraMode.Interior)
            follow.Arrive(centre, centre, look, mode, null);
        Trek!.Begin(motion, follow);
        _camera = new CameraRig { Follow = follow, Yaw = yaw, Pitch = Mathf.DegToRad(-pitch) };
        AddChild(_camera);
        _camera.Sync();
    }

    /// <summary>Trek's popups, over the world and under the pause menu, and the fade over them.</summary>
    private void BuildTrekUi(PluginSet plugins, TrekLoop trek)
    {
        var trekUi = new CanvasLayer { Name = "Trek UI", Layer = PauseMenu.MenuLayer - 1 };
        _trekScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _trekScreen.MouseFilter = Control.MouseFilterEnum.Ignore;
        trekUi.AddChild(_trekScreen);
        _hud = new HudView(plugins) { Trek = trek };
        _trekScreen.Canvas.AddChild(_hud);
        _battleScreen = new CombatGuiView(plugins) { Visible = false };
        _trekScreen.Canvas.AddChild(_battleScreen);
        _popupView = new TrekPopupView(plugins);
        _trekScreen.Canvas.AddChild(_popupView);
        _box = new ConversationBoxView(plugins) { Visible = false };
        _trekScreen.Canvas.AddChild(_box);
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _fade.Visible = false;
        trekUi.AddChild(_fade);
        AddChild(trekUi);
    }

    /// <summary>The pause menu, the backdrop's page, the cinematic and the credits layers, and what the menu starts.</summary>
    private void BuildMenus(PluginSet plugins, GameState game)
    {
        var session = _session!;
        _menu = new PauseMenu(plugins, game, _settings, _stick) { SaveGame = SaveTo, LoadGame = LoadFrom, Where = session.Where, Audio = _audio };
        _menu.Opened += session.MenuOpened;
        AddChild(_menu);
        _backdrop = new BackdropLayer(plugins);
        _backdrop.Closed += session.BackdropClosed;
        AddChild(_backdrop);
        _cinematic = new CinematicLayer(plugins, _models!, _audio!);
        AddChild(_cinematic);
        _credits = new CreditsLayer(plugins);
        AddChild(_credits);
    }

    /// <summary>
    /// A tick of the pause menu, after the game's, and once it has rolled up, the map reloaded, as the original's is as the
    /// menu closes, so what changed shows; then the session carries out how it ended (<see cref="GameSession.MenuClosed"/>).
    /// </summary>
    private void StepMenu()
    {
        if (_menu?.Step() is not { } exit)
            return;
        // Trek's world is freed while a battle plays.
        if (_session!.Battle is null)
            _chunks!.Regate();
        _session.MenuClosed(exit);
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
        _session?.PrecipitationDensity = Math.Clamp(s.PrecipitationDensity, 0, WeatherParticles.MaxDensity);
        _sky?.Placement = s.SkyPlacement;
        _player?.Motion.Pace = s.BriskPace ? BriskPace : 1;
        _cinematic?.Viewport.Msaa3D = GetViewport().Msaa3D;
        foreach (var screen in new[] { _menu?.Screen, _backdrop?.Screen, _cinematic?.Screen, _credits?.Screen, _trekScreen })
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

    /// <summary>One 60 Hz tick: the game's, then the pause menu's.</summary>
    public override void _PhysicsProcess(double delta)
    {
        Tick();
        StepMenu();
    }

    /// <summary>
    /// One 60 Hz tick of the game (<see cref="GameSession.Tick"/>), and what draws the mode that ran: the cinematic or the
    /// credits, which step themselves; a cut-scene; or trek, with its popup's pulse stepped before it.
    /// </summary>
    private void Tick()
    {
        if (_player is null || _camera is null || _follow is null || _session is not { } session)
            return;
        _audio!.Step();
        // The HUD is trek's: not in a cut-scene. Under the pause menu and a backdrop's page it's drawn as it stands,
        // as the world is, so that it lays itself out again as the window or the menu size changes.
        _hud!.Visible = Scene is null && Battle is null;
        _hud.QueueRedraw();
        if (session.TrekRuns)
            _popupView!.Step();
        var before = _player.Motion.Sphere.Position;
        var step = session.Tick(ReadInput, _camera.Flying, _sky!.State.Drawn,
            actor => _actorViews.TryGetValue(actor, out var view) ? view.Motion : null,
            combatant => _combatantViews.TryGetValue(combatant, out var view) ? view.Motion : null, _spellFxView);
        switch (step)
        {
            case SessionStep.Cinematic:
                StepCinematic();
                return;
            case SessionStep.Credits:
                StepCredits();
                return;
            case SessionStep.Held:
                return;
            case SessionStep.CutScene or SessionStep.CutSceneOver:
                StepCutScene(over: step == SessionStep.CutSceneOver);
                _particles!.Step();
                return;
            case SessionStep.Combat or SessionStep.CombatOver:
                StepBattle(over: step == SessionStep.CombatOver);
                _particles!.Step();
                return;
        }
        _particles!.Step();
        ShowFade(session.Trek.Brightness);
        if (step == SessionStep.CutSceneCued)
        {
            BeginCutScene();
            return;
        }
        if (step == SessionStep.BattleCued)
        {
            BeginBattle();
            return;
        }
        _sky!.Step();
        ScrollTextures();
        _hud.Outdoors = session.Where().Outdoors;
        if (step == SessionStep.Frozen)
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
        if (_pointLights)
            LightBodies();
        var motion = _player.Motion;
        TrackPlayer(motion.Sphere.Position);
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
        if (_popupView!.Popup != Trek!.Use.Popup)
            _popupView.Popup = Trek.Use.Popup;
    }

    /// <summary>Darkens the screen to a brightness, from 0 for black to 1.</summary>
    private void ShowFade(float brightness)
    {
        // The particles darken with the fade (the particle head's +0x334, which ScreenFade.Step writes).
        if (_particles is not null)
            _particles.Head.Brightness = brightness;
        _fade.Visible = brightness < 1;
        _fade.Color = new Color(0, 0, 0, 1 - brightness);
    }

    // What trek asks of the runtime.

    void ITrekHost.Entered(ChunkEntry? chunk) => _sky!.Enter(chunk);

    /// <summary>The player has come through a teleporter: the sky, the camera and his body are where he is now.</summary>
    void ITrekHost.Arrived()
    {
        var motion = _player!.Motion;
        // Trek has killed the emitters still spawning (ParticleHead.KillActive).
        _sky!.Apply();
        _camera!.Yaw = MathF.Atan2(-motion.Heading.X, -motion.Heading.Y);
        _player.Sync();
        _camera.Sync();
        PlaceShadow();
        ResetInterpolation();
    }

    IRootMotion ITrekHost.Motion(Wanderer wanderer) => _chunks!.View(wanderer)?.Motion ?? Core.Actors.IActorMotion.Still;

    void ITrekHost.Say(string text) => Say(text);

    void ITrekHost.Log(string text) => GD.Print(text);

    void ITrekHost.Warn(string text) => GD.PushWarning(text);

    /// <summary>
    /// Once a frame at the console's average 20 fps, the scenes the original would draw load their
    /// own copies of their scrolling textures, which moves them on.
    /// </summary>
    private void ScrollTextures()
    {
        if (!_textureFrame.Tick())
            return;
        var camera = _camera!.Current.GlobalTransform;
        foreach (var scene in Streamer!.DrawnScenes(camera.Origin, -camera.Basis.Z))
            scene.Draw();
        _ringView?.Draw();
        _spellFxView?.Draw();
        _aimView?.Draw();
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
        var follow = Scene?.Camera ?? _follow!;
        var camera = new System.Numerics.Vector2(follow.Position.X, follow.Position.Z);
        var toAim = new System.Numerics.Vector2(follow.Aim.X, follow.Aim.Z) - camera;
        var distance = toAim.Length();
        var from = Scene is not null ? follow.Aim : _player!.Motion.Sphere.Position;
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
        _shadow.Place(_world, sphere, BodyShadow.DrawAlpha(BodyShadow.TrekDistance, System.Numerics.Vector3.Distance(_follow!.Aim, sphere.Position), player: true));
    }

    /// <summary>
    /// Lights the maps' actors, the player and the wanderers by the point lights that reach them this tick, in the order
    /// the original draws them (<see cref="Core.Lights.WorldLights"/>): the actors with their chunks, then the bodies.
    /// </summary>
    private bool _pointLights = true;
    private bool _spellFx = true;

    private void LightBodies()
    {
        var trek = Trek!;
        var lights = trek.Zone.Lights;
        trek.Zone.BeginLightFrame(trek.Brightness, _sky!.State.Drawn, _sky.Moon);
        _chunks!.LightActors(lights);
        var centre = _player!.Motion.Sphere.Position;
        N64Material.SetLights(_player.Meshes, lights.ForBody(centre, _chunks.Chunks.MapAt(centre), party: true));
        _chunks.LightWanderers(lights, _chunks.Chunks);
    }

    /// <summary>Moves the wanderers' bodies to where they are, and fades them and their shadows by the camera's aim.</summary>
    private void DrawWanderers()
    {
        var aim = _follow!.Aim;
        var centre = _chunks!.Chunks.MapAt(_player!.Motion.Sphere.Position);
        var ownAim = BodyShadow.ChunkAim(aim, centre?.Origin);
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

    public override void _Process(double delta)
    {
        SetTickRate();
        if (Streamer is not { } chunks || _player is null || _camera is null)
            return;
        // The world goes on being drawn behind the pause menu and a backdrop's page, still: its scenery's actors,
        // which animate with the frame, stop, and so does the free camera. The parent processes first, so this
        // holds from the frame the menu opens.
        var still = _session is { Held: true } ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        _chunks!.ProcessMode = still;
        _camera.ProcessMode = still;
        foreach (var map in _changed)
            chunks.Rebuild(map);
        _changed.Clear();
        var focus = _camera.Flying ? _camera.Current.GlobalPosition
            : Scene is not null ? Scene.Camera.Aim.ToGodot()
            : Battle is not null ? Battle.Camera.Aim.ToGodot()
            : _player.GlobalPosition;
        // A cut-scene's and an arena's 3×3 stay as they loaded.
        if (Scene is null && Battle is null)
            chunks.Chunks.Update(focus.ToNumerics());
        var view = View();
        chunks.Fade(view, _drawDistance);
        // Oriana's path lights go out as they're reached; not for the free camera, which the original hasn't.
        if (!_camera.Flying)
            chunks.PutOut(view);
        RepeatStick(delta);
        // From where the scenery is drawn this frame: between ticks, the camera interpolated.
        var eye = _camera.Current.GetGlobalTransformInterpolated().Origin;
        Billboards.Face(chunks.BillboardFacings, eye);
        _ringView?.Face(eye);

        if (_overlay.Visible)
            UpdateOverlay(chunks, focus);
    }

    /// <summary>
    /// The stick held in trek's popups, conversations and cut-scenes moves their cursor on as the D-pad would. With
    /// none up, what was held is forgotten, so the next doesn't start by repeating. The pause menu steps its own.
    /// </summary>
    private void RepeatStick(double delta)
    {
        switch (_session?.Mode)
        {
            case GameMode.Menu:
                return;
            case GameMode.Popup or GameMode.Conversation or GameMode.CutScene or GameMode.Backdrop:
            case GameMode.Combat when Battle?.Menu is not null:
                _stick.Step(delta, direction => _UnhandledInput(new InputEventAction { Action = MenuStick.Dpad(direction), Pressed = true }));
                break;
            default:
                _stick.Clear();
                break;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        _stick.Track(e);
        var action = _stick.Translate(e);
        // The press goes to what runs (GameSession.Mode).
        switch (_session?.Mode)
        {
            case GameMode.Cinematic:
                CinematicInput(action);
                return;
            case GameMode.Credits:
                CreditsInput(action);
                return;
            case GameMode.Menu:
                if (_menu!.Handle(e))
                    return;
                break;
            case GameMode.Backdrop:
                Browse(action);
                return;
            case GameMode.Popup:
                var use = Trek!.Use;
                if (action == MenuAction.Accept)
                    use.Accept();
                else if (action == MenuAction.Back)
                    use.Back();
                else if (action is MenuAction.Up or MenuAction.Down)
                    use.MoveCursor(MenuStick.Vertical(action));
                else
                    return;
                ShowPopup();
                _popupView!.QueueRedraw();
                return;
            case GameMode.Conversation:
                Converse(action);
                return;
            case GameMode.CutScene:
                CutSceneInput(action);
                if (!e.IsActionPressed("toggle_fly") && !e.IsActionPressed("toggle_overlay"))
                    return;
                break;
            case GameMode.Combat:
                CombatInput(action);
                if (CombatDebugKey(e))
                    return;
                if (!e.IsActionPressed("toggle_fly") && !e.IsActionPressed("toggle_overlay"))
                    return;
                break;
            default:
                if (Trek is { Cued: not null } or { CutSceneFade.Fading: true })
                {
                    // Trek's input is dead while it fades into or out of a cut-scene.
                    if (action is MenuAction.Start or MenuAction.Accept)
                        return;
                }
                else if (action == MenuAction.Start && _menu is not null && Trek is { Teleporting: false })
                {
                    _menu.Open();
                    return;
                }
                else if (action == MenuAction.Accept && _camera is { Flying: false } && Trek is { TeleportFade.Fading: false } && UseNearest())
                {
                    return;
                }
                else if (action == MenuAction.Back && _camera is { Flying: false } && Trek is { } trek)
                {
                    // B raises or lowers the party's portraits, and the minimap with them.
                    trek.PressB();
                    return;
                }
                break;
        }

        if (e.IsActionPressed("cycle_ui_lines") && _menu is not null)
        {
            _settings.UiLines = _settings.UiLines switch { < 360 => 360, < 480 => 480, _ => 240 };
            _settings.Notify();
        }
        else if (e.IsActionPressed("quick_save") && _game is not null && Scene is null && Battle is null)
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

    /// <summary>A in trek: uses the map object the player reaches, and shows any popup it brings up.</summary>
    private bool UseNearest()
    {
        var used = Trek!.UseNearest();
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
    private void Converse(MenuAction action)
    {
        var conversation = Trek!.Conversation!;
        var runner = conversation.Runner;
        if (runner.Status != Core.Scripts.ScriptStatus.Showing || runner.Page is null)
            return;
        var box = _box!.Box;
        if (action == MenuAction.Accept)
        {
            if (_box.Accept(runner, conversation.Press) != DialogueAccept.ShowedChoices)
                Trek.Refresh();
        }
        else if (action is MenuAction.Up or MenuAction.Down)
        {
            _box.Point(box.Highlight + MenuStick.Vertical(action));
        }
    }

    /// <summary>
    /// The buttons on a backdrop's or the help menu's page: A takes the cursor's choice, or with none moves on, and
    /// up and down move the cursor. In the help menu, B takes the last choice, Exit Help or back, or with none moves on
    /// as A does: only a help script's page gets a B (<c>Dialogue.ShowNode</c>, 0x80047714, by its start mode, 4) and
    /// it returns the last row, else the text, which carries the node's link (<c>Dialogue.OnBHelp</c>, 0x80048354).
    /// </summary>
    private void Browse(MenuAction action)
    {
        if (!_backdrop!.Taking || Trek!.Conversation?.Runner is not { Status: Core.Scripts.ScriptStatus.Showing, Page: { } page } runner)
            return;
        var back = action == MenuAction.Back && runner.Script.Mode == DialogueMode.Help;
        if (action == MenuAction.Accept || back)
        {
            DialogueBox.Advance(runner, back ? page.Choices.Count - 1 : _backdrop.Page!.Highlight);
            Trek.Refresh();
        }
        else if (action is MenuAction.Up or MenuAction.Down)
        {
            _backdrop.MoveCursor(MenuStick.Vertical(action));
        }
    }

    /// <summary>
    /// Shows the conversation's page in the box, or a backdrop's on its picture, or hides the box once there's none
    /// and lets a backdrop's page fade out.
    /// </summary>
    void ITrekHost.ConversationChanged()
    {
        if (Trek!.Conversation is { ShowsBackdrop: true } backdrop)
        {
            if (!_backdrop!.Showing)
            {
                _backdrop.Open(BackdropStyle.For(backdrop.Runner.Script.Backdrop));
                _session!.BackdropOpened();
            }
            if (backdrop.Runner is { Status: Core.Scripts.ScriptStatus.Showing, Page: { } shown })
                _backdrop.Show(shown);
            return;
        }
        _backdrop!.Close();
        if (Trek!.Conversation?.Runner is { Status: Core.Scripts.ScriptStatus.Showing, Page: { } page })
            _box!.Show(page);
        else
            _box!.Visible = false;
    }

    /// <summary>
    /// Shows where the game goes as a mode, the menu or a script ends (<see cref="Transition"/>): the shop or training in the
    /// pause menu, the cut-scene's stage, a battle's arena and its pause menu, trek again, the credits, a cinematic or the menu
    /// reopened.
    /// </summary>
    void ITransitionHost.Transition(Transition next)
    {
        switch (next)
        {
            case Transition.OpenShop shop:
                _menu!.OpenShop(shop.Shop);
                break;
            case Transition.OpenTraining training:
                _menu!.OpenTraining(training.Trainer);
                break;
            case Transition.StartBattle battle:
                SayBattle(battle.Setup);
                break;
            case Transition.StartedBattle started:
                ShowArena(started.Battle, started.Arena);
                break;
            case Transition.PauseBattle pause:
                _menu!.OpenCombat(pause.PartyTab, pause.CanFlee, Battle is { } fighting ? CombatPause.For(fighting) : null);
                break;
            case Transition.EndBattle:
                FreeArena();
                break;
            case Transition.PostBattle won:
                _menu!.OpenPostBattle(won.Spoils);
                break;
            case Transition.GameOver:
                GameOver();
                break;
            case Transition.Camp camp:
                GD.Print($"Camping: {camp.Outcome}.");
                break;
            case Transition.Rest rest:
                GD.Print($"{rest.Hours} hours pass.");
                break;
            case Transition.Say say:
                Say(say.Text);
                break;
            case Transition.StartCutScene cutScene:
                ShowStage(cutScene.Scene, cutScene.Stage);
                break;
            case Transition.EndCutScene ended:
                FreeStage(ended.Scene);
                break;
            case Transition.ResumeTrek:
                ShowTrek();
                break;
            case Transition.PlayCredits:
                PlayCredits();
                break;
            case Transition.PlayCinematic cinematic:
                PlayCinematic(cinematic.Cinematic);
                break;
            case Transition.ReopenMenu { Theatre: true }:
                _menu!.OpenTheatre();
                break;
            case Transition.ReopenMenu:
                _menu!.Open();
                break;
        }
    }

    /// <summary>A battle no trek cued (<see cref="Transition.StartBattle"/>), which nothing in the runtime sends: it's said.</summary>
    private void SayBattle(BattleSetup setup) => Say($"A battle with {setup.Party?.ToString() ?? "a script's enemies"} would start.");
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
            if (!_session!.NotePlace())
                return "The player isn't in a chunk.";
            _game.Label = null;
            SaveFile.Write(path, _game.Save());
            _lastSave = path;
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
            _lastSave = path;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            Say(e.Message);
            return;
        }
        GetTree().ReloadCurrentScene();
    }

    /// <summary>Whether the scene starts again on a new game, whatever the command line's save, as a game over with no save does.</summary>
    private static bool _newGame;

    /// <summary>
    /// The game is over (<see cref="Transition.GameOver"/>): where the original goes back to the title, the runtime, which has none,
    /// loads the save the game last came from or went to, the user's choice, or with none starts a new game.
    /// </summary>
    private void GameOver()
    {
        if (_lastSave is { } path && File.Exists(path))
        {
            GD.Print($"The game is over: loading {path}.");
            LoadFrom(path);
            return;
        }
        GD.Print("The game is over: a new game.");
        _newGame = true;
        GetTree().ReloadCurrentScene();
    }

    /// <summary>Saves the game to <c>quick.json</c> in the saves folder, with where the player stands.</summary>
    private void QuickSave()
    {
        if (!_session!.NotePlace())
        {
            Say("Can't save: the player isn't in a chunk.");
            return;
        }
        var path = Path.Combine(DataPaths.Saves, "quick" + SaveFile.Extension);
        SaveFile.Write(path, _game!.Save());
        _lastSave = path;
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
        Add("toggle_radar_anchor", Key.F9);
        Add("battle_rout", Key.F10);
        Add("battle_collapse", Key.F11);
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
}
