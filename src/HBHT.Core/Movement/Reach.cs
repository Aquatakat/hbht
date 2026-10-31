using System.Numerics;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.MapObjects;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Core.Movement;

/// <summary>
/// Where the player can get to in a map set on foot, under a movement and a pace: the reachability check that Modern movement is
/// held to (docs/systems/movement.md#the-reachability-check), which <c>hbht movement reach</c> runs. From the places play puts him,
/// he runs in a dozen ways for two seconds, from wherever he comes to rest again, until nothing new is reached. He's
/// <see cref="PlayerMotion"/> with Alaron's run, in the 3 × 3 chunks <see cref="ChunkStreamer"/> loads about him as trek does,
/// so the check plays by the game's own rules.
/// </summary>
/// <remarks>
/// A cell is reached where his feet pass over it on the ground, a unit square and a unit of height. He sets off again only from
/// where he came to rest with the stick let go, so a slope he climbs a little way and slides back down from doesn't count as a
/// place he can start from. The map set is a new game's: its toggles and triggers are left as they load, and teleporters don't
/// take him anywhere, each map set being reached from all of their arrivals.
/// </remarks>
public sealed class Reach
{
    /// <summary>How a map set is explored.</summary>
    /// <param name="Ways">How many ways he runs from each place he comes to rest.</param>
    /// <param name="RunTicks">How long each run is held.</param>
    /// <param name="RestTicks">The most he's given to come to rest after a run, the stick let go.</param>
    /// <param name="StartCell">The size of the cells he sets off from, in X, Z and height: one run a way from each.</param>
    /// <param name="Within">Explores only within this box, in the world's coordinates, if given.</param>
    public sealed record Options(MovementMode Movement, float Pace = 1, ModernRules? Rules = null, int Ways = 12, int RunTicks = 120,
        int RestTicks = 90, float StartCell = 2, (Vector3 Min, Vector3 Max)? Within = null);

    /// <summary>A unit cell of the world: floored X, Z and height.</summary>
    public readonly record struct Cell(int X, int Z, int Y)
    {
        public static Cell Of(Vector3 p, float size = 1) =>
            new((int)MathF.Floor(p.X / size), (int)MathF.Floor(p.Z / size), (int)MathF.Floor(p.Y / size));
    }

    /// <summary>Alaron's actor, whose run moves him.</summary>
    public static readonly RecordId Alaron = RecordId.Hex(RecordTypes.Actor, 0x25F9);

    private Reach(MapSetRecord set, Dictionary<Cell, (Vector3, Vector2)> cells, Dictionary<Vector3, (Vector3, Vector2)> parents, int runs)
    {
        _parents = parents;
        Set = set;
        Cells = cells.Keys.ToHashSet();
        _via = cells;
        Runs = runs;
    }

    public MapSetRecord Set { get; }

    /// <summary>The cells his feet reached on the ground.</summary>
    public IReadOnlySet<Cell> Cells { get; }

    /// <summary>How many runs it took.</summary>
    public int Runs { get; }

    private readonly Dictionary<Cell, (Vector3, Vector2)> _via;

    /// <summary>The run each place he set off from came to rest at the end of.</summary>
    private readonly Dictionary<Vector3, (Vector3, Vector2)> _parents;

    /// <summary>The places he came to rest at and set off from again.</summary>
    public IReadOnlyCollection<Vector3> Rests => _parents.Keys;

    /// <summary>
    /// The runs that took him to a cell, the last first: where each set off from and the way it ran, back to one of the places
    /// play puts him, for following them.
    /// </summary>
    public IEnumerable<(Vector3 From, Vector2 Way)> Path(Cell cell)
    {
        var run = _via[cell];
        while (true)
        {
            yield return run;
            if (!_parents.TryGetValue(run.Item1, out run))
                yield break;
        }
    }

    /// <summary>
    /// The places play puts the player in a map set, in the world's coordinates, before he's set down: every teleporter's arrival
    /// into it, the places it puts him entering by number (<see cref="MapSetRecord.Spawns"/>, the Overworld's portal 0 a new
    /// game's start), and its save points. Each comes with the height to set him down near, or none to set him down from on high.
    /// </summary>
    public static IReadOnlyList<(Vector3 At, float? Near)> Starts(PluginSet plugins, MapSetRecord set)
    {
        // The Battle arenas are no one's arrival: combat puts bodies in them.
        if (set.Id == Arena.MapSet)
            return ArenaStarts(plugins);
        var starts = new List<(Vector3, float?)>();
        foreach (var map in plugins.All<MapRecord>(RecordTypes.Map))
        {
            foreach (var teleporter in map.Objects.OfType<TeleporterObject>())
            {
                if (teleporter.Destination.MapSet != set.Id || TeleportArrival.For(plugins, teleporter) is not { } arrival)
                    continue;
                var at = set.Origin(arrival.Chunk.Row, arrival.Chunk.Column) + arrival.Position;
                starts.Add((at, at.Y - PlayerMotion.Radius));
            }
        }
        foreach (var spawn in set.Spawns ?? [])
            starts.Add((set.Origin(spawn.Row, spawn.Column) + new Vector3(spawn.Position.X, 0, spawn.Position.Y), null));
        foreach (var chunk in set.Chunks)
        {
            foreach (var save in plugins.Get<MapRecord>(chunk.Map).Objects.OfType<SavePointObject>())
            {
                var at = set.Origin(chunk.Row, chunk.Column) + save.Position;
                starts.Add((at, at.Y - PlayerMotion.Radius));
            }
        }
        return starts;
    }

    /// <summary>
    /// The places combat puts a body in each of the Battle arenas, in the world's coordinates, as <see cref="Body.Spawn"/> does
    /// (<c>Combat.CreateBody</c>): the arena's 40 spawns, <c>good0a</c> to <c>bad4d</c>, which the party, the enemies and their
    /// reserves are made at, each a unit up at its point's cell (<see cref="Battle.CellOf"/>), set down from on high. Chunks sharing
    /// a map are the same arena, so only the first of them is taken.
    /// </summary>
    public static IReadOnlyList<(Vector3 At, float? Near)> ArenaStarts(PluginSet plugins) =>
        [.. ArenaPlacements(plugins).Where(p => !p.Name.StartsWith("flee", StringComparison.Ordinal)).Select(p => (p.At, (float?)null))];

    /// <summary>
    /// The names and places combat puts bodies in the arenas, with the arena's number: <see cref="ArenaStarts"/>' spawns, and the
    /// flee points, <c>flee0</c> to <c>flee7</c>, which fleeing enemies walk to (leaving within 1.5 of one) and nothing is made at.
    /// </summary>
    public static IEnumerable<(int Arena, string Name, Vector3 At)> ArenaPlacements(PluginSet plugins)
    {
        var maps = new HashSet<RecordId>();
        for (var number = 0; number < Arena.Count; number++)
        {
            if (Arena.For(plugins, number) is not { } arena || !maps.Add(arena.Map.Id))
                continue;
            var names = new List<string>();
            foreach (var good in (ReadOnlySpan<bool>)[true, false])
                for (var set = 0; set < Battle.SpawnSets; set++)
                    for (var letter = 0; letter < Battle.OnField; letter++)
                        names.Add(Battle.SpawnPoint(good, set, letter));
            names.AddRange(Enumerable.Range(0, Battle.MaxFleePoints).Select(Battle.FleePoint));
            foreach (var name in names)
                if (arena.Point(name) is { } point)
                    yield return (number, name, Battle.BodyStart(arena.Origin, Battle.CellOf(point)));
        }
    }

    /// <summary>
    /// Where he is set down from a start: as <see cref="Body.SetDown"/> does for the player's arrivals, and for the rest, with the
    /// tries to the side that the original's set-down makes when the sweep misses (a spawn on a triangle's edge, as the arenas'
    /// are, can miss by rounding), drawn here from a generator of its own, if <paramref name="sideways"/>: the arenas' only, so the
    /// other map sets' starts are the ones the check has always had.
    /// </summary>
    public static (Vector3 Ground, bool Grounded) SetDownAt(CollisionWorld world, Vector3 at, float? near, bool sideways = false)
    {
        var (ground, grounded) = Body.SetDown(world, at, PlayerMotion.Radius, null, near);
        if (!grounded && near is not null)
            (ground, grounded) = Body.SetDown(world, ground, PlayerMotion.Radius, null);
        return grounded || !sideways ? (ground, grounded) : Body.SetDown(world, at, PlayerMotion.Radius, new Lcg(0));
    }

    /// <summary>Explores a map set from <paramref name="starts"/> (<see cref="Starts"/>).</summary>
    public static Reach Explore(PluginSet plugins, MapSetRecord set, IEnumerable<(Vector3 At, float? Near)> starts, Options options)
    {
        var world = new CollisionWorld();
        var chunks = new ChunkStreamer(plugins, GameState.NewGame(plugins), world, set);
        var actor = plugins.Get<ActorRecord>(Alaron);
        var clips = ActorClips.For(plugins, actor.Scene, actor.RootTransform)
            ?? throw new InvalidDataException($"{Alaron} has no animations.");
        var cells = new Dictionary<Cell, (Vector3, Vector2)>();
        var started = new HashSet<Cell>();
        // Where he's still to set off from, by the chunk it's in: each chunk's are run with the 3 × 3 about it loaded.
        var pending = new SortedDictionary<(int Row, int Column), List<Vector3>>();
        var parents = new Dictionary<Vector3, (Vector3, Vector2)>();
        var runs = 0;

        void Queue(Vector3 at, (Vector3, Vector2)? parent)
        {
            if (!Inside(at, options) || chunks.ChunkAt(at) is not { } chunk || !started.Add(Cell.Of(at, options.StartCell)))
                return;
            if (parent is { } run)
                parents.TryAdd(at, run);
            if (!pending.TryGetValue((chunk.Row, chunk.Column), out var list))
                pending[(chunk.Row, chunk.Column)] = list = [];
            list.Add(at);
        }

        foreach (var (at, near) in starts)
        {
            if (!Inside(at, options))
                continue;
            chunks.Update(at);
            var (ground, grounded) = SetDownAt(world, at, near, sideways: set.Id == Arena.MapSet);
            if (grounded && Rest(world, ground, Vector2.UnitY, options, clips, actor, null) is { } rest)
                Queue(rest, null);
        }

        var ways = Enumerable.Range(0, options.Ways)
            .Select(i => new Vector2(MathF.Sin(i * MathF.Tau / options.Ways), MathF.Cos(i * MathF.Tau / options.Ways))).ToArray();
        while (pending.Count > 0)
        {
            var (key, from) = pending.First();
            pending.Remove(key);
            chunks.Update(set.Origin(key.Row, key.Column) + new Vector3(set.CellSize / 2, 0, set.CellSize / 2));
            var results = new (List<Cell> Cells, Vector3? Stop)[from.Count * ways.Length];
            Parallel.For(0, results.Length, i =>
            {
                var passed = new List<Cell>();
                results[i] = (passed, Run(world, from[i / ways.Length], ways[i % ways.Length], options, clips, actor, passed));
            });
            runs += results.Length;
            for (var i = 0; i < results.Length; i++)
            {
                var (passed, rest) = results[i];
                foreach (var cell in passed)
                    cells.TryAdd(cell, (from[i / ways.Length], ways[i % ways.Length]));
                if (rest is { } at)
                    Queue(at, (from[i / ways.Length], ways[i % ways.Length]));
            }
        }
        return new Reach(set, cells, parents, runs);
    }

    private static bool Inside(Vector3 at, Options options) =>
        options.Within is not var (min, max) || (at.X >= min.X && at.Y >= min.Y && at.Z >= min.Z && at.X <= max.X && at.Y <= max.Y && at.Z <= max.Z);

    /// <summary>How far below his sphere something must be for him to stand on it, for the check.</summary>
    private const float Underfoot = 0.1f;

    /// <summary>
    /// Whether he stands on something: anything within <see cref="Underfoot"/> below his sphere, wall or ground, plain or not,
    /// judged the same way under either movement rather than by each one's <see cref="PlayerMotion.Grounded"/>.
    /// </summary>
    private static bool Standing(CollisionWorld world, PlayerMotion player) =>
        SphereCast.Cast(world, player.Sphere.Position, new Vector3(0, -Underfoot, 0), PlayerMotion.Radius, player.Sphere.IgnoresSeeThrough,
            _ => true, out _);

    private static PlayerMotion Player(Vector3 at, Vector2 way, Options options) =>
        new(at, way) { Movement = options.Movement, Pace = options.Pace, Rules = options.Rules ?? ModernRules.Default };

    /// <summary>One run from where he rests, the way given, then let go: where he comes to rest, or null if he fell out of the world or never did.</summary>
    private static Vector3? Run(CollisionWorld world, Vector3 from, Vector2 way, Options options, ActorClips clips, ActorRecord actor, List<Cell> passed)
    {
        var player = Player(from, way, options);
        var animation = new ActorMotion(actor, clips, new Lcg(0));
        for (var i = 0; i < options.RunTicks; i++)
        {
            player.Step(world, new MoveInput(Vector2.UnitY), way, animation);
            if (player.FellOut)
                return null;
            if (Standing(world, player) && Inside(player.Feet, options))
                passed.Add(Cell.Of(player.Feet));
        }
        return Rest(player, world, way, options, animation, passed);
    }

    private static Vector3? Rest(CollisionWorld world, Vector3 at, Vector2 way, Options options, ActorClips clips, ActorRecord actor, List<Cell>? passed) =>
        Rest(Player(at, way, options), world, way, options, new ActorMotion(actor, clips, new Lcg(0)), passed);

    /// <summary>The ticks he must stand on something within <see cref="Still"/> of one place to be at rest.</summary>
    private const int RestingTicks = 10;

    /// <summary>
    /// How far he may move while at rest: 1.0's sphere against plain collision is pushed down each frame and bobs by about 0.03,
    /// and is as much at rest as one that doesn't.
    /// </summary>
    private const float Still = 0.05f;

    /// <summary>The stick let go until he stands still on something: where, or null if he never does or falls out of the world.</summary>
    private static Vector3? Rest(PlayerMotion player, CollisionWorld world, Vector2 way, Options options, ActorMotion animation, List<Cell>? passed)
    {
        var still = 0;
        var at = player.Sphere.Position;
        for (var i = 0; i < options.RestTicks && still < RestingTicks; i++)
        {
            player.Step(world, new MoveInput(Vector2.Zero), way, animation);
            if (player.FellOut)
                return null;
            var standing = Standing(world, player);
            if (standing && Inside(player.Feet, options))
                passed?.Add(Cell.Of(player.Feet));
            if (standing && Vector3.Distance(at, player.Sphere.Position) < Still)
                still++;
            else
                (still, at) = (0, player.Sphere.Position);
        }
        return still >= RestingTicks ? player.Sphere.Position : null;
    }

    /// <summary>Whether this reached a cell or one beside it, across or up and down, by a cell at most.</summary>
    public bool Covers(Cell cell)
    {
        for (var x = -1; x <= 1; x++)
            for (var z = -1; z <= 1; z++)
                for (var y = -1; y <= 1; y++)
                    if (Cells.Contains(new Cell(cell.X + x, cell.Z + z, cell.Y + y)))
                        return true;
        return false;
    }

    /// <summary>
    /// What this reaches that <paramref name="other"/> doesn't, in clusters of touching cells: a cell counts as reached by both
    /// if the other reached one beside it, across or up and down, by a cell at most. Each cluster's rise is how far its highest
    /// cell is above the nearest the other reached within <paramref name="around"/> across the ground, or null with none there.
    /// The highest rise first.
    /// </summary>
    public IReadOnlyList<Cluster> Beyond(Reach other, int around = 3)
    {
        var columns = new Dictionary<(int X, int Z), List<int>>();
        foreach (var c in other.Cells)
        {
            if (!columns.TryGetValue((c.X, c.Z), out var heights))
                columns[(c.X, c.Z)] = heights = [];
            heights.Add(c.Y);
        }

        bool Shared(Cell c)
        {
            for (var x = -1; x <= 1; x++)
                for (var z = -1; z <= 1; z++)
                    if (columns.TryGetValue((c.X + x, c.Z + z), out var heights) && heights.Any(y => Math.Abs(y - c.Y) <= 1))
                        return true;
            return false;
        }

        float? Rise(Cell c)
        {
            float? best = null;
            var nearest = int.MaxValue;
            for (var x = -around; x <= around; x++)
            {
                for (var z = -around; z <= around; z++)
                {
                    if (!columns.TryGetValue((c.X + x, c.Z + z), out var heights))
                        continue;
                    foreach (var y in heights)
                    {
                        var off = Math.Abs(c.Y - y);
                        if (off < nearest)
                            (nearest, best) = (off, c.Y - y);
                    }
                }
            }
            return best;
        }

        var only = Cells.Where(c => !Shared(c)).ToHashSet();
        var clusters = new List<Cluster>();
        var seen = new HashSet<Cell>();
        foreach (var start in only.OrderBy(c => c.X).ThenBy(c => c.Z).ThenBy(c => c.Y))
        {
            if (!seen.Add(start))
                continue;
            var members = new List<Cell>();
            var queue = new Queue<Cell>([start]);
            while (queue.TryDequeue(out var c))
            {
                members.Add(c);
                for (var x = -1; x <= 1; x++)
                    for (var z = -1; z <= 1; z++)
                        for (var y = -1; y <= 1; y++)
                            if (c with { X = c.X + x, Z = c.Z + z, Y = c.Y + y } is var n && only.Contains(n) && seen.Add(n))
                                queue.Enqueue(n);
            }
            var top = members.MaxBy(c => c.Y);
            var rises = members.Select(Rise).Where(r => r is not null).Select(r => r!.Value).ToList();
            clusters.Add(new Cluster(members, top, rises.Count > 0 ? rises.Max() : null));
        }
        return [.. clusters.OrderByDescending(c => c.Rise ?? float.MaxValue).ThenByDescending(c => c.Cells.Count)];
    }

    /// <summary>Touching cells one reach has that another hasn't.</summary>
    /// <param name="Top">The highest of them.</param>
    /// <param name="Rise">How far the highest is above the nearest the other reached about it, or null where it reached nothing near.</param>
    public sealed record Cluster(IReadOnlyList<Cell> Cells, Cell Top, float? Rise);
}
