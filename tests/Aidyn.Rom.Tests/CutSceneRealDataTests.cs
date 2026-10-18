using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Every cut-scene the base game starts, from a dialogue object or a talker, played on its real stage in a new game,
/// pressing A through every page and taking the first choice, until it's over.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class CutSceneRealDataTests(Na10Import fixture, ITestOutputHelper output)
{
    /// <summary>Walks and runs move a body along; every state lasts a second.</summary>
    private sealed class Walking : IActorMotion
    {
        public static readonly Walking Instance = new();

        public int StateLength => 60;

        public float Tick(int request) => request switch { PlayerMotion.Walk => 0.03f, PlayerMotion.Run => 0.06f, _ => 0 };
    }

    private sealed record Start(RecordId Script, ScriptStart From, ChunkRef Stage);

    /// <summary>What starts a cut-scene: dialogue objects with their stage and base, and talkers in their own chunk from 0.</summary>
    private IEnumerable<Start> Starts(PluginSet plugins)
    {
        var chunkOf = plugins.All<MapSetRecord>(RecordTypes.MapSet)
            .SelectMany(s => s.Chunks.Select(c => (c.Map, Ref: new ChunkRef(s.Id, c.Row, c.Column))))
            .GroupBy(c => c.Map).ToDictionary(g => g.Key, g => g.First().Ref);
        foreach (var map in fixture.Data.Maps)
        {
            foreach (var o in map.Objects)
            {
                var source = MapRecord.VoxelId(map.Id, o.Index);
                if (o is DialogueObject { Stage: { } stage } d && Mode(plugins, d.Script) == DialogueMode.Cutscene)
                    yield return new Start(d.Script, new ScriptStart(d.RefPointBase, source), stage);
                else if (o is MonsterPartyObject { Dialogue: { } script } p && (p.Flags & PartyFlags.Silent) == 0
                    && Mode(plugins, script) == DialogueMode.Cutscene && chunkOf.TryGetValue(map.Id, out var own))
                    yield return new Start(script, new ScriptStart(0, source), own);
            }
        }
    }

    private static DialogueMode? Mode(PluginSet plugins, RecordId script) => plugins.TryGet<DialogueRecord>(script)?.Mode;

    private static CollisionWorld World(PluginSet plugins, MapSetRecord set, ChunkEntry centre)
    {
        var world = new CollisionWorld();
        foreach (var chunk in set.Chunks.Where(c => Math.Abs(c.Row - centre.Row) <= 1 && Math.Abs(c.Column - centre.Column) <= 1))
        {
            if (plugins.FindAsset(chunk.Map, "glb") is { } glb)
                world.Add((chunk.Row, chunk.Column), CollisionMesh.Read(File.ReadAllBytes(glb)), set.Origin(chunk.Row, chunk.Column));
        }
        return world;
    }

    [Fact]
    public void Every_cut_scene_plays_to_its_end()
    {
        var plugins = PluginSet.Load([fixture.Data.Dir]);
        var starts = Starts(plugins).ToList();
        var problems = new List<string>();
        var played = 0;
        var longest = 0;
        foreach (var start in starts)
        {
            var game = GameState.NewGame(plugins);
            var stageWorld = CutSceneStage.For(plugins, start.Stage, new CollisionWorld())!;
            var stage = stageWorld with { World = World(plugins, stageWorld.Set, stageWorld.Chunk) };
            var scene = new CutScene(game, plugins.Get<DialogueRecord>(start.Script), start.From, stage);
            scene.Start();
            var ticks = 0;
            for (; !scene.Over && ticks < 100_000; ticks++)
            {
                if (scene.Accepts && scene.Page is { } page)
                {
                    if (page.Choices.Count > 0 && scene.Runner.Wait != ScriptWait.Player)
                        scene.Press(() => scene.Runner.Choose(0));
                    else
                        scene.Press(scene.Runner.Continue);
                }
                scene.Tick(_ => Walking.Instance);
            }
            Assert.True(scene.Over, $"{start.Script} from {start.From.MapObject} isn't over after {ticks} ticks, waiting for {scene.Runner.Wait}.");
            played++;
            longest = Math.Max(longest, ticks);
            problems.AddRange(scene.Problems.Select(p => $"{start.From.MapObject}: {p}").Distinct());
        }
        output.WriteLine($"{played} cut-scenes played, the longest {longest} ticks.");
        foreach (var p in problems.Distinct())
            output.WriteLine(p);
        Assert.Equal(305, played);
        // Only talkers' cut-scenes, staged in the chunk the player stands in, name reference points their party's chunk
        // lacks; the player may stand in a neighbour as he talks.
        Assert.All(problems, p => Assert.IsType<MonsterPartyObject>(fixture.Data.Objects[RecordId.Parse(p[..p.IndexOf(": ", StringComparison.Ordinal)])]));
    }
}
