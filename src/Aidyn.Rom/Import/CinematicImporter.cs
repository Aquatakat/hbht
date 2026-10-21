using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Graphics;
using Aidyn.Rom.Text;

namespace Aidyn.Rom.Import;

/// <summary>
/// The four cinematics and the credits.
/// </summary>
internal static class CinematicImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Cinematics");
        ctx.WriteRecords("cinematics.json", BuildCinematics(ctx.Rom, ctx.Archive, ctx.Db));
        ctx.WriteRecords("credits.json", new[] { BuildCredits(CreditsTable.Read(ctx.Rom), ctx.Db) });
    }

    /// <summary>
    /// The four cinematics: each shot's animation and its scene, music, planes, captions and fades, the table's times
    /// doubled into ticks as the game doubles them, and the Theatre menu's title, which is listed by the done flag.
    /// </summary>
    private static List<CinematicRecord> BuildCinematics(RomImage rom, BorgArchive archive, GameDb db)
    {
        var titles = db.CinematicTitles.ToDictionary(t => t.Flag, t => t.Text.Trim());
        return [.. Cinematics.Read(rom).Select((c, i) => new CinematicRecord(
            RecordId.Decimal(RecordTypes.Cinematic, i),
            titles.GetValueOrDefault(c.DoneFlag),
            [.. c.Scenes.Select((s, shot) =>
            {
                var animation = Borg6Animation.Parse(archive.Read(s.Animation));
                return new CinematicShot(
                    RecordId.Hex(RecordTypes.Scene, animation.Scene),
                    RecordId.Hex(RecordTypes.Animation, s.Animation),
                    animation.Length,
                    RecordId.Hex(RecordTypes.Sequence, s.Audio),
                    s.Near,
                    s.Far,
                    s.Captions is { } list ? [.. db.Captions[list].Select(t => new CinematicCaption(t.Start * 2, t.End * 2, t.Text))] : [],
                    [.. c.Fades.Where(f => f.Scene == shot).Select(f => new CinematicFade(
                        f.Color, f.Start * 2, f.Length * 2, f.Hold * 2))]);
            })],
            DbRecordIds.Flag(c.DoneFlag),
            CinematicRecord.ConsoleAspect))];
    }

    /// <summary>
    /// The credits: the credits' string table cut into cards by the code's table, each its first line over the rest,
    /// the last running to the line before the table's last, "The End".
    /// </summary>
    internal static CreditsRecord BuildCredits(CreditsTable table, GameDb db)
    {
        var lines = db.Strings[RomStringTable.Credits].Select(DialogueText.Unescape).ToList();
        var starts = table.Starts.Append(lines.Count - 1).ToList();
        return new CreditsRecord(
            RecordId.Decimal(RecordTypes.Credits, 0),
            [.. starts.Zip(starts.Skip(1), (from, to) => new CreditsCard(lines[from], lines[(from + 1)..to]))],
            table.Shown,
            lines[^1]);
    }
}
