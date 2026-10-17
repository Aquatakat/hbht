using Aidyn.Core.Sky;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws what the original draws behind the scenery each frame: the sky's gradient
/// (<see cref="SkyBackdrop"/>), then the stars, sun and moon (<see cref="SkyObjects"/>), then the
/// clouds (<see cref="Clouds"/>). Where the terrain's sky isn't drawn, it's black.
/// </summary>
/// <remarks>
/// It's a canvas layer below the 3D scene, which the environment shows as the background, so the
/// scenery covers it and the far plane cuts the scenery off against it, as on the N64. The
/// original's 2D drawing works on a 320 × 240 screen, which this scales to the window's height.
/// Godot's 2D blends as the N64 does, in on-screen values.
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
    private readonly Random _twinkle = new();

    public SkyBackdropView(SkyView sky)
    {
        _sky = sky;
        Name = "Backdrop";
        _gradient = ImageTexture.CreateFromImage(_image);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>A canvas layer holding a backdrop, for <paramref name="sky"/>.</summary>
    public static CanvasLayer Create(SkyView sky)
    {
        var layer = new CanvasLayer { Name = "Sky", Layer = Layer };
        layer.AddChild(new SkyBackdropView(sky));
        // The scene is drawn in on-screen values (OnScreenOutput), and so must the backdrop be.
        layer.AddChild(OnScreenOutput.BackgroundEncoder());
        return layer;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var state = _sky.State;
        if (state.Current is null || !state.Drawn || GetViewport().GetCamera3D() is not { } camera)
        {
            DrawRect(GetRect(), Colors.Black);
            return;
        }
        // Where the scenery is drawn from this frame: between ticks, Godot draws the camera
        // interpolated, and the sky has to match it or it runs up to a tick ahead as the camera turns.
        var look = (-camera.GetGlobalTransformInterpolated().Basis.Z).Normalized().ToNumerics();
        var colors = _sky.Colors;
        if (_sky.Flash)
        {
            // RenderSky's lightning: the whole screen white in place of the gradient, with the sky's objects over it.
            DrawRect(GetRect(), Colors.White);
        }
        else
        {
            SkyBackdrop.Draw(state.Current, state.Next, state.Change, _sky.Clock.TimeOfDay, look, colors.Fog, _lines,
                _sky.FogTime, _sky.LensFlareValue);
            for (var y = 0; y < _lines.Length; y++)
            {
                var c = _lines[y];
                _image.SetPixel(0, y, new Color(c.X, c.Y, c.Z));
            }
            _gradient.Update(_image);
            DrawTextureRect(_gradient, GetRect(), false);
        }

        _sprites.Clear();
        _sky.Objects?.Draw(_sprites, _sky.Clock.Time, look, colors.Sun, _sky.ImageSize, _twinkle, _sky.FogTime);
        _sky.Clouds?.Draw(_sprites, look, colors.Cloud, _sky.ImageSize, _sky.FogTime);
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
