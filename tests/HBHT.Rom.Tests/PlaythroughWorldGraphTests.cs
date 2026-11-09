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

    /// <summary>
    /// Walks to a map object as the bot's pilot would, with the real PlayerMotion in the real collision: each tick the stick is pushed
    /// up with the camera turned to the next waypoint, which is passed within 0.8 of it. A pilot that stops getting nearer (the
    /// original's sphere physics isn't a steering a cell graph can promise: it runs into a pocket where the recorded run, carried
    /// by its speed, went over a lip) replans from where it stands with the place it stuck at avoided, as the bot's does. Whether he
    /// ends within use range of the object, in how many replans.
    /// </summary>
    private (bool Reached, string Why) Follow(GameState state, RecordId target, int maxReplans = 8)
    {
        var actor = Plugins.Get<ActorRecord>(Reach.Alaron);
        var clips = ActorClips.For(Plugins, actor.Scene, actor.RootTransform)!;
        var place = Graph.PlaceOf(state);
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
                chunks = new ChunkStreamer(Plugins, GameState.NewGame(Plugins), world, set);
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
            var end = Vector3.Distance(centre, at2);
            return (end <= route.UseRange, $"{replans}r {teleports}t ends {end:0.00} of {route.UseRange}");
        }
    }
}
