using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text;
using HBHT.Core.Cameras;
using HBHT.Core.MapObjects;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Trek;

/// <summary>
/// One run of an arrival (<see cref="CameraCensus"/>): the trace, and what the census asks of the arrival. <paramref name="SeenAtFirst"/>:
/// his chest in the frame at the first tick; <paramref name="Frame"/>: how the enhanced camera framed it; <paramref name="WentIn"/>: he
/// ended at least half a unit further along the way he arrived facing and didn't go back through a door; <paramref name="BackOut"/>:
/// he went back through a door or ended behind where he started; <paramref name="RoundBy"/>: at 1.5 s the look within 45° of the way
/// he faces with the way ahead in frame (or no way ahead to see); <paramref name="OutDuringSwing"/>: the share of the first 1.5 s
/// his chest is out of the frame or hidden; <paramref name="Lifted"/>: the share of ticks the pivot is lifted more than half a unit
/// over his head; <paramref name="PeakTurn"/>: the view's fastest turn over the first 2 s, °/s; <paramref name="Wander"/>: how far
/// he strays to the side of his arrival line over the first 2 s.
/// </summary>
public sealed record ArrivalRun(CameraTraceResult Trace, bool SeenAtFirst, TrekArrival? Frame, bool WentIn, bool BackOut, bool RoundBy,
    float OutDuringSwing, float Lifted, float PeakTurn, float Wander, bool RoundBy3 = false, float TotalTurn = 0, CameraFeel? Feel = null);

/// <summary>One teleporter's arrival under both cameras and each way of pushing the stick, or why it wasn't traced.</summary>
public sealed record CameraArrival(string Teleporter, string Into, bool Interior, IReadOnlyDictionary<(CameraStyle, CameraStick), ArrivalRun>? Runs,
    string? Skipped)
{
    public ArrivalRun Of(CameraStyle style, CameraStick stick = CameraStick.Steer) => Runs![(style, stick)];
}

/// <summary>
/// Every teleporter in the plugins arrived through, under the original camera and the enhanced one (<see cref="CameraTrace"/>),
/// for 3 seconds with the stick pushed each of three ways (<see cref="CameraStick"/>): a player steering into the room along the
/// way he arrived facing, the same after half a second at rest, and the stick held up (an informational row). Arrivals where a
/// camera object takes the camera are skipped and counted, as are those that can't be made.
/// </summary>
public static class CameraCensus
{
    public const int Ticks = 180;

    /// <summary>The tick by which the camera should be round behind him.</summary>
    public const int RoundTick = 90;

    public static readonly CameraStick[] Sticks = [CameraStick.Steer, CameraStick.RestThenSteer, CameraStick.Up, CameraStick.Still];

    public static IReadOnlyList<CameraArrival> Run(PluginSet plugins, TrekCameraTuning? tuning = null)
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
                var runs = new Dictionary<(CameraStyle, CameraStick), ArrivalRun>();
                foreach (var style in new[] { CameraStyle.Original, CameraStyle.Enhanced })
                    foreach (var stick in Sticks)
                    {
                        var run = Trace(plugins, walk, style, stick, tuning);
                        if (run.Trace.Held > 0)
                        {
                            results.Add(new(name, d.Arrival.Chunk.Id.ToString(), interior, null, "a camera object"));
                            return;
                        }
                        runs[(style, stick)] = run;
                    }
                results.Add(new(name, d.Arrival.Chunk.Id.ToString(), interior, runs, null));
            }
            catch (Exception e)
            {
                results.Add(new(name, d.Arrival.Chunk.Id.ToString(), interior, null, e.Message));
            }
        });
        return [.. results.OrderBy(r => r.Teleporter, StringComparer.Ordinal)];
    }

    /// <summary>One arrival's run, measured as <see cref="ArrivalRun"/> says.</summary>
    public static ArrivalRun Trace(PluginSet plugins, CameraWalk walk, CameraStyle style, CameraStick stick, TrekCameraTuning? tuning = null)
    {
        var seenAtFirst = false;
        TrekArrival? frame = null;
        Vector3 start = default, last = default;
        Vector2 heading = default;
        bool jumped = false, round = false, round3 = false;
        int out_ = 0, lifted = 0, ticks = 0;
        float peak = 0, wander = 0, total = 0;
        Vector2? lastLook = null;
        var feel = new CameraFeel();
        var trace = CameraTrace.Run(plugins, walk, style, (trek, tick) =>
        {
            feel.Tick(trek);
            var at = trek.Player.Sphere.Position;
            var chest = at + new Vector3(0, 0.5f, 0);
            var seen = CameraTrace.InFrame(trek, chest);
            if (tick == 0)
            {
                start = last = at;
                heading = trek.Player.Heading;
                seenAtFirst = seen;
                if (style == CameraStyle.Enhanced)
                    frame = trek.EnhancedCamera.Arrival;
            }
            if (Vector3.Distance(at, last) > 2)
                jumped = true;
            last = at;
            ticks++;
            if (tick < RoundTick && !seen)
                out_++;
            if (style == CameraStyle.Enhanced && trek.EnhancedCamera.Pivot.Y > at.Y + trek.EnhancedCamera.Tuning.Height + 0.5f)
                lifted++;
            var look = trek.Camera.LookXZ;
            if (lastLook is { } was)
                total += Between(was, look);
            if (tick < 120)
            {
                if (lastLook is { } before)
                    peak = MathF.Max(peak, Between(before, look) * 60);
                if (!jumped)
                {
                    var off = new Vector2(at.X - start.X, at.Z - start.Z);
                    wander = MathF.Max(wander, MathF.Abs(off.X * heading.Y - off.Y * heading.X));
                }
            }
            lastLook = look;
            if (tick == RoundTick || tick == Ticks - 1)
            {
                var faces = trek.Player.Heading;
                var isRound = Vector2.Dot(Vector2.Normalize(look), Vector2.Normalize(faces)) > MathF.Cos(MathF.PI / 4)
                    && CameraTrace.AheadInFrame(trek) != false;
                if (tick == RoundTick)
                    round = isRound;
                else
                    round3 = isRound;
            }
        }, tuning, stick);
        var along = Vector2.Dot(new Vector2(last.X - start.X, last.Z - start.Z), heading);
        return new ArrivalRun(trace, seenAtFirst, frame, !jumped && along > 0.5f, jumped || along < -0.25f, round,
            Math.Min(ticks, RoundTick) == 0 ? 0 : (float)out_ / Math.Min(ticks, RoundTick), ticks == 0 ? 0 : (float)lifted / ticks, peak, wander, round3, total, feel);
    }

    /// <summary>The census's shares for each style and stick, interiors and outdoors apart, the enhanced frames, and the worst arrivals.</summary>
    public static string Report(IReadOnlyList<CameraArrival> arrivals)
    {
        var text = new StringBuilder();
        var traced = arrivals.Where(a => a.Skipped is null).ToList();
        text.AppendLine(CultureInfo.InvariantCulture, $"{arrivals.Count} arrivals: {traced.Count} traced, "
            + $"{arrivals.Count(a => a.Skipped == "a camera object")} taken by a camera object, "
            + $"{arrivals.Count(a => a.Skipped is not null and not "a camera object")} not made");
        text.AppendLine($"{"",-9} {"style",-9} {"stick",-14} {"n",4} {"seen@0",7} {"past",6} {"hidden",7} {"went in",8} {"back out",8} {"round@1.5",9} {"@3",6} {"out<1.5",8} {"lifted",7} {"turn pk",8} {"turn pk95",9} {"wander95",8} {"ahead",6}");
        foreach (var (label, interior) in new[] { ("interior", true), ("outdoor", false) })
        {
            var group = traced.Where(a => a.Interior == interior).ToList();
            if (group.Count == 0)
                continue;
            foreach (var style in new[] { CameraStyle.Original, CameraStyle.Enhanced })
                foreach (var stick in Sticks)
                {
                    var r = group.Select(a => a.Of(style, stick)).ToList();
                    float Of(Func<ArrivalRun, bool> f) => (float)r.Count(f) / r.Count;
                    text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"{label,-9} {style,-9} {stick,-14} {r.Count,4} {Of(x => x.SeenAtFirst),7:P1} {Of(x => x.Trace.PastWall > 0),6:P1} {Of(x => x.Trace.Hidden > 0),7:P1} {Of(x => x.WentIn),8:P1} {Of(x => x.BackOut),8:P1} {Of(x => x.RoundBy),9:P1} {Of(x => x.RoundBy3),6:P1} {r.Average(x => x.OutDuringSwing),8:P1} {r.Average(x => x.Lifted),7:P1} {r.Max(x => x.PeakTurn),8:F0} {Percentile(r.Select(x => x.PeakTurn), 0.95f),9:F0} {Percentile(r.Select(x => x.Wander), 0.95f),8:F2} {Mean(r.Select(x => x.Trace.AheadInFrame)),6:P1}"));
                }
            var frames = group.Select(a => a.Of(CameraStyle.Enhanced).Frame).OfType<TrekArrival>().ToList();
            var kinds = string.Join(", ", Enum.GetValues<TrekArrivalKind>().Select(k =>
                string.Create(CultureInfo.InvariantCulture, $"{k} {(float)frames.Count(f => f.Kind == k) / Math.Max(frames.Count, 1):P1}")));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{label} enhanced frames: reached the length {(float)frames.Count(f => f.Reached) / Math.Max(frames.Count, 1):P1}; {kinds}"));
            float F(Func<TrekArrival, bool> f) => (float)frames.Count(f) / Math.Max(frames.Count, 1);
            var t = TrekCameraTuning.Default;
            bool Rear(TrekArrival f) => f.Kind != TrekArrivalKind.Authored && f.Kind != TrekArrivalKind.Lifted && MathF.Abs(f.Off) <= t.RearBand;
            bool Side(TrekArrival f) => f.Kind != TrekArrivalKind.Authored && f.Kind != TrekArrivalKind.Lifted && MathF.Abs(f.Off) > t.RearBand && MathF.Abs(f.Off) <= t.SideBand;
            bool Front(TrekArrival f) => f.Kind != TrekArrivalKind.Authored && f.Kind != TrekArrivalKind.Lifted && MathF.Abs(f.Off) > t.SideBand;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{label} bands: rear full {F(f => Rear(f) && !f.Close):P1} close {F(f => Rear(f) && f.Close):P1}; side full {F(f => Side(f) && !f.Close):P1} close {F(f => Side(f) && f.Close):P1}; front full {F(f => Front(f) && !f.Close):P1} close {F(f => Front(f) && f.Close):P1}; authored {F(f => f.Kind == TrekArrivalKind.Authored):P1}; lifted {F(f => f.Kind == TrekArrivalKind.Lifted):P1}"));
            var offs = frames.Where(f => f.Kind != TrekArrivalKind.Authored).Select(f => MathF.Abs(f.Off)).ToList();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{label} swing to behind him: mean {offs.DefaultIfEmpty().Average():F0}°, median {Percentile(offs, 0.5f):F0}°, 75% {Percentile(offs, 0.75f):F0}°, 90% {Percentile(offs, 0.9f):F0}°; none (under 30°) {(float)offs.Count(o => o < 30) / Math.Max(offs.Count, 1):P1}, over 110° {(float)offs.Count(o => o > 110) / Math.Max(offs.Count, 1):P1}"));
        }
        foreach (var style in new[] { CameraStyle.Original, CameraStyle.Enhanced })
            foreach (var stick in Sticks)
            {
                var feels = traced.Select(a => a.Of(style, stick).Feel!).ToList();
                var ticks = feels.Sum(f => f.Ticks);
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"feel {style,-9} {stick,-14}: cuts {feels.Sum(f => f.Cuts) * 3600f / Math.Max(ticks, 1),5:F1}/min, excursions {feels.Sum(f => f.Excursions) * 3600f / Math.Max(ticks, 1),5:F1}/min, urgent {(float)feels.Sum(f => f.Urgent) / Math.Max(ticks, 1):P1}, past drawn {feels.Sum(f => f.PastDrawn)}, off floor {feels.Sum(f => f.OffFloor)}, lens in {feels.Sum(f => f.LensInside)}, accel peak {feels.Max(f => f.AccelPeak),6:F0} 95% {Percentile(feels.Select(f => f.Accel95), 0.95f),5:F0}, standing bad {(float)feels.Sum(f => f.StandingBad) / Math.Max(feels.Sum(f => f.Standing), 1):P1}, out of frame {(float)feels.Sum(f => f.StandingOut) / Math.Max(feels.Sum(f => f.Standing), 1):P1} (by the drawn world {(float)feels.Sum(f => f.StandingOutDrawn) / Math.Max(feels.Sum(f => f.Standing), 1):P1}) of {feels.Sum(f => f.Standing)}"));
            }
        text.AppendLine("ever hidden under Enhanced, steering:");
        foreach (var a in traced.Where(a => a.Of(CameraStyle.Enhanced).Trace.Hidden > 0).Take(40))
        {
            var e = a.Of(CameraStyle.Enhanced);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {a.Teleporter} -> {a.Into}: hidden {e.Trace.Hidden:P0} frame {e.Frame?.Kind} {e.Frame?.Off:F0}° arm {e.Frame?.Arm:F1} lifted {e.Lifted:P0} (original hidden {a.Of(CameraStyle.Original).Trace.Hidden:P0})"));
        }
        text.AppendLine("not seen at the first tick under Enhanced:");
        foreach (var a in traced.Where(a => !a.Of(CameraStyle.Enhanced).SeenAtFirst))
        {
            var e = a.Of(CameraStyle.Enhanced);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {a.Teleporter} -> {a.Into}: frame {e.Frame?.Kind} {e.Frame?.Off:F0}° arm {e.Frame?.Arm:F1} (original seen {a.Of(CameraStyle.Original).SeenAtFirst})"));
        }
        text.AppendLine("went back out under Enhanced, steering:");
        foreach (var a in traced.Where(a => a.Of(CameraStyle.Enhanced).BackOut || a.Of(CameraStyle.Enhanced, CameraStick.RestThenSteer).BackOut))
            text.AppendLine($"  {a.Teleporter} -> {a.Into}: frame {a.Of(CameraStyle.Enhanced).Frame?.Kind}");
        return text.ToString();
    }

    /// <summary>The angle between two directions in degrees, by their cross and dot (an arccosine near 1 is all rounding).</summary>
    public static float Between(Vector2 a, Vector2 b) => MathF.Abs(MathF.Atan2(a.X * b.Y - a.Y * b.X, Vector2.Dot(a, b))) * 180 / MathF.PI;

    /// <summary>The mean of the values that are numbers.</summary>
    public static float Mean(IEnumerable<float> values) => values.Where(v => !float.IsNaN(v)).DefaultIfEmpty(float.NaN).Average();

    /// <summary>The share true of the answers there are.</summary>
    public static float Share(IEnumerable<bool?> answers)
    {
        var given = answers.Where(a => a is not null).ToList();
        return given.Count == 0 ? float.NaN : (float)given.Count(a => a == true) / given.Count;
    }

    public static float Percentile(IEnumerable<float> values, float share)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(share * sorted.Count))];
    }
}
