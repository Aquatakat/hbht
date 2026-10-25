using HBHT.Core.Chunks;
using HBHT.Plugins;
using HBHT.Core.Ui;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Game;

/// <summary>
/// The title screen in the runtime (<see cref="TitleLayer"/>): the session shows it over a new game whose trek has gone, and steps it
/// (<see cref="Core.GameSession.Title"/>); this draws its cards, logo and menu, and its fly-throughs' scenery, the view moved along each
/// fly-through's camera animation. New Game ends it into the game; Load Game loads the save as the System tab's does, and the game over
/// and the ending's credits start the scene again on it. See docs/systems/ui.md#the-title-screen.
/// </summary>
public partial class Game
{
    private TitleLayer? _titleLayer;
    /// <summary>The fly-through's scenery: the Overworld's chunks about its camera.</summary>
    private ChunkStreamerView? _sceneryView;
    /// <summary>The fly-through's scene, posed by its animation, whose camera the view is moved to.</summary>
    private FlycamDriver? _flycam;

    /// <summary>Whether the scene starts again on the title, as after the game over or the ending's credits (<c>Game.Restart</c>).</summary>
    private static bool _toTitle;

    /// <summary>Whether that title starts at its logo, without the intro cards, as Quit Game's Return to Title does.</summary>
    private static bool _titleLogo;

    /// <summary>The fly-through the title shows, kept from one title to the next as the original keeps its place (<c>Title.FlycamPlace</c>).</summary>
    private static int _titlePlace;

    /// <summary>The title showing, or null.</summary>
    private TitleScreen? Title => _session?.Title;

    /// <summary>
    /// Shows the title over the game just built (<see cref="Core.GameSession.ShowTitle"/>): with its cards, which a saved game to load lets
    /// A, B or Start skip, the user's choice, or straight to the logo for <c>--title logo</c>.
    /// </summary>
    private void StartTitle(bool cards)
    {
        var skippable = SaveSlot.List(DataPaths.Saves).Count > 0;
        _session!.ShowTitle(cards, skippable, _titlePlace);
    }

    /// <summary>The title has shown: the player's body, his shadow and the HUD go with trek, and the layer shows the title.</summary>
    private void ShowTitle(TitleScreen title)
    {
        _player!.Visible = false;
        if (_shadow is not null)
            _shadow.Visible = false;
        _camera!.Follow = null;
        _box!.Visible = false;
        ShowFade(0);
        _titleLayer!.Show(title);
    }

    /// <summary>A fly-through has started: its scenery drawn, and the view put on its camera at its first tick.</summary>
    private void ShowScenery(TitleFlycam flycam, ZoneEngine scenery, System.Numerics.Vector3 corner)
    {
        _sceneryView = new ChunkStreamerView(_plugins!, _models!, scenery.Chunks, null, _shadowTexture) { Name = "Title scenery" };
        AddChild(_sceneryView);
        _flycam = FlycamDriver.For(_models!, flycam, corner.ToGodot());
        if (_flycam is null)
            GD.PushWarning($"{flycam.Scene} has no camera to fly the title's scenery with.");
        else
            AddChild(_flycam);
        PoseFlycam(Title!);
        _camera!.Far = SkyView.Far;
        _camera.ResetPhysicsInterpolation();
    }

    /// <summary>The fly-through's scenery has gone.</summary>
    private void FreeScenery()
    {
        _sceneryView?.QueueFree();
        _sceneryView = null;
        _flycam?.QueueFree();
        _flycam = null;
    }

    /// <summary>
    /// A tick of the title the session stepped: the card or the logo and menu drawn, and the fly-through posed at its time, the view moved
    /// there and the session told where it looks, its scenery lit and darkened by its fade.
    /// </summary>
    private void DrawTitle()
    {
        // New Game ends the title on its last tick, and a save loaded starts the scene again.
        if (Title is not { } title || !IsInsideTree())
            return;
        _titlePlace = title.Place;
        _titleLayer!.Draw(title);
        if (_sceneryView is null)
            return;
        PoseFlycam(title);
        ScrollTextures();
        ShowFade(title.SceneryBrightness);
        if (_pointLights && _session!.Scenery is { } scenery)
        {
            scenery.BeginLightFrame(title.SceneryBrightness);
            _sceneryView.LightActors(scenery.Lights);
        }
    }

    /// <summary>Poses the fly-through at the title's time, and puts the view and the session's camera where its camera is.</summary>
    private void PoseFlycam(TitleScreen title)
    {
        if (_flycam?.Pose(title.Time) is not { } view)
            return;
        _camera!.ShowFrom(view);
        title.Look(view.Origin.ToNumerics(), (view.Origin - view.Basis.Z).ToNumerics());
    }

    /// <summary>The title has ended into the game: its layer goes.</summary>
    private void EndTitle()
    {
        FreeScenery();
        _titleLayer!.End();
    }

    /// <summary>
    /// A fly-through's scene (<c>Title.StartScenery</c> loads its Borg6 onto its Borg5): posed by its animation at a tick, it gives its
    /// camera's place in the world, which the scenery's chunks are in. Not drawn.
    /// </summary>
    private sealed partial class FlycamDriver : Node3D
    {
        private Camera3D _camera = null!;
        private AnimationPlayer? _player;
        private DrivenNodes _driven = null!;

        /// <param name="corner">Where in the world its scene's origin is: the corner of the chunk its coordinates are from.</param>
        public static FlycamDriver? For(Models models, TitleFlycam flycam, Vector3 corner)
        {
            if (models.Instantiate(flycam.Scene) is not { } scene || Nodes.FindAll<Camera3D>(scene).FirstOrDefault() is not { } camera)
                return null;
            var driver = new FlycamDriver
            {
                Name = "Title fly-through", Visible = false, Position = corner, _camera = camera, _driven = models.Glb(flycam.Scene)!.Driven,
            };
            // Its scene stands on its ticks' poses; the view's camera is smoothed between them.
            scene.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
            driver.AddChild(scene);
            var player = Nodes.FindAll<AnimationPlayer>(scene).FirstOrDefault();
            var animation = SceneNames.Animation(Convert.ToInt32(flycam.Animation.Key, 16));
            if (player is not null && player.HasAnimation(animation))
            {
                player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
                player.Play(animation);
                driver._player = player;
            }
            return driver;
        }

        /// <summary>The camera's place at a tick of the animation, from 1, as the cinematics' shots are posed (<see cref="CinematicView.Pose"/>).</summary>
        public Transform3D? Pose(int time)
        {
            if (!IsInsideTree())
                return null;
            if (_player is not null)
            {
                _player.Seek((time - 1) / 60.0, update: true);
                TransformTracks.Apply(_player, _driven);
            }
            return _camera.GlobalTransform;
        }
    }
}
