using System.Collections.Concurrent;
using System.Numerics;
using HBHT.Core;
using HBHT.Core.Chunks;
using HBHT.Core.MapObjects;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Core.Trek;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>Monster parties and their wanderers: what NA 1.0's data holds, and the bodies a loaded chunk gives them.</summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class WandererTests(Na10Import fixture)
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<CollisionTriangle>> Collisions = new();

    private static readonly RecordId Overworld = RecordId.Parse("aidyn:mapset/0");

    /// <summary>A chunk of another map set, for a player no wanderer shares a chunk with.</summary>
    private static readonly ChunkKey Far = Worlds.Chunk(mapSet: 1);

    private Na10Import.Loaded Data => fixture.Data;

    private MapSetRecord Set => Data.MapSets.Single(s => s.Id == Overworld);

    private IReadOnlyList<CollisionTriangle> Collision(MapRecord map) =>
        Collisions.GetOrAdd(Data.Set.FindAsset(map.Id, "glb")!, path => CollisionMesh.Read(File.ReadAllBytes(path)));

    private MapRecord Chunk(int row, int column) => Data.Set.Get<MapRecord>(Data.Chunk(new ChunkRef(Overworld, row, column))!.Map);

    private static WanderNodeObject? HomeOf(MapRecord map, MonsterPartyObject party) =>
        party.Home is { } home && MapRecord.MapOf(home) == map.Id ? map.Objects.FirstOrDefault(o => o.Index == MapRecord.IndexOf(home)) as WanderNodeObject : null;

    private static bool HasMovingHome(MapRecord map, MonsterPartyObject party) => HomeOf(map, party) is { } home && (home.Flags & WanderFlags.Move) != 0;

    /// <summary>A chunk of the Overworld loaded on a new game, with its collision in a world and the wanderers it gives.</summary>
    private sealed class Loaded
    {
        public required MapRecord Map { get; init; }
        public required LoadedMap Chunk { get; init; }
        public required CollisionWorld World { get; init; }
        public required Wanderers Wanderers { get; init; }
        public required IReadOnlyList<Wanderer> Added { get; init; }
        public required GameState Game { get; init; }
    }

    private Loaded Load(int row, int column, GameState? game = null, CollisionWorld? world = null, Wanderers? wanderers = null)
    {
        var map = Chunk(row, column);
        var origin = Set.Origin(row, column);
        game ??= GameState.NewGame(Data.Set);
        world ??= new CollisionWorld();
        wanderers ??= new Wanderers(Data.Set);
        var key = new ChunkKey(Set.Id, row, column);
        world.Add(key, Collision(map), origin);
        var loaded = LoadedMap.Activate(map, key, game.Flags, game.Memory, game.Party, new Lcg(), origin);
        var added = wanderers.Load(loaded, Set.CellSize, world, game.Memory);
        return new Loaded { Map = map, Chunk = loaded, World = world, Wanderers = wanderers, Added = added, Game = game };
    }

    private static IEnumerable<MonsterPartyObject> Active(Loaded l) =>
        l.Map.Objects.OfType<MonsterPartyObject>().Where(p => l.Chunk.IsActive(p.Index) && HasMovingHome(l.Map, p));

    [Fact]
    public void EveryMonsterPartysHomeIsAMovingWanderNodeOfItsOwnMap()
    {
        var parties = Data.Of<MonsterPartyObject>().ToList();
        Assert.Equal(1079, parties.Count);
        foreach (var (id, map, party) in parties)
        {
            Assert.NotNull(party.Home);
            Assert.Equal(map.Id, MapRecord.MapOf(party.Home!.Value));
            var home = HomeOf(map, party);
            Assert.True(home is not null, $"{id}: home {party.Home} isn't a wander node");
            Assert.True((home!.Flags & WanderFlags.Move) != 0, $"{id}: home {party.Home} doesn't move: {home.Flags}");
        }
    }

    [Fact]
    public void EveryMonsterPartysEntityHasABodyWithAnActor()
    {
        var parties = Data.Of<MonsterPartyObject>().ToList();
        Assert.All(parties, p =>
        {
            var body = Data.Set.TryGet<DbRecord>(p.Object.Entity)?.Body;
            Assert.True(body is not null, $"{p.Id}: {p.Object.Entity} has no body");
            Assert.True(Data.Set.TryGet<ActorRecord>(body!.Actor) is not null, $"{p.Id}: {body.Actor} isn't an actor");
            Assert.True(body.Radius > 0 && body.Scale > 0 && body.Height > 0, $"{p.Id}: {body}");
        });
    }

    [Fact]
    public void MostMonsterPartiesWalkToTheirNodes()
    {
        var parties = Data.Of<MonsterPartyObject>().ToList();
        Assert.Equal(990, parties.Count(p => (p.Object.Flags & PartyFlags.Walks) != 0));
    }

    [Fact]
    public void ChunkM07GivesABodyToEachActiveMonsterPartyWhoseHomeMoves()
    {
        var l = Load(13, 7);
        var expected = Active(l).ToList();
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Select(p => p.Index), l.Added.Select(w => w.Party.Index));
        Assert.Equal(l.Added, l.Wanderers.All);
        CheckBodies(l);
    }

    [Fact]
    public void TheStartChunkGivesItsBodiesToo()
    {
        var l = Load(9, 8);
        var expected = Active(l).ToList();
        Assert.Equal(expected.Select(p => p.Index), l.Added.Select(w => w.Party.Index));
        CheckBodies(l);
    }

    private void CheckBodies(Loaded l)
    {
        var bounds = (Min: new Vector2(l.Chunk.Origin.X, l.Chunk.Origin.Z) + new Vector2(0.05f), Max: new Vector2(l.Chunk.Origin.X, l.Chunk.Origin.Z) + new Vector2(Set.CellSize - 0.05f));
        foreach (var w in l.Added)
        {
            var look = Data.Set.Get<DbRecord>(w.Party.Entity).Body!;
            Assert.Equal(look, w.Look);
            Assert.Equal(look.Radius, w.Body.Sphere.Radius);
            Assert.Equal(look.Scale, w.Body.Scale);
            var at = l.Chunk.Origin + w.Party.Position;
            Assert.Equal(at.X, w.Body.Sphere.Position.X);
            Assert.Equal(at.Z, w.Body.Sphere.Position.Z);
            var dy = w.Body.Sphere.Position.Y - (at.Y + look.Radius);
            Assert.InRange(Math.Abs(dy), 0, 0.6f);
            Assert.NotNull(w.Body.Bounds);
            var (min, max) = w.Body.Bounds!.Value;
            Assert.Equal(bounds.Min, min);
            Assert.Equal(bounds.Max, max);
            Assert.InRange(w.Body.Sphere.Position.X, min.X, max.X);
            Assert.InRange(w.Body.Sphere.Position.Z, min.Y, max.Y);
            Assert.Equal(l.Chunk, w.Map);
        }
    }

    [Fact]
    public void UnloadingOneChunkRemovesOnlyItsWanderers()
    {
        var game = GameState.NewGame(Data.Set);
        var world = new CollisionWorld();
        var wanderers = new Wanderers(Data.Set);
        var m07 = Load(13, 7, game, world, wanderers);
        var i08 = Load(9, 8, game, world, wanderers);
        Assert.NotEmpty(m07.Added);
        Assert.NotEmpty(i08.Added);
        Assert.Equal(m07.Added.Count + i08.Added.Count, wanderers.All.Count);

        var gone = wanderers.Unload(m07.Chunk);
        Assert.Equal(m07.Added, gone);
        Assert.Equal(i08.Added, wanderers.All);
        Assert.Empty(wanderers.Unload(m07.Chunk));
    }

    [Fact]
    public void AWandererFurtherFromItsNodeThanItsRadiusSetsOffAndOneWithinItStays()
    {
        int far = 0, near = 0;
        foreach (var (row, column) in new[] { (13, 7), (9, 8) })
        {
            var l = Load(row, column);
            foreach (var w in l.Added)
            {
                var node = w.Node!;
                var distance = Vector2.Distance(new Vector2(w.Body.Sphere.Position.X, w.Body.Sphere.Position.Z), w.Goal);
                if (distance > node.WanderRadius)
                {
                    far++;
                    Assert.True(w.Body.Moving, $"{l.Map.Id} party {w.Party.Index}: {distance} from node radius {node.WanderRadius}");
                    Assert.True(w.Body.Steered);
                    Assert.Equal((w.Party.Flags & PartyFlags.Walks) != 0, w.Body.Walks);
                    Assert.Equal(w.Goal, w.Body.Goal);
                    Assert.Equal(node.WanderRadius, w.Body.GoalRadius);
                }
                else
                {
                    near++;
                    Assert.False(w.Body.Moving, $"{l.Map.Id} party {w.Party.Index}: {distance} from node radius {node.WanderRadius}");
                }
            }
        }
        Assert.True(far > 0 && near > 0);
    }

    [Fact]
    public void AWandererHeadingHomeArrivesWithinItsNodesRadiusAndStops()
    {
        var player = new CollisionSphere { Position = new Vector3(500, 0, 500) };
        var motion = new SteadyMotion(0.02f);
        var arrived = 0;
        foreach (var (row, column, party) in Heading())
        {
            var wanderers = new Wanderers(Data.Set);
            var fresh = Load(row, column, wanderers: wanderers);
            var w = fresh.Added.Single(a => a.Party.Index == party);
            var node = w.Node!;
            var ticks = 0;
            while (w.Body.Steered && ticks < 2000)
            {
                wanderers.Step(fresh.World, player, player.Position, Far, w.Body.Sphere.Position, _ => motion);
                ticks++;
            }
            var distance = Vector2.Distance(new Vector2(w.Body.Sphere.Position.X, w.Body.Sphere.Position.Z), w.Goal);
            Assert.False(w.Body.Steered, $"{fresh.Map.Id} party {w.Party.Index} is still going after {ticks} ticks, {distance} from its goal");
            Assert.True(distance < node.WanderRadius, $"{fresh.Map.Id} party {w.Party.Index} stopped {distance} from its goal");
            arrived++;
        }
        Assert.True(arrived >= 6);
    }

    /// <summary>The 3x3 chunks around a chunk of the Overworld, those that exist, loaded together on a new game.</summary>
    private (Wanderers Wanderers, CollisionWorld World, GameState Game) LoadBlock(int row, int column)
    {
        var game = GameState.NewGame(Data.Set);
        var world = new CollisionWorld();
        var wanderers = new Wanderers(Data.Set);
        for (var r = row - 1; r <= row + 1; r++)
        {
            for (var c = column - 1; c <= column + 1; c++)
            {
                if (Data.Chunk(new ChunkRef(Overworld, r, c)) is not null)
                    Load(r, c, game, world, wanderers);
            }
        }
        return (wanderers, world, game);
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    /// <summary>
    /// The blocks around M07 (7 chunks, 14 wanderers: 8 hunters and 6 talkers) and I08 (6 chunks, 17
    /// wanderers: 16 hunters and 1 talker) wander with nobody near: they choose new goals as their timers
    /// run out, go on from node to node, stay inside their chunks, and none chases.
    /// </summary>
    [Fact]
    public void WanderersWithNobodyNearChooseNewGoalsAndStayInTheirChunks()
    {
        var motion = new SteadyMotion(0.05f);
        var player = new CollisionSphere { Position = new Vector3(5000, 0, 5000) };
        var nodeChanges = 0;
        foreach (var (row, column, count) in new[] { (13, 7, 14), (9, 8, 17) })
        {
            var (wanderers, world, _) = LoadBlock(row, column);
            Assert.Equal(count, wanderers.All.Count);
            var random = new Lcg(1234);
            var node = wanderers.All.ToDictionary(w => w, w => w.Node);
            var goal = wanderers.All.ToDictionary(w => w, w => w.Goal);
            var chose = new HashSet<Wanderer>();
            var start = wanderers.All.ToDictionary(w => w, w => w.Body.Sphere.Position);
            // The camera's aim goes from one wanderer to the next every 600 ticks, so that each has its turn
            // to walk: only the bodies within 30 of it do.
            for (var tick = 0; tick < 600 * count; tick++)
            {
                var aim = wanderers.All[tick / 600].Body.Sphere.Position;
                Assert.Empty(wanderers.Tick(world, player.Position, Body.Walk, null, random));
                wanderers.Step(world, player, player.Position, Far, aim, _ => motion);
                foreach (var w in wanderers.All)
                {
                    Assert.False(w.Chasing, $"{w.Map.Record.Id} party {w.Party.Index} chases at tick {tick}");
                    if (w.Goal != goal[w])
                    {
                        chose.Add(w);
                        goal[w] = w.Goal;
                    }
                    if (w.Node != node[w])
                    {
                        nodeChanges++;
                        node[w] = w.Node;
                    }
                    var (min, max) = w.Body.Bounds!.Value;
                    var at = Flat(w.Body.Sphere.Position);
                    Assert.True(at.X >= min.X && at.X <= max.X && at.Y >= min.Y && at.Y <= max.Y,
                        $"{w.Map.Record.Id} party {w.Party.Index} left its chunk at tick {tick}: {at}");
                }
            }
            // Some nodes, as 0x0FB6's 211, link only to themselves, which scatter a new goal without changing the node.
            Assert.True(chose.Count >= count / 2, $"{row},{column}: only {chose.Count} of {wanderers.All.Count} chose a new goal");
            Assert.True(wanderers.All.Count(w => Vector3.Distance(w.Body.Sphere.Position, start[w]) > 1) >= count / 2);
        }
        Assert.True(nodeChanges > 0);
    }

    /// <summary>The first hunter of the block with the player placed 9 ahead of it, where it looks from 3 ahead.</summary>
    private static (Wanderer Hunter, Vector3 Player) HunterWithPlayerAhead(Wanderers wanderers)
    {
        var hunter = wanderers.All.First(w => w.Hunts);
        var at = hunter.Body.Sphere.Position;
        var player = at + new Vector3(hunter.Body.Heading.X, 0, hunter.Body.Heading.Y) * 9;
        return (hunter, player);
    }

    [Fact]
    public void AHunterNoticesAWalkingPlayerBesideItAndChasesHimButNotOneStandingWithNoParty()
    {
        var (wanderers, world, game) = LoadBlock(13, 7);
        var (hunter, player) = HunterWithPlayerAhead(wanderers);
        Assert.Equal(10, hunter.Perception);

        // Standing with nobody to hide him (the party's Stealth is 0 with no party), he isn't noticed.
        wanderers.Tick(world, player, Body.Idle, null, new Lcg());
        Assert.False(hunter.Chasing);

        var ambushes = wanderers.Tick(world, player, Body.Walk, game.Party, new Lcg());
        Assert.DoesNotContain(hunter, ambushes);
        Assert.True(hunter.Chasing);
        Assert.True(hunter.Body.Moving);
        Assert.False(hunter.Body.Walks);
        Assert.Equal(Flat(player), hunter.Body.Goal);
        Assert.Equal(2, hunter.Body.GoalRadius);
    }

    [Fact]
    public void AHunterChasingThePlayerCatchesHimAndAmbushesHim()
    {
        var (wanderers, world, game) = LoadBlock(13, 7);
        var (hunter, player) = HunterWithPlayerAhead(wanderers);
        var sphere = new CollisionSphere { Radius = 0.5f, Position = player };
        var motion = new SteadyMotion(0.03f);
        var ambushed = false;
        for (var tick = 0; tick < 1500 && !ambushed; tick++)
        {
            ambushed = wanderers.Tick(world, sphere.Position, Body.Walk, game.Party, new Lcg()).Contains(hunter);
            wanderers.Step(world, sphere, sphere.Position, Far, sphere.Position, _ => motion);
        }
        Assert.True(ambushed);
        Assert.False(hunter.Hunts);
        Assert.False(hunter.Body.Moving);
        Assert.True(Vector3.Distance(hunter.Body.Sphere.Position, sphere.Position) < Wanderer.AmbushDistance);
    }

    /// <summary>The wanderers of M07 and I08 that set off for their nodes on loading: chunk row and column, and party index.</summary>
    private IEnumerable<(int Row, int Column, int Party)> Heading() =>
        new[] { (13, 7), (9, 8) }.SelectMany(c => Load(c.Item1, c.Item2).Added.Where(w => w.Body.Moving).Select(w => (c.Item1, c.Item2, w.Party.Index))).ToList();

    /// <summary>
    /// Only the 3x3 around H13 has more than <see cref="Wanderers.OriginalLimit"/> (44), past what the original has room
    /// for if the player stood in H13 on a new game; the port has no limit, so all of them get a body.
    /// </summary>
    [Fact]
    public void OnlyTheBlockAroundH13HoldsMoreMonsterPartiesThanWanderersCanBe()
    {
        var game = GameState.NewGame(Data.Set);
        var counts = new Dictionary<(int Row, int Column), int>();
        foreach (var entry in Set.Chunks)
        {
            var map = Data.Set.Get<MapRecord>(entry.Map);
            var loaded = LoadedMap.Activate(map, ChunkKey.Of(Set, entry), game.Flags, game.Memory, game.Party, new Lcg(), Set.Origin(entry.Row, entry.Column));
            counts[(entry.Row, entry.Column)] = map.Objects.OfType<MonsterPartyObject>().Count(p => loaded.IsActive(p.Index) && HasMovingHome(map, p));
        }

        int Block(int row, int column) => Enumerable.Range(row - 1, 3).Sum(r => Enumerable.Range(column - 1, 3).Sum(c => counts.GetValueOrDefault((r, c))));

        var over = counts.Keys.Where(k => Block(k.Row, k.Column) > Wanderers.OriginalLimit).ToList();
        Assert.Equal([(8, 13)], over);
        Assert.Equal(44, Block(8, 13));
        Assert.Equal(44, counts.Keys.Max(k => Block(k.Row, k.Column)));
    }
}
