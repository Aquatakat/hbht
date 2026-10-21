using System.Numerics;
using Aidyn.Core.Actors;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.Cinematics;
using Aidyn.Core.Combat;
using Aidyn.Core.Events;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Scripts;
using Aidyn.Core.Sky;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Plugins.Saves;

namespace Aidyn.Core;

/// <summary>
/// What runs, by precedence, lowest first: the one highest that's up takes the input, and decides whether trek steps
/// (<see cref="GameSession.Mode"/>).
/// </summary>
public enum GameMode
{
    /// <summary>Free exploration (<see cref="TrekLoop"/>).</summary>
    Trek,

    /// <summary>A battle on its arena, in place of trek's world (game mode 2).</summary>
    Combat,

    /// <summary>A cut-scene on its stage, in place of trek's world.</summary>
    CutScene,

    /// <summary>A conversation or sign in trek's box: trek runs under it.</summary>
    Conversation,

    /// <summary>A map object's popup: trek runs under it, but for the clock.</summary>
    Popup,

    /// <summary>A backdrop's or the help menu's page, from its opening until it has faded out: the world is held still.</summary>
    Backdrop,

    /// <summary>The pause menu, or its barter or training state: the world is held still.</summary>
    Menu,

    /// <summary>The credits: the world is held still, its sounds going on silently.</summary>
    Credits,

    /// <summary>A cinematic, over everything.</summary>
    Cinematic,
}

/// <summary>What a tick of the game came to, for whoever draws it.</summary>
public enum SessionStep
{
    /// <summary>The pause menu or a backdrop's page holds the world still.</summary>
    Held,

    /// <summary>A cinematic is playing: its view steps it, and says when it ends (<see cref="GameSession.CinematicEnded"/>).</summary>
    Cinematic,

    /// <summary>The credits are rolling: their view steps them, and says when they end (<see cref="GameSession.CreditsEnded"/>).</summary>
    Credits,

    /// <summary>A cut-scene ticked.</summary>
    CutScene,

    /// <summary>A cut-scene ticked, its screen has faded out, and it ends once drawn (<see cref="GameSession.EndCutScene"/>).</summary>
    CutSceneOver,

    /// <summary>Trek ran.</summary>
    Trek,

    /// <summary>Time passed in trek, but the player and the world didn't move: the free camera flies.</summary>
    Frozen,

    /// <summary>Trek has faded out into a cut-scene, which begins once drawn (<see cref="GameSession.BeginCutScene"/>).</summary>
    CutSceneCued,

    /// <summary>Trek has faded out into a battle, which begins once drawn (<see cref="GameSession.BeginBattle"/>).</summary>
    BattleCued,

    /// <summary>A battle ticked.</summary>
    Combat,

    /// <summary>A battle ticked, its screen has faded out, and it ends once drawn (<see cref="GameSession.EndBattle"/>).</summary>
    CombatOver,

    /// <summary>
    /// The pause menu rolled up this tick and how it ended was carried out (<see cref="GameSession.MenuClosed"/>): the map was
    /// gated again, and whoever draws the menu hides it.
    /// </summary>
    MenuClosed,
}

/// <summary>
/// A game being played: the game's state, trek with the zone engine its world is loaded in, and what runs over or in
/// place of it, a cut-scene, the pause menu, a backdrop's page, the credits or a cinematic, as the original's game modes
/// and trek's states are (<c>AppProc</c>'s modes, <c>TrekLoop.Frame</c>, <c>0x8001ffd8</c>). Each <see cref="Tick"/> counts the
/// tick and steps the mode that runs. Every way a mode, a menu or a script ends becomes a <see cref="Transition"/>, decided
/// and carried out here and handed to the host, so the runtime switches its views on <see cref="Mode"/> and the
/// transitions and keeps no rules of its own.
/// </summary>
/// <remarks>
/// The pause menu (<see cref="Menu"/>), a backdrop's page, a cinematic and the credits are stepped here
/// (<see cref="Backdrop"/>, <see cref="Cinematic"/>, <see cref="Credits"/>), and the runtime draws them; the menu's ending is
/// carried out as its step returns it (<see cref="MenuClosed"/>). The player's presses come in as <see cref="MenuAction"/>s
/// (<see cref="Press"/>), and the held directions repeat here (<see cref="Stick"/>). Combat is a mode here too, on a stage of
/// trek's zone engine as a cut-scene is (<see cref="BeginBattle"/>).
/// </remarks>
public sealed class GameSession
{
    private readonly ITrekHost _host;
    private Triggers? _triggers;
    /// <summary>The pause menu reopening once trek has faded in, on the Theatre's page or as it was (trek's state 8), if it's to.</summary>
    private Transition.ReopenMenu? _reopen;
    /// <summary>The settings the session plays by: the last tick's, or those it started with (<see cref="Apply"/>).</summary>
    private SessionSettings _settings;
    /// <summary>Whether the last tick froze trek, as while the runtime's free camera flies: then A and B in trek do nothing.</summary>
    private bool _frozen;

    private GameSession(GameState game, ITrekHost host, ZoneEngine zone, PlayerMotion player, FollowCamera camera, Settings? settings,
        IPauseMenuHost? menuHost, string? savesFolder, SessionSettings? playing)
    {
        Game = game;
        _host = host;
        Trek = new TrekLoop(game, new Relay(this, host), zone, player, camera);
        zone.Sky.Warn = host.Warn;
        Menu = new PauseMenu(game, settings ?? new Settings(), savesFolder ?? DataPaths.Saves, Where, menuHost ?? NoMenuHost.Instance, Stick);
        _settings = playing ?? SessionSettings.Original;
        Apply();
    }

    /// <summary>
    /// Starts a game where <paramref name="start"/> says (<c>Title.Leave</c>, then <c>initGameTrek</c>): the player's body is
    /// made at its point, the chunk is entered, so that its terrain's flags are written before the chunks' objects are gated
    /// by them, the 3×3 loads about him, he's set down on the ground if he's to be, the follow camera takes the mode the
    /// chunk's terrain chooses, arriving as through a teleporter in an interior, and trek begins (<see cref="TrekLoop.Begin"/>).
    /// Whoever presents the game then builds its views from <see cref="TrekLoop.Player"/> and <see cref="TrekLoop.Camera"/>.
    /// </summary>
    /// <param name="host">What presents the game: trek's views and the transitions.</param>
    /// <param name="zone">The zone engine trek's world is loaded in (<see cref="TrekLoop.Zone"/>), on the start's map set.</param>
    /// <param name="settings">
    /// The player's settings, which the pause menu's Options page changes; a default set if none. The session doesn't play by
    /// them: each tick is handed what it plays by (<see cref="Tick"/>).
    /// </param>
    /// <param name="menuHost">What plays the pause menu's sounds, steps its scroll and writes saves; nothing does if none.</param>
    /// <param name="savesFolder">Where the save and load screens list saves: the data folder's by default.</param>
    /// <param name="playing">What the session plays by until its first tick: the original's if none.</param>
    public static GameSession Start(GameState game, ITrekHost host, ZoneEngine zone, GameStart start, Settings? settings = null,
        IPauseMenuHost? menuHost = null, string? savesFolder = null, SessionSettings? playing = null)
    {
        var chunks = zone.Chunks;
        if (chunks.MapSet.Id != start.Set.Id)
            throw new ArgumentException($"The zone engine is on {chunks.MapSet.Label}, not {start.Set.Label}.", nameof(zone));
        var chunk = chunks.Find(start.Chunk) ?? throw new KeyNotFoundException($"{start.Set.Label} has no chunk {start.Chunk}.");
        var spawn = chunks.Origin(chunk) + start.Local;
        var look = start.Heading == default ? GameStart.Facing(0) : Vector2.Normalize(start.Heading);
        var player = new PlayerMotion(spawn, look);
        var camera = new FollowCamera(spawn, look);
        var session = new GameSession(game, host, zone, player, camera, settings, menuHost, savesFolder, playing);
        var trek = session.Trek;
        trek.Enter(chunk);
        chunks.Update(spawn);
        if (start.SetDown)
        {
            // The original makes the player's body at a new game's height, 10, and sets it down from on high. This puts him
            // on the highest ground below 1000, so a start anywhere works.
            var radius = player.Sphere.Radius;
            var above = spawn with { Y = 1000 };
            if (SpherePhysics.Sweep(zone.World, above, above - new Vector3(0, 2000, 0), radius, false, out var ground, out var normal))
            {
                player.Sphere.Position = ground + normal * radius;
                camera.Place(player.Sphere.Position, look);
            }
        }
        // A save loaded from the game's menu arrives as through a teleporter, which chooses the mode.
        var mode = start.Camera ?? FollowCamera.ModeFor(game.Plugins, chunk);
        if (mode == CameraMode.Interior)
            camera.Arrive(player.Sphere.Position, player.Sphere.Position, look, mode, null);
        trek.Begin();
        return session;
    }

    public GameState Game { get; }

    public TrekLoop Trek { get; }

    /// <summary>The zone engine trek's world is loaded in, which a cut-scene's stage is made from.</summary>
    public ZoneEngine Zone => Trek.Zone;

    /// <summary>
    /// The sky, trek's and every stage's (<see cref="ZoneEngine.Sky"/>): stepped after trek, a cut-scene or a battle ticks,
    /// its stars twinkling every tick, and chosen again as each world is built.
    /// </summary>
    public SkyDirector Sky => Zone.Sky;

    /// <summary>The cut-scene playing, in place of trek's world.</summary>
    public CutScene? Scene { get; private set; }

    /// <summary>The cut-scene's stage: its own zone engine (<see cref="ZoneEngine.Stage"/>).</summary>
    public ZoneEngine? Stage { get; private set; }

    /// <summary>The battle playing, in place of trek's world (<see cref="BeginBattle"/>).</summary>
    public Battle? Battle { get; private set; }

    /// <summary>The battle's arena: a zone engine of its own on the Battle arenas (<see cref="ZoneEngine.Stage"/>).</summary>
    public ZoneEngine? Arena { get; private set; }

    /// <summary>
    /// The pause menu: its tabs, windows and presses (<see cref="Ui.PauseMenu"/>). It opens with the transition that opens it,
    /// Start's (<see cref="OpenMenu"/>), a container's loot, a script's shop or training, a battle's pause, a win's post-battle
    /// windows or the menu reopening after the credits or the Theatre (<see cref="Send"/>), is stepped each tick, and closes
    /// itself as its scroll rolls up, its ending carried out here (<see cref="MenuClosed"/>). Whoever draws it reads its state.
    /// </summary>
    public PauseMenu Menu { get; }

    /// <summary>Whether the pause menu is open, from the transition that opens it until it has rolled up.</summary>
    public bool MenuOpen => Menu.IsOpen;

    /// <summary>
    /// The stick's directions held, which repeat in the menus, popups, conversations, cut-scenes and combat's held menus
    /// (<see cref="StickRepeat"/>): whoever reads the controls presses and releases them here, and each tick repeats what's due.
    /// </summary>
    public StickRepeat Stick { get; } = new();

    /// <summary>
    /// The pause menu's scroll actor's clock, which its opening and closing go by (<see cref="Ui.PauseMenu.Step"/>): the frame
    /// it's at in its state, from 1, and the state's length. Whoever draws the scroll sets it; without one, past any state's end,
    /// so the scroll's animations take no time.
    /// </summary>
    public Func<(int Frame, int Length)> ScrollClock { get; set; } = () => (1000, 1);

    /// <summary>
    /// What the battle just won gave, while its post-battle windows are up or about to be (game mode 6,
    /// <see cref="Transition.PostBattle"/>): the world is held, and trek is built again as they close.
    /// </summary>
    public BattleSpoils? PostBattle { get; private set; }

    /// <summary>
    /// A backdrop's or the help menu's page, from its opening until it has faded out (<see cref="TrekLoop.Backdrop"/>): made as
    /// trek's conversation shows one, shown each node, taking the presses (<see cref="TrekLoop.Press"/>), closed as the
    /// conversation ends, and stepped each tick. Whoever presents the game draws it.
    /// </summary>
    public BackdropPage? Backdrop => Trek.Backdrop;

    /// <summary>Whether a backdrop's or the help menu's page shows, or is still fading out.</summary>
    public bool BackdropShowing => Backdrop is not null;

    private TextLayout? _font;

    /// <summary>The main font, which a backdrop's page is laid out in.</summary>
    private TextLayout Font => _font ??= new TextLayout(Game.Plugins.Get<FontRecord>(RecordId.Decimal(RecordTypes.Font, 0)));

    /// <summary>The credits rolling (<see cref="Transition.PlayCredits"/>), stepped each tick until they end (<see cref="CreditsEnded"/>).</summary>
    public CreditsShowing? Credits { get; private set; }

    /// <summary>Whether the credits are rolling.</summary>
    public bool CreditsRolling => Credits is not null;

    /// <summary>Whether the cinematic stands still where it is, not stepped, for a screenshot.</summary>
    public bool CinematicHeld { get; set; }

    /// <summary>The installed plugins' triggers, which the session fires its events on (roadmap 2.10).</summary>
    public Triggers Triggers => _triggers ??= LoadTriggers();

    /// <summary>Loads the triggers, and notes what's wrong with them for whoever's watching the log, as the game's own problems are noted.</summary>
    private Triggers LoadTriggers()
    {
        var triggers = new Triggers(Game.Plugins);
        foreach (var problem in triggers.Problems)
            _host.Warn(problem);
        return triggers;
    }

    /// <summary>The cinematic playing (<see cref="Transition.PlayCinematic"/>), stepped each tick until it ends (<see cref="CinematicEnded"/>).</summary>
    public CinematicShowing? Cinematic { get; private set; }

    /// <summary>
    /// What runs, the highest that's up: a cinematic, the credits, the pause menu, a backdrop's page, a popup, a
    /// conversation, a cut-scene, a battle, else trek.
    /// </summary>
    public GameMode Mode =>
        Cinematic is not null ? GameMode.Cinematic
        : CreditsRolling ? GameMode.Credits
        : MenuOpen || PostBattle is not null ? GameMode.Menu
        : BackdropShowing ? GameMode.Backdrop
        : Trek.Use is { Popup: not null } ? GameMode.Popup
        : Trek.Conversation is not null ? GameMode.Conversation
        : Scene is not null ? GameMode.CutScene
        : Battle is not null ? GameMode.Combat
        : GameMode.Trek;

    /// <summary>Whether the world is held still: under the pause menu, a backdrop's page, the credits or a cinematic.</summary>
    public bool Held => Mode is GameMode.Menu or GameMode.Backdrop or GameMode.Credits or GameMode.Cinematic;

    /// <summary>Whether trek steps this tick: nothing holds the world and no cut-scene or battle plays in its place.</summary>
    public bool TrekRuns => !Held && Scene is null && Battle is null;

    /// <summary>What the session plays by of the player's settings: the last tick's, or those it started with.</summary>
    public SessionSettings Settings => _settings;

    /// <summary>How dense rain and snow fall, trek's, a stage's and an arena's: the settings', up to <see cref="Particles.WeatherParticles.MaxDensity"/>.</summary>
    private float PrecipitationDensity => Math.Clamp(_settings.PrecipitationDensity, 0, Particles.WeatherParticles.MaxDensity);

    /// <summary>
    /// Hands the settings on to what plays by them, the one place the core takes them in: the audio's volumes, the rain and
    /// snow of trek, a stage and an arena, the sky's placement, the player's pace and a battle's spell auras. A stage, an
    /// arena and a battle made later take them as they're made, and the credits as they start.
    /// </summary>
    private void Apply()
    {
        var settings = _settings;
        Zone.Audio.Settings = settings;
        var density = PrecipitationDensity;
        Zone.Precipitation?.Density = density;
        Stage?.Precipitation?.Density = density;
        Arena?.Precipitation?.Density = density;
        Sky.Placement = settings.SkyPlacement;
        Trek.Player.Pace = settings.Pace;
        Battle?.Fx.Turns = settings.SpellAuras;
    }

    /// <summary>
    /// One 60 Hz tick: it counts in every mode (<see cref="GameState.CountTick"/>), then the mode that runs steps. A
    /// cinematic steps, holding everything, and ends itself; the credits step with the map's sounds going on under them and
    /// everything held still, and end themselves; under the pause menu and a backdrop's page the sounds go on the same. A
    /// cut-scene ticks on its stage; trek reopens the menu once it has faded in after the credits or the Theatre, then ticks.
    /// The sky steps after trek, a cut-scene or a battle, and its stars twinkle in every mode, last.
    /// </summary>
    /// <param name="settings">
    /// What this tick plays by of the player's settings, handed on before anything steps (<see cref="Apply"/>):
    /// <see cref="SessionSettings.Original"/> for the original's.
    /// </param>
    /// <param name="readInput">The player's controls, read only when trek or a battle's turn takes them.</param>
    /// <param name="frozen">Whether only time passes in trek, as while the free camera flies.</param>
    /// <param name="actors">A cut-scene's actors' animations, or null for one that has none yet.</param>
    /// <param name="combatants">A battle's combatants' animations, or null for one that has none.</param>
    /// <param name="spellFx">A battle's spells' visuals' actors, or null for none (<see cref="Battle.Tick"/>).</param>
    public SessionStep Tick(SessionSettings settings, Func<PlayerInput> readInput, bool frozen, Func<CutSceneActor, IActorMotion?> actors,
        Func<Combatant, IActorMotion?>? combatants = null, ISpellFxHost? spellFx = null)
    {
        if (settings != _settings)
        {
            _settings = settings;
            Apply();
        }
        Game.CountTick();
        _frozen = frozen;
        StepStick();
        // The menu's tick, every tick, as the runtime stepped it after the game's: a frame of it while it's open, and once it has
        // closed, how it ended, carried out here.
        var (scrollFrame, scrollLength) = ScrollClock();
        if (Menu.Step(scrollFrame, scrollLength) is { } exit)
        {
            MenuClosed(exit);
            Trek.StepBackdrop();
            Sky.StepTwinkle();
            return SessionStep.MenuClosed;
        }
        var step = Step(readInput, frozen, actors, combatants, spellFx);
        // A backdrop's page fades and pulses in every mode, after the mode's step: once it has faded out, it's gone from the next tick's mode.
        Trek.StepBackdrop();
        // The sky is drawn behind whatever holds the world, and its stars twinkle as it's drawn.
        Sky.StepTwinkle();
        return step;
    }

    /// <summary>
    /// A tick of the held directions: in the pause menu they repeat as its cursor's moves (<see cref="Ui.PauseMenu.Repeat"/>); in
    /// trek's popups, conversations and backdrops, in a cut-scene and in combat's held menus they repeat as presses; and where
    /// nothing has a cursor, what's held is forgotten, so the next doesn't start by repeating.
    /// </summary>
    private void StepStick()
    {
        switch (Mode)
        {
            case GameMode.Menu:
                if (Menu.Repeats)
                    Stick.Tick(Menu.Repeat);
                break;
            case GameMode.Popup or GameMode.Conversation or GameMode.CutScene or GameMode.Backdrop:
            case GameMode.Combat when Battle?.Menu is not null:
                Stick.Tick(direction => Press(direction));
                break;
            default:
                Stick.Clear();
                break;
        }
    }

    /// <summary>
    /// A press, to the mode that runs (<see cref="Mode"/>), as the original's modes each read the controller: a cinematic
    /// takes every press, B or Start skipping it, and the credits too, A, B or Start ending them; the pause menu takes its own
    /// (<see cref="Ui.PauseMenu.Press"/>); a battle's are <see cref="Combat.Battle.Press"/>; a cut-scene's its box's
    /// (<see cref="CutScene.Press(MenuAction)"/>); and trek's go to what's up in it, a backdrop's page, a popup or a
    /// conversation, else trek itself (<see cref="TrekLoop.Press"/>), where Start opens the pause menu (<see cref="OpenMenu"/>).
    /// Returns what the press came to, for whoever reads the controls to show.
    /// </summary>
    /// <param name="keyboard">Whether a keyboard key pressed it, which the pause menu's name entry hears differently (<see cref="Ui.PauseMenu.Press"/>).</param>
    public PressOutcome Press(MenuAction action, bool keyboard = false)
    {
        if (action == MenuAction.None)
            return PressOutcome.Ignored;
        switch (Mode)
        {
            case GameMode.Cinematic:
                if (action is MenuAction.Back or MenuAction.Start)
                    Cinematic!.Player.Skip();
                return PressOutcome.Taken;
            case GameMode.Credits:
                if (action is MenuAction.Accept or MenuAction.Back or MenuAction.Start)
                    Credits!.Roll.Skip();
                return PressOutcome.Taken;
            case GameMode.Menu:
                return Menu.Press(action, keyboard) ? PressOutcome.Taken : PressOutcome.Ignored;
            case GameMode.Combat:
                return Battle!.Press(action) ? PressOutcome.Taken : PressOutcome.Ignored;
            case GameMode.CutScene:
                return Scene!.Press(action) ? PressOutcome.Taken : PressOutcome.Ignored;
            default:
                var outcome = Trek.Press(action, _frozen);
                if (outcome == PressOutcome.MenuOpened)
                    OpenMenu();
                return outcome;
        }
    }

    /// <summary>A menu host that plays nothing and can't save: a headless session's, unless a test gives it a recording one.</summary>
    private sealed class NoMenuHost : IPauseMenuHost
    {
        public static readonly NoMenuHost Instance = new();

        public void Cue(PauseMenuCue cue)
        {
        }

        public string? Save(string path) => "Saving isn't possible here.";
    }

    /// <summary>
    /// The mode that runs steps (<see cref="Tick"/>), and after trek, a cut-scene or a battle, the sky (<see cref="SkyDirector.Step"/>):
    /// not while the world is held, nor on the tick trek fades out into a cut-scene or a battle.
    /// </summary>
    private SessionStep Step(Func<PlayerInput> readInput, bool frozen, Func<CutSceneActor, IActorMotion?> actors,
        Func<Combatant, IActorMotion?>? combatants, ISpellFxHost? spellFx)
    {
        switch (Mode)
        {
            case GameMode.Cinematic:
                StepCinematic();
                return SessionStep.Cinematic;
            case GameMode.Credits:
                StepSounds();
                StepCredits();
                return SessionStep.Credits;
            case GameMode.Menu or GameMode.Backdrop:
                // The menu's frame keeps the map's sounds going, with everything held still (PauseMenu.Frame).
                StepSounds();
                return SessionStep.Held;
        }
        if (Scene is { } scene)
        {
            // The zone engine draws the stage, its sounds, weather and rain, as it draws trek (CutScene.Tick), under the stage's sky.
            scene.Tick(Stage!, Sky.Drawn, actors);
            Sky.Step();
            return scene.Over ? SessionStep.CutSceneOver : SessionStep.CutScene;
        }
        if (Battle is { } battle)
        {
            battle.Tick(Arena!, Sky.Drawn, combatants ?? (_ => null), readInput, spellFx);
            if (battle.Paused && !MenuOpen)
            {
                // Combat's states 14 and 27: the pause menu, mode 3.
                Send(new Transition.PauseBattle(battle.PausedFromTurn, battle.PausedFromTurn && battle.CanFlee));
            }
            // A cast dims the sky for a while (CombatSpellFx.DimSky), where spells' visuals are drawn.
            Sky.Dim = spellFx is null ? 1 : battle.Fx.Sky.Value;
            Sky.Step();
            return battle.Over ? SessionStep.CombatOver : SessionStep.Combat;
        }
        if (_reopen is { } reopen && !Trek.CutSceneFade.Fading)
        {
            _reopen = null;
            Send(reopen);
        }
        var trek = Trek.Tick(readInput, frozen);
        if (trek is TrekStep.Ran or TrekStep.Frozen)
            Sky.Step();
        return trek switch
        {
            TrekStep.CutScene => SessionStep.CutSceneCued,
            TrekStep.Battle => SessionStep.BattleCued,
            TrekStep.Frozen => SessionStep.Frozen,
            _ => SessionStep.Trek,
        };
    }

    /// <summary>The map's sounds, while the world is held still: trek's, not a stage's or an arena's.</summary>
    private void StepSounds()
    {
        if (Scene is null && Battle is null)
            Trek.StepSounds();
    }

    /// <summary>A tick of the cinematic, unless it's held for a screenshot, and its end once it has ended.</summary>
    private void StepCinematic()
    {
        if (CinematicHeld)
            return;
        var player = Cinematic!.Player;
        player.Step();
        if (player.Ended)
            CinematicEnded();
    }

    /// <summary>A tick of the credits, and their end once they've ended.</summary>
    private void StepCredits()
    {
        var roll = Credits!.Roll;
        roll.Step();
        if (roll.Ended)
            CreditsEnded();
    }

    /// <summary>
    /// The cued cut-scene begins (<c>clear_HUD_elements</c>, then <c>DialogueModeInitPrescripted</c>): trek's world goes,
    /// the zone engine starts again with its particles emptied, and the stage's 3×3 loads in a zone engine of its own,
    /// without wanderers, asking for its own music, its rain about the cut-scene's camera; then the script starts.
    /// </summary>
    public void BeginCutScene()
    {
        StartCutScene(Trek.BeginCutScene());
    }

    /// <summary>A cut-scene starts on its stage, trek's world having gone (<see cref="BeginCutScene"/>).</summary>
    private void StartCutScene(CutSceneLaunch launch)
    {
        var (script, start, stageRef) = launch;
        Zone.Particles?.Clear();
        if (CutSceneStage.For(Game.Plugins, stageRef, Trek.Chunks.World) is not { } stage)
        {
            Send(new Transition.Say($"{script.Id} plays in a chunk no plugin has."));
            Send(new Transition.EndCutScene(null));
            ResumeTrek();
            return;
        }
        // The stage's chunks play their sounds and light its actors, and its map set and chunk ask for their music
        // (InitZoneEngine); its rain falls about the cut-scene's camera.
        var zone = Stage = Zone.Stage(stage.Set);
        zone.Precipitation?.Density = PrecipitationDensity;
        zone.RequestMusic(stage.Chunk);
        zone.Chunks.Update(stage.Origin + new Vector3(stage.Set.CellSize / 2, 0, stage.Set.CellSize / 2));
        var scene = Scene = new CutScene(Game, script, start, stage, _host);
        zone.Camera = scene.Camera;
        scene.Start();
        Send(new Transition.StartCutScene(scene, zone));
    }

    /// <summary>
    /// The cut-scene is over (<c>load_one_of_two_cinematics</c>): its stage goes, trek starts again where the player stood
    /// (<c>initGameTrek</c>), and its script's end is carried out, as a conversation's is.
    /// </summary>
    public void EndCutScene()
    {
        var scene = Scene ?? throw new InvalidOperationException("No cut-scene is playing.");
        Scene = null;
        // The stage's zone engine ends, and its pool with the rain; the weather drawn carries on into trek.
        Stage!.End();
        Stage = null;
        foreach (var problem in scene.Problems)
            _host.Warn(problem);
        Send(new Transition.EndCutScene(scene));
        // CutScene.Free's ending kinds, as the base plugin's triggers: the game over, Roog's cinematic and the ending's,
        // in place of trek.
        if (Fire(GameEvents.ScriptEnded, scene.Runner.Script.Id))
            return;
        if (scene.Runner.Ending is null or { Kind: ScriptEndKind.Ended } && Chained() is { } next)
        {
            StartCutScene(next);
            return;
        }
        ResumeTrek();
        if (scene.Runner.Ending is { } ending)
        {
            var chunk = scene.Stage.Chunk;
            Trek.CarryOut(
                Transition.AfterScript(Game, ending, ending.Speaker, (new ChunkRef(scene.Stage.Set.Id, chunk.Row, chunk.Column), chunk.Terrain)));
        }
    }

    /// <summary>
    /// The cut-scene a cut-scene's end chains into, in place of trek (<c>CutScene.Free</c>'s last check, <c>0x80051810</c>):
    /// while trek starts again at a named place (<see cref="TrekLoop.AtPlace"/>), the first dialogue object, in the map's
    /// order, of the chunk the player stands in, that's tangible, holds his place within its radius and whose
    /// <see cref="MapObject.Enabled"/> holds (<c>0x800113bc</c>), whether it was active or not. So Oriana's first cut-scene after
    /// the Goblin Ambush leads straight into the next, and the screen goes from one to the other without trek between.
    /// </summary>
    private CutSceneLaunch? Chained()
    {
        var at = Trek.Player.Sphere.Position;
        if (!Trek.AtPlace || Trek.Chunks.ChunkAt(at) is not { } chunk || Game.Plugins.TryGet<MapRecord>(chunk.Map) is not { } map)
            return null;
        var local = at - Trek.Chunks.Origin(chunk);
        foreach (var dialogue in map.Objects.OfType<DialogueObject>())
        {
            if (!dialogue.Tangible || Vector3.Distance(dialogue.Position, local) > dialogue.Radius
                || dialogue.Enabled is { } gate && !Game.Flags.Holds(gate))
                continue;
            // The original starts any script there; every one the data reaches is a cut-scene with a stage.
            return CutSceneLaunch.Of(Game.Plugins, dialogue, MapRecord.VoxelId(map.Id, dialogue.Index));
        }
        return null;
    }

    /// <summary>Trek comes back where the player stood, its world built again and the screen fading in (<see cref="TrekLoop.Resume"/>).</summary>
    private void ResumeTrek()
    {
        Trek.Resume();
        Send(new Transition.ResumeTrek());
    }

    /// <summary>Asks trek for a cut-scene, unless one or a battle is playing (<see cref="TrekLoop.Cue"/>).</summary>
    public void Cue(CutSceneLaunch launch)
    {
        if (Scene is null && Battle is null)
            Trek.Cue(launch);
    }

    /// <inheritdoc cref="Cue(CutSceneLaunch)"/>
    public void Cue(DialogueRecord script, ScriptStart start, ChunkRef stage) => Cue(new CutSceneLaunch(script, start, stage));

    /// <summary>Asks trek for a battle, unless one or a cut-scene is playing (<see cref="TrekLoop.CueBattle"/>).</summary>
    public void CueBattle(BattleSetup setup)
    {
        if (Scene is null && Battle is null)
            Trek.CueBattle(setup);
    }

    /// <summary>The last battle's arena, which a terrain choosing none keeps (<see cref="BattleSetup.ChooseArena"/>).</summary>
    private int _lastArena;

    /// <summary>
    /// The cued battle begins (<c>Combat.Init</c>, <c>0x80022D1C</c>): trek's world goes, the zone engine starts again with
    /// its particles emptied, on the Battle arenas about the battle's arena, without wanderers, asking for the map set's
    /// music; and the battle is set up on it (<see cref="Combat.Battle.Begin"/>), fading in.
    /// </summary>
    public void BeginBattle()
    {
        var setup = Trek.BeginBattle();
        Zone.Particles?.Clear();
        if (setup.Party is null && setup.Enemies.Count == 0)
        {
            // A camp ambush on a terrain with no camp encounters (27): the original reads past the table and finds no enemy.
            Send(new Transition.Say("The party is ambushed, but no one comes."));
            Trek.Resume();
            Send(new Transition.ResumeTrek());
            return;
        }
        var number = setup.Arena ?? setup.ChooseArena(Game.Plugins, Game.Random.Combat) ?? _lastArena;
        if (Combat.Arena.For(Game.Plugins, number) is not { } arena)
        {
            Send(new Transition.Say($"Arena {number} is in no plugin."));
            Trek.Resume();
            Send(new Transition.ResumeTrek());
            return;
        }
        _lastArena = number;
        var zone = Arena = Zone.Stage(arena.Set);
        zone.Precipitation?.Density = PrecipitationDensity;
        zone.RequestMusic(arena.Chunk);
        zone.Chunks.Update(arena.Origin + Combat.Battle.ZonePoint);
        var battle = Battle = Combat.Battle.Begin(Game, setup, arena, zone.World);
        battle.Fx.Turns = _settings.SpellAuras;
        zone.Camera = battle.Camera;
        foreach (var line in battle.Problems)
            _host.Warn(line);
        Send(new Transition.StartedBattle(battle, zone));
    }

    /// <summary>
    /// The battle is over (<c>Combat.Exit</c>, <c>0x80023060</c>): its music is cut, its arena goes, what it put on the party comes off and the dead
    /// leave it (<see cref="Combat.Battle.Leave"/>), and a battle with an end of its own moves where trek starts again
    /// (<see cref="EncounterEnd"/>). Then as it ended: won, the post-battle windows (<see cref="Transition.PostBattle"/>), trek
    /// coming back once they close; lost, the game over (<see cref="PlayGameOver"/>); merged with the Shadow, its cinematic, after which
    /// trek comes back where the battle began (<see cref="CinematicEnded"/>); otherwise trek starts again
    /// (<see cref="TrekLoop.Resume"/>), fading in where the battle began, or having fled, at the farthest breadcrumb.
    /// </summary>
    public void EndBattle()
    {
        var battle = Battle ?? throw new InvalidOperationException("No battle is playing.");
        Battle = null;
        // Combat.Exit (0x80023060) frees the arena with FreeZoneEngine(0), which cuts the battle's music (Bgm.Stop(1)) whatever the
        // outcome: the post-battle windows are silent but for their own sounds, and trek's music starts over as it fades in.
        Arena!.Audio.StopMusic();
        Arena.End();
        Arena = null;
        Send(new Transition.EndBattle(battle));
        battle.Leave();
        if (battle.Rule?.End is { } end)
            Trek.Keep(end.Place, end.Position, end.Heading);
        switch (battle.Outcome)
        {
            case BattleOutcome.Won when battle.Spoils is { } spoils:
                PostBattle = spoils;
                Send(new Transition.PostBattle(spoils));
                return;
            case BattleOutcome.Lost:
                PlayGameOver();
                return;
            // Combat.StateShadowMerge returns game mode 0xE: the Shadow's cinematic, once the battle has gone, then trek where it began.
            case BattleOutcome.Merged when Game.Plugins.TrySingleton<CombatRecord>()?.Shadow is { } shadow
                && Game.Plugins.TryGet<CinematicRecord>(shadow.Cinematic) is { } cinematic:
                PlayCinematic(cinematic, fromTheatre: false);
                return;
        }
        Trek.Resume(fled: battle.Outcome == BattleOutcome.Fled);
        Send(new Transition.ResumeTrek());
    }

    /// <summary>
    /// The game over (<c>Combat.StartGameOverScript</c>, <c>0x8001F860</c>, then mode <c>0xC</c>): its script plays as a cut-scene,
    /// started from its dialogue object in the Dreamscape's map (<see cref="CombatRecord.GameOver"/>), and its end is the game's
    /// (<see cref="EndCutScene"/>). The battle has already healed the party's leader.
    /// </summary>
    private void PlayGameOver()
    {
        if (Game.Plugins.TrySingleton<CombatRecord>()?.GameOver is { } gameOver
            && Game.Plugins.TryGet<MapRecord>(gameOver.Map) is { } map
            && map.Objects.OfType<DialogueObject>().FirstOrDefault(d => d.Script == gameOver.Script) is { } dialogue
            && Game.Plugins.TryGet<DialogueRecord>(gameOver.Script) is { } script
            && CutSceneLaunch.Of(script, dialogue, MapRecord.VoxelId(map.Id, dialogue.Index)) is { } launch)
        {
            StartCutScene(launch);
            return;
        }
        Send(new Transition.GameOver());
    }

    /// <summary>
    /// Start in trek: the pause menu opens (<see cref="Transition.OpenMenu"/>), on a tab or the one it last showed, unless
    /// trek is going through a teleporter or fading into a cut-scene or a battle, when the press is dead
    /// (<c>Actor::Move</c>), or something else runs.
    /// </summary>
    public void OpenMenu(PauseTab? tab = null)
    {
        if (Mode != GameMode.Trek || Trek.Teleporting || Trek.Cued is not null || Trek.CutSceneFade.Fading)
            return;
        Send(new Transition.OpenMenu(tab));
    }

    /// <summary>The loot window opens on a pile, as a container's does (<see cref="Transition.OpenLoot"/>).</summary>
    public void OpenLoot(ItemPile loot, int gold) => Send(new Transition.OpenLoot(loot, gold));

    /// <summary>
    /// The pause menu has rolled up (<see cref="Ui.PauseMenu.Step"/>): the map is gated again by the flags as they are now, as
    /// the original reloads it as the menu closes (<see cref="TrekLoop.Regate"/>), and the game carries on as the menu's exit
    /// says (<see cref="PauseExit"/>): the party camps, the help menu's script starts, the credits roll, the Theatre's cinematic
    /// plays or a save is loaded; the post-battle windows close to trek, and combat's pause goes back to its turn or flees.
    /// </summary>
    public void MenuClosed(PauseExit exit)
    {
        Menu.Discard();
        // Trek's world is freed while a battle plays.
        if (Battle is null)
            Trek.Regate();
        if (PostBattle is not null)
        {
            // The loot window closes to trek (PauseMenu.CloseToTrek): built again where the battle began, fading in.
            PostBattle = null;
            Trek.Resume();
            Send(new Transition.ResumeTrek());
            return;
        }
        if (Battle is { } battle)
        {
            // Combat's pause menu returns to the state it paused, or flees (state 16).
            battle.Unpause(exit.Code == PauseExitCode.Flee);
            return;
        }
        switch (exit.Code)
        {
            case PauseExitCode.Camp:
                Send(new Transition.Camp(Trek.Camp()));
                break;
            case PauseExitCode.Help:
                Trek.ShowBackdrop(Game.Plugins.Get<DialogueRecord>(SystemMenu.HelpScript));
                break;
            case PauseExitCode.Credits:
                PlayCredits(Game.Plugins.Singleton<CreditsRecord>(), ending: false, fromMenu: true);
                break;
            case PauseExitCode.Theatre when exit.Cinematic is { } cinematic:
                PlayCinematic(cinematic, fromTheatre: true);
                break;
            case PauseExitCode.Load when exit.SavePath is { } path:
                Send(new Transition.LoadGame(path));
                break;
        }
    }

    /// <summary>
    /// Hands a transition to the host, opening the pause menu first on the ones that open it (<see cref="Menu"/>): Start's, a
    /// container's loot, a script's shop or training, a battle's pause, a win's post-battle windows and the menu reopening, so
    /// the menu is open, and the mode is its, as the host hears of them.
    /// </summary>
    private void Send(Transition next)
    {
        switch (next)
        {
            case Transition.OpenMenu open:
                Menu.Open(open.Tab);
                break;
            case Transition.OpenLoot loot:
                Menu.OpenLoot(loot.Loot, loot.Gold);
                break;
            case Transition.OpenShop shop:
                Menu.OpenShop(shop.Shop);
                break;
            case Transition.OpenTraining training:
                Menu.OpenTraining(training.Trainer);
                break;
            case Transition.PauseBattle pause:
                Menu.OpenCombat(pause.PartyTab, pause.CanFlee, Battle is { } battle ? CombatPause.For(battle) : null);
                break;
            case Transition.PostBattle won:
                Menu.OpenPostBattle(won.Spoils);
                break;
            case Transition.ReopenMenu { Theatre: true }:
                Menu.OpenTheatre();
                break;
            case Transition.ReopenMenu:
                Menu.Open();
                break;
            // The sky as each world is built and goes (FreeZoneEngine leaves none up): a stage's or an arena's chunk chooses
            // its own at once, a cut-scene's scenery fog ignoring the weather, and trek's chooses again as it's built.
            case Transition.StartCutScene start:
                Sky.ClearSky();
                Sky.Enter(start.Scene.Stage.Chunk);
                Sky.CutScene = true;
                Sky.Apply();
                break;
            case Transition.EndCutScene:
                Sky.ClearSky();
                Sky.CutScene = false;
                break;
            case Transition.StartedBattle started:
                Sky.ClearSky();
                Sky.Enter(started.Battle.Arena.Chunk);
                Sky.Apply();
                break;
            case Transition.EndBattle:
                Sky.Dim = 1;
                Sky.ClearSky();
                break;
            case Transition.ResumeTrek:
                Sky.Apply();
                break;
        }
        _host.Transition(next);
    }

    /// <summary>
    /// Trek's host as the session gives it: everything passes to the host that presents the game, and on the way the session
    /// opens the menu on the transitions that open it (<see cref="Send"/>).
    /// </summary>
    private sealed class Relay(GameSession session, ITrekHost host) : ITrekHost
    {
        public void Transition(Transition next) => session.Send(next);

        public void ConversationChanged() => host.ConversationChanged();

        public void Arrived() => host.Arrived();

        public IRootMotion Motion(Wanderer wanderer) => host.Motion(wanderer);

        public void Animate(LoadedMap map, SceneObject actor, int request) => host.Animate(map, actor, request);

        public void Changed(LoadedMap map, SceneObject scene) => host.Changed(map, scene);

        public void PutOut(LoadedMap map, SceneObject scene) => host.PutOut(map, scene);

        public void Regated(LoadedMap map) => host.Regated(map);

        public void Log(string text) => host.Log(text);

        public void Warn(string text) => host.Warn(text);

        public void Cue(ScriptCue cue) => host.Cue(cue);
    }

    /// <summary>
    /// Plays a cinematic (<c>Cinematic.Load</c>), unless one is playing, the party leader's name in its captions in place
    /// of Alaron's: from the Theatre, the menu reopens on its page after it. Trek's world may have gone for it, under a
    /// battle (the Shadow's merge) or a cut-scene; it's built again after.
    /// </summary>
    public void PlayCinematic(CinematicRecord cinematic, bool fromTheatre)
    {
        if (Cinematic is not null)
            return;
        Cinematic = new CinematicShowing(new CinematicPlayer(cinematic, Game.Party.Members.FirstOrDefault()?.Name ?? "Alaron"), fromTheatre);
        Send(new Transition.PlayCinematic(cinematic));
    }

    /// <summary>
    /// The cinematic has ended (<c>Cinematic.EndCase</c>, <c>0x80053c20</c>): its picture and music go
    /// (<see cref="Transition.EndCinematic"/>), and its done flag is set however it ended, from the Theatre too. In the
    /// story, what follows it is the triggers' on <see cref="GameEvents.CinematicEnded"/>: the opening's script, Roog's True
    /// Name, the ending's credits. Unless one takes over, trek comes back where the player stood, its music asked for again
    /// and the screen fading in from black, its world built again if it had gone; from the Theatre, the menu reopens on its
    /// page once it has (trek's state 8).
    /// </summary>
    private void CinematicEnded()
    {
        var (player, fromTheatre) = Cinematic ?? throw new InvalidOperationException("No cinematic is playing.");
        Cinematic = null;
        var cinematic = player.Cinematic;
        Send(new Transition.EndCinematic(cinematic));
        Game.Flags.TrySet(cinematic.DoneFlag, true);
        if (!fromTheatre && Fire(GameEvents.CinematicEnded, cinematic.Id))
            return;
        var freed = !Zone.WorldLoaded;
        Trek.Resume();
        if (freed)
            Send(new Transition.ResumeTrek());
        if (fromTheatre)
            _reopen = new Transition.ReopenMenu(Theatre: true);
    }

    /// <summary>
    /// Rolls the credits (<c>Credits.Start</c>) over the world held still, on a screen as tall as the UI's: the ending's,
    /// which can't be skipped and end the game, or the menu's, after which the menu reopens.
    /// </summary>
    private void PlayCredits(CreditsRecord credits, bool ending, bool fromMenu)
    {
        Credits = new CreditsShowing(new CreditsRoll(credits, Font.LineHeight(), _settings.UiLines, ending), fromMenu);
        Send(new Transition.PlayCredits(ending));
    }

    /// <summary>
    /// The credits have ended (<c>Credits.Destroy</c>): the ending's end the game (<see cref="Transition.GameOver"/>).
    /// Otherwise trek fades in from black over half a second, built again if its world had gone, their music stopping and
    /// the game's coming back as the screen is black (<see cref="Transition.EndCredits"/>), and after the menu's Credits the
    /// menu reopens once it has faded in, on the System tab's list.
    /// </summary>
    private void CreditsEnded()
    {
        var (roll, fromMenu) = Credits ?? throw new InvalidOperationException("No credits are rolling.");
        Credits = null;
        if (roll.Ending)
        {
            Send(new Transition.EndCredits(Ending: true));
            Send(new Transition.GameOver());
            return;
        }
        var freed = !Zone.WorldLoaded;
        if (freed)
            Trek.Resume();
        else
            Trek.CutSceneFade.In(TrekLoop.CutSceneFadePerTick);
        Send(new Transition.EndCredits(Ending: false));
        if (freed)
            Send(new Transition.ResumeTrek());
        if (fromMenu)
            _reopen = new Transition.ReopenMenu(Theatre: false);
    }

    /// <summary>
    /// A new game begins (<c>Title.Leave</c>'s New Game), its world built where the player arrives: the triggers on
    /// <see cref="GameEvents.GameStarted"/> go off, the base plugin's playing the opening cinematic. False if none takes
    /// over, and trek simply starts.
    /// </summary>
    public bool StartNewGame() => Fire(GameEvents.GameStarted, null);

    /// <summary>
    /// Fires an event on the triggers (<see cref="Triggers.Fire"/>), their sounds to the host, and carries out an effect that takes over from trek.
    /// True if one did, so what would come next at the event's place, trek coming back, doesn't. Trek's world may have gone,
    /// as under a cut-scene; what takes over builds it again after.
    /// </summary>
    private bool Fire(string name, RecordId? subject)
    {
        if (Triggers.Fire(Game, name, subject, _host.Warn, _host) is not { } effect)
            return false;
        switch (effect)
        {
            case TriggerEffect.Cinematic { Play: var id } when Game.Plugins.TryGet<CinematicRecord>(id) is { } cinematic:
                PlayCinematic(cinematic, fromTheatre: false);
                return true;
            case TriggerEffect.Script { From: var from } when StartFrom(from):
                return true;
            case TriggerEffect.Credits credits:
                PlayCredits(Game.Plugins.Singleton<CreditsRecord>(), credits.Ending, fromMenu: false);
                return true;
            case TriggerEffect.GameOver:
                Send(new Transition.GameOver());
                return true;
        }
        _host.Warn($"A trigger on {name} asks for {effect}, which no plugin has.");
        return false;
    }

    /// <summary>
    /// Starts the script a dialogue object starts, at once and in place of trek: a cut-scene on the object's stage from its
    /// reference points, trek's world going first if it hasn't. False if the object or its script isn't a cut-scene a
    /// plugin has.
    /// </summary>
    private bool StartFrom(RecordId voxel)
    {
        if (MapRecord.Find(Game.Plugins, voxel) is not DialogueObject dialogue
            || CutSceneLaunch.Of(Game.Plugins, dialogue, voxel) is not { } launch)
            return false;
        Trek.Free();
        StartCutScene(launch);
        return true;
    }

    /// <summary>
    /// How a new game starts (<see cref="NewGameRecord"/>): where the player arrives, and the party <see cref="GameState.NewGame"/>
    /// builds. Plugins imported before the record was added have none, and the game can't start from them: this says to
    /// import again, as an out-of-date import does.
    /// </summary>
    public static NewGameRecord NewGame(PluginSet plugins) => plugins.TrySingleton<NewGameRecord>()
        ?? throw new InvalidDataException("The plugins have no new game record (aidyn:newgame/0): they were imported by an older importer. Run: aidyn import <rom>");

    /// <summary>
    /// Where a new game puts the player's body before it's set down: the record's chunk, by name, in its map set, and the
    /// point in it at the record's height.
    /// </summary>
    public static (MapSetRecord Set, string Chunk, Vector3 At) Arrival(PluginSet plugins, NewGameRecord start)
    {
        var set = plugins.Get<MapSetRecord>(start.Chunk.MapSet);
        var chunk = set.Chunk(start.Chunk.Row, start.Chunk.Column)
            ?? throw new InvalidDataException($"{set.Label} has no chunk at row {start.Chunk.Row}, column {start.Chunk.Column}.");
        return (set, chunk.Name, new Vector3(start.Position.X, start.Height, start.Position.Y));
    }

    /// <summary>The player's body's actor: the party leader's (<see cref="EntityBody.Actor"/>), Alaron's in the base game.</summary>
    public RecordId? PlayerActor =>
        Game.Party.Members.FirstOrDefault() is { } leader ? Game.Plugins.TryGet<EntityRecord>(leader.Entity)?.Body?.Actor : null;

    /// <summary>The chunk the player is in, and whether its terrain has the outdoor sky, for the World tab's map and Camp.</summary>
    public (ChunkRef? Place, bool Outdoors) Where()
    {
        if (Trek.Chunks.ChunkAt(Trek.Player.Sphere.Position) is not { } chunk)
            return (null, false);
        var terrain = TerrainRecord.Find(Game.Plugins, chunk.Terrain);
        return (new ChunkRef(Trek.Chunks.MapSet.Id, chunk.Row, chunk.Column), terrain?.SkyDrawn ?? false);
    }

    /// <summary>Notes where the player stands and which way he faces, for a save (<see cref="GameState.Place"/>). False if he isn't in a chunk.</summary>
    public bool NotePlace()
    {
        var position = Trek.Player.Sphere.Position;
        if (Trek.Chunks.ChunkAt(position) is not { } chunk)
            return false;
        Game.Place = new SavePlace(Trek.Chunks.MapSet.Id, chunk.Name, position - Trek.Chunks.Origin(chunk), Trek.Player.Heading);
        return true;
    }
}

/// <summary>
/// A cinematic playing over everything (game mode 0xE), which the session steps (<see cref="GameSession.Cinematic"/>):
/// from the Theatre, after which the menu reopens on its page, or the story's, whose end the triggers carry on.
/// </summary>
public sealed record CinematicShowing(CinematicPlayer Player, bool FromTheatre)
{
    public CinematicRecord Record => Player.Cinematic;
}

/// <summary>
/// The credits rolling (game mode 0x13), which the session steps (<see cref="GameSession.Credits"/>): the ending's, which
/// end the game, or from the menu's Credits, after which the menu reopens.
/// </summary>
public sealed record CreditsShowing(CreditsRoll Roll, bool FromMenu)
{
    public bool Ending => Roll.Ending;
}
