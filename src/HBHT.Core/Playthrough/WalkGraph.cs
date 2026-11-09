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
    internal const int Version = 2;

    private readonly int[] _x, _z, _y;
    private readonly byte[] _fx, _fz, _fy;   // where in the cell the feet were on average, in 1/255 of it
    private readonly int[] _start;   // CSR: _start[i].._start[i + 1] index _to
    private readonly int[] _to;
    private Dictionary<long, int>? _index;
    private Dictionary<long, int[]>? _columns;

    private WalkGraph(RecordId mapSet, int[] x, int[] z, int[] y, byte[] fx, byte[] fy, byte[] fz, int[] start, int[] to)
    {
        MapSet = mapSet;
        (_x, _z, _y, _fx, _fy, _fz, _start, _to) = (x, z, y, fx, fy, fz, start, to);
    }

    public RecordId MapSet { get; }

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

    /// <summary>What the pilot follows from one cell to another: how far apart their middles are.</summary>
    public float Length(int from, int to) => Vector3.Distance(Center(from), Center(to));

    // Building.

    /// <summary>
    /// Explores a map set from where play puts the player in it (<see cref="Reach.Starts"/>) and keeps what he reached. Takes minutes
    /// for the larger sets.
    /// </summary>
    public static WalkGraph Explore(PluginSet plugins, MapSetRecord set, Reach.Options options)
    {
        var reach = Reach.Explore(plugins, set, Reach.Starts(plugins, set), options with { Edges = true });
        return From(reach);
    }

    /// <summary>Keeps a reach (made with <see cref="Reach.Options.Edges"/>) as a graph.</summary>
    public static WalkGraph From(Reach reach)
    {
        var cells = reach.Cells.OrderBy(c => c.X).ThenBy(c => c.Z).ThenBy(c => c.Y).ToArray();
        var index = new Dictionary<Reach.Cell, int>(cells.Length);
        for (var i = 0; i < cells.Length; i++)
            index[cells[i]] = i;
        var counts = new int[cells.Length + 1];
        var edges = new List<(int From, int To)>(reach.Edges.Count);
        foreach (var (a, b) in reach.Edges)
        {
            if (index.TryGetValue(a, out var from) && index.TryGetValue(b, out var to))
            {
                edges.Add((from, to));
                counts[from + 1]++;
            }
        }
        for (var i = 0; i < cells.Length; i++)
            counts[i + 1] += counts[i];
        var next = (int[])counts.Clone();
        var targets = new int[edges.Count];
        foreach (var (from, to) in edges)
            targets[next[from]++] = to;
        static byte Fraction(float v) => (byte)Math.Clamp((int)MathF.Round((v - MathF.Floor(v)) * 255), 0, 255);
        var feet = cells.Select(reach.Feet).ToArray();
        return new WalkGraph(reach.Set.Id, [.. cells.Select(c => c.X)], [.. cells.Select(c => c.Z)], [.. cells.Select(c => c.Y)],
            [.. feet.Select(f => Fraction(f.X))], [.. feet.Select(f => Fraction(f.Y))], [.. feet.Select(f => Fraction(f.Z))], counts, targets);
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
            return start[count] == edges ? new WalkGraph(set, x, z, y, fx, fy, fz, start, to) : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or FormatException)
        {
            return null;
        }
    }
}
