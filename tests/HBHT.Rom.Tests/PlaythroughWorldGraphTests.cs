using System.Numerics;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Playthrough;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// The playthrough bot's world graph (docs/systems/playthrough.md): routes over the walk graphs of Reach's exploration and the
/// teleporters between map sets, with the gates read against a game, and a pilot that follows a route with the real PlayerMotion.
/// The graphs are kept in a cache under the temp folder, keyed by the plugins' fingerprint, so only the first run explores (about
/// three minutes for the Overworld, a few seconds for each interior).
/// </summary>
public sealed class PlaythroughWorldGraphTests(Na10Import fixture)
{
    private PluginSet Plugins => fixture.Data.Set;

    private static WorldGraph? _graph;
    private static readonly Lock GraphLock = new();

    private WorldGraph Graph
    {
        get
        {
            lock (GraphLock)
                return _graph ??= new WorldGraph(Plugins, Path.Combine(Path.GetTempPath(), "hbht-playthrough-cache"));
        }
    }

    private static RecordId Object(string text) => RecordId.Parse("aidyn:mapobject/" + text);

    // A container in the Overworld's J08, a chunk away from where a new game starts (I08).
    private static readonly RecordId OverworldChest = Object("0/J08/63");

    // A chest in the Gwernia interiors (map set 5), through a door of the Overworld.
    private static readonly RecordId InteriorChest = Object("5/C03/9");

    // A chest in Gwernia castle (map set 4) behind a locked door (flag 19) of O07.
    private static readonly RecordId LockedChest = Object("4/K05/12");

    [Fact]
    public void ARouteInTheOverworldGoesFromANewGamesStartToAContainerInAnotherChunk()
    {
        var state = GameState.NewGame(Plugins);
        var result = Graph.Route(state, OverworldChest);

        Assert.True(result.Found, result.Refusal?.Message);
        var route = result.Route!;
        var (set, startChunk, _) = GameSession.Arrival(Plugins, GameSession.NewGame(Plugins));
        Assert.Equal("I08", startChunk);
        Assert.Equal("J08", Plugins.Get<ChunkRecord>(ChunkRecord.ChunkOf(OverworldChest)).Name);
        var leg = Assert.Single(route.Legs);
        Assert.Equal(set.Id, leg.MapSet);
        Assert.Null(leg.Teleporter);
        Assert.True(leg.Waypoints.Count > 5);
        Assert.InRange(route.Distance, 0, route.UseRange);
        Assert.True(Graph.CanReach(OverworldChest, state));
    }

    [Fact]
    public void ARouteCrossesATeleporterIntoAnInteriorMapSet()
    {
        var state = GameState.NewGame(Plugins);
        var route = Graph.Route(state, InteriorChest).Route;

        Assert.NotNull(route);
        Assert.Equal(2, route.Legs.Count);
        Assert.Equal(RecordId.Decimal(RecordTypes.MapSet, 0), route.Legs[0].MapSet);
        Assert.Equal(RecordId.Decimal(RecordTypes.MapSet, 5), route.Legs[1].MapSet);
        var teleporter = Assert.Single(route.Teleporters);
        var portal = Graph.Portals.Single(p => p.Id == teleporter);
        Assert.Equal(route.Legs[1].MapSet, portal.ToSet);
        Assert.Equal(portal.Arrives, route.Legs[0].Arrives);
        // The way in is open in a new game: no gate on the route is closed.
        Assert.DoesNotContain(route.Gates, g => g.Closed(state.Flags));
        Assert.True(Graph.CanReach(InteriorChest, state));
    }

    [Fact]
    public void ARouteIsRefusedForALockedDoorsFlagAndAllowedOnceItIsSet()
    {
        var state = GameState.NewGame(Plugins);
        var refused = Graph.Route(state, LockedChest);

        Assert.False(refused.Found);
        Assert.Equal(RefusalKind.Gated, refused.Refusal!.Kind);
        var lockGate = Assert.Single(refused.Refusal.Blockers);
        Assert.Equal(GateKind.Locked, lockGate.Kind);
        Assert.Equal(Object("4/O07/1"), lockGate.Object);
        Assert.Equal(RecordId.Decimal(RecordTypes.Flag, 19), lockGate.Flag);
        Assert.False(Graph.CanReach(LockedChest, state));
        // A party that can pick the lock isn't stopped by it.
        Assert.True(Graph.Route(state, LockedChest, new RouteOptions(PickLocks: true)).Found);

        state.Flags.Set(lockGate.Flag!.Value, true);
        var allowed = Graph.Route(state, LockedChest);
        Assert.True(allowed.Found, allowed.Refusal?.Message);
        Assert.Contains(lockGate.Object, allowed.Route!.Gates.Select(g => g.Object));
        Assert.True(Graph.CanReach(LockedChest, state));
    }

    [Fact]
    public void ATeleportersArrivalIsTheCellThePlayerIsSetDownIn()
    {
        // The bridge's door back into Gwernia castle's courtyard arrives on a floor at a whole height (y 4). Snapped from under the
        // floor, it was the twin cell a unit down, a pocket by the wall that no step leaves, and the castle's way out was walled off
        // from the library, the lab and the stairwell between them (4/K07, where 0x035F's backdrop plays).
        var state = GameState.NewGame(Plugins);
        var bridgeDoor = Graph.Portals.Single(p => p.Id == Object("4/M05/0"));
        var arrival = Graph.ArrivalOf(bridgeDoor);
        Assert.NotNull(arrival);
        Assert.Equal(new Reach.Cell(915, 703, 4), Graph.Walk(bridgeDoor.ToSet).Cell(arrival.Value));

        var stairwell = new Place(RecordId.Decimal(RecordTypes.MapSet, 4), new Vector3(1105.69f, 0.007f, 711.46f));
        Assert.True(Graph.CanReach(Object("4/I07/4"), state, stairwell));

        // Standing by the observatory's door (4/O05/1), the feet are a hair over a floor at a whole height: the twin cell a unit down
        // is a pocket of two that lead only into each other, and a way from it got nowhere.
        var observatory = new Place(RecordId.Decimal(RecordTypes.MapSet, 4), new Vector3(1510.031f, 0.0099999f, 504.36f));
        var walk = Graph.Walk(observatory.MapSet);
        Assert.Equal(new Reach.Cell(1510, 504, 0), walk.Cell(walk.SnapStart(observatory.Feet, 6)!.Value));
        Assert.True(Graph.CanReach(Object("4/O05/1"), state, observatory));
        Assert.True(Graph.CanReach(Object("4/E07/2"), state, observatory));

        // Every arrival is a cell some step leaves.
        foreach (var portal in Graph.Portals)
        {
            if (Graph.ArrivalOf(portal) is { } cell)
                Assert.True(Graph.Walk(portal.ToSet).Steps(cell).Length > 0, $"{portal.Id} arrives in a cell no step leaves");
        }
    }

    [Fact]
    public void AnObjectsOwnGatesAreReportedWithoutSearching()
    {
        var state = GameState.NewGame(Plugins);
        var gated = Object("0/A22/13");   // enabled while flag 5552 is set
        var result = Graph.Route(state, gated);

        Assert.Equal(RefusalKind.ObjectGated, result.Refusal!.Kind);
        Assert.Equal(GateKind.Enabled, Assert.Single(result.Refusal.Blockers).Kind);
        Assert.False(Graph.CanReach(gated, state));
        Assert.Equal(RefusalKind.UnknownObject, Graph.Route(state, Object("0/I08/9999")).Refusal!.Kind);
    }

    [Theory]
    [InlineData("0/D14/6")]   // Batrin on the bridge, placed 1.8 above it
    [InlineData("0/Q16/4")]   // Damek in the bog, placed 2.1 under it
    [InlineData("0/C14/10")]  // the Human Bandit north of Port Saiid, 2.8 above the ground
    public void AnNpcOrEncounterIsUsedWhereItsBodyIsSetDownNotWhereItIsPlaced(string name)
    {
        var id = Object(name);
        var chunk = Plugins.Get<ChunkRecord>(ChunkRecord.ChunkOf(id));
        var o = chunk.Object(ChunkRecord.IndexOf(id))!;
        var placed = Plugins.Get<MapSetRecord>(chunk.MapSet).Origin(chunk) + o.Position;
        var walk = Graph.Walk(chunk.MapSet);

        // Placed, it's more than its talking range from every cell the player stood on.
        Assert.DoesNotContain(WorldGraph.StandNear(walk, placed, WorldGraph.PartyTalkRange), c => c.Distance <= WorldGraph.PartyTalkRange - 0.5f);
        var target = Graph.TargetOf(id, chunk, o);
        Assert.Equal(placed.X, target.X);
        Assert.Equal(placed.Z, target.Z);
        Assert.NotEqual(placed.Y, target.Y);
        Assert.NotEmpty(WorldGraph.StandNear(walk, target, WorldGraph.PartyTalkRange));
    }

    [Fact]
    public void TheChaosPartyInShamsuksTowerIsClosedForGoodOnceHeIsDefeated()
    {
        var model = QuestModel.Build(Plugins);
        var game = GameState.NewGame(Plugins);
        var closings = Closings.Find(model, Graph, game);

        // The Chaos party at EhudUnder O03 is there only once Shamsuk is defeated (458), which only his battle at Overworld I08/13 sets;
        // the tower's one way in, the climb at Overworld P17/4, is enabled only while 458 is clear.
        var closed = closings.Why.Select(c => (model.Actions[c.Key].Place, c.Value)).ToList();
        Assert.Contains((Object("12/O03/3"), Ids.Flag(458)), closed);
        Assert.All(closed, c => Assert.Equal(Ids.Flag(458), c.Value));
        Assert.All(closed, c => Assert.StartsWith("aidyn:mapobject/12/O03/", c.Place.ToString(), StringComparison.Ordinal));
        // The tower's chests need no order, and stay: they're open before the battle.
        Assert.DoesNotContain(closed, c => c.Place == Object("12/G03/7"));

        var start = PlanState.From(model, game);
        var targets = CoverGoal.StaticTargets(model, start, game, closings);
        Assert.DoesNotContain(Ids.Flag(1425), targets);
        Assert.Contains(Ids.Flag(3219), targets);
        Assert.Contains(Ids.Flag(1425), CoverGoal.StaticTargets(model, start, game));
    }

    [Fact]
    public void TheWalkGraphsAreKeptInTheCacheUnderTheFingerprint()
    {
        var set = RecordId.Decimal(RecordTypes.MapSet, 3);
        var graph = Graph.Walk(set);
        var path = Path.Combine(Path.GetTempPath(), "hbht-playthrough-cache", "walk-3.bin");
        Assert.True(File.Exists(path));

        var again = WalkGraph.TryLoad(path, Graph.Fingerprint);
        Assert.NotNull(again);
        Assert.Equal((graph.Count, graph.EdgeCount), (again.Count, again.EdgeCount));
        for (var i = 0; i < graph.Count; i += 37)
        {
            Assert.Equal(graph.Cell(i), again.Cell(i));
            Assert.Equal(graph.Steps(i).ToArray(), again.Steps(i).ToArray());
        }
        Assert.Null(WalkGraph.TryLoad(path, "another plugin set"));
        Assert.Equal(Graph.Fingerprint, new WorldGraph(Plugins, Path.GetTempPath()).Fingerprint);
    }

    [Fact]
    public void GraphsKeptAtOnceInOneProcessDoNotCollide()
    {
        // Two world graphs in one process making the same map set's graph from a cold cache keep it at once: each writes a file of its
        // own and renames it over the cache's, while others read it. None throws, and what's left is the graph whole.
        var graph = Graph.Walk(RecordId.Decimal(RecordTypes.MapSet, 3));
        var folder = Path.Combine(Path.GetTempPath(), $"hbht-cache-race-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "walk-3.bin");
        try
        {
            Parallel.For(0, 16, i =>
            {
                if (i % 4 == 3)
                    WalkGraph.TryLoad(path, Graph.Fingerprint);
                else
                    graph.Save(path, Graph.Fingerprint);
            });
            Assert.Equal(graph.Count, WalkGraph.TryLoad(path, Graph.Fingerprint)?.Count);
            Assert.Equal(["walk-3.bin"], Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TeleportersJoinTheMapSetsAndMostAreOpen()
    {
        var portals = Graph.Portals;
        Assert.True(portals.Count > 800);
        Assert.Contains(portals, p => p.FromSet != p.ToSet);
        Assert.Contains(portals, p => p.Gates.Any(g => g.Kind == GateKind.Locked && g.Blocking));
        Assert.Contains(portals, p => p.Gates.Any(g => g.Kind == GateKind.Enabled));
        // A tangible teleporter skips its lock.
        Assert.DoesNotContain(portals, p => p.Tangible && p.Gates.Any(g => g.Kind is GateKind.Locked or GateKind.Secret or GateKind.Trapped));
    }

    // Collision toggles.

    // The Jundar gate (Overworld P24, tripwire 1): its toggles open once flag 480 enables the tripwire, and only it joins the mainland
    // to the south-east island.
    private static readonly RecordId JundarGate = Object("0/P24/1");

    private static readonly RecordId Riddle = RecordId.Decimal(RecordTypes.Flag, 480);

    /// <summary>A map object on the south-east island, beyond the gate: the save point in O25, which has no gates of its own.</summary>
    private static RecordId IslandObject() => Object("0/O25/135");

    [Fact]
    public void EveryCollisionToggleIsATogglesTripwireWithItsGates()
    {
        var toggles = Graph.Toggles;
        Assert.Equal(9, toggles.Count);
        var gate = Assert.Single(toggles, t => t.Id == JundarGate);
        Assert.Equal(Riddle, Assert.Single(gate.Gates).Flag);
        // The Overworld's graph has gated steps across the gate, which a new game's ground alone lacks.
        var walk = Graph.Walk(RecordId.Decimal(RecordTypes.MapSet, 0));
        Assert.Contains(JundarGate, walk.Toggles);
        Assert.True(walk.GatedCount > 0);
    }

    [Fact]
    public void ARouteOntoTheSouthEastIslandNeedsTheJundarGatesFlag()
    {
        var state = GameState.NewGame(Plugins);
        var island = IslandObject();
        // From a new game the way out of the start's ground is shut too (I08/2, enabled by 458): open the rest so only the gate is left.
        foreach (var other in Graph.Route(state, island).Refusal!.Blockers.Where(b => b.Object != JundarGate))
            state.Flags.Set(other.Flag!.Value, !other.Invert);
        var refused = Graph.Route(state, island);

        Assert.False(refused.Found);
        Assert.Equal(RefusalKind.Gated, refused.Refusal!.Kind);
        var blocker = Assert.Single(refused.Refusal.Blockers);
        Assert.Equal(JundarGate, blocker.Object);
        Assert.Equal(Riddle, blocker.Flag);
        Assert.False(Graph.CanReach(island, state));
        var reach = new WorldReach(Graph, state, Graph.PlaceOf(state));
        Assert.Equal(new Literal[] { new FlagIs(Riddle, true) }, reach.Gates(island));

        state.Flags.Set(Riddle, true);
        var allowed = Graph.Route(state, island);
        Assert.True(allowed.Found, allowed.Refusal?.Message);
        Assert.Contains(JundarGate, allowed.Route!.Legs.SelectMany(l => l.Toggles ?? []));
        Assert.True(Graph.CanReach(island, state));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void APilotWalksThroughTheJundarGateOnceItsFlagIsSet()
    {
        var state = GameState.NewGame(Plugins);
        state.Flags.Set(Riddle, true);
        // From the mainland a little way off the gate, beyond its tripwire's radius: ground a new game's start reaches with the
        // toggles shut.
        var overworld = RecordId.Decimal(RecordTypes.MapSet, 0);
        var gate = Graph.Toggles.Single(t => t.Id == JundarGate);
        var walk = Graph.Walk(overworld);
        var mainland = Graph.Flood(Graph.PlaceOf(GameState.NewGame(Plugins)), way => way is Toggle ? null : 0);
        var start = walk.Near(gate.At.X, gate.At.Z, 30).Where(i => mainland.CellCost(overworld, i) is not null)
            .Select(i => (Cell: i, Distance: Vector3.Distance(walk.Center(i), gate.At))).Where(c => c.Distance > gate.Range + 2)
            .MinBy(c => c.Distance).Cell;
        var (reached, why) = Follow(state, IslandObject(), from: new Place(overworld, walk.Center(start)));
        Assert.True(reached, why);
    }

    // Following a route.

    [Fact]
    [Trait("Category", "Slow")]
    public void APilotSteeringAtTheWaypointsWithThePlayersMotionArrivesWithinUseRangeOfAContainer()
    {
        var (reached, why) = Follow(GameState.NewGame(Plugins), OverworldChest);
        Assert.True(reached, why);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void APilotFollowsARouteThroughATeleporterIntoAnInterior()
    {
        var (reached, why) = Follow(GameState.NewGame(Plugins), InteriorChest);
        Assert.True(reached, why);
    }

    private static void Trip(GameState state, ChunkStreamer chunks, CollisionWorld world, Vector3 sphere)
    {
        if (chunks.MapAt(sphere) is not { } map)
            return;
        foreach (var o in map.Record.Objects.OfType<TripwireObject>())
        {
            if (o.Does is ChangeCollisionAction change && o.Tangible && map.IsActive(o.Index) && Vector3.Distance(o.Position, sphere - map.Origin) <= o.Radius
                && (o.Enabled is not { } enabled || state.Flags.IsSet(enabled.Flag) != enabled.Invert))
                world.ChangeToggles(map.Key, change.Toggle, change.KeepFlags, change.AddFlags);
        }
    }

    /// <summary>
    /// Walks to a map object as the bot's pilot would, with the real PlayerMotion in the real collision: each tick the stick is pushed
    /// up with the camera turned to the next waypoint, which is passed within 0.8 of it. A pilot that stops getting nearer (the
    /// original's sphere physics isn't a steering a cell graph can promise: it runs into a pocket where the recorded run, carried
    /// by its speed, went over a lip) replans from where it stands with the place it stuck at avoided, as the bot's does. Whether he
    /// ends within use range of the object, in how many replans.
    /// </summary>
    /// <remarks>
    /// A tripwire that changes collision goes off as trek's does (<c>MapObjectUse.Touch</c>): in the chunk he's in, active, enabled by
    /// the game's flags and within its radius of his sphere, it changes its toggles.
    /// </remarks>
    private (bool Reached, string Why) Follow(GameState state, RecordId target, int maxReplans = 8, Place? from = null)
    {
        var actor = Plugins.Get<ActorRecord>(Reach.Alaron);
        var clips = ActorClips.For(Plugins, actor.Scene, actor.RootTransform)!;
        var place = from ?? Graph.PlaceOf(state);
        var avoid = new List<Place>();
        var replans = 0;
        // Where the player is set down from, and the height to set him near, when he arrives in a map set; none once he is walking.
        Vector3? arrival = place.Feet + new Vector3(0, PlayerMotion.Radius, 0);
        float? near = null;
        CollisionWorld? world = null;
        ChunkStreamer? chunks = null;
        PlayerMotion? player = null;
        ActorMotion animation = new(actor, clips, new Lcg(0));
        var way = Vector2.UnitY;
        var teleports = 0;
        while (true)
        {
            var result = Graph.Route(state, place, target, new RouteOptions(Avoid: [.. avoid]));
            if (!result.Found)
                return (false, $"no route from {place} after {replans} replans: {result.Refusal!.Message}");
            var route = result.Route!;
            var leg = route.Legs[0];
            var set = Plugins.Get<MapSetRecord>(leg.MapSet);
            if (arrival is { } at)
            {
                world = new CollisionWorld();
                chunks = new ChunkStreamer(Plugins, state, world, set);
                chunks.Update(at);
                var (ground, grounded) = Reach.SetDownAt(world, at, near);
                if (!grounded)
                    return (false, $"not set down at {at} in {leg.MapSet}");
                player = new PlayerMotion(ground, way) { Movement = MovementMode.Original, Pace = 1, Rules = ModernRules.Default };
                arrival = null;
            }
            var stuck = false;
            foreach (var waypoint in leg.Waypoints)
            {
                var (ticks, best, since) = (0, float.MaxValue, 0);
                while (true)
                {
                    var feet = player!.Feet;
                    var across = new Vector2(waypoint.X - feet.X, waypoint.Z - feet.Z);
                    if (across.Length() < 0.8f && MathF.Abs(waypoint.Y - feet.Y) < 2)
                        break;
                    if (player.FellOut)
                        return (false, $"fell out of the world at {feet} going to {waypoint}");
                    // Not getting nearer for a second and a half.
                    if (across.Length() < best - 0.2f)
                        (best, since) = (across.Length(), ticks);
                    else if (ticks - since > 90 || ticks > 3000)
                    {
                        avoid.Add(new Place(leg.MapSet, waypoint));
                        stuck = true;
                        break;
                    }
                    ticks++;
                    way = across.Length() > 1e-3f ? Vector2.Normalize(across) : way;
                    player.Step(world!, new MoveInput(Vector2.UnitY), way, animation);
                    chunks!.Update(player.Sphere.Position);
                    Trip(state, chunks, world!, player.Sphere.Position);
                }
                if (stuck)
                    break;
            }
            if (stuck)
            {
                if (++replans > maxReplans)
                    return (false, $"{replans} replans: stuck at {player!.Feet}, avoiding {string.Join(' ', avoid.Select(a => a.Feet.ToString("0.0")))}");
                place = new Place(leg.MapSet, player!.Feet);
                continue;
            }
            var centre = player!.Sphere.Position;
            if (leg.Teleporter is { } teleporter)
            {
                var portal = Graph.Portals.Single(p => p.Id == teleporter);
                var distance = Vector3.Distance(centre, portal.At);
                if (distance > portal.Range)
                    return (false, $"{distance:0.00} from {teleporter}, range {portal.Range}");
                teleports++;
                place = new Place(portal.ToSet, portal.Arrives - new Vector3(0, PlayerMotion.Radius, 0));
                arrival = portal.Arrives;
                near = portal.Arrives.Y - PlayerMotion.Radius;
                avoid.Clear();
                continue;
            }
            var chunk = Plugins.Get<ChunkRecord>(ChunkRecord.ChunkOf(target));
            var at2 = set.Origin(chunk) + chunk.Object(ChunkRecord.IndexOf(target))!.Position;
            // On to the object for a second or two, as the pilot goes on to it once the route is walked.
            for (var t = 0; t < 120 && Vector3.Distance(player.Sphere.Position, at2) > route.UseRange; t++)
            {
                var across = new Vector2(at2.X - player.Feet.X, at2.Z - player.Feet.Z);
                way = across.Length() > 1e-3f ? Vector2.Normalize(across) : way;
                player.Step(world!, new MoveInput(Vector2.UnitY), way, animation);
                chunks!.Update(player.Sphere.Position);
            }
            centre = player.Sphere.Position;
            var end = Vector3.Distance(centre, at2);
            return (end <= route.UseRange, $"{replans}r {teleports}t ends {end:0.00} of {route.UseRange}");
        }
    }
}
