using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>A font's layout and its sheet, to print with.</summary>
public sealed record UiFont(TextLayout Layout, Texture2D? Sheet)
{
    public int LineHeight => Layout.LineHeight();

    public int Width(string text) => Layout.Width(text);
}

/// <summary>
/// The menus' widget kit, drawn immediately on a <see cref="UiScreen"/>'s canvas in its pixels: text
/// in either font, UI images whole, scaled or cut out, the original's parchment popup, and scrolling
/// lists with a cursor and arrows. Views call it from <c>_Draw</c>.
/// </summary>
public sealed class UiPainter
{
    /// <summary>The colour of the arrows beside a list's title (<c>COLOR_OFFWHITE</c>).</summary>
    public static readonly Rgba ArrowColor = new(225, 225, 225, 255);

    public UiPainter(PluginSet plugins)
    {
        Images = new UiImages(plugins);
        Main = Font(plugins, 0);
        Slim = Font(plugins, 1);
    }

    public UiImages Images { get; }

    /// <summary>The main font, and the slim one the lists and details use (<c>SlimFont</c>).</summary>
    public UiFont Main { get; }

    public UiFont Slim { get; }

    private UiFont Font(PluginSet plugins, int n)
    {
        var record = plugins.Get<FontRecord>(RecordId.Decimal(RecordTypes.Font, n));
        return new UiFont(new TextLayout(record), Images.Get(record.Image));
    }

    /// <summary>
    /// Prints text from (x, y), wrapping at <paramref name="bounds"/>' right edge and drawing only
    /// what's inside them, and returns how tall it is (<c>WidgetClipText</c>).
    /// </summary>
    public int Text(CanvasItem canvas, UiFont font, string text, int x, int y, ScreenRect bounds, Rgba color)
    {
        var run = font.Layout.Print(text, x, y, bounds, color);
        if (font.Sheet is { } sheet)
        {
            foreach (var glyph in run.Glyphs)
            {
                canvas.DrawTextureRectRegion(sheet, new Rect2(glyph.X, glyph.Y, glyph.Width, glyph.Height),
                    new Rect2(glyph.SourceX, glyph.SourceY, glyph.Width, glyph.Height), Color8(glyph.Color));
            }
        }
        return run.Height;
    }

    /// <summary>
    /// A message box at the centre of a screen of the size (<c>TextPopup_New</c>): a feathered black box
    /// <paramref name="width"/> + 8 across and <paramref name="height"/> down, with the text 4 in, in the
    /// main font.
    /// </summary>
    public void MessageBox(CanvasItem canvas, Vector2I screen, string text, int width, int height, Rgba color)
    {
        var x0 = (screen.X - width) / 2;
        var y0 = (screen.Y - height) / 2;
        Images.DrawPanel(canvas, Images.Around(new ScreenRect(x0, y0, x0 + width + 8, y0 + height)));
        Text(canvas, Main, text, x0 + 4, y0 + 4, new ScreenRect(x0 + 4, y0, x0 + 4 + width, y0 + height + 4), color);
    }

    /// <summary>
    /// A message fitted to its text (<c>TextBox_Centered</c>): as wide as the text up to
    /// <paramref name="wrap"/>, wrapping there, and as tall as it wraps plus 5, with
    /// <paramref name="extraWidth"/> and <paramref name="extraHeight"/> more.
    /// </summary>
    public void Message(CanvasItem canvas, Vector2I screen, string text, int wrap, Rgba color, int extraWidth = 0, int extraHeight = 0)
    {
        var width = Math.Min(Main.Width(text), wrap);
        MessageBox(canvas, screen, text, width + extraWidth, Main.Layout.Height(text, 0, width) + 5 + extraHeight, color);
    }

    /// <summary>
    /// A question over choices (<c>WidgetChoiceDia::Update</c>): the question wrapped to
    /// <paramref name="width"/> and its options under it, <paramref name="gap"/> apart, all centred on the
    /// screen on a feathered panel as wide as the popup, with the cursor's option in <paramref name="cursor"/>.
    /// </summary>
    public void Choice(CanvasItem canvas, Vector2I screen, string text, int width, int gap, Rgba textColor, Rgba optionColor,
        MenuList<string> options, Rgba cursor)
    {
        var font = Main;
        var half = width / 2;
        var centre = new Vector2I(screen.X / 2, screen.Y / 2);
        var (left, right) = (centre.X - half, centre.X + half);
        var textHeight = font.Layout.Height(text, left, right);
        var rows = options.Rows;
        var rowHeight = font.LineHeight;
        var top = centre.Y - (rows.Count * rowHeight + textHeight + gap) / 2;
        var choicesTop = top + textHeight + gap;
        var bottom = choicesTop + rows.Count * rowHeight;
        Images.DrawPanel(canvas, Images.Around(new ScreenRect(left, top, right, bottom)));
        Text(canvas, font, text, left, top, new ScreenRect(left, top, right, choicesTop), textColor);
        for (var i = 0; i < rows.Count; i++)
        {
            Text(canvas, font, rows[i], left - 1, choicesTop + i * rowHeight, new ScreenRect(left - 1, choicesTop, right + 1, bottom),
                i == options.Cursor ? cursor : optionColor);
        }
    }

    /// <summary>Prints one line unbounded but by the screen.</summary>
    public int Text(CanvasItem canvas, UiFont font, string text, int x, int y, Rgba color) =>
        Text(canvas, font, text, x, y, new ScreenRect(-4096, -4096, 4096, 4096), color);

    /// <summary>An image's size, or 0 × 0 if no plugin has it.</summary>
    public Vector2I Size(RecordId image) => Images.Get(image) is { } texture ? (Vector2I)texture.GetSize() : Vector2I.Zero;

    /// <summary>
    /// Draws an image at (x, y), at its own size or scaled to <paramref name="size"/>, as a texture
    /// rectangle times a colour (<c>WidgetBorg8</c>). Scaled images are filtered, as the console's are.
    /// </summary>
    /// <param name="mirror">Draws it flipped left to right.</param>
    public void Image(CanvasItem canvas, RecordId image, int x, int y, Vector2I? size = null, Rgba? color = null, bool mirror = false)
    {
        if (Images.Get(image) is not { } texture)
            return;
        var drawn = size ?? (Vector2I)texture.GetSize();
        // A rectangle of negative width draws its texture flipped, in the same place.
        var rect = new Rect2(x, y, mirror ? -drawn.X : drawn.X, drawn.Y);
        canvas.DrawTextureRectRegion(texture, rect, Sampled(new Rect2(Vector2.Zero, texture.GetSize()), drawn),
            color is { } c ? Color8(c) : Colors.White);
    }

    /// <summary>
    /// Where to read a source rectangle from so that scaling it samples as the RDP does: its first
    /// pixel on the first texel's centre and each next one a step of the scale on, where Godot's
    /// first sample is half a step in.
    /// </summary>
    private static Rect2 Sampled(Rect2 source, Vector2 drawn)
    {
        var step = source.Size / drawn;
        return new Rect2(source.Position + (Vector2.One - step) / 2, source.Size);
    }

    /// <summary>
    /// Draws part of an image: <paramref name="source"/>, in the image's pixels, at (x, y) at its own
    /// size or scaled to <paramref name="size"/>. The wide layouts compose their parchment from cut-outs.
    /// </summary>
    public void Cut(CanvasItem canvas, RecordId image, Rect2I source, int x, int y, Vector2I? size = null, Rgba? color = null, bool mirror = false)
    {
        if (Images.Get(image) is not { } texture)
            return;
        var drawn = size ?? source.Size;
        canvas.DrawTextureRectRegion(texture, new Rect2(x, y, mirror ? -drawn.X : drawn.X, drawn.Y), Sampled(source, drawn),
            color is { } c ? Color8(c) : Colors.White);
    }

    /// <summary>
    /// Draws part of an image at its own size with its edges faded out over <paramref name="feather"/>
    /// pixels on each side (left, top, right, bottom), so a piece of parchment blends into the sheet
    /// it's placed on. It's nine quads, the outer corners of the outer ones transparent.
    /// </summary>
    /// <param name="mirror">Flips it left to right; the feathering is by the sides as drawn.</param>
    public void Feathered(CanvasItem canvas, RecordId image, Rect2I source, int x, int y, (int Left, int Top, int Right, int Bottom) feather,
        bool mirror = false)
    {
        if (Images.Get(image) is not { } texture)
            return;
        var texel = Vector2.One / texture.GetSize();
        int[] xs = [0, feather.Left, source.Size.X - feather.Right, source.Size.X];
        int[] ys = [0, feather.Top, source.Size.Y - feather.Bottom, source.Size.Y];
        for (var j = 0; j < 3; j++)
        {
            for (var i = 0; i < 3; i++)
            {
                if (xs[i + 1] <= xs[i] || ys[j + 1] <= ys[j])
                    continue;
                (int I, int J)[] corners = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)];
                canvas.DrawPolygon(
                    [.. corners.Select(c => new Vector2(x + xs[c.I], y + ys[c.J]))],
                    [.. corners.Select(c => c.I is 1 or 2 && c.J is 1 or 2 ? Colors.White : Colors.Transparent)],
                    [.. corners.Select(c => (source.Position + new Vector2(mirror ? source.Size.X - xs[c.I] : xs[c.I], ys[c.J])) * texel)],
                    texture);
            }
        }
    }

    /// <summary>
    /// Draws an image cut off at a right edge, as a widget's bounds cut it: the hit points bar is
    /// drawn to the share of hit points left.
    /// </summary>
    public void ImageTo(CanvasItem canvas, RecordId image, int x, int y, Vector2I size, int right, Rgba? color = null)
    {
        if (Images.Get(image) is not { } texture || right <= x)
            return;
        var shown = Math.Min(right - x, size.X);
        var source = texture.GetSize();
        canvas.DrawTextureRectRegion(texture, new Rect2(x, y, shown, size.Y), new Rect2(0, 0, source.X * shown / size.X, source.Y),
            color is { } c ? Color8(c) : Colors.White);
    }

    /// <summary>The parchment popup at (x, y), with its shadow 5 to the right and 2 down (<c>WidgetMenuPopup</c>).</summary>
    /// <param name="mirror">
    /// Flips it left to right, its straight edge at the right, and its shadow with it: 5 to the left,
    /// past the torn edge, rather than to the right.
    /// </param>
    public void Parchment(CanvasItem canvas, int x, int y, Vector2I? size = null, bool mirror = false)
    {
        // The shadow is 2 wider than the parchment, so mirrored it starts 5 + 2 to the left.
        Image(canvas, MenuImages.ParchmentShadow, mirror ? x - 7 : x + 5, y + 2, size is { } s ? s + new Vector2I(2, 2) : null, mirror: mirror);
        Image(canvas, MenuImages.Parchment, x, y, size, mirror: mirror);
    }

    /// <summary>
    /// Draws a list's rows in view, from (x, y), a row every <paramref name="rowHeight"/>, with the
    /// cursor's row in <paramref name="cursor"/> and the rest in <paramref name="color"/>
    /// (<c>WidgetFastScrollMenu::Update</c>).
    /// </summary>
    /// <param name="draw">Draws a row at its top, in its colour.</param>
    public void List<TRow>(MenuList<TRow> list, int y, int rowHeight, Rgba color, Rgba cursor, Action<TRow, int, Rgba> draw)
    {
        var last = Math.Min(list.Rows.Count, list.First + list.Visible);
        for (var i = list.First; i < last; i++)
            draw(list.Rows[i], y + (i - list.First) * rowHeight, i == list.Cursor ? cursor : color);
    }

    /// <summary>
    /// The arrows that show a list has rows above or below: up at <paramref name="up"/> and down at
    /// <paramref name="down"/>, each drawn while rows are hidden its way (<c>WidgetScrollArrows</c>).
    /// </summary>
    public void Arrows<TRow>(CanvasItem canvas, MenuList<TRow> list, Vector2I up, Vector2I down)
    {
        if (list.MoreAbove)
            Image(canvas, MenuImages.ScrollUp, up.X, up.Y, color: ArrowColor);
        if (list.MoreBelow)
            Image(canvas, MenuImages.ScrollDown, down.X, down.Y, color: ArrowColor);
    }

    public static Color Color8(Rgba c) => Color.Color8(c.R, c.G, c.B, c.A);
}
