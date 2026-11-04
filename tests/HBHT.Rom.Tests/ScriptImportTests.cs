using System.Collections.Concurrent;
using System.Text.Json;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Dialogue;
using HBHT.Rom.Engine;
using HBHT.Rom.Import;
using HBHT.Rom.Quests;
using HBHT.Rom.Text;

namespace HBHT.Rom.Tests;

public class ScriptImportTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Where(v => !v.ResearchOnly).Select(v => v.Id)];

    private sealed record Built(BorgArchive Archive, EngineTables Tables, List<ScriptRecord> Records, ScriptImporter.Report Report);

    private static readonly ConcurrentDictionary<RomVersionId, Lazy<Built>> Cache = new();

    private static Built Build(RomVersionId id) =>
        Cache.GetOrAdd(id, id => new(() =>
        {
            var rom = TestRoms.Get(id);
            var archive = BorgArchive.Open(rom);
            var tables = EngineTables.Read(rom);
            var db = GameDb.Read(rom);
            var names = db.Entities.Select(e => (Id: DbRecordIds.For(e.Id), e.Name))
                .Concat(db.DialogEntities.Select(e => (Id: DbRecordIds.For(e.Id), e.Name)))
                .Where(e => e.Id is not null).DistinctBy(e => e.Id).ToDictionary(e => e.Id!.Value, e => e.Name);
            var report = new ScriptImporter.Report();
            return new Built(archive, tables, ScriptImporter.BuildScripts(archive, tables, names.GetValueOrDefault, report), report);
        })).Value;

    private static ScriptRecord Script(int archiveIndex) =>
        Build(RomVersionId.Na10).Records.Single(r => r.Id == RecordId.Hex(RecordTypes.Script, archiveIndex));

    /// <summary>The script with this number in the original's tools, as the debug build prints it.</summary>
    private static ScriptRecord ByNumber(int number) => Build(RomVersionId.Na10).Records.Single(r => r.Number == number);

    private static IEnumerable<ScriptOp> Ops(IEnumerable<ScriptRecord> records) => records.SelectMany(r => r.Nodes).SelectMany(n => n.Ops);

    /// <summary>The record IDs an op names, whatever its parameters are called.</summary>
    private static IEnumerable<RecordId> Named(ScriptOp op) => op.GetType().GetProperties()
        .Select(p => p.GetValue(op)).OfType<RecordId>();

    private static readonly RecordId Alaron = RecordId.Hex(RecordTypes.Entity, 0x023F);

    [Theory]
    [MemberData(nameof(Versions))]
    public void EveryScriptBuildsWithOpsThatNameRecordsOfTheRightTypes(RomVersionId id)
    {
        var (archive, _, records, report) = Build(id);
        var borgs = archive.Listings.Where(l => l.Type == BorgType.Dialogue).Select(l => l.Index).ToList();

        Assert.Equal(892, records.Count);
        Assert.Equal(borgs.Select(i => RecordId.Hex(RecordTypes.Script, i)), records.Select(r => r.Id));
        Assert.All(records, r => Assert.Equal(r.Nodes.Select((_, i) => i), r.Nodes.Select(n => n.Index)));

        // Every op is a type the core knows, in a place it can run, with a kind of record where it names one.
        var ops = Ops(records).ToList();
        Assert.All(ops, op => Assert.NotNull(op.Name));
        foreach (var node in records.SelectMany(r => r.Nodes))
        {
            Assert.All(node.Setup ?? [], op => Assert.True((op.Use & OpUse.Setup) != 0, op.ToString()));
            if (node.Condition is { } condition)
                Assert.IsAssignableFrom<ScriptOp.Check>(condition);
            if (node.Effect is { } effect)
                Assert.True((effect.Use & OpUse.Effect) != 0, effect.ToString());
        }

        foreach (var op in ops)
        {
            switch (op)
            {
                case ScriptOp.CheckFlag or ScriptOp.FlagSet or ScriptOp.FlagClear:
                    Assert.All(Named(op), r => Assert.Equal(RecordTypes.Flag, r.Type));
                    break;
                case ScriptOp.ScriptRandomLine random:
                    Assert.Equal(RecordTypes.Script, random.From.Type);
                    Assert.Equal(BorgType.Dialogue, archive.Listings[Convert.ToInt32(random.From.Key, 16)].Type);
                    break;
                case ScriptOp.ActorHold hold:
                    Assert.Equal(RecordTypes.Entity, hold.Actor.Type);
                    Assert.Contains(hold.Item.Type, new[] { RecordTypes.Item, RecordTypes.Spell });
                    break;
                case ScriptOp.CheckItem or ScriptOp.PartyAddItem or ScriptOp.PartyTakeItem:
                    Assert.All(Named(op), r => Assert.Equal(RecordTypes.Item, r.Type));
                    break;
                case ScriptOp.CheckSkill skill:
                    Assert.Equal(RecordTypes.Skill, skill.Skill.Type);
                    break;
                case ScriptOp.CheckBestStat or ScriptOp.CheckWorstStat or ScriptOp.CheckStat:
                    Assert.Contains(Named(op), r => r.Type == RecordTypes.Stat);
                    break;
                case ScriptOp.ActorAnimate animate:
                    Assert.Equal(RecordTypes.Request, animate.Request.Type);
                    Assert.Equal(RecordTypes.Entity, animate.Actor.Type);
                    break;
                case ScriptOp.BattleArena arena:
                    Assert.Equal(RecordTypes.Chunk, arena.Arena.Type);
                    break;
                default:
                    // Whoever acts, enemy or member an op names is an entity.
                    Assert.All(Named(op), r => Assert.Equal(RecordTypes.Entity, r.Type));
                    break;
            }
        }
        Assert.All(records.SelectMany(r => r.Nodes).Select(n => n.Speaker).OfType<RecordId>(), s => Assert.Equal(RecordTypes.Entity, s.Type));

        // Each source op is in the records once, or accounted for: a SetSpeaker is its node's speaker, and the rest go unwritten
        // as the game never reads them, never runs them, or has no record for what they name.
        var source = borgs.SelectMany(i => Borg13Script.Parse(archive.Read(i)).Nodes).SelectMany(n => n.Ops).ToList();
        var unused = new[] { Borg13Opcode.Unknown1, Borg13Opcode.Unknown2, Borg13Opcode.Backdrop, Borg13Opcode.Unknown55, Borg13Opcode.Unknown59 };
        var kept = source.Where(op => !unused.Contains(op.Opcode)).ToList();
        var speakers = kept.Count(op => op.Opcode == Borg13Opcode.SetSpeaker);
        var neverRun = kept.Count(op => op.Opcode is Borg13Opcode.Unimplemented or Borg13Opcode.Unknown53);
        var conditions = kept.Count(op => op.Opcode.IsCondition());
        var nodesWithCondition = records.SelectMany(r => r.Nodes).Count(n => n.Condition is not null) + report.Dropped.Count(d => d.Contains("Check", StringComparison.Ordinal));
        Assert.Equal(conditions, nodesWithCondition);
        Assert.Equal(kept.Count - speakers - neverRun, ops.Count + report.DeadEffects.Count + report.Dropped.Count);
    }

    [Fact]
    public void ScriptsCarryTheirHeaderTablesAndPresentation()
    {
        var (_, tables, records, _) = Build(RomVersionId.Na10);

        // Line 24 of script 1040 has no link: a known data bug that the script runner must handle.
        var node = Script(1040).Nodes[24];
        Assert.Equal((ScriptNodeKind.Line, null), (node.Kind, node.Links));
        Assert.Equal(RecordId.Parse("aidyn:flag/5"), Script(866).DoneFlag);

        // The Goblin Ambush starts a battle whose win sets a flag.
        Assert.Equal(RecordId.Decimal(RecordTypes.Flag, tables.ScriptBattleFlags[869]), Script(869).BattleFlag);
        Assert.Equal(tables.ScriptBattleFlags.Count, records.Count(r => r.BattleFlag is not null));
        Assert.Equal(tables.CampfireScenes.OrderBy(i => i), records.Where(r => r.Rest).Select(r => int.Parse(r.Id.Key[2..], System.Globalization.NumberStyles.HexNumber, null)));
        Assert.Equal(9, records.Count(r => r.Rest));

        // For tools, a script's number and its first speaker's name.
        Assert.Equal(RecordId.Hex(RecordTypes.Script, 0x0362), ByNumber(1004).Id);
        Assert.Equal("1004 Alaron", ByNumber(1004).Label);

        // Only cut-scenes, backdrops and the help menu have a done flag, and only backdrops and help a picture.
        Assert.All(records, r =>
        {
            Assert.Equal(r.Mode is ScriptMode.Cutscene or ScriptMode.Backdrop or ScriptMode.Help, r.DoneFlag is not null);
            Assert.Equal(r.Mode is ScriptMode.Backdrop or ScriptMode.Help, r.Backdrop is not null);
        });
        Assert.Equal([596, 211, 78, 6, 1], Enum.GetValues<ScriptMode>().Select(m => records.Count(r => r.Mode == m)));
        Assert.All(records.Where(r => r.Actors is not null), r => Assert.All(r.Actors!, a => Assert.Equal(RecordTypes.Entity, a.Type)));
    }

    [Fact]
    public void AnUnsetActorOrSpeakerKeepsNoRecord()
    {
        // The tools' fill value 0xCCCC, which script 1294 has as an actor and a speaker.
        var (archive, _, records, _) = Build(RomVersionId.Na10);
        var source = Borg13Script.Parse(archive.Read(1294));
        Assert.Contains(new ItemId(0xCCCC), source.Actors);
        var script = Script(1294);
        Assert.Equal(source.Actors.Count(a => a.Value != 0xCCCC), script.Actors?.Count ?? 0);
        var unset = source.Nodes.Where(n => n.Ops.Contains(new Borg13Op(Borg13Opcode.SetSpeaker, 0xCCCC))).Select(n => n.Index).ToList();
        Assert.NotEmpty(unset);
        Assert.All(unset, n => Assert.Null(script.Nodes[n].Speaker));

        // Everywhere else, every actor names a record.
        foreach (var record in records.Where(r => r.Id != script.Id))
            Assert.Equal(Borg13Script.Parse(archive.Read(Convert.ToInt32(record.Id.Key, 16))).Actors.Count, record.Actors?.Count ?? 0);
    }

    /// <summary>What the import left out or chose where the original was unclear (docs/systems/scripts.md has each).</summary>
    [Fact]
    public void TheReportHoldsWhatTheImportLeftOutOrChose()
    {
        var (_, _, records, report) = Build(RomVersionId.Na10);

        // Two lines the ways to them disagree on, both chosen by hand: Alaron's.
        Assert.Equal([(1446, 6, (RecordId?)Alaron, true), (1470, 37, Alaron, true)], report.Disagreements.OrderBy(d => d.Script));
        // Second effects 1.0 never runs.
        Assert.Equal([(1076, 33), (1901, 88), (1906, 78), (3626, 78)], report.DeadEffects.OrderBy(d => d));
        Assert.Equal(212, report.Unknown53);
        // The one op that names no record: script 1796's node 35 checks 0x04AE, an ID with no record, so it has no condition.
        var dropped = Assert.Single(report.Dropped);
        Assert.Contains("1796 node 35", dropped, StringComparison.Ordinal);
        Assert.Contains("CheckItem(1198)", dropped, StringComparison.Ordinal);
        Assert.Null(ByNumber(1796).Nodes[35].Condition);
    }

    [Fact]
    public void NodesCarryTheirSpeakersAndAMenusChoicesTakeTheChoiceBefore()
    {
        var (_, _, records, _) = Build(RomVersionId.Na10);
        var nodes = records.SelectMany(r => r.Nodes).ToList();

        Assert.Equal(26_684, nodes.Count);
        Assert.Equal(17_568, nodes.Count(n => n.Text.Length > 0));
        Assert.Equal(3_043, nodes.Count(n => n.Text.Length > 0 && n.Speaker is null));
        // A choice with no speaker of its own takes the one before it, as 1.0 shows them in turn.
        Assert.Equal(Alaron, ByNumber(1091).Nodes[65].Speaker);
        Assert.Equal(Alaron, ByNumber(10922).Nodes[10].Speaker);
        Assert.Contains(65, ByNumber(1091).Nodes[18].Links!);
        Assert.Contains(10, ByNumber(10922).Nodes[4].Links!);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void TextIsCleaned(RomVersionId id)
    {
        var texts = Build(id).Records.SelectMany(r => r.Nodes).Select(n => n.Text).ToList();

        Assert.All(texts, t =>
        {
            Assert.DoesNotContain('\\', t);
            Assert.DoesNotContain('#', t);
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(t, "^ *[vV][0-9]{4}"), t);
        });
        Assert.Equal(402, texts.Count(t => t.Contains("{player}", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheButtonPicturesSurvive()
    {
        var text = string.Concat(Script(1355).Nodes.Select(n => n.Text));
        Assert.Contains(text, c => c is >= '\u007F' and <= '\u0087');
        Assert.All(text.Where(char.IsControl), c => Assert.True(c is '\n' or (>= '\u007F' and <= '\u0087'), $"U+{(int)c:X4}"));
        // A menu choice keeps its leading spaces.
        Assert.Equal("  Lonely Mountain", Script(883).Nodes[2].Text);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void OnlyNodesACaseLinksToSelect(RomVersionId id)
    {
        var cases = 0;
        foreach (var record in Build(id).Records)
        {
            var linked = record.Nodes.Where(n => n.Kind == ScriptNodeKind.Case).SelectMany(n => n.Links ?? []).ToHashSet();
            cases += record.Nodes.Count(n => n.Kind == ScriptNodeKind.Case);
            foreach (var node in record.Nodes)
                Assert.Equal(linked.Contains(node.Index), node.Selects is not null);
        }
        Assert.Equal(118, cases);

        // A range without an upper bound stays open.
        var selects = Build(id).Records.SelectMany(r => r.Nodes).Select(n => n.Selects).OfType<ScriptRange>().ToList();
        Assert.Contains(selects, s => s.Max is null);
        Assert.All(selects, s => Assert.True(s.Max is null || s.Max >= s.Min));
        Assert.All(selects.Where(s => s.Max is null), s => Assert.True(s.Holds(60_000)));
    }

    /// <summary>
    /// A script that ended with a blank node, which 1.0 counts, gives a random line of it more weight for its first node, which
    /// the blank node shows.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void RandomLinesCountTheirTargetsBlankNodes(RomVersionId id)
    {
        var (archive, _, records, _) = Build(id);
        var blanks = records.ToDictionary(r => r.Id, r => Borg13Script.Parse(archive.Read(Convert.ToInt32(r.Id.Key, 16))).BlankNodes);
        Assert.Equal(153, blanks.Values.Count(b => b == 1));
        Assert.All(blanks.Values, b => Assert.InRange(b, 0, 1));
        Assert.Equal(26_837, records.Sum(r => r.Nodes.Count + blanks[r.Id]));

        var lines = Ops(records).OfType<ScriptOp.ScriptRandomLine>().ToList();
        Assert.All(lines, line => Assert.Equal(blanks[line.From] == 1 ? 2 : (int?)null, line.FirstLineWeight));
        Assert.Equal(13, lines.Count(l => l.FirstLineWeight == 2));
        Assert.Equal(12, lines.Where(l => l.FirstLineWeight == 2).Select(l => l.From).Distinct().Count());
    }

    [Fact]
    public void TheRecordsRoundTripThroughJson()
    {
        var records = Build(RomVersionId.Na10).Records;
        var json = JsonSerializer.SerializeToUtf8Bytes(records, PluginJson.Options);
        var back = JsonSerializer.Deserialize<List<ScriptRecord>>(json, PluginJson.Options)!;

        Assert.Equal(records.Count, back.Count);
        foreach (var (a, b) in records.Zip(back))
        {
            Assert.Equal((a.Id, a.Number, a.Label, a.Mode, a.DoneFlag, a.Backdrop, a.BattleFlag, a.Rest),
                (b.Id, b.Number, b.Label, b.Mode, b.DoneFlag, b.Backdrop, b.BattleFlag, b.Rest));
            Assert.Equal(a.Actors ?? [], b.Actors ?? []);
            Assert.Equal(a.Nodes.Count, b.Nodes.Count);
            foreach (var (x, y) in a.Nodes.Zip(b.Nodes))
            {
                Assert.Equal((x.Index, x.Kind, x.Text, x.Speaker, x.Condition, x.Effect, x.Selects),
                    (y.Index, y.Kind, y.Text, y.Speaker, y.Condition, y.Effect, y.Selects));
                Assert.Equal(x.Links ?? [], y.Links ?? []);
                Assert.Equal(x.Setup ?? [], y.Setup ?? []);
            }
        }
    }

    [Fact]
    public void DbIdsNameRecordsByTheirWholeId()
    {
        Assert.Equal(RecordId.Parse("aidyn:entity/0x023F"), DbRecordIds.For(new ItemId(0x023F)));
        // A dialogue entity is an entity (ADR 0032), and the four twins are the entity they are twins of.
        Assert.Equal(RecordId.Parse("aidyn:entity/0x1403"), DbRecordIds.For(ItemId.Of(DbType.DialogEntity, 3)));
        Assert.Equal(RecordId.Parse("aidyn:entity/0x02DD"), DbRecordIds.For(ItemId.Of(DbType.DialogEntity, 4)));
        Assert.Equal(RecordTypes.Item, DbRecordIds.For(ItemId.Of(DbType.Shield, 1))!.Value.Type);
        Assert.Equal(RecordTypes.Item, DbRecordIds.For(ItemId.Of(DbType.KeyItem, 1))!.Value.Type);
        Assert.Equal(RecordTypes.Item, DbRecordIds.For(ItemId.Of(DbType.Weapon, 9))!.Value.Type);
        Assert.Equal(RecordTypes.Spell, DbRecordIds.For(ItemId.Of(DbType.Spell, 2))!.Value.Type);
        Assert.Equal(RecordId.Parse("aidyn:loot/0x0801"), DbRecordIds.For(new ItemId(0x0801)));
        Assert.All(new ushort[] { 0, 0x0401, 0xCCCC, 0x1501 }, v => Assert.Null(DbRecordIds.For(new ItemId(v))));
    }

    [Fact]
    public void TextCleaningFollowsTheRules()
    {
        var name = new string('#', 20);
        Assert.Equal("Hello, {player}!", DialogueText.Clean($"Hello, {name}!"));
        Assert.Equal("Hi there", DialogueText.Clean("  V0173Hi there"));
        Assert.Equal("  Lonely Mountain", DialogueText.Clean("  Lonely Mountain"));
        Assert.Equal("  and on", DialogueText.Clean("v1234  and on"));
        Assert.Equal("V12 stays", DialogueText.Clean("V12 stays"));
        Assert.Equal("one\ntwo \u0080 three", DialogueText.Clean("one\\ntwo \\x80 three"));
        Assert.Throws<InvalidDataException>(() => DialogueText.Clean("a # b"));
        Assert.Throws<InvalidDataException>(() => DialogueText.Clean(new string('#', 21)));
        Assert.Throws<InvalidDataException>(() => DialogueText.Clean("tab\\t"));
        Assert.Throws<InvalidDataException>(() => DialogueText.Clean("end\\"));
        Assert.Throws<InvalidDataException>(() => DialogueText.Clean("bad\\x8"));
    }

    [Fact]
    public void EveryDbRecordAScriptNamesHasAName()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var db = GameDb.Read(rom);
        var models = EntityModels.Read(rom);
        // Entities (the dialogue entities among them), spells and items have records of their own, which name them too.
        var names = new Dictionary<RecordId, string>();
        foreach (var entity in CharacterImporter.BuildEntities(db, models))
            names.Add(entity.Id, entity.Name);
        foreach (var spell in CharacterImporter.BuildSpells(db))
            names.Add(spell.Id, spell.Name);
        foreach (var item in ItemImporter.BuildItems(db, ItemTables.Read(rom), EngineTables.Read(rom)))
            names.Add(item.Id, item.Name);
        Assert.Equal("Alaron", names[RecordId.Hex(RecordTypes.Entity, 0x023F)]);
        Assert.Equal("Trahern", names[RecordId.Hex(RecordTypes.Entity, 0x0239)]);
        // Potions have no DB table: they're named by their own string table, by index.
        Assert.Equal("Healing Potion", names[RecordId.Hex(RecordTypes.Item, 0x1004)]);

        var named = Build(RomVersionId.Na10).Records
            .SelectMany(r => (r.Actors ?? []).Concat(r.Nodes.Select(n => n.Speaker).OfType<RecordId>()).Concat(Ops([r]).SelectMany(Named)))
            .Where(id => id.Type is RecordTypes.Entity or RecordTypes.Item or RecordTypes.Spell)
            .Distinct().ToList();
        Assert.NotEmpty(named);
        // Scene 1076 shows two "spells", which have no model, so nothing shows (get_item_borg5).
        Assert.Equal(["aidyn:spell/0x03EB", "aidyn:spell/0x03ED"],
            named.Where(id => !names.ContainsKey(id)).Select(id => id.ToString()).Order());
    }
}
