using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Views;

/// <summary>UI images from the plugins, as textures, loaded once each.</summary>
public sealed class UiImages(PluginSet plugins)
{
    // The pieces of the original's feathered popups (WidgetChoiceDia::SetImages), as the machine code
    // loads them: the band, a 1 × 1 white texel of alpha 136, then the gradient above, below, left and
    // right, and the corners from the top left clockwise. The decomp names three of the gradients wrongly.
    private static readonly RecordId Band = RecordId.Hex(RecordTypes.Image, 0x01C4);
    private static readonly RecordId[] Edges = [.. new[] { 0x01C3, 0x01C2, 0x01C0, 0x01C1 }.Select(i => RecordId.Hex(RecordTypes.Image, i))];
    private static readonly RecordId[] Corners = [.. new[] { 0x01BC, 0x01BD, 0x01BE, 0x01BF }.Select(i => RecordId.Hex(RecordTypes.Image, i))];

    private readonly Dictionary<RecordId, Texture2D?> _textures = [];

    /// <summary>A UI image, or null if no plugin has it.</summary>
    public Texture2D? Get(RecordId image)
    {
        if (_textures.TryGetValue(image, out var texture))
            return texture;
        if (plugins.FindAsset(image, "png") is { } png)
            texture = Png.Texture(png);
        else
        {
            GD.PushWarning($"No asset for {image}.");
        }
        return _textures[image] = texture;
    }

    private readonly Dictionary<RecordId, Texture2D?> _plain = [];

    /// <summary>
    /// An image with what's painted into it covered by plain parchment from elsewhere in it (<see cref="PlainImage"/>),
    /// made the first time it's asked for, or null if no plugin has the image.
    /// </summary>
    public Texture2D? Plain(RecordId image, IReadOnlyList<PlainImage.Patch> patches)
    {
        if (_plain.TryGetValue(image, out var plain))
            return plain;
        if (Get(image)?.GetImage() is { } data)
        {
            data.Convert(Image.Format.Rgba8);
            var (width, height) = (data.GetWidth(), data.GetHeight());
            var bytes = data.GetData();
            PlainImage.Apply(bytes, width, height, patches);
            plain = ImageTexture.CreateFromImage(Image.CreateFromData(width, height, false, Image.Format.Rgba8, bytes));
        }
        return _plain[image] = plain;
    }

    private readonly Dictionary<RecordId, Texture2D?> _feathered = [];

    /// <summary>
    /// An image faded out at its edges with round corners, by the feathered boxes' own ramp (<see cref="EdgeFeather"/>),
    /// made the first time it's asked for, or null if no plugin has the image.
    /// </summary>
    public Texture2D? Feathered(RecordId image, int left, int top, int right, int bottom)
    {
        if (_feathered.TryGetValue(image, out var feathered))
            return feathered;
        if (Get(image)?.GetImage() is { } data)
        {
            data.Convert(Image.Format.Rgba8);
            var (width, height) = (data.GetWidth(), data.GetHeight());
            var bytes = data.GetData();
            EdgeFeather.Apply(bytes, width, height, left, top, right, bottom, EdgeRamp());
            feathered = ImageTexture.CreateFromImage(Image.CreateFromData(width, height, false, Image.Format.Rgba8, bytes));
        }
        return _feathered[image] = feathered;
    }

    /// <summary>
    /// The feathered boxes' ramp from the edge in, a value a pixel from 0 to 1: the left gradient's top row, over the
    /// band's alpha, 136, which its last pixels reach.
    /// </summary>
    private float[] EdgeRamp()
    {
        if (Get(Edges[2])?.GetImage() is not { } data)
            return [1];
        data.Convert(Image.Format.Rgba8);
        var full = data.GetPixel(data.GetWidth() - 1, 0).A;
        return [.. Enumerable.Range(0, data.GetWidth()).Select(x => Math.Min(1, data.GetPixel(x, 0).A / full))];
    }

    /// <summary>
    /// A band feathered by each piece at its own size, as a message's box is (<c>IntroMenu_ShadowBG</c>):
    /// the gradients' thickness is their images' height above and below and width beside.
    /// </summary>
    public FeatheredPanel Around(ScreenRect band)
    {
        int Height(RecordId image) => Get(image) is { } t ? t.GetHeight() : 0;
        int Width(RecordId image) => Get(image) is { } t ? t.GetWidth() : 0;
        return new FeatheredPanel(band, Height(Edges[0]), Height(Edges[1]), Width(Edges[2]), Width(Edges[3]));
    }

    /// <summary>Draws a feathered panel in black, as the original's popups are drawn, each piece scaled to fit.</summary>
    public void DrawPanel(CanvasItem canvas, FeatheredPanel panel)
    {
        var b = panel.Band;
        var (left, top, right, bottom) = ((float)b.Left, (float)b.Top, (float)b.Right, (float)b.Bottom);
        Piece(Band, new Rect2(left, top, right - left, bottom - top));
        Piece(Edges[0], new Rect2(left, top - panel.Top, right - left, panel.Top));
        Piece(Edges[1], new Rect2(left, bottom, right - left, panel.Bottom));
        Piece(Edges[2], new Rect2(left - panel.Left, top, panel.Left, bottom - top));
        Piece(Edges[3], new Rect2(right, top, panel.Right, bottom - top));
        Piece(Corners[0], new Rect2(left - panel.Left, top - panel.Top, panel.Left, panel.Top));
        Piece(Corners[1], new Rect2(right, top - panel.Top, panel.Right, panel.Top));
        Piece(Corners[2], new Rect2(right, bottom, panel.Right, panel.Bottom));
        Piece(Corners[3], new Rect2(left - panel.Left, bottom, panel.Left, panel.Bottom));

        void Piece(RecordId image, Rect2 rect)
        {
            if (rect.Size.X > 0 && rect.Size.Y > 0 && Get(image) is { } texture)
                canvas.DrawTextureRect(texture, rect, false, Colors.Black);
        }
    }
}
