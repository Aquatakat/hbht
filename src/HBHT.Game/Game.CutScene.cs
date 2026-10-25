using HBHT.Core.Chunks;
using HBHT.Core.Scripts;
using HBHT.Core.Ui;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Cut-scenes in the runtime: trek fades out, the session frees its world, loads the script's stage and starts the core's
/// <see cref="CutScene"/> on it (<see cref="Core.GameSession.BeginCutScene"/>), and this draws it; when it's over, the
/// session brings trek back where the player stood. See docs/systems/scripts.md.
/// </summary>
public partial class Game
{
    private ChunkStreamerView? _stageView;
    private readonly Dictionary<CutSceneActor, CutSceneActorView> _actorViews = [];

    /// <summary>The cut-scene playing, in place of trek's world.</summary>
    private CutScene? Scene => _session?.Scene;

    /// <summary>The cut-scene's stage, in place of trek's world: its own zone engine (<see cref="ZoneEngine.Stage"/>).</summary>
    private ZoneEngine? Stage => _session?.Stage;

    /// <summary>The chunks the world shows now: the title's fly-through's, the cut-scene's, the battle's arena, or trek's.</summary>
    private ChunkStreamerView? Streamer => Title is not null ? _sceneryView : Scene is not null ? _stageView : Battle is not null ? _arenaView : _chunks;

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
    /// The cut-scene has begun on its stage: its chunks drawn under the sky the session chose for it, and the view through its
    /// camera.
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
        _camera!.Follow = scene.Camera;
        _camera.Far = CutScene.Far;
        SyncCutScene();
        _camera.ResetPhysicsInterpolation();
    }

    /// <summary>
    /// What draws a tick of the cut-scene the session stepped: the stage's scenery goes on, and the actors, camera,
    /// box and lights follow it. Once its screen has faded out, the session ends it.
    /// </summary>
    private void StepCutScene(bool over)
    {
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
            view.Draw(_world, scene.Camera.Aim, _objectDistance.Bodies);
        }
        // A cut jumps the view rather than blending it over the tick. Godot resets interpolation from where the
        // camera is when it's called, so after the camera has moved.
        var cut = scene.Camera.TakeCut();
        _camera!.Sync();
        if (cut)
            _camera.ResetPhysicsInterpolation();
        // The box as the cut-scene lays it out, while it has a page.
        _box!.Box = scene.Box;
        _box.Visible = scene.Page is not null;
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
        stage.BeginLightFrame(Scene!.Fade.Brightness);
        _stageView!.LightActors(stage.Lights);
        foreach (var view in _actorViews.Values)
            view.Light(stage.Lights, stage.Chunks);
    }

    /// <summary>
    /// The cut-scene's stage has gone, or one couldn't start (<paramref name="scene"/> null): its actors and chunks are no
    /// longer drawn.
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
    }

    /// <summary>
    /// Trek is back where the player stood (<c>initGameTrek</c>): the player's body and the follow camera, aimed at him, and
    /// trek's sky, as the screen fades in.
    /// </summary>
    private void ShowTrek()
    {
        _player!.Visible = true;
        PlaceShadow();
        _camera!.Follow = Trek!.Camera;
        _camera.Far = SkyView.Far;
        _camera.Sync();
        _player.Sync();
        ResetInterpolation();
    }
}
