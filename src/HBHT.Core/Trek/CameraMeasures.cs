using System.Globalization;
using System.Numerics;
using HBHT.Core.Actors;
using HBHT.Core.Cameras;
using HBHT.Core.Chunks;
using HBHT.Core.MapObjects;
using HBHT.Core.Physics;
using HBHT.Core.Playthrough;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Trek;

/// <summary>How a <see cref="MeasureWalk"/> is steered.</summary>
public enum MeasureSteer
{
    /// <summary>The stick held forward alone, camera-relative, as a player trusting the camera would.</summary>
    Forward,
    /// <summary>Steered along the path, by the stick read along the camera's look, as <see cref="CameraTrace"/> does.</summary>
    Route,
}

/// <summary>
/// A walk for <see cref="CameraMeasures"/>: trek started in a chunk at (X, Z) <see cref="Local"/>, set down on the ground,
/// facing <see cref="Heading"/>, then steered for at most <see cref="Ticks"/> along <see cref="Path"/> (the chunk's
/// coordinates), sneaking or not. <see cref="Target"/> is what the walk passes, where it matters: an encounter, a container
/// or an npc, in the chunk's coordinates.
/// </summary>
public sealed record MeasureWalk(string Name, int MapSet, string Chunk, Vector2 Local, Vector2 Heading, Vector2[] Path, MeasureSteer Steer,
    int Ticks = 900, bool Sneak = false, Vector2? Target = null)
{
    /// <summary>
    /// The walks slices 2 and 3 of the enhanced trek camera are measured on (docs/systems/cameras.md): forward alone along
    /// C15's forest path into its bend (walk c's) and a second forest path's bend in B15, an open stretch for the bend's lean
    /// staying at nothing, a slope, sneaking past an encounter, and walking past a container and an npc.
    /// </summary>
    public static IReadOnlyList<MeasureWalk> Standard { get; } =
    [
        new("c forward C15", 0, "C15", new(2.8f, 23.9f), new(1, 0),
            [new(2.8f, 23.9f), new(13, 23.6f), new(20, 19.6f), new(23.4f, 21), new(28.6f, 25.6f), new(45.8f, 38.5f)], MeasureSteer.Forward),
        new("d forward C15 north", 0, "C15", new(38, 10), new(1, 0),
            [new(38, 10), new(51, 13), new(57, 22), new(57, 26), new(39.5f, 38)], MeasureSteer.Forward),
        new("open C14", 0, "C14", new(40, 53), new(1, 0), [new(40, 53), new(70, 53)], MeasureSteer.Route),
        new("up D08", 0, "D08", new(34, 66), new(1, 0), [new(34, 66), new(58, 66)], MeasureSteer.Route),
        new("down D08", 0, "D08", new(58, 66), new(-1, 0), [new(58, 66), new(34, 66)], MeasureSteer.Route),
        new("sneak E14", 0, "E14", new(38, 27), new(1, 0), [new(38, 27), new(64, 24)], MeasureSteer.Route, 2400, Sneak: true,
            Target: new(46.8f, 12.2f)),
        new("container C14", 0, "C14", new(13, 25), new(0, 1), [new(13, 25), new(13, 50)], MeasureSteer.Route, Target: new(8, 43)),
        new("npc C15", 0, "C15", new(35, 9), new(1, 0), [new(35, 9), new(60, 12)], MeasureSteer.Route, Target: new(49.4f, 14.1f)),
        new("npc forward C15", 0, "C15", new(35, 9), new(1, 0), [new(35, 9), new(62, 9)], MeasureSteer.Forward, 420, Target: new(49.4f, 14.1f)),
        new("hunter forward E14", 0, "E14", new(36, 23.5f), new(1, 0), [new(36, 23.5f), new(62, 23.5f)], MeasureSteer.Forward, 420,
            Target: new(46.8f, 12.2f)),
        new("street G01", 8, "G01", new(62, 20), new(0, 1), [new(62, 20), new(62, 100)], MeasureSteer.Route, 1800),
    ];
}

/// <summary>What a <see cref="MeasureWalk"/> came to under one camera.</summary>
/// <param name="Progress">How far along its path he got, units.</param>
/// <param name="Pressed">The share of ticks after the first second he moved under a quarter of his usual speed over a tenth of a second, the stick held.</param>
/// <param name="BendPeak">The bend's lean's largest, either way, degrees.</param>
/// <param name="BendSpeed95">The 95th percentile of how fast the bend's lean changes, degrees a second.</param>
/// <param name="NudgePeak">The nudges' largest, either way.</param>
/// <param name="NudgeEnd">The nudges' lean over the walk's last second, the largest either way.</param>
/// <param name="Travelled">How far he went across the ground, units: for a walk held forward, which may leave its path.</param>
/// <param name="Slope">The ground's mean slope ahead of his travel, degrees, rising positive, over the ticks he moved.</param>
/// <param name="SlopePitch">What the slope added to the pitch's goal over the same ticks, the mean.</param>
/// <param name="InFrame">The share of ticks the target was inside the frame and not behind Alaron.</param>
/// <param name="Drift">
/// How far he went sideways toward the target, off the line he started along, by the time he was abreast of it (or at the
/// walk's end): units, positive toward it.
/// </param>
public sealed record MeasureResult(string Walk, CameraStyle Style, int Ticks, float Progress, float PathLength, float Pressed, float BendPeak,
    float BendSpeed95, float NudgePeak, float NudgeEnd, float Travelled, float Slope, float SlopePitch, float InFrame, float Drift = 0)
{
    public static string Header =>
        $"{"walk",-22} {"style",-9} {"ticks",5} {"along",11} {"pressed",8} {"bend pk",8} {"bend95",7} {"nudge pk",9} {"end",5} {"went",5} {"slope",6} {"pitch",6} {"frame",6} {"drift",6}";

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Walk,-22} {Style,-9} {Ticks,5} {Progress,5:F1}/{PathLength,5:F1} {Pressed,8:P0} {BendPeak,8:F1} {BendSpeed95,7:F1} {NudgePeak,9:F1} {NudgeEnd,5:F1} {Travelled,5:F1} {Slope,6:F1} {SlopePitch,6:F1} {InFrame,6:P0} {Drift,6:F2}");
}

/// <summary>
/// The enhanced trek camera's slices 2 and 3 measured headless on real data (<see cref="MeasureWalk"/>): how far a player
/// holding forward gets along a bending path and how much of it he spends pressed against the trees; the bend's lean on open
/// ground and how fast it changes; the pitch against the slope; how much of a sneak the hunter is in the frame; the nudges.
/// </summary>
public static class CameraMeasures
{
    private const float VerticalFov = 45, Aspect = 16f / 9, FrameMargin = 0.9f;

    /// <summary>Trek started where <paramref name="walk"/> starts, set down on the ground, with the drawn world.</summary>
    public static TrekLoop Start(PluginSet plugins, MeasureWalk walk)
    {
        var game = GameState.NewGame(plugins);
        var set = plugins.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, walk.MapSet));
        var streamer = new ChunkStreamer(plugins, game, new CollisionWorld(), set, new VisibleWorld());
        var start = GameStart.OfNewGame(plugins) with
        {
            Set = set, Chunk = walk.Chunk, Local = new Vector3(walk.Local.X, 0, walk.Local.Y), Heading = walk.Heading, SetDown = true,
        };
        var session = GameSession.Start(game, new NullTrekHost(), new ZoneEngine(game, streamer), start);
        if (session.PlayerActor is { } id && plugins.TryGet<ActorRecord>(id) is { } actor)
            session.Trek.PlayerAnimation = ActorMotion.AsDrawn(plugins, actor, game.Random.Animation) ?? IActorMotion.Still;
        return session.Trek;
    }

    /// <summary>Walks <paramref name="walk"/> under <paramref name="style"/> and measures it.</summary>
    public static MeasureResult Run(PluginSet plugins, MeasureWalk walk, CameraStyle style, TrekCameraTuning? tuning = null,
        Action<TrekLoop, int>? each = null)
    {
        var trek = Start(plugins, walk);
        trek.CameraStyle = style;
        trek.EnhancedCamera.Tuning = tuning ?? TrekCameraTuning.Default;
        var origin = trek.Chunks.Origin(trek.Chunks.Find(walk.Chunk) ?? throw new InvalidOperationException($"{walk.Name}: no chunk {walk.Chunk}."));
        var path = walk.Path.Select(p => new Vector2(origin.X + p.X, origin.Z + p.Y)).ToArray();
        var length = 0f;
        for (var i = 1; i < path.Length; i++)
            length += Vector2.Distance(path[i - 1], path[i]);
        var target = walk.Target is { } local ? new Vector2(origin.X + local.X, origin.Z + local.Y) : (Vector2?)null;
        var startAt = new Vector2(trek.Player.Sphere.Position.X, trek.Player.Sphere.Position.Z);
        var ahead = Vector2.Normalize(walk.Heading);
        var side = new Vector2(-ahead.Y, ahead.X);
        if (target is { } t0 && Vector2.Dot(t0 - startAt, side) < 0)
            side = -side;
        float? drift = null;

        var next = 1;
        float progress = 0, bendPeak = 0, nudgePeak = 0, nudgeEnd = 0, travelled = 0;
        var slopes = new List<(float Ground, float Pitch)>();
        var steps = new List<float>();
        var bendSpeeds = new List<float>();
        var nudges = new List<float>();
        int inFrame = 0, framed = 0;
        var lastBend = 0f;
        var last = trek.Player.Sphere.Position;
        var trail = new Queue<Vector3>();
        var ticks = 0;
        for (; ticks < walk.Ticks; ticks++)
        {
            var at = trek.Player.Sphere.Position;
            var flat = new Vector2(at.X, at.Z);
            while (next < path.Length - 1 && Vector2.Distance(flat, path[next]) < 0.8f)
                next++;
            Vector2 stick;
            if (walk.Steer == MeasureSteer.Forward)
            {
                stick = new Vector2(0, 1);
            }
            else
            {
                if (Vector2.Distance(flat, path[^1]) < 0.6f)
                    break;
                stick = Pilot.StickFor(Vector2.Normalize(path[next] - flat), trek.Camera.LookXZ);
            }
            var step = trek.Tick(() => new PlayerInput(stick, walk.Sneak));
            if (step != TrekStep.Ran)
                break;

            var player = trek.Player.Sphere.Position;
            var moved = new Vector2(player.X - at.X, player.Z - at.Z).Length();
            // Root motion moves him a 30 fps frame at a time, unevenly, so his speed is read over a tenth of a second.
            trail.Enqueue(player);
            if (trail.Count > 6)
                last = trail.Dequeue();
            if (ticks >= 60)
                steps.Add(new Vector2(player.X - last.X, player.Z - last.Z).Length());
            progress = MathF.Max(progress, Along(path, new Vector2(player.X, player.Z)));

            var enhanced = trek.EnhancedCamera;
            var bend = style == CameraStyle.Enhanced ? trek.BendLean?.Value ?? 0 : 0;
            var nudge = style == CameraStyle.Enhanced ? trek.Nudges?.Value ?? 0 : 0;
            bendPeak = MathF.Max(bendPeak, MathF.Abs(bend));
            bendSpeeds.Add(MathF.Abs(bend - lastBend) * 60);
            lastBend = bend;
            nudgePeak = MathF.Max(nudgePeak, MathF.Abs(nudge));
            nudges.Add(nudge);
            travelled += moved;
            if (ticks >= 60 && moved > 0.005f
                && EnhancedTrekCamera.GroundSlope(trek.Chunks.World, player, Vector2.Normalize(new Vector2(player.X - at.X, player.Z - at.Z)), enhanced.Tuning.SlopeAhead) is { } slope)
                slopes.Add((slope, style == CameraStyle.Enhanced ? enhanced.SlopePitch : 0));

            if (target is { } passing && drift is null)
            {
                var now = new Vector2(player.X, player.Z) - startAt;
                if (Vector2.Dot(now, ahead) >= Vector2.Dot(passing - startAt, ahead))
                    drift = Vector2.Dot(now, side);
            }
            if (target is { } goal)
            {
                var hunter = Nearest(trek, goal) ?? new Vector3(goal.X, player.Y, goal.Y);
                framed++;
                if (InView(trek.Camera.Position, trek.Camera.Aim, hunter, player, trek.Chunks.World, trek.Chunks.Visible))
                    inFrame++;
            }
            each?.Invoke(trek, ticks);
        }
        var usual = steps.Count == 0 ? 0 : steps.Order().ElementAt((int)(steps.Count * 0.9f));
        var pressed = steps.Count == 0 ? 0 : (float)steps.Count(s => s < usual / 4) / steps.Count;
        nudgeEnd = nudges.TakeLast(60).Select(MathF.Abs).DefaultIfEmpty().Max();
        bendSpeeds.Sort();
        var bend95 = bendSpeeds.Count == 0 ? 0 : bendSpeeds[Math.Min(bendSpeeds.Count - 1, (int)(bendSpeeds.Count * 0.95f))];
        return new MeasureResult(walk.Name, style, ticks, progress, length, pressed, bendPeak, bend95, nudgePeak, nudgeEnd, travelled,
            slopes.Count == 0 ? 0 : slopes.Average(s => s.Ground), slopes.Count == 0 ? 0 : slopes.Average(s => s.Pitch), framed == 0 ? 0 : (float)inFrame / framed,
            target is null ? 0 : drift ?? Vector2.Dot(new Vector2(last.X, last.Z) - startAt, side));
    }

    /// <summary>How far along <paramref name="path"/> the point nearest <paramref name="at"/> is.</summary>
    public static float Along(Vector2[] path, Vector2 at)
    {
        float best = float.MaxValue, along = 0, run = 0;
        for (var i = 1; i < path.Length; i++)
        {
            var a = path[i - 1];
            var ab = path[i] - a;
            var l = ab.Length();
            var s = l < 1e-6f ? 0 : Math.Clamp(Vector2.Dot(at - a, ab) / (l * l), 0, 1);
            var d = Vector2.Distance(at, a + ab * s);
            if (d < best)
                (best, along) = (d, run + s * l);
            run += l;
        }
        return along;
    }

    /// <summary>The body of a wanderer nearest <paramref name="home"/>'s, within 6 of it, the walk's target as it moves.</summary>
    private static Vector3? Nearest(TrekLoop trek, Vector2 home) =>
        trek.Wanderers.All.Select(w => w.Body.Sphere.Position)
            .Where(p => Vector2.Distance(new Vector2(p.X, p.Z), home) < 6)
            .OrderBy(p => Vector2.Distance(new Vector2(p.X, p.Z), home))
            .Cast<Vector3?>().FirstOrDefault();

    /// <summary>
    /// Whether <paramref name="target"/> (a body's centre) is inside a 16:9 frame of the runtime's 45° high lens, with a 10%
    /// margin, nothing solid between, and not behind Alaron: not within his apparent size of his centre beyond him.
    /// </summary>
    public static bool InView(Vector3 camera, Vector3 aim, Vector3 target, Vector3 player, CollisionWorld world, VisibleWorld? visible)
    {
        var forward = Vector3.Normalize(aim - camera);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var to = target + new Vector3(0, 0.5f, 0) - camera;
        var depth = Vector3.Dot(to, forward);
        if (depth <= 0.1f)
            return false;
        var tanY = MathF.Tan(VerticalFov / 2 * MathF.PI / 180) * FrameMargin;
        var tanX = MathF.Tan(VerticalFov / 2 * MathF.PI / 180) * Aspect * FrameMargin;
        if (MathF.Abs(Vector3.Dot(to, right) / depth) > tanX || MathF.Abs(Vector3.Dot(to, up) / depth) > tanY)
            return false;
        // Behind Alaron: farther than him and within the angle his body (about 0.6 across, 1.8 high) covers.
        var him = player + new Vector3(0, 0.5f, 0) - camera;
        if (to.Length() > him.Length())
        {
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(to), Vector3.Normalize(him)), -1, 1));
            if (angle < MathF.Atan(0.6f / him.Length()))
                return false;
        }
        return !CameraTrace.Crosses(world, visible, camera, target + new Vector3(0, 0.5f, 0));
    }

    /// <summary>The table of results, a line each, under its header.</summary>
    public static string Table(IEnumerable<MeasureResult> results) =>
        string.Join(Environment.NewLine, new[] { MeasureResult.Header }.Concat(results.Select(r => r.ToString()))) + Environment.NewLine;

    /// <summary>A host that presents nothing.</summary>
    private sealed class NullTrekHost : ITrekHost
    {
        void ITrekHost.Arrived()
        {
        }

        void ITrekHost.ConversationChanged()
        {
        }

        Movement.IRootMotion ITrekHost.Motion(Wanderer wanderer) => IActorMotion.Still;

        void ITrekHost.Animate(LoadedMap map, SceneObject actor, int request)
        {
        }

        void ITrekHost.Changed(LoadedMap map, SceneObject scene)
        {
        }

        void ITrekHost.Log(string text)
        {
        }

        void ITrekHost.Warn(string text)
        {
        }

        void ITransitionHost.Transition(Transition next)
        {
        }

        void Scripts.IScriptHost.Cue(Scripts.ScriptCue cue)
        {
        }
    }
}
