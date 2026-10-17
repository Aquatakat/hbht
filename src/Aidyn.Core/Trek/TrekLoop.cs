using System.Numerics;
using Aidyn.Core.Audio;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Particles;
using Aidyn.Core.Scripts;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Trek;

/// <summary>What trek asks of whatever presents it: the runtime, or a test.</summary>
public interface ITrekHost : IScriptHost
{
    /// <summary>The player is in a chunk, every tick: the sky follows it.</summary>
    void Entered(ChunkEntry? chunk);

    /// <summary>The player has come through a teleporter and stands at its arrival point.</summary>
    void Arrived();

    /// <summary>A conversation or sign showing has a new page, or has ended.</summary>
    void ConversationChanged();

    /// <summary>
    /// A conversation has ended: what it ends in (a shop, training, a battle, a camp), and any fault.
    /// </summary>
    /// <param name="keeper">Who keeps a shop or training it opens (<see cref="Conversation.Keeper"/>).</param>
    void ScriptEnded(RecordId script, ScriptEnding ending, RecordId? keeper);

    /// <summary>A wanderer's animation, which moves its body by its root motion.</summary>
    IRootMotion Motion(Wanderer wanderer);

    /// <inheritdoc cref="IMapObjectHost.OpenLoot"/>
    void OpenLoot(ItemPile loot, int gold);

    /// <inheritdoc cref="IMapObjectHost.Animate"/>
    void Animate(LoadedMap map, SceneObject actor, int request);

    /// <inheritdoc cref="IMapObjectHost.Changed"/>
    void Changed(LoadedMap map, SceneObject scene);

    /// <summary>Tells the player something the port can't do yet, such as a battle or a backdrop.</summary>
    void Say(string text);

    /// <summary>Notes something for whoever's watching the log.</summary>
    void Log(string text);

    /// <summary>A UI image's width and height, which the HUD's minimap centres its pieces by.</summary>
    (int Width, int Height) ImageSize(RecordId image);
}

/// <summary>What a tick of trek came to.</summary>
public enum TrekStep
{
    /// <summary>Trek ran.</summary>
    Ran,
    /// <summary>Time passed, but the player and the world didn't move: the free camera is flying.</summary>
    Frozen,
    /// <summary>A cut-scene is cued and the screen is black: it takes over (<see cref="TrekLoop.Cued"/>).</summary>
    CutScene,
}

/// <summary>
/// Trek, the original's free exploration (<c>gametrek.cpp</c>, <c>Trek_GameState</c>), as a loop over the
/// subsystems it shares with the rest of the game: the chunks around the player, the clock, map objects,
/// the follow camera, the player's movement, the wanderers, conversations, and the fades into a teleport
/// or a cut-scene. Each <see cref="Tick"/> steps them in the original's order. What draws trek reads the
/// state here and is told what changed through <see cref="ITrekHost"/>.
/// </summary>
public sealed class TrekLoop : IMapObjectHost
{
    /// <summary>
    /// How a teleport fades (<c>set_teleport_pointer</c>, <c>handleZoneEngineFrame</c>, <c>appState_1</c>):
    /// out at 2/3 of full brightness a tick, black for 3 frames, then in at 1/15 a tick.
    /// </summary>
    public const float TeleportFadeOutPerTick = 2f / 3, TeleportFadeInPerTick = 1f / 15;

    /// <summary>Trek fades out into a cut-scene, and in after it, at 1/30 a tick (<c>FUN_80024c54</c>, <c>initGameTrek</c>).</summary>
    public const float CutSceneFadePerTick = 1f / 30;

    private readonly ITrekHost _host;
    /// <summary>The teleporter the player is going through once the screen is black.</summary>
    private TeleporterObject? _pending;
    /// <summary>The chunk the player was last in, to age what map objects remember as he crosses into another.</summary>
    private (int Row, int Column)? _centre;
    private ChunkEntry? _chunk;
    /// <summary>The tick within a frame of the sky, which flashes lightning once a frame.</summary>
    private int _skyTick;
    /// <summary>The last script a dialogue object asked to start that trek can't run yet, such as a backdrop.</summary>
    private RecordId? _lastScript;

    /// <param name="particles">The particle pool, which the weather's rain and snow fall in; none draws none.</param>
    public TrekLoop(GameState game, ITrekHost host, ChunkStreamer chunks, GameAudio? audio = null, ParticleHead? particles = null)
    {
        Game = game;
        Audio = audio ?? GameAudio.Silent(game.Plugins);
        _host = host;
        Chunks = chunks;
        Particles = particles;
        // The drops land on every loaded chunk's collision, where the original's find only the player's chunk's
        // (Zones.PlayerMap): the user's choice, so that near a chunk's edge they don't fall through the next one.
        if (particles is not null && game.Weather is { } weather)
            Precipitation = new WeatherParticles(particles, weather.Rules, game.Random.Cosmetic, () => Camera.Aim, () => Camera.LookXZ,
                () => Chunks.World);
        Wanderers = new Wanderers(game.Plugins);
        Use = new MapObjectUse(game, MenuText.From(game.Plugins), this) { World = chunks.World };
        Hud = new TrekHud(game, host.ImageSize);
        Sounds = new MapSounds(Audio, game.Random.Cosmetic,
            o => Flags.MapObjectPresence.There(o, f => game.Flags.TryHandle(f, out var h) && game.Flags.Get(h)));
        Keys = new AudioKeys(Audio, game.Sounds, game.Random.Cosmetic);
        Lights = new Lights.WorldLights(game.Random.Cosmetic) { World = chunks.World };
        WeatherSounds = new WeatherSounds(Audio, game.Sounds);
        _playerSounds = game.Plugins.TryGet<EntityRecord>(GameState.Alaron)?.Sounds;
        chunks.Loaded += (_, map) =>
        {
            Wanderers.Load(map, Chunks.MapSet.CellSize, Chunks.World, Game.Memory);
            Sounds.Load(map);
            Lights.Load(map);
        };
        chunks.Unloading += (_, map) =>
        {
            Wanderers.Unload(map);
            Sounds.Unload(map);
            Lights.Unload(map);
        };
    }

    public GameState Game { get; }

    /// <summary>What plays: the map set's music, and the sounds of what's here.</summary>
    public GameAudio Audio { get; }

    /// <summary>The loaded chunks' sound map objects.</summary>
    public MapSounds Sounds { get; }

    /// <summary>The loaded chunks' light map objects and the dynamic lights, which light the player and the maps' actors.</summary>
    public Lights.WorldLights Lights { get; }

    /// <summary>Bodies' footsteps and voices.</summary>
    public AudioKeys Keys { get; }

    /// <summary>The player's sounds: Alaron's, which every body has until it's given its own (<c>Player.Reset</c>), as cut-scenes' actors keep.</summary>
    public BodySounds? AlaronSounds => _playerSounds;

    private readonly BodySounds? _playerSounds;

    private readonly BodyAudio _playerAudio = new();

    /// <summary>The chunks around the player, which a teleport to another map set changes.</summary>
    public ChunkStreamer Chunks { get; private set; }

    /// <summary>The particle pool (<c>Particles.Head</c>), which its owner steps, or null.</summary>
    public ParticleHead? Particles { get; }

    /// <summary>The weather's rain and snow, in <see cref="Particles"/>, or null without them.</summary>
    public WeatherParticles? Precipitation { get; }

    /// <summary>The loaded chunks' monster parties' bodies.</summary>
    public Wanderers Wanderers { get; }

    /// <summary>Using map objects, and the popups that shows.</summary>
    public MapObjectUse Use { get; }

    public PlayerMotion Player { get; private set; } = null!;

    public FollowCamera Camera { get; private set; } = null!;

    /// <summary>The player's animation, which moves him by its root motion.</summary>
    public IRootMotion PlayerAnimation { get; set; } = IActorMotion.Still;

    /// <summary>The sundial, the party's portraits, and the compass or the minimap.</summary>
    public TrekHud Hud { get; }

    /// <summary>Whether the HUD is drawn and steps: not while a conversation or sign shows (<c>diaClass->menu</c>).</summary>
    public bool HudShown => Conversation is null;

    /// <summary>The conversation or sign showing, if any.</summary>
    public Conversation? Conversation { get; private set; }

    /// <summary>The player's nearest body and how far, from the last tick's push loop, which A can reach.</summary>
    public (Wanderer? Nearest, float Distance) Nearest { get; private set; }

    /// <summary>A teleport's fade.</summary>
    public ScreenFade TeleportFade { get; } = new();

    /// <summary>Trek's fades into and out of a cut-scene.</summary>
    public ScreenFade CutSceneFade { get; } = new();

    /// <summary>The screen's brightness, from 0 for black to 1.</summary>
    public float Brightness => MathF.Min(TeleportFade.Brightness, CutSceneFade.Brightness);

    /// <summary>Whether a teleport is fading out, before the player goes through.</summary>
    public bool Teleporting => _pending is not null;

    /// <summary>A cut-scene waiting for trek to fade out, or for its host to start it.</summary>
    public (DialogueRecord Script, ScriptStart Start, ChunkRef Stage)? Cued { get; private set; }

    /// <summary>Puts the player and the follow camera in trek, before the first tick.</summary>
    public void Begin(PlayerMotion player, FollowCamera camera)
    {
        Player = player;
        Camera = camera;
        Use.Camera = camera;
        Use.PlayerRadius = player.Sphere.Radius;
        Hud.Start();
        RequestMusic(Chunks.ChunkAt(player.Sphere.Position));
    }

    /// <summary>
    /// Asks for the music of the map set and the chunk the player is in (<c>Zones.SetMapSet</c>), as the original does on
    /// every crossing into another chunk and every teleport. Out of the chunks with music, it stops.
    /// </summary>
    public void RequestMusic(ChunkEntry? chunk) => Audio.Music = MapMusic.For(Chunks.MapSet, chunk, Game.Random.Cosmetic);

    /// <summary>
    /// The player is in a chunk: its terrain is written into the flags, and another terrain may change the weather. Crossing into another chunk is a
    /// teleport in the original (<c>ConfirmPlayerWithinZone</c>), which ages what map objects remember.
    /// </summary>
    public void Enter(ChunkEntry? chunk)
    {
        _host.Entered(chunk);
        if (chunk is not null && _centre != (chunk.Row, chunk.Column))
        {
            if (_centre is not null)
                Game.Memory.Age();
            _centre = (chunk.Row, chunk.Column);
        }
        if (chunk is null || chunk == _chunk)
            return;
        _chunk = chunk;
        RequestMusic(chunk);
        Game.EnterTerrain(chunk.Terrain);
        Hud.Arrive(Game.Terrain(chunk.Terrain)?.SkyDrawn ?? true);
    }

    /// <summary>
    /// A tick of the weather as the zone engine draws it (<c>HandleZoneEngineFrame</c>), in trek and in cut-scenes:
    /// <c>RenderSky</c>'s lightning once a frame, with its thunder, then <c>ProcessWeather</c> every tick, with the
    /// rain loop.
    /// </summary>
    /// <param name="skyDrawn">Whether the sky is drawn (the original's sky type 3).</param>
    /// <param name="precipitation">Whether rain and snow fall too: trek's alone, as cut-scenes don't draw them yet.</param>
    public void StepWeather(bool skyDrawn, bool precipitation = false)
    {
        if (Game.Weather is not { } weather)
            return;
        _skyTick = (_skyTick + 1) % Cadence.FrameTicks;
        if (_skyTick == 0)
        {
            weather.SkyFrame(skyDrawn, Game.Random.Cosmetic);
            if (weather.Thunder is { } thunder)
                WeatherSounds.Thunder(thunder);
        }
        // The rain's particles go with the weather's one decision about them; cut-scenes draw none.
        weather.Draw(skyDrawn, emitters: precipitation ? Precipitation : null);
        WeatherSounds.Step(weather);
    }

    /// <summary>The rain loop and the thunder.</summary>
    public WeatherSounds WeatherSounds { get; }

    /// <summary>What's said when a journal entry is added: a literal in the code (<c>0x800ce28c</c> in 1.0).</summary>
    public const string NewJournalEntry = "A new journal entry has been added.";

    /// <summary>
    /// One 60 Hz tick of trek, as the original orders it: the fades, the clock, the map objects the player
    /// touches, the camera, the player, the wanderers' decisions and then their bodies, a conversation's
    /// tick, the trigger triangles he treads on and a trigger triangle's camera. A popup stops only the
    /// clock (<c>TickGameTime</c>, skipped while <c>isPaused</c>), which the user saw in the original: the
    /// sun stops, while bodies, animations and clouds go on, and the player's input goes to the popup. A
    /// conversation stops the clock and the wanderers' decisions too, and a fade takes the player's input.
    /// </summary>
    /// <param name="readInput">The player's controls, read only when trek takes them.</param>
    /// <param name="frozen">Whether only time passes, as while the free camera flies.</param>
    public TrekStep Tick(Func<PlayerInput> readInput, bool frozen = false)
    {
        StepFades();
        if (Cued is not null && !CutSceneFade.Fading)
            return TrekStep.CutScene;
        var fading = TeleportFade.Fading || CutSceneFade.Fading;
        // Bgm.Update waits for a fade to end, so the music changes once the screen is up.
        Audio.HoldMusic = fading;
        Enter(Chunks.ChunkAt(Player.Sphere.Position));
        // The clock stops under a popup or a conversation.
        var talking = Conversation is not null;
        // NewJournalEntryPopup: with nothing else up, a new entry is told once, and its flag cleared.
        var newEntry = Game.Flags.Handle(Game.StateFlagsRecord.NewJournalEntry);
        if (!fading && Use.Popup is null && !talking && Game.Flags.Peek(newEntry))
        {
            Game.Flags.Set(newEntry, false);
            Use.Tell(NewJournalEntry);
        }
        // HandleZoneEngineFrame's weather (RenderSky's lightning and ProcessWeather), whatever stops the clock; then TickGameTime.
        StepWeather(precipitation: true, skyDrawn: _chunk is null || (Game.Terrain(_chunk.Terrain)?.SkyDrawn ?? true));
        if (Use.Popup is null && !talking)
            Game.PassTime(1);
        if (frozen)
            return TrekStep.Frozen;

        var world = Chunks.World;
        var motion = Player;
        // ProcessVoxelObjects comes before the player moves. Through a teleport's fade, only camera
        // objects work, so the camera is where the room's puts it as the screen comes up, and in a
        // conversation too, so one holding the camera keeps it rather than the conversation's.
        var playing = !fading;
        if (Chunks.MapAt(motion.Sphere.Position) is { } centre)
        {
            var at = motion.Sphere.Position - centre.Origin;
            if (playing && !talking)
                Use.Touch(centre, at);
            else
                Use.TouchCameras(centre, at);
        }
        var input = fading || Use.Popup is not null || talking ? new PlayerInput(Vector2.Zero) : readInput();
        // Actor::Move: the stick pushed lowers the portraits, but not while it orbits the camera.
        if (!input.Locked && input.Stick.Length() > 0.05f)
            Hud.Raise(false);
        var before = motion.Sphere.Position;
        if (Conversation is { With: not null } framing)
            framing.Frame(Camera, world);
        else
            Camera.Step(world, motion.Sphere.Position, motion.Heading);
        Keys.Eye = Camera.Position;
        Keys.Aim = Camera.Aim;
        motion.Step(world, input, Camera, Keys.Sounding(PlayerAnimation, _playerAudio, _playerSounds, motion.Sphere));
        // WanderTick, which a popup, a conversation, the menu and a fade stop.
        if (playing && Use.Popup is null && !talking)
        {
            foreach (var w in Wanderers.Tick(world, motion.Sphere.Position, motion.Request, Game.Party, Game.Random.Wander,
                Game.Weather?.FogTime ?? 0))
                _host.Log($"{MapRecord.VoxelId(w.Map.Record.Id, w.Party.Index)} ambushes the party; battles come with combat (2.5).");
        }
        // ProcessPlayers: the player is the first body, then the rest move and push.
        Nearest = Wanderers.Step(world, motion.Sphere, before, Chunks.MapAt(motion.Sphere.Position)?.Key, Camera.Aim,
            w => Keys.Sounding(_host.Motion(w), w.Audio, w.Sounds, w.Body.Sphere));
        if (Conversation is { } conversation && conversation.Tick())
            Refresh();
        foreach (var (part, target) in motion.Sphere.Trod)
        {
            if (Chunks.Map(part) is not { } map)
                continue;
            if (playing && !talking)
                Use.Tread(map, target, motion.Sphere.Position - map.Origin);
            else
                Use.TreadCamera(map, target);
        }
        Use.StepCameras(motion.Sphere.Position);
        StepSounds();
        if (HudShown)
        {
            Hud.Enter(Chunks.MapSet);
            var chunk = Chunks.ChunkAt(motion.Sphere.Position);
            Hud.Tick(chunk, chunk is null ? motion.Sphere.Position : motion.Sphere.Position - Chunks.Origin(chunk), Game.Flags);
        }
        return TrekStep.Ran;
    }

    /// <summary>
    /// The map's sounds follow the player and the camera, as in trek, and in the pause menu, which steps them too with
    /// everything held still.
    /// </summary>
    public void StepSounds() => Sounds.Step(Player.Sphere.Position, Camera.Position, Camera.Aim);

    /// <summary>
    /// Steps a teleport's fade: out, black for a few frames, then the player goes through and it fades
    /// in. The original fades out only if nothing else is fading, and teleports once it's done.
    /// </summary>
    private void StepFades()
    {
        TeleportFade.Step();
        if (_pending is { } teleporter && !TeleportFade.Fading)
        {
            _pending = null;
            TeleportFade.In(TeleportFadeInPerTick, fromBlack: false);
            GoThrough(teleporter);
        }
        CutSceneFade.Step();
    }

    /// <summary>
    /// Takes the player through a teleporter (<c>TeleportPlayer</c>): what map objects remember ages, the
    /// destination's chunks load, in another map set if need be, and he's set down at the arrival point
    /// facing its way. The camera goes with him, outdoors or in an interior by the chunk's terrain, to the
    /// teleporter's camera point if it has one (<see cref="FollowCamera.Arrive"/>).
    /// </summary>
    private void GoThrough(TeleporterObject teleporter)
    {
        if (TeleportArrival.For(Game.Plugins, teleporter) is not { } arrival)
        {
            _host.Say($"{teleporter.Name} leads to a chunk no plugin has.");
            return;
        }
        Game.Memory.Age();
        if (arrival.Set.Id != Chunks.MapSet.Id)
        {
            Chunks.Change(arrival.Set);
            Chunks.World.Clear();
            Wanderers.Clear();
            // The zone engine starts again, and its dynamic lights with it (FreeZoneEngine, DynamicLights.FreeAll).
            Lights.Dynamic.Clear();
        }
        var origin = Chunks.Origin(arrival.Chunk);
        var point = origin + arrival.Position;
        Chunks.Update(point);
        Use.ClearCameras();
        // The weather's emitters are forgotten, then every emitter still spawning is killed, theirs among them, so the
        // rain starts again about the player from the next frame, asking for the rain sky again; its loop plays on.
        Game.Weather?.Forget(Precipitation);
        Particles?.KillActive();
        var motion = Player;
        var from = motion.Sphere.Position;
        motion.Sphere.Position = TeleportArrival.Settle(Chunks.World, point, motion.Sphere.Radius) ?? point;
        motion.Sphere.Velocity = Vector3.Zero;
        if (arrival.Heading is { } heading)
            motion.Heading = heading;
        _centre = (arrival.Chunk.Row, arrival.Chunk.Column);
        Enter(arrival.Chunk);
        var camera = arrival.Camera is { } local ? origin + local : (Vector3?)null;
        Camera.Arrive(from, motion.Sphere.Position, motion.Heading, FollowCamera.ModeFor(Game.Plugins, arrival.Chunk), camera);
        _host.Arrived();
    }

    /// <summary>B in trek: raises or lowers the portraits, but not through a fade, a popup or a conversation (<c>Actor::Move</c>).</summary>
    public void PressB()
    {
        if (!TeleportFade.Fading && !CutSceneFade.Fading && Cued is null && Use.Popup is null && Conversation is null)
            Hud.PressB();
    }

    /// <summary>A in trek: uses the map object the player reaches in the chunk he's in (<c>obj_ref_func</c>).</summary>
    public bool UseNearest()
    {
        var sphere = Player.Sphere;
        if (Chunks.MapAt(sphere.Position) is not { } map)
            return false;
        (MonsterPartyObject, Vector3, float)? body = null;
        if (Nearest is ({ } nearest, var distance) && nearest.Map == map && Wanderers.Reaches(nearest, distance, sphere))
            body = (nearest.Party, nearest.Body.Sphere.Position - map.Origin, distance);
        return Use.UseNearest(map, sphere.Position - map.Origin, Player.Heading, sphere.Radius, body);
    }

    /// <summary>
    /// The cued cut-scene starts, once <see cref="Tick"/> says so: trek's world goes, and the cut-scene is
    /// its host's to play on its own stage.
    /// </summary>
    public (DialogueRecord Script, ScriptStart Start, ChunkRef Stage) BeginCutScene()
    {
        var cued = Cued ?? throw new InvalidOperationException("No cut-scene is cued.");
        Cued = null;
        Free();
        // The stage is built as trek's world is (InitZoneEngine), and what's drawn of the weather starts again.
        Game.Weather?.StartDrawing();
        return cued;
    }

    /// <summary>
    /// Trek's world goes (<c>clear_HUD_elements</c>): its chunks, their collision and wanderers, and the
    /// cameras holding the view.
    /// </summary>
    public void Free()
    {
        Chunks.Clear();
        Chunks.World.Clear();
        Wanderers.Clear();
        Sounds.Clear();
        Nearest = default;
        Use.ClearCameras();
        // FreeZoneEngine: Weather.Free stops the rain loop.
        Game.Weather?.Free();
        WeatherSounds.StopRain();
        // The zone engine's end frees the pool, and with it the rain.
        Precipitation?.Clear();
    }

    /// <summary>
    /// Loads the map set's chunks and their wanderers again where the player stands, as trek starts again
    /// after a cut-scene, a backdrop or the help menu (<c>initGameTrek</c>): every map object is gated by
    /// the flags as they are now, and the wanderers start at their home nodes.
    /// </summary>
    public void Rebuild()
    {
        Free();
        Game.Weather?.StartDrawing();
        Chunks.Update(Player.Sphere.Position);
    }

    /// <summary>
    /// Trek comes back after a cut-scene: its world where the player stood, the follow camera aimed at
    /// him and swinging round quickly, and the screen fading in.
    /// </summary>
    public void Resume()
    {
        Rebuild();
        var at = Player.Sphere.Position;
        Enter(Chunks.ChunkAt(at));
        // InitZoneEngine asks for the music again, which the cut-scene's stage may have changed.
        RequestMusic(Chunks.ChunkAt(at));
        Camera.AimAt(at);
        Camera.Snap = 1;
        Hud.Start();
        CutSceneFade.In(CutSceneFadePerTick);
    }

    /// <summary>
    /// Camps where the player stands (<c>Party::CampAmbushCheck</c>): refused where the chunk's terrain allows none, as the
    /// World tab's Camp already checks; otherwise ambushed, which waits for combat, or healed by half, with the camp
    /// scene cued: the first dialogue object in the chunk whose enabling flag is one of the camping record's and holds
    /// while its check flag is set, wherever it is (<c>dialoug_ref_obj_func</c>), or else the Campsite's
    /// (<c>campfire_func</c>). Eight hours pass when the scene ends, as it's a <c>rest</c> script.
    /// </summary>
    public CampOutcome Camp()
    {
        var rules = Game.Camping;
        var at = Player.Sphere.Position;
        var terrain = Chunks.ChunkAt(at) is { } chunk ? Game.Plugins.TryGet<TerrainRecord>(RecordId.Decimal(RecordTypes.Terrain, chunk.Terrain)) : null;
        var factor = terrain?.Camp ?? 0;
        if (factor <= 0)
            return CampOutcome.Refused;
        if (Camping.Ambushed(rules, factor, Game.Party, Game.Flags.Get, Game.Random.World))
        {
            _host.Say("The party is ambushed; battles come with combat (2.5).");
            return CampOutcome.Ambushed;
        }
        Game.Party.CampHeal(rules.Heal, rules.EffectTicks);

        Game.Flags.Set(rules.Checking, true);
        var map = Chunks.MapAt(at);
        var scene = map?.Record.Objects.OfType<DialogueObject>().FirstOrDefault(d =>
            d.Enabled is { } gate && rules.Scenes.Contains(gate.Flag) && Game.Flags.Get(gate.Flag) != gate.Invert);
        Game.Flags.Set(rules.Checking, false);
        RecordId? source = scene is not null ? MapRecord.VoxelId(map!.Record.Id, scene.Index) : null;
        if (scene is null && Game.Plugins.TryGet<MapRecord>(rules.Campsite) is { } campsite
            && campsite.Objects.OfType<DialogueObject>().FirstOrDefault() is { } fire)
        {
            scene = fire;
            source = MapRecord.VoxelId(campsite.Id, fire.Index);
        }
        if (scene is not null && Game.Plugins.TryGet<DialogueRecord>(scene.Script) is { Mode: DialogueMode.Cutscene } script && scene.Stage is { } stage)
            Cue(script, new ScriptStart(scene.RefPointBase, source), stage);
        else
            _host.Say("The party camps, but there's no camp scene to show; eight hours would pass.");
        return CampOutcome.Camped;
    }

    /// <summary>
    /// Asks for a cut-scene (<c>dialoug_func</c>, then <c>funcs0[1]</c>): trek fades out, and it starts once
    /// the screen is black. One at a time.
    /// </summary>
    public void Cue(DialogueRecord script, ScriptStart start, ChunkRef stage)
    {
        if (Cued is not null)
            return;
        Cued = (script, start, stage);
        CutSceneFade.Out(CutSceneFadePerTick);
    }

    /// <summary>
    /// Brings the conversation up to date after a tick or a press: what a script waits for that trek can't
    /// do (a camera, an actor walking or animating) is taken as done, and an ended conversation ends.
    /// </summary>
    public void Refresh()
    {
        if (Conversation is not { } conversation)
            return;
        var runner = conversation.Runner;
        while (runner.Status == ScriptStatus.Waiting && runner.Wait != ScriptWait.Timer)
        {
            _host.Log($"{runner.Script.Id} waits for {runner.Wait}, which a conversation can't show; going on.");
            runner.Resume();
        }
        if (conversation.Ended)
        {
            conversation.End(Wanderers, Camera);
            Conversation = null;
            if (conversation.RebuildsTrek)
                Rebuild();
            _host.ConversationChanged();
            _host.ScriptEnded(conversation.Runner.Script.Id, conversation.Runner.Ending!, conversation.Keeper);
            return;
        }
        _host.ConversationChanged();
    }

    // What using map objects asks of trek.

    void IMapObjectHost.Teleport(LoadedMap from, TeleporterObject teleporter)
    {
        // set_teleport_pointer: the fade starts unless one is under way, and the teleport waits for it.
        if (TeleportFade.Fading)
            return;
        _pending = teleporter;
        TeleportFade.Out(TeleportFadeOutPerTick);
    }

    void IMapObjectHost.StartScript(LoadedMap map, DialogueObject dialogue)
    {
        // One script at a time (dialoug_func).
        if (Conversation is not null)
            return;
        var source = MapRecord.VoxelId(map.Record.Id, dialogue.Index);
        if (Game.Plugins.TryGet<DialogueRecord>(dialogue.Script) is { Mode: DialogueMode.Sign } sign)
        {
            Conversation = Conversation.Show(Game, sign, new ScriptStart(dialogue.RefPointBase, source), _host, Player);
            Refresh();
            return;
        }
        if (Game.Plugins.TryGet<DialogueRecord>(dialogue.Script) is { Mode: DialogueMode.Cutscene } cutscene && dialogue.Stage is { } stage)
        {
            Cue(cutscene, new ScriptStart(dialogue.RefPointBase, source), stage);
            return;
        }
        if (Game.Plugins.TryGet<DialogueRecord>(dialogue.Script) is { Mode: DialogueMode.Backdrop or DialogueMode.Help } backdrop)
        {
            ShowBackdrop(backdrop, new ScriptStart(dialogue.RefPointBase, source));
            return;
        }
        // A tangible dialogue asks every tick, so say it once.
        if (_lastScript == dialogue.Script)
            return;
        _lastScript = dialogue.Script;
        _host.Say($"{dialogue.Name} would start {dialogue.Script}.");
    }

    /// <summary>
    /// Shows a backdrop or the help menu (<c>Backdrop.Start</c>, <c>0x80051904</c> in 1.0): its page over the world,
    /// which stops under it as under the pause menu, and trek is rebuilt once it ends (<see cref="Conversation.RebuildsTrek"/>).
    /// One script at a time.
    /// </summary>
    public void ShowBackdrop(DialogueRecord script, ScriptStart? start = null)
    {
        if (Conversation is not null)
            return;
        Conversation = Conversation.Show(Game, script, start ?? new ScriptStart(), _host, Player);
        Refresh();
    }

    void IMapObjectHost.Talk(LoadedMap map, MonsterPartyObject party)
    {
        if (Conversation is not null || Wanderers.Of(map, party) is not { } wanderer)
            return;
        if (party.Dialogue is null)
        {
            _host.Say($"{MapRecord.VoxelId(map.Record.Id, party.Index)} would fight; battles come with combat (2.5).");
            return;
        }
        // A talker whose script is a cut-scene stages it in the chunk the player is in, from reference
        // point 0 (monsterpary_func), and holds still as trek fades out.
        if (Game.Plugins.TryGet<DialogueRecord>(party.Dialogue.Value) is { Mode: DialogueMode.Cutscene } cutscene)
        {
            if ((party.Flags & PartyFlags.Silent) != 0 || Chunks.ChunkAt(Player.Sphere.Position) is not { } chunk)
                return;
            wanderer.Calmed = true;
            wanderer.Body.Held = true;
            Cue(cutscene, new ScriptStart(0, MapRecord.VoxelId(map.Record.Id, party.Index)), new ChunkRef(Chunks.MapSet.Id, chunk.Row, chunk.Column));
            if (party.Used is { } used)
                Game.Flags.Set(used.Flag, true);
            return;
        }
        Conversation = Conversation.TalkTo(Game, wanderer, _host, Player);
        Refresh();
    }

    void IMapObjectHost.OpenLoot(ItemPile loot, int gold) => _host.OpenLoot(loot, gold);

    void IMapObjectHost.Animate(LoadedMap map, SceneObject actor, int request) => _host.Animate(map, actor, request);

    void IMapObjectHost.Changed(LoadedMap map, SceneObject scene) => _host.Changed(map, scene);

    void IMapObjectHost.Sound(OneShot sound) => Audio.Play(sound);

    void IMapObjectHost.Explode(Vector3 at) => Lights.Dynamic.Explosion(at);
}
