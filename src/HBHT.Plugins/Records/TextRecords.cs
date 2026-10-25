namespace HBHT.Plugins.Records;

/// <summary>
/// A font (<c>aidyn:font/0</c> is the main one, <c>aidyn:font/1</c> the slim one): a sheet of glyph
/// cells, how wide each glyph is and which character draws which glyph (<c>font_face</c>,
/// <c>Font::SetupBorg8</c>, <c>struct_unk_.textIndexies</c>).
/// </summary>
/// <remarks>
/// The sheet is <see cref="Columns"/> × <see cref="Rows"/> cells, filled a column at a time: glyph
/// <c>g</c> is the cell in column <c>g / Rows</c> and row <c>g % Rows</c>. A glyph is drawn from its
/// cell's left edge, <see cref="Widths"/> wide and a whole cell tall, and the next character starts
/// where it ends. The original measures the widths from the sheet as it loads it; the importer
/// measures them the same way, so a mod that redraws the sheet sets them too.
/// </remarks>
/// <param name="Image">The sheet. The text's colour multiplies it, alpha too.</param>
/// <param name="Widths">Each glyph's width in texels, by glyph number.</param>
/// <param name="Glyphs">
/// Which glyph each character draws, keyed by the character. A character that isn't here isn't
/// drawn and takes no room, as <c>#</c>, <c>[</c> and <c>]</c> in the original.
/// </param>
/// <param name="ButtonColors">
/// The characters drawn in their own colour in place of the text's, which are the controller
/// buttons (<c>extraFontColors</c>). The text's alpha still applies.
/// </param>
public sealed record FontRecord(
    RecordId Id,
    RecordId Image,
    int Rows,
    int Columns,
    int CellWidth,
    int CellHeight,
    IReadOnlyList<int> Widths,
    IReadOnlyDictionary<string, int> Glyphs,
    IReadOnlyDictionary<string, Rgba>? ButtonColors = null);

/// <summary>
/// One of the game's string tables (<c>RomString::Load</c>), numbered in the order they sit in the ROM,
/// such as <c>aidyn:strings/5</c>, the menus' text. Engine code picks lines by position.
/// </summary>
/// <remarks>
/// Lines hold <c>\n</c> as a newline and <c>\xHH</c> as that character, as the game turns them when it
/// loads the table. <c>%s</c> and <c>%d</c> are for the engine to fill in.
/// </remarks>
/// <param name="Name">What the table is for, such as <c>Common</c>.</param>
public sealed record StringsRecord(RecordId Id, string Name, IReadOnlyList<string> Lines);
