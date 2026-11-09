using HBHT.Core.Characters;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>
/// The world graph behind the planner's places (<see cref="IPlaceReach"/>), from where the player stands in a real game. One flood
/// (<see cref="WorldGraph.Flood"/>) finds the way to every place with the fewest closed gates the plan could open; a place's
/// <see cref="Gates"/> are those gates' literals (a locked door's flag set, a teleporter's enabled flag), which the planner takes as the
/// preconditions of the actions there, so a plan to a place behind a closed door first makes its flag true. A gate whose literal can't
/// come true by any order of steps (<c>canOpen</c>), or a lock with no flag the party can't pick, is a wall. A lock the party's thief
/// can pick (<see cref="CanPick"/>) is open, picked on the way.
/// </summary>
/// <remarks>
/// <para>
/// A place no way on foot reaches, however the gates stand, is <see cref="OffFoot"/>: the story takes the player to some places
/// itself (the Goblin Ambush's battle ends in Oriana's hut), and the graph has no edge for a teleporter only a trigger triangle names,
/// or with no reached ground about it. Such a place needs nothing in the relaxation, so a goal beyond it still has a relaxed plan, but
/// the search can't step there (<see cref="CanReach"/> is false): a plan makes the progress it can on foot, and the run replans once
/// the story has moved the player.
/// </para>
/// <para>
/// The flood reads the gates against the game's flags as they stand, derived ones included (<see cref="RouteGate.Closed"/> on the
/// game's network), so a plan's later states see the gates only through the literals: a gate open now is taken to stay open, and the
/// ways are from where the player stands now, not where the plan's earlier steps leave him. The run replans after every step, so
/// both are put right as they go wrong.
/// </para>
/// </remarks>
public sealed class WorldReach : IPlaceReach
{
    private readonly GameState _game;
    private readonly Dictionary<RecordId, (IReadOnlyList<Literal>? Literals, IReadOnlyList<RouteGate> Gates, bool Picks)> _known = [];
    private readonly Func<Literal, bool>? _canOpen;

    /// <param name="graph">The world graph.</param>
    /// <param name="game">The real game, whose flags the gates are read against and whose party picks locks.</param>
    /// <param name="from">Where the player stands.</param>
    /// <param name="canOpen">Whether a closed gate's literal can ever come true (the relaxation's say); every one can if null.</param>
    public WorldReach(WorldGraph graph, GameState game, Place from, Func<Literal, bool>? canOpen = null)
    {
        Graph = graph;
        _game = game;
        _canOpen = canOpen;
        From = from;
        Flood = graph.Flood(from, Toll);
    }

    public WorldGraph Graph { get; }

    public Place From { get; }

    public GateFlood Flood { get; }

    /// <summary>What a teleporter costs: its closed gates the plan can open, or null for a wall.</summary>
    private int? Toll(IGatedWay portal)
    {
        var cost = 0;
        foreach (var gate in portal.Gates)
        {
            switch (Judge(gate))
            {
                case GateState.Wall:
                    return null;
                case GateState.Closed:
                    cost++;
                    break;
            }
        }
        return cost;
    }

    private enum GateState
    {
        Open,
        Picked,
        Closed,
        Wall,
    }

    private GateState Judge(RouteGate gate)
    {
        if (!gate.Closed(_game.Flags))
            return GateState.Open;
        if (gate.Kind == GateKind.Locked && CanPick(_game.Party, gate.Level))
            return GateState.Picked;
        if (Literal(gate) is not { } literal || _canOpen?.Invoke(literal) == false)
            return GateState.Wall;
        return GateState.Closed;
    }

    /// <summary>What makes a gate hold: its flag set (or clear, inverted); none for a lock with no flag.</summary>
    public static FlagIs? Literal(RouteGate gate) => gate.Flag is { } flag ? new FlagIs(flag, !gate.Invert) : null;

    /// <summary>
    /// Whether the party picks a lock of a level: its best thief's skill, at most 10, is above it, and the level is below 10
    /// (<see cref="Items.Containers.PickLock"/>, which also takes some Stamina, left out here).
    /// </summary>
    public static bool CanPick(Party party, int level) =>
        level < 10 && party.MostSkilled(Skill.Thief) is { } thief && Math.Min(thief.Skills.Modified(Skill.Thief), 10) - 1 >= level;

    private (IReadOnlyList<Literal>? Literals, IReadOnlyList<RouteGate> Gates, bool Picks) Look(RecordId place)
    {
        if (_known.TryGetValue(place, out var known))
            return known;
        var portals = place.Type == RecordTypes.Chunk ? Flood.ToChunk(place) : Flood.To(place);
        if (portals is null)
            return _known[place] = (null, [], false);
        var gates = new List<RouteGate>();
        var picks = false;
        foreach (var gate in portals.SelectMany(p => p.Gates))
        {
            switch (Judge(gate))
            {
                case GateState.Closed:
                    gates.Add(gate);
                    break;
                case GateState.Picked:
                    picks = true;
                    break;
            }
        }
        IReadOnlyList<Literal> literals = [.. gates.Select(Literal).OfType<Literal>().Distinct()];
        return _known[place] = (literals, gates, picks);
    }

    /// <summary>The closed gates' literals on the way to a place; none for one off foot, which the relaxation takes as reached.</summary>
    public IReadOnlyList<Literal>? Gates(RecordId place) => Look(place).Literals ?? [];

    /// <summary>Whether no way on foot reaches a place from where the player stands, however the gates stand.</summary>
    public bool OffFoot(RecordId place) => Look(place).Literals is null;

    /// <summary>The closed gates on the way to a place with the fewest, for reports; empty if it's open or can't be reached.</summary>
    public IReadOnlyList<RouteGate> Blockers(RecordId place) => Look(place).Gates;

    /// <summary>Whether the way to a place with the fewest closed gates takes a lock the party picks, as far as the flood's way goes.</summary>
    public bool Picks(RecordId place) => Look(place).Picks;

    public bool CanReach(RecordId place, PlanState state) => Look(place).Literals is { } gates && gates.All(state.Holds);
}
