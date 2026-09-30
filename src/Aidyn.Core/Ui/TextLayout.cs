using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// A glyph placed on the original's 320 × 240 screen: a piece of its font's sheet, drawn at
/// <see cref="X"/>, <see cref="Y"/> and scaled by the text's scale.
/// </summary>
/// <param name="SourceX">Where the piece starts on the sheet, in texels.</param>
/// <param name="Width">The piece's size on the sheet, in texels.</param>
/// <param name="Color">What the sheet's texels are multiplied by.</param>
public readonly record struct PlacedGlyph(int X, int Y, int SourceX, int SourceY, int Width, int Height, Rgba Color);

/// <summary>A rectangle on the 320 × 240 screen, right and bottom exclusive, as the original's widgets bound what they draw.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>Printed text: its glyphs, and how tall its lines are all together.</summary>
public sealed record TextRun(IReadOnlyList<PlacedGlyph> Glyphs, int Height);

/// <summary>
/// Lays text out in a font as the original's font engine does (<c>Font::PrintMain</c>,
/// <c>GetHeightScaled</c>, <c>GetWidthScaled</c>).
/// </summary>
/// <remarks>
/// Text wraps only at spaces. After a space, if the next word would end past the right edge, the
/// line breaks and the space is dropped. A space at the start of a line takes no room, and a word
/// longer than the line runs past the edge. Lines are the cell's height plus 2 apart. Characters
/// the font has no glyph for are skipped, and so are control characters but the newline, which
/// the original reads garbage for.
/// </remarks>
public sealed class TextLayout(FontRecord font)
{
    public FontRecord Font { get; } = font;

    /// <summary>How far apart lines are.</summary>
    public int LineHeight(float scaleY = 1) => (int)(Font.CellHeight * scaleY + 2f);

    /// <summary>The glyph a character draws, or none if it isn't drawn. A glyph the font has no width for isn't drawn either.</summary>
    public int? Glyph(char c) =>
        c >= ' ' && Font.Glyphs.TryGetValue(c.ToString(), out var glyph) && glyph >= 0 && glyph < Font.Widths.Count ? glyph : null;

    /// <summary>How far a character moves the pen (<c>GetCharWidthScaled</c>).</summary>
    public int Advance(char c, float scaleX = 1) => Glyph(c) is { } glyph ? (int)(Font.Widths[glyph] * scaleX) : 0;

    /// <summary>How wide text is on one line, newlines ignored (<c>GetWidthScaled</c>).</summary>
    public int Width(string text, float scaleX = 1)
    {
        var width = 0;
        foreach (var c in text)
            width += Advance(c, scaleX);
        return width;
    }

    /// <summary>
    /// How tall text is when it starts at <paramref name="x"/> and wraps at <paramref name="right"/>
    /// (<c>GetHeightScaled</c>): its lines times the line height, or 0 if it's empty or has no room.
    /// </summary>
    public int Height(string text, int x, int right, float scaleX = 1, float scaleY = 1) =>
        text.Length == 0 || right <= x ? 0 : Walk(text, x, 0, right, scaleX, scaleY, null) + LineHeight(scaleY);

    /// <summary>
    /// Prints text from <paramref name="x"/>, <paramref name="y"/>, wrapping at the right edge of
    /// <paramref name="bounds"/> and drawing only what's inside them (<c>Font::PrintMain</c>).
    /// </summary>
    /// <param name="color">The text's colour. Controller buttons have their own, with this alpha.</param>
    public TextRun Print(string text, int x, int y, ScreenRect bounds, Rgba color, float scaleX = 1, float scaleY = 1)
    {
        if (bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
            return new TextRun([], 0);
        var glyphs = new List<PlacedGlyph>();
        var lastLine = Walk(text, x, y, bounds.Right, scaleX, scaleY, (c, glyph, penX, penY) =>
        {
            if (Clip(glyph, penX, penY, bounds, scaleX, scaleY) is { } placed)
                glyphs.Add(placed with { Color = ColorOf(c, color) });
        });
        return new TextRun(glyphs, lastLine + LineHeight(scaleY) - y);
    }

    /// <summary>
    /// Moves the pen through the text, calling <paramref name="draw"/> for each glyph, and returns the
    /// last line's top.
    /// </summary>
    private int Walk(string text, int x, int y, int right, float scaleX, float scaleY, Action<char, int, int, int>? draw)
    {
        var lineHeight = LineHeight(scaleY);
        var penX = x;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                y += lineHeight;
                penX = x;
            }
            else if (Glyph(c) is not { } glyph)
            {
            }
            else if (c != ' ')
            {
                draw?.Invoke(c, glyph, penX, y);
                penX += Advance(c, scaleX);
            }
            else if (penX != x)
            {
                penX += Advance(c, scaleX);
                var end = penX;
                for (var j = i + 1; j < text.Length && text[j] is not ' ' and not '\n'; j++)
                    end += Advance(text[j], scaleX);
                if (right < end)
                {
                    y += lineHeight;
                    penX = x;
                }
            }
        }
        return y;
    }

    /// <summary>
    /// The part of a glyph inside the bounds. The original tests against the unscaled cell, and
    /// scales only how much it cuts off.
    /// </summary>
    private PlacedGlyph? Clip(int glyph, int x, int y, ScreenRect bounds, float scaleX, float scaleY)
    {
        var width = Font.Widths[glyph];
        var height = Font.CellHeight;
        if (y + height <= bounds.Top || bounds.Bottom <= y)
            return null;

        var left = x < bounds.Left ? (int)((bounds.Left - x) / scaleX) : 0;
        var cutRight = bounds.Right < x + width ? (int)((x + width - bounds.Right) / scaleX) : 0;
        var top = y < bounds.Top ? (int)((bounds.Top - y) / scaleY) : 0;
        var cutBottom = bounds.Bottom < y + height ? (int)((y + height - bounds.Bottom) / scaleY) : 0;
        if (left >= width - cutRight || top >= height - cutBottom)
            return null;

        var sourceX = glyph / Font.Rows * Font.CellWidth;
        var sourceY = glyph % Font.Rows * Font.CellHeight;
        return new PlacedGlyph(x + left, y + top, sourceX + left, sourceY + top, width - cutRight - left, height - cutBottom - top, default);
    }

    private Rgba ColorOf(char c, Rgba text) =>
        c > '~' && Font.ButtonColors?.TryGetValue(c.ToString(), out var button) == true ? button with { A = text.A } : text;
}
