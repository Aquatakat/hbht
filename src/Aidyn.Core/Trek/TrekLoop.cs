using System.Numerics;
using Aidyn.Core.Actors;
using Aidyn.Core.Audio;
using Aidyn.Core.Calendar;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Scenes;
using Aidyn.Core.Scripts;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Trek;

/// <summary>
/// What trek asks of whatever presents it: the runtime, or a test. Where trek's conversations and map objects lead, a battle
/// or a script's shop or training, comes as a <see cref="Core.Transition"/>.
/// </summary>
public interface ITrekHost : IScriptHost, ITransitionHost
{
    /// <summary>The player is in a chunk, every tick: the sky follows it.</summary>
    void Entered(ChunkEntry? chunk);

    /// <summary>The player has come through a teleporter and stands at its arrival point.</summary>
    void Arrived();

    /// <summary>A conversation or sign showing has a new page, or has ended.</summary>
    void ConversationChanged();

    /// <summary>A wanderer's animation, which moves its body by its root motion.</summary>
    IRootMotion Motion(Wanderer wanderer);

    /// <inheritdoc cref="IMapObjectHost.OpenLoot"/>
    void OpenLoot(ItemPile loot, int gold);

    /// <inheritdoc cref="IMapObjectHost.Animate"/>
    void Animate(LoadedMap map, SceneObject actor, int request);

    /// <inheritdoc cref="IMapObjectHost.Changed"/>
    void Changed(LoadedMap map, SceneObject scene);

    /// <summary>
    /// A scene object that goes out once reached, one of Oriana's path lights, went out this tick
    /// (<see cref="ChunkStreamer.PutOut"/>): it's no longer active, and nothing else in its chunk changed. By default
    /// it's told as any other change (<see cref="Changed"/>).
    /// </summary>
    void PutOut(LoadedMap map, SceneObject scene) => Changed(map, scene);

    /// <summary>Notes something for whoever's watching the log.</summary>
    void Log(string text);

    /// <summary>Notes a problem for whoever's watching the log, such as where a script stopped that the original would crash.</summary>
    void Warn(string text);
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
    /// <summary>A battle is cued and the screen is black: combat takes over (<see cref="TrekLoop.CuedBattle"/>).</summary>
    Battle,
}

/// <summary>
/// Trek, the original's free exploration (<c>gametrek.cpp</c>, <c>Trek_GameState</c>), as a loop over the
/// subsystems it shares with the rest of the game: the zone engine's world about the player (<see cref="Zone"/>),
/// the clock, map objects, the follow camera, the player's movement, the wanderers, conversations, and the fades
/// into a teleport or a cut-scene. Each <see cref="Tick"/> steps them in the original's order. What draws trek
/// reads the state here and is told what changed through <see cref="ITrekHost"/>.
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
    /// <summary>Whether trek has just been built again and hasn't ticked, so its fade-in hasn't stepped (<see cref="Resume"/>).</summary>
    private bool _resumed;
    /// <summary>The last script a dialogue object asked to start that trek can't run yet, such as a backdrop.</summary>
    private RecordId? _lastScript;
    /// <summary>The frames scene objects are drawn in, which <see cref="PutOut"/> goes by.</summary>
    private readonly FrameGate _drawFrame = new(Cadence.FrameTicks);
    /// <summary>The main font, which a backdrop's page is laid out in.</summary>
    private TextLayout? _font;

    /// <param name="zone">
    /// The zone engine trek's world is loaded in, about the player. Its rain lands on the solid surfaces every loaded chunk
    /// draws, where the original's find only the player's chunk's collision (<c>Zones.PlayerMap</c>): the user's choices, so
    /// that near a chunk's edge they don't fall through the next one, and they splash on roofs and not on invisible walls.
    /// </param>
    /// <param name="player">The player's body, which <see cref="GameSession.Start"/> sets down on the ground once the chunks are loaded.</param>
    /// <param name="camera">The follow camera, behind him.</param>
    public TrekLoop(GameState game, ITrekHost host, ZoneEngine zone, PlayerMotion player, FollowCamera camera)
    {
        Game = game;
        _host = host;
        Zone = zone;
        Player = player;
        Camera = camera;
        Wanderers = new Wanderers(game.Plugins);
        Use = new MapObjectUse(game, MenuText.From(game.Plugins), this) { World = zone.World, Camera = camera, PlayerRadius = player.Sphere.Radius };
        Hud = new TrekHud(game);
        Box = DialogueBox.For(game.Plugins);
        _playerSounds = game.Plugins.TryGet<EntityRecord>(GameState.Alaron)?.Sounds;
        // Each chunk's wanderers load before its sounds and lights.
        zone.Loaded += (_, map) => Wanderers.Load(map, Chunks.MapSet.CellSize, Chunks.World, Game.Memory, Game.Random.Wander);
        zone.Unloading += (_, map) => Wanderers.Unload(map);
    }

    public GameState Game { get; }

    /// <summary>
    /// The zone engine trek's world is loaded in: the chunks around the player, their sounds and lights, the music, the
    /// weather's rain and its sounds, and bodies' footsteps and voices.
    /// </summary>
    public ZoneEngine Zone { get; }

    /// <summary>The player's sounds: Alaron's, which every body has until it's given its own (<c>Player.Reset</c>).</summary>
    private readonly BodySounds? _playerSounds;

    private readonly BodyAudio _playerAudio = new();

    /// <summary>The chunks around the player, which a teleport to another map set changes.</summary>
    public ChunkStreamer Chunks => Zone.Chunks;

    /// <summary>The loaded chunks' monster parties' bodies.</summary>
    public Wanderers Wanderers { get; }

    /// <summary>Using map objects, and the popups that shows.</summary>
    public MapObjectUse Use { get; }

    public PlayerMotion Player { get; }

    public FollowCamera Camera { get; }

    /// <summary>The player's animation, which moves him by its root motion.</summary>
    public IRootMotion PlayerAnimation { get; set; } = IActorMotion.Still;

    /// <summary>The sundial, the party's portraits, and the compass or the minimap.</summary>
    public TrekHud Hud { get; }

    /// <summary>Whether the HUD is drawn and steps: not while a conversation or sign shows (<c>diaClass->menu</c>).</summary>
    public bool HudShown => Conversation is null;

    /// <summary>The conversation or sign showing, if any.</summary>
    public Conversation? Conversation { get; private set; }

    /// <summary>
    /// The box a conversation or a sign shows its pages in, and whose cursor its presses move (<see cref="Press"/>): laid out
    /// here, and drawn by whoever presents trek, which sizes it to the UI screen (<see cref="DialogueBox.Resize"/>).
    /// </summary>
    public DialogueBox Box { get; }

    /// <summary>
    /// A backdrop's or the help menu's page over the world, from its opening until it has faded out (<c>Backdrop.Start</c>): the
    /// world is held still under it, as under the pause menu, and its presses turn the page (<see cref="Press"/>).
    /// </summary>
    public BackdropPage? Backdrop { get; private set; }

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

    /// <summary>The last places the player stood, which fleeing a battle goes back to.</summary>
    public Breadcrumbs Breadcrumbs { get; } = new();

    /// <summary>Where the player stands, as a breadcrumb keeps it, or null out of the chunks.</summary>
    public Breadcrumb? Standing => Chunks.ChunkAt(Player.Sphere.Position) is { } chunk
        ? new Breadcrumb(Chunks.MapSet.Id, chunk.Row, chunk.Column, Player.Sphere.Position - Chunks.Origin(chunk), Player.Heading)
        : null;

    /// <summary>The battle trek is fading out into, which starts once the screen is black.</summary>
    public BattleSetup? CuedBattle { get; private set; }

    /// <summary>
    /// Whether trek starts again at a named place of its map set rather than simply where the player stood (the original's
    /// saved <c>Zones.Chunk</c>, <c>Zones.SavedPlace</c> at <c>0x800d9092</c>, not −1): only the Goblin Ambush's end puts the
    /// party there (<see cref="Keep"/>), and going through a teleporter leaves it (<c>TeleportPlayer</c>, <c>0x8000bb54</c>).
    /// While it holds, a cut-scene's end chains into the tangible dialogue object at the player's place
    /// (<see cref="GameSession.EndCutScene"/>).
    /// </summary>
    public bool AtPlace { get; private set; }

    /// <summary>
    /// Trek begins, the player set down and the chunks loaded about him, before the first tick: the zone engine hears from
    /// the follow camera, the HUD starts, the chunk's music is asked for and the breadcrumbs are all where he stands.
    /// </summary>
    public void Begin()
    {
        Zone.Camera = Camera;
        Hud.Start();
        Zone.RequestMusic(Chunks.ChunkAt(Player.Sphere.Position));
        ResetBreadcrumbs();
    }

    /// <summary>All the breadcrumbs where the player stands, as trek is built (<c>Zones.ResetBreadcrumbs</c>).</summary>
    private void ResetBreadcrumbs()
    {
        if (Standing is { } here)
            Breadcrumbs.Reset(here);
    }

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
        Zone.RequestMusic(chunk);
        Game.EnterTerrain(chunk.Terrain);
        Hud.Arrive(Game.Terrain(chunk.Terrain)?.SkyDrawn ?? true);
    }

    /// <summary>What's said when a journal entry is added: a literal in the code (<c>0x800ce28c</c> in 1.0).</summary>
    public const string NewJournalEntry = "A new journal entry has been added.";

    /// <summary>
    /// One 60 Hz tick of trek, as the original orders it: the fades, the clock, the map objects the player
    /// touches, the camera, the player, the wanderers' decisions and then their bodies, a conversation's
    /// tick, the trigger triangles he treads on and a trigger triangle's camera, and once a frame what goes out once
    /// reached (<see cref="PutOut"/>). A popup stops only the
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
        if (CuedBattle is not null && !CutSceneFade.Fading)
            return TrekStep.Battle;
        var fading = TeleportFade.Fading || CutSceneFade.Fading;
        // Walking into map objects and onto trigger triangles has no fade term (MapObjects.CanStart, 0x80010d08): only a
        // teleport's fade and trek's fading out into a cut-scene or a battle hold them, so trek's first tick after it's built
        // again can start a cut-scene while the screen is still black, as the Goblin Ambush's end into Oriana's hut does.
        var touching = !TeleportFade.Fading && CutSceneFade.Mode != FadeMode.Out;
        // Bgm.Update waits for a fade to end, so the music changes once the screen is up.
        Zone.Audio.HoldMusic = fading;
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
        Zone.StepWeather(_chunk is null || (Game.Terrain(_chunk.Terrain)?.SkyDrawn ?? true), Zone.Precipitation);
        // HandleZoneEngineFrame keeps where he stands, in trek's zone engine alone.
        if (Standing is { } standing)
            Breadcrumbs.Tick(standing);
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
            if (touching && !talking)
                Use.Touch(centre, at);
            else
                Use.TouchCameras(centre, at);
        }
        var input = fading || Use.Popup is not null || talking ? new PlayerInput(Vector2.Zero) : readInput();
        // Actor::Move: the stick pushed lowers the portraits, but not while it orbits the camera.
        if (!input.Locked && input.Pushed)
            Hud.Raise(false);
        var before = motion.Sphere.Position;
        if (Conversation is { With: not null } framing)
            framing.Frame(Camera, world);
        else
            Camera.Step(world, motion.Sphere.Position, motion.Heading);
        Zone.Listen();
        var keys = Zone.Keys;
        StepPlayer(world, motion, input, Camera, keys.Sounding(PlayerAnimation, _playerAudio, _playerSounds, motion.Sphere));
        // WanderTick, which a popup, a conversation, the menu and a fade stop.
        if (playing && Use.Popup is null && !talking)
        {
            foreach (var w in Wanderers.Tick(world, motion.Sphere.Position, motion.Request, Game.Party, Game.Random.Wander,
                Game.Weather?.FogTime ?? 0))
                CueBattle(Battle(BattleSource.Wanderer, w.Map, w.Party, ambush: true) with { AniByte = AniByte(motion.Request) });
        }
        // ProcessPlayers: the player is the first body, then the rest move and push.
        Nearest = Wanderers.Step(world, motion.Sphere, before, Chunks.MapAt(motion.Sphere.Position)?.Key, Camera.Aim,
            w => keys.Sounding(_host.Motion(w), w.Audio, w.Sounds, w.Body.Sphere));
        if (Conversation is { } conversation && conversation.Tick())
            Refresh();
        foreach (var (part, target) in motion.Sphere.Trod)
        {
            if (Chunks.Map(part) is not { } map)
                continue;
            if (touching && !talking)
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
        // RenderVoxelScenes, as the frame is drawn.
        if (_drawFrame.Tick())
            PutOut();
        return TrekStep.Ran;
    }

    /// <summary>
    /// Puts out what goes out once reached, Oriana's path lights, once a frame (<see cref="Cadence.FrameTicks"/>), as
    /// the original does as it draws the scene objects (<c>RenderVoxelScenes</c>), from the player and the follow camera
    /// (<see cref="ChunkStreamer.PutOut"/>), and tells the host of each. It's game state, which a save keeps, so it goes by
    /// the original's cone of view (<see cref="ViewCone.Original"/>), of a 4:3 screen, whatever the window's shape, and not
    /// by the view the runtime draws from: a wider window doesn't put a light out sooner.
    /// </summary>
    private void PutOut()
    {
        var view = SceneView.Aimed(Player.Sphere.Position, Camera.Position, Camera.Aim, ViewCone.Original);
        foreach (var (map, scene) in Chunks.PutOut(view))
            _host.PutOut(map, scene);
    }

    /// <summary>
    /// The player's step in trek (<c>Actor::Move</c>): first what the controls ask of the trek camera, R held
    /// orbiting it about him and otherwise following, and his standing still anchoring it; then he moves, up on the
    /// stick being the way the camera looks.
    /// </summary>
    public static void StepPlayer(CollisionWorld world, PlayerMotion player, PlayerInput input, FollowCamera camera, IRootMotion animation)
    {
        if (input.Orbit)
            camera.Orbit(player.Sphere.Position, input.OrbitStick, moving: input.Free && input.Pushed);
        else
            camera.Follow();
        if (!input.Locked && !input.Pushed)
            camera.Anchor(player.Sphere.Position);
        player.Step(world, input.Move, camera.LookXZ, animation);
    }

    /// <summary>
    /// The map's sounds follow the player and the camera, as in trek, and in the pause menu, which steps them too with
    /// everything held still.
    /// </summary>
    public void StepSounds() => Zone.StepSounds(Player.Sphere.Position);

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
        // Trek's first frame after it's built again is drawn black: InitGameTrek starts the fade-in after the frame's
        // ScreenFade.Step (0x8001f330), which raises it from the next.
        if (_resumed)
            _resumed = false;
        else
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
            _host.Transition(new Transition.Say($"{teleporter.Name} leads to a chunk no plugin has."));
            return;
        }
        Game.Memory.Age();
        AtPlace = false;
        if (arrival.Set.Id != Chunks.MapSet.Id)
        {
            Chunks.Change(arrival.Set);
            Chunks.World.Clear();
            Chunks.Visible?.Clear();
            Wanderers.Clear();
            // The zone engine starts again, and its dynamic lights with it (FreeZoneEngine, DynamicLights.FreeAll).
            Zone.Lights.Dynamic.Clear();
        }
        var origin = Chunks.Origin(arrival.Chunk);
        var point = origin + arrival.Position;
        Chunks.Update(point);
        Use.ClearCameras();
        // The weather's emitters are forgotten, then every emitter still spawning is killed, theirs among them, so the
        // rain starts again about the player from the next frame, asking for the rain sky again; its loop plays on.
        Game.Weather?.Forget(Zone.Precipitation);
        Zone.Particles?.KillActive();
        var motion = Player;
        var from = motion.Sphere.Position;
        // Set down near the arrival's height, its sphere's bottom there, or failing that from on high, twice.
        var radius = motion.Sphere.Radius;
        var (ground, grounded) = Body.SetDown(Chunks.World, point, radius, Game.Random.World, near: point.Y - radius);
        if (!grounded)
            ground = Body.SetDown(Chunks.World, ground, radius, Game.Random.World).Position;
        motion.Sphere.Position = ground;
        motion.Sphere.Velocity = Vector3.Zero;
        if (arrival.Heading is { } heading)
            motion.Heading = heading;
        _centre = (arrival.Chunk.Row, arrival.Chunk.Column);
        Enter(arrival.Chunk);
        var camera = arrival.Camera is { } local ? origin + local : (Vector3?)null;
        Camera.Arrive(from, motion.Sphere.Position, motion.Heading, FollowCamera.ModeFor(Game.Plugins, arrival.Chunk), camera);
        _host.Arrived();
    }

    /// <summary>B in trek: raises or lowers the portraits, but not through a fade, a popup or a conversation (<c>Actor::Move</c>). Whether it did.</summary>
    public bool PressB()
    {
        if (TeleportFade.Fading || CutSceneFade.Fading || Cued is not null || Use.Popup is not null || Conversation is not null)
            return false;
        Hud.PressB();
        return true;
    }

    /// <summary>
    /// A press in trek, to what's up, highest first: a backdrop's page, a map object's popup, a conversation, else trek itself,
    /// where Start opens the pause menu, A uses the map object the player reaches (<c>obj_ref_func</c>) and B raises or lowers
    /// the portraits. Trek's input is dead while it fades into or out of a cut-scene (<see cref="Cued"/>, <see cref="CutSceneFade"/>);
    /// Start waits for a teleport to end, and A for its fade. The session opens the menu (<see cref="PressOutcome.MenuOpened"/>).
    /// </summary>
    /// <param name="frozen">Whether the player and the world stand still, as while the runtime's free camera flies: then A and B do nothing.</param>
    public PressOutcome Press(MenuAction action, bool frozen = false)
    {
        if (Backdrop is { } page)
            return Browse(page, action);
        if (Use.Popup is not null)
            return Use.Press(action) ? PressOutcome.Taken : PressOutcome.Ignored;
        if (Conversation is { } conversation)
            return Converse(conversation, action);
        var dead = Cued is not null || CutSceneFade.Fading;
        switch (action)
        {
            case MenuAction.Start:
                return dead || Teleporting ? PressOutcome.Ignored : PressOutcome.MenuOpened;
            case MenuAction.Accept:
                if (dead || frozen || TeleportFade.Fading)
                    return PressOutcome.Ignored;
                return UseNearest() ? PressOutcome.Used : PressOutcome.Ignored;
            case MenuAction.Back:
                return !frozen && PressB() ? PressOutcome.Taken : PressOutcome.Ignored;
            default:
                return PressOutcome.Ignored;
        }
    }

    /// <summary>
    /// The buttons in a conversation (<c>run_dialougemode_funcs2</c>): A moves it on, bringing up the choices behind a line that
    /// showed alone or taking the cursor's choice (<see cref="DialogueBox.Accept"/>), and up and down move the cursor. Start does
    /// nothing. Nothing reaches it while the script runs or waits.
    /// </summary>
    private PressOutcome Converse(Conversation conversation, MenuAction action)
    {
        var runner = conversation.Runner;
        if (runner.Status != ScriptStatus.Showing || runner.Page is null)
            return PressOutcome.Ignored;
        switch (action)
        {
            case MenuAction.Accept:
                if (Box.Accept(runner, conversation.Press) != DialogueAccept.ShowedChoices)
                    Refresh();
                else
                    _host.ConversationChanged();
                return PressOutcome.Taken;
            case MenuAction.Up or MenuAction.Down:
                Box.MoveCursor(action.Vertical());
                _host.ConversationChanged();
                return PressOutcome.Taken;
            default:
                return PressOutcome.Ignored;
        }
    }

    /// <summary>
    /// The buttons on a backdrop's or the help menu's page: A takes the cursor's choice, or with none moves on, and up and down
    /// move the cursor. In the help menu, B takes the last choice, Exit Help or back, or with none moves on as A does: only a
    /// help script's page gets a B (<c>Dialogue.ShowNode</c>, 0x80047714, by its start mode, 4) and it returns the last row,
    /// else the text, which carries the node's link (<c>Dialogue.OnBHelp</c>, 0x80048354). A page fading out takes nothing.
    /// </summary>
    private PressOutcome Browse(BackdropPage page, MenuAction action)
    {
        if (page.Closing || Conversation?.Runner is not { Status: ScriptStatus.Showing, Page: { } shown } runner)
            return PressOutcome.Ignored;
        var back = action == MenuAction.Back && runner.Script.Mode == DialogueMode.Help;
        if (action == MenuAction.Accept || back)
        {
            DialogueBox.Advance(runner, back ? shown.Choices.Count - 1 : page.Highlight);
            Refresh();
            return PressOutcome.Taken;
        }
        if (action is MenuAction.Up or MenuAction.Down)
        {
            page.MoveCursor(action.Vertical());
            _host.ConversationChanged();
            return PressOutcome.Taken;
        }
        return PressOutcome.Ignored;
    }

    /// <summary>
    /// A tick of the backdrop's page, whatever runs over it: its fade and its cursor's pulse; once it has faded out after its
    /// script ended, it's gone and the game goes on.
    /// </summary>
    public void StepBackdrop()
    {
        if (Backdrop is not { } page)
            return;
        page.Step();
        if (page.Closed)
            Backdrop = null;
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
    /// its host's to play on its own stage (<see cref="ZoneEngine.Stage"/>).
    /// </summary>
    public (DialogueRecord Script, ScriptStart Start, ChunkRef Stage) BeginCutScene()
    {
        var cued = Cued ?? throw new InvalidOperationException("No cut-scene is cued.");
        Cued = null;
        // The stage is built as trek's world is (InitZoneEngine), and the weather drawn carries on into it.
        Free();
        return cued;
    }

    /// <summary>
    /// Trek's world goes (<c>clear_HUD_elements</c>): the zone engine's (<see cref="ZoneEngine.Free"/>), its chunks with
    /// their collision, sounds and lights, and the rain's emitters, which the weather forgets; then the wanderers and the
    /// cameras holding the view.
    /// </summary>
    /// <remarks>
    /// The weather drawn carries on, the user's choice: what's falling, how hard, the fog, the rain sky and the rain loop
    /// go on through the stage or the world built again (<see cref="Weather"/>), where the original's
    /// <c>FreeZoneEngine</c> stops the loop (<c>Weather.Free</c>, <c>0x8001e030</c>) and its <c>InitZoneEngine</c>
    /// clears the rest (<c>Weather.Init</c>, <c>0x8001e010</c>). A battle's arena is a stage of trek's zone
    /// engine as a cut-scene's is, with trek's world freed here and built again through <see cref="Rebuild"/>, and so
    /// carries the weather the same way.
    /// </remarks>
    public void Free()
    {
        Zone.Free();
        Wanderers.Clear();
        Nearest = default;
        Use.ClearCameras();
    }

    /// <summary>
    /// Loads the map set's chunks and their wanderers again where the player stands, as trek starts again
    /// after a cut-scene, a backdrop or the help menu (<c>initGameTrek</c>): every map object is gated by
    /// the flags as they are now, and the wanderers start at their home nodes. The weather drawn carries on
    /// (<see cref="Free"/>).
    /// </summary>
    public void Rebuild()
    {
        Free();
        Chunks.Update(Player.Sphere.Position);
        ResetBreadcrumbs();
    }

    /// <summary>
    /// Where trek starts again becomes <paramref name="position"/> in <paramref name="place"/>'s chunk, facing
    /// <paramref name="heading"/>, in another map set if need be: the kept place rewritten, as the Goblin Ambush's end takes the
    /// party to Oriana's hut (<c>Combat.Exit</c>'s <c>Zones.SavedPosition</c>). Trek's world must be freed, as it is under a
    /// battle; <see cref="Resume"/> builds it there.
    /// </summary>
    public void Keep(ChunkRef place, Vector3 position, Vector2 heading)
    {
        if (Game.Plugins.TryGet<MapSetRecord>(place.MapSet) is not { } set
            || set.Chunk(place.Row, place.Column) is not { } chunk)
        {
            _host.Transition(new Transition.Say($"{place.MapSet} has no chunk at row {place.Row}, column {place.Column}."));
            return;
        }
        if (set.Id != Chunks.MapSet.Id)
        {
            Chunks.Change(set);
            Chunks.World.Clear();
            Chunks.Visible?.Clear();
            Zone.Lights.Dynamic.Clear();
        }
        Player.Sphere.Position = Chunks.Origin(chunk) + position;
        Player.Sphere.Velocity = Vector3.Zero;
        Player.Heading = heading;
        _centre = (chunk.Row, chunk.Column);
        AtPlace = true;
    }

    /// <summary>
    /// Trek comes back after a cut-scene or a battle: its world where the player stood, the follow camera aimed at
    /// him and swinging round quickly, and the screen fading in. Having <paramref name="fled"/> a battle, he stands at the
    /// breadcrumb in the map set farthest from where it began, facing as he did there (<c>InitZoneEngine</c>'s restoring,
    /// with <c>Zones.Fled</c> set), not set down, as the original puts him exactly where it kept him.
    /// </summary>
    public void Resume(bool fled = false)
    {
        if (fled && Standing is { } start && Breadcrumbs.Find(start, farthest: true) is { } crumb
            && Chunks.MapSet.Chunk(crumb.Row, crumb.Column) is { } chunk)
        {
            Player.Sphere.Position = Chunks.Origin(chunk) + crumb.Local;
            Player.Heading = crumb.Heading;
        }
        Rebuild();
        var at = Player.Sphere.Position;
        Enter(Chunks.ChunkAt(at));
        // InitZoneEngine asks for the music again, which the cut-scene's stage may have changed.
        Zone.RequestMusic(Chunks.ChunkAt(at));
        Camera.AimAt(at);
        Camera.Snap = 1;
        Hud.Start();
        CutSceneFade.In(CutSceneFadePerTick);
        _resumed = true;
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
        var terrain = Chunks.ChunkAt(at) is { } chunk ? TerrainRecord.Find(Game.Plugins, chunk.Terrain) : null;
        var factor = terrain?.Camp ?? 0;
        if (factor <= 0)
            return CampOutcome.Refused;
        if (Camping.Ambushed(rules, factor, Game.Party, Game.Flags.IsSet, Game.Random.World))
        {
            var (place, terrainIndex) = Here();
            CueBattle(BattleSetup.CampAmbush(place, terrainIndex, Game.Plugins, Game.Party));
            return CampOutcome.Ambushed;
        }
        Game.Party.CampHeal(rules.Heal, rules.EffectTicks, Game.Random.Combat);

        Game.Flags.TrySet(rules.Checking, true);
        var map = Chunks.MapAt(at);
        var scene = map?.Record.Objects.OfType<DialogueObject>().FirstOrDefault(d =>
            d.Enabled is { } gate && rules.Scenes.Contains(gate.Flag) && Game.Flags.Holds(gate));
        Game.Flags.TrySet(rules.Checking, false);
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
            _host.Transition(new Transition.Say("The party camps, but there's no camp scene to show; eight hours would pass."));
        return CampOutcome.Camped;
    }

    /// <summary>
    /// Asks for a cut-scene (<c>dialoug_func</c>, then <c>funcs0[1]</c>): trek fades out, and it starts once
    /// the screen is black. One at a time, and none once a battle is cued.
    /// </summary>
    public void Cue(DialogueRecord script, ScriptStart start, ChunkRef stage)
    {
        if (Cued is not null || CuedBattle is not null)
            return;
        Cued = (script, start, stage);
        CutSceneFade.Out(CutSceneFadePerTick);
    }

    /// <summary>
    /// Asks for a battle (<c>TrekLoop.QueueState(2)</c>, <c>0x8001FF04</c>): trek fades out as into a cut-scene, and
    /// combat starts once the screen is black (<see cref="TrekStep.Battle"/>). One at a time, and none once a cut-scene is
    /// cued.
    /// </summary>
    public void CueBattle(BattleSetup setup)
    {
        if (Cued is not null || CuedBattle is not null)
            return;
        CuedBattle = setup;
        CutSceneFade.Out(CutSceneFadePerTick);
    }

    /// <summary>
    /// The cued battle starts, once <see cref="Tick"/> says so: trek's world goes, and the battle is its host's to play on
    /// its arena (<see cref="ZoneEngine.Stage"/>).
    /// </summary>
    public BattleSetup BeginBattle()
    {
        var setup = CuedBattle ?? throw new InvalidOperationException("No battle is cued.");
        CuedBattle = null;
        Free();
        // TrekLoop.Free (0x800211A4) frees the zone engine with FreeZoneEngine(0) for a battle, which cuts the music (Bgm.Stop(1)):
        // the arena's starts once its fade in has ended, with silence till then.
        Zone.Audio.StopMusic();
        return setup;
    }

    /// <summary>
    /// Carries out what a script's end leads to (<see cref="Transition.CarryOut"/>), but a battle, which trek cues
    /// (<see cref="CueBattle"/>) rather than hands on.
    /// </summary>
    public void CarryOut(IEnumerable<Transition> next) => Transition.CarryOut(Game, new Carrier(this), next);

    /// <summary>Hands the host every transition but a battle, which trek cues.</summary>
    private sealed class Carrier(TrekLoop trek) : ITransitionHost
    {
        public void Transition(Transition next)
        {
            if (next is Transition.StartBattle battle)
                trek.CueBattle(battle.Setup);
            else
                trek._host.Transition(next);
        }
    }

    /// <summary>
    /// Brings the conversation up to date after a tick or a press: what a script waits for that trek can't
    /// do (a camera, an actor walking or animating) is taken as done, its page is shown, in the box or on a
    /// backdrop's page (<see cref="Box"/>, <see cref="Backdrop"/>), and an ended conversation ends, a backdrop's page fading out.
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
            Backdrop?.Close();
            if (conversation.RebuildsTrek)
                Rebuild();
            _host.ConversationChanged();
            var ending = conversation.Runner.Ending!;
            if (ending.Fault is { } fault)
                _host.Warn($"{conversation.Runner.Script.Id}: {fault}");
            var talker = conversation.With is { } with ? (MapRecord.VoxelId(with.Map.Record.Id, with.Party.Index), with.Party) : default((RecordId, MonsterPartyObject)?);
            CarryOut(Transition.AfterScript(Game, ending, conversation.Keeper, Here(), talker));
            return;
        }
        if (conversation.ShowsBackdrop)
        {
            // Backdrop.Start: the page opens over the world, in its picture's style, as the script starts; a page still fading out is replaced.
            if (Backdrop is null or { Closing: true })
                Backdrop = new BackdropPage(_font ??= new TextLayout(Game.Plugins.Get<FontRecord>(RecordId.Decimal(RecordTypes.Font, 0))),
                    BackdropStyle.For(runner.Script.Backdrop));
            if (runner is { Status: ScriptStatus.Showing, Page: { } page })
                Backdrop.Show(page);
        }
        else if (runner is { Status: ScriptStatus.Showing, Page: { } page })
        {
            Box.Show(page);
        }
        _host.ConversationChanged();
    }

    /// <summary>The chunk the player is in, and its terrain, where a battle in trek starts.</summary>
    public (ChunkRef? Chunk, int? Terrain) Here() =>
        Chunks.ChunkAt(Player.Sphere.Position) is { } chunk ? (new ChunkRef(Chunks.MapSet.Id, chunk.Row, chunk.Column), chunk.Terrain) : (null, null);

    /// <summary>
    /// What the player was doing as a wanderer caught him, for the enemies' spawn set (<c>EncounterDat.aniByte</c>, which
    /// <c>Wanderer.Tick</c> notes as it notices him): 3 running, 2 walking, 1 sneaking, else 0.
    /// </summary>
    public static int AniByte(int request) => request switch
    {
        Body.Run => 3,
        Body.Walk => 2,
        Body.Sneak => 1,
        _ => 0,
    };

    /// <summary>A battle with a monster party in a loaded map, where the player stands.</summary>
    private BattleSetup Battle(BattleSource source, LoadedMap map, MonsterPartyObject party, bool ambush)
    {
        var (place, terrain) = Here();
        return BattleSetup.WithParty(source, MapRecord.VoxelId(map.Record.Id, party.Index), party, place, terrain, ambush);
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
        _host.Transition(new Transition.Say($"{dialogue.Name} would start {dialogue.Script}."));
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
            CueBattle(Battle(BattleSource.MonsterParty, map, party, ambush: false));
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
                Game.Flags.TrySet(used.Flag, true);
            return;
        }
        Conversation = Conversation.TalkTo(Game, wanderer, _host, Player);
        Refresh();
    }

    void IMapObjectHost.OpenLoot(ItemPile loot, int gold) => _host.OpenLoot(loot, gold);

    void IMapObjectHost.Animate(LoadedMap map, SceneObject actor, int request) => _host.Animate(map, actor, request);

    void IMapObjectHost.Changed(LoadedMap map, SceneObject scene)
    {
        Chunks.Refresh(map);
        _host.Changed(map, scene);
    }

    void IMapObjectHost.Sound(OneShot sound) => Zone.Audio.Play(sound);

    void IMapObjectHost.Explode(Vector3 at) => Zone.Lights.Dynamic.Explosion(at);
}
