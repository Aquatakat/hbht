using System.Globalization;
using HBHT.Core.Rng;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Editor;

/// <summary>
/// A 3D preview: loads a glTF at run time and orbits it. Drag to turn, right- or middle-drag to
/// pan, and scroll to zoom. Its animations loop, and can be scrubbed and stepped by the tick
/// (space plays and pauses, comma and full stop step). An actor's preview adds its states, and can
/// run its state machine with the runtime's <see cref="ActorAnimator"/>.
/// </summary>
public partial class ModelView : VBoxContainer
{
    private readonly Label _note = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly OptionButton _animations = new() { TooltipText = "Animation" };
    private readonly Button _play = new() { Text = "Pause", CustomMinimumSize = new Vector2(64, 0), TooltipText = "Play or pause (space)" };
    private readonly HSlider _time = new() { CustomMinimumSize = new Vector2(200, 0), Step = 1, TooltipText = "Scrub by the tick (comma and full stop step)" };
    private readonly Label _tick = new() { CustomMinimumSize = new Vector2(110, 0) };
    private readonly CheckBox _wireframe = new() { Text = "Wireframe" };
    private readonly CheckBox _sceneCamera = new() { Text = "Scene camera", TooltipText = "Look through the scene's own camera, at 4:3" };
    private readonly CheckBox _emitters = new() { Text = "Emitters", ButtonPressed = true, TooltipText = "Mark particle emitters on the scene's locators, in their particles' brightest colour" };
    private readonly Control _frame = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
    private readonly SubViewportContainer _container = new() { Stretch = true, MouseFilter = MouseFilterEnum.Stop, FocusMode = FocusModeEnum.All };
    private readonly SubViewport _viewport = new() { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X };
    private readonly Camera3D _camera = new() { Fov = 45 };
    private readonly DirectionalLight3D _headlight = new() { LightEnergy = 0.8f };
    private readonly ActorPanel _actorPanel = new() { Visible = false, CustomMinimumSize = new Vector2(0, 220) };
    private readonly List<(MeshInstance3D Marker, Node3D Node)> _markers = [];
    private Node3D? _content;
    private Camera3D? _ownCamera;
    // The scene camera's own planes, from its Borg3, which a cinematic's shot replaces.
    private float _ownNear, _ownFar;
    private AnimationPlayer? _player;
    private MirroredMeshes? _mirroring;
    private bool _playing = true;
    private readonly Orbit _orbit = new();

    // An actor's: its record, the root transform and its pivot, and the animator while driving.
    private ActorRecord? _actor;
    private GlbScene? _glb;
    private Node3D? _root;
    private Vector3 _pivot, _motion;
    private ActorAnimator? _animator;
    private double _fraction;
    private HBHT.Plugins.PluginSet? _plugins;
    // Driving an actor runs its emitters, as the runtime does.
    private ParticlesView? _particles;
    private MeshInstance3D? _ground;

    public ModelView()
    {
        // Wireframes are drawn only for meshes made after this.
        RenderingServer.SetDebugGenerateWireframes(true);

        var bar = new HBoxContainer();
        bar.AddChild(_note);
        bar.AddChild(_animations);
        bar.AddChild(_play);
        bar.AddChild(_time);
        bar.AddChild(_tick);
        bar.AddChild(_emitters);
        bar.AddChild(_sceneCamera);
        bar.AddChild(_wireframe);
        AddChild(bar);

        var split = new VSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _container.GuiInput += OnViewInput;
        _container.AddChild(_viewport);
        _frame.AddChild(_container);
        _frame.Resized += Layout;
        split.AddChild(_frame);
        split.AddChild(_actorPanel);
        AddChild(split);

        _viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.16f, 0.17f, 0.2f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.45f, 0.45f, 0.45f),
            },
        });
        _headlight.RotationDegrees = new Vector3(-50, 35, 0);
        _viewport.AddChild(_headlight);
        _viewport.AddChild(_camera);

        _animations.ItemSelected += _ => PlaySelected();
        _play.Pressed += TogglePlay;
        _time.ValueChanged += OnScrub;
        _wireframe.Toggled += on => _viewport.DebugDraw = on ? Viewport.DebugDrawEnum.Wireframe : Viewport.DebugDrawEnum.Disabled;
        _sceneCamera.Toggled += _ => UseCamera();
        _emitters.Toggled += on => _markers.ForEach(m => m.Marker.Visible = on);

        _actorPanel.StateClicked += OnStateClicked;
        _actorPanel.DriveChanged += SetDrive;
        _actorPanel.RequestChanged += request =>
        {
            if (_animator is not null)
                _animator.Request = request;
        };
        _actorPanel.InPlaceChanged += _ =>
        {
            // Posed afresh, so turning it off puts the root back.
            if (_player is not null && _animator is null)
                _player.Seek(_player.CurrentAnimationPosition, update: true);
            ShowFrame();
        };
        _actorPanel.HomePressed += Home;
    }

    /// <summary>Runs an actor's state machine for a request, as the Drive switch does.</summary>
    public void Drive(int request)
    {
        if (_actor is not null)
            _actorPanel.Drive(request);
    }

    /// <summary>The installed plugins, for what a scene's asset doesn't hold: its emitters, and the cinematics' planes.</summary>
    public HBHT.Plugins.PluginSet? Plugins
    {
        get => _plugins;
        set
        {
            _plugins = value;
            _particles?.QueueFree();
            _particles = null;
        }
    }

    public void SetWireframe(bool on) => _wireframe.ButtonPressed = on;

    public void SetSceneCamera(bool on) => _sceneCamera.ButtonPressed = on;

    public void Clear()
    {
        _content?.QueueFree();
        _content = null;
        _ownCamera = null;
        _player = null;
        _actor = null;
        _animator = null;
        _particles?.Head.Clear();
        _root = null;
        _glb?.Dispose();
        _glb = null;
        _markers.Clear();
        _ground?.QueueFree();
        _ground = null;
        _animations.Clear();
        _animations.Visible = _play.Visible = _time.Visible = _tick.Visible = false;
        _emitters.Visible = _sceneCamera.Visible = false;
        _actorPanel.Visible = false;
        _note.Text = "";
    }

    /// <summary>Shows a glTF binary, playing <paramref name="animation"/> if it has one by that name.</summary>
    /// <param name="note">What the preview shows, when that isn't just the asset itself.</param>
    /// <param name="actor">The actor whose scene this is, to show its states and drive it.</param>
    public void Show(byte[] glb, string? animation, string? note, ActorRecord? actor = null)
    {
        Clear();
        _note.Text = note ?? "";

        GlbScene file;
        try
        {
            file = GlbScene.Parse(glb);
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException)
        {
            _note.Text = $"Not a glTF binary ({e.Message}).";
            return;
        }
        if (GlbFile.Load(file, out var error) is not { } scene)
        {
            file.Dispose();
            _note.Text = $"Godot couldn't load the glTF ({error}).";
            return;
        }
        _content = scene;
        _glb = file;
        _viewport.AddChild(scene);

        // The scene's own lights replace the headlight; unlit materials ignore both.
        _headlight.Visible = Nodes.FindAll<Light3D>(scene).Count == 0;
        var radius = Frame(scene);
        _ownCamera = Nodes.FindAll<Camera3D>(scene).FirstOrDefault();
        if (_ownCamera is not null)
            (_ownNear, _ownFar) = (_ownCamera.Near, _ownCamera.Far);
        _sceneCamera.Visible = _ownCamera is not null;
        UseCamera();
        // Sized for the view, so they show on effects with little or no geometry.
        MarkEmitters(file.Extras, scene, _orbit.Distance / 90);

        _mirroring = MirroredMeshes.For(scene, file.Extras);
        _player = Nodes.FindAll<AnimationPlayer>(scene).FirstOrDefault();
        if (_player is not null)
        {
            // Advanced here, so ticks can be stepped and the actor's root held.
            _player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
            var names = _player.GetAnimationList().Where(n => n != "RESET").Order(StringComparer.Ordinal).ToList();
            foreach (var name in names)
            {
                _player.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
                _animations.AddItem($"{name} ({Ticks(name)} ticks)");
                _animations.SetItemMetadata(_animations.ItemCount - 1, name);
            }
            if (names.Count > 0)
            {
                var start = animation is { } a && names.IndexOf(a) is >= 0 and var i ? i : 0;
                _animations.Select(start);
                _animations.Visible = _play.Visible = _time.Visible = _tick.Visible = true;
                PlaySelected();
            }
        }

        // An actor whose animations all hold it still has no player, but its states still run and start its emitters.
        if (actor is not null && (_player is not null || file.Extras.StillAnimations.Count > 0))
        {
            _actor = actor;
            var rootName = SceneNames.Transform(actor.RootTransform);
            _root = Nodes.FindAll<Node3D>(scene).FirstOrDefault(n => n.Name == rootName);
            _pivot = file.Clips(actor.RootTransform).Pivot.ToGodot();
            _motion = Vector3.Zero;
            var clips = file.Clips(actor.RootTransform);
            _actorPanel.Show(actor, n => clips.Clip(actor.States[n].Animation)?.Length ?? 0);
            _actorPanel.Visible = true;
            _ground = Ground(radius);
            _viewport.AddChild(_ground);
            ShowFrame();
        }
    }

    /// <summary>Plays the selected animation from its first tick.</summary>
    public void PlaySelected()
    {
        if (_player is null || Selected() is not { } name)
            return;
        _player.Play(name);
        _player.Seek(0, update: true);
        _time.MaxValue = Ticks(name);
        UseShotPlanes(name);
        SetPlaying(true);
        ShowFrame();
    }

    private string? Selected() => _animations.Selected < 0 ? null : _animations.GetItemMetadata(_animations.Selected).AsString();

    /// <summary>An animation's length in ticks: one loop, as the runtime counts it.</summary>
    private int Ticks(string name) =>
        _player is not null && _player.HasAnimation(name) ? (int)Math.Round(_player.GetAnimation(name).Length * 60) : 0;

    public override void _Process(double delta)
    {
        if (_player is null && _animator is null || !IsVisibleInTree())
            return;
        foreach (var (marker, node) in _markers)
        {
            // Markers keep one size, whatever the scale their node inherits.
            var scale = node.GlobalBasis.Scale;
            marker.Scale = new Vector3(1 / Math.Max(scale.X, 1e-4f), 1 / Math.Max(scale.Y, 1e-4f), 1 / Math.Max(scale.Z, 1e-4f));
        }
        if (!_playing)
            return;
        if (_animator is not null)
        {
            // The state machine ticks at 60 Hz, and the root's motion moves the whole actor.
            _fraction += delta * 60;
            var before = _motion;
            for (; _fraction >= 1; _fraction--)
            {
                var moved = _animator.TickMotion(_actorPanel.Request);
                _motion += new Vector3(moved.X, 0, moved.Z);
                _content!.Position = _motion;
                _particles?.Step();
            }
            _content!.Position = _motion;
            // The camera follows the actor over the ground's grid.
            _orbit.Target += _motion - before;
            _orbit.Apply(_camera);
        }
        else
        {
            _player?.Advance(delta);
        }
        ShowFrame();
    }

    /// <summary>Holds the actor's root if asked, and shows where the animation is.</summary>
    private void ShowFrame()
    {
        var tick = 0;
        if (_player is not null)
        {
            TransformTracks.Apply(_player, _glb!.Driven);
            if (_root is not null && _animator is null && _actorPanel.InPlace)
                ActorAnimator.HoldRoot(_root, _pivot);
            _mirroring?.Update();
            tick = (int)Math.Round(_player.CurrentAnimationPosition * 60);
            _time.SetValueNoSignal(tick);
        }
        if (_animator is { Machine: var machine })
        {
            _tick.Text = $"time {machine.Clock} / {machine.Length(machine.State)}";
            _actorPanel.Mark(machine.State, $"state {machine.State}, time {machine.Clock} / {machine.Length(machine.State)}");
        }
        else if (_player is not null)
        {
            _tick.Text = $"time {tick + 1} / {(int)_time.MaxValue}";
            if (_actor is not null)
                _actorPanel.Mark(_actor.States.ToList().FindIndex(s => ActorAnimator.AnimationName(s) == Selected()), "");
        }
    }

    private void TogglePlay() => SetPlaying(!_playing);

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        _play.Text = playing ? "Pause" : "Play";
    }

    private void OnScrub(double tick)
    {
        if (_player is null || _animator is not null)
            return;
        SetPlaying(false);
        _player.Seek(tick / 60, update: true);
        ShowFrame();
    }

    private void Step(int by)
    {
        if (_player is null || _animator is not null)
            return;
        SetPlaying(false);
        var tick = Math.Clamp(Math.Round(_player.CurrentAnimationPosition * 60) + by, 0, _time.MaxValue);
        _player.Seek(tick / 60, update: true);
        ShowFrame();
    }

    // ---- Actors ---------------------------------------------------------------------------------

    private void SetDrive(bool on)
    {
        if (_actor is null || _content is null || _glb is null)
            return;
        Home();
        _animator = on ? ActorAnimator.Create(_content, _actor, _glb, new Lcg(0)) : null;
        _fraction = 0;
        _particles?.Head.Clear();
        if (_animator is not null && _plugins is not null)
        {
            if (_particles is null)
            {
                _particles = new ParticlesView(_plugins) { TickFraction = () => _fraction };
                _viewport.AddChild(_particles);
            }
            _animator.Particles = _particles.For(_actor, _content);
        }
        _time.Editable = !on;
        if (_animator is not null)
        {
            _animator.Driven = true;
            SetPlaying(true);
        }
        else
        {
            PlaySelected();
        }
    }

    private void OnStateClicked(int state)
    {
        if (_actor is null)
            return;
        if (_animator is not null)
        {
            _animator.Enter(state);
            return;
        }
        var name = ActorAnimator.AnimationName(_actor.States[state]);
        for (var i = 0; i < _animations.ItemCount; i++)
        {
            if (_animations.GetItemMetadata(i).AsString() == name)
            {
                _animations.Select(i);
                PlaySelected();
                return;
            }
        }
    }

    private void Home()
    {
        _orbit.Target -= _motion;
        _orbit.Apply(_camera);
        _motion = Vector3.Zero;
        if (_content is not null)
            _content.Position = Vector3.Zero;
    }

    // ---- Cameras and markers --------------------------------------------------------------------

    /// <summary>The scene's camera, at 4:3, or the orbiting one filling the view.</summary>
    private void UseCamera()
    {
        if (_sceneCamera.ButtonPressed && _ownCamera is not null)
            _ownCamera.MakeCurrent();
        else
            _camera.MakeCurrent();
        Layout();
    }

    /// <summary>
    /// Gives the scene camera a cinematic shot's near and far planes while its animation plays, as the cinematic table
    /// sets them in place of the scene's Borg3's, and the Borg3's otherwise.
    /// </summary>
    private void UseShotPlanes(string animation)
    {
        if (_ownCamera is null)
            return;
        var shot = Plugins is null || !SceneNames.TryAnimation(animation, out var borg6) ? null
            : Plugins.All<CinematicRecord>(HBHT.Plugins.RecordTypes.Cinematic).SelectMany(c => c.Shots)
                .FirstOrDefault(s => Convert.ToInt32(s.Animation.Key, 16) == borg6);
        (_ownCamera.Near, _ownCamera.Far) = shot is null ? (_ownNear, _ownFar) : (shot.Near, shot.Far);
    }

    private void Layout()
    {
        var size = _frame.Size;
        var shown = size;
        if (_sceneCamera.ButtonPressed && _ownCamera is not null && size.Y > 0)
            shown = size.X / size.Y > 4f / 3 ? new Vector2(size.Y * 4 / 3, size.Y) : new Vector2(size.X, size.X * 3 / 4);
        _container.Position = ((size - shown) / 2).Floor();
        _container.Size = shown.Floor();
    }

    /// <summary>
    /// Puts a marker on each particle emitter on the scene's own locators (nodes the importer
    /// names <see cref="SceneNames.Emitter"/>), drawn through the model in its particles' brightest colour.
    /// </summary>
    private void MarkEmitters(SceneExtras extras, Node3D scene, float radius)
    {
        var emitters = extras.EmitterColors;
        if (emitters.Count == 0)
            return;
        foreach (var node in Nodes.FindAll<Node3D>(scene).ToList())
        {
            if (!SceneNames.TryEmitter(node.Name.ToString(), out var i) || i >= emitters.Count)
                continue;
            var c = emitters[i];
            var marker = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 12, Rings = 6 },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(c.X, c.Y, c.Z, 0.8f),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    NoDepthTest = true,
                    RenderPriority = 1,
                },
                Visible = _emitters.ButtonPressed,
            };
            node.AddChild(marker);
            _markers.Add((marker, node));
        }
        _emitters.Visible = true;
        _emitters.Text = $"Emitters ({emitters.Count}, {_markers.Count} here)";
    }

    /// <summary>A grid of lines a scene unit apart on the ground under an actor, to show it moving.</summary>
    private static MeshInstance3D Ground(float radius)
    {
        var mesh = new ImmediateMesh();
        var half = Math.Max(40, (int)(radius * 40));
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (var i = -half; i <= half; i++)
        {
            mesh.SurfaceAddVertex(new Vector3(i, 0, -half));
            mesh.SurfaceAddVertex(new Vector3(i, 0, half));
            mesh.SurfaceAddVertex(new Vector3(-half, 0, i));
            mesh.SurfaceAddVertex(new Vector3(half, 0, i));
        }
        mesh.SurfaceEnd();
        return new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.5f, 0.55f, 0.65f, 0.35f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
    }

    /// <summary>Points the camera at the meshes' bounds, from far enough to see them whole. Returns their radius.</summary>
    private float Frame(Node3D scene)
    {
        Aabb? bounds = null;
        foreach (var mesh in Nodes.FindAll<MeshInstance3D>(scene))
        {
            if (mesh.Mesh is null)
                continue;
            var box = mesh.GlobalTransform * mesh.GetAabb();
            bounds = bounds is { } b ? b.Merge(box) : box;
        }
        var aabb = bounds ?? new Aabb(-Vector3.One, 2 * Vector3.One);
        var radius = Mathf.Max(aabb.Size.Length() / 2, 0.01f);
        _orbit.Frame(_camera, aabb.GetCenter(), radius);
        return radius;
    }

    private void OnViewInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle }:
                _container.GrabFocus();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Space }:
                TogglePlay();
                _container.AcceptEvent();
                return;
            case InputEventKey { Pressed: true, Keycode: Key.Comma }:
                Step(-1);
                _container.AcceptEvent();
                return;
            case InputEventKey { Pressed: true, Keycode: Key.Period }:
                Step(1);
                _container.AcceptEvent();
                return;
        }
        _orbit.Handle(e, _camera);
    }
}
