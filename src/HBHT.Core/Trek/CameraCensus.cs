using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text;
using HBHT.Core.Cameras;
using HBHT.Core.MapObjects;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Trek;

/// <summary>One teleporter's arrival under both cameras, from <see cref="CameraCensus"/>, or why it wasn't traced.</summary>
public sealed record CameraArrival(string Teleporter, string Into, bool Interior, CameraTraceResult? Original, CameraTraceResult? Enhanced,
    string? Skipped);

/// <summary>
/// Every teleporter in the plugins arrived through, holding forward along the way he arrives facing for 3 seconds, under the
/// original camera and the enhanced one (<see cref="CameraTrace"/>): whether the camera is ever past a wall, whether he's
/// seen from the front after the first second, and how much of the time the view ahead is blocked close by. Arrivals where
/// a camera object takes the camera are skipped and counted, as are those that can't be made.
/// </summary>
public static class CameraCensus
{
    public const int Ticks = 180;

    public static IReadOnlyList<CameraArrival> Run(PluginSet plugins)
    {
        var doors = new List<(ChunkRecord From, TeleporterObject Door, TeleportArrival Arrival)>();
        foreach (var chunk in plugins.All<ChunkRecord>(RecordTypes.Chunk))
            foreach (var t in chunk.Objects.OfType<TeleporterObject>())
                if (TeleportArrival.For(plugins, t) is { } arrival)
                    doors.Add((chunk, t, arrival));
        var results = new ConcurrentBag<CameraArrival>();
        Parallel.ForEach(doors, d =>
        {
            var name = $"{d.From.Id}/{d.Door.Index}";
            var interior = plugins.TryGet<TerrainRecord>(d.Arrival.Chunk.Terrain) is { SkyDrawn: false };
            var set = int.Parse(d.From.MapSet.ToString().Split('/')[^1], CultureInfo.InvariantCulture);
            var walk = new CameraWalk(name, set, d.From.Name, d.Door.Position + new Vector3(0, 0.5f, 0), Vector2.UnitX,
                d.Arrival.Chunk.Id.ToString(), d.Arrival.Chunk.Name, [], Ticks, d.Door.Index);
            try
            {
                var original = CameraTrace.Run(plugins, walk, CameraStyle.Original);
                if (original.Held > 0)
                {
                    results.Add(new(name, d.Arrival.Chunk.Id.ToString(), interior, null, null, "a camera object"));
                    return;
                }
                var enhanced = CameraTrace.Run(plugins, walk, CameraStyle.Enhanced);
                results.Add(new(name, d.Arrival.Chunk.Id.ToString(), interior, original, enhanced, null));
            }
            catch (Exception e)
            {
                results.Add(new(name, d.Arrival.Chunk.Id.ToString(), interior, null, null, e.Message));
            }
        });
        return [.. results.OrderBy(r => r.Teleporter, StringComparer.Ordinal)];
    }

    /// <summary>The census's shares for each style, interiors and outdoors apart, and the ten worst arrivals under Enhanced.</summary>
    public static string Report(IReadOnlyList<CameraArrival> arrivals)
    {
        var text = new StringBuilder();
        var traced = arrivals.Where(a => a.Skipped is null).ToList();
        text.AppendLine(CultureInfo.InvariantCulture, $"{arrivals.Count} arrivals: {traced.Count} traced, "
            + $"{arrivals.Count(a => a.Skipped == "a camera object")} taken by a camera object, "
            + $"{arrivals.Count(a => a.Skipped is not null and not "a camera object")} not made");
        text.AppendLine($"{"",-9} {"style",-9} {"n",5} {"ever past",10} {"front>1s",9} {"blocked",8}");
        foreach (var (label, interior) in new[] { ("interior", true), ("outdoor", false) })
        {
            var group = traced.Where(a => a.Interior == interior).ToList();
            foreach (var style in new[] { CameraStyle.Original, CameraStyle.Enhanced })
            {
                var r = group.Select(a => style == CameraStyle.Original ? a.Original! : a.Enhanced!).ToList();
                if (r.Count == 0)
                    continue;
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{label,-9} {style,-9} {r.Count,5} {(float)r.Count(x => x.PastWall > 0) / r.Count,10:P1} {(float)r.Count(x => x.Front > 0) / r.Count,9:P1} {r.Average(x => x.Blocked),8:P1}"));
            }
        }
        text.AppendLine("worst under Enhanced:");
        foreach (var a in traced.OrderByDescending(a => Badness(a.Enhanced!)).Take(10))
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {a.Teleporter} -> {a.Into}: past {a.Enhanced!.PastWall:P0} front {a.Enhanced.Front:P0} blocked {a.Enhanced.Blocked:P0} (original {a.Original!.PastWall:P0} / {a.Original.Front:P0} / {a.Original.Blocked:P0})"));
        return text.ToString();
    }

    /// <summary>How bad an arrival is: past a wall worst, then his front, then the view blocked.</summary>
    public static float Badness(CameraTraceResult r) => r.PastWall * 4 + r.Front * 2 + r.Blocked;
}
