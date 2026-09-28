using Aidyn.Core.Sky;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws what the original draws behind the scenery each frame: the sky's gradient
/// (<see cref="SkyBackdrop"/>), or black where the terrain's sky isn't drawn.
/// </summary>
/// <remarks>
/// It's a canvas layer below the 3D scene, which the environment shows as the background, so the
/// scenery covers it and the far plane cuts the scenery off against it, as on the N64. The
/// original's 2D drawing works on a 320 × 240 screen, which this scales to the window's height.
/// </remarks>
public partial class SkyBackdropView : Control
{
    /// <summary>The canvas layer the environment shows behind the scene.</summary>
    public const int Layer = -1;

    private readonly SkyView _sky;
    private readonly System.Numerics.Vector3[] _lines = new System.Numerics.Vector3[SkyBackdrop.ScreenLines];
    private readonly Image _image = Image.CreateEmpty(1, SkyBackdrop.ScreenLines, false, Image.Format.Rgb8);
    private readonly ImageTexture _gradient;

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
        var look = (-camera.GlobalBasis.Z).Normalized().ToNumerics();
        SkyBackdrop.Draw(state.Current, state.Next, state.Change, _sky.Clock.TimeOfDay, look, _sky.Colors.Fog, _lines, _sky.FogTime);
        for (var y = 0; y < _lines.Length; y++)
        {
            var c = _lines[y];
            _image.SetPixel(0, y, new Color(c.X, c.Y, c.Z));
        }
        _gradient.Update(_image);
        DrawTextureRect(_gradient, GetRect(), false);
    }
}
