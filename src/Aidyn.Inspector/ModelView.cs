using Aidyn.Rom.Inspection;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// A 3D preview: loads a <see cref="ModelPreview"/>'s glTF at run time and orbits it. Drag to turn,
/// right- or middle-drag to pan, and scroll to zoom. Its animations loop.
/// </summary>
public partial class ModelView : VBoxContainer
{
    private readonly Label _note = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly OptionButton _animations = new() { TooltipText = "Animation" };
    private readonly Button _play = new() { Text = "Pause", CustomMinimumSize = new Vector2(72, 0) };
    private readonly SubViewport _viewport = new() { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X };
    private readonly Camera3D _camera = new() { Fov = 45 };
    private readonly DirectionalLight3D _headlight = new() { LightEnergy = 0.8f };
    private Node3D? _content;
    private AnimationPlayer? _player;
    private Vector3 _target;
    private float _yaw = 0.6f, _pitch = -0.35f, _distance = 5;

    public ModelView()
    {
        var bar = new HBoxContainer();
        bar.AddChild(_note);
        bar.AddChild(_animations);
        bar.AddChild(_play);
        AddChild(bar);

        var container = new SubViewportContainer
        {
            Stretch = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
        };
        container.GuiInput += OnViewInput;
        container.AddChild(_viewport);
        AddChild(container);

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

        _animations.ItemSelected += i => PlaySelected();
        _play.Pressed += TogglePlay;
    }

    public void Clear()
    {
        _content?.QueueFree();
        _content = null;
        _player = null;
        _animations.Clear();
        _animations.Visible = _play.Visible = false;
        _note.Text = "";
    }

    public void Show(ModelPreview preview)
    {
        Clear();
        _note.Text = preview.Note ?? "";

        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromBuffer(preview.Glb, "", state);
        if (error != Error.Ok || document.GenerateScene(state) is not Node3D scene)
        {
            _note.Text = $"Godot couldn't load the glTF ({error}).";
            return;
        }
        N64Material.Apply(preview.Glb, state, scene);
        _content = scene;
        _viewport.AddChild(scene);
        _camera.MakeCurrent();

        // The scene's own lights replace the headlight; unlit materials ignore both.
        _headlight.Visible = FindAll<Light3D>(scene).Count == 0;
        Frame(scene);

        _player = FindAll<AnimationPlayer>(scene).FirstOrDefault();
        if (_player is not null)
        {
            var names = _player.GetAnimationList().Where(n => n != "RESET").Order(StringComparer.Ordinal).ToList();
            foreach (var name in names)
            {
                _player.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
                _animations.AddItem(name);
            }
            if (names.Count > 0)
            {
                var start = preview.Animation is { } a && names.IndexOf(a) is >= 0 and var i ? i : 0;
                _animations.Select(start);
                _animations.Visible = _play.Visible = true;
                PlaySelected();
            }
        }
    }

    /// <summary>Plays the selected animation from its first tick, or pauses at it for a screenshot.</summary>
    public void PlaySelected()
    {
        if (_player is null || _animations.Selected < 0)
            return;
        _player.Play(_animations.GetItemText(_animations.Selected));
        _play.Text = "Pause";
    }

    private void TogglePlay()
    {
        if (_player is null)
            return;
        if (_player.IsPlaying())
        {
            _player.Pause();
            _play.Text = "Play";
        }
        else
        {
            _player.Play();
            _play.Text = "Pause";
        }
    }

    /// <summary>Points the camera at the meshes' bounds, from far enough to see them whole.</summary>
    private void Frame(Node3D scene)
    {
        Aabb? bounds = null;
        foreach (var mesh in FindAll<MeshInstance3D>(scene))
        {
            if (mesh.Mesh is null)
                continue;
            var box = mesh.GlobalTransform * mesh.GetAabb();
            bounds = bounds is { } b ? b.Merge(box) : box;
        }
        var aabb = bounds ?? new Aabb(-Vector3.One, 2 * Vector3.One);
        _target = aabb.GetCenter();
        var radius = Mathf.Max(aabb.Size.Length() / 2, 0.01f);
        _distance = radius / Mathf.Sin(Mathf.DegToRad(_camera.Fov / 2)) * 1.1f;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        var offset = new Vector3(0, 0, _distance).Rotated(Vector3.Right, _pitch).Rotated(Vector3.Up, _yaw);
        _camera.Position = _target + offset;
        _camera.Near = _distance / 1000;
        _camera.Far = _distance * 100;
        _camera.LookAt(_target, Vector3.Up);
    }

    private void OnViewInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Left) != 0:
                _yaw -= m.Relative.X * 0.01f;
                _pitch = Mathf.Clamp(_pitch - m.Relative.Y * 0.01f, -1.55f, 1.55f);
                UpdateCamera();
                break;
            case InputEventMouseMotion m when (m.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0:
                var basis = _camera.GlobalTransform.Basis;
                _target += (-basis.X * m.Relative.X + basis.Y * m.Relative.Y) * _distance * 0.0015f;
                UpdateCamera();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _distance *= 0.9f;
                UpdateCamera();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _distance /= 0.9f;
                UpdateCamera();
                break;
        }
    }

    private static List<T> FindAll<T>(Node root) where T : Node
    {
        var found = new List<T>();
        var stack = new Stack<Node>([root]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is T t)
                found.Add(t);
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
        return found;
    }
}
