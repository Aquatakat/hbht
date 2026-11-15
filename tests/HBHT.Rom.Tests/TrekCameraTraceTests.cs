using HBHT.Core.Cameras;
using HBHT.Core.Trek;

namespace HBHT.Rom.Tests;

/// <summary>
/// Trek's camera traced headless over the fixed walks on NA 1.0's data (<see cref="CameraTrace"/>,
/// docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced). The table is written to the test's output, as
/// <c>hbht camera trace</c> prints it.
/// </summary>
public sealed class TrekCameraTraceTests(Na10Import fixture, ITestOutputHelper output)
{
    /// <summary>
    /// The original's camera over each walk, tick for tick, as it was before the enhanced trek camera came: a checksum of its
    /// position and aim every tick, the teleport's fade included.
    /// </summary>
    private static readonly Dictionary<string, ulong> OriginalChecksums = new()
    {
        ["a M05 hallway"] = 0xA9C1F3EA8ACE5EC0,
        ["b I07 entrance"] = 0x6D68DC7BA0342311,
        ["c C15 bend"] = 0x363E6DFD65C444BC,
    };

    [Fact]
    public void TheOriginalCameraIsUnchangedTickForTick()
    {
        var results = CameraWalk.Standard.Select(w => CameraTrace.Run(fixture.Data.Set, w, CameraStyle.Original)).ToList();
        output.WriteLine(CameraTrace.Table(results));
        foreach (var r in results)
        {
            output.WriteLine($"{r.Walk}: 0x{r.Checksum:X16}");
            Assert.Equal(r.Points, r.Ends);
        }
        foreach (var r in results)
            Assert.Equal(OriginalChecksums[r.Walk], r.Checksum);
    }

    /// <summary>
    /// The enhanced camera on the same walks: never past a wall (collision from either side, or the drawn world), the player
    /// never hidden, never seen from the front running down M05's hallway after the arrival's second, and its view turning
    /// no more jerkily than the original's, by the 95th percentile of the turn's change.
    /// </summary>
    [Fact]
    public void TheEnhancedCameraStaysInsideTheWallsAndBehindHim()
    {
        var original = CameraWalk.Standard.Select(w => CameraTrace.Run(fixture.Data.Set, w, CameraStyle.Original)).ToList();
        var enhanced = CameraWalk.Standard.Select(w => CameraTrace.Run(fixture.Data.Set, w, CameraStyle.Enhanced)).ToList();
        output.WriteLine(CameraTrace.Table(original.Concat(enhanced)));
        foreach (var (o, e) in original.Zip(enhanced))
        {
            Assert.Equal(e.Points, e.Ends);
            Assert.Equal(0, e.PastWall);
            Assert.Equal(0, e.Hidden);
            // (b)'s authored shot lets go with him seen from the front, and the swing behind is meant to be quick.
            if (!e.Walk.StartsWith('b'))
                Assert.True(e.Accel95 <= o.Accel95, $"{e.Walk}: the view's turn changes more sharply than the original's");
        }
        foreach (var (o, e) in original.Zip(enhanced))
            Assert.True(e.AheadInFrame >= o.AheadInFrame, $"{e.Walk}: the way ahead is in frame less than under the original");
        // M05's door is at his back: the arrival frame is beside him, and the swing behind as he sets off.
        var hallway = enhanced.Single(r => r.Walk.StartsWith('a'));
        Assert.True(hallway.AheadInFrame > 0.5f);
        // The swing is slow on purpose: his front shows for a moment at most.
        Assert.True(hallway.Front < 0.05f);
        // After I07's authored shot lets go, the swing behind him is slow on purpose, but he's still seen from the front less than under the original.
        Assert.True(enhanced.Single(r => r.Walk.StartsWith('b')).Front < original.Single(r => r.Walk.StartsWith('b')).Front);
    }

    /// <summary>
    /// Every teleporter arrived through, under both cameras and each way of pushing the stick (<see cref="CameraCensus"/>):
    /// under Enhanced, with a player steering into the room, the camera is never past a wall, he's in the frame at the first
    /// tick, he walks in and never back out, the camera is round behind him by 1.5 s with the way ahead in frame, its swing no
    /// faster than the tight turn, and the arrival frame reaches the place's length nearly always. About twenty seconds.
    /// </summary>
    [Fact]
    public void EveryArrivalUnderTheEnhancedCameraFramesHimAndComesRound()
    {
        var arrivals = CameraCensus.Run(fixture.Data.Set);
        output.WriteLine(CameraCensus.Report(arrivals));
        var traced = arrivals.Where(a => a.Skipped is null).ToList();
        Assert.True(traced.Count > 400);
        Assert.True(arrivals.Count(a => a.Skipped is not null and not "a camera object") < 10);
        foreach (var interior in new[] { true, false })
        {
            var group = traced.Where(a => a.Interior == interior).ToList();
            foreach (var stick in new[] { CameraStick.Steer, CameraStick.RestThenSteer })
            {
                var r = group.Select(a => a.Of(CameraStyle.Enhanced, stick)).ToList();
                float Share(Func<ArrivalRun, bool> f) => (float)r.Count(f) / r.Count;
                Assert.True(Share(x => x.Trace.PastWall > 0) <= 0.01f, $"interior {interior} {stick}: past a wall");
                Assert.True(Share(x => x.SeenAtFirst) >= 0.97f, $"interior {interior} {stick}: not seen on arriving");
                Assert.True(Share(x => x.WentIn) >= 0.9f, $"interior {interior} {stick}: didn't walk in");
                Assert.True(Share(x => x.BackOut) <= 0.01f, $"interior {interior} {stick}: walked back out");
                Assert.True(Share(x => x.Trace.Hidden > 0) <= 0.08f, $"interior {interior} {stick}: hidden too often");
                Assert.True(r.Average(x => x.Lifted) <= 0.15f, $"interior {interior} {stick}: lifted too long");
                // The swing at its 90°/s; keeping up with his own turn past a wall, the quickened follow's.
                Assert.True(r.Max(x => x.PeakTurn) <= MathF.Max(TrekCameraTuning.Default.TightTurn, TrekCameraTuning.Default.UrgentTurn) + 5, $"interior {interior} {stick}: whipped round");
            }
            var steer = group.Select(a => a.Of(CameraStyle.Enhanced)).ToList();
            // The swing is slow on purpose: round by 3 s.
            Assert.True((float)steer.Count(x => x.RoundBy3) / steer.Count >= 0.8f, $"interior {interior}: not round behind him by 3 s");
            var frames = steer.Select(x => x.Frame).OfType<TrekArrival>().Where(f => f.Kind != TrekArrivalKind.Authored).ToList();
            Assert.True((float)frames.Count(f => f.Reached || f.Close) / frames.Count >= 0.95f, $"interior {interior}: the frame fell short");
            // Fewer front views than at the full length alone: under a fifth turned more than 110° from behind him.
            Assert.True((float)frames.Count(f => MathF.Abs(f.Off) > 110) / frames.Count < 0.2f, $"interior {interior}: too many front views");
        }
    }

    /// <summary>
    /// An arrival holds: standing where every teleporter set him down, the view doesn't turn for 3 s, but where the teleporter's
    /// own point is kept and the arm eases in. The set-down's push out of a wall was read as travel, and from GwerniaCastle
    /// A07 into A05 and Port Saiid's A09 into the Overworld the view wheeled round behind him though he never moved.
    /// </summary>
    [Fact]
    public void StandingAfterAnArrivalTheViewHolds()
    {
        var plugins = fixture.Data.Set;
        var doors = new[] { ("aidyn:chunk/4/A07", 0), ("aidyn:chunk/2/A09", 3) };
        foreach (var (chunk, index) in doors)
        {
            var from = plugins.Get<HBHT.Plugins.Records.ChunkRecord>(HBHT.Plugins.RecordId.Parse(chunk));
            var door = from.Objects.OfType<HBHT.Plugins.Records.TeleporterObject>().Single(t => t.Index == index);
            var arrival = HBHT.Core.MapObjects.TeleportArrival.For(plugins, door)!;
            var set = int.Parse(chunk.Split('/')[1], System.Globalization.CultureInfo.InvariantCulture);
            var walk = new CameraWalk(chunk, set, from.Name, door.Position + new System.Numerics.Vector3(0, 0.5f, 0), System.Numerics.Vector2.UnitX,
                arrival.Chunk.Id.ToString(), arrival.Chunk.Name, [], CameraCensus.Ticks, index);
            var run = CameraCensus.Trace(plugins, walk, CameraStyle.Enhanced, CameraStick.Still);
            output.WriteLine($"{chunk}/{index}: turned {run.TotalTurn:F1}°");
            // A07 into A05 keeps the teleporter's point, whose aim settles onto his head: a degree or so of drift, no wheeling round.
            Assert.True(run.TotalTurn < 3, $"{chunk}/{index} turned {run.TotalTurn:F1}° standing");
        }
        var arrivals = CameraCensus.Run(plugins);
        var turned = arrivals.Where(a => a.Skipped is null)
            .Select(a => (a.Teleporter, Run: a.Of(CameraStyle.Enhanced, CameraStick.Still)))
            .Where(x => x.Run.Frame?.Kind != TrekArrivalKind.Authored && x.Run.TotalTurn >= 1).ToList();
        foreach (var (t, r) in turned)
            output.WriteLine($"{t}: turned {r.TotalTurn:F1}°");
        Assert.Empty(turned);
    }

    /// <summary>
    /// The walking wedge (<see cref="TrekCameraTuning.WedgeWidth"/>): wandering round Port Saiid's E01 cave (map set 2, from the
    /// Overworld's B15) and along the standard walks, the goal is never put further off behind his travel than the wedge's
    /// clamp, it changes side at most once in the cave's 20 s, and on C15's forest path (c) and down M05's hallway (a) it
    /// never takes hold.
    /// </summary>
    [Fact]
    public void TheWalkingWedgeStaysInsideItsClampAndOffOpenPaths()
    {
        var plugins = fixture.Data.Set;
        var (from, door) = plugins.All<HBHT.Plugins.Records.ChunkRecord>(HBHT.Plugins.RecordTypes.Chunk)
            .SelectMany(c => c.Objects.OfType<HBHT.Plugins.Records.TeleporterObject>().Select(t => (c, t)))
            .First(x => x.c.Name == "B15" && x.t.Leads?.ToString() == "aidyn:chunk/2/E01");
        var cave = new CameraWalk("E01", 0, "B15", door.Position + new System.Numerics.Vector3(0, 0.5f, 0), System.Numerics.Vector2.UnitX,
            "aidyn:chunk/2/E01", "E01", [], 1200, door.Index);
        var width = TrekCameraTuning.Default.WedgeWidth;
        int engaged = 0, flips = 0, last = 0;
        CameraTrace.Run(plugins, cave, CameraStyle.Enhanced, (t, _) =>
        {
            var e = t.EnhancedCamera;
            Assert.True(MathF.Abs(e.WedgePush) <= width + 1e-3f, $"pushed {e.WedgePush:F1}°");
            engaged += e.WedgeEngaged ? 1 : 0;
            flips += e.WedgeSide != 0 && last != 0 && e.WedgeSide != last ? 1 : 0;
            last = e.WedgeSide;
        }, stick: CameraStick.Wander);
        output.WriteLine($"E01 wandering: wedge engaged {engaged} ticks, {flips} changes of side");
        Assert.True(engaged > 0, "the wedge never took hold in the cave");
        Assert.True(flips <= 1);
        foreach (var walk in CameraWalk.Standard.Where(w => w.Name[0] is 'a' or 'c'))
        {
            var held = 0;
            CameraTrace.Run(plugins, walk, CameraStyle.Enhanced, (t, _) => held += t.EnhancedCamera.WedgeEngaged ? 1 : 0);
            Assert.Equal(0, held);
        }
    }

    /// <summary>
    /// Turning back on himself (<see cref="TrekCameraTuning.TurnFrom"/>): walking M05's hallway and back six times, each
    /// turn-back is a planned tuck, the arm never moving in faster than 5 a second after the arrival's first second; and on
    /// open ground (Overworld C14) the same walk is the ordinary follow, tick for tick as with planning off.
    /// </summary>
    [Fact]
    public void ATurnBackIsOneCalmTuckAndOpenGroundIsUnchanged()
    {
        var plugins = fixture.Data.Set;
        static IReadOnlyList<CameraTour.Stop> Back(System.Numerics.Vector2 a, System.Numerics.Vector2 b) =>
            [.. Enumerable.Range(0, 6).Select(i => new CameraTour.Stop(i % 2 == 0 ? b : a, 45))];
        var hall = new CameraWalk("M05", 4, "I07", new System.Numerics.Vector3(15.5f, 5, 4), new System.Numerics.Vector2(0, -1), "aidyn:chunk/4/M05", "M05", [], 3600);
        float fastest = 0, last = float.NaN;
        CameraTrace.Run(plugins, hall, CameraStyle.Enhanced, (t, tick) =>
        {
            var arm = t.EnhancedCamera.Arm;
            if (tick > 60 && !float.IsNaN(last))
                fastest = MathF.Max(fastest, (last - arm) * 60);
            last = arm;
        }, plan: t =>
        {
            var o = t.Chunks.Origin(t.Chunks.Find("M05")!);
            return Back(new(o.X + 4.3f, o.Z + 20), new(o.X + 4.3f, o.Z + 6));
        });
        output.WriteLine($"M05 turn-backs: the arm's fastest inward {fastest:F1} a second");
        Assert.True(fastest <= 5, $"the arm came in at {fastest:F1} a second");
        var open = new CameraWalk("C14", 0, "C14", new System.Numerics.Vector3(40, 0, 53), System.Numerics.Vector2.UnitX, null, "C14", [], 3600);
        ulong Sum(TrekCameraTuning tuning) => CameraTrace.Run(plugins, open, CameraStyle.Enhanced, tuning: tuning, setDown: true, plan: t =>
        {
            var o = t.Chunks.Origin(t.Chunks.Find("C14")!);
            return Back(new(o.X + 40, o.Z + 53), new(o.X + 52, o.Z + 53));
        }).Checksum;
        Assert.Equal(Sum(TrekCameraTuning.Default with { TurnFrom = 0 }), Sum(TrekCameraTuning.Default));
    }

    /// <summary>
    /// Laps hugging GwerniaCastle G01's meeting table (map set 4), whose collision is a hollow box rising well over the drawn
    /// table: under the furniture rule the boom passes it and stays at its length with no falls; under collision it keeps
    /// being cut.
    /// </summary>
    [Fact]
    public void UnderTheFurnitureRuleTheBoomStaysWholeRoundG01sTable()
    {
        var walk = new CameraWalk("g01", 4, "G01", new System.Numerics.Vector3(19.3f, 5, 8.3f), System.Numerics.Vector2.UnitX, null, "G01", [], 7200);
        CameraFeel Laps(string limits)
        {
            var feel = new CameraFeel();
            CameraTrace.Run(fixture.Data.Set, walk, CameraStyle.Enhanced, (t, _) => feel.Tick(t), TrekCameraTuning.Default with { BoomLimits = limits },
                plan: t => CameraTour.Around(t, new System.Numerics.Vector2(t.Player.Sphere.Position.X - 3.25f, t.Player.Sphere.Position.Z + 0.25f), 0.8f, 2),
                setDown: true);
            return feel;
        }
        var furniture = Laps("furniture");
        var collision = Laps("collision");
        output.WriteLine($"furniture: excursions {furniture.Excursions}, arm median {furniture.ArmMedian:F2}; collision: {collision.Excursions}, {collision.ArmMedian:F2}");
        Assert.Equal(0, furniture.Excursions);
        Assert.True(furniture.ArmMedian > 3.4f);
        Assert.Equal(0, furniture.PastDrawn);
        Assert.True(collision.Excursions > 5);
    }
}
