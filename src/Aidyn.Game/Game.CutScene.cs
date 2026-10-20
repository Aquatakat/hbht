using Aidyn.Core.Chunks;
using Aidyn.Core.Scripts;
using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Cut-scenes in the runtime: trek fades out, the session frees its world, loads the script's stage and starts the core's
/// <see cref="CutScene"/> on it (<see cref="Core.GameSession.BeginCutScene"/>), and this draws it; when it's over, the
/// session brings trek back where the player stood. See docs/systems/scripts.md.
/// </summary>
public partial class Game
{
    private ChunkStreamerView? _stageView;
    private readonly Dictionary<CutSceneActor, CutSceneActorView> _actorViews = [];
    /// <summary>The line the box last showed in the cut-scene, to show a new page once.</summary>
    private ScriptLine? _sceneLine;

    /// <summary>The cut-scene playing, in place of trek's world.</summary>
    private CutScene? Scene => _session?.Scene;

    /// <summary>The cut-scene's stage, in place of trek's world: its own zone engine (<see cref="ZoneEngine.Stage"/>).</summary>
    private ZoneEngine? Stage => _session?.Stage;

    /// <summary>The chunks the world shows now: the cut-scene's, the battle's arena, or trek's.</summary>
    private ChunkStreamerView? Streamer => Scene is not null ? _stageView : Battle is not null ? _arenaView : _chunks;

    /// <summary>
    /// Trek has faded out into a cut-scene: the player's body goes with trek's world, and the session begins it
    /// (<c>clear_HUD_elements</c>, <c>DialogueModeInitPrescripted</c>).
    /// </summary>
    private void BeginCutScene()
    {
        _changed.Clear();
        _player!.Visible = false;
        if (_shadow is not null)
            _shadow.Visible = false;
        _session!.BeginCutScene();
    }

    /// <summary>
    /// The cut-scene has begun on its stage: its chunks drawn, its sky chosen at once with the scenery's fog ignoring the
    /// weather (FreeZoneEngine leaves no sky), and the view through its camera.
    /// </summary>
    private void ShowStage(CutScene scene, ZoneEngine stage)
    {
        // Trek's world has gone, the player's body with it, however the cut-scene began: cued in trek, or at once, as after
        // a cinematic or another cut-scene.
        _changed.Clear();
        _player!.Visible = false;
        if (_shadow is not null)
            _shadow.Visible = false;
        _stageView = new ChunkStreamerView(_plugins!, _models!, stage.Chunks, null, _shadowTexture) { Name = "Stage" };
        AddChild(_stageView);
        _sky!.ClearSky();
        _sky.Enter(scene.Stage.Chunk);
        _sky.CutScene = true;
        _sky.Apply();
        _camera!.Follow = scene.Camera;
        _camera.Far = CutScene.Far;
        _sceneLine = null;
        SyncCutScene();
        _camera.ResetPhysicsInterpolation();
    }

    /// <summary>
    /// What draws a tick of the cut-scene the session stepped: the sky and the stage's scenery go on, and the actors, camera,
    /// box and lights follow it. Once its screen has faded out, the session ends it.
    /// </summary>
    private void StepCutScene(bool over)
    {
        _sky!.Step();
        ScrollTextures();
        SyncCutScene();
        if (_pointLights)
            LightStage();
        if (over)
            _session!.EndCutScene();
    }

    /// <summary>Draws the actors, the camera, the box and the fade where the cut-scene has them.</summary>
    private void SyncCutScene()
    {
        var scene = Scene!;
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
        var stage = Stage!;
        stage.BeginLightFrame(Scene!.Fade.Brightness, _sky!.State.Drawn, _sky.Moon);
        _stageView!.LightActors(stage.Lights);
        foreach (var view in _actorViews.Values)
            view.Light(stage.Lights, stage.Chunks);
    }

    /// <summary>Shows the cut-scene's page in the box, or hides it while there's none.</summary>
    private void ShowCutScenePage(bool force)
    {
        var page = Scene!.Page;
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
        var scene = Scene!;
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
    /// The cut-scene's stage has gone, or one couldn't start (<paramref name="scene"/> null): its actors and chunks are no
    /// longer drawn, and the sky is cleared, so trek's chunk chooses its own at once as trek's world is built again.
    /// </summary>
    private void FreeStage(CutScene? scene)
    {
        if (scene is not null)
        {
            foreach (var view in _actorViews.Values)
                view.QueueFree();
            _actorViews.Clear();
            _stageView!.QueueFree();
            _stageView = null;
            _box!.Visible = false;
        }
        _sky!.ClearSky();
        _sky.CutScene = false;
    }

    /// <summary>
    /// Trek is back where the player stood (<c>initGameTrek</c>): the player's body and the follow camera, aimed at him, and
    /// the sky trek's chunk chose, as the screen fades in.
    /// </summary>
    private void ShowTrek()
    {
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
