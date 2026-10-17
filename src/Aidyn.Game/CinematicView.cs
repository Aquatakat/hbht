using System.Globalization;
using System.Text.Json;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a cinematic's shot: its scene in a world of its own, posed by its animation at the shot's time and seen through
/// the scene's own camera with the shot's planes, lit by the scene's own lights as its animation turns them. Its
/// billboards face the camera, and its scrolling textures move a step each picture, as each draw of a scene moves them.
/// </summary>
public partial class CinematicView : SubViewportContainer
{
    private readonly SubViewport _viewport = new() { OwnWorld3D = true, HandleInputLocally = false };
    private readonly Models _models;
    private Node3D? _scene;
    private Camera3D? _camera;
    private ScrollingScene? _scrolling;
    private MirroredMeshes? _mirroring;
    private List<Node3D> _facings = [];
    private AnimationPlayer? _player;
    private string? _animation;
    private Color _ambient;
    /// <summary>The node whose scale tints the ambient: the transform after the last light's, the first model node.</summary>
    private Node3D? _ambientTint;
    private readonly List<(Light3D Node, Color Color)> _lights = [];

    public CinematicView(Models models)
    {
        _models = models;
        Name = "Cinematic";
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black };
        OnScreenOutput.Apply(environment);
        _viewport.AddChild(new WorldEnvironment { Environment = environment });
        AddChild(_viewport);
    }

    /// <summary>The 3D view, whose antialiasing and resolution the game sets as its own.</summary>
    public SubViewport Viewport => _viewport;

    /// <summary>Shows a shot's scene from its first tick.</summary>
    public void Show(CinematicShot shot)
    {
        Clear();
        if (_models.Instantiate(shot.Scene) is not { } scene)
            return;
        _scene = scene;
        _viewport.AddChild(scene);
        N64Material.SetLighting(scene, N64Material.Lighting.Own);
        ReadLights(scene, _models.Glb(shot.Scene)!);
        _scrolling = _models.Scrolls.Attach(shot.Scene, scene);
        _mirroring = MirroredMeshes.For(scene, _models.Glb(shot.Scene)!);
        _facings = Billboards.Facings(scene);
        _camera = Nodes.FindAll<Camera3D>(scene).FirstOrDefault();
        if (_camera is not null)
        {
            (_camera.Near, _camera.Far) = (shot.Near, shot.Far);
            _camera.MakeCurrent();
        }
        _player = Nodes.FindAll<AnimationPlayer>(scene).FirstOrDefault();
        _animation = $"borg6 {Convert.ToInt32(shot.Animation.Key, 16).ToString(CultureInfo.InvariantCulture)}";
        if (_player is not null && _player.HasAnimation(_animation))
        {
            // Posed here by the shot's time, not Godot's clock.
            _player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
            _player.Play(_animation);
        }
        else
        {
            _player = null;
        }
        Pose(1);
    }

    /// <summary>
    /// Poses the scene at a time of its animation, from 1: the clip is baked from time 1, a sample a tick (see
    /// docs/formats/animation.md), so time <paramref name="time"/> is <c>(time − 1) / 60</c> seconds in.
    /// </summary>
    public void Pose(int time)
    {
        if (_scene is null)
            return;
        if (_player is not null)
        {
            _player.Seek((time - 1) / 60.0, update: true);
            TransformTracks.Apply(_player);
        }
        _mirroring?.Update();
        _scrolling?.Draw();
        if (_camera is not null)
        {
            Billboards.Face(_facings, _camera.GlobalPosition);
            // The scene's eye is its camera's position (Cinematic.StartShot leaves the camera its own).
            N64Material.SetEye(_scene, _camera.GlobalPosition);
        }
        N64Material.SetOwnLights(_scene, Tinted(_ambient, _ambientTint),
            [.. _lights.Select(l => (Tinted(l.Color, l.Node), l.Node.GlobalBasis.Z))]);
    }

    /// <summary>
    /// A light's colour tinted by its transform's scale, each channel clamped to 0–1 and the product to 1, as the
    /// original reads <c>+0x28</c>–<c>+0x30</c> into it (<c>BorgAnimDrawSceneRaw</c>, <c>0x80097d54</c>).
    /// </summary>
    private static Color Tinted(Color color, Node3D? transform)
    {
        if (transform is null)
            return color;
        var scale = transform.Scale;
        return new Color(Math.Min(1, color.R * Math.Clamp(scale.X, 0, 1)), Math.Min(1, color.G * Math.Clamp(scale.Y, 0, 1)),
            Math.Min(1, color.B * Math.Clamp(scale.Z, 0, 1)));
    }

    public void Clear()
    {
        _scene?.QueueFree();
        _scene = null;
        _camera = null;
        _scrolling = null;
        _mirroring = null;
        _facings = [];
        _player = null;
        _lights.Clear();
        _ambientTint = null;
    }

    /// <summary>
    /// The scene's lights, as the importer writes them: each light but the last a directional light node, which its
    /// animation can turn, and the last the ambient, in the scene's extras. Their nodes are kept, unlit, for their way.
    /// </summary>
    private void ReadLights(Node3D scene, byte[] glb)
    {
        using var json = GlbFile.Json(glb);
        var root = json.RootElement;
        var colors = new List<Color>();
        if (root.TryGetProperty("extensions", out var extensions) && extensions.TryGetProperty("KHR_lights_punctual", out var punctual))
            colors.AddRange(punctual.GetProperty("lights").EnumerateArray().Select(l => Rgb(l.GetProperty("color"))));
        JsonElement ambient = default;
        var lit = root.TryGetProperty("scenes", out var scenes) && scenes[0].TryGetProperty("extras", out var extras) &&
            extras.TryGetProperty("ambientLight", out ambient);
        _ambient = lit ? Rgb(ambient) : Colors.White;
        // The ambient is tinted by transform 1 + its index, as each light is, which is the first model node; a scene
        // without lights gets the plain white ambient.
        var first = $"transform {1 + colors.Count}";
        _ambientTint = !lit ? null : Nodes.FindAll<Node3D>(scene).FirstOrDefault(n => n.Name == first || n.Name == first + " billboard");
        foreach (var light in Nodes.FindAll<Light3D>(scene))
        {
            light.Visible = false;
            if (int.TryParse(light.Name.ToString().Replace("light ", "", StringComparison.Ordinal), CultureInfo.InvariantCulture, out var i) && i < colors.Count)
                _lights.Add((light, colors[i]));
        }
        _lights.Sort((a, b) => string.CompareOrdinal(a.Node.Name, b.Node.Name));
        if (_lights.Count > N64Material.MaxLights)
            _lights.RemoveRange(N64Material.MaxLights, _lights.Count - N64Material.MaxLights);
    }

    private static Color Rgb(JsonElement c) => new(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle());
}
