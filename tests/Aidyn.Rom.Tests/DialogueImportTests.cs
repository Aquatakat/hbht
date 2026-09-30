using System.Collections.Concurrent;
using System.Text.Json;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Dialogue;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Import;
using Aidyn.Rom.Quests;

namespace Aidyn.Rom.Tests;

public class DialogueImportTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Where(v => !v.ResearchOnly).Select(v => v.Id)];

    private static readonly ConcurrentDictionary<RomVersionId, Lazy<(BorgArchive Archive, EngineTables Tables, List<DialogueRecord> Records)>> Built = new();

    private static (BorgArchive Archive, EngineTables Tables, List<DialogueRecord> Records) Build(RomVersionId id) =>
        Built.GetOrAdd(id, id => new(() =>
        {
            var rom = TestRoms.Get(id);
            var archive = BorgArchive.Open(rom);
            var tables = EngineTables.Read(rom);
            return (archive, tables, BaseImporter.BuildDialogues(archive, tables));
        })).Value;

    private static DialogueRecord Script(int archiveIndex) =>
        Build(RomVersionId.Na10).Records.Single(r => r.Id == RecordId.Hex(RecordTypes.Dialogue, archiveIndex));

    private static IEnumerable<DialogueOp> Ops(IEnumerable<DialogueRecord> records) =>
        records.SelectMany(r => r.Nodes).SelectMany(n => n.Ops ?? []);

    private static readonly HashSet<DialogueCommand> DbTargets =
    [
        DialogueCommand.SetSpeaker, DialogueCommand.CameraCutToActor, DialogueCommand.CameraOnActor,
        DialogueCommand.CameraAngleToActor, DialogueCommand.CameraPanToActor, DialogueCommand.CameraSineToActor,
        DialogueCommand.AddEnemy, DialogueCommand.CheckMember, DialogueCommand.AddMember, DialogueCommand.RemoveMember,
        DialogueCommand.CheckItem, DialogueCommand.AddItem, DialogueCommand.TakeItem, DialogueCommand.ShowItem,
    ];

    private static readonly HashSet<DialogueCommand> FlagTargets =
        [DialogueCommand.CheckFlag, DialogueCommand.SetFlag, DialogueCommand.ClearFlag];

    private static readonly HashSet<DialogueCommand> NoOperand =
    [
        DialogueCommand.CameraTrackOn, DialogueCommand.EndDialogue, DialogueCommand.FadeOut, DialogueCommand.Shop,
        DialogueCommand.Battle, DialogueCommand.Train, DialogueCommand.CampHeal, DialogueCommand.HideItem,
        DialogueCommand.Unknown53, DialogueCommand.Unimplemented, DialogueCommand.WaitForCamera, DialogueCommand.ResetVisited,
        DialogueCommand.CheckPartySize, DialogueCommand.CheckGold,
    ];

    [Theory]
    [MemberData(nameof(Versions))]
    public void EveryScriptBuildsWithOpsThatFollowTheOperandRules(RomVersionId id)
    {
        var (archive, _, records) = Build(id);
        var borgs = archive.Listings.Where(l => l.Type == BorgType.Dialogue).Select(l => l.Index).ToList();

        Assert.Equal(892, records.Count);
        Assert.Equal(borgs.Select(i => RecordId.Hex(RecordTypes.Dialogue, i)), records.Select(r => r.Id));
        Assert.All(records, r => Assert.Equal(r.Nodes.Select((_, i) => i), r.Nodes.Select(n => n.Index)));

        var dropped = new[] { 1, 2, 19, 55, 59 };
        Assert.All(Ops(records), op => Assert.DoesNotContain((int)op.Command, dropped));
        // The rest keep their order and count: every kept source op appears once.
        var kept = borgs.SelectMany(i => Borg13Script.Parse(archive.Read(i)).Nodes).SelectMany(n => n.Ops)
            .Where(op => !dropped.Contains((int)op.Opcode)).Select(op => (DialogueCommand)op.Opcode);
        Assert.Equal(kept, Ops(records).Select(op => op.Command));

        foreach (var op in Ops(records))
        {
            if (DbTargets.Contains(op.Command))
            {
                // An ID with no record, such as the tools' 0xCCCC, keeps its number.
                Assert.True(op.Target is not null ^ op.Value is not null, op.ToString());
                if (op.Target is { } target)
                {
                    Assert.Contains(target.Type, new[] { RecordTypes.Entity, RecordTypes.DialogEntity, RecordTypes.Item, RecordTypes.Weapon, RecordTypes.Armor, RecordTypes.Spell });
                }
                else
                {
                    Assert.Null(DbRecordIds.For(new ItemId((ushort)op.Value!.Value)));
                }
            }
            else if (FlagTargets.Contains(op.Command))
            {
                Assert.Null(op.Value);
                Assert.Equal(RecordTypes.Flag, op.Target!.Value.Type);
            }
            else if (op.Command == DialogueCommand.RandomLine)
            {
                Assert.Null(op.Value);
                var target = op.Target!.Value;
                Assert.Equal(RecordTypes.Dialogue, target.Type);
                Assert.Equal(BorgType.Dialogue, archive.Listings[Convert.ToInt32(target.Key, 16)].Type);
            }
            else if (NoOperand.Contains(op.Command))
            {
                Assert.Equal((null, null), (op.Value, op.Target));
            }
            else
            {
                Assert.NotNull(op.Value);
                Assert.Null(op.Target);
            }
        }
    }

    [Fact]
    public void ScriptsCarryTheirHeaderTablesAndPresentation()
    {
        var (_, tables, records) = Build(RomVersionId.Na10);

        // Line 24 of script 1040 has no link: a known data bug that the script runner must handle.
        var node = Script(1040).Nodes[24];
        Assert.Equal((DialogueNodeKind.Line, null), (node.Kind, node.Links));
        Assert.Equal(RecordId.Parse("aidyn:flag/5"), Script(866).DoneFlag);

        // The Goblin Ambush starts a battle whose win sets a flag.
        Assert.Equal(RecordId.Decimal(RecordTypes.Flag, tables.ScriptBattleFlags[869]), Script(869).BattleFlag);
        Assert.Equal(tables.ScriptBattleFlags.Count, records.Count(r => r.BattleFlag is not null));
        Assert.Equal(tables.CampfireScenes.OrderBy(i => i), records.Where(r => r.Rest).Select(r => int.Parse(r.Id.Key[2..], System.Globalization.NumberStyles.HexNumber, null)));

        // Only cut-scenes, backdrops and the help menu have a done flag, and only backdrops and help a picture.
        Assert.All(records, r =>
        {
            Assert.Equal(r.Mode is DialogueMode.Cutscene or DialogueMode.Backdrop or DialogueMode.Help, r.DoneFlag is not null);
            Assert.Equal(r.Mode is DialogueMode.Backdrop or DialogueMode.Help, r.Backdrop is not null);
        });
        Assert.Equal([596, 211, 78, 6, 1], Enum.GetValues<DialogueMode>().Select(m => records.Count(r => r.Mode == m)));
        Assert.All(records.Where(r => r.Actors is not null), r => Assert.All(r.Actors!, a => Assert.Contains(a.Type, new[] { RecordTypes.Entity, RecordTypes.DialogEntity })));
    }

    [Fact]
    public void AnUnsetActorOrSpeakerKeepsNoRecord()
    {
        // The tools' fill value 0xCCCC, which script 1294 has as an actor and a speaker.
        var (archive, _, records) = Build(RomVersionId.Na10);
        var source = Borg13Script.Parse(archive.Read(1294));
        Assert.Contains(new ItemId(0xCCCC), source.Actors);
        var script = Script(1294);
        Assert.Equal(source.Actors.Count(a => a.Value != 0xCCCC), script.Actors?.Count ?? 0);
        Assert.Contains(Ops([script]), op => op is { Command: DialogueCommand.SetSpeaker, Value: 0xCCCC, Target: null });

        // Everywhere else, every actor names a record.
        foreach (var record in records.Where(r => r.Id != script.Id))
            Assert.Equal(Borg13Script.Parse(archive.Read(Convert.ToInt32(record.Id.Key, 16))).Actors.Count, record.Actors?.Count ?? 0);
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
            var linked = record.Nodes.Where(n => n.Kind == DialogueNodeKind.Case).SelectMany(n => n.Links ?? []).ToHashSet();
            cases += record.Nodes.Count(n => n.Kind == DialogueNodeKind.Case);
            foreach (var node in record.Nodes)
                Assert.Equal(linked.Contains(node.Index), node.Selects is not null);
        }
        Assert.Equal(118, cases);

        // A range without an upper bound stays open.
        var selects = Build(id).Records.SelectMany(r => r.Nodes).Select(n => n.Selects).OfType<DialogueRange>().ToList();
        Assert.Contains(selects, s => s.Max is null);
        Assert.All(selects, s => Assert.True(s.Max is null || s.Max >= s.Min));
        Assert.All(selects.Where(s => s.Max is null), s => Assert.True(s.Holds(60_000)));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void BlankNodesAreCounted(RomVersionId id)
    {
        var (archive, _, records) = Build(id);
        Assert.Equal(153, records.Count(r => r.BlankNodes == 1));
        Assert.All(records, r => Assert.InRange(r.BlankNodes, 0, 1));
        Assert.Equal(26_837, records.Sum(r => r.Nodes.Count + r.BlankNodes));
        Assert.Equal(archive.Listings.Count(l => l.Type == BorgType.Dialogue), records.Count);
    }

    [Fact]
    public void TheRecordsRoundTripThroughJson()
    {
        var records = Build(RomVersionId.Na10).Records;
        var json = JsonSerializer.SerializeToUtf8Bytes(records, PluginJson.Options);
        var back = JsonSerializer.Deserialize<List<DialogueRecord>>(json, PluginJson.Options)!;

        Assert.Equal(records.Count, back.Count);
        foreach (var (a, b) in records.Zip(back))
        {
            Assert.Equal((a.Id, a.Number, a.Mode, a.DoneFlag, a.Backdrop, a.BattleFlag, a.Rest, a.BlankNodes),
                (b.Id, b.Number, b.Mode, b.DoneFlag, b.Backdrop, b.BattleFlag, b.Rest, b.BlankNodes));
            Assert.Equal(a.Actors ?? [], b.Actors ?? []);
            Assert.Equal(a.Nodes.Count, b.Nodes.Count);
            foreach (var (x, y) in a.Nodes.Zip(b.Nodes))
            {
                Assert.Equal((x.Index, x.Kind, x.Text, x.Selects), (y.Index, y.Kind, y.Text, y.Selects));
                Assert.Equal(x.Links ?? [], y.Links ?? []);
                Assert.Equal(x.Ops ?? [], y.Ops ?? []);
            }
        }
    }

    [Fact]
    public void DbIdsNameRecordsByTheirWholeId()
    {
        Assert.Equal(RecordId.Parse("aidyn:entity/0x023F"), DbRecordIds.For(new ItemId(0x023F)));
        Assert.Equal(RecordTypes.DialogEntity, DbRecordIds.For(ItemId.Of(DbType.DialogEntity, 3))!.Value.Type);
        Assert.Equal(RecordTypes.Armor, DbRecordIds.For(ItemId.Of(DbType.Shield, 1))!.Value.Type);
        Assert.Equal(RecordTypes.Item, DbRecordIds.For(ItemId.Of(DbType.KeyItem, 1))!.Value.Type);
        Assert.Equal(RecordTypes.Weapon, DbRecordIds.For(ItemId.Of(DbType.Weapon, 9))!.Value.Type);
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
        // Entities and spells have records of their own, which name them too.
        var names = BaseImporter.BuildNames(db, models).SelectMany(f => f.Records).ToDictionary(r => r.Id, r => r.Name);
        foreach (var entity in CharacterImporter.BuildEntities(db, models))
            names.Add(entity.Id, entity.Name);
        foreach (var spell in CharacterImporter.BuildSpells(db))
            names.Add(spell.Id, spell.Name);
        Assert.Equal("Alaron", names[RecordId.Hex(RecordTypes.Entity, 0x023F)]);
        Assert.Equal("Trahern", names[RecordId.Hex(RecordTypes.Entity, 0x0239)]);
        // Potions have no DB table: they're named by their own string table, by index.
        Assert.Equal("Healing Potion", names[RecordId.Hex(RecordTypes.Item, 0x1004)]);

        var named = Build(RomVersionId.Na10).Records
            .SelectMany(r => (r.Actors ?? []).Concat(Ops([r]).Select(o => o.Target).OfType<RecordId>()))
            .Where(id => id.Type is not (RecordTypes.Flag or RecordTypes.Dialogue))
            .Distinct().ToList();
        Assert.NotEmpty(named);
        // Scene 1076 shows two "spells", which have no model, so nothing shows (get_item_borg5).
        Assert.Equal(["aidyn:spell/0x03EB", "aidyn:spell/0x03ED"],
            named.Where(id => !names.ContainsKey(id)).Select(id => id.ToString()).Order());
    }
}
