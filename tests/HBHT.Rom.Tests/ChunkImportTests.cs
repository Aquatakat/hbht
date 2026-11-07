using HBHT.Core.Scripts;
using HBHT.Core.Trek;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>What NA 1.0's import makes of chunks, their map objects and what refers to them (ADRs 0035 and 0036).</summary>
public sealed class ChunkImportTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    [Fact]
    public void TheImportIsCleanAndHasNoMapsOrMinimapsFiles()
    {
        Assert.True(Data.Set.Validate().IsClean, Data.Set.Validate().Describe());
        var records = Path.Combine(Data.Dir, "records");
        Assert.False(Directory.Exists(Path.Combine(records, "maps")));
        Assert.False(File.Exists(Path.Combine(records, "minimaps.json")));
        Assert.Equal(629, Directory.GetFiles(Path.Combine(records, "chunks"), "*.json", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void ThereAreFourHundredTwentyOneNpcsAndSixHundredFiftyEightEncounters()
    {
        Assert.Equal(24869, Data.ChunkRecords.Sum(c => c.Objects.Count));
        Assert.Equal(421, Data.Of<NpcObject>().Count());
        Assert.Equal(658, Data.Of<EncounterObject>().Count());
        Assert.Equal(421 + 658, Data.Of<PartyObject>().Count());
    }

    [Fact]
    public void AnNpcKeepsItsEnemiesOnlyWhereItsScriptStartsABattleOrTheListNamesSomeoneElse()
    {
        var npcs = Data.Of<NpcObject>().ToList();
        var keeping = npcs.Where(n => n.Object.Enemies is not null).ToList();
        Assert.Equal(66, keeping.Count);
        Assert.Equal(355, npcs.Count(n => n.Object.Enemies is null));

        bool StartsABattle(NpcObject npc) => Data.Set.Get<ScriptRecord>(npc.Script).Nodes.SelectMany(n => n.Ops).Any(o => o is ScriptOp.BattleStart);
        Assert.Equal(35, keeping.Count(n => StartsABattle(n.Object)));
        // The rest list someone other than the npc itself.
        Assert.Equal(31, keeping.Count(n => !StartsABattle(n.Object)));
        Assert.All(keeping.Where(n => !StartsABattle(n.Object)), n => Assert.Contains(n.Object.Enemies!, e => e.Entity != n.Object.Entity));
        Assert.All(npcs.Where(n => n.Object.Enemies is null), n => Assert.False(StartsABattle(n.Object)));
    }

    [Fact]
    public void SixNpcsStageTheirCutSceneFromAScriptObject()
    {
        var staged = Data.Of<NpcObject>().Where(n => n.Object.Stage is not null)
            .Select(n => $"{n.Id.Key} -> {n.Object.Stage!.Value.Key}").Order(StringComparer.Ordinal).ToList();
        Assert.Equal(new[]
        {
            "0/C15/39 -> 0/C15/27", "0/I08/19 -> 0/I08/4", "0/I08/27 -> 0/I08/6", "0/I19/4 -> 0/I19/2",
            "4/I01/5 -> 4/I01/2", "8/C05/17 -> 8/C05/12",
        }.Order(StringComparer.Ordinal), staged);
        // Each stage is a script object that stages a cut-scene.
        Assert.All(Data.Of<NpcObject>().Where(n => n.Object.Stage is not null),
            n => Assert.NotNull(Assert.IsType<ScriptObject>(Data.Objects[n.Object.Stage!.Value]).Stage));
        // Edur's, in I08.
        Assert.Equal(RecordId.Hex(RecordTypes.Script, 0x0391), Data.Objects[Ids.MapObject(0, "I08", 19)] is NpcObject edur ? edur.Script : default);
    }

    [Fact]
    public void KeelinsCutSceneStartsFromHerStagesMarksAndAnotherTalkersFromZeroInTheChunkTheyAreIn()
    {
        var plugins = Data.Set;
        var keelinId = Ids.MapObject(8, "C05", 17);
        var keelin = Assert.IsType<NpcObject>(Data.Objects[keelinId]);
        Assert.Equal(Ids.Script(0x0382), keelin.Script);
        var script = plugins.Get<ScriptRecord>(keelin.Script);
        Assert.Equal(ScriptMode.Cutscene, script.Mode);

        var launch = TrekLoop.TalkerLaunch(plugins, script, keelin, keelinId, Ids.Chunk(8, "D06"));
        Assert.Equal(script, launch.Script);
        Assert.Equal(new ScriptStart(128, keelinId), launch.Start);
        Assert.Equal(Ids.Chunk(8, "C05"), launch.Stage);
        var stage = Assert.IsType<ScriptObject>(Data.Objects[keelin.Stage!.Value]);
        Assert.Equal((128, Ids.Chunk(8, "C05")), (stage.Marks, stage.Stage));

        // Without a stage it starts from mark 0 in the chunk the player is in.
        var plain = Data.Of<NpcObject>().First(n => n.Object.Stage is null && plugins.Get<ScriptRecord>(n.Object.Script).Mode == ScriptMode.Cutscene);
        var plainLaunch = TrekLoop.TalkerLaunch(plugins, plugins.Get<ScriptRecord>(plain.Object.Script), plain.Object, plain.Id, Ids.Chunk(0, "K11"));
        Assert.Equal(new ScriptStart(0, plain.Id), plainLaunch.Start);
        Assert.Equal(Ids.Chunk(0, "K11"), plainLaunch.Stage);
    }

    [Fact]
    public void TeleportersNameTheirArrivalAndSoTheirChunkExceptOneThatNamesJustTheChunk()
    {
        var teleporters = Data.Of<TeleporterObject>().ToList();
        Assert.Equal(894, teleporters.Count);
        Assert.Equal(893, teleporters.Count(t => t.Object.Arrival is not null));
        Assert.All(teleporters.Where(t => t.Object.Arrival is not null), t => Assert.Null(t.Object.Destination));
        var only = Assert.Single(teleporters, t => t.Object.Arrival is null);
        Assert.NotNull(only.Object.Destination);
        Assert.Equal(only.Object.Destination, only.Object.Leads);
        Assert.Equal("aidyn:mapobject/11/K07/0", only.Id.ToString());
    }

    [Fact]
    public void RecordsNameChunksAndMapObjectsInTheNewKeys()
    {
        var plugins = Data.Set;
        Assert.Equal(Ids.Chunk(0, "I08"), plugins.Singleton<NewGameRecord>().Chunk);
        Assert.Equal(Ids.Chunk(16, "A01"), plugins.Singleton<CombatRecord>().DefaultArena);
        Assert.Equal(Ids.MapObject(6, "C03", 2), plugins.Singleton<CombatRecord>().GameOver!.From);
        Assert.Equal(Ids.MapObject(6, "C05", 0), plugins.Singleton<CampingRecord>().Campsite);

        var start = plugins.Get<TriggerRecord>(RecordId.Decimal(RecordTypes.Trigger, 1)).Do.OfType<ScriptOp.StartScript>().Single();
        Assert.Equal(Ids.MapObject(0, "I08", 7), start.From);

        var arenas = plugins.All<ScriptRecord>().SelectMany(s => s.Nodes).SelectMany(n => n.Ops).OfType<ScriptOp.BattleArena>()
            .GroupBy(a => a.Arena.Key).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int>
        {
            ["16/A01"] = 3, ["16/I11"] = 1, ["16/I01"] = 3, ["16/C01"] = 1, ["16/A11"] = 1, ["16/E11"] = 9, ["16/K01"] = 1, ["16/M11"] = 2,
        }, arenas);
        Assert.Equal(21, arenas.Values.Sum());
    }

    [Fact]
    public void OnlyTheGoblinAmbushLugashAndTheMarquisHaveBattleRules()
    {
        var scripts = Data.Set.All<ScriptRecord>().Where(s => s.BattleRules is not null).Select(s => s.Id.Key).Order(StringComparer.Ordinal);
        Assert.Equal(["0x0365", "0x04D1"], scripts);
        var npcs = Data.Of<NpcObject>().Where(n => n.Object.BattleRules is not null).Select(n => n.Id.Key).Order(StringComparer.Ordinal);
        Assert.Equal(["0/E23/2", "0/H08/13", "0/H08/8", "0/I08/26"], npcs);
        Assert.Equal(Data.Set.Get<ScriptRecord>(Ids.Script(0x0365)).BattleRules, Assert.IsType<NpcObject>(Data.Objects[Ids.MapObject(0, "H08", 8)]).BattleRules);
        Assert.Equal(Ids.Chunk(6, "E03"), Data.Set.Get<ScriptRecord>(Ids.Script(0x0365)).BattleRules!.End!.Place);
        Assert.Equal(new BattleRules(Flee: true), Assert.IsType<NpcObject>(Data.Objects[Ids.MapObject(0, "E23", 2)]).BattleRules);
        Assert.DoesNotContain(Data.Of<EncounterObject>(), e => e.Object.BattleRules is not null);
    }

    [Fact]
    public void TheBaseChunksAndScriptsAreIndexedWithoutBeingRead()
    {
        // A fresh load of the imported plugin reads no chunk or script until it is asked for (ADR 0036).
        var fresh = PluginSet.Load([Data.Dir]);
        var chunks = fresh.IdsOf(RecordTypes.Chunk);
        Assert.Equal(629, chunks.Count);
        Assert.All(chunks, id => Assert.False(fresh.IsRead(id)));
        Assert.All(fresh.IdsOf(RecordTypes.Script), id => Assert.False(fresh.IsRead(id)));
        var index = ChunkIndex.For(fresh);
        Assert.Equal(Ids.Chunk(0, "I08"), index.At(RecordId.Decimal(RecordTypes.MapSet, 0), 9, 8));
        Assert.All(chunks, id => Assert.False(fresh.IsRead(id)));

        var i08 = fresh.ChunkAt(RecordId.Decimal(RecordTypes.MapSet, 0), 9, 8)!;
        Assert.True(fresh.IsRead(i08.Id));
        Assert.Equal(1, chunks.Count(fresh.IsRead));
    }
}
