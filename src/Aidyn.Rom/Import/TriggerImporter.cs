using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Maps;
using Aidyn.Rom.Quests;

namespace Aidyn.Rom.Import;

/// <summary>
/// The base game's triggers (roadmap 2.10): what the original's engine code does when a new game begins, a cut-scene's
/// script ends and a cinematic ends, as records a plugin can change. docs/systems/events.md has each with its address.
/// </summary>
internal static class TriggerImporter
{
    /// <summary>The opening's cut-scene: Borg13 0x0362 (number 1004), from its dialogue object in Overworld I08's Borg9.</summary>
    internal const int OpeningScript = 0x0362, OpeningMap = 0x0FDB;

    /// <summary>The cinematics by their place in the table.</summary>
    internal const int Opening = 0, Roog = 2, Ending = 3;

    /// <summary>The flag Roog's cinematic waits on as the Rooughah scene ends (<c>CutScene.Free</c>'s kind 3).</summary>
    internal const int RoogFlag = 3442;

    public static void Import(ImportContext ctx)
    {
        ctx.Report("Triggers");
        var opening = Borg9Voxels.Read(ctx.Archive.Read(OpeningMap))
            .First(o => o.Data is DialogueData d && d.Script == OpeningScript).Index;
        ctx.WriteRecords("triggers.json", BuildTriggers(ctx.Engine, MapRecord.VoxelId(RecordId.Hex(RecordTypes.Map, OpeningMap), opening)));
    }

    /// <summary>
    /// The triggers, numbered from 0: a new game's opening cinematic and its script after it, each script-endings table
    /// entry (<c>CutScene.Free</c>'s kinds: the game over, Roog's cinematic with its flag, the ending's), Roog's True Name
    /// after his cinematic, and the ending's credits after its.
    /// </summary>
    /// <param name="openingObject">The dialogue object the opening's cut-scene starts from.</param>
    internal static List<TriggerRecord> BuildTriggers(EngineTables tables, RecordId openingObject)
    {
        var cinematic = (int n) => RecordId.Decimal(RecordTypes.Cinematic, n);
        var triggers = new List<(string On, RecordId? Subject, IReadOnlyList<TriggerCondition>? If, IReadOnlyList<TriggerEffect> Do)>
        {
            ("game.started", null, null, [new TriggerEffect.Cinematic(cinematic(Opening))]),
            ("cinematic.ended", cinematic(Opening), null, [new TriggerEffect.Script(openingObject)]),
        };
        foreach (var (script, ending) in tables.ScriptEndings.OrderBy(e => e.Key))
        {
            var subject = RecordId.Hex(RecordTypes.Dialogue, script);
            triggers.Add(ending switch
            {
                ScriptEnding.GameOver => ("script.ended", subject, null, [new TriggerEffect.GameOver()]),
                ScriptEnding.RoogCinematic => ("script.ended", subject,
                    [new TriggerCondition(DialogueCommand.CheckFlag, Target: DbRecordIds.Flag(RoogFlag))],
                    [new TriggerEffect.Cinematic(cinematic(Roog))]),
                ScriptEnding.EndingCinematic => ("script.ended", subject, null, [new TriggerEffect.Cinematic(cinematic(Ending))]),
                _ => throw new InvalidDataException($"Script {script} has an ending no trigger carries."),
            });
        }
        triggers.Add(("cinematic.ended", cinematic(Roog), null, [new TriggerEffect.TrueName(DbRecordIds.Required(new ItemId(Alaron)))]));
        triggers.Add(("cinematic.ended", cinematic(Ending), null, [new TriggerEffect.Credits(Ending: true)]));
        return [.. triggers.Select((t, i) => new TriggerRecord(RecordId.Decimal(RecordTypes.Trigger, i), t.On, t.Subject, t.If, t.Do))];
    }

    /// <summary>Alaron's entity, who gains his True Name.</summary>
    private const ushort Alaron = 0x023F;
}
