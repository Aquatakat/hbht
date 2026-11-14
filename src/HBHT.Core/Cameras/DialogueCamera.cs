using System.Numerics;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Plugins.Records;

namespace HBHT.Core.Cameras;

/// <summary>A talker as the conversation camera sees it: its sphere and its eyes.</summary>
public readonly record struct TalkingBody(Vector3 Centre, float Radius, Vector3 Eyes)
{
    /// <summary>Alaron's eyes above his feet, for a body with no look (<c>CutSceneActor.Eyes</c>' formula gives about this).</summary>
    public const float DefaultEyeHeight = 1.62f;

    /// <summary>
    /// A body's sphere and eyes: <c>CutSceneActor.Eyes</c>' formula, the sphere's centre raised by its height × scale less 0.2 and its
    /// scaled radius, and 0.15 forward along its heading. With no look, <see cref="DefaultEyeHeight"/> above its feet.
    /// </summary>
    public static TalkingBody Of(Body body, EntityBody? look) => At(body.Sphere.Position, body.ScaledRadius, body.Heading, look);

    /// <summary>A body whose sphere's centre is <paramref name="at"/>, its scaled radius <paramref name="radius"/>, facing <paramref name="heading"/>.</summary>
    public static TalkingBody At(Vector3 at, float radius, Vector2 heading, EntityBody? look)
    {
        var height = look is { } l ? l.Height * l.Scale - Scripts.CutSceneActor.EyeBelowTop - radius : DefaultEyeHeight - radius;
        var forward = heading * Scripts.CutSceneActor.EyeForward;
        return new TalkingBody(at, radius, new Vector3(at.X + forward.X, at.Y + height, at.Z + forward.Y));
    }
}

/// <summary>What a conversation's shots are fitted against: the collision, the drawn world if the host has one, the two talkers and anyone else nearby.</summary>
public sealed record DialogueScene(CollisionWorld World, VisibleWorld? Visible, TalkingBody Player, TalkingBody Npc, IReadOnlyList<ShotSubject> Others)
{
    public TalkingBody Of(Talker talker) => talker == Talker.Player ? Player : Npc;

    /// <summary>The point between their eyes, which every shot's way is tested from.</summary>
    public Vector3 Focus => (Player.Eyes + Npc.Eyes) / 2;
}

/// <summary>
/// The enhanced conversation camera (Camera: Enhanced, docs/systems/cameras.md): in place of the original's framing
/// (<see cref="FollowCamera.Frame"/>), it chooses a shot a page at a time (<see cref="DialoguePlanner"/>), places it on the side of
/// the line between the two talkers chosen as the conversation starts, tests it against the world and the bodies, falling down a
/// ladder to the original's framing, cuts to it and lets it drift in slowly. It writes the follow camera's eye and aim, so
/// everything that reads the camera reads it.
/// </summary>
public sealed class DialogueCamera(DialoguePlanner planner, DialogueTuning? tuning = null)
{
    private readonly DialogueTuning _t = tuning ?? DialogueTuning.Default;

    public DialoguePlanner Planner { get; } = planner;

    /// <summary>The side of the line of action, +1 or −1 of its perpendicular (<see cref="Across"/>), or 0 before the first tick.</summary>
    public int Side { get; private set; }

    /// <summary>The shot the planner chose for the page showing.</summary>
    public DialogueShot Wanted { get; private set; }

    /// <summary>The shot that fitted and is shown: <see cref="Wanted"/>, or a step down the ladder.</summary>
    public DialogueShot Shown { get; private set; }

    /// <summary>Where the shot shown stands before its drift, or null for the original's framing.</summary>
    public Shot? Base { get; private set; }

    private long _tick, _shownAt;
    private object? _page;
    private bool _started;

    /// <summary>
    /// One tick: a new page (told by <paramref name="key"/> changing, the line shown, by value) is planned, and a new shot fitted and cut to;
    /// then the shot is drifted and written into <paramref name="camera"/>.
    /// </summary>
    public void Step(FollowCamera camera, DialogueScene scene, DialoguePage? page, object? key)
    {
        _tick++;
        if (Side == 0)
            Side = ChooseSide(scene, camera.Position, _t);
        var wanted = Wanted;
        if (page is { } p && !Equals(key, _page))
        {
            _page = key;
            wanted = Planner.Next(p, _tick);
        }
        else if (!_started)
        {
            wanted = DialogueShot.TwoShot;
        }
        if (!_started || wanted != Wanted)
        {
            _started = true;
            Wanted = wanted;
            (Shown, Base) = Fit(scene, Side, wanted, _t);
            _shownAt = _tick;
            camera.Cut();
        }
        if (Base is not { } shot)
        {
            camera.Frame(scene.World, scene.Player.Centre, scene.Npc.Centre);
            return;
        }
        var drift = MathF.Min(MathF.Min((_tick - _shownAt) / 60f * _t.DriftSpeed, _t.DriftMost), MathF.Max(shot.Distance - 1.5f, 0));
        camera.PlaceAt(shot.Eye + shot.Forward * drift);
        camera.AimAt(shot.Aim);
        camera.Orient();
    }

    /// <summary>The perpendicular of the line from Alaron to the npc, across the ground, a unit long.</summary>
    public static Vector3 Across(DialogueScene scene)
    {
        var u = Flat(scene.Npc.Eyes - scene.Player.Eyes);
        u = u.LengthSquared() > 1e-6f ? Vector3.Normalize(u) : Vector3.UnitX;
        return new Vector3(-u.Z, 0, u.X);
    }

    /// <summary>Which side of the line a point is on, +1 or −1.</summary>
    public static int SideOf(DialogueScene scene, Vector3 point) => Vector3.Dot(Flat(point - scene.Focus), Across(scene)) < 0 ? -1 : 1;

    /// <summary>
    /// The side of the line, chosen once as the conversation starts: the side the camera is on, unless the other has clearly more
    /// room (<see cref="DialogueTuning.SideMargin"/> more, looked along to <see cref="DialogueTuning.SideReach"/>).
    /// </summary>
    public static int ChooseSide(DialogueScene scene, Vector3 camera, DialogueTuning t)
    {
        var side = SideOf(scene, camera);
        var here = Room(scene, side, t.SideReach);
        var there = Room(scene, -side, t.SideReach);
        return there >= here + t.SideMargin ? -side : side;
    }

    /// <summary>How far from the focus the way to one side is open, to <paramref name="reach"/>, tested from both ends and against the drawn world.</summary>
    public static float Room(DialogueScene scene, int side, float reach)
    {
        var from = scene.Focus;
        var to = from + Across(scene) * side * reach;
        var room = reach;
        if (SpherePhysics.Sweep(scene.World, from, to, 0.2f, skipSeeThrough: true, out var hit, out _))
            room = MathF.Min(room, Vector3.Distance(from, hit));
        if (SpherePhysics.Sweep(scene.World, to, from, 0.2f, skipSeeThrough: true, out hit, out _))
            room = MathF.Min(room, MathF.Max(0, Vector3.Distance(from, hit) - 0.4f));
        if (scene.Visible?.Raycast(from, to, out hit, out _, out _) == true)
            room = MathF.Min(room, Vector3.Distance(from, hit));
        return room;
    }

    /// <summary>The ladder a shot falls down when it doesn't fit, ending at the original's framing.</summary>
    public static IEnumerable<DialogueShot> Ladder(DialogueShot shot)
    {
        yield return shot;
        if (shot.Kind == DialogueShotKind.OverShoulder)
            yield return shot with { Kind = DialogueShotKind.Single };
        if (shot.Kind == DialogueShotKind.Punch)
            yield return shot with { Kind = DialogueShotKind.Single };
        // A single that can't be clean is honestly an over-the-shoulder.
        if (shot.Kind is DialogueShotKind.Single or DialogueShotKind.Punch)
            yield return shot with { Kind = DialogueShotKind.OverShoulder };
        if (shot.Kind != DialogueShotKind.TwoShot)
            yield return DialogueShot.TwoShot;
        if (shot.Kind != DialogueShotKind.Wide)
            yield return DialogueShot.Wide;
    }

    /// <summary>The first shot down <paramref name="wanted"/>'s ladder that fits, or the original's framing (a null shot).</summary>
    public static (DialogueShot Shown, Shot? At) Fit(DialogueScene scene, int side, DialogueShot wanted, DialogueTuning t)
    {
        foreach (var shot in Ladder(wanted))
        {
            if (TryPlace(scene, side, shot, t, out var at))
                return (shot, at);
        }
        return (new DialogueShot(DialogueShotKind.Original), null);
    }

    /// <summary>Whether <paramref name="shot"/> fits on <paramref name="side"/> in one of its placings, and where.</summary>
    public static bool TryPlace(DialogueScene scene, int side, DialogueShot shot, DialogueTuning t, out Shot at) =>
        TryPlace(scene, side, shot, t, out at, out _);

    /// <summary>As <see cref="TryPlace(DialogueScene, int, DialogueShot, DialogueTuning, out Shot)"/>, and which of its placings, counted from 0.</summary>
    public static bool TryPlace(DialogueScene scene, int side, DialogueShot shot, DialogueTuning t, out Shot at, out int placing)
    {
        placing = 0;
        foreach (var (candidate, shows, foreground) in Placings(scene, side, shot, t))
        {
            if (Fits(scene, side, candidate, shows, foreground, t)
                && (shot.Kind is not (DialogueShotKind.Single or DialogueShotKind.Punch) || ListenerCover(scene, candidate, Other(shot.Subject), t) <= t.SingleSliver))
            {
                at = candidate;
                return true;
            }
            placing++;
        }
        at = default;
        return false;
    }

    /// <summary>
    /// The share of the frame's width a talker's body covers in a shot: its head and chest (the two-shot's spheres) projected across the screen (the
    /// whole lens, not its margins), their union clipped to the frame. 0 when it's out of the frame or behind the eye.
    /// </summary>
    public static float ListenerCover(DialogueScene scene, Shot shot, Talker who, DialogueTuning t)
    {
        var body = scene.Of(who);
        var lens = new Lens(t.FieldOfView, 4f / 3, 1);
        var spans = new List<(float From, float To)>();
        foreach (var (at, r) in new[] { (body.Eyes, t.HeadRadius), (body.Eyes - Vector3.UnitY * t.ChestDrop, t.ChestRadius) })
        {
            var v = at - shot.Eye;
            var depth = Vector3.Dot(v, shot.Forward);
            if (depth <= 0.05f)
                continue;
            var y = Vector3.Dot(v, shot.Up) / (depth * lens.TanV);
            if (MathF.Abs(y) - r / (depth * lens.TanV) > 1)
                continue;
            var x = Vector3.Dot(v, shot.Right) / (depth * lens.TanH);
            var w = r / (depth * lens.TanH);
            var (from, to) = (MathF.Max(x - w, -1), MathF.Min(x + w, 1));
            if (to > from)
                spans.Add((from, to));
        }
        if (spans.Count == 0)
            return 0;
        spans.Sort();
        float covered = 0, start = spans[0].From, end = spans[0].To;
        foreach (var (from, to) in spans.Skip(1))
        {
            if (from > end)
            {
                covered += end - start;
                (start, end) = (from, to);
            }
            else
                end = MathF.Max(end, to);
        }
        return (covered + end - start) / 2;
    }

    /// <summary>Where a shot may stand, best first: the talkers it must show, and the one whose shoulder is in front, if any.</summary>
    public static IEnumerable<(Shot Shot, Talker[] Shows, Talker Foreground)> Placings(DialogueScene scene, int side, DialogueShot shot,
        DialogueTuning t)
    {
        var n = Across(scene) * side;
        var up = Vector3.UnitY;
        Talker[] both = [Talker.Player, Talker.Npc];
        switch (shot.Kind)
        {
            case DialogueShotKind.TwoShot or DialogueShotKind.Wide:
            {
                var wide = shot.Kind == DialogueShotKind.Wide;
                var subjects = new List<ShotSubject>();
                foreach (var b in new[] { scene.Player, scene.Npc })
                {
                    subjects.Add(new ShotSubject(b.Eyes, t.HeadRadius));
                    subjects.Add(new ShotSubject(b.Eyes - up * t.ChestDrop, t.ChestRadius));
                    if (wide)
                        subjects.Add(new ShotSubject(b.Centre, b.Radius));
                }
                var aim = ShotFraming.Middle(subjects) - up * t.AimDrop;
                var pitch = Degrees.ToRadians(wide ? t.WidePitch : t.TwoShotPitch);
                foreach (var turn in t.AcrossYaws)
                {
                    var (s, c) = MathF.SinCos(Degrees.ToRadians(turn));
                    var back = new Vector3(n.X * c + n.Z * s, 0, -n.X * s + n.Z * c);
                    var yaw = MathF.Atan2(back.X, back.Z);
                    var hold = ShotFraming.DistanceToHold(subjects, aim, yaw, pitch, t.Lens);
                    var distance = wide ? MathF.Max(hold * t.WideScale, t.WideNearest) : MathF.Max(hold, t.TwoShotNearest);
                    yield return (new Shot(aim, yaw, pitch, distance), both, Talker.None);
                }
                break;
            }
            case DialogueShotKind.OverShoulder:
            {
                var (x, l) = (scene.Of(shot.Subject), scene.Of(Other(shot.Subject)));
                var toL = Way(x.Eyes, l.Eyes, n);
                var aim = x.Eyes - up * t.AimDrop;
                // The roomy placing, then the tight one, then nearer still, as rooms have space for.
                foreach (var (back, across, rise) in new[] { (t.ShoulderBack, t.ShoulderSide, t.ShoulderRise),
                    (t.TightShoulderBack, t.TightShoulderSide, t.TightShoulderRise),
                    (t.TightShoulderBack * 0.75f, t.TightShoulderSide * 1.2f, t.TightShoulderRise),
                    (t.TightShoulderBack * 0.5f, t.TightShoulderSide * 1.4f, t.TightShoulderRise) })
                {
                    var eye = l.Eyes + toL * back + n * across + up * rise;
                    yield return (Shot.Of(eye, aim), [shot.Subject], Other(shot.Subject));
                }
                break;
            }
            case DialogueShotKind.Single or DialogueShotKind.Punch:
            {
                var (x, l) = (scene.Of(shot.Subject), scene.Of(Other(shot.Subject)));
                var toL = Way(x.Eyes, l.Eyes, n);
                var aim = x.Eyes - up * t.AimDrop;
                var near = shot.Kind == DialogueShotKind.Punch ? t.PunchDistance : t.SingleDistance;
                // Round toward across the line, so the listener leaves the frame; the view turned away from the listener by up to a
                // share of the half-width, which sets the subject off-centre on the listener's side and the listener past the edge.
                foreach (var angle in t.SingleAngles)
                foreach (var scale in new[] { 1f, 0.85f })
                foreach (var lead in t.SingleLeads)
                {
                    var (s, c) = MathF.SinCos(Degrees.ToRadians(angle));
                    var way = Vector3.Normalize(toL * c + n * s);
                    var eye = x.Eyes + way * near * scale + up * t.SingleRise;
                    var straight = Shot.Of(eye, aim);
                    // Turn the aim away from the listener's side of the frame.
                    var listenerSide = MathF.Sign(Vector3.Dot(l.Eyes - eye, straight.Right));
                    var turned = aim - straight.Right * listenerSide * lead * Vector3.Distance(eye, aim) * MathF.Tan(t.FieldOfView * MathF.PI / 360) * 4f / 3;
                    yield return (Shot.Of(eye, turned), [shot.Subject], Talker.None);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Whether a shot fits: on its side of the line; its eye reached from the focus without crossing the collision, from either side,
    /// or the drawn world; out of every body; each talker it shows within the lens, in sight of the collision and the drawn world, and
    /// no body in the way (the foreground shoulder taken at half its size).
    /// </summary>
    public static bool Fits(DialogueScene scene, int side, Shot shot, IReadOnlyList<Talker> shows, Talker foreground, DialogueTuning t)
    {
        var eye = shot.Eye;
        var focus = scene.Focus;
        if (Vector3.Dot(Flat(eye - focus), Across(scene) * side) <= 0.05f)
            return false;
        if (SpherePhysics.Blocked(scene.World, focus, eye, t.EyeRadius, skipSeeThrough: true)
            || SpherePhysics.Blocked(scene.World, eye, focus, t.EyeRadius, skipSeeThrough: true)
            || scene.Visible?.Raycast(focus, eye, out _, out _, out _) == true)
            return false;
        foreach (var b in scene.Others)
        {
            if (Vector3.Distance(eye, b.At) < b.Radius + 0.3f)
                return false;
        }
        foreach (var b in new[] { scene.Player, scene.Npc })
        {
            if (Vector3.Distance(eye, b.Centre) < b.Radius + 0.15f)
                return false;
        }
        var lens = t.Lens;
        var sight = new SightLines(null);
        sight.Bodies.AddRange(scene.Others);
        foreach (var who in shows)
        {
            var body = scene.Of(who);
            var other = scene.Of(Other(who));
            if (!ShotFraming.Holds(shot, new ShotSubject(body.Eyes, t.HeadRadius * 0.8f), lens))
                return false;
            if (SpherePhysics.Blocked(scene.World, eye, body.Eyes, 0.05f, skipSeeThrough: true)
                || SpherePhysics.Blocked(scene.World, body.Eyes, eye, 0.05f, skipSeeThrough: true)
                || scene.Visible?.Raycast(eye, body.Eyes, out _, out _, out _) == true)
                return false;
            var head = new ShotSubject(body.Eyes, 0.2f);
            if (sight.HiddenByBody(eye, head))
                return false;
            // The other talker hides this one's face only where it stands squarely across it: the shoulder in front of an over the
            // shoulder shot is taken at half its size.
            var scale = Other(who) == foreground ? 0.5f : 1f;
            var line = body.Eyes - eye;
            var along = Vector3.Dot(other.Centre + Vector3.UnitY * other.Radius - eye, line) / line.LengthSquared();
            var top = other.Centre + Vector3.UnitY * other.Radius;
            if (along is > 0 and < 1 && Vector3.Distance(eye + line * along, top) < other.Radius * scale)
                return false;
        }
        return true;
    }

    private static Talker Other(Talker t) => t == Talker.Player ? Talker.Npc : Talker.Player;

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    /// <summary>The way across the ground from <paramref name="from"/> to <paramref name="to"/>, or along the line's perpendicular if they're on top of each other.</summary>
    private static Vector3 Way(Vector3 from, Vector3 to, Vector3 fallback)
    {
        var w = Flat(to - from);
        return w.LengthSquared() > 1e-6f ? Vector3.Normalize(w) : new Vector3(fallback.Z, 0, -fallback.X);
    }
}
