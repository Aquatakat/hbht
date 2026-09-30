using System.Buffers.Binary;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Engine;

/// <summary>A font: a Borg8 sheet of glyph cells, filled a column at a time (<c>font_face</c>).</summary>
/// <param name="Image">The Borg8's archive index.</param>
/// <param name="Rows">How many cells tall the sheet is. Glyph <c>g</c> is in column <c>g / Rows</c>, row <c>g % Rows</c>.</param>
/// <param name="Columns">How many cells wide.</param>
public sealed record FontFace(int Image, int Rows, int Columns);

/// <summary>
/// The fonts and how characters map to their glyphs, read from the code segment. Every font shares
/// the one character map (<c>struct_unk_.textIndexies</c>) and the controller buttons' colours
/// (<c>extraFontColors</c>).
/// </summary>
/// <param name="Faces">The main font, then the slim one.</param>
/// <param name="Glyphs">Each character's glyph from <see cref="FirstCharacter"/> on, or <see cref="NotDrawn"/>.</param>
/// <param name="ButtonColors">The RGB each character from <see cref="FirstButton"/> on draws in, in place of the text's colour.</param>
public sealed record Fonts(IReadOnlyList<FontFace> Faces, IReadOnlyList<int> Glyphs, IReadOnlyList<(byte R, byte G, byte B)> ButtonColors)
{
    /// <summary>The character the map starts at: the map is indexed by the character minus a space.</summary>
    public const int FirstCharacter = 0x20;

    /// <summary>The glyph number that marks a character the game skips: it isn't drawn and takes no room.</summary>
    public const int NotDrawn = 0x2B;

    /// <summary>The first character drawn in a button's colour: every character after <c>~</c>.</summary>
    public const int FirstButton = 0x7F;

    public static Fonts Read(RomImage rom)
    {
        var data = CodeSegment.Table(rom, CodeTable.FontFaces);
        var faces = new List<FontFace>();
        for (var at = 0; at + 8 <= data.Length && CodeSegment.U32(data, at) is var image and not 0; at += 8)
            faces.Add(new FontFace((int)image, data[at + 4], data[at + 5]));

        var glyphs = CodeSegment.Table(rom, CodeTable.FontGlyphs).ToArray().Select(b => (int)b).ToList();

        data = CodeSegment.Table(rom, CodeTable.FontColors);
        var colors = new List<(byte, byte, byte)>();
        for (var at = 0; at + 3 <= data.Length && data.Slice(at, 3).IndexOfAnyExcept((byte)0) >= 0; at += 3)
            colors.Add((data[at], data[at + 1], data[at + 2]));

        return new Fonts(faces, glyphs, colors);
    }

    /// <summary>
    /// How wide each glyph is, measured from the sheet as <c>Font::SetupBorg8</c> does, by glyph number.
    /// </summary>
    /// <remarks>
    /// The scan reads the top row of each cell leftward from its right edge, and the glyph ends
    /// after the last texel that differs from a reference texel. For 8-bit formats that texel is
    /// the cell's own rightmost one; for 16- and 32-bit formats it's the next one in memory, which
    /// for the last column is the start of the sheet's next row, and a cell whose rightmost texel
    /// already differs is full width. Checked against the debug ROM's machine code, since the
    /// decomp's 8-bit case compares a texel with itself. A cell with no differing texel sends the
    /// original's scan out of the cell; no character uses one, and it's 0 here.
    /// </remarks>
    /// <exception cref="NotSupportedException">The sheet is 4-bit, which the original scans wrongly and no font uses.</exception>
    public static IReadOnlyList<int> MeasureGlyphs(Borg8Image sheet, int rows, int columns)
    {
        var bytesPerTexel = sheet.Format switch
        {
            Borg8Format.Rgba32 => 4,
            Borg8Format.Rgba16 or Borg8Format.Ia16 => 2,
            Borg8Format.Ci8 or Borg8Format.Ia8 or Borg8Format.I8 => 1,
            _ => throw new NotSupportedException($"No font is {sheet.Format}, whose glyph widths the original measures wrongly."),
        };
        var bitmap = sheet.Bitmap;
        var cellWidth = sheet.Width / columns;
        var cellHeight = sheet.Height / rows;
        var widths = new int[rows * columns];
        for (var glyph = 0; glyph < widths.Length; glyph++)
        {
            var start = (glyph % rows * cellHeight * sheet.Width) + (glyph / rows * cellWidth);
            var reference = Texel(bitmap, bytesPerTexel, bytesPerTexel == 1 ? start + cellWidth - 1 : start + cellWidth);
            var width = cellWidth;
            if (Texel(bitmap, bytesPerTexel, start + cellWidth - 1) == reference)
            {
                do
                    width--;
                while (width > 0 && Texel(bitmap, bytesPerTexel, start + width - 1) == reference);
            }
            widths[glyph] = width;
        }
        return widths;

        static uint Texel(ReadOnlySpan<byte> bitmap, int bytesPerTexel, int index) => bytesPerTexel switch
        {
            4 => BinaryPrimitives.ReadUInt32BigEndian(bitmap[(index * 4)..]),
            2 => BinaryPrimitives.ReadUInt16BigEndian(bitmap[(index * 2)..]),
            _ => bitmap[index],
        };
    }
}
