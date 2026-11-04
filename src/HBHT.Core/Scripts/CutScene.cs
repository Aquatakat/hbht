using System.Numerics;
using HBHT.Core.Actors;
using HBHT.Core.Cameras;
using HBHT.Core.Chunks;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Scripts;

/// <summary>
/// Where a cut-scene plays (<c>DialogueModeInitPrescripted_set_map</c>): the 3×3 chunks around its
/// staging chunk, which the runtime loads into <see cref="World"/> without their wanderers, and the
/// staging chunk, whose reference points' marks the script names.
/// </summary>
public sealed record CutSceneStage(MapSetRecord Set, ChunkRecord Chunk, CollisionWorld World)
{
    /// <summary>Where the staging chunk's coordinates start in the map set.</summary>
    public Vector3 Origin => Set.Origin(Chunk.Row, Chunk.Column);

    /// <summary>
    /// The reference point with a mark, in the staging chunk only (<c>FindReferncePoint</c>,
    /// <c>0x8000b6f0</c>): its position in the map set, and the way it faces.
    /// </summary>
    public ReferencePointObject? Point(int mark) => Chunk.Objects.OfType<ReferencePointObject>().FirstOrDefault(p => p.Mark == mark);

    /// <summary>A mark's position in the map set.</summary>
    public Vector3? PointAt(int mark) => Point(mark) is { } point ? Origin + point.Position : null;

    /// <summary>Where actors are kept: the staging chunk, 0.05 inside its edges.</summary>
    public (Vector2 Min, Vector2 Max) Bounds => Body.ChunkBounds(Origin, Set.CellSize);

    /// <summary>A chunk of a map set as a stage, or null if no plugin has it.</summary>
    public static CutSceneStage? For(PluginSet plugins, RecordId chunk, CollisionWorld world)
    {
        if (plugins.TryGet<ChunkRecord>(chunk) is not { } entry || plugins.TryGet<MapSetRecord>(entry.MapSet) is not { } set)
            return null;
        return new CutSceneStage(set, entry, world);
    }
}

/// <summary>
/// A cut-scene (a script whose mode is <see cref="ScriptMode.Cutscene"/>): the original's dialogue
/// mode with its own world (<c>Dialog_GameState</c>). Trek's world is gone while it plays; the script
/// spawns its actors at the stage's marks, walks, turns and animates them, hands them items,
/// and moves the camera, while its lines show in the conversation box. Checked in 1.0's code.
/// </summary>
/// <remarks>
/// <para>
/// It fades in over a second as it starts, after the script's first nodes have spawned its actors and
/// cut the camera. While it plays the clock stops. The box hides, and the player's presses are dropped,
/// while the screen fades and while the script waits; while the camera moves, the box shows but
/// presses are dropped. When the script ends it fades out over a second, and then
/// <see cref="Over"/> is set for the runtime to bring trek back.
/// </para>
/// <para>
/// A script's waits end as the original's do: a walk or run when no actor has been steered for 30
/// ticks and the camera is still (<c>ProcessPlayers</c>, <c>0x800136c4</c>), so an actor that can't get
/// there still ends it, as does one too far from the camera to move; an animation after the length of
/// the state its actor is in once the request has had its tick (<c>FUN_80058ad4</c>); the camera once it
/// has been still a step; a timer by its ticks.
/// </para>
/// <para>
/// Each cue is carried out on the stage as it comes, and what a wait waits for is a state of the stage
/// (the actors steered, the cameras moving), not of the cue. So scripts could one day share a stage and
/// run side by side.
/// </para>
/// </remarks>
public sealed class CutScene
{
    /// <summary>The fades in and out, a second each (<c>0x3C888889</c>).</summary>
    public const float FadePerTick = 1f / 60;

    /// <summary>A walk or run ends this long after no actor was steered (<c>0x1E</c>).</summary>
    public const int StillTicks = 30;

    /// <summary>An actor that comes no nearer where it's going for this long is put there (<c>0x12C</c>).</summary>
    public const int GiveUpTicks = 300;

    /// <summary>How far a walk or run stops from its mark (<c>SetAiDest</c>'s radius).</summary>
    public const float ArriveRadius = 1;

    /// <summary>The camera's near and far planes in a cut-scene (<c>RenderZones</c>, <c>0x8000d66c</c>): 0.1 and 125, against trek's 1 and 180.</summary>
    public const float Near = 0.1f, Far = 125;

    /// <summary>How long an animation the script plays is asked for: the original's frame it's set in and the next.</summary>
    private static readonly int StimulusTicks = Cadence.Frames(2);

    private readonly GameState _game;
    private readonly IScriptHost? _host;
    private readonly List<CutSceneActor> _actors = [];
    private readonly List<string> _problems = [];
    private int _still;
    /// <summary>The actor whose animation the script waits for, until its length is taken after the next step.</summary>
    private CutSceneActor? _animating;
    /// <summary>Ticks left of an animation's wait (<c>timer</c>, with <c>timerFlag</c>).</summary>
    private int? _animationTimer;
    private readonly SpeakerTalk _talk = new();
    /// <summary>
    /// The actors' sounds: Alaron's, which every body has until it's given its own (<c>Player.Reset</c>), and a cut-scene's
    /// actors keep.
    /// </summary>
    private readonly BodySounds? _actorSounds;
    /// <summary>The script's animation has no actor to play it, so its wait ends as soon as the script lets go.</summary>
    private bool _resumeNow;
    private bool _ending;

    /// <param name="host">Where sounds go, and every cue for the record.</param>
    public CutScene(GameState game, ScriptRecord script, ScriptStart start, CutSceneStage stage, IScriptHost? host = null)
    {
        _game = game;
        _host = host;
        Stage = stage;
        _actorSounds = game.Plugins.TryGet<EntityRecord>(GameState.Alaron)?.Sounds;
        Runner = game.RunScript(script, new Cues(this), start);
        Box = DialogueBox.For(game.Plugins);
        // DialogueModeInitPrescripted puts the camera at the stage's corner, looking along X, until the
        // script's first cut.
        Camera = new FollowCamera(stage.Origin, Vector2.UnitX);
        Camera.PlaceAt(stage.Origin);
        Camera.AimAt(stage.Origin + new Vector3(100, 0, 0));
    }

    public ScriptRunner Runner { get; }

    public CutSceneStage Stage { get; }

    /// <summary>The camera, which only the script's camera moves.</summary>
    public FollowCamera Camera { get; }

    public ScriptCameras Cameras { get; } = new();

    public ScreenFade Fade { get; } = new();

    /// <summary>How the actors move: the Movement setting (<see cref="SessionSettings.Movement"/>), always at the original's pace.</summary>
    public MovementMode Movement { get; set; }

    /// <summary>The actors spawned, in order. An actor spawned again leaves its first body standing, as in the original.</summary>
    public IReadOnlyList<CutSceneActor> Actors => _actors;

    /// <summary>What the original would crash on, or read past, that the port steps over.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Whether the script has ended and the screen faded out, so trek should come back.</summary>
    public bool Over => _ending && !Fade.Fading;

    /// <summary>What the conversation box shows: nothing while the screen fades or the script waits.</summary>
    public ScriptPage? Page => Fade.Fading ? null : Runner.Page;

    /// <summary>
    /// The box the page shows in, whose cursor the presses move (<see cref="Press(MenuAction)"/>): laid out here, with the page as
    /// it changes, and drawn by whoever presents the cut-scene while <see cref="Page"/> is up, sized to the UI screen.
    /// </summary>
    public DialogueBox Box { get; }

    /// <summary>The line the box was last laid out with, so a page is shown once, and again when its line changes.</summary>
    private ScriptLine? _shownLine;

    /// <summary>Whether a press reaches the box (<c>cutScene_control_func</c>): not while it fades, the script waits or the camera moves.</summary>
    public bool Accepts => !Fade.Fading && Runner.Status == ScriptStatus.Showing && !Cameras.Busy;

    /// <summary>The actor that is an entity, the latest spawned (<c>DialougGetActorAndID</c>).</summary>
    public CutSceneActor? Actor(RecordId? entity) => entity is { } id ? _actors.FindLast(a => a.Entity == id) : null;

    /// <summary>Runs the script's first nodes, then fades in (<c>DialogueModeInitPrescripted</c>).</summary>
    public void Start()
    {
        Runner.Start();
        Fade.In(FadePerTick);
        CheckEnd();
    }

    /// <summary>
    /// A button in a cut-scene, as in a conversation, but dropped while the screen fades, the script waits or the camera
    /// moves (<c>cutScene_control_func</c>, <see cref="Accepts"/>): A moves the box on, bringing up the choices behind a line
    /// that showed alone or taking the cursor's choice, and up and down move the cursor. Nothing skips it: 1.0 has no skip.
    /// Whether the press reached the box.
    /// </summary>
    public bool Press(MenuAction action)
    {
        if (!Accepts || Page is null)
            return false;
        switch (action)
        {
            case MenuAction.Accept:
                if (Box.Accept(Runner, advance => Press(advance)) != DialogueAccept.ShowedChoices)
                    ShowPage(force: true);
                return true;
            case MenuAction.Up or MenuAction.Down:
                Box.MoveCursor(action.Vertical());
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Lays the page out in the box when it's new: its line has changed, or the box was hidden while the screen faded or the
    /// script waited. After a press the page is laid out again whatever its line, as its choices may have changed.
    /// </summary>
    private void ShowPage(bool force)
    {
        var page = Page;
        if (page is null)
        {
            _shownLine = null;
            Box.Hide();
            return;
        }
        if (force || page.Line != _shownLine)
        {
            Box.Show(page);
            _shownLine = page.Line;
        }
    }

    /// <summary>
    /// The player moves the box on (<c>funcs2[1]</c>, <c>0x80051ed4</c>), if it takes presses: then,
    /// unless the script played an animation, the speaker talks for a tick (<see cref="SpeakerTalk"/>).
    /// </summary>
    /// <param name="advance">What the press does: <see cref="ScriptRunner.Continue"/>, <see cref="ScriptRunner.Choose"/> and so on.</param>
    /// <returns>Whether the press reached the box.</returns>
    public bool Press(Action advance)
    {
        if (!Accepts)
            return false;
        if (_talk.Press(advance) && Runner.Status == ScriptStatus.Showing && Actor(Runner.Speaker) is { } speaker)
            speaker.Body.Request = Body.Talk;
        CheckEnd();
        return true;
    }

    /// <summary>
    /// One tick on its stage as the zone engine draws it (<c>CutScene.PlayFrame</c>, <c>0x80050f50</c>): the actors are
    /// heard from the camera as they step (<see cref="Tick(Func{CutSceneActor, IActorMotion})"/>), then the stage's sounds
    /// from where the camera looks (<c>ProcessAudioBubbles</c>), then the weather (<c>HandleZoneEngineFrame</c>). The
    /// stage's <see cref="ZoneEngine.Camera"/> is this one's <see cref="Camera"/>.
    /// </summary>
    /// <param name="stage">The zone engine the stage is loaded in (<see cref="ZoneEngine.Stage"/>).</param>
    /// <param name="skyDrawn">Whether the stage's sky is drawn (the original's sky type 3).</param>
    /// <param name="animation">Each actor's animations, or null for one that has none yet, which stands, unheard.</param>
    public void Tick(ZoneEngine stage, bool skyDrawn, Func<CutSceneActor, IActorMotion?> animation)
    {
        stage.Listen();
        var keys = stage.Keys;
        Tick(actor => animation(actor) is { } motion ? keys.Sounding(motion, actor.Audio, _actorSounds, actor.Body.Sphere) : IActorMotion.Still);
        // HandleZoneEngineFrame runs Bgm.Update only while the screen isn't fading (0x8000e784), so the stage's music, asked for as
        // it loaded (CutScene.LoadStage, 0x80051984), takes over once it has faded in: trek's plays on through the fade, and stops
        // there if the stage's map set and chunk have none.
        stage.Audio.HoldMusic = Fade.Fading;
        stage.StepSounds(Camera.Aim);
        stage.StepWeather(skyDrawn, stage.Precipitation);
    }

    /// <summary>
    /// One tick (<c>Dialog_GameState</c>): the actors near the camera step, then the script's cameras, by
    /// the game's <see cref="GameState.Ticker"/>; then the script's waits count, unless it's fading out at
    /// its end; then the fade, and the actors stand again unless the script asked them for something.
    /// </summary>
    /// <param name="motion">Each actor's animations.</param>
    public void Tick(Func<CutSceneActor, IActorMotion> motion)
    {
        var steered = false;
        foreach (var actor in _actors)
        {
            var body = actor.Body;
            if (!body.Near(Camera.Aim))
                continue;
            body.Faded = Fade.Fading;
            // The Movement setting, at the original's pace: a cut-scene is timed by its script, and never plays brisk.
            body.Movement = Movement;
            body.Pace = 1;
            steered |= body.Steered;
            // ProcessPlayers holds a death's last frame for every body (Body.Mortal).
            body.Step(Stage.World, body.Mortal(motion(actor)));
        }
        Cameras.Step(Camera, _game.Ticker);

        if (!_ending)
        {
            // ProcessPlayers counts the ticks since an actor was steered, and a walk's wait ends at 30.
            _still = steered ? 0 : _still + 1;
            if (_still >= StillTicks && Runner.Wait == ScriptWait.Move && !Cameras.Busy)
                Runner.Resume();
            if (_animating is { } animating)
            {
                _animationTimer = motion(animating).StateLength;
                _animating = null;
            }
            if (_animationTimer is { } left)
            {
                _animationTimer = left > 1 ? left - 1 : null;
                if (_animationTimer is null && Runner.Wait == ScriptWait.Animation)
                    Runner.Resume();
            }
            Runner.Step(1);
        }
        Fade.Step();
        foreach (var actor in _actors)
            actor.Settle();
        // funcs1 at the start of the next frame: the camera's wait ends once it's still.
        if (!_ending && Runner.Wait == ScriptWait.Camera && !Cameras.Busy)
            Runner.Resume();
        CheckEnd();
        ShowPage(force: false);
        Box.Step();
    }

    /// <summary>
    /// After the script moves: an animation with no actor is waited for no longer, and once the script ends, its
    /// done flag set, the screen fades out (<c>0x80051f4c</c>).
    /// </summary>
    private void CheckEnd()
    {
        while (_resumeNow && Runner.Wait == ScriptWait.Animation)
        {
            _resumeNow = false;
            Runner.Resume();
        }
        _resumeNow = false;
        if (_ending || Runner.Status != ScriptStatus.Ended)
            return;
        _ending = true;
        if (Runner.Ending?.Fault is { } fault)
            _problems.Add(fault);
        Fade.Out(FadePerTick);
    }

    private void Problem(string what) => _problems.Add($"{Runner.Script.Id}: {what}");

    /// <summary>Carries out a cue on the stage (<c>DialoguePreCallback</c>, <c>DialogCallbackC</c>).</summary>
    private void Cue(ScriptCue cue)
    {
        _host?.Cue(cue);
        switch (cue)
        {
            case CameraCue camera:
                Move(camera);
                break;
            case SpawnCue spawn:
                Spawn(spawn);
                break;
            case FaceCue face:
                if (Speaker(face.Actor, "turn") is { } turner && Point(face.Mark) is { } toward)
                {
                    var at = turner.Body.Sphere.Position;
                    turner.Body.Face(new Vector2(toward.X - at.X, toward.Z - at.Z));
                }
                break;
            case MoveCue move:
                if (Speaker(move.Actor, "walk") is { } walker && Point(move.Mark) is { } to)
                    walker.Body.GoTo(new Vector2(to.X, to.Z), ArriveRadius, walk: !move.Run);
                break;
            case AnimateCue { Actor: { } animated } animate:
                _talk.Animated();
                if (Speaker(animated, "animate") is { } actor)
                {
                    actor.Stimulate(animate.Request, StimulusTicks);
                    _animating = actor;
                }
                // Its spawn failed on a mark the stage doesn't have, where the original reads garbage:
                // there's nothing to wait for.
                else
                {
                    _resumeNow = true;
                }
                break;
            case HoldCue hold:
                if (Speaker(hold.Actor, "hold an item") is not { } holder)
                    break;
                if (hold.Item is not { } item)
                    holder.Held = null;
                // An item with no model leaves what's held as it is (get_item_borg5 gives −1).
                else if (ItemModel(item) is { } model)
                    holder.Held = model;
                break;
            case FadeOutCue:
                Fade.Dip(FadePerTick);
                break;
        }
    }

    /// <summary>Spawns an actor at a mark, set down on the ground (<c>CreateActor</c>).</summary>
    private void Spawn(SpawnCue spawn)
    {
        Unhide();
        var entity = spawn.Actor;
        if (_game.Plugins.TryGet<DbRecord>(entity)?.Body is not { } look)
        {
            Problem($"{entity} has no body to spawn.");
            return;
        }
        if (Point(spawn.Mark) is not { } at)
            return;
        var body = Body.Spawn(Stage.World, at, look, _game.Random.World);
        body.SnapNextFace = true;
        body.GiveUpTicks = GiveUpTicks;
        body.Bounds = Stage.Bounds;
        _actors.Add(new CutSceneActor(entity, look, body));
    }

    /// <summary>
    /// Starts a camera move (<c>DialoguePreCallback</c>'s camera ops, from 1.0's jump table at <c>0x800d1d80</c>): moves to a
    /// mark move the camera or its aim there; moves to an actor move it to the actor's eyes, and a cut there hides the actor
    /// for a point of view. A turn eases unless it's steady (1.0's angle-to a point and pan-to an actor; the importer says
    /// which).
    /// </summary>
    private void Move(CameraCue cue)
    {
        var flags = cue.Shot switch
        {
            CameraShot.CutTo => CameraFlags.Position | CameraFlags.Cut,
            CameraShot.LookAt => CameraFlags.Aim | CameraFlags.Cut,
            CameraShot.TurnTo => CameraFlags.Aim | (cue.Steady ? CameraFlags.Pan : CameraFlags.Sine),
            CameraShot.MoveTo => CameraFlags.Position | (cue.Steady ? CameraFlags.Pan : CameraFlags.Sine),
            _ => CameraFlags.Track,
        };
        // FUN_80014dfc: these show every actor again, before a point-of-view cut hides its own.
        if (cue.Shot is CameraShot.CutTo or CameraShot.MoveTo)
            Unhide();
        Vector3? point = null;
        Vector3 focus;
        if (cue.Mark is { } mark)
        {
            // 1.0 reads the speaker's actor for a move to a point too, and crashes without one; the move doesn't use it,
            // so the op names none (ADR 0034).
            if (Point(mark) is not { } at)
                return;
            point = focus = at;
        }
        else
        {
            if (Speaker(cue.Actor, $"have the camera ({cue.Shot})") is not { } subject)
                return;
            if (cue.Shot == CameraShot.CutTo)
                subject.Hidden = true;
            focus = cue.Shot == CameraShot.Follow ? subject.Body.Sphere.Position : subject.Eyes;
        }
        if (!Cameras.Start(flags, point, focus, cue.Ticks))
            Problem($"starts an eighth camera move, where the original crashes (\"No Free Cameras!\").");
    }

    private void Unhide()
    {
        foreach (var actor in _actors)
            actor.Hidden = false;
    }

    /// <summary>The actor an op acts on, if it's spawned, or a problem noted: the original reads through a null pointer.</summary>
    private CutSceneActor? Speaker(RecordId? entity, string what)
    {
        if (Actor(entity) is { } actor)
            return actor;
        Problem($"{entity?.ToString() ?? "no actor"} isn't spawned to {what}.");
        return null;
    }

    /// <summary>A mark of the stage, or a problem noted: the original reads address 0.</summary>
    private Vector3? Point(int mark)
    {
        if (Stage.PointAt(mark) is { } at)
            return at;
        Problem($"names mark {mark}, which {Stage.Chunk.Id} doesn't have.");
        return null;
    }

    /// <summary>The scene the original draws for an item in a hand (<c>get_item_borg5</c>), if any.</summary>
    private RecordId? ItemModel(RecordId item) => item.Type == RecordTypes.Item ? _game.Plugins.TryGet<ItemRecord>(item)?.Model : null;

    private sealed class Cues(CutScene scene) : IScriptHost
    {
        public void Cue(ScriptCue cue) => scene.Cue(cue);
    }
}
