using System.Diagnostics;
using System.Globalization;
using System.Text;
using HBHT.Core.Movement;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;

/// <summary>
/// <c>hbht movement compare</c>: every cut-scene played under the original's movement and under Modern, at the original's pace,
/// and where their outcomes differ, worst first (<see cref="MovementCompare"/>, docs/systems/movement.md#the-cut-scene-comparison).
/// </summary>
internal static class CompareReport
{
    public const string Usage = """
          hbht movement compare [<script>...] [--read <ticks>] [--all]
                                               every cut-scene (or those given, as aidyn:dialogue/0x… or 0x…) played
                                               under the original's movement and Modern's at the original's pace, and
                                               where they differ, worst first: an actor ending elsewhere, popping to
                                               its goal, off the ground or out of the world, and the walks' waits and
                                               the script taking longer; --read is how long each page shows before A
                                               (default 120), and --all lists those that come out the same too
        """;

    public static int Run(PluginSet plugins, string[] args)
    {
        var options = new MovementCompare.Options();
        var scripts = new HashSet<RecordId>();
        var all = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--read" when i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var read):
                    (options, i) = (options with { ReadTicks = read }, i + 1);
                    break;
                case "--all":
                    all = true;
                    break;
                case var name when name.StartsWith("aidyn:", StringComparison.Ordinal):
                    scripts.Add(RecordId.Parse(name));
                    break;
                case var name when name.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(name.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var index):
                    scripts.Add(RecordId.Hex(RecordTypes.Dialogue, index));
                    break;
                case var other:
                    Console.Error.WriteLine($"Not a script or option: {other}.");
                    return 1;
            }
        }
        var clock = Stopwatch.StartNew();
        var launches = MovementCompare.Launches(plugins).Where(l => scripts.Count == 0 || scripts.Contains(l.Script)).ToList();
        var differences = MovementCompare.Run(plugins, launches, options, out var skipped);
        Console.Write(Describe(differences, skipped, all));
        Console.WriteLine($"{clock.Elapsed.TotalSeconds:F0} s");
        return 0;
    }

    public static string Describe(IReadOnlyList<MovementCompare.Difference> differences, IReadOnlyList<string> skipped, bool all)
    {
        var s = new StringBuilder();
        var differ = differences.Count(d => d.Severity != MovementCompare.Severity.Same);
        s.AppendLine(CultureInfo.InvariantCulture,
            $"{differences.Count} cut-scenes played under both movements, {skipped.Count} not; {differ} differ by more than " +
            $"{MovementCompare.PlaceTolerance} in an actor's end or {MovementCompare.TimeTolerance} ticks in a wait.");
        foreach (var why in skipped)
            s.AppendLine(CultureInfo.InvariantCulture, $"  not played: {why}");
        foreach (var group in differences.GroupBy(d => d.Severity))
            s.AppendLine(CultureInfo.InvariantCulture, $"  {group.Key}: {group.Count()}");
        foreach (var d in differences)
        {
            if (!all && d.Severity == MovementCompare.Severity.Same)
                continue;
            var (o, m) = (d.Original, d.Modern);
            s.AppendLine(CultureInfo.InvariantCulture,
                $"{d.Launch.Script} from {d.Launch.From.MapObject} (points from {d.Launch.From.RefPoint}, stage {d.Launch.Stage.MapSet} {d.Launch.Stage.Row},{d.Launch.Stage.Column}): {d.Severity}, " +
                $"{Ticks(o)} ticks under the original, {Ticks(m)} under Modern ({d.TickDelta:+0;-0;0})");
            var waits = o.Waits.Keys.Union(m.Waits.Keys).Order()
                .Select(k => (Kind: k, Delta: m.Waits.GetValueOrDefault(k) - o.Waits.GetValueOrDefault(k)))
                .Where(w => w.Kind != ScriptWait.Move && Math.Abs(w.Delta) > MovementCompare.TimeTolerance).ToList();
            if (waits.Count > 0)
                s.AppendLine(CultureInfo.InvariantCulture, $"  other waits: {string.Join(", ", waits.Select(w => $"{w.Kind} {w.Delta:+0;-0;0}"))}");
            if (!d.SameCourse)
            {
                s.AppendLine(CultureInfo.InvariantCulture,
                    $"  original: {o.Actors.Count} actors, {o.Walks.Count} walks' waits{(o.Ended ? "" : $", waiting for {o.Waiting}")}; " +
                    $"Modern: {m.Actors.Count} actors, {m.Walks.Count} walks' waits{(m.Ended ? "" : $", waiting for {m.Waiting}")}");
            }
            foreach (var a in d.Actors)
            {
                if (!all && a.Severity == MovementCompare.Severity.Same)
                    continue;
                s.AppendLine(CultureInfo.InvariantCulture,
                    $"  actor {a.Index} {a.Original.Entity}: ends {a.Distance:F2} apart, original {End(a.Original, o.Origin)}, Modern {End(a.Modern, o.Origin)}");
            }
            foreach (var (index, wo, wm, delta) in d.Walks)
            {
                if (!all && Math.Abs(delta) <= MovementCompare.TimeTolerance)
                    continue;
                var moves = string.Join(", ", wm.Moves.Select(mv => $"{mv.Actor} {(mv.Run ? "runs" : "walks")} to {mv.RefPoint}"));
                s.AppendLine(CultureInfo.InvariantCulture,
                    $"  walk {index} ({moves}): {wo.Ticks} ticks under the original, {wm.Ticks} under Modern ({delta:+0;-0;0})");
            }
        }
        return s.ToString();

        static string Ticks(MovementCompare.Outcome outcome) => outcome.Ended ? $"{outcome.Ticks}" : $"no end in {outcome.Ticks}";

        static string End(MovementCompare.ActorEnd end, System.Numerics.Vector3 origin)
        {
            var notes = new List<string>();
            if (end.PutAtGoal > 0)
                notes.Add($"put at its goal {end.PutAtGoal}× (popped {end.Pop:F2})");
            if (end.Jump > 0)
                notes.Add($"jumped {end.Jump:F2} in a tick");
            if (end.FellOut)
                notes.Add("fell out");
            else if (end.OffGround)
                notes.Add("off the ground");
            var p = end.Position - origin;
            return string.Create(CultureInfo.InvariantCulture, $"({p.X:F2}, {p.Y:F2}, {p.Z:F2})") + (notes.Count > 0 ? $" {string.Join(", ", notes)}" : "");
        }
    }
}
