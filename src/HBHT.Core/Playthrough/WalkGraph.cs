using System.IO.Compression;
using System.Numerics;
using HBHT.Core.Movement;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>
/// Where the player can walk in one map set: the cells his feet reached (<see cref="Reach"/>, 1 × 1 × 1 in the world's
/// coordinates) and the steps he made from one to the next. A cell is a node, and a step a directed edge, so a ledge he dropped off
/// has the way down and no way up unless some run climbed it. Built from <see cref="Reach.Explore"/> at the original's movement and
/// pace, kept as flat arrays, and written to a cache file (<see cref="Save"/>, <see cref="TryLoad"/>).
/// </summary>
/// <remarks>
/// Cells joined by the steps the runs made, rather than by neighbouring cells, so an edge is always something the real
/// <see cref="PlayerMotion"/> did and a stick pointed at the next cell's centre from this one's follows it. Two cells that touch but were
/// never stepped between (a wall's two sides) aren't joined.
/// </remarks>
public sealed class WalkGraph
{
    /// <summary>The cache file's format, and the explorer's settings it was made with: bump it when either changes.</summary>
    internal const int Version = 5;

    private readonly int[] _x, _z, _y;
    private readonly byte[] _fx, _fz, _fy;   // where in the cell the feet were on average, in 1/255 of it
    private readonly int[] _start;   // CSR: _start[i].._start[i + 1] index _to
    private readonly int[] _to;
    private readonly Dictionary<int, (int To, int Toggle)[]> _gated;
    private readonly ulong[] _hasGated;   // a bit a cell: whether it's in _gated, which the floods ask of every cell
    private Dictionary<long, int>? _index;
    private Dictionary<long, int[]>? _columns;

    private WalkGraph(RecordId mapSet, int[] x, int[] z, int[] y, byte[] fx, byte[] fy, byte[] fz, int[] start, int[] to,
        RecordId[] toggles, (int From, int To, int Toggle)[] gated)
    {
        MapSet = mapSet;
        (_x, _z, _y, _fx, _fy, _fz, _start, _to) = (x, z, y, fx, fy, fz, start, to);
        Toggles = toggles;
        GatedCount = gated.Length;
        _gated = gated.GroupBy(g => g.From).ToDictionary(g => g.Key, g => g.Select(e => (e.To, e.Toggle)).ToArray());
        _hasGated = new ulong[(x.Length + 63) / 64];
        foreach (var from in _gated.Keys)
            _hasGated[from >> 6] |= 1UL << from;
    }

    public RecordId MapSet { get; }

    /// <summary>
    /// The map set's tripwires that change collision whose open toggles let the player take <see cref="GatedSteps"/>: a gated step's
    /// toggle is an index into these.
    /// </summary>
    public IReadOnlyList<RecordId> Toggles { get; }

    /// <summary>How many gated steps.</summary>
    public int GatedCount { get; }

    /// <summary>
    /// The steps from <paramref name="i"/> only an open toggle lets the player take (a gate's wall opened by its tripwire, the Jundar
    /// gate among them), each with the index of its toggle's tripwire in <see cref="Toggles"/>. Ground only they lead to is joined
    /// by plain <see cref="Steps"/>.
    /// </summary>
    public ReadOnlySpan<(int To, int Toggle)> GatedSteps(int i) =>
        (_hasGated[i >> 6] & (1UL << i)) != 0 && _gated.TryGetValue(i, out var steps) ? steps : default;

    /// <summary>Every cell with a gated step from it.</summary>
    public IEnumerable<int> GatedFrom => _gated.Keys;

    /// <summary>How many cells.</summary>
    public int Count => _x.Length;

    /// <summary>How many steps.</summary>
    public int EdgeCount => _to.Length;

    public Reach.Cell Cell(int i) => new(_x[i], _z[i], _y[i]);

    /// <summary>
    /// Where the pilot steers for cell <paramref name="i"/>: where his feet were in it on average, in the world's coordinates, which is
    /// a place he stood and not the cell's middle, where he ran along a wall.
    /// </summary>
    public Vector3 Center(int i) => new(_x[i] + _fx[i] / 255f, _y[i] + _fy[i] / 255f, _z[i] + _fz[i] / 255f);

    /// <summary>The cells a step from <paramref name="i"/> reaches.</summary>
    public ReadOnlySpan<int> Steps(int i) => _to.AsSpan(_start[i], _start[i + 1] - _start[i]);

    private static long Key(int x, int z, int y) => ((long)(x + (1 << 20)) << 42) | ((long)(z + (1 << 20)) << 21) | (uint)(y + (1 << 20));

    private static long ColumnKey(int x, int z) => ((long)(x + (1 << 20)) << 21) | (uint)(z + (1 << 20));

    /// <summary>The index of a cell, or null if the player never stood in it.</summary>
    public int? Find(Reach.Cell cell)
    {
        _index ??= BuildIndex();
        return _index.TryGetValue(Key(cell.X, cell.Z, cell.Y), out var i) ? i : null;
    }

    private Dictionary<long, int> BuildIndex()
    {
        var index = new Dictionary<long, int>(Count);
        for (var i = 0; i < Count; i++)
            index[Key(_x[i], _z[i], _y[i])] = i;
        return index;
    }

    private Dictionary<long, int[]> Columns()
    {
        if (_columns is { } built)
            return built;
        var lists = new Dictionary<long, List<int>>();
        for (var i = 0; i < Count; i++)
        {
            var key = ColumnKey(_x[i], _z[i]);
            if (!lists.TryGetValue(key, out var list))
                lists[key] = list = [];
            list.Add(i);
        }
        return _columns = lists.ToDictionary(l => l.Key, l => l.Value.ToArray());
    }

    /// <summary>The cells in the columns within <paramref name="across"/> cells of (<paramref name="x"/>, <paramref name="z"/>), in the world's units.</summary>
    public IEnumerable<int> Near(float x, float z, float across)
    {
        var columns = Columns();
        var (x0, x1) = ((int)MathF.Floor(x - across), (int)MathF.Floor(x + across));
        var (z0, z1) = ((int)MathF.Floor(z - across), (int)MathF.Floor(z + across));
        for (var cx = x0; cx <= x1; cx++)
            for (var cz = z0; cz <= z1; cz++)
                if (columns.TryGetValue(ColumnKey(cx, cz), out var list))
                    foreach (var i in list)
                        yield return i;
    }

    /// <summary>
    /// The cell nearest a place, by the distance from its middle to <paramref name="feet"/> with the height counted double, within
    /// <paramref name="within"/>: null if the player never walked there.
    /// </summary>
    public int? Snap(Vector3 feet, float within = 4)
    {
        int? best = null;
        var bestDistance = float.MaxValue;
        foreach (var i in Near(feet.X, feet.Z, within))
        {
            var c = Center(i);
            var d = new Vector3(c.X - feet.X, (c.Y - feet.Y) * 2, c.Z - feet.Z).Length();
            if (d <= within && d < bestDistance)
                (best, bestDistance) = (i, d);
        }
        if (best is not null)
            return best;
        // A place above the ground, as a new game's start is before he is set down: the highest cell under it within a unit across.
        foreach (var i in Near(feet.X, feet.Z, 1))
            if (_y[i] + 0.5f <= feet.Y + 1 && (best is null || _y[i] > _y[best.Value]))
                best = i;
        return best;
    }

    /// <summary>
    /// The cell to start a way from at a place: the nearest, as <see cref="Snap"/> finds it, that some step leads on from. A run only
    /// set off again from one rest in each start cell (<see cref="Reach.Options.StartCell"/>), so where one came to rest beside it is a
    /// cell steps lead into and none out of, and a way from it goes nowhere.
    /// </summary>
    public int? SnapStart(Vector3 feet, float within = 4)
    {
        int? best = null;
        var bestDistance = float.MaxValue;
        foreach (var i in Near(feet.X, feet.Z, within))
        {
            if (_start[i + 1] == _start[i] && !_gated.ContainsKey(i))
                continue;
            var c = Center(i);
            var d = new Vector3(c.X - feet.X, (c.Y - feet.Y) * 2, c.Z - feet.Z).Length();
            if (d <= within && d < bestDistance)
                (best, bestDistance) = (i, d);
        }
        return best ?? Snap(feet, within);
    }

    /// <summary>What the pilot follows from one cell to another: how far apart their middles are.</summary>
    public float Length(int from, int to) => Vector3.Distance(Center(from), Center(to));

    // Building.

    /// <summary>
    /// Explores a map set from where play puts the player in it (<see cref="Reach.Starts"/>) and keeps what he reached, then explores
    /// about each of its collision toggles (<see cref="TogglesIn"/>) with the toggle opened, and keeps the steps that only an open toggle
    /// lets him take as <see cref="GatedSteps"/>. Takes minutes for the larger sets.
    /// </summary>
    /// <param name="longRunTicks">
    /// If given, the map set is explored a second time with runs this long, and the two are joined: the original's sphere climbs some
    /// ramps and stairs only by pushing up them for longer, while longer runs come to rest less often and so reach fewer nooks.
    /// </param>
    public static WalkGraph Explore(PluginSet plugins, MapSetRecord set, Reach.Options options, int? longRunTicks = null)
    {
        options = options with { Edges = true };
        var bases = new List<Reach> { Reach.Explore(plugins, set, Reach.Starts(plugins, set), options) };
        if (longRunTicks is { } ticks && ticks > options.RunTicks)
            bases.Add(Reach.Explore(plugins, set, Reach.Starts(plugins, set), options with { RunTicks = ticks }));
        var opened = new List<(RecordId, Reach)>();
        var known = bases.SelectMany(b => b.Cells).ToHashSet();
        var feet = new Dictionary<Reach.Cell, Vector3>();
        foreach (var b in bases.AsEnumerable().Reverse())
            foreach (var c in b.Cells)
                feet[c] = b.Feet(c);
        var left = TogglesIn(plugins, set).ToList();
        // A toggle beyond another (the Barrows' doorways) is explored from the ground the first opened, so round again while any is
        // explored.
        for (var grew = true; grew && left.Count > 0;)
        {
            grew = false;
            foreach (var toggle in left.ToList())
            {
                var (min, max) = (toggle.Min - new Vector3(ToggleMargin), toggle.Max + new Vector3(ToggleMargin));
                bool Near(Vector3 p, float more) => p.X >= min.X - more && p.Z >= min.Z - more && p.Y >= min.Y - more
                    && p.X <= max.X + more && p.Z <= max.Z + more && p.Y <= max.Y + more;
                var starts = known.Select(c => feet[c]).Where(f => Near(f, 0))
                    .Select(f => (f + new Vector3(0, PlayerMotion.Radius, 0), (float?)f.Y)).ToList();
                if (starts.Count == 0)
                    continue;
                // He sets off again only near the toggle, or from ground none of the explorations so far reached.
                var around = Reach.Explore(plugins, set, starts, options, [toggle.Tripwire],
                    at => Near(at, 4) || !known.Contains(Reach.Cell.Of(at - new Vector3(0, PlayerMotion.Radius, 0))));
                opened.Add((toggle.Tripwire, around));
                foreach (var c in around.Cells)
                    if (known.Add(c))
                        feet[c] = around.Feet(c);
                left.Remove(toggle);
                grew = true;
            }
        }
        return From(bases, opened);
    }

    /// <summary>How far about a toggle's triangles the ground to explore it from reaches.</summary>
    private const float ToggleMargin = 6;

    /// <summary>A tripwire that changes its chunk's collision, and the box its toggles' triangles span, in the world's coordinates.</summary>
    public readonly record struct ToggleSite(RecordId Tripwire, Vector3 Min, Vector3 Max);

    /// <summary>
    /// The map set's tripwires that change their chunk's collision (<see cref="ChangeCollisionAction"/>, <see cref="Reach.Explore"/>'s
    /// <c>tripped</c>), with the toggles' triangles' box: those with no triangle to change are left out.
    /// </summary>
    public static IEnumerable<ToggleSite> TogglesIn(PluginSet plugins, MapSetRecord set)
    {
        foreach (var chunk in plugins.ChunksIn(set.Id))
        {
            var tripwires = chunk.Objects.OfType<TripwireObject>().Where(t => t.Does is ChangeCollisionAction).ToList();
            if (tripwires.Count == 0 || chunk.Collision is not { } asset)
                continue;
            var mesh = HBHT.Plugins.Assets.CollisionMesh.For(plugins, asset);
            var origin = set.Origin(chunk);
            foreach (var t in tripwires)
            {
                var toggle = ((ChangeCollisionAction)t.Does).Toggle;
                var corners = mesh.Where(c => c.Role == HBHT.Plugins.Assets.CollisionRole.Toggle && c.Target == toggle)
                    .SelectMany(c => new[] { c.A, c.B, c.C }).Select(p => p + origin).ToList();
                if (corners.Count > 0)
                    yield return new ToggleSite(chunk.ObjectId(t.Index), corners.Aggregate(Vector3.Min), corners.Aggregate(Vector3.Max));
            }
        }
    }

    /// <summary>
    /// How many steps of the ground a new game loads a gated step between two cells of it must save to be kept: one that only cuts a
    /// corner the open ground already goes round isn't the toggle's.
    /// </summary>
    private const int ShortCut = 8;

    /// <summary>
    /// Keeps a reach (made with <see cref="Reach.Options.Edges"/>) as a graph, with the explorations about its toggles, each with its
    /// tripwire taken as gone off: a step one of them made from ground the reach or another exploration has into ground it alone
    /// found, or between cells of the reach that are more than <see cref="ShortCut"/> of its steps apart, is a gated step of the
    /// toggle; a step from ground it alone found is open, since only a gated step leads there.
    /// </summary>
    public static WalkGraph From(Reach reach, IReadOnlyList<(RecordId Tripwire, Reach Opened)>? opened = null) => From([reach], opened);

    /// <summary>As <see cref="From(Reach, IReadOnlyList{ValueTuple{RecordId, Reach}}?)"/>, with the ground a new game loads joined from several explorations of it.</summary>
    public static WalkGraph From(IReadOnlyList<Reach> bases, IReadOnlyList<(RecordId Tripwire, Reach Opened)>? opened = null)
    {
        opened ??= [];
        var baseCells = bases.SelectMany(b => b.Cells).ToHashSet();
        var all = baseCells.ToHashSet();
        var feetOf = new Dictionary<Reach.Cell, Vector3>();
        foreach (var b in bases.Reverse())
            foreach (var c in b.Cells)
                feetOf[c] = b.Feet(c);
        foreach (var (_, around) in opened)
            foreach (var c in around.Cells)
                if (all.Add(c))
                    feetOf[c] = around.Feet(c);
        var cells = all.OrderBy(c => c.X).ThenBy(c => c.Z).ThenBy(c => c.Y).ToArray();
        var index = new Dictionary<Reach.Cell, int>(cells.Length);
        for (var i = 0; i < cells.Length; i++)
            index[cells[i]] = i;

        var open = new HashSet<(int From, int To)>();
        foreach (var (a, b) in bases.SelectMany(r => r.Edges))
            if (index.TryGetValue(a, out var from) && index.TryGetValue(b, out var to))
                open.Add((from, to));
        var baseSteps = open.ToLookup(e => e.From, e => e.To);
        bool Near(int from, int to)
        {
            var seen = new HashSet<int> { from };
            var ring = new List<int> { from };
            for (var d = 0; d < ShortCut && ring.Count > 0; d++)
            {
                var next = new List<int>();
                foreach (var c in ring)
                    foreach (var n in baseSteps[c])
                    {
                        if (n == to)
                            return true;
                        if (seen.Add(n))
                            next.Add(n);
                    }
                ring = next;
            }
            return false;
        }

        var gated = new SortedSet<(int From, int To, int Toggle)>();
        for (var t = 0; t < opened.Count; t++)
        {
            var around = opened[t].Opened;
            foreach (var (a, b) in around.Edges)
            {
                if (!index.TryGetValue(a, out var from) || !index.TryGetValue(b, out var to) || open.Contains((from, to)))
                    continue;
                var mine = !baseCells.Contains(a) && !opened.Take(t).Any(o => o.Opened.Cells.Contains(a));
                if (mine)
                    open.Add((from, to));
                else if (!baseCells.Contains(b) || !baseCells.Contains(a) || !Near(from, to))
                    gated.Add((from, to, t));
            }
        }

        var counts = new int[cells.Length + 1];
        foreach (var (from, _) in open)
            counts[from + 1]++;
        for (var i = 0; i < cells.Length; i++)
            counts[i + 1] += counts[i];
        var next = (int[])counts.Clone();
        var targets = new int[open.Count];
        foreach (var (from, to) in open.OrderBy(e => e.From).ThenBy(e => e.To))
            targets[next[from]++] = to;
        static byte Fraction(float v) => (byte)Math.Clamp((int)MathF.Round((v - MathF.Floor(v)) * 255), 0, 255);
        var feet = cells.Select(c => feetOf[c]).ToArray();
        return new WalkGraph(bases[0].Set.Id, [.. cells.Select(c => c.X)], [.. cells.Select(c => c.Z)], [.. cells.Select(c => c.Y)],
            [.. feet.Select(f => Fraction(f.X))], [.. feet.Select(f => Fraction(f.Y))], [.. feet.Select(f => Fraction(f.Z))], counts, targets,
            [.. opened.Select(o => o.Tripwire)], [.. gated]);
    }

    // The cache file: Brotli over a version, the fingerprint the graph was made under, the cells and the steps, each column
    // delta-coded so that neighbouring cells compress to almost nothing.

    /// <summary>Writes the graph to <paramref name="path"/> under <paramref name="fingerprint"/> (<see cref="WorldGraph.Fingerprint"/>).</summary>
    public void Save(string path, string fingerprint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Environment.ProcessId + ".tmp";
        using (var file = File.Create(temp))
        using (var brotli = new BrotliStream(file, CompressionLevel.Optimal))
        using (var w = new BinaryWriter(brotli))
        {
            w.Write(Version);
            w.Write(fingerprint);
            w.Write(MapSet.ToString());
            w.Write(Count);
            w.Write(_to.Length);
            WriteDeltas(w, _x);
            WriteDeltas(w, _z);
            WriteDeltas(w, _y);
            w.Write(_fx);
            w.Write(_fy);
            w.Write(_fz);
            var degree = new int[Count];
            for (var i = 0; i < Count; i++)
                degree[i] = _start[i + 1] - _start[i];
            foreach (var d in degree)
                w.Write7BitEncodedInt(d);
            for (var i = 0; i < Count; i++)
                foreach (var t in Steps(i))
                    w.Write(t - i);
            w.Write(Toggles.Count);
            foreach (var toggle in Toggles)
                w.Write(toggle.ToString());
            w.Write(GatedCount);
            foreach (var from in _gated.Keys.Order())
                foreach (var (to, toggle) in _gated[from])
                {
                    w.Write(from);
                    w.Write(to);
                    w.Write(toggle);
                }
        }
        File.Move(temp, path, overwrite: true);
    }

    private static void WriteDeltas(BinaryWriter w, int[] values)
    {
        var last = 0;
        foreach (var v in values)
        {
            w.Write(v - last);
            last = v;
        }
    }

    private static int[] ReadDeltas(BinaryReader r, int count)
    {
        var values = new int[count];
        var last = 0;
        for (var i = 0; i < count; i++)
            values[i] = last += r.ReadInt32();
        return values;
    }

    /// <summary>Reads a graph saved under the same fingerprint, or null if the file is missing, out of date or damaged.</summary>
    public static WalkGraph? TryLoad(string path, string fingerprint)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using var file = File.OpenRead(path);
            using var brotli = new BrotliStream(file, CompressionMode.Decompress);
            using var r = new BinaryReader(brotli);
            if (r.ReadInt32() != Version || r.ReadString() != fingerprint)
                return null;
            var set = RecordId.Parse(r.ReadString());
            var count = r.ReadInt32();
            var edges = r.ReadInt32();
            var (x, z, y) = (ReadDeltas(r, count), ReadDeltas(r, count), ReadDeltas(r, count));
            var (fx, fy, fz) = (r.ReadBytes(count), r.ReadBytes(count), r.ReadBytes(count));
            var start = new int[count + 1];
            for (var i = 0; i < count; i++)
                start[i + 1] = start[i] + r.Read7BitEncodedInt();
            var to = new int[edges];
            for (var i = 0; i < count; i++)
                for (var e = start[i]; e < start[i + 1]; e++)
                    to[e] = i + r.ReadInt32();
            var toggles = new RecordId[r.ReadInt32()];
            for (var i = 0; i < toggles.Length; i++)
                toggles[i] = RecordId.Parse(r.ReadString());
            var gated = new (int, int, int)[r.ReadInt32()];
            for (var i = 0; i < gated.Length; i++)
                gated[i] = (r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            return start[count] == edges ? new WalkGraph(set, x, z, y, fx, fy, fz, start, to, toggles, gated) : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or FormatException)
        {
            return null;
        }
    }
}
