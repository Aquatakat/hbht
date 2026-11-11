using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

public sealed partial class WorldGraph
{
    /// <summary>
    /// Every cell the player can get to from <paramref name="from"/>, by map set, each with the fewest closed gates on a way to it and
    /// the teleporters and toggles that carry them: what the planner's places need (<see cref="WorldReach"/>). <paramref name="toll"/>
    /// says what a teleporter or a toggle's gated step costs to take: 0 if it's open, the number of its closed gates the plan can open,
    /// or null where it can never be taken. A toggle already on the way to a cell costs nothing again, so the gated steps across one
    /// wall count once. A search over costs by bucket, so one flood answers every place.
    /// </summary>
    public GateFlood Flood(Place from, Func<IGatedWay, int?> toll) => Flood(from, Tolls(toll));

    /// <summary>What each way costs a flood (<see cref="Flood(Place, Func{IGatedWay, int?})"/>), by its index: the portals', then the toggles'.</summary>
    internal int?[] Tolls(Func<IGatedWay, int?> toll)
    {
        var tolls = new int?[Portals.Count + Toggles.Count];
        for (var w = 0; w < tolls.Length; w++)
            tolls[w] = toll(Way(w));
        return tolls;
    }

    /// <summary>The flood with each way's toll given by its index (<see cref="Tolls"/>).</summary>
    internal GateFlood Flood(Place from, int?[] tolls)
    {
        var flood = new GateFlood(this);
        if (_plugins.TryGet<MapSetRecord>(from.MapSet) is null || Walk(from.MapSet).SnapStart(from.Feet, 6) is not { } startCell)
            return flood;

        var buckets = new List<Stack<long>>();
        void Push(int cost, int set, int cell)
        {
            while (buckets.Count <= cost)
                buckets.Add(new Stack<long>());
            buckets[cost].Push(Pack(set, cell));
        }

        var start = flood.Set(from.MapSet);
        start.Cost[startCell] = 0;
        Push(0, start.Index, startCell);
        for (var d = 0; d < buckets.Count; d++)
        {
            var bucket = buckets[d];
            while (bucket.TryPop(out var node))
            {
                var (s, cell) = ((int)(node >> 32), (int)(uint)node);
                var here = flood.Sets[s];
                if (here.Cost[cell] != d)
                    continue;
                var chain = here.Chain[cell];
                foreach (var to in here.Graph.Steps(cell))
                {
                    if (here.Cost[to] > d)
                    {
                        here.Cost[to] = d;
                        here.Chain[to] = chain;
                        bucket.Push(Pack(s, to));
                    }
                }
                foreach (var (to, local) in here.Graph.GatedSteps(cell))
                {
                    var way = Portals.Count + ToggleOf(here.Graph, local);
                    if (tolls[way] is not { } cost)
                        continue;
                    var crossed = cost == 0 || flood.Carries(chain, way);
                    var nd = crossed ? d : d + cost;
                    if (here.Cost[to] <= nd)
                        continue;
                    here.Cost[to] = nd;
                    here.Chain[to] = crossed ? chain : flood.Link(way, chain);
                    if (nd == d)
                        bucket.Push(Pack(s, to));
                    else
                        Push(nd, s, to);
                }
                if (!(here.Portals ??= PortalCells(here.Graph.MapSet)).TryGetValue(cell, out var portals))
                    continue;
                foreach (var p in portals)
                {
                    if (tolls[p] is not { } cost || ArrivalCell(p) is not { } arrival)
                        continue;
                    var there = flood.Set(Portals[p].ToSet);
                    var nd = d + cost;
                    if (there.Cost[arrival] <= nd)
                        continue;
                    there.Cost[arrival] = nd;
                    there.Chain[arrival] = cost == 0 ? chain : flood.Link(p, chain);
                    if (nd == d)
                        bucket.Push(Pack(there.Index, arrival));
                    else
                        Push(nd, there.Index, arrival);
                }
            }
        }
        return flood;
    }
}

/// <summary>
/// The fewest closed gates on a way from a place to each cell the player can get to (<see cref="WorldGraph.Flood"/>), and the
/// teleporters that carry them.
/// </summary>
public sealed class GateFlood
{
    private readonly WorldGraph _graph;
    private readonly Dictionary<RecordId, FloodSet> _byId = [];
    private readonly List<(int Way, int Prev)> _chains = [];

    internal GateFlood(WorldGraph graph) => _graph = graph;

    internal List<FloodSet> Sets { get; } = [];

    internal sealed class FloodSet(int index, WalkGraph graph)
    {
        public int Index { get; } = index;
        public WalkGraph Graph { get; } = graph;
        public int[] Cost { get; } = Filled(graph.Count, int.MaxValue);
        public int[] Chain { get; } = Filled(graph.Count, -1);

        /// <summary>The map set's portals by cell, once the flood first asks.</summary>
        internal WorldGraph.PortalCellIndex? Portals { get; set; }

        private static int[] Filled(int count, int value)
        {
            var a = new int[count];
            Array.Fill(a, value);
            return a;
        }
    }

    internal FloodSet Set(RecordId mapSet)
    {
        if (_byId.TryGetValue(mapSet, out var set))
            return set;
        set = _byId[mapSet] = new FloodSet(Sets.Count, _graph.Walk(mapSet));
        Sets.Add(set);
        return set;
    }

    internal int Link(int way, int prev)
    {
        _chains.Add((way, prev));
        return _chains.Count - 1;
    }

    /// <summary>Whether the flood started at all: the place it started from was on its map set's walk graph.</summary>
    public bool Started => Sets.Count > 0;

    /// <summary>
    /// The way to the place to stand to use a map object with the fewest closed gates: their teleporters and toggles, first first, or null if no
    /// way reaches it however the gates stand. The object's own gates aren't counted.
    /// </summary>
    public IReadOnlyList<IGatedWay>? To(RecordId mapObject)
    {
        var plugins = _graph.Plugins;
        if (plugins.TryGet<ChunkRecord>(ChunkRecord.ChunkOf(mapObject)) is not { } chunk || chunk.Object(ChunkRecord.IndexOf(mapObject)) is not { } o
            || WorldGraph.UseRange(o) is not { } range || !_byId.TryGetValue(chunk.MapSet, out var set))
            return null;
        var best = -1;
        foreach (var (cell, _) in _graph.StandCells(mapObject, chunk, o, range))
        {
            if (set.Cost[cell] != int.MaxValue && (best < 0 || set.Cost[cell] < set.Cost[best]))
                best = cell;
        }
        return best < 0 ? null : Ways(set.Chain[best]);
    }

    /// <summary>The way into a chunk with the fewest closed gates, as <see cref="To"/>: to any ground in its cell.</summary>
    public IReadOnlyList<IGatedWay>? ToChunk(RecordId chunkId)
    {
        var plugins = _graph.Plugins;
        if (plugins.TryGet<ChunkRecord>(chunkId) is not { } chunk || !_byId.TryGetValue(chunk.MapSet, out var set))
            return null;
        // The first of the cell's ground with the fewest: its cells are in order.
        var best = -1;
        foreach (var i in _graph.ChunkCells(chunk.MapSet, chunk.Row, chunk.Column))
        {
            if (set.Cost[i] != int.MaxValue && (best < 0 || set.Cost[i] < set.Cost[best]))
                best = i;
        }
        return best >= 0 ? Ways(set.Chain[best]) : null;
    }

    /// <summary>The fewest closed gates on a way to a cell of a map set's walk graph, or null if no way reaches it.</summary>
    public int? CellCost(RecordId mapSet, int cell) =>
        _byId.TryGetValue(mapSet, out var set) && set.Cost[cell] != int.MaxValue ? set.Cost[cell] : null;

    /// <summary>Whether the flood reached a map set at all.</summary>
    public bool Reached(RecordId mapSet) => _byId.ContainsKey(mapSet);

    private List<IGatedWay> Ways(int chain)
    {
        var list = new List<IGatedWay>();
        for (var c = chain; c >= 0; c = _chains[c].Prev)
            list.Add(_graph.Way(_chains[c].Way));
        list.Reverse();
        return list;
    }

    /// <summary>Whether a way is on a chain already.</summary>
    internal bool Carries(int chain, int way)
    {
        for (var c = chain; c >= 0; c = _chains[c].Prev)
            if (_chains[c].Way == way)
                return true;
        return false;
    }
}
