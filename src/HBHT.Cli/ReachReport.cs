using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using HBHT.Core;
using HBHT.Core.Movement;
using HBHT.Plugins;
using HBHT.Plugins.Records;

/// <summary>
/// <c>hbht movement reach</c>: where Modern movement takes the player that the original's doesn't, and the other way, per map set
/// and pace (<see cref="Reach"/>, docs/systems/movement.md#the-reachability-check).
/// </summary>
internal static class ReachReport
{
    public const string Usage = """
          hbht movement reach [<mapset>...] [--pace original|brisk] [--chunk <name>] [--against original] [--plain-floor] [--all]
                                               where Modern movement reaches on foot that the original's doesn't, and the
                                               other way, in each map set (number or label; default all, the Battle arenas
                                               from where combat puts bodies) at each pace; --chunk keeps to one chunk, --against original checks the
                                               original against itself, --plain-floor walks Modern up plain collision as ground,
                                               and --all lists every cluster rather than the first 25
        """;

    public static int Run(PluginSet plugins, string[] args)
    {
        try
        {
            return Run(plugins, args, Console.Out);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static int Run(PluginSet plugins, string[] args, TextWriter output)
    {
        var sets = new List<MapSetRecord>();
        float[] paces = [1, SessionSettings.BriskPace];
        string? chunkName = null;
        var self = false;
        var plainFloor = false;
        var all = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pace" when i + 1 < args.Length:
                    paces = args[++i] switch
                    {
                        "original" => [1],
                        "brisk" => [SessionSettings.BriskPace],
                        var other => throw new ArgumentException($"--pace is original or brisk, not {other}."),
                    };
                    break;
                case "--chunk" when i + 1 < args.Length:
                    chunkName = args[++i];
                    break;
                case "--against" when i + 1 < args.Length && args[i + 1] == "original":
                    (self, i) = (true, i + 1);
                    break;
                case "--plain-floor":
                    plainFloor = true;
                    break;
                case "--all":
                    all = true;
                    break;
                case var name:
                    sets.Add(Find(plugins, name) ?? throw new ArgumentException($"No map set {name}."));
                    break;
            }
        }
        if (sets.Count == 0)
            sets = [.. plugins.All<MapSetRecord>(RecordTypes.MapSet).OrderBy(s => int.Parse(s.Id.Key, CultureInfo.InvariantCulture))];

        var modern = new Reach.Options(self ? MovementMode.Original : MovementMode.Modern,
            Rules: plainFloor ? ModernRules.Default with { PlainIsWall = false } : null);
        var clock = Stopwatch.StartNew();
        var jobs = sets.SelectMany(set => paces.Select(pace => (set, pace))).ToList();
        var reports = new string[jobs.Count];
        Parallel.For(0, jobs.Count, j =>
        {
            var (set, pace) = jobs[j];
            var within = Within(set, chunkName);
            if (chunkName is not null && within is null)
            {
                reports[j] = "";
                return;
            }
            var starts = Reach.Starts(plugins, set);
            var original = Reach.Explore(plugins, set, starts, new Reach.Options(MovementMode.Original, pace, Within: within));
            var other = Reach.Explore(plugins, set, starts, modern with { Pace = pace, Within = within });
            reports[j] = Describe(set, pace, original, other, all);
        });
        foreach (var report in reports)
            output.Write(report);
        output.WriteLine($"{clock.Elapsed.TotalSeconds:F0} s");
        return 0;
    }

    private static MapSetRecord? Find(PluginSet plugins, string name) =>
        plugins.All<MapSetRecord>(RecordTypes.MapSet).FirstOrDefault(s => s.Id.Key == name || string.Equals(s.Label, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A chunk's box, from far below to far above, or null where the map set hasn't the chunk.</summary>
    private static (Vector3, Vector3)? Within(MapSetRecord set, string? chunkName)
    {
        if (chunkName is null)
            return null;
        if (set.Chunks.FirstOrDefault(c => c.Name == chunkName) is not { } chunk)
            return null;
        var origin = set.Origin(chunk.Row, chunk.Column);
        return (origin with { Y = -1000 }, origin + new Vector3(set.CellSize, 1000, set.CellSize));
    }

    private static string Describe(MapSetRecord set, float pace, Reach original, Reach other, bool all)
    {
        var s = new System.Text.StringBuilder();
        var gained = other.Beyond(original);
        var lost = original.Beyond(other);
        s.AppendLine(CultureInfo.InvariantCulture,
            $"{set.Label} at pace {pace}: the original reaches {original.Cells.Count} cells in {original.Runs} runs, the other {other.Cells.Count} in {other.Runs}; " +
            $"{gained.Count} clusters only the other reaches, {lost.Count} only the original");
        List("  Only the other:", gained, other, original);
        List("  Only the original:", lost, original, other);
        return s.ToString();

        void List(string title, IReadOnlyList<Reach.Cluster> clusters, Reach reach, Reach against)
        {
            if (clusters.Count == 0)
                return;
            s.AppendLine(title);
            foreach (var c in all ? clusters : clusters.Take(25))
            {
                var top = new Vector3(c.Top.X + 0.5f, c.Top.Y, c.Top.Z + 0.5f);
                var row = (int)MathF.Floor(top.X / set.CellSize);
                var column = (int)MathF.Floor(top.Z / set.CellSize);
                var chunk = set.Chunk(row, column);
                var local = top - set.Origin(row, column);
                var rise = c.Rise is { } r ? $"{r:+0;-0;0} above the other's reach" : "nothing reached near";
                s.AppendLine(CultureInfo.InvariantCulture,
                    $"    {chunk?.Name ?? $"{row},{column}"} ({local.X:F1}, {local.Y:F1}, {local.Z:F1}): {c.Cells.Count} cells, {rise}");
                if (!all)
                    continue;
                // The run that first took him where the other doesn't reach, on the way there, and the one after.
                var path = reach.Path(c.Top).Reverse().ToList();
                var escape = path.FindIndex(run => !against.Covers(Reach.Cell.Of(run.From - new Vector3(0, PlayerMotion.Radius, 0))));
                var shown = escape < 0 ? path.TakeLast(1) : path.Skip(Math.Max(0, escape - 5)).Take(6);
                foreach (var (from, way) in shown)
                {
                    var (fromRow, fromColumn) = ((int)MathF.Floor(from.X / set.CellSize), (int)MathF.Floor(from.Z / set.CellSize));
                    var start = from - set.Origin(fromRow, fromColumn);
                    s.AppendLine(CultureInfo.InvariantCulture,
                        $"      from {set.Chunk(fromRow, fromColumn)?.Name} ({start.X:F2}, {start.Y:F2}, {start.Z:F2}) toward ({way.X:F2}, {way.Y:F2})");
                }
            }
            if (!all && clusters.Count > 25)
                s.AppendLine(CultureInfo.InvariantCulture, $"    and {clusters.Count - 25} more");
        }
    }
}
