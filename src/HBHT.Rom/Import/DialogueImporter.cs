using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Dialogue;
using HBHT.Rom.Quests;
using HBHT.Rom.Text;

namespace HBHT.Rom.Import;

/// <summary>
/// The scripts (Borg13): conversations, cut-scenes, backdrops and signs.
/// </summary>
internal static class DialogueImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Scripts");
        ctx.WriteRecords("dialogue.json", BuildDialogues(ctx.Archive, ctx.Engine));
    }

    /// <summary>
    /// Every Borg13 as a <see cref="DialogueRecord"/>, in archive order. Text is cleaned
    /// (<see cref="DialogueText"/>), the ops the game never reads are dropped, and operands that name
    /// a DB record, a flag or another script become record IDs. A node a Case links to gets its range.
    /// </summary>
    internal static List<DialogueRecord> BuildDialogues(BorgArchive archive, EngineTables tables)
    {
        var records = new List<DialogueRecord>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Dialogue))
        {
            var index = listing.Index;
            var script = Borg13Script.Parse(archive.Read(index));
            var mode = script.StartMode;
            var hasDoneFlag = mode is DialogueMode.Cutscene or DialogueMode.Backdrop or DialogueMode.Help;

            var cased = script.Nodes.Where(n => n.Kind == DialogueNodeKind.Case).SelectMany(n => n.Links).ToHashSet();
            var nodes = script.Nodes.Select(node => new DialogueNode(
                node.Index,
                node.Kind,
                DialogueText.Clean(node.Text),
                NullIfEmpty(node.Ops.Where(op => !UnusedOps.Contains(op.Opcode)).Select(BuildOp).ToList()),
                NullIfEmpty(node.Links),
                cased.Contains(node.Index)
                    ? new DialogueRange(node.Min, node.Max == Borg13Node.NoUpperBound ? null : node.Max)
                    : null)).ToList();

            records.Add(new DialogueRecord(
                RecordId.Hex(RecordTypes.Dialogue, index),
                script.Id,
                mode,
                nodes,
                NullIfEmpty(script.Actors.Select(DbRecordIds.For).OfType<RecordId>().ToList()),
                hasDoneFlag ? DbRecordIds.Flag(script.DoneFlag) : null,
                mode is DialogueMode.Backdrop or DialogueMode.Help ? script.Backdrop : null,
                tables.ScriptBattleFlags.TryGetValue(index, out var battle) ? DbRecordIds.Flag(battle) : null,
                tables.CampfireScenes.Contains(index),
                script.BlankNodes));
        }
        return records;
    }

    /// <summary>Ops the game never reads, which the records leave out.</summary>
    private static readonly HashSet<Borg13Opcode> UnusedOps =
        [Borg13Opcode.Unknown1, Borg13Opcode.Unknown2, Borg13Opcode.Backdrop, Borg13Opcode.Unknown55, Borg13Opcode.Unknown59];

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> list) => list.Count == 0 ? null : list;

    private static DialogueOp BuildOp(Borg13Op op)
    {
        var command = (DialogueCommand)op.Opcode;
        switch (op.Opcode)
        {
            case Borg13Opcode.SetSpeaker or Borg13Opcode.CameraCutToActor or Borg13Opcode.CameraOnActor
                or Borg13Opcode.CameraAngleToActor or Borg13Opcode.CameraPanToActor or Borg13Opcode.CameraSineToActor
                or Borg13Opcode.AddEnemy or Borg13Opcode.CheckMember or Borg13Opcode.AddMember or Borg13Opcode.RemoveMember
                or Borg13Opcode.CheckItem or Borg13Opcode.AddItem or Borg13Opcode.TakeItem or Borg13Opcode.ShowItem:
                // An ID with no record, such as the tools' 0xCCCC, keeps its number.
                return DbRecordIds.For(new ItemId(op.Value)) is { } target
                    ? new DialogueOp(command, Target: target)
                    : new DialogueOp(command, op.Value);
            case Borg13Opcode.CheckFlag or Borg13Opcode.SetFlag or Borg13Opcode.ClearFlag:
                return new DialogueOp(command, Target: DbRecordIds.Flag(op.Value));
            case Borg13Opcode.RandomLine:
                return new DialogueOp(command, Target: RecordId.Hex(RecordTypes.Dialogue, op.Value));
            // The game ignores the operand of these.
            case Borg13Opcode.CameraTrackOn or Borg13Opcode.EndDialogue or Borg13Opcode.FadeOut or Borg13Opcode.Shop
                or Borg13Opcode.Battle or Borg13Opcode.Train or Borg13Opcode.CampHeal or Borg13Opcode.HideItem
                or Borg13Opcode.Unknown53 or Borg13Opcode.Unimplemented or Borg13Opcode.WaitForCamera
                or Borg13Opcode.ResetVisited or Borg13Opcode.CheckPartySize or Borg13Opcode.CheckGold:
                return new DialogueOp(command);
            default:
                return new DialogueOp(command, op.Value);
        }
    }
}
