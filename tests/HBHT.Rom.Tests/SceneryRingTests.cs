using System.Numerics;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.MapObjects;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Scripts;
using HBHT.Core.Trek;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Rom.Tests;

/// <summary>
/// The scenery round the 3×3 on NA 1.0's data (<see cref="SceneryRing"/>): what its chunks show, and under the Draw Distance
/// setting's Extended the Overworld's 5×5, as scenery only, which a game plays the same with or without.
/// </summary>
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
        var ring = new SceneryRing(game, chunks) { Enabled = true, Extended = true };
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
        int Distance(ChunkKey k) => Math.Max(Math.Abs(k.Row - centre.Row), Math.Abs(k.Column - centre.Column));
        // The 5×5's outer 16, and past them what its chunks show, one further.
        var ringOf = Enumerable.Range(-2, 5).SelectMany(r => Enumerable.Range(-2, 5).Select(c => centre with { Row = centre.Row + r, Column = centre.Column + c }))
            .Where(k => Distance(k) == 2 && Data.Set.ChunkAt(chunks.MapSet.Id, k.Row, k.Column) is not null).ToList();
        Assert.InRange(ringOf.Count, 8, 16);
        Assert.Superset(ringOf.ToHashSet(), loaded.Select(m => m.Key).ToHashSet());
        Assert.All(loaded, m => Assert.InRange(Distance(m.Key), 2, 3));
        Assert.All(loaded.Where(m => Distance(m.Key) == 3), m => Assert.Contains(ringOf.Concat(chunks.All.Select(c => c.Map.Key)),
            k => Data.Set.ChunkAt(chunks.MapSet.Id, k.Row, k.Column)!.Shows?.Contains(ChunkRecord.IdOf(chunks.MapSet.Id, m.Key.Row, m.Key.Column)) == true));
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
        var ring = new SceneryRing(game, chunks) { Enabled = true, Extended = true };
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
        var ring = new SceneryRing(game, chunks) { Enabled = true, Extended = true };
        var j08 = chunks.Find("J08")!;
        var lights = j08.Objects.OfType<SceneObject>().Where(o => o.GoesOut).ToList();
        Assert.NotEmpty(lights);
        // I10's 3×3 leaves J08 in the ring, a row down and two columns west.
        var i10 = chunks.Origin(chunks.Find("I10")!) + new Vector3(50, 0, 50);
        chunks.Update(i10);
        ring.Update();
        var shown = ring.Map(ChunkKey.Of(j08))!;
        // Oriana's path lights aren't there until her conversation sets flag 50.
        Assert.All(lights, o => Assert.False(shown.IsActive(o.Index)));

        game.Flags.Set(Ids.Flag(50), true);
        // Brought up to date, as trek's tick does: the ring reads the flags as they're stored, so it waits for that.
        Assert.True(game.Flags.Dirty);
        Assert.False(ring.Regate());
        game.Flags.Process();
        var putOut = lights[0];
        game.Memory.Remember(new Memory(j08.ObjectId(putOut.Index), MemoryKind.PutOut), 0, MapObjectMemory.PutOutLoads);
        var memory = game.Memory.Entries.Select(e => (e.Key, e.Value, e.Lifetime)).ToList();
        // Gated again, as the menu closing does: the lights are there, but for the one remembered as put out.
        Assert.True(ring.Regate());
        shown = ring.Map(ChunkKey.Of(j08))!;
        Assert.False(shown.IsActive(putOut.Index));
        Assert.All(lights.Skip(1), o => Assert.True(shown.IsActive(o.Index)));
        Assert.Equal(memory, game.Memory.Entries.Select(e => (e.Key, e.Value, e.Lifetime)));
        Assert.False(ring.Regate());
    }

    [Fact]
    public void UnderOriginalItDrawsTheChunksTheThreeByThreeShows()
    {
        var game = GameState.NewGame(Data.Set);
        var chunks = new ChunkStreamer(Data.Set, game, new CollisionWorld(), Set(0));
        var ring = new SceneryRing(game, chunks) { Enabled = true };
        // L20's 3×3 has M20, whose scene drew a copy of J19, two rows north of L20: the original's view across the bay.
        chunks.Update(chunks.Origin(chunks.Find("L20")!) + new Vector3(50, 0, 50));
        Assert.True(ring.Update());
        Assert.Equal(1, ring.Reach);
        var centre = chunks.Centre;
        Assert.Contains(ring.All, c => c.Chunk.Name == "J19");
        Assert.All(ring.All, c => Assert.Equal(2, Math.Max(Math.Abs(c.Chunk.Row - centre.Row), Math.Abs(c.Chunk.Column - centre.Column))));

        // Extended reaches the 5×5, with what it shows one further.
        ring.Extended = true;
        Assert.True(ring.Update());
        Assert.Equal(2, ring.Reach);
        Assert.Contains(ring.All, c => Math.Max(Math.Abs(c.Chunk.Row - centre.Row), Math.Abs(c.Chunk.Column - centre.Column)) == 3);

        // Disabled, as out of trek: nothing.
        ring.Enabled = false;
        Assert.True(ring.Update());
        Assert.Empty(ring.All);
    }

    [Fact]
    public void OnTheIslandsOrOffTheOverworldExtendedKeepsTheThreeByThree()
    {
        var game = GameState.NewGame(Data.Set);
        var chunks = new ChunkStreamer(Data.Set, game, new CollisionWorld(), Set(0));
        var ring = new SceneryRing(game, chunks) { Enabled = true, Extended = true };
        // Namer's Isle: Chaos Isle's D22, two rows south, isn't drawn.
        chunks.Update(chunks.Origin(chunks.Find("B22")!) + new Vector3(50, 0, 50));
        ring.Update();
        Assert.Equal(1, ring.Reach);
        Assert.DoesNotContain(ring.All, c => c.Chunk.Name is "D22" or "D23" or "D21");

        // Another map set, Gwernia's interiors among them: the original's 3×3 alone.
        var other = Data.Set.All<MapSetRecord>(RecordTypes.MapSet).First(s => s.Id != Set(0).Id && Data.Set.ChunksIn(s.Id).Count() > 9);
        chunks.Change(other);
        var otherChunks = Data.Set.ChunksIn(other.Id).ToList();
        var middle = otherChunks.OrderBy(c => c.Row).ThenBy(c => c.Column).ElementAt(otherChunks.Count / 2);
        chunks.Update(chunks.Origin(middle) + new Vector3(1, 0, 1));
        ring.Update();
        Assert.Empty(ring.All);

        // An emptied 3×3, as under a cut-scene's stage: nothing.
        chunks.Change(Set(0));
        chunks.Update(chunks.Origin(chunks.Find("I12")!) + new Vector3(50, 0, 50));
        ring.Update();
        Assert.NotEmpty(ring.All);
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
        var scenery = new SceneryRing(game, streamer) { Enabled = ring, Extended = ring };
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
