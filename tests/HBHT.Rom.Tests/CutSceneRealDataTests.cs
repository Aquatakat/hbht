using System.Numerics;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

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

        public float Tick(int request) => request switch { Body.Walk => 0.03f, Body.Run => 0.06f, _ => 0 };
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
                world.Add(ChunkKey.Of(set, chunk), CollisionMesh.Read(File.ReadAllBytes(glb)), set.Origin(chunk.Row, chunk.Column));
        }
        return world;
    }

    /// <summary>A cut-scene from its dialogue object, on its real stage with its chunks' collision.</summary>
    private CutScene Staged(PluginSet plugins, string script)
    {
        var id = RecordId.Parse(script);
        var start = Starts(plugins).First(s => s.Script == id && s.From.MapObject is { } o && fixture.Data.Objects[o] is DialogueObject);
        var bare = CutSceneStage.For(plugins, start.Stage, new CollisionWorld())!;
        var stage = bare with { World = World(plugins, bare.Set, bare.Chunk) };
        return new CutScene(GameState.NewGame(plugins), plugins.Get<DialogueRecord>(id), start.From, stage);
    }

    /// <summary>
    /// Shamsuk's close-up in his tower (<c>aidyn:dialogue/0x0494</c>, nodes 64, 72 and 116: <c>CameraCutToActor</c> Alaron and
    /// <c>CameraOn</c> point 3): the camera looks out of Alaron's eyes at the ground point beyond Shamsuk, who stands where his
    /// walk to point 9 (node 44) left him. Played with the actors' own animations and A pressed two seconds into each page, his
    /// walk's last step carries him on from within its radius of 1 to half a unit short of the point, as the user's screenshot
    /// of 1.0 frames him, left of the middle, because the original steers once a frame (<c>Body.StepTowardGoal</c>).
    /// </summary>
    [Fact]
    public void Shamsuks_close_up_frames_him_where_his_walk_ends()
    {
        var plugins = PluginSet.Load([fixture.Data.Dir]);
        var scene = Staged(plugins, "aidyn:dialogue/0x0494");
        var motions = new Dictionary<CutSceneActor, IActorMotion>();
        IActorMotion Motion(CutSceneActor actor)
        {
            if (!motions.TryGetValue(actor, out var motion))
                motions[actor] = motion = (IActorMotion?)ActorMotion.For(plugins, plugins.Get<ActorRecord>(actor.Look.Actor), new Core.Rng.Lcg(1)) ?? IActorMotion.Still;
            return motion;
        }
        scene.Start();
        var (shown, since) = (-1, 0);
        for (var tick = 0; !scene.Over && tick < 20_000; tick++)
        {
            var node = scene.Page?.Line.Node ?? -1;
            (shown, since) = node == shown ? (shown, since + 1) : (node, 0);
            if (node == 116)
                break;
            if (scene.Accepts && scene.Page is not null && since >= 120)
                scene.Press(scene.Runner.Continue);
            scene.Tick(Motion);
        }
        Assert.Equal(116, shown);
        var origin = scene.Stage.Origin;
        var alaron = scene.Actor(GameState.Alaron)!;
        var shamsuk = scene.Actor(Ids.Entity(0x0213))!;
        Assert.True(alaron.Hidden);
        AssertNear(alaron.Eyes, scene.Camera.Position);
        AssertNear(origin + new Vector3(8.713f, -19.128f, 40.231f), scene.Camera.Position);
        AssertNear(scene.Stage.PointAt(768 + 3)!.Value, scene.Camera.Aim);
        var point9 = scene.Stage.PointAt(768 + 9)!.Value;
        var at = shamsuk.Body.Sphere.Position;
        var short9 = Vector2.Distance(new Vector2(at.X, at.Z), new Vector2(point9.X, point9.Z));
        Assert.InRange(short9, 0.4f, 0.6f);
        // A little left of the line from the camera to its aim: +Z, looking along −X.
        var look = Vector3.Normalize(scene.Camera.Aim - scene.Camera.Position);
        var off = Vector3.Cross(look, Vector3.UnitY);
        Assert.InRange(Vector3.Dot(at - scene.Camera.Position, off), -0.4f, -0.1f);

        static void AssertNear(Vector3 expected, Vector3 actual) => Assert.True(Vector3.Distance(expected, actual) < 0.01f, $"{actual} isn't {expected}");
    }

    /// <summary>
    /// Back in Port Saiid from the Namer's Isle (<c>aidyn:dialogue/0x0430</c>), Alaron is spawned at point 43, about 10 above
    /// the ground around it, over a hole in D15's collision: the set-down from on high finds nothing (<c>CreateActor</c>'s
    /// <c>Body.SetDown(body, 0, 0, 0)</c>, <c>0x8004f4e4</c>), so he's left at the point's height and falls until he's 30 from
    /// the camera's aim, where bodies stop stepping. The script's one camera (point 35, on point 11) has him 33° off its
    /// axis, outside a 4:3 picture's 28.9° but inside 16:9's 36.4°: only a wide window shows it.
    /// </summary>
    [Fact]
    public void Alaron_falls_where_a_4_by_3_picture_doesnt_show_him_back_in_Port_Saiid()
    {
        var plugins = PluginSet.Load([fixture.Data.Dir]);
        var scene = Staged(plugins, "aidyn:dialogue/0x0430");
        scene.Start();
        var alaron = scene.Actor(GameState.Alaron)!;
        var spawned = alaron.Body.Sphere.Position;
        var point = scene.Stage.PointAt(128 + 43)!.Value;
        Assert.Equal(point.Y, spawned.Y, 3);
        Assert.False(Body.SetDown(scene.Stage.World, point, alaron.Body.Sphere.Radius, null).Grounded);
        for (var tick = 0; tick < 300; tick++)
            scene.Tick(_ => IActorMotion.Still);
        Assert.True(alaron.Body.Sphere.Position.Y < point.Y - 20);
        var look = scene.Camera.Aim - scene.Camera.Position;
        var to = alaron.Body.Sphere.Position - scene.Camera.Position;
        var angle = MathF.Abs(MathF.Atan2(look.X * to.Z - look.Z * to.X, look.X * to.X + look.Z * to.Z)) * 180 / MathF.PI;
        Assert.InRange(angle, 28.9f, 36.4f);
    }

    /// <summary>
    /// The final cut-scene (<c>aidyn:dialogue/0x04FE</c>) spawns Phelan as his entity (<c>0x0230</c>), whose actor
    /// (<c>aidyn:actor/0x2918</c>) is his one-legged model, at point 143 on the floor, set down from on high as every spawn is
    /// (<c>CreateActor</c>, <c>0x8004f4c8</c>). He stands (state 0) until the script asks for request 20, and lies from then on:
    /// states 9 and 10 for 20, 11 for 21, and his death (13, request 9) holds its last frame, never going on to 12. The pose is the views' to draw
    /// (<c>TransformTracks</c>); this is the core's part, which was right.
    /// </summary>
    [Fact]
    public void Phelan_lies_from_his_first_animation_and_his_death_holds()
    {
        var plugins = PluginSet.Load([fixture.Data.Dir]);
        var scene = Staged(plugins, "aidyn:dialogue/0x04FE");
        var motions = new Dictionary<CutSceneActor, ActorMotion>();
        IActorMotion Motion(CutSceneActor actor)
        {
            if (!motions.TryGetValue(actor, out var motion))
                motions[actor] = motion = ActorMotion.For(plugins, plugins.Get<ActorRecord>(actor.Look.Actor), new Core.Rng.Lcg(1))!;
            return motion;
        }
        scene.Start();
        var phelan = scene.Actor(Ids.Entity(0x0230))!;
        Assert.Equal(RecordId.Hex(RecordTypes.Actor, 0x2918), phelan.Look.Actor);
        var point = scene.Stage.PointAt(143)!.Value;
        Assert.Equal(new Vector2(point.X, point.Z), new Vector2(phelan.Body.Sphere.Position.X, phelan.Body.Sphere.Position.Z));
        var states = new List<int>();
        var (shown, since) = (-1, 0);
        for (var tick = 0; !scene.Over && tick < 20_000; tick++)
        {
            var node = scene.Page?.Line.Node ?? -1;
            (shown, since) = node == shown ? (shown, since + 1) : (node, 0);
            if (scene.Accepts && scene.Page is not null && since >= 60)
                scene.Press(scene.Runner.Continue);
            scene.Tick(Motion);
            if (motions.TryGetValue(phelan, out var m) && (states.Count == 0 || states[^1] != m.Machine.State))
                states.Add(m.Machine.State);
        }
        Assert.True(scene.Over);
        // Standing until the first animation, then never again.
        Assert.Equal(0, states[0]);
        Assert.Equal(9, states[1]);
        Assert.DoesNotContain(0, states.Skip(1));
        Assert.Contains(11, states);
        // The death holds its own last frame: the state after it (12) is never stepped to.
        Assert.Equal(13, states[^1]);
        Assert.True(phelan.Body.DeathHeld);
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
