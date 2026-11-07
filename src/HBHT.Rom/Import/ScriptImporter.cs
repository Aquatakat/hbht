using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Dialogue;
using HBHT.Rom.Quests;
using HBHT.Rom.Text;

namespace HBHT.Rom.Import;

/// <summary>
/// The scripts (Borg13): conversations, cut-scenes, backdrops and signs, as typed ops with a speaker on each node (ADR 0034).
/// docs/systems/scripts.md has the mapping from each of the original's commands to its op.
/// </summary>
internal static class ScriptImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Scripts");
        var names = ctx.Db.Entities.Select(e => (Id: DbRecordIds.For(e.Id), e.Name))
            .Concat(ctx.Db.DialogEntities.Select(e => (Id: DbRecordIds.For(e.Id), e.Name)))
            .Where(e => e.Id is not null).DistinctBy(e => e.Id).ToDictionary(e => e.Id!.Value, e => e.Name);
        var scripts = BuildScripts(ctx.Archive, ctx.Engine, names.GetValueOrDefault, new Report(), ctx.ArenaChunks);
        foreach (var script in scripts)
            ctx.WriteRecords($"scripts/{script.Id.Key}.json", script);
        ctx.Scripts = scripts;
    }

    /// <summary>
    /// The hand choice of speaker where the ways to a line disagree, by the script's number and the node: 1470's "Let's see what
    /// the old man has." is framed on Alaron (<c>CameraOnActor</c> him). docs/systems/scripts.md has it.
    /// </summary>
    internal static readonly IReadOnlyDictionary<(int Script, int Node), ushort> HandSpeakers = new Dictionary<(int, int), ushort>
    {
        [(1470, 37)] = 0x023F,
    };

    /// <summary>
    /// Lines reached with different speakers in force, given a copy for one way in so each shows the speaker 1.0 shows there, the
    /// user's choice: by the script's number, the node, and the node whose link goes to the copy instead, which is added after the
    /// script's last node. 1446's "Farewell." (Cadme's talk, node 6) is Alaron's after his "I have no time to tell it now." (node 5)
    /// and Cadme's after her closing lines.
    /// </summary>
    internal static readonly IReadOnlyList<(int Script, int Node, int From)> SplitNodes = [(1446, 6, 5)];

    /// <summary>The script with a copy of each node <see cref="SplitNodes"/> names, and the way in it names pointed at the copy.</summary>
    internal static Borg13Script Split(Borg13Script script)
    {
        foreach (var (_, node, from) in SplitNodes.Where(s => s.Script == script.Id))
        {
            var nodes = script.Nodes.ToList();
            nodes.Add(nodes[node] with { Index = nodes.Count });
            nodes[from] = nodes[from] with { Links = nodes[from].Links.Select(l => l == node ? nodes.Count - 1 : l).ToList() };
            script = script with { Nodes = nodes };
        }
        return script;
    }

    /// <summary>What the import left out or chose, for the tests and the docs.</summary>
    internal sealed class Report
    {
        /// <summary>Second effects, which 1.0 never runs: (script number, node).</summary>
        public List<(int Script, int Node)> DeadEffects { get; } = [];
        /// <summary>Nodes whose effect was command 53, which does nothing.</summary>
        public int Unknown53 { get; set; }
        /// <summary>Lines whose speaker the ways to them disagree on, and who was chosen.</summary>
        public List<(int Script, int Node, RecordId? Speaker, bool ByHand)> Disagreements { get; } = [];
        /// <summary>Ops dropped because they name no record, or their actor is no one.</summary>
        public List<string> Dropped { get; } = [];
    }

    internal static List<ScriptRecord> BuildScripts(BorgArchive archive, EngineTables tables, Func<RecordId, string?>? names = null,
        IReadOnlyList<RecordId>? arenas = null) =>
        BuildScripts(archive, tables, names, new Report(), arenas);

    /// <summary>
    /// Every Borg13 as a <see cref="ScriptRecord"/>, in archive order. Text is cleaned (<see cref="DialogueText"/>), the ops the
    /// game never reads are dropped, each node gets its speaker (<see cref="Speakers"/>), and each op its typed form with the
    /// speaker in force as its actor. A node a Case links to gets its range. A battle's arena, which the original names by its number, is
    /// its chunk (<paramref name="arenas"/>, <see cref="Arenas.Chunks"/>), arena 0's for a number above 27 as the original takes it; without
    /// them, the Battlefields' A01. A script with a battle flag has the rules 1.0 keys by it (<see cref="CombatImporter.BattleRules"/>).
    /// </summary>
    internal static List<ScriptRecord> BuildScripts(BorgArchive archive, EngineTables tables, Func<RecordId, string?>? names, Report report,
        IReadOnlyList<RecordId>? arenas = null)
    {
        RecordId Arena(int number) => arenas is { Count: > 0 } a ? a[(number & 0xFF) < a.Count ? number & 0xFF : 0] : ChunkRecord.IdOf(16, 1, 1);
        var rules = CombatImporter.BattleRules(tables);
        var parsed = archive.Listings.Where(l => l.Type == BorgType.Dialogue)
            .Select(l => (l.Index, Script: Split(Borg13Script.Parse(archive.Read(l.Index))))).ToList();
        var blanks = parsed.ToDictionary(p => p.Index, p => p.Script.BlankNodes);
        var records = new List<ScriptRecord>();
        foreach (var (index, script) in parsed)
        {
            var mode = script.StartMode;
            var hasDoneFlag = mode is ScriptMode.Cutscene or ScriptMode.Backdrop or ScriptMode.Help;
            var ops = script.Nodes.Select(n => n.Ops.Where(op => !UnusedOps.Contains(op.Opcode)).ToList()).ToList();
            var speakers = Speakers(script, ops, report);

            var cased = script.Nodes.Where(n => n.Kind == ScriptNodeKind.Case).SelectMany(n => n.Links).ToHashSet();
            var nodes = new List<ScriptNode>();
            foreach (var node in script.Nodes)
            {
                var nodeOps = ops[node.Index];
                var speaker = speakers[node.Index];
                var where = $"{script.Id} node {node.Index}";
                var setup = nodeOps.Where(op => !op.Opcode.IsCondition() && !op.Opcode.IsEffect() && op.Opcode != Borg13Opcode.SetSpeaker)
                    .OrderBy(op => SetsUp(op.Opcode) ? 0 : 1)
                    .Select(op => Setup(op, speaker, where, report, Arena)).OfType<ScriptOp>().ToList();
                var condition = nodeOps.Cast<Borg13Op?>().FirstOrDefault(op => op!.Value.Opcode.IsCondition()) is { } c
                    ? Condition(c, speaker, where, report) : null;
                var effects = nodeOps.Where(op => op.Opcode.IsEffect()).ToList();
                foreach (var _ in effects.Skip(1))
                    report.DeadEffects.Add((script.Id, node.Index));
                ScriptOp? effect = null;
                if (effects.Count > 0)
                {
                    if (effects[0].Opcode == Borg13Opcode.Unknown53)
                        report.Unknown53++;
                    effect = Effect(effects[0], speaker, where, report, blanks);
                }
                var text = DialogueText.Clean(node.Text);
                // Only a node that shows a line carries its speaker.
                var shows = text.Length > 0 || effect is ScriptOp.ScriptRandomLine;
                nodes.Add(new ScriptNode(
                    node.Index,
                    node.Kind,
                    text,
                    shows ? speaker : null,
                    NullIfEmpty(setup),
                    condition,
                    effect,
                    NullIfEmpty(node.Links),
                    cased.Contains(node.Index)
                        ? new ScriptRange(node.Min, node.Max == Borg13Node.NoUpperBound ? null : node.Max)
                        : null));
            }

            var first = nodes.Select(n => n.Speaker).OfType<RecordId>().Select(s => names?.Invoke(s)).FirstOrDefault(n => n is not null);
            records.Add(new ScriptRecord(
                RecordId.Hex(RecordTypes.Script, index),
                script.Id,
                mode,
                nodes,
                $"{script.Id} {first ?? mode.ToString().ToLowerInvariant()}",
                NullIfEmpty(script.Actors.Select(DbRecordIds.For).OfType<RecordId>().ToList()),
                hasDoneFlag ? DbRecordIds.Flag(script.DoneFlag) : null,
                mode is ScriptMode.Backdrop or ScriptMode.Help ? script.Backdrop : null,
                tables.ScriptBattleFlags.TryGetValue(index, out var battle) ? DbRecordIds.Flag(battle) : null,
                tables.CampfireScenes.Contains(index),
                tables.ScriptBattleFlags.TryGetValue(index, out var flag) && rules.TryGetValue(flag, out var own) ? own : null));
        }
        return records;
    }

    /// <summary>Ops the game never reads, which the records leave out.</summary>
    private static readonly HashSet<Borg13Opcode> UnusedOps =
        [Borg13Opcode.Unknown1, Borg13Opcode.Unknown2, Borg13Opcode.Backdrop, Borg13Opcode.Unknown55, Borg13Opcode.Unknown59];

    /// <summary>The setup ops 1.0 runs in a pass before the cameras and actors (<c>Dialoug_commands</c>), which the importer writes first.</summary>
    private static bool SetsUp(Borg13Opcode op) => op is Borg13Opcode.CameraTime or Borg13Opcode.AddEnemy or Borg13Opcode.SetCollideByte
        or Borg13Opcode.SetBattlefield or Borg13Opcode.SetAniByte or Borg13Opcode.ResetVisited;

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> list) => list.Count == 0 ? null : list;

    /// <summary>
    /// Each node's speaker, as 1.0's walk leaves it when the node is reached (<c>Dialoug_commands</c> sets it from the node's
    /// SetSpeaker before anything else): the node's own SetSpeaker, else the speaker of the nodes that lead to it. A menu sets
    /// each choice up in turn as it shows them, so a choice without one takes the choice before it, and the first the menu.
    /// Where the ways to a node disagree, a hand choice (<see cref="HandSpeakers"/>), else the speaker of the lowest-numbered
    /// node that leads to it with one. No speaker on any way to a node is none, as at the start of a script.
    /// </summary>
    internal static RecordId?[] Speakers(Borg13Script script, IReadOnlyList<IReadOnlyList<Borg13Op>> ops, Report report)
    {
        var count = script.Nodes.Count;
        // A SetSpeaker that names no record (the tools' 0xCCCC) sets the speaker to no one.
        var own = new (bool Set, RecordId? Speaker)[count];
        for (var i = 0; i < count; i++)
        {
            if (ops[i].Cast<Borg13Op?>().FirstOrDefault(op => op!.Value.Opcode == Borg13Opcode.SetSpeaker) is { } set)
                own[i] = (true, DbRecordIds.For(new ItemId(set.Value)));
        }
        var from = new List<int>[count];
        for (var i = 0; i < count; i++)
            from[i] = [];
        foreach (var node in script.Nodes)
        {
            var links = node.Links.Where(l => l < count).ToList();
            for (var i = 0; i < links.Count; i++)
                from[links[i]].Add(node.Kind == ScriptNodeKind.Menu && i > 0 ? links[i - 1] : node.Index);
        }

        // Every speaker that can be in force at each node, with null for no one.
        var possible = new HashSet<RecordId?>[count];
        for (var i = 0; i < count; i++)
            possible[i] = own[i].Set ? [own[i].Speaker] : [];
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = 0; i < count; i++)
            {
                if (own[i].Set)
                    continue;
                foreach (var f in from[i])
                {
                    foreach (var speaker in possible[f])
                        changed |= possible[i].Add(speaker);
                }
            }
        }

        var resolved = new RecordId?[count];
        var done = new bool[count];
        RecordId? Resolve(int i, HashSet<int> seen)
        {
            if (done[i])
                return resolved[i];
            RecordId? speaker;
            if (own[i].Set)
                speaker = own[i].Speaker;
            else if (possible[i].Count == 0)
                speaker = null;
            else if (possible[i].Count == 1)
                speaker = possible[i].First();
            else if (HandSpeakers.TryGetValue((script.Id, i), out var hand))
                speaker = DbRecordIds.For(new ItemId(hand));
            else if (!seen.Add(i))
                return null;
            else
                speaker = from[i].Where(f => possible[f].Count > 0).Order().Select(f => (int?)f).FirstOrDefault() is { } f ? Resolve(f, seen) : null;
            done[i] = true;
            return resolved[i] = speaker;
        }
        for (var i = 0; i < count; i++)
        {
            Resolve(i, []);
            if (!own[i].Set && possible[i].Count > 1 && DialogueText.Clean(script.Nodes[i].Text).Length > 0)
                report.Disagreements.Add((script.Id, i, resolved[i], HandSpeakers.ContainsKey((script.Id, i))));
        }
        return resolved;
    }

    private static RecordId? Entity(Borg13Op op) => DbRecordIds.For(new ItemId(op.Value));

    /// <summary>An op that needs a record or an actor, or nothing and a line in the report if it has none.</summary>
    private static ScriptOp? Need<T>(T? value, Func<T, ScriptOp> op, Borg13Op raw, string where, Report report) where T : struct =>
        value is { } v ? op(v) : Drop(raw, where, report);

    private static ScriptOp? Drop(Borg13Op raw, string where, Report report)
    {
        report.Dropped.Add($"{where}: {raw} names no record, or its actor is no one.");
        return null;
    }

    /// <summary>A setup op, from 1.0's command (docs/systems/scripts.md has the table).</summary>
    private static ScriptOp? Setup(Borg13Op op, RecordId? speaker, string where, Report report, Func<int, RecordId> arena) => op.Opcode switch
    {
        Borg13Opcode.CameraCutTo => new ScriptOp.CameraCutTo { Mark = op.Value },
        Borg13Opcode.CameraCutToActor => Need(Entity(op), a => new ScriptOp.CameraCutTo { Actor = a }, op, where, report),
        Borg13Opcode.CameraOn => new ScriptOp.CameraLookAt { Mark = op.Value },
        Borg13Opcode.CameraOnActor => Need(Entity(op), a => new ScriptOp.CameraLookAt { Actor = a }, op, where, report),
        // 1.0's angle-to a point pans at a steady speed, and its pan-to a point eases; on an actor, the other way round.
        Borg13Opcode.CameraAngleTo => new ScriptOp.CameraTurnTo(Steady: true) { Mark = op.Value },
        Borg13Opcode.CameraAngleToActor => Need(Entity(op), a => new ScriptOp.CameraTurnTo { Actor = a }, op, where, report),
        Borg13Opcode.CameraPanTo => new ScriptOp.CameraTurnTo { Mark = op.Value },
        Borg13Opcode.CameraPanToActor => Need(Entity(op), a => new ScriptOp.CameraTurnTo(Steady: true) { Actor = a }, op, where, report),
        Borg13Opcode.CameraSineTo => new ScriptOp.CameraMoveTo { Mark = op.Value },
        Borg13Opcode.CameraSineToActor => Need(Entity(op), a => new ScriptOp.CameraMoveTo { Actor = a }, op, where, report),
        // It follows the speaker; its operand is ignored.
        Borg13Opcode.CameraTrackOn => Need(speaker, a => new ScriptOp.CameraFollow(a), op, where, report),
        Borg13Opcode.CameraTime => new ScriptOp.CameraTime(op.Value),
        Borg13Opcode.AddEnemy => Need(Entity(op), e => new ScriptOp.BattleAddEnemy(e), op, where, report),
        Borg13Opcode.FaceRefPoint => Need(speaker, a => new ScriptOp.ActorFace(a, op.Value), op, where, report),
        Borg13Opcode.CreateActor => Need(speaker, a => new ScriptOp.ActorSpawn(a, op.Value), op, where, report),
        Borg13Opcode.SetCollideByte => new ScriptOp.BattleCollisionByte(op.Value),
        Borg13Opcode.SetBattlefield => new ScriptOp.BattleArena(arena(op.Value)),
        Borg13Opcode.SetAniByte => new ScriptOp.BattleAnimationByte(op.Value),
        Borg13Opcode.ResetVisited => new ScriptOp.ScriptResetChoices(),
        _ => throw new InvalidDataException($"{where}: {op} isn't a setup op."),
    };

    /// <summary>A condition op. A check of an item with no record is always 0 in 1.0, as no condition is.</summary>
    private static ScriptOp? Condition(Borg13Op op, RecordId? speaker, string where, Report report) => op.Opcode switch
    {
        Borg13Opcode.CheckMember => Need(Entity(op), e => new ScriptOp.CheckMember(e), op, where, report),
        Borg13Opcode.CheckFlag => new ScriptOp.CheckFlag(DbRecordIds.Flag(op.Value)),
        Borg13Opcode.CheckSkill => new ScriptOp.CheckSkill(RecordId.Decimal(RecordTypes.Skill, op.Value)),
        Borg13Opcode.CheckItem => Need(Entity(op), i => new ScriptOp.CheckItem(i), op, where, report),
        Borg13Opcode.CheckBestStat => new ScriptOp.CheckBestStat(RecordId.Decimal(RecordTypes.Stat, op.Value)),
        Borg13Opcode.CheckWorstStat => new ScriptOp.CheckWorstStat(RecordId.Decimal(RecordTypes.Stat, op.Value)),
        Borg13Opcode.CheckSpeakerStat => Need(speaker, m => new ScriptOp.CheckStat(m, RecordId.Decimal(RecordTypes.Stat, op.Value)), op, where, report),
        Borg13Opcode.CheckPartySize => new ScriptOp.CheckPartySize(),
        Borg13Opcode.CheckGold => new ScriptOp.CheckGold(),
        _ => throw new InvalidDataException($"{where}: {op} isn't a condition op."),
    };

    /// <summary>
    /// An effect op. Command 53 counts as the node's effect and does nothing, and 49 crashes the original; neither is written.
    /// A random line's target that ended with a blank node, which 1.0 counts and shows as the first, gives its first line that
    /// much more weight.
    /// </summary>
    private static ScriptOp? Effect(Borg13Op op, RecordId? speaker, string where, Report report, IReadOnlyDictionary<int, int> blanks) => op.Opcode switch
    {
        Borg13Opcode.SetFlag => new ScriptOp.FlagSet(DbRecordIds.Flag(op.Value)),
        Borg13Opcode.ClearFlag => new ScriptOp.FlagClear(DbRecordIds.Flag(op.Value)),
        Borg13Opcode.EndDialogue => new ScriptOp.ScriptEnd(),
        Borg13Opcode.FadeOut => new ScriptOp.ScreenDip(),
        Borg13Opcode.AddGold => new ScriptOp.PartyAddGold(op.Value),
        Borg13Opcode.TakeGold => new ScriptOp.PartyTakeGold(op.Value),
        Borg13Opcode.AddItem => Need(Entity(op), i => new ScriptOp.PartyAddItem(i), op, where, report),
        Borg13Opcode.TakeItem => Need(Entity(op), i => new ScriptOp.PartyTakeItem(i), op, where, report),
        Borg13Opcode.WalkTo => Need(speaker, a => new ScriptOp.ActorWalkTo(a, op.Value), op, where, report),
        Borg13Opcode.RunTo => Need(speaker, a => new ScriptOp.ActorRunTo(a, op.Value), op, where, report),
        Borg13Opcode.PlayAnimation => Need(speaker, a => new ScriptOp.ActorAnimate(a, RecordId.Decimal(RecordTypes.Request, op.Value)), op, where, report),
        Borg13Opcode.PlaySound => new ScriptOp.SoundPlay(op.Value),
        Borg13Opcode.Shop => Need(speaker, k => new ScriptOp.ShopOpen(k), op, where, report),
        Borg13Opcode.Battle => new ScriptOp.BattleStart(),
        Borg13Opcode.Train => Need(speaker, t => new ScriptOp.TrainingOpen(t), op, where, report),
        Borg13Opcode.AddMember => Need(Entity(op), e => new ScriptOp.PartyAddMember(e), op, where, report),
        Borg13Opcode.RemoveMember => Need(Entity(op), e => new ScriptOp.PartyRemoveMember(e), op, where, report),
        Borg13Opcode.RandomLine => new ScriptOp.ScriptRandomLine(RecordId.Hex(RecordTypes.Script, op.Value),
            blanks.GetValueOrDefault(op.Value) is > 0 and var blank ? 1 + blank : null),
        Borg13Opcode.ShowItem => speaker is { } holder && Entity(op) is { } item ? new ScriptOp.ActorHold(holder, item) : Drop(op, where, report),
        Borg13Opcode.HideItem => Need(speaker, a => new ScriptOp.ActorPutAway(a), op, where, report),
        Borg13Opcode.CampHeal => new ScriptOp.PartyCampHeal(),
        Borg13Opcode.AddExp => Need(speaker, m => new ScriptOp.PartyAddExp(m, op.Value), op, where, report),
        Borg13Opcode.Wait => new ScriptOp.ScriptWait(op.Value),
        Borg13Opcode.WaitForCamera => new ScriptOp.CameraWait(),
        Borg13Opcode.Unknown53 or Borg13Opcode.Unimplemented => null,
        _ => throw new InvalidDataException($"{where}: {op} isn't an effect op."),
    };
}
