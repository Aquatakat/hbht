using System.Runtime.CompilerServices;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Scripts;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>
/// What the runtime is to a <see cref="GameSession"/>, without drawing (docs/systems/playthrough.md, layer 3): it owns the session and
/// steps it a tick at a time, carries out each tick's ending as the runtime's <c>Game.Tick</c> does (begins and ends cut-scenes and
/// battles), streams trek's chunks about the player as the runtime's frame does, and carries out every <see cref="Transition"/> as
/// <c>Game.cs</c> does, recording each with its tick. Bodies move by their actors' own animations, made as the runtime's views make them.
/// What a player would press it asks of its policies: trek's input (<see cref="Input"/>), the pages and choices of conversations, signs,
/// backdrops and cut-scenes (<see cref="Conversations"/>), battles (<see cref="Battles"/>), and the pause menu's windows and trek's popups
/// (<see cref="Menus"/>). Cinematics and the credits are skipped or hurried as <see cref="PlaythroughOptions"/> says.
/// </summary>
public sealed partial class PlaythroughHost
{
    private readonly PluginSet _plugins;
    private readonly List<TransitionAt> _transitions = [];
    private readonly List<Note> _notes = [];
    private readonly List<PageTurned> _pages = [];
    private readonly List<BattleFought> _battles = [];

    /// <summary>The cut-scene's actors' animations, made as the runtime makes their views: after the tick that spawned them.</summary>
    private readonly Dictionary<CutSceneActor, IActorMotion> _actors = [];

    /// <summary>Each wanderer's animations, made the first time trek asks for them.</summary>
    private readonly ConditionalWeakTable<Wanderer, IRootMotion> _wanderers = [];

    /// <summary>The tick being stepped, which what it sends is stamped with: 0 before the first.</summary>
    private long _now;

    /// <summary>A save the System tab's Load chose, loaded once the tick that chose it is over.</summary>
    private string? _load;

    private PlaythroughHost(PluginSet plugins, PlaythroughOptions? options)
    {
        _plugins = plugins;
        Options = options ?? PlaythroughOptions.Default;
    }

    /// <summary>
    /// A new game (<see cref="GameState.NewGame"/>), begun as the title's New Game begins one: trek built where it arrives, and the
    /// triggers on the game's start fired (<see cref="GameSession.StartNewGame"/>), the base plugin's playing the opening cinematic.
    /// </summary>
    public static PlaythroughHost NewGame(PluginSet plugins, PlaythroughOptions? options = null)
    {
        var host = new PlaythroughHost(plugins, options);
        var game = GameState.NewGame(plugins);
        host.StartSession(game, GameStart.For(game), opening: true);
        return host;
    }

    /// <summary>
    /// A game from its state, a save's loaded with <see cref="GameState.Load"/>, where <paramref name="start"/> says or else where it
    /// stands (<see cref="GameStart.For"/>). A save of a new game, which stands nowhere, plays the opening as the runtime's load does,
    /// unless <paramref name="opening"/> says otherwise.
    /// </summary>
    public static PlaythroughHost From(GameState game, PlaythroughOptions? options = null, GameStart? start = null, bool? opening = null)
    {
        var host = new PlaythroughHost(game.Plugins, options);
        host.StartSession(game, start ?? GameStart.For(game), opening ?? game.StartsAsNewGame);
        return host;
    }

    public PlaythroughOptions Options { get; }

    /// <summary>The session playing. A save loaded from the System tab replaces it.</summary>
    public GameSession Session { get; private set; } = null!;

    /// <summary>The game being played: the session's, and before its first tick, the one it's being started on.</summary>
    public GameState Game => _game;

    private GameState _game = null!;

    /// <summary>Trek's input when nothing else has the controls: nothing held by default.</summary>
    public IInputPolicy Input { get; set; } = NoInput.Instance;

    /// <summary>How pages are read and choices taken: each page at once, the first choice not yet taken there by default.</summary>
    public IConversationPolicy Conversations { get; set; } = FirstChoice.Instance;

    /// <summary>Who plays a battle: won outright by default.</summary>
    public IBattlePolicy Battles { get; set; } = WinOutright.Instance;

    /// <summary>What's done in the pause menu and trek's popups: loot taken, the rest left, by default.</summary>
    public IMenuPolicy Menus { get; set; } = new TakeLootLeaveTheRest();

    /// <summary>How many ticks apart the host presses in the pause menu and on trek's popups, so fades and slides between presses play.</summary>
    public int MenuPressGap { get; set; } = 2;

    /// <summary>How long the pause menu may stay open before the host closes it itself, warning that it did: a minute of ticks.</summary>
    public int MenuPatience { get; set; } = 60 * 60;

    /// <summary>Ticks stepped and the wall time they took.</summary>
    public HostClock Clock { get; } = new();

    /// <summary>Every transition the session sent, in order, with its tick.</summary>
    public IReadOnlyList<TransitionAt> Transitions => _transitions;

    /// <summary>Every log line, warning and thing said to the player, in order, with its tick.</summary>
    public IReadOnlyList<Note> Notes => _notes;

    /// <summary>The warnings alone (<see cref="ITrekHost.Warn"/>).</summary>
    public IEnumerable<Note> Warnings => _notes.Where(n => n.Kind == NoteKind.Warn);

    /// <summary>Every page the host pressed on, in order, with the choice taken on those with choices.</summary>
    public IReadOnlyList<PageTurned> Pages => _pages;

    /// <summary>The pages with a choice taken.</summary>
    public IEnumerable<PageTurned> Choices => _pages.Where(p => p.Choice is not null);

    /// <summary>Every battle begun, in order, the one playing last.</summary>
    public IReadOnlyList<BattleFought> BattlesFought => _battles;

    /// <summary>Why the host stopped, once the game has ended; <see cref="HostEnd.None"/> while it plays.</summary>
    public HostEnd End { get; private set; }

    /// <summary>
    /// Whether trek has the player's controls: nothing runs over it or in its place, and it isn't going through a teleporter or fading
    /// into or out of a cut-scene, as when Start would open the pause menu.
    /// </summary>
    public bool PlayerHasControl => End == HostEnd.None && Session is { Mode: GameMode.Trek } session && !session.Trek.Teleporting
        && session.Trek.Cued is null && session.Trek.CuedBattle is null && !session.Trek.CutSceneFade.Fading;

    /// <summary>Starts a session on <paramref name="game"/> at <paramref name="start"/>, as the runtime builds its world.</summary>
    private void StartSession(GameState game, GameStart start, bool opening)
    {
        _game = game;
        foreach (var problem in game.Problems)
            Warn(problem);
        _actors.Clear();
        _wanderers.Clear();
        _page = null;
        var streamer = new ChunkStreamer(_plugins, game, new CollisionWorld(), start.Set, new VisibleWorld());
        var session = Session = GameSession.Start(game, this, new ZoneEngine(game, streamer), start, menuHost: this,
            savesFolder: Options.SavesFolder, playing: Options.Playing, combatHost: this);
        // The player's body plays the party leader's actor, as the runtime's does (Game.BuildPlayer).
        if (session.PlayerActor is { } id && _plugins.TryGet<ActorRecord>(id) is { } actor)
            session.Trek.PlayerAnimation = MotionOf(actor);
        if (opening)
            session.StartNewGame();
    }

    /// <summary>
    /// An actor's animations as the runtime's views make them (<c>Models.InstantiateActor</c>), on the game's animation stream: none, so
    /// it stands, for a scene with no animations.
    /// </summary>
    private IActorMotion MotionOf(ActorRecord actor) => ActorMotion.AsDrawn(_plugins, actor, Game.Random.Animation) ?? IActorMotion.Still;

    private IActorMotion MotionOf(RecordId actor) => _plugins.TryGet<ActorRecord>(actor) is { } record ? MotionOf(record) : IActorMotion.Still;

    /// <summary>
    /// One tick, as the runtime's: the presses the policies ask for go to the mode that runs, then the session ticks with the stick, then
    /// what the tick came to is carried out (a cut-scene or a battle cued begins, one over ends), trek's chunks follow the player, and the
    /// cut-scene's new actors get their animations. Null once the game has ended.
    /// </summary>
    public SessionStep? Step()
    {
        if (End != HostEnd.None)
            return null;
        _now = Clock.Ticks + 1;
        Clock.Start();
        _stepping = true;
        try
        {
            var session = Session;
            var stick = Deliver(session);
            if (End != HostEnd.None)
                return null;
            var step = session.Tick(Options.Playing, () => stick, false, actor => _actors.GetValueOrDefault(actor));
            // What Game.Tick does with the step, once the tick is drawn.
            switch (step)
            {
                case SessionStep.CutSceneCued:
                    session.BeginCutScene();
                    break;
                case SessionStep.BattleCued:
                    if (session.Trek.CuedBattle is { } setup)
                        Battles.Cued(this, setup);
                    session.BeginBattle();
                    break;
                case SessionStep.CutSceneOver:
                    session.EndCutScene();
                    break;
                case SessionStep.CombatOver:
                    session.EndBattle();
                    break;
            }
            if (_load is { } path)
            {
                _load = null;
                Load(path);
            }
            if (_restart is var (game, then))
            {
                _restart = null;
                RestartNow(game, then);
            }
            if (End == HostEnd.None)
                Follow();
            return step;
        }
        finally
        {
            _stepping = false;
            Clock.Stop();
        }
    }

    /// <summary>
    /// After a tick, what the runtime's frame does that the rules hang on: trek's chunks stream about the player while the session streams
    /// them (<see cref="GameSession.StreamsTrek"/>, <c>Game._Process</c>), and the cut-scene's actors spawned this tick get their
    /// animations, as their views are made after the tick that spawned them (<c>Game.SyncCutScene</c>).
    /// </summary>
    private void Follow()
    {
        var session = Session;
        if (session.StreamsTrek)
            session.Trek.Chunks.Update(session.Trek.Player.Sphere.Position);
        if (session.Scene is { } scene)
            MakeActors(scene);
    }

    private void MakeActors(CutScene scene)
    {
        foreach (var actor in scene.Actors)
        {
            if (!_actors.ContainsKey(actor))
                _actors[actor] = MotionOf(actor.Look.Actor);
        }
    }

    /// <summary>A game to start again on once the tick being stepped is over, and what to do then (<see cref="Restart"/>).</summary>
    private (GameState Game, Action<PlaythroughHost>? Then)? _restart;

    /// <summary>Whether a tick is being stepped.</summary>
    private bool _stepping;

    /// <summary>
    /// Starts the game again on <paramref name="game"/> where it stands (<see cref="GameStart.For"/>), with no opening, as a save loaded from
    /// the System tab starts again, and then does <paramref name="then"/>, such as cueing a battle: at once between ticks, or once the tick
    /// being stepped is over when a policy asks for it. The game goes on even if it had ended; the records so far are kept, and the
    /// policies stay.
    /// </summary>
    public void Restart(GameState game, Action<PlaythroughHost>? then = null)
    {
        if (_stepping)
            _restart = (game, then);
        else
            RestartNow(game, then);
    }

    private void RestartNow(GameState game, Action<PlaythroughHost>? then)
    {
        End = HostEnd.None;
        _menuOpen = 0;
        StartSession(game, GameStart.For(game), opening: false);
        then?.Invoke(this);
    }

    /// <summary>Steps until <paramref name="done"/> holds, the game ends, or <paramref name="most"/> ticks have been stepped. Why it stopped.</summary>
    public HostRun RunUntil(Func<PlaythroughHost, bool> done, long most)
    {
        for (long i = 0; i < most; i++)
        {
            if (done(this))
                return HostRun.Done;
            if (Step() is null)
                return HostRun.Ended;
        }
        return done(this) ? HostRun.Done : HostRun.OutOfTicks;
    }

    /// <summary>Steps until trek has the player's controls (<see cref="PlayerHasControl"/>).</summary>
    public HostRun RunUntilControl(long most) => RunUntil(h => h.PlayerHasControl, most);

    /// <summary>A press, to the mode that runs (<see cref="GameSession.Press"/>), as the player's between ticks.</summary>
    public PressOutcome Press(MenuAction action) => Session.Press(action);

    /// <summary>
    /// Wins the battle playing outright, if it's a turn the player plays (<see cref="Battle.ForceWin"/>), and notes that it was. Whether it
    /// did.
    /// </summary>
    public bool WinBattleOutright()
    {
        if (Session.Battle is not { } battle || !battle.ForceWin())
            return false;
        if (_battles.LastOrDefault() is { Ended: null } fought)
            fought.WonOutright = true;
        return true;
    }

    private void Note(NoteKind kind, string text) => _notes.Add(new Note(_now, kind, text));

    private void Warn(string text) => Note(NoteKind.Warn, text);
}

/// <summary>Why <see cref="PlaythroughHost.RunUntil"/> stopped.</summary>
public enum HostRun
{
    /// <summary>What it ran for came about.</summary>
    Done,

    /// <summary>The game ended (<see cref="PlaythroughHost.End"/>).</summary>
    Ended,

    /// <summary>It stepped as many ticks as it was given.</summary>
    OutOfTicks,
}
