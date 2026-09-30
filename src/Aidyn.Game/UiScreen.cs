using Godot;

namespace Aidyn.Game;

/// <summary>How <see cref="UiScreen"/> scales the original's 320 × 240 screen up to the window.</summary>
public enum UiFilter
{
    /// <summary>Sharp pixels.</summary>
    Pixels,

    /// <summary>
    /// As the console's video output shows it: each line's 320 pixels spread over 640, with the
    /// pixels between them the average of their neighbours, then scaled up sharp. The emulator's
    /// captures of the original show exactly this.
    /// </summary>
    VideoOut,

    /// <summary>Bilinear in both directions.</summary>
    Smooth,
}

/// <summary>
/// A low-resolution screen that UI is drawn on and then scaled up to fill this control: the
/// original's 320 × 240, or taller for smaller UI, and as wide as the control's shape. Views that
/// draw UI go in <see cref="Canvas"/>, in its pixels, and lay themselves out by its size.
/// </summary>
/// <remarks>
/// The original draws its UI images pixel for pixel: they're texture rectangles with bilinear
/// filtering on, which at 1:1 samples each texel exactly, and only a scaled image, such as the
/// conversation box's gradient, is filtered. So the canvas filters linearly, as the console does,
/// and the screen is drawn at its own size and scaled up afterwards by <see cref="Filter"/>.
/// <see cref="Lines"/> sets its height: 240 is the original's, and 480 draws everything half the
/// size, with room for twice as much. It's never narrower than 4:3; a narrower control shows it
/// letterboxed.
/// </remarks>
public partial class UiScreen : Control
{
    /// <summary>The original's screen.</summary>
    public const int Width = 320, Height = 240;

    // The canvas is premultiplied: alpha blending into a transparent target leaves colour times alpha.
    private const string ShaderCode = """
        shader_type canvas_item;
        render_mode blend_premul_alpha;

        uniform int mode = 1;

        vec4 texel(sampler2D screen, ivec2 at, ivec2 size) {
            return texelFetch(screen, clamp(at, ivec2(0), size - 1), 0);
        }

        void fragment() {
            ivec2 size = textureSize(TEXTURE, 0);
            vec2 at = UV * vec2(size);
            int y = int(at.y);
            if (mode == 0) {
                COLOR = texel(TEXTURE, ivec2(int(at.x), y), size);
            } else if (mode == 1) {
                int column = int(at.x * 2.0);
                int x = column / 2;
                vec4 left = texel(TEXTURE, ivec2(x, y), size);
                COLOR = column % 2 == 0 ? left : (left + texel(TEXTURE, ivec2(x + 1, y), size)) * 0.5;
            } else {
                COLOR = texture(TEXTURE, UV);
            }
        }
        """;

    private static readonly Shader Shader = new() { Code = ShaderCode };

    private readonly SubViewport _viewport = new()
    {
        Size = new Vector2I(Width, Height),
        TransparentBg = true,
        Disable3D = true,
        CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear,
        RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
    };

    private readonly ShaderMaterial _material = new() { Shader = Shader };
    private readonly ColorRect _backdrop = new() { Name = "Backdrop", Size = new Vector2(Width, Height), Visible = false, MouseFilter = MouseFilterEnum.Ignore };
    private UiFilter _filter;
    private int _lines = Height;

    public UiScreen()
    {
        Name = "UI screen";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        Material = _material;
        Canvas = new Control { Name = "Canvas", Size = new Vector2(Width, Height), MouseFilter = MouseFilterEnum.Ignore };
        Canvas.AddChild(_backdrop);
        _viewport.AddChild(Canvas);
        AddChild(_viewport);
        Filter = UiFilter.VideoOut;
    }

    /// <summary>What views draw in, in the screen's pixels.</summary>
    public Control Canvas { get; }

    /// <summary>How many pixels tall the screen is.</summary>
    public int Lines
    {
        get => _lines;
        set
        {
            _lines = Math.Max(value, 1);
            Fit();
        }
    }

    /// <summary>The screen's size in its pixels.</summary>
    public Vector2I CanvasSize => _viewport.Size;

    public UiFilter Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            _material.SetShaderParameter("mode", (int)value);
        }
    }

    /// <summary>What's drawn behind the UI on the screen, if anything: the editor shows it over a plain colour.</summary>
    public Color Backdrop
    {
        get => _backdrop.Color;
        set
        {
            _backdrop.Color = value;
            _backdrop.Visible = value.A > 0;
        }
    }

    /// <summary>Where the screen is drawn: as large as fits, centred.</summary>
    public Rect2 ScreenRect
    {
        get
        {
            var canvas = (Vector2)CanvasSize;
            var scale = Math.Min(Size.X / canvas.X, Size.Y / canvas.Y);
            var size = canvas * scale;
            return new Rect2((Size - size) / 2, size);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            Fit();
    }

    /// <summary>Sizes the screen to <see cref="Lines"/> and this control's shape.</summary>
    private void Fit()
    {
        var minimum = (int)Math.Round(_lines * 4 / 3.0);
        var width = Size.Y > 0 ? Math.Max(minimum, (int)Math.Round(_lines * Size.X / Size.Y)) : minimum;
        var size = new Vector2I(width, _lines);
        if (_viewport.Size != size)
        {
            _viewport.Size = size;
            Canvas.Size = size;
            _backdrop.Size = size;
        }
        QueueRedraw();
    }

    public override void _Draw() => DrawTextureRect(_viewport.GetTexture(), ScreenRect, false);
}
