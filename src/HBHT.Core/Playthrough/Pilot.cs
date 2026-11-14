using System.Numerics;
using HBHT.Core.Items;
using HBHT.Core.MapObjects;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>How the pilot walks and when it gives up.</summary>
/// <param name="StallTicks">How long the player may go without getting <paramref name="Progress"/> nearer what he steers for before it's a stall.</param>
/// <param name="Progress">How much nearer counts as getting nearer, in units.</param>
/// <param name="WaypointReach">How near across the ground a waypoint is passed (and within 2 in height).</param>
/// <param name="MaxStalls">Stalls allowed in a step before it's stuck.</param>
/// <param name="MaxReplans">Routes asked for after the first, for any reason, before it's stuck.</param>
/// <param name="SettleTicks">How long trek must have had the controls, after the use, for its consequences to have played out.</param>
/// <param name="UsePatience">How long he may stand within use range with A not reaching the object before it's not used.</param>
/// <param name="DirectRange">How near across the ground an npc's or encounter's body must be for him to steer straight at it rather than along the route.</param>
/// <param name="JumpDistance">How far the player may move in one tick before it counts as being moved, and the route is asked for again.</param>
/// <param name="PickLocks">Whether routes may take locked doors (<see cref="RouteOptions.PickLocks"/>).</param>
/// <param name="Rest">What makes the party camp when it's hurt, checked each tick the player has the controls; never if null.</param>
/// <param name="Triggers">
/// The story triggers: one that plays the same way twice on a step, other than the step's own, has turned the player back, and the step
/// fails <see cref="StepFailure.Blocked"/>, teaching the triggers that way. Not watched if null.
/// </param>
/// <param name="Dress">What puts better gear on the party from the inventory, checked as <paramref name="Rest"/> is; never if null.</param>
/// <param name="Waits">Whether a way shut only by gates the hour opens is waited for where the player stands; never if null.</param>
/// <param name="SidestepTicks">
/// How long the player steps aside after a stall before the way is asked for again: to one side, the other, back, and between, in turn.
/// </param>
/// <param name="KeepClear">
/// Whether routes keep out of the script objects walking into would set off, other than the step's own, while a route that does is
/// to be had: the step plays what it was asked to, and not the story's cut-scenes on the way.
/// </param>
/// <param name="Shun">
/// Story triggers no route may cross, other than the step's own: a cover branch's, whose crossing lost its targets in its parent (a
/// step out of Gwernia castle playing the story on). A step with no way round them fails <see cref="StepFailure.NoRoute"/>.
/// </param>
public sealed record PilotOptions(
    int StallTicks = 90,
    float Progress = 0.2f,
    float WaypointReach = 0.8f,
    int MaxStalls = 10,
    int MaxReplans = 40,
    int SettleTicks = 60,
    int UsePatience = 300,
    float DirectRange = 10,
    float JumpDistance = 3,
    bool PickLocks = false,
    bool KeepClear = true,
    Resting? Rest = null,
    StoryTriggers? Triggers = null,
    Outfitting? Dress = null,
    int SidestepTicks = 30,
    Func<RouteRefusal, bool>? Waits = null,
    IReadOnlyCollection<RecordId>? Shun = null)
{
    public static readonly PilotOptions Default = new();
}

/// <summary>
/// The playthrough bot's fourth layer (docs/systems/playthrough.md): plays one step of a plan in a <see cref="PlaythroughHost"/>'s real
/// session. It routes from where the player stands over the <see cref="WorldGraph"/>, walks the route with the stick turned through the
/// trek camera's look, takes the teleporters on it by walking in or pressing A, and once within range uses the object: presses A when A
/// would reach it, walks into it, or walks up to the npc's body where it wanders now and talks. It asks for a new route after a stall,
/// avoiding where it stuck, and after anything that took the controls or moved the player. The step is done once trek has had the
/// controls for <see cref="PilotOptions.SettleTicks"/> after the use, so whatever it started (a conversation, cut-scenes, a battle, the
/// loot) has played out; the host's other policies play those. It checks the flags, members and items against what the step expected.
/// </summary>
public sealed class Pilot : IInputPolicy
{
    private readonly WorldGraph _graph;
    private readonly Dictionary<RecordId, Portal> _portals;

    public Pilot(WorldGraph graph, PilotOptions? options = null)
    {
        _graph = graph;
        Options = options ?? PilotOptions.Default;
        _portals = graph.Portals.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
    }

    public PilotOptions Options { get; }

    public WorldGraph Graph => _graph;

    /// <summary>The step being played, and what the pilot knows of it; null between steps.</summary>
    private Walk? _walk;

    /// <summary>How many times its budget a step may take all told, battles, cut-scenes and menus included.</summary>
    public const int WholeBudget = 4;

    /// <summary>
    /// Plays <paramref name="step"/> to its end: the object used and its consequences played out, or a failure. The host's input and
    /// conversation policies are the pilot's while it runs, and are put back after. <paramref name="mostTicks"/> counts the ticks the
    /// player has the controls; the step may take <see cref="WholeBudget"/> times it all told.
    /// </summary>
    public StepOutcome Run(PlaythroughHost host, PilotStep step, long mostTicks)
    {
        var before = GameSnapshot.Take(host.Game);
        var (input, conversations, menus) = (host.Input, host.Conversations, host.Menus);
        var choices = new WantedChoices(step.Choices ?? [],
            step.Consequences.Count > 0 ? new WantedChoices(step.Consequences, conversations) : conversations, step.Ways);
        var (start, transitions, pages, battles) = (host.Clock.Ticks, host.Transitions.Count, host.Pages.Count, host.BattlesFought.Count);
        var walk = _walk = new Walk(step);
        host.Input = this;
        host.Conversations = choices;
        if (step.Disarms is not null)
            host.Menus = new TrapTiming(walk, menus);
        try
        {
            Begin(walk);
            var settle = 0;
            var controlled = 0L;
            var hadControl = host.PlayerHasControl;
            while (walk.Failure is null)
            {
                if (host.End != HostEnd.None)
                {
                    walk.Fail(StepFailure.GameOver, $"the game ended: {host.End}");
                    break;
                }
                // Backed out of the step's trap for its roll: saved and loaded where he stands, and the trap tried again.
                if (walk.Reroll && host.PlayerHasControl)
                {
                    walk.Reroll = false;
                    if (host.SaveAndLoad())
                    {
                        walk.Used = false;
                        walk.Replan = true;
                    }
                }
                var control = host.PlayerHasControl;
                // Back from a conversation, a cut-scene, a battle, a menu or a teleport: the player may be anywhere.
                if (control && !hadControl && !walk.Used)
                    walk.Replan = true;
                hadControl = control;
                if (walk.Used)
                {
                    settle = control ? settle + 1 : 0;
                    if (settle >= Options.SettleTicks)
                        break;
                }
                // The budget is the player's: a long way through wanderers' battles, each some minutes, isn't a step gone wrong. All told
                // a step may take a few times it.
                // Waiting for the hour a gate opens at isn't the step's budget either.
                if (control && walk.WaitTicks == 0)
                    controlled++;
                if (controlled >= mostTicks || host.Clock.Ticks - start - walk.Waited >= mostTicks * WholeBudget)
                {
                    walk.Fail(StepFailure.OutOfTicks, $"{(controlled >= mostTicks ? $"{mostTicks} ticks with the controls" : $"{mostTicks * WholeBudget} ticks")} ran out, " +
                        $"{(walk.Used ? "used, its consequences playing" : walk.Arrived ? "arrived, not used" : "on the way")}");
                    break;
                }
                host.Step();
                if (!walk.Used)
                {
                    Noticed(host, walk, control);
                    Watch(host, walk);
                }
            }
        }
        finally
        {
            host.Input = input;
            host.Conversations = conversations;
            host.Menus = menus;
            _walk = null;
        }
        var after = GameSnapshot.Take(host.Game);
        var changes = after.Since(before);
        var expect = step.Expect ?? [];
        Place? where = host.Session is { Mode: GameMode.Trek } s ? new Place(s.Trek.Chunks.MapSet.Id, s.Trek.Player.Feet) : null;
        return new StepOutcome(step, walk.Failure ?? StepFailure.None, walk.Arrived, walk.Used, walk.Reason, walk.Refusal,
            host.Clock.Ticks - start, Math.Max(0, walk.Plans - 1), walk.Stalls, changes, [.. expect.Where(e => !e.Holds(host.Game))],
            changes.Except(expect), [.. host.Transitions.Skip(transitions)], [.. host.Pages.Skip(pages)], [.. host.BattlesFought.Skip(battles)],
            [.. choices.Taken], [.. choices.Missed], [.. choices.NotReached], where)
        {
            Blocker = walk.Blocker,
            Crossed = [.. walk.Crossed],
            WalledBy = walk.Failure is StepFailure.Stuck ? walk.WalledBy : [],
            TrapTries = walk.TrapTries,
        };
    }

    /// <summary>
    /// After a tick: a story trigger other than the step's own cued as the player walked into it, with the ways it plays as the game
    /// stands, before its script runs. The second time one cues the same ways on a step, it has turned him back: the triggers learn
    /// those ways, and the step fails <see cref="StepFailure.Blocked"/>.
    /// </summary>
    private void Watch(PlaythroughHost host, Walk walk)
    {
        if (host.Session is not { Mode: GameMode.Trek } session)
            return;
        var cued = session.Trek.Cued;
        if (cued is not { Start.MapObject: { } source } || walk.Cued == source)
        {
            walk.Cued = cued?.Start.MapObject;
            return;
        }
        walk.Cued = source;
        if (source != walk.Step.MapObject)
            walk.Crossed.Add(source);
        if (Options.Triggers is not { } triggers || source == walk.Step.MapObject || triggers.Get(source) is null)
            return;
        var ways = triggers.WaysNow(source, host.Game);
        var key = $"{source} {string.Join(",", ways)}";
        if (walk.Fired.Add(key))
            return;
        triggers.Learn(source, ways);
        walk.Blocker = source;
        walk.Fail(StepFailure.Blocked, $"{source} ({cued.Value.Script.Id.Key}) played the same way twice on the way, ways {string.Join(", ", ways)}: it turns the player back");
    }

    /// <summary>What the step's object is, where, and how it's used.</summary>
    private void Begin(Walk walk)
    {
        var id = walk.Step.MapObject;
        if (_graph.Plugins.TryGet<ChunkRecord>(ChunkRecord.ChunkOf(id)) is not { } chunk || chunk.Object(ChunkRecord.IndexOf(id)) is not { } o)
        {
            walk.Fail(StepFailure.NoRoute, $"no chunk has {id}");
            return;
        }
        if (_graph.RangeOf(id, chunk, o) is not { } range)
        {
            walk.Fail(StepFailure.NoRoute, $"{id} is a {o.GetType().Name}, which the player doesn't use");
            return;
        }
        walk.Chunk = chunk;
        walk.Object = o;
        walk.Set = chunk.MapSet;
        walk.At = _graph.TargetOf(id, chunk, o);
        walk.Range = range;
        walk.How = walk.Step.How != UseKind.Auto ? walk.Step.How : o switch
        {
            PartyObject => UseKind.Talk,
            TeleporterObject or TripwireObject or ScriptObject when o.Tangible => UseKind.WalkInto,
            _ => UseKind.Press,
        };
        walk.Replan = true;
    }

    /// <summary>After a tick: whether walking in set the object off, read from what the tick started.</summary>
    private static void Noticed(PlaythroughHost host, Walk walk, bool hadControl)
    {
        if (walk.How != UseKind.WalkInto || walk.Object is null || host.Session is not { } session)
            return;
        var trek = session.Trek;
        var sphere = trek.Player.Sphere.Position;
        var inRange = trek.Chunks.MapSet.Id == walk.Set && Vector3.Distance(sphere, walk.At) <= walk.Range + 0.5f;
        var started = walk.Object switch
        {
            ScriptObject so => trek.Cued?.Script.Id == so.Plays || trek.Conversation?.Runner.Script.Id == so.Plays
                || session.Scene?.Runner.Script.Id == so.Plays,
            TeleporterObject => trek.Teleporting && inRange,
            // A tripwire goes off where trek's touch takes it (MapObjectUse.Touches): the grid cell under the player lists it, so standing
            // at the edge of its radius may not set it off, and he goes on to its middle (Barrows B01/0). Its flag says nothing: 77
            // tripwires clear their own flag as they set it, as 1.0's do (the castle's 4/E03/0 and 4/G07/1 flash 33 and 35), and a done
            // rule on the flag called them used before he'd moved. Touch runs before he moves, so he trips it as he stands to settle.
            TripwireObject => trek.Chunks.MapAt(sphere) is { } map && map.Record.Id == walk.Chunk!.Id
                && trek.Use.Touches(map, sphere - map.Origin, walk.Object),
            _ => false,
        };
        // Anything that took the controls while he stood in it.
        if (started || (hadControl && !host.PlayerHasControl && inRange))
        {
            walk.Used = true;
            walk.Arrived = true;
        }
    }

    /// <summary>
    /// The stick and presses for a tick of trek: nothing unless the player has the controls and the step is under way; else steering
    /// along the route, at a teleporter or at the object, and A when A would reach what's wanted.
    /// </summary>
    public TickInput Next(PlaythroughHost host)
    {
        if (_walk is not { Failure: null, Used: false } walk || !host.PlayerHasControl)
            return TickInput.None;
        // Hurt: the party camps first, and the way is asked for again after.
        if (Options.Rest?.TryCamp(host) == true || Options.Dress?.TryDress(host) == true)
        {
            walk.Replan = true;
            walk.Steering = null;
            return TickInput.None;
        }
        var trek = host.Session.Trek;
        var feet = trek.Player.Feet;
        var set = trek.Chunks.MapSet.Id;
        if (walk.Last is { } last && (last.Set != set || Vector3.Distance(last.Feet, feet) > Options.JumpDistance))
            walk.Replan = true;
        walk.Last = (set, feet);
        // Stalled: a step aside first, as a player does who walks into a lip, and the way is asked for again from where it leaves him.
        if (walk.Sidestep is { } aside && walk.SidestepTicks > 0)
        {
            walk.SidestepTicks--;
            return new TickInput(new PlayerInput(StickFor(aside, trek.Camera.LookXZ)), []);
        }
        // Waiting where he stands for a gate the hour opens.
        if (walk.WaitTicks > 0)
        {
            walk.Waited++;
            if (--walk.WaitTicks == 0)
            {
                walk.Replan = true;
                // An npc's or encounter's gates are read as its chunk loads, and its body made then: with its chunk loaded as the hour
                // opened them, trek's world is built again where he stands, as after a backdrop, a sign or the help menu, so it's there.
                if (walk.Object is PartyObject && trek.Chunks.All.Any(m => m.Chunk.Id == walk.Chunk!.Id)
                    && !WorldGraph.ObjectGates(walk.Step.MapObject, walk.Object).Any(g => g.Closed(host.Game.Flags))
                    && Wanderer(trek, walk) is null)
                    trek.Rebuild();
            }
            return TickInput.None;
        }
        if (walk.Replan && !Plan(host, walk, new Place(set, feet)))
            return TickInput.None;

        var sphere = trek.Player.Sphere.Position;
        var here = set == walk.Set;
        // An npc or encounter: steer at its body once it's near, and talk once A reaches it.
        if (walk.How == UseKind.Talk && here && Wanderer(trek, walk) is { } wanderer)
        {
            var at = wanderer.Body.Sphere.Position;
            if (Across(at, sphere) <= Options.DirectRange && MathF.Abs(at.Y - sphere.Y) < 3 && walk.DirectOff-- <= 0)
            {
                if (Wanderers.Reaches(wanderer, Across(at, sphere), trek.Player.Sphere))
                    walk.Arrived = true;
                if (Reaches(trek, walk.Step.MapObject))
                    return Use(host, walk, isTarget: true);
                return Steer(walk, trek, at, Goal.Body, stalled: () => walk.DirectOff = 180);
            }
        }
        // A camp scene's: once he stands in its object's chunk, wherever in it, the party camps, and the scene plays as the camp looks for one.
        if (walk.How == UseKind.Camp && here && trek.Chunks.ChunkAt(sphere)?.Id == walk.Chunk!.Id)
        {
            walk.Arrived = true;
            if (Options.Rest?.CampNow(host) == true)
                walk.Used = true;
            else if (++walk.InRange > Options.UsePatience)
                walk.Fail(StepFailure.NotUsed, $"in {walk.Chunk.Id} for {Options.UsePatience} ticks, and the party can't camp there");
            return TickInput.None;
        }
        if (here && Vector3.Distance(sphere, walk.At) <= walk.Range)
        {
            walk.Arrived = true;
            if (walk.How == UseKind.Arrive)
            {
                walk.Used = true;
                return TickInput.None;
            }
            if (walk.How == UseKind.Press)
            {
                if (Reaches(trek, walk.Step.MapObject))
                    return Use(host, walk, isTarget: true);
                if (++walk.InRange > Options.UsePatience)
                {
                    walk.Fail(StepFailure.NotUsed, $"within {walk.Range:0.##} of {walk.Step.MapObject} for {Options.UsePatience} ticks, and A doesn't reach it");
                    return TickInput.None;
                }
            }
        }

        var leg = walk.Route!.Legs[0];
        if (walk.Phase == Phase.Route)
        {
            // Passed: the waypoint steered for, or one a little further on that he came to first.
            var passed = -1;
            for (var i = walk.Waypoint; i < Math.Min(leg.Waypoints.Count, walk.Waypoint + 4); i++)
            {
                if (Across(leg.Waypoints[i], feet) < Options.WaypointReach && MathF.Abs(leg.Waypoints[i].Y - feet.Y) < 2)
                    passed = i;
            }
            if (passed >= 0)
            {
                walk.Waypoint = passed + 1;
                walk.Push = null;
            }
            if (walk.Waypoint >= leg.Waypoints.Count)
            {
                walk.Phase = leg.Teleporter is null ? Phase.Object : Phase.Teleporter;
            }
            else
            {
                var i = walk.Waypoint;
                var point = leg.Waypoints[i];
                // A step up a stair that the player stalled short of is climbed as the explorer's run that made it was: from the step below,
                // at rest, by the stick held one way, another each time. Steered straight at, re-aimed as the sphere slides, he comes off
                // the stair's inside.
                var line = Along(leg, i);
                if (line is not null && walk.StairTries.GetValueOrDefault(point) is var tried && tried > 0 && tried <= PushAngles.Length)
                {
                    var below = leg.Waypoints[i - 1];
                    if (walk.Push is null && Across(below, feet) < StairReach && below.Y - feet.Y is < ClimbSlack and > -2)
                    {
                        var angle = PushAngles[tried - 1] * MathF.PI / 180;
                        var way = line.Value;
                        walk.Push = new Vector2(way.X * MathF.Cos(angle) - way.Y * MathF.Sin(angle), way.X * MathF.Sin(angle) + way.Y * MathF.Cos(angle));
                        walk.PushRest = PushRestTicks;
                    }
                    // Come to rest on the step below first, as the run set off from rest.
                    if (walk.Push is not null && walk.PushRest > 0)
                    {
                        walk.PushRest--;
                        walk.Steering = null;
                        return TickInput.None;
                    }
                    if (walk.Push is null)
                        return Steer(walk, trek, below, Goal.Stair, () => Avoid(walk, leg.MapSet, below));
                    return Steer(walk, trek, point, Goal.Waypoint, () =>
                    {
                        walk.StairTries[point] = walk.StairTries.GetValueOrDefault(point) + 1;
                        walk.Push = null;
                    }, walk.Push, sidestep: false);
                }
                // The first stall short of a step up a stair is its pushes' to get past; others, and once they're spent, avoid it.
                return Steer(walk, trek, point, Goal.Waypoint, () =>
                {
                    if (line is not null && walk.StairTries.GetValueOrDefault(point) == 0)
                        walk.StairTries[point] = 1;
                    else
                        Avoid(walk, leg.MapSet, point);
                });
            }
        }
        if (walk.Phase == Phase.Teleporter)
        {
            var portal = _portals[leg.Teleporter!.Value];
            if (!portal.Tangible && Reaches(trek, portal.Id))
                return Use(host, walk, isTarget: false);
            return Steer(walk, trek, portal.At, Goal.Teleporter, () => AvoidEnd(walk, leg, feet));
        }
        // The object itself: on to its middle for one walked into, or until A reaches it.
        if (walk.How == UseKind.Talk && here && Wanderer(trek, walk) is { } body)
            return Steer(walk, trek, body.Body.Sphere.Position, Goal.Object, () => AvoidEnd(walk, leg, feet));
        return Steer(walk, trek, walk.At, Goal.Object, () => AvoidEnd(walk, leg, feet));
    }

    /// <summary>Asks for a route from where the player stands; false, failing the step, if there's none or the budget is spent.</summary>
    private bool Plan(PlaythroughHost host, Walk walk, Place from)
    {
        walk.Replan = false;
        // An npc whose body trek holds is there to talk to whatever their gates now say: 1.0 gates them only as their chunk loads or trek
        // is rebuilt, so one a conversation just shut (Batrin at Overworld D14/6, his task taken) still stands to be talked to again.
        walk.BodyThere = walk.How == UseKind.Talk && Wanderer(host.Session.Trek, walk) is not null;
        if (++walk.Plans - 1 > Options.MaxReplans)
        {
            walk.Fail(StepFailure.Stuck, $"{Options.MaxReplans} replans spent, {walk.Stalls} of them for stalls, at {from.Feet} in {from.MapSet}");
            return false;
        }
        // Bodies that stand where they are are walls a route goes round: the player's push never moves them (Crowd.Push moves only the one
        // that moved), and the walk graph was made without them.
        walk.Bodies = host.Session.Trek is { } trek && trek.Chunks.MapSet.Id == from.MapSet ? Standing(trek, walk) : [];
        walk.WalledBy = [];
        var result = Route(host.Game, walk, from);
        // Avoiding where it stuck can cut the only way: try once more avoiding only the cells nearest where it stuck, which in a room's
        // doorway or on a stair leaves the way round (MiscInteriors A05's door into the portal room, 6/A05/1, beside the step across its
        // wall that a run made and the stick can't), and then without.
        if (!result.Found && walk.Avoid.Count > 0 && result.Refusal!.Kind is RefusalKind.NotOnFoot or RefusalKind.StartOffGraph or RefusalKind.Gated)
            result = Route(host.Game, walk, from, NearAvoid);
        if (!result.Found && walk.Avoid.Count > 0 && result.Refusal!.Kind is RefusalKind.NotOnFoot or RefusalKind.StartOffGraph or RefusalKind.Gated)
        {
            walk.Avoid.Clear();
            result = _graph.Route(host.Game, from, walk.Step.MapObject,
                new RouteOptions(Options.PickLocks, KeepClear: [.. walk.Bodies.Select(b => b.Zone)], IgnoreOwnGates: walk.How == UseKind.Camp || walk.BodyThere));
        }
        // The standing bodies cut every way: the way without them is walked all the same, since the walk graph's cells are a unit across
        // and a gap the player fits through may have none left, and they're named, so that if he sticks the run bans every way at the object
        // where he stands rather than trying each (StepOutcome.WalledBy).
        if (!result.Found && walk.Bodies.Count > 0
            && _graph.Route(host.Game, from, walk.Step.MapObject,
                new RouteOptions(Options.PickLocks, KeepClear: Shunned(walk, from), IgnoreOwnGates: walk.How == UseKind.Camp || walk.BodyThere))
                is { Found: true, Route: { } through } without)
        {
            var walling = walk.Bodies.Where(b => through.Legs.Any(l => l.MapSet == b.Zone.Centre.MapSet
                && l.Waypoints.Any(w => Vector3.Distance(w, b.Zone.Centre.Feet) <= b.Zone.Radius + WallSlack))).ToList();
            walk.WalledBy = [.. (walling.Count > 0 ? walling : walk.Bodies).Select(b => b.Id)];
            result = without;
        }
        // Shut only by gates the hour opens (the Burial Chamber's door in Ugarit2 A05 opens at night), on the way or on the object itself
        // (the Troll west of Darkling Bog, Overworld K16/2, is out only at night): he waits, up to a day.
        if (!result.Found && result.Refusal is { Kind: RefusalKind.Gated or RefusalKind.ObjectGated } gated && Options.Waits?.Invoke(gated) == true
            && walk.Waited < MostWait)
        {
            walk.WaitTicks = WaitStep;
            walk.Plans--;
            return false;
        }
        if (!result.Found)
        {
            walk.Refusal = result.Refusal;
            walk.Fail(StepFailure.NoRoute, result.Refusal!.Message);
            return false;
        }
        walk.Route = result.Route;
        walk.Waypoint = 0;
        walk.Phase = Phase.Route;
        walk.Steering = null;
        walk.Push = null;
        walk.InRange = 0;
        return true;
    }

    /// <summary>
    /// The route, kept clear of other script objects' triggers where it can be (<see cref="PilotOptions.KeepClear"/>). Where it can't, the
    /// triggers the plain route walks into are let be and the rest kept clear of; failing that, the plain route. The story funnels so: a
    /// new game's only way out of where the opening leaves the player passes Edur's cut-scene.
    /// </summary>
    private RouteResult Route(GameState game, Walk walk, Place from, float avoidRadius = WideAvoid)
    {
        var shun = Shunned(walk, from);
        // A camp scene's object holds only as the party camps: the way is to its chunk, whatever its own gates.
        var plain = new RouteOptions(Options.PickLocks, [.. walk.Avoid], avoidRadius, KeepClear: [.. shun, .. walk.Bodies.Select(b => b.Zone)],
            IgnoreOwnGates: walk.How == UseKind.Camp || walk.BodyThere);
        if (!Options.KeepClear || Triggers(game, walk, from) is not { Count: > 0 } zones)
            return _graph.Route(game, from, walk.Step.MapObject, plain);
        var clear = _graph.Route(game, from, walk.Step.MapObject, plain with { KeepClear = [.. plain.KeepClear!, .. zones] });
        if (clear.Found)
            return clear;
        var through = _graph.Route(game, from, walk.Step.MapObject, plain);
        if (!through.Found)
            return through;
        var crossed = zones.Where(z => through.Route!.Legs.Any(l => l.MapSet == z.Centre.MapSet
            && l.Waypoints.Any(w => Vector3.Distance(w, z.Centre.Feet) <= z.Radius))).ToHashSet();
        if (crossed.Count == 0 || crossed.Count == zones.Count)
            return through;
        var fewer = _graph.Route(game, from, walk.Step.MapObject, plain with { KeepClear = [.. plain.KeepClear!, .. zones.Where(z => !crossed.Contains(z))] });
        return fewer.Found ? fewer : through;
    }

    /// <summary>The zones of the triggers no route may cross (<see cref="PilotOptions.Shun"/>), but the step's own and one he stands in.</summary>
    private List<RouteZone> Shunned(Walk walk, Place from)
    {
        var zones = new List<RouteZone>();
        foreach (var id in Options.Shun ?? [])
        {
            if (id == walk.Step.MapObject || _graph.Plugins.TryGet<ChunkRecord>(ChunkRecord.ChunkOf(id)) is not { } chunk
                || chunk.Object(ChunkRecord.IndexOf(id)) is not { } o)
                continue;
            var at = _graph.Plugins.Get<MapSetRecord>(chunk.MapSet).Origin(chunk) + o.Position;
            var radius = o.Radius + 1;
            if (chunk.MapSet == from.MapSet && Vector3.Distance(at, from.Feet) <= radius + 1)
                continue;
            zones.Add(new RouteZone(new Place(chunk.MapSet, at), radius));
        }
        return zones;
    }

    /// <summary>
    /// The ground walking onto would set off another script object, in the map sets the player and the object are in: each enabled
    /// tangible script object that its chunk's grid lists (walking can't set off those only trigger triangles name), within its radius
    /// of the player's sphere and a little more, as trek's touch tests it. Those he stands in, and those about the object, are left out.
    /// </summary>
    private List<RouteZone> Triggers(GameState game, Walk walk, Place from)
    {
        var zones = new List<RouteZone>();
        foreach (var setId in new[] { from.MapSet, walk.Set }.Distinct())
        {
            var set = _graph.Plugins.Get<MapSetRecord>(setId);
            foreach (var chunk in _graph.Plugins.ChunksIn(setId))
            {
                foreach (var o in chunk.Objects)
                {
                    if (o is not ScriptObject { Tangible: true } || !Listed(chunk).Contains(o.Index))
                        continue;
                    var id = chunk.ObjectId(o.Index);
                    if (id == walk.Step.MapObject || WorldGraph.ObjectGates(id, o).Any(g => g.Closed(game.Flags)))
                        continue;
                    var at = set.Origin(chunk) + o.Position;
                    var radius = o.Radius + 1;
                    if (Vector3.Distance(at, from.Feet) <= radius + 1 || (setId == walk.Set && Vector3.Distance(at, walk.At) <= radius + walk.Range))
                        continue;
                    zones.Add(new RouteZone(new Place(setId, at), radius));
                }
            }
        }
        return zones;
    }

    /// <summary>
    /// The bodies loaded about the player that stand where they are, but the step's own: npcs (an encounter hunts) whose every wander node
    /// that's walked to is within its radius of where the body stands now, less its scatter, so it never sets off; the rest only turn. Each
    /// is ground no route may cross: where the player's centre would be within the two radii of the body's.
    /// </summary>
    private static List<(RecordId Id, RouteZone Zone)> Standing(TrekLoop trek, Walk walk)
    {
        var set = trek.Chunks.MapSet.Id;
        var radius = trek.Player.Sphere.Radius;
        var bodies = new List<(RecordId, RouteZone)>();
        foreach (var w in trek.Wanderers.All)
        {
            var id = ChunkRecord.ObjectId(w.Map.Record.Id, w.Party.Index);
            if (id == walk.Step.MapObject || !Stands(w))
                continue;
            var feet = w.Body.Sphere.Position - new Vector3(0, w.Body.ScaledRadius, 0);
            bodies.Add((id, new RouteZone(new Place(set, feet), w.Body.ScaledRadius + radius)));
        }
        return bodies;
    }

    /// <summary>Whether a wanderer never sets off from where its body stands (<see cref="Wanderer.Resume"/> walks only to a node it's further from than its radius).</summary>
    internal static bool Stands(Wanderer w)
    {
        if (w.Hunts || w.Chasing || w.Node is not { } first)
            return !w.Hunts && !w.Chasing;
        var at = new Vector2(w.Body.Sphere.Position.X, w.Body.Sphere.Position.Z);
        var seen = new HashSet<int>();
        var nodes = new Stack<WanderNodeObject>([first]);
        while (nodes.TryPop(out var node))
        {
            if (!seen.Add(node.Index))
                continue;
            if ((node.Flags & WanderFlags.Move) != 0
                && Vector2.Distance(new Vector2(node.Position.X + w.Map.Origin.X, node.Position.Z + w.Map.Origin.Z), at) + node.Scatter > node.WanderRadius)
                return false;
            foreach (var next in node.Next)
            {
                if (w.Map[ChunkRecord.IndexOf(next)] is WanderNodeObject n)
                    nodes.Push(n);
            }
        }
        return true;
    }

    /// <summary>How far past a standing body's zone a waypoint of the way without the bodies counts as crossing it, the walk graph's cells being a unit across.</summary>
    private const float WallSlack = 0.75f;

    private readonly Dictionary<RecordId, HashSet<int>> _listed = [];

    /// <summary>The map objects a chunk's grid lists in some cell: all of them without a grid.</summary>
    private HashSet<int> Listed(ChunkRecord chunk)
    {
        if (!_listed.TryGetValue(chunk.Id, out var listed))
            _listed[chunk.Id] = listed = chunk.Grid is { } grid ? [.. grid.Cells.SelectMany(c => c)] : [.. chunk.Objects.Select(o => o.Index)];
        return listed;
    }

    /// <summary>
    /// Pushes the stick toward a point across the ground, through the trek camera's look, and watches for a stall: no
    /// <see cref="PilotOptions.Progress"/> nearer in <see cref="PilotOptions.StallTicks"/>, after which it asks for a new route.
    /// </summary>
    private TickInput Steer(Walk walk, TrekLoop trek, Vector3 point, Goal goal, Action stalled, Vector2? hold = null, bool sidestep = true)
    {
        var feet = trek.Player.Feet;
        var key = (goal, goal is Goal.Waypoint or Goal.Stair ? walk.Waypoint : 0);
        if (walk.Steering != key)
            (walk.Steering, walk.Best, walk.Since) = (key, float.MaxValue, 0);
        var across = new Vector2(point.X - feet.X, point.Z - feet.Z);
        var distance = across.Length();
        if (distance < walk.Best - Options.Progress)
            (walk.Best, walk.Since) = (distance, 0);
        else if (++walk.Since > Options.StallTicks)
        {
            // A push up a stair that stalls is the next push's to try, and isn't counted.
            if (sidestep && ++walk.Stalls > Options.MaxStalls)
            {
                walk.Fail(StepFailure.Stuck, $"stalled {walk.Stalls} times, the last at {feet} in {trek.Chunks.MapSet.Id} steering for {goal} {point}");
                return TickInput.None;
            }
            stalled();
            walk.Replan = true;
            walk.Steering = null;
            if (sidestep && distance > 1e-3f)
            {
                var angle = SidestepAngles[(walk.Stalls - 1) % SidestepAngles.Length] * MathF.PI / 180;
                var way = across / distance;
                walk.Sidestep = new Vector2(way.X * MathF.Cos(angle) - way.Y * MathF.Sin(angle), way.X * MathF.Sin(angle) + way.Y * MathF.Cos(angle));
                walk.SidestepTicks = Options.SidestepTicks;
            }
            return TickInput.None;
        }
        if (distance < 1e-3f)
            return TickInput.None;
        return new TickInput(new PlayerInput(StickFor(hold ?? across / distance, trek.Camera.LookXZ)), []);
    }

    /// <summary>For a waypoint that's a step up a stair from the one before, the way across the ground from that one to it.</summary>
    private static Vector2? Along(RouteLeg leg, int i)
    {
        if (i < 1 || leg.Waypoints[i].Y - leg.Waypoints[i - 1].Y <= StairRise)
            return null;
        var way = new Vector2(leg.Waypoints[i].X - leg.Waypoints[i - 1].X, leg.Waypoints[i].Z - leg.Waypoints[i - 1].Z);
        return way.Length() > 1e-3f ? Vector2.Normalize(way) : null;
    }

    /// <summary>
    /// The stick that moves the player the way <paramref name="way"/> across the ground, with the camera looking
    /// <paramref name="look"/>: <see cref="Movement.PlayerMotion"/> turns up on the stick to the look and right to its right
    /// (<c>Facing.Rotate</c> of the stick's (−x, y) by the look), which is its own inverse.
    /// </summary>
    public static Vector2 StickFor(Vector2 way, Vector2 look) =>
        new(-look.Y * way.X + look.X * way.Y, look.X * way.X + look.Y * way.Y);

    /// <summary>Presses A, as the player does before the tick. A use of the step's object marks it used.</summary>
    private static TickInput Use(PlaythroughHost host, Walk walk, bool isTarget)
    {
        if (host.Press(MenuAction.Accept) == PressOutcome.Used && isTarget)
            walk.Used = true;
        return TickInput.None;
    }

    /// <summary>
    /// Whether A pressed now would use <paramref name="target"/>: what trek's A weighs (<see cref="TrekLoop.UseNearest"/>), the body A
    /// reaches with the map objects in the player's chunk.
    /// </summary>
    private static bool Reaches(TrekLoop trek, RecordId target)
    {
        var sphere = trek.Player.Sphere;
        if (trek.Use.Popup is not null || trek.Chunks.MapAt(sphere.Position) is not { } map)
            return false;
        (PartyObject, Vector3, float)? body = null;
        if (trek.Nearest is ({ } nearest, var distance) && nearest.Map == map && Wanderers.Reaches(nearest, distance, sphere))
            body = (nearest.Party, nearest.Body.Sphere.Position - map.Origin, distance);
        return trek.Use.Reach(map, sphere.Position - map.Origin, trek.Player.Heading, sphere.Radius, body) is { } o
            && map.Record.ObjectId(o.Index) == target;
    }

    /// <summary>The step's npc's or encounter's body, if its chunk is loaded and it wanders there.</summary>
    private static Wanderer? Wanderer(TrekLoop trek, Walk walk) =>
        trek.Wanderers.All.FirstOrDefault(w => w.Map.Record.Id == walk.Chunk!.Id && w.Party.Index == walk.Object!.Index);

    private static float Across(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>How far below the step below a stair's waypoint the player's feet may be, and how near across, to push up from it.</summary>
    private const float ClimbSlack = 0.3f, StairReach = 0.4f;

    /// <summary>A waypoint this much above the one before is a step up a stair.</summary>
    private const float StairRise = 0.2f;

    private static void Avoid(Walk walk, RecordId set, Vector3 at) => walk.Avoid.Add(new Place(set, at));

    /// <summary>How far about where it stuck a route keeps away (<see cref="RouteOptions.AvoidRadius"/>), and how near when that cuts every way.</summary>
    private const float WideAvoid = 1.5f, NearAvoid = 0.5f;

    /// <summary>Stalled short of a teleporter or the object: the route's way in is avoided, its last waypoint, so the next comes another way.</summary>
    private static void AvoidEnd(Walk walk, RouteLeg leg, Vector3 feet) =>
        Avoid(walk, leg.MapSet, leg.Waypoints.Count > 0 ? leg.Waypoints[^1] : feet);

    /// <summary>How long the player waits before the way is asked for again, when only the hour shuts it: a game hour.</summary>
    private const int WaitStep = (int)(Calendar.GameClock.Hour / Calendar.GameClock.DaySpeed);

    /// <summary>The longest the player waits for the hour to open a way: a game day, 20 minutes of play.</summary>
    private const long MostWait = Calendar.GameClock.Day / Calendar.GameClock.DaySpeed;

    /// <summary>
    /// The turns, in degrees, from the way along a stair's step that each push up it takes, in turn. From the landing of the Wizard's School library's
    /// stair (JundarInteriors A01, 118.5, 7.45, 107.4), of 36 ways held only 310° climbs to the library, where the step up is at 299°.
    /// </summary>
    private static readonly float[] PushAngles = [0, 10, -10, 20, -20, 30, -30];

    /// <summary>How long the player stands still on a stair's step before pushing up from it.</summary>
    private const int PushRestTicks = 20;

    /// <summary>The turns from the way wanted that each stall's step aside takes, in turn: to the sides, back, and between.</summary>
    private static readonly float[] SidestepAngles = [90, -90, 180, 45, -45, 135, -135];

    /// <summary>What the pilot steers for.</summary>
    private enum Goal
    {
        Waypoint,
        /// <summary>The step below a stair's waypoint, to push up from.</summary>
        Stair,
        Teleporter,
        Object,
        Body,
    }

    /// <summary>Where on the route the pilot is.</summary>
    private enum Phase
    {
        /// <summary>Walking the first leg's waypoints.</summary>
        Route,

        /// <summary>At the end of a leg, going into or pressing A at its teleporter.</summary>
        Teleporter,

        /// <summary>At the end of the last leg, going on to the object.</summary>
        Object,
    }

    /// <summary>A step under way.</summary>
    /// <summary>How many times a step's trap is backed out of for its roll before Disarm is chosen however it falls.</summary>
    public const int MostTrapTries = 64;

    /// <summary>
    /// The trap's menu at the step's own object (<see cref="PilotStep.Disarms"/>): Disarm is chosen only when the roll the menu holds
    /// still comes out as the step wants (<see cref="Containers.WouldDisarm"/>; nothing draws on the world's stream while a popup is up,
    /// trek's clock stopped); otherwise Exit, and the pilot saves and loads, which moves the stream, and tries again. Who disarms is the
    /// game's choice, the member with the most Mechanic who has the Stamina, so when Disarm is chosen is all a player has. Every other
    /// menu and popup is the host's own policy's.
    /// </summary>
    private sealed class TrapTiming(Walk walk, IMenuPolicy inner) : IMenuPolicy
    {
        public MenuAction Press(PlaythroughHost host, PauseMenu menu) => inner.Press(host, menu);

        public int? Popup(PlaythroughHost host, TrekPopup popup)
        {
            if (host.Session.Trek.Use.Trap is not var (id, level) || id != walk.Step.MapObject || walk.Step.Disarms is not { } want
                || walk.TrapTries >= MostTrapTries)
                return inner.Popup(host, popup);
            if (Containers.WouldDisarm(host.Game.Party, level, host.Game.Random.World) == want)
                return 0;
            walk.TrapTries++;
            walk.Reroll = true;
            return null;
        }
    }

    private sealed class Walk(PilotStep step)
    {
        public PilotStep Step { get; } = step;
        public UseKind How;
        /// <summary>A talk's npc whose body trek holds, gates or none.</summary>
        public bool BodyThere;
        public ChunkRecord? Chunk;
        public MapObject? Object;
        public RecordId Set;
        public Vector3 At;
        public float Range;

        public WorldRoute? Route;
        public Phase Phase;
        public int Waypoint;
        public bool Replan;
        public int Plans;
        public int Stalls;
        public readonly List<Place> Avoid = [];
        public (Goal, int)? Steering;
        public float Best;
        public int Since;
        public int InRange;
        public int DirectOff;
        public (RecordId Set, Vector3 Feet)? Last;

        public int WaitTicks;
        public long Waited;
        public Vector2? Sidestep;
        public int SidestepTicks;

        /// <summary>The way the stick is held up a stair's step, once the player stands on the step below; null while it isn't.</summary>
        public Vector2? Push;

        /// <summary>The ticks left to stand still on the step below before the push.</summary>
        public int PushRest;

        /// <summary>How many pushes up each stair's step, by its waypoint, have stalled: each next is held another way.</summary>
        public readonly Dictionary<Vector3, int> StairTries = [];
        public RecordId? Cued;
        public readonly HashSet<string> Fired = [];

        /// <summary>The story triggers other than the step's own cued on the step, in the order they were.</summary>
        public readonly List<RecordId> Crossed = [];
        public RecordId? Blocker;

        public bool Arrived;
        public bool Used;

        /// <summary>The step's trap was backed out of for its roll, and the game is to be saved and loaded before it's tried again.</summary>
        public bool Reroll;

        /// <summary>How many times the step's trap has been backed out of.</summary>
        public int TrapTries;
        public StepFailure? Failure;
        public string? Reason;
        public RouteRefusal? Refusal;

        /// <summary>The bodies that stand about the player, which routes go round, as of the last route asked for.</summary>
        public List<(RecordId Id, RouteZone Zone)> Bodies = [];

        /// <summary>The standing bodies that cut every way to the object, the last time a route was asked for with any.</summary>
        public IReadOnlyList<RecordId> WalledBy = [];

        public void Fail(StepFailure failure, string reason) => (Failure, Reason) = (failure, reason);
    }
}
