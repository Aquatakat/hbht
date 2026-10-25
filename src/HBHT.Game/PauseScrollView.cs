using HBHT.Core.Ui;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Game;

/// <summary>
/// The pause menu's 3D scroll, actor <c>0x0E6B</c>: its two rollers and the paper's shadow, seen from straight above
/// by a camera of its own, drawn over the tabs on the menu's UI screen (<c>PauseMenu.Draw</c>). The menu steps it
/// once a frame and moves it with the tabs.
/// </summary>
/// <remarks>
/// <para>
/// It's drawn at the UI screen's own pixels, with its 45° of view across the original's 240 lines, so it lines up with
/// the tabs at any of the screen's heights. Its scene's own two lights light it, a dim blue one from the right and a
/// warm one from the left, with no ambient (<see cref="N64Material.Lighting.Own"/>), not the world's: so lit, the
/// System tab's roller matches the user's capture within 4 a channel on average.
/// </para>
/// <para>
/// Each roller is seen from no further off to the side than it is when its own tab is open, which is as far as the
/// console's frame ever shows it. Further out, as a wider screen shows the World tab's roller beside the Party tab, the
/// one camera would show its side, wider and reaching in over the tab, which the user found wrong (Oct 13, 2026). So
/// there each roller has a camera of its own, which follows it out, and its picture moves out with the tabs, 320
/// pixels a tab from where it is open: from its own tab's centre, the scroll is 27 to the Party tab's and 26 on to
/// the World tab's (<see cref="PauseScroll.TabX"/>), and 320 pixels at both. The two cameras see one world, each
/// drawing one roller; while both are near enough, one draws both.
/// </para>
/// </remarks>
public partial class PauseScrollView : Control
{
    /// <summary>The scroll's actor.</summary>
    public static readonly Plugins.RecordId Actor = Plugins.RecordId.Parse("aidyn:actor/0x0E6B");

    private const float FieldOfView = 45, Near = 1, Far = 256;

    /// <summary>The render layers of the System end's roller, with the paper's shadow beside it, and the World end's.</summary>
    private const uint SystemLayer = 1 << 1, WorldLayer = 1 << 2;

    private readonly End _system, _world;
    private readonly Node3D _scroll;
    private readonly ActorAnimator _animator;
    private readonly int _initialState;
    private bool _measured, _apart;

    private PauseScrollView(Node3D scroll, ActorAnimator animator, int initialState)
    {
        Name = "Pause scroll";
        MouseFilter = MouseFilterEnum.Ignore;
        // The viewports' colours are what the material wrote over transparent black: already times their alpha.
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha };
        _scroll = scroll;
        _animator = animator;
        _initialState = initialState;
        animator.Driven = true;
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor, BackgroundColor = Colors.Transparent };
        OnScreenOutput.Apply(environment);
        // The scene's own camera is the original's export of it; the menu aims its own.
        foreach (var camera in Nodes.FindAll<Camera3D>(_scroll))
            camera.Free();
        var world = new World3D();
        _system = new End(world, environment, home: 0);
        _world = new End(world, environment, home: 2);
        _system.View.AddChild(_scroll);
        AddChild(_system.View);
        AddChild(_world.View);
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
        Light(node, models.Glb(actor.Scene)!.Extras);
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

    /// <summary>How opaque it's drawn.</summary>
    public float Alpha { get; set; } = 1;

    /// <summary>Back to its first state's first frame, as the original builds it afresh for each opening.</summary>
    public void Restart()
    {
        _animator.Request = 0;
        _animator.Enter(_initialState);
        if (!_measured)
            Measure();
    }

    /// <summary>One frame: a step toward the cue's request, or into its state partway through.</summary>
    public void Play(ScrollCue cue)
    {
        if (cue.Join > 0)
            _animator.Join(cue.Request, cue.Join);
        else
            _animator.TickMotion(cue.Request);
    }

    /// <summary>
    /// Aims each roller's camera for the scroll as it is now, and returns the columns of this control where the rollers
    /// are drawn, along their middles, which the tabs are cut at.
    /// </summary>
    public (float System, float World) Arrange()
    {
        Fit();
        if (!_measured)
            return (float.NegativeInfinity, float.PositiveInfinity);
        _system.Aim(Axis(_system));
        _world.Aim(Axis(_world));
        // Near enough, both are drawn by one camera, so they hide each other as they should where they meet.
        _apart = _system.Camera.Position.X != 0 || _world.Camera.Position.X != 0;
        _system.Camera.CullMask = _apart ? SystemLayer : SystemLayer | WorldLayer;
        _world.Camera.CullMask = _apart ? WorldLayer : 0;
        QueueRedraw();
        return (_system.Column(Axis(_system)), _world.Column(Axis(_world)));
    }

    public override void _Process(double delta)
    {
        Fit();
        QueueRedraw();
    }

    /// <summary>
    /// Whether the viewports render, every frame: while the menu is open, so that each has the scroll as it is
    /// whenever it's drawn. The World end's camera sees nothing while the System end's draws both rollers.
    /// </summary>
    public bool Rendering
    {
        set
        {
            var mode = value ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
            _system.View.RenderTargetUpdateMode = mode;
            _world.View.RenderTargetUpdateMode = mode;
        }
    }

    public override void _Draw()
    {
        var tint = new Color(Alpha, Alpha, Alpha, Alpha);
        _system.Draw(this, tint);
        if (_apart)
            _world.Draw(this, tint);
    }

    /// <summary>The viewports at this control's size, with 45° across the frame's 240 lines, wherever they are on a taller screen.</summary>
    private void Fit()
    {
        var size = new Vector2I(Math.Max((int)Size.X, 1), Math.Max((int)Size.Y, 1));
        var fov = Mathf.RadToDeg(2 * MathF.Atan(MathF.Tan(Mathf.DegToRad(FieldOfView / 2)) * size.Y / UiScreen.Height));
        foreach (var end in new[] { _system, _world })
        {
            if (end.View.Size != size)
                end.View.Size = size;
            end.Camera.Fov = fov;
        }
    }

    /// <summary>The middle of the end's roller in the scroll's scene, along its axis.</summary>
    private Vector3 Axis(End end) => end.Roller!.GlobalTransform * end.Roller.GetAabb().GetCenter();

    /// <summary>
    /// Finds each end's roller, and where it is in its first state, open: the meshes left of the scroll's middle go
    /// with the System end, the rest with the World end, and each end's outermost is its roller.
    /// </summary>
    private void Measure()
    {
        var x = X;
        X = 0;
        foreach (var mesh in Nodes.FindAll<MeshInstance3D>(_scroll))
        {
            var centre = (mesh.GlobalTransform * mesh.GetAabb().GetCenter()).X;
            var end = centre < 0 ? _system : _world;
            mesh.Layers = end == _system ? SystemLayer : WorldLayer;
            if (end.Roller is null || Math.Abs(centre) > Math.Abs(end.Open))
                (end.Roller, end.Open) = (mesh, centre);
        }
        X = x;
        _measured = _system.Roller is not null && _world.Roller is not null;
    }

    /// <summary>One end of the scroll: its roller, and the viewport and camera that draw it.</summary>
    private sealed class End
    {
        private readonly int _home;
        private float _shift;

        public End(World3D world, Godot.Environment environment, int home)
        {
            _home = home;
            View = new SubViewport { World3D = world, TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
            // The menu poses the scroll a frame at a time, to be drawn as it is. Interpolated, as the project has it, it
            // drew a tick behind, and its first frame shown was where it had been reset to, rolled out.
            View.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
            Camera = new Camera3D { Near = Near, Far = Far, Environment = environment, CullMask = home == 0 ? SystemLayer : WorldLayer };
            View.AddChild(Camera);
            Camera.Current = true;
            // Looking straight down, with −Z up the screen and +X to the right.
            Camera.Transform = new Transform3D(new Basis(Vector3.Right, Vector3.Forward, Vector3.Up), new Vector3(0, PauseScroll.CameraHeight, 0));
        }

        public SubViewport View { get; }
        public Camera3D Camera { get; }
        public MeshInstance3D? Roller { get; set; }

        /// <summary>The roller's x, along its axis, in its first state with the scroll at 0: open.</summary>
        public float Open { get; set; }

        /// <summary>
        /// Aims the camera at a roller whose middle is at <paramref name="axis"/>: over the scene's middle while the
        /// roller is no further out than when its tab is open, and else following it so that it's seen as it is then,
        /// its picture moved out a tab's 320 pixels for each tab it has gone.
        /// </summary>
        public void Aim(Vector3 axis)
        {
            // Where the scroll would be for the roller, open, to be here, and so which tab it's at.
            var at = axis.X - Open;
            var tab = at >= 0 ? (PauseScroll.TabX[0] - at) / PauseScroll.TabX[0] : 1 + at / PauseScroll.TabX[2];
            var beyond = _home == 0 ? tab > 0 : tab < 2;
            var camera = Camera.Position;
            Camera.Position = camera with { X = beyond ? at - PauseScroll.TabX[_home] : 0 };
            _shift = beyond ? (_home - tab) * UiScreen.Width : 0;
        }

        /// <summary>Where a point of the scene is drawn across the control.</summary>
        public float Column(Vector3 point) => Camera.UnprojectPosition(point).X + _shift;

        public void Draw(Control control, Color tint) =>
            control.DrawTextureRect(View.GetTexture(), new Rect2(new Vector2(_shift, 0), control.Size), false, tint);
    }

    /// <summary>
    /// Lights the scroll by its scene's two lights, as the glTF gives them: directional, each shining along its node's
    /// −Z. Godot's own lights are dropped, as the material lights it.
    /// </summary>
    private static void Light(Node3D scroll, SceneExtras extras)
    {
        var colors = extras.LightColors.Select(c => new Color(c.X, c.Y, c.Z)).ToList();
        var lights = Nodes.FindAll<Light3D>(scroll);
        Vector3 Toward(string name) => lights.Where(l => l.Name == name).Select(l => l.Transform.Basis.Z).FirstOrDefault();
        var (first, second) = (Toward(SceneNames.Light(0)), Toward(SceneNames.Light(1)));
        foreach (var light in lights)
            light.Free();
        if (colors.Count < 2 || first == Vector3.Zero || second == Vector3.Zero)
            return;
        N64Material.SetOwnLights(scroll, Colors.Black, (colors[0], first), (colors[1], second));
    }
}
