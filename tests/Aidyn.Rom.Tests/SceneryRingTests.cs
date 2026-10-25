using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Actors;
using Aidyn.Core.Chunks;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Plugins.Saves;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The Draw Distance setting's Extended on NA 1.0's data (<see cref="SceneryRing"/>): the Overworld's 5×5 drawn, its outer
/// 16 chunks as scenery only, which a game plays the same with or without.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class SceneryRingTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    private MapSetRecord Set(int id) => Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, id));

    private sealed class Host : ITrekHost
    {
        void ITransitionHost.Transition(Transition next)
        {
        }

        void ITrekHost.Arrived()
        {
        }

        void ITrekHost.ConversationChanged()
        {
        }

        IRootMotion ITrekHost.Motion(Wanderer wanderer) => IActorMotion.Still;

        void ITrekHost.Animate(LoadedMap map, SceneObject actor, int request)
        {
        }

        void ITrekHost.Changed(LoadedMap map, SceneObject scene)
        {
        }

        void ITrekHost.Log(string text)
        {
        }

        void ITrekHost.Warn(string text)
        {
        }

        void IScriptHost.Cue(ScriptCue cue)
        {
        }
    }

    [Fact]
    public void OnTheOverworldItDrawsTheChunksRoundTheThreeByThreeAsSceneryAlone()
    {
        var game = GameState.NewGame(Data.Set);
        var zone = new ZoneEngine(game, new ChunkStreamer(Data.Set, game, new CollisionWorld(), Set(0), new VisibleWorld()));
        var chunks = zone.Chunks;
        var ring = new SceneryRing(game, chunks) { Enabled = true };
        var loaded = new List<LoadedMap>();
        ring.Loaded += (_, map) => loaded.Add(map);
        // I12, in the middle of the Overworld's widest stretch.
        chunks.Update(chunks.Origin(chunks.Find("I12")!) + new Vector3(50, 0, 50));
        var collision = chunks.World.Count;
        var surfaces = chunks.Visible!.Count;
        var sounds = zone.Sounds.Count;
        var memory = game.Memory.Entries.Count;
        var seeds = Seeds(game);

        Assert.True(ring.Update());

        var centre = chunks.Centre;
        var there = Enumerable.Range(-2, 5).SelectMany(r => Enumerable.Range(-2, 5).Select(c => (r, c)))
            .Count(o => Math.Max(Math.Abs(o.r), Math.Abs(o.c)) == 2 && chunks.MapSet.Chunk(centre.Row + o.r, centre.Column + o.c) is not null);
        Assert.InRange(there, 8, 16);
        Assert.Equal(there, loaded.Count);
        Assert.All(loaded, m => Assert.Equal(2, Math.Max(Math.Abs(m.Key.Row - centre.Row), Math.Abs(m.Key.Column - centre.Column))));
        Assert.Equal(loaded.Select(m => m.Key).ToHashSet(), ring.All.Select(c => c.Map.Key).ToHashSet());
        Assert.DoesNotContain(loaded, m => chunks.Map(m.Key) is not null);
        // Scenery alone: no collision, drawn surfaces, sounds or lights, and nothing remembered, rolled or aged.
        Assert.Equal(collision, chunks.World.Count);
        Assert.Equal(surfaces, chunks.Visible.Count);
        Assert.Equal(sounds, zone.Sounds.Count);
        Assert.All(loaded, m => Assert.Empty(zone.Lights.In(m)));
        Assert.Equal(memory, game.Memory.Entries.Count);
        Assert.Equal(seeds, Seeds(game));
        Assert.Equal(9, chunks.All.Count());
        // Its scene objects show by the same rules as the 3×3's, and nothing else of its maps is active.
        Assert.Contains(loaded, m => m.Record.Objects.OfType<SceneObject>().Any(o => m.IsActive(o.Index)));
        Assert.All(loaded, m => Assert.Equal(0, m.HarvestShare));

        // Unchanged where the 3×3 is unchanged.
        Assert.False(ring.Update());
    }

    [Fact]
    public void ItFollowsTheThreeByThreeAndHandsOverAChunkAsItComesIn()
    {
        var game = GameState.NewGame(Data.Set);
        var chunks = new ChunkStreamer(Data.Set, game, new CollisionWorld(), Set(0));
        var ring = new SceneryRing(game, chunks) { Enabled = true };
        var i12 = chunks.Origin(chunks.Find("I12")!) + new Vector3(50, 0, 50);
        chunks.Update(i12);
        ring.Update();
        var before = ring.All.Select(c => c.Map.Key).ToHashSet();

        // A column east: the ring's column east of I12's 3×3 comes into it, and the ring moves with it.
        chunks.Update(i12 + new Vector3(0, 0, 100));
        Assert.True(ring.Update());
        var after = ring.All.Select(c => c.Map.Key).ToHashSet();
        Assert.All(after, k => Assert.Null(chunks.Map(k)));
        Assert.Contains(before, k => chunks.Map(k) is not null);
        Assert.Contains(before, after.Contains);
        Assert.Contains(after, k => !before.Contains(k));
    }

    [Fact]
    public void ItsSceneObjectsShowByTheirFlagGatesAndThePutOutMemoryWhichItReadsWithoutChanging()
    {
        var game = GameState.NewGame(Data.Set);
        var chunks = new ChunkStreamer(Data.Set, game, new CollisionWorld(), Set(0));
        var ring = new SceneryRing(game, chunks) { Enabled = true };
        var j08 = chunks.Find("J08")!;
        var lights = Data.Set.Get<MapRecord>(j08.Map).Objects.OfType<SceneObject>().Where(o => o.GoesOut).ToList();
        Assert.NotEmpty(lights);
        // I10's 3×3 leaves J08 in the ring, a row down and two columns west.
        var i10 = chunks.Origin(chunks.Find("I10")!) + new Vector3(50, 0, 50);
        chunks.Update(i10);
        ring.Update();
        var shown = ring.Map(ChunkKey.Of(chunks.MapSet, j08))!;
        // Oriana's path lights aren't there until her conversation sets flag 50.
        Assert.All(lights, o => Assert.False(shown.IsActive(o.Index)));

        game.Flags.Set(Ids.Flag(50), true);
        // Brought up to date, as trek's tick does: the ring reads the flags as they're stored, so it waits for that.
        Assert.True(game.Flags.Dirty);
        Assert.False(ring.Regate());
        game.Flags.Process();
        var putOut = lights[0];
        game.Memory.Remember(new Memory(MapRecord.VoxelId(j08.Map, putOut.Index), MemoryKind.PutOut), 0, MapObjectMemory.PutOutLoads);
        var memory = game.Memory.Entries.Select(e => (e.Key, e.Value, e.Lifetime)).ToList();
        // Gated again, as the menu closing does: the lights are there, but for the one remembered as put out.
        Assert.True(ring.Regate());
        shown = ring.Map(ChunkKey.Of(chunks.MapSet, j08))!;
        Assert.False(shown.IsActive(putOut.Index));
        Assert.All(lights.Skip(1), o => Assert.True(shown.IsActive(o.Index)));
        Assert.Equal(memory, game.Memory.Entries.Select(e => (e.Key, e.Value, e.Lifetime)));
        Assert.False(ring.Regate());
    }

    [Fact]
    public void OffTheOverworldOrDisabledItHoldsNothing()
    {
        var game = GameState.NewGame(Data.Set);
        var chunks = new ChunkStreamer(Data.Set, game, new CollisionWorld(), Set(0));
        var ring = new SceneryRing(game, chunks);
        chunks.Update(chunks.Origin(chunks.Find("I12")!) + new Vector3(50, 0, 50));
        Assert.False(ring.Update());
        Assert.Empty(ring.All);

        ring.Enabled = true;
        Assert.True(ring.Update());
        Assert.NotEmpty(ring.All);

        // Another map set, Gwernia's interiors among them: the original's 3×3 alone.
        var other = Data.Set.All<MapSetRecord>(RecordTypes.MapSet).First(s => s.Id != SceneryRing.Overworld && s.Chunks.Count > 9);
        chunks.Change(other);
        var middle = other.Chunks.OrderBy(c => c.Row).ThenBy(c => c.Column).ElementAt(other.Chunks.Count / 2);
        chunks.Update(chunks.Origin(middle) + new Vector3(1, 0, 1));
        Assert.True(ring.Update());
        Assert.Empty(ring.All);

        // An emptied 3×3, as under a cut-scene's stage: nothing.
        chunks.Change(Set(0));
        chunks.Update(chunks.Origin(chunks.Find("I12")!) + new Vector3(50, 0, 50));
        ring.Update();
        chunks.Clear();
        ring.Update();
        Assert.Empty(ring.All);
    }

    [Fact]
    public void AGamePlaysTheSameWithTheRingAsWithout()
    {
        // The same game twice, its 3×3 streamed along a path as the runtime streams it about the free camera, crossing six
        // chunks east and back, with the wanderers, lights and sounds of every chunk it loads at work: once with the ring
        // brought round it every tick and gated again as the menu would, once without.
        var with = Play(ring: true);
        var without = Play(ring: false);
        Assert.Equal(without, with);
    }

    private string Play(bool ring)
    {
        var game = GameState.NewGame(Data.Set);
        var streamer = new ChunkStreamer(Data.Set, game, new CollisionWorld(), Set(0), new VisibleWorld());
        var start = GameStart.OfNewGame(Data.Set) with { Set = Set(0), Chunk = "I08", Local = new Vector3(16, 10, 62) };
        var session = GameSession.Start(game, new Host(), new ZoneEngine(game, streamer), start);
        var scenery = new SceneryRing(game, streamer) { Enabled = ring };
        var from = streamer.Origin(streamer.Find("I09")!) + new Vector3(50, 0, 50);
        var shown = 0;
        scenery.Loaded += (_, map) => shown += map.Record.Objects.OfType<SceneObject>().Count(o => map.IsActive(o.Index));
        for (var tick = 0; tick < 1200; tick++)
        {
            session.Tick(SessionSettings.Original, () => new PlayerInput(new Vector2(0, 1)), true, _ => null);
            var along = tick < 600 ? tick : 1200 - tick;
            streamer.Update(from + new Vector3(0, 0, along));
            scenery.Update();
            if (tick % 300 == 299)
                scenery.Regate();
        }
        Assert.Equal(ring, shown > 0);
        var trek = session.Trek;
        return string.Join("\n", SaveFile.Serialize(game.Save()), trek.Player.Sphere.Position, streamer.World.Count, streamer.Visible!.Count,
            trek.Wanderers.All.Count, string.Join(";", trek.Wanderers.All.Select(w => w.Body.Sphere.Position)), session.Zone.Sounds.Count,
            string.Join(";", streamer.All.Select(c => c.Map.Key)));
    }

    private static (uint, uint, uint, uint, uint) Seeds(GameState game) =>
        (game.Random.Combat.Seed, game.Random.Loot.Seed, game.Random.World.Seed, game.Random.Wander.Seed, game.Random.Cosmetic.Seed);
}
