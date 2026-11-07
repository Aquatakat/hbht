using System.Diagnostics;
using System.Globalization;
using Godot;

namespace HBHT.Game;

/// <summary>
/// <c>--bench &lt;seconds&gt;</c>: measures the run for that many seconds after two to settle, prints what each frame and tick
/// cost, and quits. Hold <c>fast_forward</c> with it (<c>--hold fast_forward,30</c>) to load the ticks as fast-forward does.
/// </summary>
public partial class Game
{
    private const double BenchSettle = 2;

    private Bench? _bench;

    /// <summary>Starts the bench, if the command line asks for one.</summary>
    private void StartBench(LaunchOptions options)
    {
        if (options.Bench is not { } seconds)
            return;
        GetTree().CreateTimer(BenchSettle).Timeout += () => _bench = new Bench();
        GetTree().CreateTimer(BenchSettle + seconds).Timeout += () =>
        {
            GD.Print(_bench?.Report());
            _bench = null;
            GetTree().Quit();
        };
    }

    /// <summary>Times one tick, while the bench runs.</summary>
    private void BenchTick(Action tick)
    {
        if (_bench is not { } bench)
        {
            tick();
            return;
        }
        var start = Stopwatch.GetTimestamp();
        tick();
        bench.Tick(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>Counts a frame, while the bench runs.</summary>
    private void BenchFrame() => _bench?.Frame();

    private sealed class Bench
    {
        private readonly List<double> _frames = [];
        private readonly List<double> _ticks = [];
        private readonly List<double> _drawCalls = [];
        private readonly List<double> _objects = [];
        private readonly List<double> _primitives = [];
        private long _last = Stopwatch.GetTimestamp();
        private readonly long _start = Stopwatch.GetTimestamp();
        private readonly int _gc0 = GC.CollectionCount(0), _gc1 = GC.CollectionCount(1), _gc2 = GC.CollectionCount(2);
        private readonly long _allocated = GC.GetTotalAllocatedBytes();

        public void Tick(double ms) => _ticks.Add(ms);

        public void Frame()
        {
            var now = Stopwatch.GetTimestamp();
            _frames.Add(Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds);
            _last = now;
            _drawCalls.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            _objects.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame));
            _primitives.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
        }

        public string Report()
        {
            var seconds = Stopwatch.GetElapsedTime(_start).TotalSeconds;
            var frames = _frames.Skip(1).ToList();
            static string Spread(List<double> values)
            {
                if (values.Count == 0)
                    return "none";
                var sorted = values.Order().ToList();
                return string.Create(CultureInfo.InvariantCulture,
                    $"mean {values.Average():F3}, median {sorted[sorted.Count / 2]:F3}, p95 {sorted[sorted.Count * 95 / 100]:F3}, max {sorted[^1]:F3}");
            }
            return string.Create(CultureInfo.InvariantCulture, $"""
                bench: {seconds:F1} s, {frames.Count / seconds:F1} fps, {_ticks.Count / seconds:F1} ticks/s
                bench: frame ms {Spread(frames)}
                bench: tick ms {Spread(_ticks)}
                bench: draw calls {_drawCalls.DefaultIfEmpty().Average():F0}, objects {_objects.DefaultIfEmpty().Average():F0}, primitives {_primitives.DefaultIfEmpty().Average():F0}
                bench: gc gen0 {GC.CollectionCount(0) - _gc0}, gen1 {GC.CollectionCount(1) - _gc1}, gen2 {GC.CollectionCount(2) - _gc2}, allocated {(GC.GetTotalAllocatedBytes() - _allocated) / 1e6:F1} MB
                """);
        }
    }
}
