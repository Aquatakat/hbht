using System.Numerics;
using Aidyn.Core.Actors;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Events;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Scripts;
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
/// The pause menu's, a backdrop's, the credits' and a cinematic's drawing and stepping are presentation, which the runtime
/// does: it says when the menu and a backdrop open and close, and when the credits and a cinematic end, and this carries
/// out what follows. Combat is a mode here too, on a stage of trek's zone engine as a cut-scene is (<see cref="BeginBattle"/>).
/// </remarks>
public sealed class GameSession
{
    private readonly ITrekHost _host;
    private (CinematicRecord Record, bool FromTheatre, bool WorldFreed)? _cinematic;
    /// <summary>The credits rolling: the ending's, from the menu's Credits, and whether trek's world has gone under them.</summary>
    private (bool Ending, bool FromMenu, bool WorldFreed)? _credits;
    private Triggers? _triggers;
    /// <summary>The pause menu reopens once trek has faded in, on the Theatre's page or as it was (trek's state 8).</summary>
    private bool? _reopenOnTheatre;
    private float _precipitationDensity = 1;

    private GameSession(GameState game, ITrekHost host, ZoneEngine zone, PlayerMotion player, FollowCamera camera)
    {
        Game = game;
        _host = host;
        Trek = new TrekLoop(game, host, zone, player, camera);
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
    public static GameSession Start(GameState game, ITrekHost host, ZoneEngine zone, GameStart start)
    {
        var chunks = zone.Chunks;
        if (chunks.MapSet.Id != start.Set.Id)
            throw new ArgumentException($"The zone engine is on {chunks.MapSet.Label}, not {start.Set.Label}.", nameof(zone));
        var chunk = chunks.Find(start.Chunk) ?? throw new KeyNotFoundException($"{start.Set.Label} has no chunk {start.Chunk}.");
        var spawn = chunks.Origin(chunk) + start.Local;
        var look = start.Heading == default ? GameStart.Facing(0) : Vector2.Normalize(start.Heading);
        var player = new PlayerMotion(spawn, look);
        var camera = new FollowCamera(spawn, look);
        var session = new GameSession(game, host, zone, player, camera);
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

    /// <summary>The cut-scene playing, in place of trek's world.</summary>
    public CutScene? Scene { get; private set; }

    /// <summary>The cut-scene's stage: its own zone engine (<see cref="ZoneEngine.Stage"/>).</summary>
    public ZoneEngine? Stage { get; private set; }

    /// <summary>The battle playing, in place of trek's world (<see cref="BeginBattle"/>).</summary>
    public Battle? Battle { get; private set; }

    /// <summary>The battle's arena: a zone engine of its own on the Battle arenas (<see cref="ZoneEngine.Stage"/>).</summary>
    public ZoneEngine? Arena { get; private set; }

    /// <summary>Whether the pause menu is open.</summary>
    public bool MenuOpen { get; private set; }

    /// <summary>
    /// What the battle just won gave, while its post-battle windows are up or about to be (game mode 6,
    /// <see cref="Transition.PostBattle"/>): the world is held, and trek is built again as they close.
    /// </summary>
    public BattleSpoils? PostBattle { get; private set; }

    /// <summary>Whether a backdrop's or the help menu's page shows, or is still fading out (<see cref="TrekLoop.Backdrop"/>).</summary>
    public bool BackdropShowing => Trek.Backdrop is not null;

    /// <summary>Whether the last tick froze trek, as while the runtime's free camera flies: then A and B in trek do nothing.</summary>
    private bool _frozen;

    /// <summary>Whether the credits are rolling.</summary>
    public bool CreditsRolling => _credits is not null;

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

    /// <summary>The cinematic playing.</summary>
    public CinematicRecord? Cinematic => _cinematic?.Record;

    /// <summary>
    /// What runs, the highest that's up: a cinematic, the credits, the pause menu, a backdrop's page, a popup, a
    /// conversation, a cut-scene, a battle, else trek.
    /// </summary>
    public GameMode Mode =>
        _cinematic is not null ? GameMode.Cinematic
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

    /// <summary>How dense rain and snow fall, trek's and a stage's: the settings' multiple of the original's.</summary>
    public float PrecipitationDensity
    {
        get => _precipitationDensity;
        set
        {
            _precipitationDensity = value;
            Zone.Precipitation?.Density = value;
            Stage?.Precipitation?.Density = value;
            Arena?.Precipitation?.Density = value;
        }
    }

    /// <summary>
    /// One 60 Hz tick: it counts in every mode (<see cref="GameState.CountTick"/>), then the mode that runs steps. Under
    /// the pause menu, a backdrop's page and the credits, the map's sounds go on with everything held still; a cinematic
    /// holds everything. A cut-scene ticks on its stage; trek reopens the menu once it has faded in after the credits or the
    /// Theatre, then ticks.
    /// </summary>
    /// <param name="readInput">The player's controls, read only when trek or a battle's turn takes them.</param>
    /// <param name="frozen">Whether only time passes in trek, as while the free camera flies.</param>
    /// <param name="skyDrawn">Whether the stage's sky is drawn, for a cut-scene's weather.</param>
    /// <param name="actors">A cut-scene's actors' animations, or null for one that has none yet.</param>
    /// <param name="combatants">A battle's combatants' animations, or null for one that has none.</param>
    /// <param name="spellFx">A battle's spells' visuals' actors, or null for none (<see cref="Battle.Tick"/>).</param>
    public SessionStep Tick(Func<PlayerInput> readInput, bool frozen, bool skyDrawn, Func<CutSceneActor, IActorMotion?> actors,
        Func<Combatant, IActorMotion?>? combatants = null, ISpellFxHost? spellFx = null)
    {
        Game.CountTick();
        _frozen = frozen;
        // A backdrop's page fades and pulses whatever runs over it, after the mode it holds has had its tick: the tick its page
        // goes on is still held, as the runtime's layer stepped after the game.
        var mode = Mode;
        Trek.StepBackdrop();
        switch (mode)
        {
            case GameMode.Cinematic:
                return SessionStep.Cinematic;
            case GameMode.Credits:
                StepSounds();
                return SessionStep.Credits;
            case GameMode.Menu or GameMode.Backdrop:
                // The menu's frame keeps the map's sounds going, with everything held still (PauseMenu.Frame).
                StepSounds();
                return SessionStep.Held;
        }
        if (Scene is { } scene)
        {
            // The zone engine draws the stage, its sounds, weather and rain, as it draws trek (CutScene.Tick).
            scene.Tick(Stage!, skyDrawn, actors);
            return scene.Over ? SessionStep.CutSceneOver : SessionStep.CutScene;
        }
        if (Battle is { } battle)
        {
            battle.Tick(Arena!, skyDrawn, combatants ?? (_ => null), readInput, spellFx);
            if (battle.Paused && !MenuOpen && !_menuAsked)
            {
                // Combat's states 14 and 27: the pause menu, mode 3.
                _menuAsked = true;
                _host.Transition(new Transition.PauseBattle(battle.PausedFromTurn, battle.PausedFromTurn && battle.CanFlee));
            }
            return battle.Over ? SessionStep.CombatOver : SessionStep.Combat;
        }
        if (_reopenOnTheatre is { } theatre && !Trek.CutSceneFade.Fading)
        {
            _reopenOnTheatre = null;
            _host.Transition(new Transition.ReopenMenu(theatre));
        }
        return Trek.Tick(readInput, frozen) switch
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

    /// <summary>
    /// The cued cut-scene begins (<c>clear_HUD_elements</c>, then <c>DialogueModeInitPrescripted</c>): trek's world goes,
    /// the zone engine starts again with its particles emptied, and the stage's 3×3 loads in a zone engine of its own,
    /// without wanderers, asking for its own music, its rain about the cut-scene's camera; then the script starts.
    /// </summary>
    public void BeginCutScene()
    {
        var (script, start, stageRef) = Trek.BeginCutScene();
        StartCutScene(script, start, stageRef);
    }

    /// <summary>A cut-scene starts on its stage, trek's world having gone (<see cref="BeginCutScene"/>).</summary>
    private void StartCutScene(DialogueRecord script, ScriptStart start, ChunkRef stageRef)
    {
        Zone.Particles?.Clear();
        if (CutSceneStage.For(Game.Plugins, stageRef, Trek.Chunks.World) is not { } stage)
        {
            _host.Transition(new Transition.Say($"{script.Id} plays in a chunk no plugin has."));
            _host.Transition(new Transition.EndCutScene(null));
            ResumeTrek();
            return;
        }
        // The stage's chunks play their sounds and light its actors, and its map set and chunk ask for their music
        // (InitZoneEngine); its rain falls about the cut-scene's camera.
        var zone = Stage = Zone.Stage(stage.Set);
        zone.Precipitation?.Density = _precipitationDensity;
        zone.RequestMusic(stage.Chunk);
        zone.Chunks.Update(stage.Origin + new Vector3(stage.Set.CellSize / 2, 0, stage.Set.CellSize / 2));
        var scene = Scene = new CutScene(Game, script, start, stage, _host);
        zone.Camera = scene.Camera;
        scene.Start();
        _host.Transition(new Transition.StartCutScene(scene, zone));
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
        _host.Transition(new Transition.EndCutScene(scene));
        // CutScene.Free's ending kinds, as the base plugin's triggers: the game over, Roog's cinematic and the ending's,
        // in place of trek.
        if (Fire(GameEvents.ScriptEnded, scene.Runner.Script.Id, worldFreed: true))
            return;
        if (scene.Runner.Ending is null or { Kind: ScriptEndKind.Ended } && Chained() is var (next, start, stage))
        {
            StartCutScene(next, start, stage);
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
    private (DialogueRecord Script, ScriptStart Start, ChunkRef Stage)? Chained()
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
            if (dialogue.Stage is not { } stage || Game.Plugins.TryGet<DialogueRecord>(dialogue.Script) is not { Mode: DialogueMode.Cutscene } script)
                return null;
            return (script, new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index)), stage);
        }
        return null;
    }

    /// <summary>Trek comes back where the player stood, its world built again and the screen fading in (<see cref="TrekLoop.Resume"/>).</summary>
    private void ResumeTrek()
    {
        Trek.Resume();
        _host.Transition(new Transition.ResumeTrek());
    }

    /// <summary>Asks trek for a cut-scene, unless one or a battle is playing (<see cref="TrekLoop.Cue"/>).</summary>
    public void Cue(DialogueRecord script, ScriptStart start, ChunkRef stage)
    {
        if (Scene is null && Battle is null)
            Trek.Cue(script, start, stage);
    }

    /// <summary>Asks trek for a battle, unless one or a cut-scene is playing (<see cref="TrekLoop.CueBattle"/>).</summary>
    public void CueBattle(BattleSetup setup)
    {
        if (Scene is null && Battle is null)
            Trek.CueBattle(setup);
    }

    /// <summary>The last battle's arena, which a terrain choosing none keeps (<see cref="BattleSetup.ChooseArena"/>).</summary>
    private int _lastArena;

    /// <summary>Whether combat's pause has asked for the menu, which it does once.</summary>
    private bool _menuAsked;

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
            _host.Transition(new Transition.Say("The party is ambushed, but no one comes."));
            Trek.Resume();
            _host.Transition(new Transition.ResumeTrek());
            return;
        }
        var number = setup.Arena ?? setup.ChooseArena(Game.Plugins, Game.Random.Combat) ?? _lastArena;
        if (Combat.Arena.For(Game.Plugins, number) is not { } arena)
        {
            _host.Transition(new Transition.Say($"Arena {number} is in no plugin."));
            Trek.Resume();
            _host.Transition(new Transition.ResumeTrek());
            return;
        }
        _lastArena = number;
        var zone = Arena = Zone.Stage(arena.Set);
        zone.Precipitation?.Density = _precipitationDensity;
        zone.RequestMusic(arena.Chunk);
        zone.Chunks.Update(arena.Origin + Combat.Battle.ZonePoint);
        var battle = Battle = Combat.Battle.Begin(Game, setup, arena, zone.World);
        zone.Camera = battle.Camera;
        _menuAsked = false;
        foreach (var line in battle.Problems)
            _host.Warn(line);
        _host.Transition(new Transition.StartedBattle(battle, zone));
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
        _host.Transition(new Transition.EndBattle(battle));
        battle.Leave();
        if (battle.Rule?.End is { } end)
            Trek.Keep(end.Place, end.Position, end.Heading);
        switch (battle.Outcome)
        {
            case BattleOutcome.Won when battle.Spoils is { } spoils:
                PostBattle = spoils;
                _host.Transition(new Transition.PostBattle(spoils));
                return;
            case BattleOutcome.Lost:
                PlayGameOver();
                return;
            // Combat.StateShadowMerge returns game mode 0xE: the Shadow's cinematic, once the battle has gone, then trek where it began.
            case BattleOutcome.Merged when Game.Plugins.TrySingleton<CombatRecord>(RecordTypes.Combat)?.Shadow is { } shadow
                && Game.Plugins.TryGet<CinematicRecord>(shadow.Cinematic) is { } cinematic:
                PlayCinematic(cinematic, fromTheatre: false, worldFreed: true);
                return;
        }
        Trek.Resume(fled: battle.Outcome == BattleOutcome.Fled);
        _host.Transition(new Transition.ResumeTrek());
    }

    /// <summary>
    /// The game over (<c>Combat.StartGameOverScript</c>, <c>0x8001F860</c>, then mode <c>0xC</c>): its script plays as a cut-scene,
    /// started from its dialogue object in the Dreamscape's map (<see cref="CombatRecord.GameOver"/>), and its end is the game's
    /// (<see cref="EndCutScene"/>). The battle has already healed the party's leader.
    /// </summary>
    private void PlayGameOver()
    {
        if (Game.Plugins.TrySingleton<CombatRecord>(RecordTypes.Combat)?.GameOver is { } gameOver
            && Game.Plugins.TryGet<MapRecord>(gameOver.Map) is { } map
            && map.Objects.OfType<DialogueObject>().FirstOrDefault(d => d.Script == gameOver.Script) is { Stage: { } stage } dialogue
            && Game.Plugins.TryGet<DialogueRecord>(gameOver.Script) is { } script)
        {
            StartCutScene(script, new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index)), stage);
            return;
        }
        _host.Transition(new Transition.GameOver());
    }

    /// <summary>
    /// The pause menu has opened, on a tab or in its barter or training state; in combat, as its pause asked
    /// (<see cref="Transition.PauseBattle"/>).
    /// </summary>
    public void MenuOpened() => MenuOpen = true;

    /// <summary>
    /// The pause menu has rolled up, and trek carries on as its exit says (<see cref="PauseExit"/>): the party camps, the
    /// help menu's script starts, the credits roll or the Theatre's cinematic plays.
    /// </summary>
    public void MenuClosed(PauseExit exit)
    {
        MenuOpen = false;
        if (PostBattle is not null)
        {
            // The loot window closes to trek (PauseMenu.CloseToTrek): built again where the battle began, fading in.
            PostBattle = null;
            Trek.Resume();
            _host.Transition(new Transition.ResumeTrek());
            return;
        }
        if (Battle is { } battle)
        {
            // Combat's pause menu returns to the state it paused, or flees (state 16).
            _menuAsked = false;
            battle.Unpause(exit.Code == PauseExitCode.Flee);
            return;
        }
        switch (exit.Code)
        {
            case PauseExitCode.Camp:
                _host.Transition(new Transition.Camp(Trek.Camp()));
                break;
            case PauseExitCode.Help:
                Trek.ShowBackdrop(Game.Plugins.Get<DialogueRecord>(SystemMenu.HelpScript));
                break;
            case PauseExitCode.Credits when Game.Plugins.TrySingleton<CreditsRecord>(RecordTypes.Credits) is not null:
                _credits = (Ending: false, FromMenu: true, WorldFreed: false);
                _host.Transition(new Transition.PlayCredits());
                break;
            case PauseExitCode.Theatre when exit.Cinematic is { } cinematic:
                PlayCinematic(cinematic, fromTheatre: true);
                break;
        }
    }

    /// <summary>
    /// A press, to the mode that runs (<see cref="Mode"/>), as the original's modes each read the controller: a cinematic
    /// and the credits take every press, B or Start skipping the one and A, B or Start the other; the pause menu takes its
    /// own (<see cref="Ui.PauseMenu"/>); a battle's are <see cref="Combat.Battle.Press"/>; a cut-scene's its box's
    /// (<see cref="CutScene.Press(MenuAction)"/>); and trek's go to what's up in it, a backdrop's page, a popup or a
    /// conversation, else trek itself (<see cref="TrekLoop.Press"/>), where Start opens the pause menu. Returns what the
    /// press came to, for whoever reads the controls to show.
    /// </summary>
    public PressOutcome Press(MenuAction action)
    {
        if (action == MenuAction.None)
            return PressOutcome.Ignored;
        switch (Mode)
        {
            case GameMode.Cinematic:
                return action is MenuAction.Back or MenuAction.Start ? PressOutcome.Skip : PressOutcome.Taken;
            case GameMode.Credits:
                return action is MenuAction.Accept or MenuAction.Back or MenuAction.Start ? PressOutcome.Skip : PressOutcome.Taken;
            case GameMode.Menu:
                return PressOutcome.Ignored;
            case GameMode.Combat:
                return Battle!.Press(action) ? PressOutcome.Taken : PressOutcome.Ignored;
            case GameMode.CutScene:
                return Scene!.Press(action) ? PressOutcome.Taken : PressOutcome.Ignored;
            default:
                return Trek.Press(action, _frozen);
        }
    }

    /// <summary>
    /// Plays a cinematic (<c>Cinematic.Load</c>), unless one is playing: from the Theatre, the menu reopens on its page
    /// after it.
    /// </summary>
    /// <param name="worldFreed">
    /// Whether trek's world has gone for it, under a battle (the Shadow's merge) or a cut-scene, so it's built again after.
    /// </param>
    public void PlayCinematic(CinematicRecord cinematic, bool fromTheatre, bool worldFreed = false)
    {
        if (_cinematic is not null)
            return;
        _cinematic = (cinematic, fromTheatre, worldFreed);
        _host.Transition(new Transition.PlayCinematic(cinematic));
    }

    /// <summary>
    /// The cinematic has ended (<c>Cinematic.EndCase</c>, <c>0x80053c20</c>): its done flag is set however it ended, from the
    /// Theatre too. In the story, what follows it is the triggers' on <see cref="GameEvents.CinematicEnded"/>: the opening's
    /// script, Roog's True Name, the ending's credits. Unless one takes over, trek comes back where the player stood, its
    /// music asked for again and the screen fading in from black; from the Theatre, the menu reopens on its page once it
    /// has (trek's state 8).
    /// </summary>
    public void CinematicEnded()
    {
        var (cinematic, fromTheatre, worldFreed) = _cinematic ?? throw new InvalidOperationException("No cinematic is playing.");
        _cinematic = null;
        Game.Flags.TrySet(cinematic.DoneFlag, true);
        if (!fromTheatre && Fire(GameEvents.CinematicEnded, cinematic.Id, worldFreed))
            return;
        Trek.Resume();
        if (worldFreed)
            _host.Transition(new Transition.ResumeTrek());
        if (fromTheatre)
            _reopenOnTheatre = true;
    }

    /// <summary>
    /// The credits have ended (<c>Credits.Destroy</c>): the ending's end the game (<see cref="Transition.GameOver"/>).
    /// Otherwise trek fades in from black over half a second, built again if it had gone, and after the menu's Credits the
    /// menu reopens once it has, on the System tab's list.
    /// </summary>
    public void CreditsEnded()
    {
        var (ending, fromMenu, worldFreed) = _credits ?? throw new InvalidOperationException("No credits are rolling.");
        _credits = null;
        if (ending)
        {
            _host.Transition(new Transition.GameOver());
            return;
        }
        if (worldFreed)
            ResumeTrek();
        else
            Trek.CutSceneFade.In(TrekLoop.CutSceneFadePerTick);
        if (fromMenu)
            _reopenOnTheatre = false;
    }

    /// <summary>
    /// A new game begins (<c>Title.Leave</c>'s New Game), its world built where the player arrives: the triggers on
    /// <see cref="GameEvents.GameStarted"/> go off, the base plugin's playing the opening cinematic. False if none takes
    /// over, and trek simply starts.
    /// </summary>
    public bool StartNewGame() => Fire(GameEvents.GameStarted, null, worldFreed: false);

    /// <summary>
    /// Fires an event on the triggers (<see cref="Triggers.Fire"/>), their sounds to the host, and carries out an effect that takes over from trek.
    /// True if one did, so what would come next at the event's place, trek coming back, doesn't.
    /// </summary>
    /// <param name="worldFreed">Whether trek's world has gone, as under a cut-scene, so it's built again after what takes over.</param>
    private bool Fire(string name, RecordId? subject, bool worldFreed)
    {
        if (Triggers.Fire(Game, name, subject, _host.Warn, _host) is not { } effect)
            return false;
        switch (effect)
        {
            case TriggerEffect.Cinematic { Play: var id } when Game.Plugins.TryGet<CinematicRecord>(id) is { } cinematic:
                PlayCinematic(cinematic, fromTheatre: false, worldFreed);
                return true;
            case TriggerEffect.Script { From: var from } when StartFrom(from):
                return true;
            case TriggerEffect.Credits credits when Game.Plugins.TrySingleton<CreditsRecord>(RecordTypes.Credits) is not null:
                _credits = (credits.Ending, FromMenu: false, worldFreed);
                _host.Transition(new Transition.PlayCredits(credits.Ending));
                return true;
            case TriggerEffect.GameOver:
                _host.Transition(new Transition.GameOver());
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
        if (MapRecord.Find(Game.Plugins, voxel) is not DialogueObject { Stage: { } stage } dialogue
            || Game.Plugins.TryGet<DialogueRecord>(dialogue.Script) is not { Mode: DialogueMode.Cutscene } script)
            return false;
        Trek.Free();
        StartCutScene(script, new ScriptStart(dialogue.RefPointBase, voxel), stage);
        return true;
    }

    /// <summary>
    /// How a new game starts (<see cref="NewGameRecord"/>): where the player arrives, and the party <see cref="GameState.NewGame"/>
    /// builds. Plugins imported before the record was added have none, and the game can't start from them: this says to
    /// import again, as an out-of-date import does.
    /// </summary>
    public static NewGameRecord NewGame(PluginSet plugins) => plugins.TrySingleton<NewGameRecord>(RecordTypes.NewGame)
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
