using System.Globalization;
using System.Numerics;
using System.Text;
using HBHT.Core.Actors;
using HBHT.Core.Cameras;
using HBHT.Core.Chunks;
using HBHT.Core.MapObjects;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Playthrough;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Trek;

/// <summary>
/// A fixed walk for <see cref="CameraTrace"/>: trek started at a chunk's point (on the ground), optionally through the
/// teleporter of that chunk which leads to <see cref="Door"/>, then the player steered along <see cref="Route"/>, points in
/// <see cref="RouteChunk"/>'s coordinates (X and Z), running, by the stick read along the camera's look. With no route, he
/// runs along the way he arrived facing for <see cref="MostTicks"/>. <see cref="DoorIndex"/> names the teleporter by its
/// index in the start's chunk, where several lead to <see cref="Door"/>.
/// </summary>
public sealed record CameraWalk(string Name, int MapSet, string Chunk, Vector3 Local, Vector2 Heading, string? Door, string RouteChunk,
    Vector2[] Route, int MostTicks = 900, int? DoorIndex = null)
{
    /// <summary>
    /// The walks the enhanced trek camera is checked on (docs/systems/cameras.md): (a) into GwerniaCastle's M05 from the
    /// courtyard and down its hallway, (b) in at the castle's entrance, I07, from the Overworld, where a camera object holds
    /// the view, and (c) along the forest path in the Overworld's C15 into its bend.
    /// </summary>
    public static IReadOnlyList<CameraWalk> Standard { get; } =
    [
        new("a M05 hallway", 4, "I07", new Vector3(15.5f, 5, 4), new Vector2(0, -1), "aidyn:chunk/4/M05", "M05",
            [new(4.3f, 20f), new(4.3f, 4f)]),
        new("b I07 entrance", 0, "M06", new Vector3(68.9f, -19.4f, 62.5f), new Vector2(0, 1), "aidyn:chunk/4/I07", "I07",
            [new(30f, 20.6f), new(26.5f, 20.3f)]),
        new("c C15 bend", 0, "C15", new Vector3(2.8f, -26.5f, 23.9f), new Vector2(0, 1), null, "C15",
            [new(9f, 23.9f), new(13f, 23.6f), new(20f, 19.6f)]),
    ];
}

/// <summary>How the stick is pushed on a walk with no route (<see cref="CameraTrace.Run"/>).</summary>
public enum CameraStick
{
    /// <summary>A player who steers correctly: each tick, whatever way of the camera's heads him along the way he arrived facing.</summary>
    Steer,
    /// <summary>At rest for half a second, then steering as <see cref="Steer"/>.</summary>
    RestThenSteer,
    /// <summary>Held up from the first tick, whatever the camera does: with a front view, back toward the door.</summary>
    Up,

    /// <summary>At rest throughout: he stands where he arrived.</summary>
    Still,

    /// <summary>At rest for half a second, then steering correctly toward where the camera was then: walking at the lens.</summary>
    Down,

    /// <summary>Steering correctly, along a way that turns 70° every two seconds from the one he arrived facing: a walk round a room.</summary>
    Wander,
}

/// <summary>
/// One walk's trace of trek's camera, as shares of ticks and the view's turning: the view ahead blocked close by (a level ray
/// from the aim along the look, out to 3 past the player, crosses collision from either side or the drawn world), the camera
/// past a wall (the segment from the aim to it does), the player hidden from it, and the camera looking at his front while
/// he moves (within 60° of against the way his body faces); the view ahead blocked again from the walk's second second; and how fast the view turns, its peak and 95th percentile, and how fast that
/// changes. The front and the turning are counted from the walk's second second, past the arrival.
/// </summary>
public sealed record CameraTraceResult(string Walk, CameraStyle Style, int Ticks, float Blocked, float BlockedLate, float PastWall, float Hidden, float Front,
    float PeakTurn, float Turn95, float PeakAccel, float Accel95, ulong Checksum, int Ends, int Points, int Held = 0,
    float AheadInFrame = float.NaN, bool? AheadAtFirst = null, float Settle = float.NaN, float SettleOff = float.NaN)
{
    public static string Header =>
        $"{"walk",-16} {"style",-9} {"ticks",5} {"blocked",8} {">1s",5} {"past",6} {"hidden",7} {"front",6} {"turn pk",8} {"turn95",7} {"acc pk",8} {"acc95",7} {"ahead",6} {"route",5}";

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Walk,-16} {Style,-9} {Ticks,5} {Blocked,8:P0} {BlockedLate,5:P0} {PastWall,6:P0} {Hidden,7:P0} {Front,6:P0} {PeakTurn,8:F0} {Turn95,7:F0} {PeakAccel,8:F0} {Accel95,7:F0} {(float.IsNaN(AheadInFrame) ? "-" : AheadInFrame.ToString("P0", CultureInfo.InvariantCulture)),6} {Ends,3}/{Points}");
}

/// <summary>
/// A headless trace of trek's camera over a fixed walk with real data (<see cref="CameraWalk"/>), under either camera, with
/// the drawn world loaded: the rig the enhanced trek camera is tuned on (docs/systems/cameras.md). Turning is in degrees a
/// second, and its change in degrees a second squared.
/// </summary>
public static class CameraTrace
{
    /// <summary>How far past the player the view ahead is looked along.</summary>
    public const float AheadReach = 3;

    /// <summary>The player's travel that counts as moving, units a tick.</summary>
    private const float Moving = 0.01f;

    private const int TicksPerSecond = 60;

    /// <summary>Trek started where <paramref name="walk"/> starts, on the ground, with the drawn world.</summary>
    /// <param name="setDown">Whether he's set down on the ground under <see cref="CameraWalk.Local"/> rather than put there.</param>
    public static TrekLoop Start(PluginSet plugins, CameraWalk walk, bool setDown = false) => Start(plugins, walk, out _, setDown);

    private static TrekLoop Start(PluginSet plugins, CameraWalk walk, out Host host, bool setDown = false)
    {
        var game = GameState.NewGame(plugins);
        var set = plugins.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, walk.MapSet));
        var streamer = new ChunkStreamer(plugins, game, new CollisionWorld(), set, new VisibleWorld());
        var start = GameStart.OfNewGame(plugins) with { Set = set, Chunk = walk.Chunk, Local = walk.Local, Heading = walk.Heading, SetDown = setDown };
        host = new Host();
        var session = GameSession.Start(game, host, new ZoneEngine(game, streamer), start);
        // The player's body plays the party leader's actor, as the runtime's does, which moves him by its root motion.
        if (session.PlayerActor is { } id && plugins.TryGet<ActorRecord>(id) is { } actor)
            session.Trek.PlayerAnimation = ActorMotion.AsDrawn(plugins, actor, game.Random.Animation) ?? IActorMotion.Still;
        return session.Trek;
    }

    /// <summary>Walks <paramref name="walk"/> under <paramref name="style"/> and measures the camera.</summary>
    /// <param name="each">Called after each tick traced, with the tick's number from the arrival.</param>
    /// <param name="stick">How the stick is pushed on a walk with no route.</param>
    public static CameraTraceResult Run(PluginSet plugins, CameraWalk walk, CameraStyle style, Action<TrekLoop, int>? each = null,
        TrekCameraTuning? tuning = null, CameraStick stick = CameraStick.Steer, int standTicks = 0, bool tour = false,
        Func<TrekLoop, IReadOnlyList<CameraTour.Stop>>? plan = null, bool setDown = false)
    {
        var trek = Start(plugins, walk, out var host, setDown);
        trek.CameraStyle = style;
        trek.EnhancedCamera.Tuning = tuning ?? TrekCameraTuning.Default;
        var checksum = 14695981039346656037UL;
        void Sum(Vector3 v)
        {
            foreach (var f in new[] { v.X, v.Y, v.Z })
                checksum = (checksum ^ BitConverter.SingleToUInt32Bits(f)) * 1099511628211UL;
        }

        if (walk.Door is { } door)
        {
            var map = trek.Chunks.MapAt(trek.Player.Sphere.Position) ?? throw new InvalidOperationException($"{walk.Name}: no chunk at the start.");
            var teleporter = map.Objects.OfType<TeleporterObject>()
                .Where(t => t.Leads?.ToString() == door && (walk.DoorIndex is not { } index || t.Index == index))
                .MinBy(t => Vector3.Distance(map.Origin + t.Position, trek.Player.Sphere.Position))
                ?? throw new InvalidOperationException($"{walk.Name}: {map.Record.Id} has no teleporter to {door}.");
            ((IMapObjectHost)trek).Teleport(map, teleporter);
            var arrived = false;
            for (var i = 0; i < 600 && !arrived; i++)
            {
                trek.Tick(() => new PlayerInput(Vector2.Zero));
                Sum(trek.Camera.Position);
                Sum(trek.Camera.Aim);
                arrived = host.Arrivals > 0;
            }
            if (!arrived)
                throw new InvalidOperationException($"{walk.Name}: never arrived in {door}.");
        }

        var routeChunk = trek.Chunks.Find(walk.RouteChunk) ?? throw new InvalidOperationException($"{walk.Name}: no chunk {walk.RouteChunk}.");
        var origin = trek.Chunks.Origin(routeChunk);
        var route = walk.Route.Select(p => new Vector2(origin.X + p.X, origin.Z + p.Y)).ToArray();
        // A tour of the place (CameraTour), with its stops.
        var pauses = new int[route.Length];
        if (tour || plan is not null)
        {
            tour = true;
            var stops = plan?.Invoke(trek) ?? CameraTour.Plan(trek);
            route = [.. stops.Select(x => x.At)];
            pauses = [.. stops.Select(x => x.Ticks)];
        }
        var paused = 0;
        // On a tour, a waypoint he makes no headway toward for a second is passed over, as a player would find another way.
        float nearest = float.MaxValue;
        int since = 0, skipped = 0;
        // With no route, he holds the way he arrived facing.
        var holding = route.Length == 0;
        var facing = trek.Player.Heading;
        Vector2? towardLens = null;
        var held = 0;
        int aheadSeen = 0, aheadCounted = 0;
        bool? aheadAtFirst = null;
        var next = 0;
        var ticks = 0;
        int blocked = 0, blockedLate = 0, past = 0, hidden = 0, front = 0, late = 0;
        var turns = new List<float>();
        var accels = new List<float>();
        Vector3? lastLook = null;
        Vector3? lastSpin = null;
        var lastPlayer = trek.Player.Sphere.Position;
        var stuck = 0;
        for (; ticks < walk.MostTicks && (holding || next < route.Length || paused > 0) && stuck < 60; ticks++)
        {
            var at = trek.Player.Sphere.Position;
            var flatAt = new Vector2(at.X, at.Z);
            while (paused == 0 && next < route.Length && Vector2.Distance(flatAt, route[next]) < 0.6f)
            {
                paused = pauses[next];
                next++;
                nearest = float.MaxValue;
                skipped = 0;
            }
            if (tour && paused == 0 && next < route.Length)
            {
                var d = Vector2.Distance(flatAt, route[next]);
                if (d < nearest - 0.05f)
                    (nearest, since) = (d, 0);
                else if (++since > 60)
                    (next, nearest, since, skipped) = (next + 1, float.MaxValue, 0, skipped + 1);
                // Three passed over in a row: he's jammed in a pinch the floor's grid let through, and the tour ends.
                if (skipped >= 3)
                    break;
            }
            if (!holding && next >= route.Length && paused == 0)
                break;
            if (paused > 0)
                paused--;
            var way = !holding ? (next < route.Length && route[next] != flatAt ? Vector2.Normalize(route[next] - flatAt) : facing)
                : stick == CameraStick.Wander ? Turned(facing, 70 * (ticks / 120))
                : facing;
            var look = trek.Camera.LookXZ;
            var push = paused > 0 ? Vector2.Zero
                : !holding ? Pilot.StickFor(way, look)
                : stick == CameraStick.Up ? Vector2.UnitY
                : stick == CameraStick.Still ? Vector2.Zero
                : stick == CameraStick.Down ? (ticks < RestTicks ? Vector2.Zero : Pilot.StickFor(towardLens ??= LensWay(trek), look))
                : stick == CameraStick.RestThenSteer && ticks < RestTicks ? Vector2.Zero
                : Pilot.StickFor(way, look);
            trek.Tick(() => new PlayerInput(push));
            var camera = trek.Camera;
            Sum(camera.Position);
            Sum(camera.Aim);
            each?.Invoke(trek, ticks);
            if (trek.Use.CurrentCamera is not null)
                held++;
            if (AheadInFrame(trek) is { } inFrame)
            {
                aheadCounted++;
                if (inFrame)
                    aheadSeen++;
                if (ticks == 0)
                    aheadAtFirst = inFrame;
            }

            var player = trek.Player.Sphere.Position;
            var world = trek.Chunks.World;
            var visible = trek.Chunks.Visible;
            var view = Vector3.Normalize(camera.Aim - camera.Position);
            var level = new Vector3(view.X, 0, view.Z);
            if (level.LengthSquared() > 1e-8f)
            {
                level = Vector3.Normalize(level);
                var reach = MathF.Max(0, Vector3.Dot(player - camera.Aim, level)) + AheadReach;
                if (Crosses(world, visible, camera.Aim, camera.Aim + level * reach))
                {
                    blocked++;
                    if (ticks >= TicksPerSecond)
                        blockedLate++;
                }
            }
            // Past a wall: the boom, from what it turns about (the enhanced camera's pivot, the original's aim) to the camera.
            var boom = trek.CameraStyle == CameraStyle.Enhanced ? trek.EnhancedCamera.Pivot : camera.Aim;
            if (Crosses(world, visible, boom, camera.Position))
                past++;
            if (Crosses(world, visible, camera.Position, player + new Vector3(0, 0.5f, 0)))
                hidden++;
            var travel = player - lastPlayer;
            travel.Y = 0;
            stuck = travel.Length() < 0.002f && ticks > 120 && push != Vector2.Zero ? stuck + 1 : 0;
            if (ticks >= TicksPerSecond)
            {
                late++;
                // His front is the way his body faces, not his travel, which a bounce off a wall turns back.
                var faces = trek.Player.Heading;
                if (travel.Length() > Moving && Vector3.Dot(new Vector3(faces.X, 0, faces.Y), level) < -0.5f)
                    front++;
                if (lastLook is { } before)
                {
                    var spin = Vector3.Cross(before, view);
                    var angle = MathF.Asin(Math.Clamp(spin.Length(), 0, 1)) * 180 / MathF.PI * TicksPerSecond;
                    turns.Add(angle);
                    spin = spin.LengthSquared() > 0 ? Vector3.Normalize(spin) * angle : Vector3.Zero;
                    if (lastSpin is { } was)
                        accels.Add((spin - was).Length() * TicksPerSecond);
                    lastSpin = spin;
                }
            }
            lastLook = view;
            lastPlayer = player;
        }

        // Standing on after the walk: how long from his heading's last change the view takes to come to rest (turning under
        // half a degree a second from then on), and how far its look rests from the way he faces.
        var settle = float.NaN;
        var settleOff = float.NaN;
        if (standTicks > 0)
        {
            var lastHeading = trek.Player.Heading;
            var changed = 0;
            var moving = 0;
            var look = trek.Camera.LookXZ;
            for (var i = 1; i <= standTicks; i++)
            {
                trek.Tick(() => new PlayerInput(Vector2.Zero));
                each?.Invoke(trek, ticks + i - 1);
                var heading = trek.Player.Heading;
                if (Vector2.Dot(heading, lastHeading) < MathF.Cos(0.5f * MathF.PI / 180))
                {
                    changed = i;
                    lastHeading = heading;
                }
                var now = trek.Camera.LookXZ;
                if (CameraCensus.Between(look, now) * TicksPerSecond >= 0.5f)
                    moving = i;
                look = now;
            }
            settle = (float)(Math.Max(moving, changed) - changed) / TicksPerSecond;
            settleOff = MathF.Acos(Math.Clamp(Vector2.Dot(trek.Camera.LookXZ, trek.Player.Heading), -1, 1)) * 180 / MathF.PI;
        }

        float Share(int n) => ticks == 0 ? 0 : (float)n / ticks;
        return new CameraTraceResult(walk.Name, style, ticks, Share(blocked), late == 0 ? 0 : (float)blockedLate / late, Share(past), Share(hidden), late == 0 ? 0 : (float)front / late,
            Peak(turns), Percentile(turns, 0.95f), Peak(accels), Percentile(accels, 0.95f), checksum, next, route.Length, held,
            aheadCounted == 0 ? float.NaN : (float)aheadSeen / aheadCounted, aheadAtFirst, settle, settleOff);
    }

    private static Vector2 LensWay(TrekLoop trek)
    {
        var to = new Vector2(trek.Camera.Position.X - trek.Player.Sphere.Position.X, trek.Camera.Position.Z - trek.Player.Sphere.Position.Z);
        return to.LengthSquared() > 1e-6f ? Vector2.Normalize(to) : -trek.Player.Heading;
    }

    private static Vector2 Turned(Vector2 v, float degrees)
    {
        var (s, c) = MathF.SinCos(degrees * MathF.PI / 180);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    /// <summary>How long <see cref="CameraStick.RestThenSteer"/> leaves the stick at rest.</summary>
    public const int RestTicks = 30;

    /// <summary>Whether <paramref name="point"/> is inside the runtime's lens from trek's camera and not behind a wall from it.</summary>
    public static bool InFrame(TrekLoop trek, Vector3 point)
    {
        var camera = trek.Camera;
        var forward = Vector3.Normalize(camera.Aim - camera.Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var to = point - camera.Position;
        var depth = Vector3.Dot(to, forward);
        if (depth <= 0)
            return false;
        var tan = MathF.Tan(HalfHigh * MathF.PI / 180);
        if (MathF.Abs(Vector3.Dot(to, up)) / depth > tan || MathF.Abs(Vector3.Dot(to, right)) / depth > tan * Aspect)
            return false;
        return !Crosses(trek.Chunks.World, trek.Chunks.Visible, camera.Position, point);
    }

    /// <summary>How far ahead of him the way ahead is looked for, at head height.</summary>
    public const float AheadSight = 8;

    /// <summary>The runtime's lens: 45° high, on a 16:9 screen.</summary>
    private const float HalfHigh = 22.5f, Aspect = 16f / 9;

    /// <summary>
    /// Whether the way ahead is in frame: the point <see cref="AheadSight"/> ahead of him along the way his body faces, at head
    /// height (1.05 above his sphere's centre), inside the lens and not behind a wall from the camera; or null where a wall is
    /// nearer than that ahead of him, when there's no way ahead to see.
    /// </summary>
    public static bool? AheadInFrame(TrekLoop trek)
    {
        var camera = trek.Camera;
        var world = trek.Chunks.World;
        var visible = trek.Chunks.Visible;
        var head = trek.Player.Sphere.Position + new Vector3(0, 1.05f, 0);
        var faces = trek.Player.Heading;
        var point = head + new Vector3(faces.X, 0, faces.Y) * AheadSight;
        if (Crosses(world, visible, head, point))
            return null;
        return InFrame(trek, point);
    }

    /// <summary>
    /// Whether the segment crosses collision, tested from both ends, or the drawn world's solid surfaces (not light shafts,
    /// canopies' cut-outs or water, <see cref="VisibleWorld.RaycastSolid"/>).
    /// </summary>
    public static bool Crosses(CollisionWorld world, VisibleWorld? visible, Vector3 from, Vector3 to) =>
        SpherePhysics.SweepEitherSide(world, from, to, 0.01f, skipSeeThrough: true) is not null
        || visible?.RaycastSolid(from, to) is not null;

    /// <summary>The table of results, a line each, under its header.</summary>
    public static string Table(IEnumerable<CameraTraceResult> results)
    {
        var text = new StringBuilder(CameraTraceResult.Header).AppendLine();
        foreach (var r in results)
            text.AppendLine(r.ToString());
        return text.ToString();
    }

    private static float Peak(List<float> values) => values.Count == 0 ? 0 : values.Max();

    private static float Percentile(List<float> values, float share)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.Order().ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)(share * sorted.Count))];
    }

    /// <summary>A host that presents nothing.</summary>
    private sealed class Host : ITrekHost
    {
        public int Arrivals { get; private set; }

        void ITrekHost.Arrived() => Arrivals++;

        void ITrekHost.ConversationChanged()
        {
        }

        IRootMotion ITrekHost.Motion(Wanderer wanderer) => IActorMotion.Still;

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

        void IScriptHost.Cue(ScriptCue cue)
        {
        }
    }
}
