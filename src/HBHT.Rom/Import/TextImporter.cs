using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;
using HBHT.Rom.Graphics;
using HBHT.Rom.Text;

namespace HBHT.Rom.Import;

/// <summary>
/// The string tables and the fonts.
/// </summary>
internal static class TextImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Text");
        ctx.WriteRecords("strings.json", BuildStrings(ctx.Db));
        ctx.WriteRecords("fonts.json", BuildFonts(Fonts.Read(ctx.Rom), ctx.Archive));
    }

    /// <summary>Every string table, numbered in ROM order, with its escapes decoded as the game loads them.</summary>
    internal static List<StringsRecord> BuildStrings(GameDb db) =>
    [
        .. Enum.GetValues<RomStringTable>().Select(table => new StringsRecord(
            RecordId.Decimal(RecordTypes.Strings, (int)table), table.ToString(), [.. db.Strings[table].Select(DialogueText.Unescape)])),
    ];

    /// <summary>
    /// The fonts, with each glyph's width measured from its sheet as the game does, and the character
    /// map and button colours every font shares.
    /// </summary>
    internal static List<FontRecord> BuildFonts(Fonts fonts, BorgArchive archive)
    {
        var glyphs = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < fonts.Glyphs.Count; i++)
        {
            if (fonts.Glyphs[i] != Fonts.NotDrawn)
                glyphs[((char)(Fonts.FirstCharacter + i)).ToString()] = fonts.Glyphs[i];
        }
        var colors = new SortedDictionary<string, Plugins.Records.Rgba>(StringComparer.Ordinal);
        for (var i = 0; i < fonts.ButtonColors.Count; i++)
        {
            var (r, g, b) = fonts.ButtonColors[i];
            colors[((char)(Fonts.FirstButton + i)).ToString()] = new Plugins.Records.Rgba(r, g, b, 255);
        }

        return
        [
            .. fonts.Faces.Select((face, i) =>
            {
                var sheet = Borg8Image.Parse(archive.Read(face.Image));
                return new FontRecord(RecordId.Decimal(RecordTypes.Font, i), RecordId.Hex(RecordTypes.Image, face.Image),
                    face.Rows, face.Columns, sheet.Width / face.Columns, sheet.Height / face.Rows,
                    Fonts.MeasureGlyphs(sheet, face.Rows, face.Columns), glyphs, colors);
            }),
        ];
    }
}
