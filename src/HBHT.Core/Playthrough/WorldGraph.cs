using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using HBHT.Core.Combat;
using HBHT.Core.MapObjects;
using HBHT.Core.Movement;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>A teleporter as an edge between map sets: where it stands, where it puts the player, and what stands in the way.</summary>
/// <param name="Id">The teleporter's map object.</param>
/// <param name="At">Its position, in its map set's world coordinates.</param>
/// <param name="Range">How near the player's sphere must be to take it: its radius, which walking into a tangible one and pressing A at one both test.</param>
/// <param name="Arrives">Where it puts the player in <see cref="ToSet"/>, in that set's world coordinates (<see cref="TeleportArrival"/>).</param>
/// <param name="Gates">Its visibility and enabled flags, and its lock, trap and secret, to be read against a game when a route is asked for.</param>
public sealed record Portal(RecordId Id, RecordId FromSet, Vector3 At, float Range, RecordId ToSet, Vector3 Arrives, bool Tangible,
    IReadOnlyList<RouteGate> Gates);

/// <summary>How long a map set's walk graph took to come by.</summary>
/// <param name="Seconds">Exploring it, or reading it from the cache.</param>
/// <param name="FromCache">Whether it was read from the cache.</param>
/// <param name="CacheBytes">The cache file's size.</param>
public sealed record WalkInfo(RecordId MapSet, double Seconds, bool FromCache, long CacheBytes, int Cells, int Steps);

/// <summary>
/// The playthrough bot's second layer (docs/systems/playthrough.md): the world as a graph it can route over. Each map set has a
/// <see cref="WalkGraph"/> from <see cref="Reach"/>'s exploration at the original's movement and pace, made when it is first asked for
/// and kept in a cache file keyed by the plugins (<see cref="Fingerprint"/>); teleporters join the map sets (<see cref="Portals"/>);
/// and <see cref="Route"/> finds the way, on foot and through teleporters, to the place to stand to use a map object, with the gates
/// on the way read against the game it is asked for rather than baked into the graph.
/// </summary>
public sealed class WorldGraph
{
    /// <summary>What a teleporter costs a route beyond the walk, in units, so a short way across a map set beats a long way round through one.</summary>
    public const float TeleportCost = 8;

    /// <summary>What a gate that doesn't hold costs the search that looks for the route with the fewest of them.</summary>
    private const float ClosedCost = 1e7f;

    /// <summary>The explorer's settings: the original's movement and pace.</summary>
    public static Reach.Options DefaultOptions { get; } = new(MovementMode.Original);

    private readonly PluginSet _plugins;
    private readonly Reach.Options _options;
    private readonly string _cache;
    private readonly ConcurrentDictionary<RecordId, Lazy<WalkGraph>> _walks = [];
    private readonly ConcurrentDictionary<RecordId, WalkInfo> _info = [];
    private readonly Lazy<IReadOnlyList<Portal>> _portals;
    private readonly Lazy<string> _fingerprint;
    private readonly ConcurrentDictionary<RecordId, Lazy<Dictionary<int, int[]>>> _portalCells = [];
    private readonly ConcurrentDictionary<int, Lazy<int?>> _arrivals = [];
    private readonly Lock _closureLock = new();
    private readonly Dictionary<string, Dictionary<RecordId, bool[]>> _closures = [];

    /// <param name="plugins">The installed plugins.</param>
    /// <param name="cacheDirectory">Where graphs are kept; <c>cache/playthrough</c> in the data folder if null.</param>
    /// <param name="options">The explorer's settings, <see cref="DefaultOptions"/> if null.</param>
    public WorldGraph(PluginSet plugins, string? cacheDirectory = null, Reach.Options? options = null)
    {
        _plugins = plugins;
        _options = options ?? DefaultOptions;
        _cache = cacheDirectory ?? Path.Combine(DataPaths.Root, "cache", "playthrough");
        _portals = new(FindPortals);
        _fingerprint = new(() => ComputeFingerprint(plugins, _options));
    }

    public PluginSet Plugins => _plugins;

    /// <summary>
    /// What the cache is keyed by: a hash of the code's graph format, the explorer's settings and, for each plugin, its manifest and
    /// its files' names and sizes, and (for any but the base plugin, which an import writes afresh and which a re-import of the same
    /// ROM by the same importer reproduces) their times. A change to any plugin that changes a record's size, or a mod's file at all,
    /// puts every graph out of date.
    /// </summary>
    public string Fingerprint => _fingerprint.Value;

    private static string ComputeFingerprint(PluginSet plugins, Reach.Options options)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text) => hash.AppendData(Encoding.UTF8.GetBytes(text + "\n"));
        Add($"walk{WalkGraph.Version} {options}");
        foreach (var plugin in plugins.Plugins)
        {
            var isBase = plugin.Manifest.Id == RecordId.BaseNamespace;
            Add(plugin.Manifest.Id);
            hash.AppendData(File.ReadAllBytes(Path.Combine(plugin.Directory, PluginManifest.FileName)));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plugin.Directory));
            foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories)
                .OrderBy(f => f.FullName, StringComparer.Ordinal))
                Add($"{Path.GetRelativePath(root, file.FullName)} {file.Length}{(isBase ? "" : " " + file.LastWriteTimeUtc.Ticks)}");
        }
        return Convert.ToHexString(hash.GetHashAndReset())[..32];
    }

    // The map sets' graphs.

    private string PathOf(RecordId mapSet) => Path.Combine(_cache, $"walk-{mapSet.Key}.bin");

    /// <summary>A map set's walk graph: read from the cache if it holds one for these plugins, else explored and kept.</summary>
    public WalkGraph Walk(RecordId mapSet) => _walks.GetOrAdd(mapSet, id => new Lazy<WalkGraph>(() => Make(id))).Value;

    private WalkGraph Make(RecordId mapSet)
    {
        var clock = Stopwatch.StartNew();
        var path = PathOf(mapSet);
        var graph = WalkGraph.TryLoad(path, Fingerprint);
        var cached = graph is not null;
        if (graph is null)
        {
            graph = WalkGraph.Explore(_plugins, _plugins.Get<MapSetRecord>(mapSet), _options);
            graph.Save(path, Fingerprint);
        }
        clock.Stop();
        _info[mapSet] = new WalkInfo(mapSet, clock.Elapsed.TotalSeconds, cached, new FileInfo(path).Length, graph.Count, graph.EdgeCount);
        return graph;
    }

    /// <summary>What making each map set's graph so far took.</summary>
    public IReadOnlyCollection<WalkInfo> Info => _info.Values.OrderBy(i => i.MapSet.Key.Length).ThenBy(i => i.MapSet.Key, StringComparer.Ordinal).ToList();

    /// <summary>Makes the graphs of the given map sets (the playable ones, not the Battle arenas, if none), side by side.</summary>
    public void Prepare(IEnumerable<RecordId>? mapSets = null, bool parallel = true)
    {
        var sets = (mapSets ?? _plugins.All<MapSetRecord>(RecordTypes.MapSet).Select(s => s.Id).Where(id => id != Arena.MapSet)).ToList();
        if (parallel)
            Parallel.ForEach(sets, id => Walk(id));
        else
            sets.ForEach(id => Walk(id));
    }

    // The teleporters.

    /// <summary>Every teleporter that leads somewhere, outside the Battle arenas, with where it stands and arrives and its gates.</summary>
    public IReadOnlyList<Portal> Portals => _portals.Value;

    private IReadOnlyList<Portal> FindPortals()
    {
        var found = new List<Portal>();
        foreach (var chunk in _plugins.All<ChunkRecord>(RecordTypes.Chunk))
        {
            if (chunk.MapSet == Arena.MapSet)
                continue;
            var set = _plugins.Get<MapSetRecord>(chunk.MapSet);
            foreach (var t in chunk.Objects.OfType<TeleporterObject>())
            {
                if (TeleportArrival.For(_plugins, t) is not { } arrival)
                    continue;
                var id = chunk.ObjectId(t.Index);
                var arrives = arrival.Set.Origin(arrival.Chunk) + arrival.Position;
                found.Add(new Portal(id, chunk.MapSet, set.Origin(chunk) + t.Position, t.Radius, arrival.Set.Id, arrives, t.Tangible, TeleporterGates(id, t)));
            }
        }
        return found;
    }

    private static List<RouteGate> TeleporterGates(RecordId id, TeleporterObject t)
    {
        var gates = ObjectGates(id, t);
        // A tangible teleporter takes the player through at once, skipping its secret, trap and lock (MapObjectUse.Touch).
        if (!t.Tangible)
        {
            if (t.SecretFlag is { } secret)
                gates.Add(new RouteGate(id, GateKind.Secret, secret, false, t.SecretLevel, t.SecretLevel > 0));
            if (t.TrapLevel != 0)
                gates.Add(new RouteGate(id, GateKind.Trapped, t.TrapFlag, false, t.TrapLevel, false));
            if (t.LockLevel != 0)
                gates.Add(new RouteGate(id, GateKind.Locked, t.LockFlag, false, t.LockLevel, true));
        }
        return gates;
    }

    /// <summary>
    /// The gates on a map object's being there and working (<see cref="MapObjectPresence"/>, <c>InReach</c>): its visibility flag,
    /// its enabled flag where the type reads one, and the flags that say it's spent.
    /// </summary>
    public static List<RouteGate> ObjectGates(RecordId id, MapObject o)
    {
        var gates = new List<RouteGate>();
        if (o.Visible is { } visible)
            gates.Add(new RouteGate(id, GateKind.Visible, visible.Flag, visible.Invert, 0, true));
        void Enabled()
        {
            if (o.Enabled is { } enabled)
                gates.Add(new RouteGate(id, GateKind.Enabled, enabled.Flag, enabled.Invert, 0, true));
        }
        switch (o)
        {
            case TeleporterObject or TripwireObject or ScriptObject:
                Enabled();
                break;
            case ContainerObject { Kind: ContainerKind.Chest or ContainerKind.Sack or ContainerKind.Crate or ContainerKind.Barrel } c:
                if (c.Opened is { } opened)
                    gates.Add(new RouteGate(id, GateKind.Spent, opened, true, 0, true));
                if (c.Exploded is { } exploded)
                    gates.Add(new RouteGate(id, GateKind.Spent, exploded, true, 0, true));
                break;
            case ContainerObject:
                Enabled();
                break;
            case PartyObject party:
                Enabled();
                if (party is not NpcObject && party.Used is { } used)
                    gates.Add(new RouteGate(id, GateKind.Spent, used.Flag, true, 0, true));
                break;
        }
        return gates;
    }

    /// <summary>How near the player's sphere must be to use an object, or null for a type he doesn't use by walking up to it.</summary>
    public static float? UseRange(MapObject o) => o switch
    {
        ContainerObject c => c.Reach + PlayerMotion.Radius,
        TeleporterObject or TripwireObject or ScriptObject or SavePointObject => o.Radius,
        PartyObject => PartyTalkRange,
        _ => null,
    };

    /// <summary>
    /// How near the sphere of the player gets to the middle of an npc or encounter that talks to him: its body's scaled radius plus his
    /// plus 0.5 (<see cref="Trek.Wanderers.Reaches"/>), taken at the least a body's: the party wanders, and the pilot finds it.
    /// </summary>
    public const float PartyTalkRange = 1.5f;

    // Standing near things.

    /// <summary>The player's sphere is half a unit above his feet.</summary>
    private static readonly Vector3 SphereUp = new(0, PlayerMotion.Radius, 0);

    /// <summary>
    /// The cells to stand in to be within <paramref name="range"/> of a point: those whose middle puts the sphere at least half a unit
    /// inside it, so the rest of the cell doesn't matter, else the nearest within the range. Each with its distance.
    /// </summary>
    public static IReadOnlyList<(int Cell, float Distance)> StandNear(WalkGraph graph, Vector3 target, float range)
    {
        var all = graph.Near(target.X, target.Z, range + 1)
            .Select(i => (Cell: i, Distance: Vector3.Distance(graph.Center(i) + SphereUp, target))).Where(c => c.Distance <= range).ToList();
        var inside = all.Where(c => c.Distance <= range - 0.5f).ToList();
        return inside.Count > 0 ? inside : [.. all.OrderBy(c => c.Distance).Take(1)];
    }

    private Dictionary<int, int[]> PortalCells(RecordId mapSet) =>
        _portalCells.GetOrAdd(mapSet, id => new Lazy<Dictionary<int, int[]>>(() =>
        {
            var graph = Walk(id);
            var cells = new Dictionary<int, List<int>>();
            for (var p = 0; p < Portals.Count; p++)
            {
                if (Portals[p].FromSet != id)
                    continue;
                foreach (var (cell, _) in StandNear(graph, Portals[p].At, Portals[p].Range))
                {
                    if (!cells.TryGetValue(cell, out var list))
                        cells[cell] = list = [];
                    list.Add(p);
                }
            }
            return cells.ToDictionary(c => c.Key, c => c.Value.ToArray());
        })).Value;

    /// <summary>The cell a portal puts the player in, or null if he never walked near its arrival.</summary>
    private int? ArrivalCell(int portal) =>
        _arrivals.GetOrAdd(portal, p => new Lazy<int?>(() => Walk(Portals[p].ToSet).Snap(Portals[p].Arrives - SphereUp, 4))).Value;

    /// <summary>The cell a portal's arrival is on the graph of its destination, or null if he never walked near it, for tools.</summary>
    public int? ArrivalOf(Portal portal) => ArrivalCell(IndexOf(portal));

    /// <summary>How many cells of its map set a portal can be taken from.</summary>
    public int StandingCells(Portal portal) =>
        PortalCells(portal.FromSet).Count(c => c.Value.Contains(IndexOf(portal)));

    private int IndexOf(Portal portal)
    {
        var i = 0;
        foreach (var p in Portals)
        {
            if (p.Id == portal.Id)
                return i;
            i++;
        }
        throw new ArgumentException($"{portal.Id} isn't a portal.", nameof(portal));
    }

    // Where the player is.

    /// <summary>
    /// Where the game has the player standing: where the runtime last said (<see cref="GameState.Place"/>), or a new game's start before
    /// it has.
    /// </summary>
    public Place PlaceOf(GameState state)
    {
        if (state.Place is { } place)
        {
            var set = _plugins.Get<MapSetRecord>(place.MapSet);
            var chunk = _plugins.ChunksIn(place.MapSet).First(c => c.Name == place.Chunk);
            return new Place(place.MapSet, set.Origin(chunk) + place.Position - SphereUp);
        }
        var newGame = GameSession.NewGame(_plugins);
        var (start, _, at) = GameSession.Arrival(_plugins, newGame);
        return new Place(start.Id, start.Origin(_plugins.Get<ChunkRecord>(newGame.Chunk)) + at - SphereUp);
    }

    // Routing.

    /// <summary>The map sets from which a way leads to <paramref name="goal"/> by teleporters, the goal's own included, whatever the gates.</summary>
    private HashSet<RecordId> SetsLeadingTo(RecordId goal)
    {
        var sets = new HashSet<RecordId> { goal };
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var p in Portals)
                if (sets.Contains(p.ToSet) && sets.Add(p.FromSet))
                    grew = true;
        }
        return sets;
    }

    /// <summary>
    /// The way to the place to stand to use a map object, from <paramref name="from"/>, with the gates read against
    /// <paramref name="state"/>: waypoints in each map set, the teleporters between them, and where to stand. Without one, why: the
    /// object's own gate that doesn't hold, the gates a route would cross that don't (the fewest), or that it isn't reached on foot.
    /// </summary>
    public RouteResult Route(GameState state, Place from, RecordId mapObject, RouteOptions? options = null)
    {
        var pick = options?.PickLocks ?? false;
        if (_plugins.TryGet<ChunkRecord>(ChunkRecord.ChunkOf(mapObject)) is not { } chunk || chunk.Object(ChunkRecord.IndexOf(mapObject)) is not { } o)
            return Refuse(RefusalKind.UnknownObject, $"No chunk has {mapObject}.");
        if (UseRange(o) is not { } range)
            return Refuse(RefusalKind.NotUsable, $"{mapObject} is a {o.GetType().Name.Replace("Object", "", StringComparison.Ordinal).ToLowerInvariant()}, which the player doesn't use.");
        var set = _plugins.Get<MapSetRecord>(chunk.MapSet);
        var target = set.Origin(chunk) + o.Position;
        var own = ObjectGates(mapObject, o);
        var shut = own.Where(g => g.Closed(state.Flags, pick)).ToList();
        if (shut.Count > 0)
            return Refuse(RefusalKind.ObjectGated, $"{mapObject} is gated: {string.Join("; ", shut)}.", shut);

        var goal = Walk(chunk.MapSet);
        var stand = StandNear(goal, target, range);
        if (stand.Count == 0)
            return Refuse(RefusalKind.NoStandingPlace, $"No ground the player reached within {range:0.##} of {mapObject}.");
        var goalCells = stand.ToDictionary(s => s.Cell, s => s.Distance);

        if (_plugins.TryGet<MapSetRecord>(from.MapSet) is null || Walk(from.MapSet).Snap(from.Feet, 6) is not { } startCell)
            return Refuse(RefusalKind.StartOffGraph, $"{from.Feet} in {from.MapSet} isn't on a walk graph.");

        var found = Search(state, from.MapSet, startCell, chunk.MapSet, goalCells, target, range, pick, options, tolerate: false);
        if (found is null)
        {
            found = Search(state, from.MapSet, startCell, chunk.MapSet, goalCells, target, range, pick, options, tolerate: true);
            if (found is null)
                return Refuse(RefusalKind.NotOnFoot, $"{mapObject} isn't reached on foot from {from.MapSet} at {from.Feet}, even with every gate open.");
            var blockers = found.Value.Portals.SelectMany(p => Portals[p].Gates).Where(g => g.Closed(state.Flags, pick)).ToList();
            return Refuse(RefusalKind.Gated, $"The way to {mapObject} crosses closed gates: {string.Join("; ", blockers)}.", blockers);
        }
        return new RouteResult(Assemble(state, mapObject, own, found.Value, goalCells, range, target), null);

        static RouteResult Refuse(RefusalKind kind, string message, IReadOnlyList<RouteGate>? blockers = null) =>
            new(null, new RouteRefusal(kind, message, blockers ?? []));
    }

    /// <summary>The route from the place the game has the player at (<see cref="PlaceOf"/>).</summary>
    public RouteResult Route(GameState state, RecordId mapObject, RouteOptions? options = null) => Route(state, PlaceOf(state), mapObject, options);

    /// <summary>A route found: its cells, with the portal each was reached through (or -1 where it was walked to), and its cost.</summary>
    private readonly record struct Found(List<(int Set, int Cell, int Portal)> Path, float Cost)
    {
        public IEnumerable<int> Portals => Path.Select(p => p.Portal).Where(p => p >= 0);
    }

    private readonly List<RecordId> _setList = [];

    private int SetIndex(RecordId set)
    {
        lock (_setList)
        {
            var i = _setList.IndexOf(set);
            if (i >= 0)
                return i;
            _setList.Add(set);
            return _setList.Count - 1;
        }
    }

    private static long Pack(int set, int cell) => ((long)set << 32) | (uint)cell;

    private Found? Search(GameState state, RecordId startSet, int startCell, RecordId goalSet, Dictionary<int, float> goalCells, Vector3 target,
        float range, bool pick, RouteOptions? options, bool tolerate)
    {
        // The ground within the avoided places' radius is left out, as where a pilot found itself stuck.
        var avoided = new Dictionary<int, HashSet<int>>();
        var zones = (options?.Avoid ?? []).Select(a => new RouteZone(a, options!.AvoidRadius)).Concat(options?.KeepClear ?? []);
        foreach (var (avoid, radius) in zones)
        {
            var graph = Walk(avoid.MapSet);
            var cells = avoided.GetValueOrDefault(SetIndex(avoid.MapSet)) ?? (avoided[SetIndex(avoid.MapSet)] = []);
            foreach (var i in graph.Near(avoid.Feet.X, avoid.Feet.Z, radius))
                if (Vector3.Distance(graph.Center(i), avoid.Feet) <= radius)
                    cells.Add(i);
        }

        var leading = SetsLeadingTo(goalSet);
        var open = new Dictionary<int, float>();
        float Toll(int p)
        {
            if (open.TryGetValue(p, out var toll))
                return toll;
            var closed = Portals[p].Gates.Count(g => g.Closed(state.Flags, pick));
            return open[p] = closed == 0 ? 0 : tolerate ? closed * ClosedCost : float.PositiveInfinity;
        }

        var graphs = new Dictionary<int, WalkGraph>();
        WalkGraph GraphOf(int set) => graphs.TryGetValue(set, out var g) ? g : graphs[set] = Walk(_setList[set]);
        var goalIndex = SetIndex(goalSet);
        var start = Pack(SetIndex(startSet), startCell);
        var cost = new Dictionary<long, float> { [start] = 0 };
        var parent = new Dictionary<long, (long From, int Portal)>();
        var queue = new PriorityQueue<long, float>();
        var done = new HashSet<long>();
        queue.Enqueue(start, 0);

        float Heuristic(int set, int cell) => set == goalIndex
            ? MathF.Max(0, Vector3.Distance(GraphOf(set).Center(cell) + SphereUp, target) - range) : 0;

        while (queue.TryDequeue(out var node, out _))
        {
            if (!done.Add(node))
                continue;
            var (set, cell) = ((int)(node >> 32), (int)(uint)node);
            var graph = GraphOf(set);
            if (set == goalIndex && goalCells.ContainsKey(cell))
            {
                var path = new List<(int, int, int)>();
                for (var n = node; ; )
                {
                    var known = parent.TryGetValue(n, out var p);
                    path.Add(((int)(n >> 32), (int)(uint)n, known ? p.Portal : -1));
                    if (!known)
                        break;
                    n = p.From;
                }
                path.Reverse();
                return new Found(path, cost[node]);
            }

            void Relax(long next, float step, int portal)
            {
                var g = cost[node] + step;
                if (!done.Contains(next) && (!cost.TryGetValue(next, out var known) || g < known))
                {
                    cost[next] = g;
                    parent[next] = (node, portal);
                    queue.Enqueue(next, g + Heuristic((int)(next >> 32), (int)(uint)next));
                }
            }

            avoided.TryGetValue(set, out var skip);
            foreach (var to in graph.Steps(cell))
                if (skip is null || !skip.Contains(to))
                    Relax(Pack(set, to), graph.Length(cell, to), -1);

            if (PortalCells(_setList[set]).TryGetValue(cell, out var here))
            {
                foreach (var p in here)
                {
                    var toll = Toll(p);
                    if (float.IsPositiveInfinity(toll) || !leading.Contains(Portals[p].ToSet) || ArrivalCell(p) is not { } arrival)
                        continue;
                    Relax(Pack(SetIndex(Portals[p].ToSet), arrival), TeleportCost + toll, p);
                }
            }
        }
        return null;
    }

    private WorldRoute Assemble(GameState state, RecordId target, List<RouteGate> own, Found found, Dictionary<int, float> goalCells, float range, Vector3 at)
    {
        var legs = new List<RouteLeg>();
        var gates = new List<RouteGate>();
        var cells = new List<int>();
        var current = _setList[found.Path[0].Set];
        foreach (var (set, cell, portal) in found.Path)
        {
            if (portal >= 0)
            {
                // The cell reached through a teleporter starts the next leg, whether it is another map set or the same one.
                var p = Portals[portal];
                legs.Add(new RouteLeg(current, Simplify(Walk(current), cells), p.Id, p.Arrives));
                gates.AddRange(p.Gates);
                cells = [];
                current = _setList[set];
            }
            cells.Add(cell);
        }
        legs.Add(new RouteLeg(current, Simplify(Walk(current), cells), null, null));
        gates.AddRange(own);
        var standCell = found.Path[^1].Cell;
        return new WorldRoute(target, legs, Walk(current).Center(standCell), range, goalCells[standCell], found.Cost, gates);
    }

    /// <summary>The most cells in a straight line a leg's waypoints skip, so the pilot, who drifts off the line, is steered often.</summary>
    private const int MaxStraight = 3;

    /// <summary>The cells of a leg as waypoints: those where the way turns, in the world's coordinates, the last always.</summary>
    private static List<Vector3> Simplify(WalkGraph graph, List<int> cells)
    {
        var points = new List<Vector3>();
        var run = 0;
        for (var i = 0; i < cells.Count; i++)
        {
            var here = graph.Cell(cells[i]);
            var straight = i > 0 && i < cells.Count - 1 && cells[i] != cells[i - 1] && run < MaxStraight &&
                Step(graph.Cell(cells[i - 1]), here) == Step(here, graph.Cell(cells[i + 1]));
            run = straight ? run + 1 : 0;
            if (!straight && (i == 0 || cells[i] != cells[i - 1]))
                points.Add(graph.Center(cells[i]));
        }
        return points;

        static (int, int, int) Step(Reach.Cell a, Reach.Cell b) => (b.X - a.X, b.Z - a.Z, b.Y - a.Y);
    }

    // Reachability.

    /// <summary>
    /// Whether the player, where the game has him, can walk and teleport to a map object's place to use it: its own gates hold, and
    /// every teleporter on a way there is there and works. The planner's question; many asks of one game cost one flood.
    /// </summary>
    public bool CanReach(RecordId mapObject, GameState state, RouteOptions? options = null) => CanReach(mapObject, state, PlaceOf(state), options);

    /// <summary>As <see cref="CanReach(RecordId, GameState, RouteOptions?)"/> from <paramref name="from"/>.</summary>
    public bool CanReach(RecordId mapObject, GameState state, Place from, RouteOptions? options = null)
    {
        var pick = options?.PickLocks ?? false;
        if (_plugins.TryGet<ChunkRecord>(ChunkRecord.ChunkOf(mapObject)) is not { } chunk || chunk.Object(ChunkRecord.IndexOf(mapObject)) is not { } o
            || UseRange(o) is not { } range)
            return false;
        if (ObjectGates(mapObject, o).Any(g => g.Closed(state.Flags, pick)))
            return false;
        var visited = Closure(state, from, pick);
        if (visited is null || !visited.TryGetValue(chunk.MapSet, out var seen))
            return false;
        var set = _plugins.Get<MapSetRecord>(chunk.MapSet);
        return StandNear(Walk(chunk.MapSet), set.Origin(chunk) + o.Position, range).Any(s => seen[s.Cell]);
    }

    /// <summary>Every cell the player can reach from a place with the gates as they are, by map set; kept for the next ask under the same gates.</summary>
    private Dictionary<RecordId, bool[]>? Closure(GameState state, Place from, bool pick)
    {
        var key = new StringBuilder().Append(from.MapSet).Append('@').Append(from.Feet.ToString()).Append(pick ? 'p' : '-');
        foreach (var p in Portals)
            key.Append(p.Gates.Any(g => g.Closed(state.Flags, pick)) ? '0' : '1');
        var text = key.ToString();
        lock (_closureLock)
        {
            if (_closures.TryGetValue(text, out var known))
                return known;
        }
        if (Walk(from.MapSet).Snap(from.Feet, 6) is not { } startCell)
            return null;
        var visited = new Dictionary<RecordId, bool[]> { [from.MapSet] = new bool[Walk(from.MapSet).Count] };
        var queue = new Queue<(RecordId Set, int Cell)>();
        visited[from.MapSet][startCell] = true;
        queue.Enqueue((from.MapSet, startCell));
        while (queue.TryDequeue(out var node))
        {
            var graph = Walk(node.Set);
            var seen = visited[node.Set];
            foreach (var to in graph.Steps(node.Cell))
            {
                if (!seen[to])
                {
                    seen[to] = true;
                    queue.Enqueue((node.Set, to));
                }
            }
            if (!PortalCells(node.Set).TryGetValue(node.Cell, out var here))
                continue;
            foreach (var p in here)
            {
                if (Portals[p].Gates.Any(g => g.Closed(state.Flags, pick)) || ArrivalCell(p) is not { } arrival)
                    continue;
                var next = Portals[p].ToSet;
                if (!visited.TryGetValue(next, out var seenThere))
                    visited[next] = seenThere = new bool[Walk(next).Count];
                if (!seenThere[arrival])
                {
                    seenThere[arrival] = true;
                    queue.Enqueue((next, arrival));
                }
            }
        }
        lock (_closureLock)
        {
            if (_closures.Count > 16)
                _closures.Clear();
            _closures[text] = visited;
        }
        return visited;
    }
}
