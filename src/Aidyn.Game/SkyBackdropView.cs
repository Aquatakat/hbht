using Aidyn.Core.Sky;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws what the original draws behind the scenery each frame: the sky's gradient
/// (<see cref="SkyBackdrop"/>), then the stars, sun and moon (<see cref="SkyObjects"/>), then the
/// clouds (<see cref="Clouds"/>), as the core's sky has them (<see cref="SkyDirector"/>). Where the terrain's sky isn't drawn,
/// it's black.
/// </summary>
/// <remarks>
/// It's a canvas layer below the 3D scene, which the environment shows as the background, so the
/// scenery covers it and the far plane cuts the scenery off against it, as on the N64. The
/// original's 2D drawing works on a 320 × 240 screen, which this scales to the window's height.
/// Godot's 2D blends as the N64 does, in on-screen values. The sky is drawn from the camera's interpolated transform each
/// frame, and drawing the sun leaves where its middle landed in the core for the lens flare's next tick
/// (<see cref="SkyObjects.SunMiddle"/>); the stars take the greys the core's last tick drew.
/// </remarks>
public partial class SkyBackdropView : Control
{
    /// <summary>The canvas layer the environment shows behind the scene.</summary>
    public const int Layer = -1;

    private readonly SkyView _sky;
    private readonly System.Numerics.Vector3[] _lines = new System.Numerics.Vector3[SkyBackdrop.ScreenLines];
    private readonly Image _image = Image.CreateEmpty(1, SkyBackdrop.ScreenLines, false, Image.Format.Rgb8);
    private readonly ImageTexture _gradient;
    private readonly List<SkySprite> _sprites = [];

    public SkyBackdropView(SkyView sky)
    {
        _sky = sky;
        Name = "Backdrop";
        _gradient = ImageTexture.CreateFromImage(_image);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _domeTexture = ImageTexture.CreateFromImage(_domeImage);
        _domeMaterial.SetShaderParameter("lines", _domeTexture);
        _domeMaterial.SetShaderParameter("dome_pitch", SkyBackdrop.DomePitch);
        _domeMaterial.SetShaderParameter("dome_down", (float)SkyBackdrop.DomeDown);
        _domeMaterial.SetShaderParameter("focal", SkyScreen.Focal);
        _dome = new ColorRect { Name = "Dome", Material = _domeMaterial, MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true, Visible = false };
        _dome.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_dome);
    }

    /// <summary>
    /// The projected gradient (<see cref="SkyBackdrop.DomeFraction"/>): each pixel takes the gradient's line for the
    /// elevation of the way through it, so the rows stay fixed against the scenery. It's drawn behind the sky's
    /// objects, which its parent draws.
    /// </summary>
    private const string DomeShader = """
        shader_type canvas_item;
        uniform sampler2D lines : filter_linear, repeat_disable;
        uniform vec3 right;
        uniform vec3 up;
        uniform vec3 back;
        uniform float tan_half;
        uniform float aspect;
        uniform float dome_pitch;
        uniform float dome_down;
        uniform float focal;
        void fragment() {
            vec2 ndc = vec2(UV.x * 2.0 - 1.0, 1.0 - UV.y * 2.0) * tan_half;
            vec3 way = normalize(right * ndc.x * aspect + up * ndc.y - back);
            float angle = asin(clamp(way.y, -1.0, 1.0)) + dome_pitch;
            float line = angle >= 1.5698 ? -1.0e9 : 120.0 - focal * tan(angle) + dome_down;
            float f = clamp(line, 1.0, 199.0);
            COLOR = vec4(texture(lines, vec2(0.5, (f - 0.5) / 199.0)).rgb, 1.0);
        }
        """;

    private static readonly Shader Dome = new() { Code = DomeShader };
    private readonly ShaderMaterial _domeMaterial = new() { Shader = Dome };
    private readonly System.Numerics.Vector3[] _domeLines = new System.Numerics.Vector3[SkyBackdrop.DomeLines];
    private readonly Image _domeImage = Image.CreateEmpty(1, SkyBackdrop.DomeLines, false, Image.Format.Rgb8);
    private readonly ImageTexture _domeTexture;
    private readonly ColorRect _dome;

    /// <summary>A canvas layer holding a backdrop, for <paramref name="sky"/>.</summary>
    public static CanvasLayer Create(SkyView sky)
    {
        var layer = new CanvasLayer { Name = "Sky", Layer = Layer };
        layer.AddChild(new SkyBackdropView(sky));
        // The scene is drawn in on-screen values (OnScreenOutput), and so must the backdrop be.
        layer.AddChild(OnScreenOutput.BackgroundEncoder());
        return layer;
    }

    public override void _Process(double delta)
    {
        var state = _sky.Sky.State;
        var camera = GetViewport().GetCamera3D();
        _dome.Visible = state is { Current: not null, Drawn: true } && camera is not null && !_sky.Sky.Flash &&
            _sky.Sky.Placement == SkyPlacement.Projected;
        if (_dome.Visible)
        {
            SkyBackdrop.Dome(state.Current!, state.Next, state.Change, _sky.Sky.TimeOfDay, _sky.Sky.Colors.Fog, _domeLines,
                _sky.Sky.FogTime, _sky.Sky.LensFlareValue);
            for (var y = 0; y < _domeLines.Length; y++)
            {
                var c = _domeLines[y] * _sky.Sky.Dim;
                _domeImage.SetPixel(0, y, new Color(c.X, c.Y, c.Z));
            }
            _domeTexture.Update(_domeImage);
            var basis = camera!.GetGlobalTransformInterpolated().Basis;
            _domeMaterial.SetShaderParameter("right", basis.X.Normalized());
            _domeMaterial.SetShaderParameter("up", basis.Y.Normalized());
            _domeMaterial.SetShaderParameter("back", basis.Z.Normalized());
            _domeMaterial.SetShaderParameter("tan_half", MathF.Tan(Mathf.DegToRad(camera.Fov) / 2));
            _domeMaterial.SetShaderParameter("aspect", Size.X / MathF.Max(Size.Y, 1));
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var state = _sky.Sky.State;
        if (state.Current is null || !state.Drawn || GetViewport().GetCamera3D() is not { } camera)
        {
            DrawRect(GetRect(), Colors.Black);
            return;
        }
        // Where the scenery is drawn from this frame: between ticks, Godot draws the camera
        // interpolated, and the sky has to match it or it runs up to a tick ahead as the camera turns.
        var look = (-camera.GetGlobalTransformInterpolated().Basis.Z).Normalized().ToNumerics();
        var colors = _sky.Sky.Colors;
        if (_sky.Sky.Flash)
        {
            // RenderSky's lightning: the whole screen white in place of the gradient, with the sky's objects over it.
            DrawRect(GetRect(), Colors.White);
        }
        // Projected, the dome, a child drawn behind this, is the gradient.
        else if (!_dome.Visible)
        {
            SkyBackdrop.Draw(state.Current, state.Next, state.Change, _sky.Sky.TimeOfDay, look, colors.Fog, _lines,
                _sky.Sky.FogTime, _sky.Sky.LensFlareValue);
            for (var y = 0; y < _lines.Length; y++)
            {
                var c = _lines[y] * _sky.Sky.Dim;
                _image.SetPixel(0, y, new Color(c.X, c.Y, c.Z));
            }
            _gradient.Update(_image);
            DrawTextureRect(_gradient, GetRect(), false);
        }

        _sprites.Clear();
        _sky.Sky.Objects?.Draw(_sprites, _sky.Sky.Clock.Time, look, colors.Sun, _sky.ImageSize, _sky.Sky.FogTime, shown: _sky.Sky.TimeOfDay);
        _sky.Sky.Clouds?.Draw(_sprites, look, colors.Cloud, _sky.ImageSize, _sky.Sky.FogTime);
        DrawSprites(this, _sky, _sprites);
    }

    /// <summary>
    /// Draws sprites placed on the original's 320 × 240 screen, scaled to <paramref name="canvas"/>'s
    /// height and centred across it.
    /// </summary>
    public static void DrawSprites(Control canvas, SkyView sky, List<SkySprite> sprites)
    {
        var size = canvas.Size;
        var scale = size.Y / SkyScreen.Height;
        var origin = new Vector2((size.X - SkyScreen.Width * scale) / 2, 0);
        foreach (var sprite in sprites)
        {
            if (sky.Image(sprite.Image) is not { } texture)
                continue;
            var color = new Color(sprite.Color.X, sprite.Color.Y, sprite.Color.Z, sprite.Alpha);
            if (sprite.Patch is { } patch)
            {
                DrawPatch(canvas, texture, patch, color, origin, scale);
                continue;
            }
            var rect = new Rect2(origin + sprite.At.ToGodot() * scale, texture.GetSize() * sprite.Scale * scale);
            canvas.DrawTextureRect(texture, rect, false, color);
        }
    }

    /// <summary>How many cells a side a sky patch is drawn in, so its texture follows the perspective.</summary>
    private const int PatchCells = 4;

    private static readonly int[] PatchIndices = [.. Enumerable.Range(0, PatchCells * PatchCells).SelectMany(cell =>
    {
        var (row, column) = Math.DivRem(cell, PatchCells);
        var corner = row * (PatchCells + 1) + column;
        return new[] { corner, corner + 1, corner + PatchCells + 2, corner, corner + PatchCells + 2, corner + PatchCells + 1 };
    })];

    private static readonly Vector2[] PatchPoints = new Vector2[(PatchCells + 1) * (PatchCells + 1)];
    private static readonly Vector2[] PatchUvs = [.. Enumerable.Range(0, PatchPoints.Length)
        .Select(i => new Vector2(i % (PatchCells + 1), i / (PatchCells + 1)) / PatchCells)];
    private static readonly Color[] PatchColors = new Color[PatchPoints.Length];

    /// <summary>
    /// Draws a sprite over its patch's projection, as a grid of triangles: projection doesn't keep
    /// a patch's texture even across it, which two triangles would kink along their diagonal.
    /// </summary>
    private static void DrawPatch(Control canvas, Texture2D texture, SkyPatch patch, Color color, Vector2 origin, float scale)
    {
        for (var i = 0; i < PatchPoints.Length; i++)
        {
            if (patch.At(PatchUvs[i].X, PatchUvs[i].Y) is not { } at)
                return;
            PatchPoints[i] = origin + at.ToGodot() * scale;
            PatchColors[i] = color;
        }
        RenderingServer.CanvasItemAddTriangleArray(canvas.GetCanvasItem(), PatchIndices, PatchPoints, PatchColors, PatchUvs,
            texture: texture.GetRid());
    }
}
