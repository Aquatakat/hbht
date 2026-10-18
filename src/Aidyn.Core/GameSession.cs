using System.Numerics;
using Aidyn.Core.Actors;
using Aidyn.Core.Chunks;
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
/// out what follows. Combat (M3) is a mode here too, on a stage of trek's zone engine as a cut-scene is.
/// </remarks>
public sealed class GameSession
{
    private readonly ITrekHost _host;
    private (CinematicRecord Record, bool FromTheatre)? _cinematic;
    /// <summary>The pause menu reopens once trek has faded in, on the Theatre's page or as it was (trek's state 8).</summary>
    private bool? _reopenOnTheatre;
    private float _precipitationDensity = 1;

    /// <param name="host">What presents the game: trek's views and the transitions.</param>
    /// <param name="zone">The zone engine trek's world is loaded in (<see cref="TrekLoop.Zone"/>).</param>
    public GameSession(GameState game, ITrekHost host, ZoneEngine zone)
    {
        Game = game;
        _host = host;
        Trek = new TrekLoop(game, host, zone);
    }

    public GameState Game { get; }

    public TrekLoop Trek { get; }

    /// <summary>The zone engine trek's world is loaded in, which a cut-scene's stage is made from.</summary>
    public ZoneEngine Zone => Trek.Zone;

    /// <summary>The cut-scene playing, in place of trek's world.</summary>
    public CutScene? Scene { get; private set; }

    /// <summary>The cut-scene's stage: its own zone engine (<see cref="ZoneEngine.Stage"/>).</summary>
    public ZoneEngine? Stage { get; private set; }

    /// <summary>Whether the pause menu is open.</summary>
    public bool MenuOpen { get; private set; }

    /// <summary>Whether a backdrop's or the help menu's page shows, or is still fading out.</summary>
    public bool BackdropShowing { get; private set; }

    /// <summary>Whether the credits are rolling.</summary>
    public bool CreditsRolling { get; private set; }

    /// <summary>The cinematic playing.</summary>
    public CinematicRecord? Cinematic => _cinematic?.Record;

    /// <summary>
    /// What runs, the highest that's up: a cinematic, the credits, the pause menu, a backdrop's page, a popup, a
    /// conversation, a cut-scene, else trek.
    /// </summary>
    public GameMode Mode =>
        _cinematic is not null ? GameMode.Cinematic
        : CreditsRolling ? GameMode.Credits
        : MenuOpen ? GameMode.Menu
        : BackdropShowing ? GameMode.Backdrop
        : Trek.Use is { Popup: not null } ? GameMode.Popup
        : Trek.Conversation is not null ? GameMode.Conversation
        : Scene is not null ? GameMode.CutScene
        : GameMode.Trek;

    /// <summary>Whether the world is held still: under the pause menu, a backdrop's page, the credits or a cinematic.</summary>
    public bool Held => Mode is GameMode.Menu or GameMode.Backdrop or GameMode.Credits or GameMode.Cinematic;

    /// <summary>Whether trek steps this tick: nothing holds the world and no cut-scene plays in its place.</summary>
    public bool TrekRuns => !Held && Scene is null;

    /// <summary>How dense rain and snow fall, trek's and a stage's: the settings' multiple of the original's.</summary>
    public float PrecipitationDensity
    {
        get => _precipitationDensity;
        set
        {
            _precipitationDensity = value;
            Zone.Precipitation?.Density = value;
            Stage?.Precipitation?.Density = value;
        }
    }

    /// <summary>
    /// One 60 Hz tick: it counts in every mode (<see cref="GameState.CountTick"/>), then the mode that runs steps. Under
    /// the pause menu, a backdrop's page and the credits, the map's sounds go on with everything held still; a cinematic
    /// holds everything. A cut-scene ticks on its stage; trek reopens the menu once it has faded in after the credits or the
    /// Theatre, then ticks.
    /// </summary>
    /// <param name="readInput">The player's controls, read only when trek takes them.</param>
    /// <param name="frozen">Whether only time passes in trek, as while the free camera flies.</param>
    /// <param name="skyDrawn">Whether the stage's sky is drawn, for a cut-scene's weather.</param>
    /// <param name="actors">A cut-scene's actors' animations, or null for one that has none yet.</param>
    public SessionStep Tick(Func<PlayerInput> readInput, bool frozen, bool skyDrawn, Func<CutSceneActor, IActorMotion?> actors)
    {
        Game.CountTick();
        switch (Mode)
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
        if (_reopenOnTheatre is { } theatre && !Trek.CutSceneFade.Fading)
        {
            _reopenOnTheatre = null;
            _host.Transition(new Transition.ReopenMenu(theatre));
        }
        return Trek.Tick(readInput, frozen) switch
        {
            TrekStep.CutScene => SessionStep.CutSceneCued,
            TrekStep.Frozen => SessionStep.Frozen,
            _ => SessionStep.Trek,
        };
    }

    /// <summary>The map's sounds, while the world is held still: trek's, not a stage's.</summary>
    private void StepSounds()
    {
        if (Scene is null)
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
        Zone.Particles?.Clear();
        if (CutSceneStage.For(Game.Plugins, stageRef, Trek.Chunks.World) is not { } stage)
        {
            _host.Transition(new Transition.Say($"{script.Id} plays in a chunk no plugin has."));
            ResumeTrek(null);
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
        ResumeTrek(scene);
        if (scene.Runner.Ending is { } ending)
        {
            var chunk = scene.Stage.Chunk;
            Transition.CarryOut(Game, _host,
                Transition.AfterScript(Game, ending, ending.Speaker, (new ChunkRef(scene.Stage.Set.Id, chunk.Row, chunk.Column), chunk.Terrain)));
        }
    }

    /// <summary>Trek comes back where the player stood, its world built again and the screen fading in (<see cref="TrekLoop.Resume"/>).</summary>
    private void ResumeTrek(CutScene? scene)
    {
        _host.Transition(new Transition.EndCutScene(scene));
        Trek.Resume();
        _host.Transition(new Transition.ResumeTrek());
    }

    /// <summary>Asks trek for a cut-scene, unless one is playing (<see cref="TrekLoop.Cue"/>).</summary>
    public void Cue(DialogueRecord script, ScriptStart start, ChunkRef stage)
    {
        if (Scene is null)
            Trek.Cue(script, start, stage);
    }

    /// <summary>The pause menu has opened, on a tab or in its barter or training state.</summary>
    public void MenuOpened() => MenuOpen = true;

    /// <summary>
    /// The pause menu has rolled up, and trek carries on as its exit says (<see cref="PauseExit"/>): the party camps, the
    /// help menu's script starts, the credits roll or the Theatre's cinematic plays.
    /// </summary>
    public void MenuClosed(PauseExit exit)
    {
        MenuOpen = false;
        switch (exit.Code)
        {
            case PauseExitCode.Camp:
                _host.Transition(new Transition.Camp(Trek.Camp()));
                break;
            case PauseExitCode.Help:
                Trek.ShowBackdrop(Game.Plugins.Get<DialogueRecord>(SystemMenu.HelpScript));
                break;
            case PauseExitCode.Credits when Game.Plugins.TrySingleton<CreditsRecord>(RecordTypes.Credits) is not null:
                CreditsRolling = true;
                _host.Transition(new Transition.PlayCredits());
                break;
            case PauseExitCode.Theatre when exit.Cinematic is { } cinematic:
                PlayCinematic(cinematic, fromTheatre: true);
                break;
        }
    }

    /// <summary>A backdrop's or the help menu's page has opened over the world.</summary>
    public void BackdropOpened() => BackdropShowing = true;

    /// <summary>The page has faded out, and the game goes on.</summary>
    public void BackdropClosed() => BackdropShowing = false;

    /// <summary>
    /// Plays a cinematic (<c>Cinematic.Load</c>), unless one is playing: from the Theatre, the menu reopens on its page
    /// after it.
    /// </summary>
    public void PlayCinematic(CinematicRecord cinematic, bool fromTheatre)
    {
        if (_cinematic is not null)
            return;
        _cinematic = (cinematic, fromTheatre);
        _host.Transition(new Transition.PlayCinematic(cinematic));
    }

    /// <summary>
    /// The cinematic has ended (<c>Cinematic.EndCase</c>, <c>0x80053c20</c>): its done flag is set however it ended, from the
    /// Theatre too, and trek comes back where the player stood, its music asked for again and the screen fading in from
    /// black; from the Theatre, the menu reopens on its page once it has (trek's state 8).
    /// </summary>
    public void CinematicEnded()
    {
        var (cinematic, fromTheatre) = _cinematic ?? throw new InvalidOperationException("No cinematic is playing.");
        _cinematic = null;
        Game.Flags.TrySet(cinematic.DoneFlag, true);
        Trek.Resume();
        if (fromTheatre)
            _reopenOnTheatre = true;
    }

    /// <summary>
    /// The credits have ended (<c>Credits.Destroy</c>): trek fades in from black over half a second, and the menu reopens
    /// once it has, on the System tab's list.
    /// </summary>
    public void CreditsEnded()
    {
        CreditsRolling = false;
        Trek.CutSceneFade.In(TrekLoop.CutSceneFadePerTick);
        _reopenOnTheatre = false;
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
        var chunk = set.Chunks.FirstOrDefault(c => c.Row == start.Chunk.Row && c.Column == start.Chunk.Column)
            ?? throw new InvalidDataException($"{set.Label} has no chunk at row {start.Chunk.Row}, column {start.Chunk.Column}.");
        return (set, chunk.Name, new Vector3(start.Position.X, start.Height, start.Position.Y));
    }

    /// <summary>The player's body's actor: the party leader's (<see cref="EntityBody.Actor"/>), Alaron's in the base game.</summary>
    public RecordId? PlayerActor =>
        Game.Party.Members.FirstOrDefault() is { } leader ? Game.Plugins.TryGet<EntityRecord>(leader.Entity)?.Body?.Actor : null;

    /// <summary>The chunk the player is in, and whether its terrain has the outdoor sky, for the World tab's map and Camp.</summary>
    public (ChunkRef? Place, bool Outdoors) Where()
    {
        if (Trek.Player is null || Trek.Chunks.ChunkAt(Trek.Player.Sphere.Position) is not { } chunk)
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
