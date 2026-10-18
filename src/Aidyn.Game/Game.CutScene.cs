using Aidyn.Core;
using Aidyn.Core.Chunks;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
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
    /// <summary>The cut-scene's stage, in place of trek's world: its own zone engine (<see cref="ZoneEngine.Stage"/>).</summary>
    private ZoneEngine? _stage;
    private ChunkStreamerView? _stageView;
    private readonly Dictionary<CutSceneActor, CutSceneActorView> _actorViews = [];
    /// <summary>The line the box last showed in the cut-scene, to show a new page once.</summary>
    private ScriptLine? _sceneLine;

    /// <summary>The chunks the world shows now: the cut-scene's, or trek's.</summary>
    private ChunkStreamerView? Streamer => _scene is not null ? _stageView : _chunks;

    /// <summary>Asks trek for a cut-scene, unless one is playing (<see cref="TrekLoop.Cue"/>).</summary>
    private void CueCutScene(DialogueRecord script, ScriptStart start, ChunkRef stage)
    {
        if (_scene is null)
            _trek!.Cue(script, start, stage);
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
        // The stage's chunks play their sounds and light its actors, and its map set and chunk ask for their music
        // (InitZoneEngine); its rain falls about the cut-scene's camera.
        var zone = _stage = _trek.Zone.Stage(stage.Set);
        zone.Precipitation?.Density = Math.Clamp(_settings.PrecipitationDensity, 0, Core.Particles.WeatherParticles.MaxDensity);
        zone.RequestMusic(stage.Chunk);
        _stageView = new ChunkStreamerView(_plugins!, _models!, zone.Chunks, null, _shadowTexture) { Name = "Stage" };
        AddChild(_stageView);
        zone.Chunks.Update(stage.Origin + new System.Numerics.Vector3(stage.Set.CellSize / 2, 0, stage.Set.CellSize / 2));
        // FreeZoneEngine leaves no sky, so the stage's comes at once, and the scenery's fog ignores the weather.
        _sky!.ClearSky();
        _sky.Enter(stage.Chunk);
        _sky.CutScene = true;
        _sky.Apply();

        var scene = _scene = new CutScene(_game!, script, start, stage, this);
        zone.Camera = scene.Camera;
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
        // The zone engine draws the stage, its sounds, weather and rain, as it draws trek (CutScene.Tick).
        scene.Tick(_stage!, _sky!.State.Drawn, actor => _actorViews.TryGetValue(actor, out var view) ? view.Motion : null);
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
        var stage = _stage!;
        stage.BeginLightFrame(_scene!.Fade.Brightness, _sky!.State.Drawn);
        _stageView!.LightActors(stage.Lights);
        foreach (var view in _actorViews.Values)
            view.Light(stage.Lights, stage.Chunks);
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
    private void CutSceneInput(MenuAction action)
    {
        var scene = _scene!;
        if (!scene.Accepts || scene.Page is null)
            return;
        var box = _box!.Box;
        if (action == MenuAction.Accept)
        {
            if (_box.Accept(scene.Runner, advance => scene.Press(advance)) != DialogueAccept.ShowedChoices)
                ShowCutScenePage(force: true);
        }
        else if (action is MenuAction.Up or MenuAction.Down)
        {
            _box.Point(box.Highlight + MenuStick.Vertical(action));
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
        _stage!.End();
        _stage = null;
        foreach (var view in _actorViews.Values)
            view.QueueFree();
        _actorViews.Clear();
        _stageView!.QueueFree();
        _stageView = null;
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
