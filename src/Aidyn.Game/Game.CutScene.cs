using Aidyn.Core;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Cut-scenes in the runtime: trek fades out, its world goes, the script's stage loads,
/// and the core's <see cref="CutScene"/> plays on it; when it's over, trek comes back where the player
/// stood. See docs/systems/scripts.md.
/// </summary>
public partial class Game
{
    private CutScene? _scene;
    /// <summary>The cut-scene's chunks, in place of trek's.</summary>
    private Core.Chunks.ChunkStreamer? _stage;
    private ChunkStreamerView? _stageView;
    private readonly Dictionary<CutSceneActor, CutSceneActorView> _actorViews = [];
    /// <summary>What each actor's sounds keep: they all have Alaron's (<see cref="TrekLoop.AlaronSounds"/>).</summary>
    private readonly Dictionary<CutSceneActor, Core.Audio.BodyAudio> _actorAudio = [];
    /// <summary>The line the box last showed in the cut-scene, to show a new page once.</summary>
    private ScriptLine? _sceneLine;
    /// <summary>
    /// The rain and snow on the stage, about the cut-scene's camera and landing on the stage's collision, which the
    /// weather's one decision makes and steers as trek's are (<c>HandleZoneEngineFrame</c> runs <c>ProcessWeather</c> in
    /// the cut-scene's frames).
    /// </summary>
    private Core.Particles.WeatherParticles? _stageFall;
    /// <summary>
    /// The stage's point lights: its chunks' light map objects, which light the actors its maps place as trek's do, and
    /// dynamic lights of its own zone engine's, which light the cut-scene's actors.
    /// </summary>
    private Core.Lights.WorldLights? _stageLights;

    /// <summary>The chunks the world shows now: the cut-scene's, or trek's.</summary>
    private ChunkStreamerView? Streamer => _scene is not null ? _stageView : _chunks;

    /// <summary>Asks trek for a cut-scene, unless one is playing (<see cref="TrekLoop.Cue"/>).</summary>
    private void CueCutScene(DialogueRecord script, ScriptStart start, ChunkRef stage)
    {
        if (_scene is null)
            _trek!.Cue(script, start, stage);
    }

    /// <summary>
    /// Plays a cut-scene from the command line (<c>--cutscene aidyn:dialogue/0x0365</c>), as the first dialogue object
    /// that starts it would, or else the first talker, in its own chunk.
    /// </summary>
    private void CueFromCommandLine(RecordId script)
    {
        if (_plugins!.TryGet<DialogueRecord>(script) is not { Mode: DialogueMode.Cutscene } record)
        {
            Say($"{script} isn't a cut-scene.");
            return;
        }
        var chunks = _plugins.All<MapSetRecord>(RecordTypes.MapSet)
            .SelectMany(s => s.Chunks.Select(c => (c.Map, Ref: new ChunkRef(s.Id, c.Row, c.Column))))
            .GroupBy(c => c.Map).ToDictionary(g => g.Key, g => g.First().Ref);
        var maps = _plugins.All<MapRecord>(RecordTypes.Map).ToList();
        foreach (var map in maps)
        {
            if (map.Objects.OfType<DialogueObject>().FirstOrDefault(d => d.Script == script && d.Stage is not null) is { } dialogue)
            {
                CueCutScene(record, new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index)), dialogue.Stage!);
                return;
            }
        }
        foreach (var map in maps)
        {
            if (map.Objects.OfType<MonsterPartyObject>().FirstOrDefault(p => p.Dialogue == script) is { } party && chunks.TryGetValue(map.Id, out var own))
            {
                CueCutScene(record, new ScriptStart(0, MapRecord.VoxelId(map.Id, party.Index)), own);
                return;
            }
        }
        Say($"Nothing starts {script}.");
    }

    /// <summary>
    /// Trek's world goes (<c>clear_HUD_elements</c>): its chunks, wanderers and the player's body. Then the
    /// stage's 3×3 loads, without wanderers, and the script starts (<c>DialogueModeInitPrescripted</c>).
    /// </summary>
    private void BeginCutScene()
    {
        var (script, start, stageRef) = _trek!.BeginCutScene();
        _changed.Clear();
        // The zone engine starts again for the cut-scene, with a new particle head.
        _particles!.Head.Clear();
        _player!.Visible = false;
        if (_shadow is not null)
            _shadow.Visible = false;
        if (Core.Scripts.CutSceneStage.For(_plugins!, stageRef, _world) is not { } stage)
        {
            Say($"{script.Id} plays in a chunk no plugin has.");
            EndTrekPause();
            return;
        }
        _stage = new Core.Chunks.ChunkStreamer(_plugins!, _game!, _world, stage.Set);
        // The stage's chunks play their sounds, and its map set and chunk ask for their music (InitZoneEngine).
        var sounds = _trek.Sounds;
        _stage.Loaded += (_, map) => sounds.Load(map);
        _stage.Unloading += (_, map) => sounds.Unload(map);
        var lights = _stageLights = new Core.Lights.WorldLights(_game!.Random.Cosmetic) { World = _world };
        _stage.Loaded += (_, map) => lights.Load(map);
        _stage.Unloading += (_, map) => lights.Unload(map);
        _audio!.Music = Core.Audio.MapMusic.For(stage.Set, stage.Chunk, _game!.Random.Cosmetic);
        _stageView = new ChunkStreamerView(_plugins!, _models!, _stage, null, _shadowTexture) { Name = "Stage" };
        AddChild(_stageView);
        var middle = stage.Origin + new System.Numerics.Vector3(stage.Set.CellSize / 2, 0, stage.Set.CellSize / 2);
        _stage.Update(middle);
        if (_game.Weather is { } weather)
        {
            var streamer = _stage;
            _stageFall = new Core.Particles.WeatherParticles(_particles.Head, weather.Rules, _game.Random.Cosmetic,
                () => _scene?.Camera.Aim ?? middle, () => _scene?.Camera.LookXZ ?? System.Numerics.Vector2.UnitX,
                () => _world, () => streamer.Centre)
            {
                Density = Math.Clamp(_settings.PrecipitationDensity, 0, Core.Particles.WeatherParticles.MaxDensity),
            };
        }
        // FreeZoneEngine leaves no sky, so the stage's comes at once, and the scenery's fog ignores the weather.
        _sky!.ClearSky();
        _sky.Enter(stage.Chunk);
        _sky.CutScene = true;
        _sky.Apply();

        var scene = _scene = new CutScene(_game!, script, start, stage, this);
        _camera!.Follow = scene.Camera;
        _camera.Far = CutScene.Far;
        _sceneLine = null;
        scene.Start();
        SyncCutScene();
        _camera.ResetPhysicsInterpolation();
    }

    /// <summary>One tick of the cut-scene. The clock stops; the sky and the stage's scenery go on.</summary>
    private void StepCutScene()
    {
        var scene = _scene!;
        var keys = _trek!.Keys;
        keys.Eye = scene.Camera.Position;
        keys.Aim = scene.Camera.Aim;
        scene.Tick(actor => _actorViews.TryGetValue(actor, out var view)
            ? keys.Sounding(view.Motion, _actorAudio.TryGetValue(actor, out var audio) ? audio : _actorAudio[actor] = new(), _trek.AlaronSounds, actor.Body.Sphere)
            : IActorMotion.Still);
        // ProcessAudioBubbles hears the stage from where the camera looks.
        _trek.Sounds.Step(scene.Camera.Aim, scene.Camera.Position, scene.Camera.Aim);
        // The zone engine draws the stage, and its weather and its rain, as it draws trek.
        _trek.StepWeather(_sky!.State.Drawn, _stageFall);
        _sky.Step();
        ScrollTextures();
        SyncCutScene();
        if (_pointLights)
            LightStage();
        if (scene.Over)
            EndCutScene();
    }

    /// <summary>Draws the actors, the camera, the box and the fade where the cut-scene has them.</summary>
    private void SyncCutScene()
    {
        var scene = _scene!;
        foreach (var actor in scene.Actors)
        {
            if (!_actorViews.TryGetValue(actor, out var view))
            {
                view = _actorViews[actor] = new CutSceneActorView(actor, _plugins!, _models!, _shadowTexture);
                AddChild(view);
                // It was placed as it was made; Godot resets interpolation from where a node is now.
                view.ResetPhysicsInterpolation();
            }
            view.Sync();
            view.Draw(_world, scene.Camera.Aim);
        }
        // A cut jumps the view rather than blending it over the tick. Godot resets interpolation from where the
        // camera is when it's called, so after the camera has moved.
        var cut = scene.Camera.TakeCut();
        _camera!.Sync();
        if (cut)
            _camera.ResetPhysicsInterpolation();
        ShowCutScenePage(force: false);
        ShowFade(scene.Fade.Brightness);
    }

    /// <summary>
    /// Lights the stage as the zone engine draws it in a cut-scene's frame: the actors its maps place, by every light of
    /// their chunk (<c>RenderVoxelScenes</c>), then the cut-scene's actors, as bodies that aren't the party's
    /// (<c>RenderPlayers</c>). Lights that face the camera shine from the cut-scene's eye.
    /// </summary>
    private void LightStage()
    {
        var lights = _stageLights!;
        lights.Fade = _scene!.Fade.Brightness;
        lights.Eye = _scene.Camera.Position;
        lights.Sun = _sky!.State.Drawn;
        lights.BeginFrame(1);
        _stageView!.LightActors(lights);
        foreach (var view in _actorViews.Values)
            view.Light(lights, _stage!);
    }

    /// <summary>Shows the cut-scene's page in the box, or hides it while there's none.</summary>
    private void ShowCutScenePage(bool force)
    {
        var page = _scene!.Page;
        if (page is null)
        {
            _box!.Visible = false;
            _sceneLine = null;
        }
        else if (force || page.Line != _sceneLine || !_box!.Visible)
        {
            _box!.Show(page);
            _sceneLine = page.Line;
        }
    }

    /// <summary>
    /// A button in a cut-scene, as in a conversation, but dropped while the screen fades, the script
    /// waits or the camera moves (<c>cutScene_control_func</c>). Nothing skips it: 1.0 has no skip.
    /// </summary>
    private void CutSceneInput(InputEvent e)
    {
        var scene = _scene!;
        if (!scene.Accepts || scene.Page is not { } page)
            return;
        var runner = scene.Runner;
        var box = _box!.Box;
        if (e.IsActionPressed("menu_accept"))
        {
            if (box.ShowsLineBeforeChoices)
            {
                scene.Press(_box.ShowChoices);
                return;
            }
            if (page.Choices.Count > 0 && runner.Wait != ScriptWait.Player)
                scene.Press(() => runner.Choose(box.Highlight));
            else
                scene.Press(runner.Continue);
            ShowCutScenePage(force: true);
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
    /// The cut-scene is over (<c>load_one_of_two_cinematics</c>): its actors and stage go, and trek
    /// starts again where the player stood (<c>initGameTrek</c>), every map object gated again and every
    /// wanderer at its home node. The camera starts where it was, aimed at the player, and the screen
    /// fades in.
    /// </summary>
    private void EndCutScene()
    {
        var scene = _scene!;
        _scene = null;
        // The stage's zone engine ends, and its pool with the rain; the weather drawn carries on into trek.
        _game!.Weather?.Forget(_stageFall);
        _stageFall?.Clear();
        _stageFall = null;
        _particles!.Head.Clear();
        foreach (var view in _actorViews.Values)
            view.QueueFree();
        _actorViews.Clear();
        _actorAudio.Clear();
        _stage!.Clear();
        _stage = null;
        _stageLights = null;
        _stageView!.QueueFree();
        _stageView = null;
        _world.Clear();
        _box!.Visible = false;
        foreach (var problem in scene.Problems)
            GD.PushWarning(problem);
        EndTrekPause();
        if (scene.Runner.Ending is { } ending)
            CarryOut(ending, ending.Speaker);
    }

    /// <summary>Trek's world, the player's body and the follow camera come back, and the screen fades in (<see cref="TrekLoop.Resume"/>).</summary>
    private void EndTrekPause()
    {
        // Trek's world is built again, and its sky chosen at once.
        _sky!.ClearSky();
        _sky.CutScene = false;
        _trek!.Resume();
        _player!.Visible = true;
        PlaceShadow();
        _sky!.Apply();
        _camera!.Follow = _follow;
        _camera.Far = SkyView.Far;
        _camera.Sync();
        _player.Sync();
        ResetInterpolation();
    }
}
