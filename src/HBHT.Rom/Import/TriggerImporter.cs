using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Text;
using HBHT.Rom.Engine;
using HBHT.Rom.Maps;
using HBHT.Rom.Quests;

namespace HBHT.Rom.Import;

/// <summary>
/// The base game's triggers (roadmap 2.10): what the original's engine code does when a new game begins, a cut-scene's
/// script ends, a cinematic ends, the first spell is trained, the first potion is gained and a journal entry is told, as
/// records a plugin can change. docs/systems/events.md has each with its address.
/// </summary>
internal static class TriggerImporter
{
    /// <summary>The opening's cut-scene: Borg13 0x0362 (number 1004), from its script object in Overworld I08's Borg9.</summary>
    internal const int OpeningScript = 0x0362, OpeningMap = 0x0FDB;

    /// <summary>The cinematics by their place in the table.</summary>
    internal const int Opening = 0, Roog = 2, Ending = 3;

    /// <summary>The flag Roog's cinematic waits on as the Rooughah scene ends (<c>CutScene.Free</c>'s kind 3).</summary>
    internal const int RoogFlag = 3442;

    /// <summary>
    /// The flags the first spell learnt at a trainer and the first potion gained in a shop or loot set, each the once its help
    /// shows (<c>WidgetSpellTrain::Purchase</c>, flag <c>0xE1B</c>; <c>WidgetBarter::AFunc</c>, <c>0xE1C</c>).
    /// </summary>
    internal const int FirstSpellFlag = 3611, FirstPotionFlag = 3612;

    /// <summary>Their help, lines of the Common string table, which the parchment shows (<c>WidgetMenuPrompt</c>).</summary>
    internal const int FirstSpellHelp = 497, FirstPotionHelp = 521;

    public static void Import(ImportContext ctx)
    {
        ctx.Report("Triggers");
        var common = ctx.Db.Strings[RomStringTable.Common];
        var messages = new Messages(DialogueText.Unescape(common[FirstSpellHelp]), DialogueText.Unescape(common[FirstPotionHelp]),
            MenuTables.Text(ctx.Rom, CodeTable.JournalNotice));
        ctx.WriteRecords("triggers.json",
            BuildTriggers(ctx.Engine, WorldImporter.FirstScriptObject(ctx.Chunks, ctx.Archive, OpeningMap, OpeningScript), messages));
    }

    /// <summary>What the triggers on the help and the notice show.</summary>
    internal sealed record Messages(string FirstSpell, string FirstPotion, string JournalEntry);

    /// <summary>
    /// The triggers, numbered from 0: a new game's opening cinematic and its script after it, each script-endings table
    /// entry (<c>CutScene.Free</c>'s kinds: the game over, Roog's cinematic with its flag, the ending's), Roog's True Name
    /// after his cinematic, the ending's credits after its, and then the first spell's and first potion's help, each shown
    /// while its flag is clear, which it sets, and the journal's notice.
    /// </summary>
    /// <param name="openingObject">The script object the opening's cut-scene starts from.</param>
    internal static List<TriggerRecord> BuildTriggers(EngineTables tables, RecordId openingObject, Messages messages)
    {
        var cinematic = (int n) => RecordId.Decimal(RecordTypes.Cinematic, n);
        var triggers = new List<(string On, RecordId? Subject, IReadOnlyList<ScriptOp>? If, IReadOnlyList<ScriptOp> Do)>
        {
            ("game.started", null, null, [new ScriptOp.CinematicPlay(cinematic(Opening))]),
            ("cinematic.ended", cinematic(Opening), null, [new ScriptOp.StartScript(openingObject)]),
        };
        foreach (var (script, ending) in tables.ScriptEndings.OrderBy(e => e.Key))
        {
            var subject = RecordId.Hex(RecordTypes.Script, script);
            triggers.Add(ending switch
            {
                ScriptEnding.GameOver => ("script.ended", subject, null, [new ScriptOp.GameOver()]),
                ScriptEnding.RoogCinematic => ("script.ended", subject,
                    [new ScriptOp.CheckFlag(DbRecordIds.Flag(RoogFlag))],
                    [new ScriptOp.CinematicPlay(cinematic(Roog))]),
                ScriptEnding.EndingCinematic => ("script.ended", subject, null, [new ScriptOp.CinematicPlay(cinematic(Ending))]),
                _ => throw new InvalidDataException($"Script {script} has an ending no trigger carries."),
            });
        }
        triggers.Add(("cinematic.ended", cinematic(Roog), null, [new ScriptOp.PartyLearnTrueName(DbRecordIds.Required(new ItemId(Alaron)))]));
        triggers.Add(("cinematic.ended", cinematic(Ending), null, [new ScriptOp.CreditsRoll(Ending: true)]));
        var clear = (int flag) => new ScriptOp.CheckFlag(DbRecordIds.Flag(flag)) { Range = new ScriptRange(0, 0) };
        triggers.Add(("spell.trained", null, [clear(FirstSpellFlag)],
            [new ScriptOp.FlagSet(DbRecordIds.Flag(FirstSpellFlag)), new ScriptOp.MessageShow(messages.FirstSpell)]));
        triggers.Add(("item.gained", null, [new ScriptOp.CheckSubjectKind(ItemKind.Potion), clear(FirstPotionFlag)],
            [new ScriptOp.FlagSet(DbRecordIds.Flag(FirstPotionFlag)), new ScriptOp.MessageShow(messages.FirstPotion)]));
        triggers.Add(("journal.added", null, null, [new ScriptOp.MessageShow(messages.JournalEntry)]));
        return [.. triggers.Select((t, i) => new TriggerRecord(RecordId.Decimal(RecordTypes.Trigger, i), t.On, t.Subject, t.If, t.Do))];
    }

    /// <summary>Alaron's entity, who gains his True Name.</summary>
    private const ushort Alaron = 0x023F;
}
