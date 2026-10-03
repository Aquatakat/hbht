using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Trek's loop, headless, on NA 1.0's data and a new game: the chunks around the player and their
/// wanderers, a door's fade, the clock under a conversation, chunk crossings and the fade into a cut-scene.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class TrekLoopTests(MapObjectFixture fixture)
{
    private static readonly RecordId Overworld = RecordId.Parse("aidyn:mapset/0");

    private MapObjectFixture.Loaded Data => fixture.Data;

    private sealed class Host : ITrekHost
    {
        public List<ChunkEntry?> Entered { get; } = [];
        public int Arrivals { get; set; }
        public int ConversationChanges { get; set; }
        public List<string> Said { get; } = [];

        void ITrekHost.Entered(ChunkEntry? chunk) => Entered.Add(chunk);

        void ITrekHost.Arrived() => Arrivals++;

        void ITrekHost.ConversationChanged() => ConversationChanges++;

        void ITrekHost.ScriptEnded(RecordId script, ScriptEnding ending, RecordId? keeper)
        {
        }

        IRootMotion ITrekHost.Motion(Wanderer wanderer) => IActorMotion.Still;

        void ITrekHost.OpenLoot(ItemPile loot, int gold)
        {
        }

        void ITrekHost.Animate(LoadedMap map, SceneObject actor, int request)
        {
        }

        void ITrekHost.Changed(LoadedMap map, SceneObject scene)
        {
        }

        void ITrekHost.Sound(string cue)
        {
        }

        void ITrekHost.Say(string text) => Said.Add(text);

        void ITrekHost.Log(string text)
        {
        }

        void IScriptHost.Cue(ScriptCue cue)
        {
        }
    }

    /// <summary>Trek as a new game starts it, with the player on the ground at a chunk's point.</summary>
    private (TrekLoop Trek, Host Host) Start(string chunkName = "I08", float x = 16, float z = 62)
    {
        var game = GameState.NewGame(Data.Set);
        var host = new Host();
        var streamer = new ChunkStreamer(Data.Set, game, new CollisionWorld(), Data.Set.Get<MapSetRecord>(Overworld));
        var trek = new TrekLoop(game, host, streamer);
        var chunk = streamer.Find(chunkName)!;
        var point = streamer.Origin(chunk) + new Vector3(x, 10, z);
        trek.Enter(chunk);
        streamer.Update(point);
        var ground = TeleportArrival.Settle(streamer.World, point, 0.5f) ?? point;
        var look = new Vector2(0, -1);
        trek.Begin(new PlayerMotion(ground, look), new FollowCamera(ground, look));
        return (trek, host);
    }

    private static PlayerInput Still() => new(Vector2.Zero);

    /// <summary>How many chunks the map set has in the 3×3 around one: fewer at its edges.</summary>
    private static int Around(ChunkStreamer chunks, ChunkEntry chunk) =>
        chunks.MapSet.Chunks.Count(c => Math.Abs(c.Row - chunk.Row) <= 1 && Math.Abs(c.Column - chunk.Column) <= 1);

    private static PlayerInput NotRead() => throw new InvalidOperationException("Trek read the player's input.");

    [Fact]
    public void TheChunksAroundThePlayerLoadWithTheirWanderersAndGoWithThem()
    {
        var (trek, _) = Start();
        var chunks = trek.Chunks;

        // I08 is at the overworld's edge.
        Assert.Equal(Around(chunks, chunks.Find("I08")!), chunks.All.Count());
        Assert.InRange(chunks.All.Count(), 4, 8);
        Assert.NotEmpty(trek.Wanderers.All);
        Assert.True(chunks.World.Count > 0);
        Assert.All(trek.Wanderers.All, w => Assert.Contains(chunks.All, c => c.Map == w.Map));

        // Far off, every chunk the 3×3 left is gone, and its wanderers with it.
        var before = chunks.All.Select(c => c.Map).ToList();
        var i08 = chunks.Find("I08")!;
        var far = chunks.MapSet.Chunks.MaxBy(c => Math.Abs(c.Row - i08.Row) + Math.Abs(c.Column - i08.Column))!;
        chunks.Update(chunks.Origin(far) + new Vector3(50, 0, 50));
        Assert.DoesNotContain(chunks.All, c => before.Contains(c.Map));
        Assert.All(trek.Wanderers.All, w => Assert.Contains(chunks.All, c => c.Map == w.Map));
    }

    [Fact]
    public void ADoorFadesOutHoldsBlackThenTakesThePlayerThroughAndFadesIn()
    {
        var (trek, host) = Start("M07", 40, 55);
        var map = trek.Chunks.MapAt(trek.Player.Sphere.Position)!;
        // A door in Gwernia to another map set, with nothing to stop it.
        var door = map.Objects.OfType<TeleporterObject>().First(t => t.LockLevel == 0 && t.TrapLevel == 0 && t.SecretFlag is null
            && TeleportArrival.For(Data.Set, t) is { } a && a.Set.Id != Overworld);
        var arrival = TeleportArrival.For(Data.Set, door)!;
        var memory = trek.Game.Memory.Remember(new Memory(MapRecord.VoxelId(map.Record.Id, door.Index), MemoryKind.Opened), 0);
        var time = trek.Game.Clock.Time;

        ((IMapObjectHost)trek).Teleport(map, door);
        Assert.True(trek.Teleporting);

        // Out at 2/3 a tick, then three frames at black, with the player's input dead throughout.
        var brightness = new List<float>();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(TrekStep.Ran, trek.Tick(NotRead));
            brightness.Add(trek.Brightness);
            if (i < 7)
                Assert.Equal(Overworld, trek.Chunks.MapSet.Id);
        }
        Assert.Equal(1 - TrekLoop.TeleportFadeOutPerTick, brightness[0]);
        Assert.All(brightness.Skip(1), b => Assert.Equal(0, b));
        Assert.Equal(arrival.Set.Id, trek.Chunks.MapSet.Id);
        Assert.False(trek.Teleporting);
        Assert.Equal(1, host.Arrivals);
        Assert.Equal(arrival.Chunk, trek.Chunks.ChunkAt(trek.Player.Sphere.Position));
        Assert.Equal(MapObjectMemory.Long - 1, memory.Lifetime);
        // A fade passes no less time than any other tick.
        Assert.NotEqual(time, trek.Game.Clock.Time);

        // In at 1/15 a tick, without input until the tick it's done.
        for (var i = 0; i < 14; i++)
            trek.Tick(NotRead);
        Assert.True(trek.TeleportFade.Fading);
        trek.Tick(Still);
        Assert.False(trek.TeleportFade.Fading);
        Assert.Equal(1, trek.Brightness);
    }

    [Fact]
    public void ASignStopsTheClockAndTheWanderersButNotTheWorld()
    {
        var (trek, host) = Start();
        var (_, record, sign) = Data.Of<DialogueObject>()
            .First(d => Data.Set.TryGet<DialogueRecord>(d.Object.Script) is { Mode: DialogueMode.Sign });
        var map = LoadedMap.Activate(record, record.Id, trek.Game.Flags, trek.Game.Memory, trek.Game.Party, new Lcg());

        ((IMapObjectHost)trek).StartScript(map, sign);
        Assert.NotNull(trek.Conversation);
        Assert.True(host.ConversationChanges > 0);

        var time = trek.Game.Clock.Time;
        var timers = trek.Wanderers.All.Select(w => w.Timer).ToList();
        for (var i = 0; i < 10; i++)
            Assert.Equal(TrekStep.Ran, trek.Tick(NotRead));
        Assert.Equal(time, trek.Game.Clock.Time);
        Assert.Equal(timers, trek.Wanderers.All.Select(w => w.Timer));
    }

    [Fact]
    public void CrossingIntoAnotherChunkAgesWhatMapObjectsRemember()
    {
        var (trek, _) = Start();
        var memory = trek.Game.Memory.Remember(new Memory(MapRecord.VoxelId(RecordId.Hex(RecordTypes.Map, 0x0FB6), 0), MemoryKind.Opened), 0);
        trek.Tick(Still);
        Assert.Equal(MapObjectMemory.Long, memory.Lifetime);

        var next = trek.Chunks.Find("I09")!;
        trek.Player.Sphere.Position = trek.Chunks.Origin(next) + new Vector3(16, trek.Player.Sphere.Position.Y, 2);
        trek.Tick(Still);
        Assert.Equal(MapObjectMemory.Long - 1, memory.Lifetime);
        trek.Tick(Still);
        Assert.Equal(MapObjectMemory.Long - 1, memory.Lifetime);
    }

    [Fact]
    public void ACueFadesTrekOutThenItsWorldGoesForTheCutSceneAndComesBackAfter()
    {
        var (trek, _) = Start();
        var (id, map, dialogue) = Data.Of<DialogueObject>()
            .First(d => Data.Set.TryGet<DialogueRecord>(d.Object.Script) is { Mode: DialogueMode.Cutscene } && d.Object.Stage is not null);
        var script = Data.Set.Get<DialogueRecord>(dialogue.Script);

        trek.Cue(script, new ScriptStart(dialogue.RefPointBase, id), dialogue.Stage!);
        var ticks = 0;
        while (trek.Tick(NotRead) == TrekStep.Ran)
            Assert.InRange(++ticks, 1, 60);
        // 1/30 a tick down to black, then three frames there; the tick the fade ends, the cut-scene takes over.
        Assert.InRange(ticks, 30 + 6 - 1, 31 + 6 - 1);
        Assert.Equal(0, trek.Brightness);

        var cued = trek.BeginCutScene();
        Assert.Equal(script, cued.Script);
        Assert.Null(trek.Cued);
        Assert.Empty(trek.Chunks.All);
        Assert.Equal(0, trek.Chunks.World.Count);
        Assert.Empty(trek.Wanderers.All);

        trek.Resume();
        Assert.Equal(Around(trek.Chunks, trek.Chunks.Find("I08")!), trek.Chunks.All.Count());
        Assert.NotEmpty(trek.Wanderers.All);
        Assert.True(trek.CutSceneFade.Fading);
        Assert.Equal(0, trek.Brightness);
    }
}
