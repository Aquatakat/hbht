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
    /// <param name="clip">A box the glyphs are cut to besides, which doesn't move the wrap, as a widget group's is.</param>
    public int Text(CanvasItem canvas, UiFont font, string text, int x, int y, ScreenRect bounds, Rgba color, Rect2I? clip = null)
    {
        var run = font.Layout.Print(text, x, y, bounds, color);
        if (font.Sheet is { } sheet)
        {
            foreach (var glyph in run.Glyphs)
            {
                DrawCut(canvas, sheet, new Rect2(glyph.X, glyph.Y, glyph.Width, glyph.Height),
                    new Rect2(glyph.SourceX, glyph.SourceY, glyph.Width, glyph.Height), clip, Tint(glyph.Color), sampled: false);
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
    /// A menu's message: fitted to its text and off-white, or in a box of its own size and white (<see cref="MenuMessage.Box"/>);
    /// a crafting popup's result has 16 more across and 5 more down (<c>WidgetCrafting::TextPopup</c>).
    /// </summary>
    public void Message(CanvasItem canvas, Vector2I screen, MenuMessage message)
    {
        if (message.Box is { } box)
            MessageBox(canvas, screen, message.Text, box.Width, box.Height, MenuMessage.White);
        else
            Message(canvas, screen, message.Text, message.Wrap, MenuMessage.Color, message.FromCrafting ? 16 : 0, message.FromCrafting ? 5 : 0);
    }

    /// <summary>A menu's question over choices, with the cursor's option in <paramref name="cursor"/>.</summary>
    public void Choice(CanvasItem canvas, Vector2I screen, MenuChoice choice, Rgba cursor) =>
        Choice(canvas, screen, choice.Text, choice.Width, choice.Gap, choice.Color, choice.ChoiceColor, choice.Choices, cursor);

    /// <summary>The popups a menu has open that are drawn over the screen: its question, then its message over that.</summary>
    public void Overlay(CanvasItem canvas, Vector2I screen, MenuOverlay overlay)
    {
        if (overlay.Choice is { } choice)
            Choice(canvas, screen, choice, overlay.Highlight);
        if (overlay.Message is { } message)
            Message(canvas, screen, message);
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

    /// <summary>Prints one line unbounded but by the screen, and cut to <paramref name="clip"/> if there's one.</summary>
    public int Text(CanvasItem canvas, UiFont font, string text, int x, int y, Rgba color, Rect2I? clip = null) =>
        Text(canvas, font, text, x, y, new ScreenRect(-4096, -4096, 4096, 4096), color, clip);

    /// <summary>An image's size, or 0 × 0 if no plugin has it.</summary>
    public Vector2I Size(RecordId image) => Images.Get(image) is { } texture ? (Vector2I)texture.GetSize() : Vector2I.Zero;

    /// <summary>
    /// Draws an image at (x, y), at its own size or scaled to <paramref name="size"/>, as a texture
    /// rectangle times a colour (<c>WidgetBorg8</c>). Scaled images are filtered, as the console's are.
    /// </summary>
    /// <param name="mirror">Draws it flipped left to right.</param>
    /// <param name="clip">A box it's cut to, cropped rather than squeezed; not with <paramref name="mirror"/>.</param>
    public void Image(CanvasItem canvas, RecordId image, int x, int y, Vector2I? size = null, Rgba? color = null, bool mirror = false,
        Rect2I? clip = null)
    {
        if (Images.Get(image) is not { } texture)
            return;
        var drawn = size ?? (Vector2I)texture.GetSize();
        var tint = color is { } c ? Tint(c) : White;
        if (clip is not null && !mirror)
        {
            DrawCut(canvas, texture, new Rect2(x, y, drawn), new Rect2(Vector2.Zero, texture.GetSize()), clip, tint);
            return;
        }
        // A rectangle of negative width draws its texture flipped, in the same place.
        var rect = new Rect2(x, y, mirror ? -drawn.X : drawn.X, drawn.Y);
        canvas.DrawTextureRectRegion(texture, rect, Sampled(new Rect2(Vector2.Zero, texture.GetSize()), drawn), tint);
    }

    /// <summary>A whole texture stretched over a rectangle, filtered as the console's are, and cut to a clip if there's one.</summary>
    public void Texture(CanvasItem canvas, Texture2D texture, Rect2 rect, Rect2I? clip = null) =>
        DrawCut(canvas, texture, rect, new Rect2(Vector2.Zero, texture.GetSize()), clip, White);

    /// <summary>
    /// Draws a texture's source rectangle over a rectangle, cut to a clip: what's outside it is cropped off, the
    /// texels left where they were.
    /// </summary>
    /// <param name="sampled">Whether to sample a scaled image as the RDP does (<see cref="Sampled"/>); glyphs are drawn 1:1.</param>
    private static void DrawCut(CanvasItem canvas, Texture2D texture, Rect2 rect, Rect2 source, Rect2I? clip, Color color, bool sampled = true)
    {
        if (clip is { } box)
        {
            var cut = rect.Intersection(new Rect2(box.Position, box.Size));
            if (cut.Size.X <= 0 || cut.Size.Y <= 0)
                return;
            var texels = source.Size / rect.Size;
            source = new Rect2(source.Position + (cut.Position - rect.Position) * texels, cut.Size * texels);
            rect = cut;
        }
        canvas.DrawTextureRectRegion(texture, rect, sampled ? Sampled(source, rect.Size) : source, color);
    }

    /// <summary>
    /// Draws an image at (x, y) at its own size, with what's painted into it covered by <paramref name="patches"/> of
    /// plain parchment (<see cref="UiImages.Plain"/>), times a colour. A tab beside the one showing is faded by it.
    /// </summary>
    public void Plain(CanvasItem canvas, RecordId image, IReadOnlyList<PlainImage.Patch> patches, int x, int y, Rgba color)
    {
        if (Images.Plain(image, patches) is { } texture)
            canvas.DrawTextureRect(texture, new Rect2(x, y, texture.GetSize()), false, Tint(color));
    }

    /// <summary>
    /// Draws one of the HUD's images: at its place in quarter pixels, scaled or stretched, times its colour, and cut to
    /// its clip, inclusive, which crops the image rather than squeezing it (<c>WidgetBorg8::Render</c>).
    /// </summary>
    public void Sprite(CanvasItem canvas, HudSprite sprite)
    {
        if (Images.Get(sprite.Image) is not { } texture)
            return;
        var source = new Rect2(Vector2.Zero, texture.GetSize());
        var size = sprite.Size is var (w, h) ? new Vector2(w, h) : source.Size * sprite.Scale;
        var rect = new Rect2(sprite.X, sprite.Y, size);
        if (sprite.Clip is { } c)
        {
            var cut = rect.Intersection(new Rect2(c.Left, c.Top, c.Right - c.Left + 1, c.Bottom - c.Top + 1));
            if (cut.Size.X <= 0 || cut.Size.Y <= 0)
                return;
            var texels = source.Size / size;
            source = new Rect2((cut.Position - rect.Position) * texels, cut.Size * texels);
            rect = cut;
        }
        canvas.DrawTextureRectRegion(texture, rect, Sampled(source, rect.Size), Tint(sprite.Color));
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
    /// size or scaled to <paramref name="size"/>. The journal cuts its parchment's faded edges from the book's image with it.
    /// </summary>
    public void Cut(CanvasItem canvas, RecordId image, Rect2I source, int x, int y, Vector2I? size = null, Rgba? color = null, bool mirror = false)
    {
        if (Images.Get(image) is not { } texture)
            return;
        var drawn = size ?? source.Size;
        canvas.DrawTextureRectRegion(texture, new Rect2(x, y, mirror ? -drawn.X : drawn.X, drawn.Y), Sampled(source, drawn),
            color is { } c ? Tint(c) : White);
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
            color is { } c ? Tint(c) : White);
    }

    /// <summary>The parchment popup at (x, y), with its shadow 5 to the right and 2 down (<c>WidgetMenuPopup</c>).</summary>
    public void Parchment(CanvasItem canvas, int x, int y)
    {
        Image(canvas, MenuImages.ParchmentShadow, x + 5, y + 2);
        Image(canvas, MenuImages.Parchment, x, y);
    }

    /// <summary>
    /// Draws a list's rows in view, from (x, y), a row every <paramref name="rowHeight"/>, with the
    /// cursor's row in <paramref name="cursor"/> and the rest in <paramref name="color"/>
    /// (<c>WidgetFastScrollMenu::Update</c>).
    /// </summary>
    /// <param name="draw">Draws a row at its top, in its colour.</param>
    public void List<TRow>(MenuList<TRow> list, ListLayout layout, Rgba color, Rgba cursor, Action<TRow, int, Rgba> draw)
    {
        var last = Math.Min(list.Rows.Count, list.First + list.Visible);
        for (var i = list.First; i < last; i++)
            draw(list.Rows[i], layout.Bounds.Top + (i - list.First) * layout.RowHeight, i == list.Cursor ? cursor : color);
    }

    /// <summary>
    /// The arrows that show a list has rows above or below: up at <paramref name="up"/> and down at
    /// <paramref name="down"/>, each drawn while rows are hidden its way (<c>WidgetScrollArrows</c>).
    /// </summary>
    public void Arrows<TRow>(CanvasItem canvas, MenuList<TRow> list, ListLayout layout)
    {
        if (list.MoreAbove)
            Image(canvas, MenuImages.ScrollUp, layout.UpArrow.X, layout.UpArrow.Y, color: ArrowColor);
        if (list.MoreBelow)
            Image(canvas, MenuImages.ScrollDown, layout.DownArrow.X, layout.DownArrow.Y, color: ArrowColor);
    }

    public static Color Color8(Rgba c) => Color.Color8(c.R, c.G, c.B, c.A);

    /// <summary>
    /// How opaque what's drawn is, from 0 to 1, times each colour's own alpha: a page of the System tab fading in or
    /// out (<c>Widget.SetAlpha</c> on the page). The message boxes and choices aren't faded by it.
    /// </summary>
    public float Opacity { get; set; } = 1;

    /// <summary>A colour as it's drawn, its alpha times <see cref="Opacity"/>.</summary>
    public Color Tint(Rgba c) => Color8(c) with { A = c.A / 255f * Opacity };

    private Color White => new(1, 1, 1, Opacity);
}
