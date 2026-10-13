using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The pause menu's 3D scroll, actor <c>0x0E6B</c>: its two rollers and the paper's shadow, seen from straight above
/// by a camera of its own, drawn over the tabs on the menu's UI screen (<c>PauseMenu.Draw</c>). The menu steps it
/// once a frame and moves it with the tabs.
/// </summary>
/// <remarks>
/// It's drawn at the UI screen's own pixels, with its 45° of view across the original's 240 lines, so it lines up with
/// the tabs at any of the screen's heights. Its scene's own two lights light it, a dim blue one from the right and a
/// warm one from the left, with no ambient (<see cref="N64Material.Lighting.Own"/>), not the world's: so lit, the
/// System tab's roller matches the user's capture within 4 a channel on average.
/// </remarks>
public partial class PauseScrollView : Control
{
    /// <summary>The scroll's actor.</summary>
    public static readonly Plugins.RecordId Actor = Plugins.RecordId.Parse("aidyn:actor/0x0E6B");

    private const float FieldOfView = 45, Near = 1, Far = 256;

    private readonly SubViewport _viewport = new()
    {
        OwnWorld3D = true,
        TransparentBg = true,
        RenderTargetUpdateMode = SubViewport.UpdateMode.WhenParentVisible,
    };

    private readonly Camera3D _camera = new() { Near = Near, Far = Far };
    private readonly Node3D _scroll;
    private readonly ActorAnimator _animator;
    private readonly int _initialState;

    private PauseScrollView(Node3D scroll, ActorAnimator animator, int initialState)
    {
        Name = "Pause scroll";
        MouseFilter = MouseFilterEnum.Ignore;
        // The viewport's colours are what the material wrote over transparent black: already times their alpha.
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha };
        _scroll = scroll;
        _animator = animator;
        _initialState = initialState;
        animator.Driven = true;
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor, BackgroundColor = Colors.Transparent };
        OnScreenOutput.Apply(environment);
        _camera.Environment = environment;
        // The scene's own camera is the original's export of it; the menu aims one of its own.
        foreach (var camera in Nodes.FindAll<Camera3D>(_scroll))
            camera.Free();
        _viewport.AddChild(_scroll);
        _viewport.AddChild(_camera);
        _camera.Current = true;
        AddChild(_viewport);
        // Looking straight down, with −Z up the screen and +X to the right.
        _camera.Transform = new Transform3D(new Basis(Vector3.Right, Vector3.Forward, Vector3.Up), new Vector3(0, Core.Ui.PauseScroll.CameraHeight, 0));
    }

    /// <summary>The scroll, or null if no plugin has its actor or scene.</summary>
    public static PauseScrollView? Create(Plugins.PluginSet plugins, Models models)
    {
        if (plugins.TryGet<ActorRecord>(Actor) is not { } actor ||
            models.InstantiateActor(actor, N64Material.Lighting.Own) is not ({ } node, { } animator))
        {
            GD.PushWarning($"No {Actor} for the pause menu's scroll.");
            return null;
        }
        Light(node, models.Glb(actor.Scene)!);
        return new PauseScrollView(node, animator, actor.InitialState);
    }

    /// <summary>The scroll's clock in the state it's in, from 1.</summary>
    public int Frame => _animator.Frame;

    /// <summary>The length of the state it's in, in frames.</summary>
    public int Length => _animator.StateLength;

    /// <summary>Where the scroll is across its scene, which the tabs' slide moves.</summary>
    public float X
    {
        get => _scroll.Position.X;
        set => _scroll.Position = new Vector3(value, 0, 0);
    }

    /// <summary>Back to its first state's first frame, as the original builds it afresh for each opening.</summary>
    public void Restart()
    {
        _animator.Request = 0;
        _animator.Enter(_initialState);
    }

    /// <summary>One frame toward <paramref name="request"/>.</summary>
    public void Step(int request) => _animator.TickMotion(request);

    public override void _Process(double delta)
    {
        var size = new Vector2I(Math.Max((int)Size.X, 1), Math.Max((int)Size.Y, 1));
        if (_viewport.Size != size)
            _viewport.Size = size;
        // 45° across the frame's 240 lines, wherever they are on a taller screen.
        _camera.Fov = Mathf.RadToDeg(2 * MathF.Atan(MathF.Tan(Mathf.DegToRad(FieldOfView / 2)) * size.Y / UiScreen.Height));
        QueueRedraw();
    }

    public override void _Draw() => DrawTextureRect(_viewport.GetTexture(), new Rect2(Vector2.Zero, Size), false);

    /// <summary>
    /// Lights the scroll by its scene's two lights, as the glTF gives them: directional, each shining along its node's
    /// −Z. Godot's own lights are dropped, as the material lights it.
    /// </summary>
    private static void Light(Node3D scroll, byte[] glb)
    {
        var colors = new List<Color>();
        using (var json = GlbFile.Json(glb))
        {
            if (json.RootElement.TryGetProperty("extensions", out var extensions) &&
                extensions.TryGetProperty("KHR_lights_punctual", out var punctual))
            {
                foreach (var light in punctual.GetProperty("lights").EnumerateArray())
                {
                    var c = light.GetProperty("color");
                    colors.Add(new Color(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle()));
                }
            }
        }
        var lights = Nodes.FindAll<Light3D>(scroll);
        Vector3 Toward(string name) => lights.Where(l => l.Name == name).Select(l => l.Transform.Basis.Z).FirstOrDefault();
        var (first, second) = (Toward("light 0"), Toward("light 1"));
        foreach (var light in lights)
            light.Free();
        if (colors.Count < 2 || first == Vector3.Zero || second == Vector3.Zero)
            return;
        N64Material.SetOwnLights(scroll, Colors.Black, (colors[0], first), (colors[1], second));
    }
}
