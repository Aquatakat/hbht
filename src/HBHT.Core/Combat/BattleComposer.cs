using System.Numerics;
using HBHT.Core.Ui;

namespace HBHT.Core.Combat;

/// <summary>
/// Schedules and composes a battle's actions as they're drawn (ADR 0040's stage 3), over its recording cut into actions
/// (<see cref="BattleActions"/>): when each action starts drawing, which tick each body, number, missile and spell's actor is drawn from on
/// each drawn tick, and what plays once as each timeline reaches it. <see cref="BattleDrawing"/> drives it, a drawn tick at a time
/// (<see cref="Next"/>) under Start Early, and keeps it at the battle's present (<see cref="Settle"/>) under Original. It needs nothing of
/// the battle but its frames and whether it waits on the player, so tests drive it with frames of their own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scheduling.</b> Actions start in the battle's order, never before the action before, each as soon as its ticks are settled (its turn
/// over, and <see cref="BattleActions.TailCap"/> ticks after it recorded, or the battle waiting on the player): on the earliest drawn tick
/// where every body it involves has finished its earlier episodes before its own episode in it begins, and nothing it draws crosses
/// another's path (<see cref="Search"/>). An action not yet settled starts only once nothing else is drawing, everything put at its first
/// tick first.
/// </para>
/// <para>
/// <b>Composing.</b> An action's timeline runs from its start a tick a drawn tick, on past its last tick while what it began lives. A body's
/// runs with the action it's in an episode of; between episodes it idles on its own, a tick a drawn tick, never past its next episode's
/// first tick or the newest action's tick, so it never runs backwards, and meets its next episode with a jump its view blends
/// (<see cref="BattleFrame.Jumped"/>). Once the newest action reaches the battle's present, every timeline is put there.
/// </para>
/// </remarks>
public sealed class BattleComposer
{
    /// <summary>How many ticks a body's pose blends over when it's drawn from a tick other than the one after its last.</summary>
    public const int BlendTicks = 8;

    /// <summary>How many ticks the drawn camera takes to settle on a newer action's.</summary>
    public const int CameraTicks = 20;

    /// <summary>How much nearer than the recording had them two shapes may be drawn, and how close they may come, for paths to cross.</summary>
    public const float Slack = 0.02f, Margin = 0.05f;

    /// <summary>The reach of a missile in flight, and of a spell's actor between its two bodies, for the path check.</summary>
    public const float MissileRadius = 0.3f, VisualRadius = 0.5f;

    private readonly BattleActions _actions;
    private readonly Dictionary<Combatant, Track> _tracks = [];
    private readonly Func<bool> _waits;
    private int _drawn;
    private int _unscheduled = 1;
    private int _live;
    private string _log;
    private DrawnAction? _cameraFrom;
    private int _cameraLeft;
    private int _leadTick;

    /// <param name="first">The frame the battle's beginning drew, at once, its one-offs played.</param>
    /// <param name="waits">Whether the battle waits on the player, so no more ticks come till the drawing catches up.</param>
    public BattleComposer(BattleFrame first, Func<bool> waits)
    {
        _actions = new BattleActions(first);
        _actions.Actions[0].Start = 0;
        _log = first.Gui.Log;
        _waits = waits;
        Frame = first;
        foreach (var body in _actions.Bodies)
            _tracks[body] = new Track(first.Tick);
    }

    /// <summary>The recording cut into actions, with where the drawing has each.</summary>
    public BattleActions Actions => _actions;

    /// <summary>The drawn ticks so far.</summary>
    public int Drawn => _drawn;

    /// <summary>The frame drawn last.</summary>
    public BattleFrame Frame { get; private set; }

    /// <summary>Takes the frame the battle just recorded.</summary>
    public void Record(BattleFrame frame)
    {
        _actions.Add(frame);
        foreach (var body in _actions.Bodies)
        {
            if (!_tracks.ContainsKey(body))
                _tracks[body] = new Track(frame.Tick - 1);
        }
    }

    /// <summary>How many ticks the battle is ahead of the slowest part of the drawing.</summary>
    public int Ahead => CaughtUp ? 0 : _actions.Last - Slowest();

    /// <summary>Whether the drawing has caught up with the battle: every action and every body drawn to its present.</summary>
    public bool CaughtUp
    {
        get
        {
            if (_unscheduled < _actions.Actions.Count)
                return false;
            var last = _actions.Last;
            for (var i = _live; i < _actions.Actions.Count; i++)
            {
                var a = _actions.Actions[i];
                if (a.Start > _drawn || a.TickAt(_drawn) < Math.Min(a.TailEnd ?? last, last))
                    return false;
            }
            foreach (var (_, track) in _tracks)
            {
                if (track.Cursor < last && !track.Gone)
                    return false;
            }
            return true;
        }
    }

    /// <summary>Whether an action drawn now is an AI's walk (state 5), which A only fast-forwards under Start Early.</summary>
    public bool Walking
    {
        get
        {
            if (Frame.Walking)
                return true;
            foreach (var a in DrawingNow())
            {
                if (a.TickAt(_drawn) <= a.Last && _actions[Own(a)].Walking)
                    return true;
            }
            return false;
        }
    }

    /// <summary>The actions drawing on the drawn tick now: started, and not yet played to their tail's end.</summary>
    public IEnumerable<DrawnAction> DrawingNow()
    {
        for (var i = _live; i < _actions.Actions.Count; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start <= _drawn && a.TickAt(_drawn) <= (a.TailEnd ?? _actions.Last))
                yield return a;
        }
    }

    /// <summary>
    /// Whether a body is still drawn behind the battle's present: idling behind it, or in an episode of an action the drawing hasn't
    /// finished. What a turn the player plays can fast-forward (ADR 0040's stage 4).
    /// </summary>
    public bool Behind(Combatant body) => _tracks.TryGetValue(body, out var t) && !t.Gone && t.Cursor < _actions.Last;

    /// <summary>The tick of the recording a body is drawn from now, or null for one not drawn.</summary>
    public int? DrawnTickOf(Combatant body) => _tracks.TryGetValue(body, out var t) && !t.Gone ? t.Cursor : null;

    /// <summary>The next drawn tick: what can start is scheduled, every timeline moves to it, and the frame composed of them is returned.</summary>
    public BattleFrame Next()
    {
        _drawn++;
        return Frame = Compose();
    }

    /// <summary>
    /// Every action at the battle's present at once: the frame composed there, with the one-offs passed on the way, but no emitter
    /// starts (<paramref name="emitters"/> false).
    /// </summary>
    public BattleFrame FastForward(bool emitters = false)
    {
        var played = new List<Moment>();
        _search = null;
        for (var i = _unscheduled; i < _actions.Actions.Count; i++)
            _actions.Actions[i].Start = _drawn;
        _unscheduled = _actions.Actions.Count;
        Snap(_actions.Last, played);
        return Frame = Composed(played, emitters);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Under Original: everything at the present

    /// <summary>A drawn tick at the battle's present, <paramref name="drawn"/>, drawn as it was recorded: every timeline is put there, its one-offs already played.</summary>
    public void Settle(BattleFrame drawn)
    {
        _drawn++;
        Frame = drawn;
        _log = drawn.Gui.Log;
        _search = null;
        var last = _actions.Last;
        for (var i = _unscheduled; i < _actions.Actions.Count; i++)
            _actions.Actions[i].Start = _drawn - (last - _actions.Actions[i].First);
        _unscheduled = _actions.Actions.Count;
        for (var i = _live; i < _actions.Actions.Count; i++)
        {
            var a = _actions.Actions[i];
            a.Start = _drawn - (last - a.First);
            a.Played = last;
            a.MomentsPlayed = a.Moments.Count;
        }
        foreach (var (body, track) in _tracks)
        {
            track.Cursor = last;
            track.MomentsPlayed = _actions.MomentsOf(body).Count;
            track.Episode = _actions.EpisodesOf(body).Count;
        }
        Retire();
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Under Start Early: scheduling

    /// <summary>
    /// Gives each action whose ticks are settled the drawn tick it starts on, in the battle's order (<see cref="Search"/>). An action not
    /// yet settled (its turn still playing, or its tail still being recorded) starts only when nothing else is drawing, and everything is
    /// put at its first tick first.
    /// </summary>
    private void Schedule(List<Moment> played)
    {
        var budget = System.Diagnostics.Stopwatch.StartNew();
        while (_unscheduled < _actions.Actions.Count)
        {
            var b = _actions.Actions[_unscheduled];
            if (Settled(b))
            {
                // The search takes at most a share of a tick; one not done goes on next tick, from the starts not yet passed.
                if (Search(b, budget) is not { } start)
                    return;
                b.Start = start;
            }
            else if (!Busy(b))
            {
                Snap(b.First - 1, played);
                b.Start = _drawn;
            }
            else
            {
                return;
            }
            _unscheduled++;
        }
    }

    /// <summary>How long a drawn tick may spend searching for actions' starts.</summary>
    public static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(2);

    /// <summary>Whether all of an action's ticks are known, and all its episodes' that could carry on past it.</summary>
    private bool Settled(DrawnAction b) =>
        b.Closed && (_actions.Last >= b.Last + BattleActions.TailCap || _waits());

    /// <summary>
    /// Whether any action is due to start, or has ticks before <paramref name="b"/>'s left to draw: its own, or its tail's (what it began
    /// that lives into <paramref name="b"/>'s ticks is drawn alongside it).
    /// </summary>
    private bool Busy(DrawnAction b)
    {
        for (var i = _live; i < _unscheduled; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start > _drawn || a.TickAt(_drawn) < Math.Min(a.TailEnd ?? int.MaxValue, b.First - 1))
                return true;
        }
        return false;
    }

    /// <summary>A search for an action's start, which may go on over several drawn ticks.</summary>
    private sealed class Searching(DrawnAction action, int start, int window, List<Placed>[] mine, List<Pair> pairs)
    {
        public DrawnAction Action { get; } = action;
        public int Start { get; } = start;
        public int Window { get; } = window;
        public List<Placed>[] Mine { get; } = mine;
        public List<Pair> Pairs { get; } = pairs;

        /// <summary>The next delay from <see cref="Start"/> to try.</summary>
        public int Next { get; set; }
    }

    private readonly record struct Pair(Placed?[] X, Placed?[] Y, (Vector3 Min, Vector3 Max) Box, object Key);

    private Searching? _search;

    /// <summary>
    /// The drawn tick an action starts on, or null if the search must go on next tick: the earliest, no earlier than the last action's start
    /// or now, where every body it involves has finished its episodes in earlier actions before its own begins, and nothing it draws (a
    /// body in or resting after one of its episodes, its missiles, its spells' actors) comes within reach of a shape another action, or a
    /// body idling, draws then, nearer than both shapes' ticks recorded them. A search past <see cref="MaxSearch"/> ticks gives up, and
    /// the action waits for every earlier one.
    /// </summary>
    private int? Search(DrawnAction b, System.Diagnostics.Stopwatch budget)
    {
        if (_search?.Action != b)
        {
            _search = null;
            var start = _drawn;
            for (var i = _live; i < b.Index; i++)
                start = Math.Max(start, _actions.Actions[i].Start!.Value);
            // Each body's earlier episodes end, as drawn, before its episode in this one begins.
            foreach (var e in b.Episodes)
            {
                foreach (var earlier in _actions.EpisodesOf(e.Body))
                {
                    if (earlier == e)
                        break;
                    if (earlier.Action == b || earlier.Action.Start is not { } s)
                        continue;
                    var end = s + (earlier.To - earlier.Action.First);
                    start = Math.Max(start, end + 1 - (e.From - b.First));
                }
            }
            var window = Window(b);
            if (start > window)
                return start;
            // What the others draw on each drawn tick of the window, as if this one weren't there, and what this one draws by its own ticks.
            var n = window - start + 1;
            var theirs = new List<Placed>[n];
            var mine = new List<Placed>[n];
            for (var i = 0; i < n; i++)
            {
                theirs[i] = Others(b, start + i);
                mine[i] = Mine(b, i);
            }
            _search = new Searching(b, start, window, mine, Pairs(theirs, mine));
        }
        var search = _search;
        var delays = Math.Min(search.Window - search.Start + 1, MaxSearch + 1);
        _shapes.Clear();
        for (var delay = Math.Max(search.Next, _drawn - search.Start); delay < delays; delay++)
        {
            if (Clear(search, delay))
            {
                _search = null;
                return search.Start + delay;
            }
            if (budget.Elapsed > SearchBudget)
            {
                search.Next = delay + 1;
                return null;
            }
        }
        _search = null;
        return Math.Max(search.Window + 1, _drawn);
    }

    /// <summary>How many drawn ticks past its earliest an action's start is searched for, before it waits for every earlier one.</summary>
    public const int MaxSearch = 1200;

    /// <summary>
    /// The pairs of shapes, one this action draws (<paramref name="mine"/>, by its ticks) and one another does (<paramref name="theirs"/>,
    /// by drawn ticks), that could cross: their paths come near at all, and they aren't both standing where both recordings have them.
    /// </summary>
    private List<Pair> Pairs(List<Placed>[] theirs, List<Placed>[] mine)
    {
        var pairs = new List<Pair>();
        var theirKeys = Paths(theirs);
        foreach (var (x, xs) in Paths(mine))
        {
            foreach (var (y, ys) in theirKeys)
            {
                if (x != y && Near(xs.Box, ys.Box) && !Still(x, xs, y, ys))
                    pairs.Add(new Pair(xs.At, ys.At, ys.Box, y));
            }
        }
        return pairs;
    }

    /// <summary>
    /// Whether the action starting <paramref name="delay"/> ticks after the search's first draws nothing through another's path: no pair's
    /// shape it draws k ticks in crosses the other's drawn then.
    /// </summary>
    private bool Clear(Searching search, int delay)
    {
        foreach (var (xs, ys, box, y) in search.Pairs)
        {
            for (var k = 0; k < xs.Length && k + delay < ys.Length; k++)
            {
                if (xs[k] is not { } px || ys[k + delay] is not { } py || !Near(Box(px), box) || Holds(search.Mine[k], y))
                    continue;
                if (Crosses(px, py))
                    return false;
            }
        }
        return true;

        static bool Holds(List<Placed> my, object key)
        {
            foreach (var x in my)
            {
                if (x.Key == key)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Whether two shapes that never move stand where both recordings have them: each where the other's ticks have it, so no tick of
    /// the one can be drawn nearer the other than it was recorded.
    /// </summary>
    private bool Still(object x, (Placed?[] At, (Vector3 Min, Vector3 Max) Box) xs, object y, (Placed?[] At, (Vector3 Min, Vector3 Max) Box) ys)
    {
        if (Place(xs) is not { } px || Place(ys) is not { } py)
            return false;
        foreach (var p in xs.At)
        {
            if (p is { } at && (ShapeOf(y, at.Tick) is not { } then || !Same(then, py)))
                return false;
        }
        foreach (var p in ys.At)
        {
            if (p is { } at && (ShapeOf(x, at.Tick) is not { } then || !Same(then, px)))
                return false;
        }
        return true;

        // Its one shape, if it never moves.
        static Shape? Place((Placed?[] At, (Vector3 Min, Vector3 Max) Box) path)
        {
            Shape? one = null;
            foreach (var p in path.At)
            {
                if (p is not { } at)
                    continue;
                if (one is { } o && !Same(o, at.Shape))
                    return null;
                one = at.Shape;
            }
            return one;
        }
    }

    /// <summary>
    /// Whether two shapes stand within <see cref="Still"/>'s tolerance of each other: under half the slack each, so two shapes that stay
    /// within it can't be drawn nearer than recorded by the slack.
    /// </summary>
    private static bool Same(Shape a, Shape b) =>
        Vector3.DistanceSquared(a.A, b.A) < StillTolerance * StillTolerance && Vector3.DistanceSquared(a.B, b.B) < StillTolerance * StillTolerance;

    private const float StillTolerance = Slack / 2 - 0.001f;

    private static (Vector3 Min, Vector3 Max) Box(Placed p)
    {
        var grow = new Vector3(p.Shape.Radius + Margin);
        return (Vector3.Min(p.Shape.A, p.Shape.B) - grow, Vector3.Max(p.Shape.A, p.Shape.B) + grow);
    }

    /// <summary>Each key's shape at each index, and the box all of them fit in, grown by their reach.</summary>
    private static Dictionary<object, (Placed?[] At, (Vector3 Min, Vector3 Max) Box)> Paths(List<Placed>[] lists)
    {
        var paths = new Dictionary<object, (Placed?[] At, (Vector3 Min, Vector3 Max) Box)>();
        for (var i = 0; i < lists.Length; i++)
        {
            foreach (var p in lists[i])
            {
                if (!paths.TryGetValue(p.Key, out var path))
                    path = (new Placed?[lists.Length], (new Vector3(float.MaxValue), new Vector3(float.MinValue)));
                path.At[i] = p;
                var grow = new Vector3(p.Shape.Radius + Margin);
                var min = Vector3.Min(path.Box.Min, Vector3.Min(p.Shape.A, p.Shape.B) - grow);
                var max = Vector3.Max(path.Box.Max, Vector3.Max(p.Shape.A, p.Shape.B) + grow);
                paths[p.Key] = (path.At, (min, max));
            }
        }
        return paths;
    }

    private static bool Near((Vector3 Min, Vector3 Max) a, (Vector3 Min, Vector3 Max) b) =>
        a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y && a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;

    /// <summary>The last drawn tick an earlier action draws on.</summary>
    private int Window(DrawnAction b)
    {
        var end = int.MinValue;
        for (var i = _live; i < b.Index; i++)
        {
            var a = _actions.Actions[i];
            end = Math.Max(end, a.DrawnAt(a.TailEnd ?? _actions.Last));
        }
        return end;
    }

    /// <summary>Every shape drawn on drawn tick <paramref name="d"/> by the actions scheduled before <paramref name="b"/>, and the bodies idling.</summary>
    private List<Placed> Others(DrawnAction b, int d)
    {
        var list = new List<Placed>();
        foreach (var body in _actions.Bodies)
        {
            if (Place(body, d, null, 0) is { } placed)
                list.Add(placed);
        }
        for (var i = _live; i < b.Index; i++)
        {
            var a = _actions.Actions[i];
            var t = a.First + (d - a.Start!.Value);
            if (t < a.First || t > _actions.Last || t < _actions.Kept)
                continue;
            AddThings(a, t, list);
        }
        return list;
    }

    /// <summary>
    /// What <paramref name="b"/> draws <paramref name="k"/> ticks into it: each body in one of its episodes then, or resting after one, and
    /// its missiles and spells' actors.
    /// </summary>
    private List<Placed> Mine(DrawnAction b, int k)
    {
        var list = new List<Placed>();
        var t = b.First + k;
        if (t > _actions.Last || t < _actions.Kept)
            return list;
        foreach (var e in b.Episodes)
        {
            var at = t >= e.From && t <= e.To ? t : t > e.To && LastBefore(b, e.Body, t) == e ? Math.Min(e.To + 1, _actions.Last) : -1;
            if (at >= 0 && Centre(e.Body, at) is { } centre)
                list.Add(new Placed(e.Body, new Shape(centre, centre, e.Body.Body.Sphere.Radius), at, true));
        }
        AddThings(b, t, list);
        return list;

        static Episode? LastBefore(DrawnAction b, Combatant body, int t)
        {
            Episode? last = null;
            foreach (var e in b.Episodes)
            {
                if (e.Body != body)
                    continue;
                if (e.From <= t && t <= e.To)
                    return null;
                if (e.To < t && (last is null || e.To > last.To))
                    last = e;
            }
            return last;
        }
    }

    /// <summary>An action's missiles and spells' actors living on tick <paramref name="t"/>, its timeline's.</summary>
    private void AddThings(DrawnAction a, int t, List<Placed> list)
    {
        foreach (var (thing, ended) in a.Things)
        {
            if (thing is not (MissileFlight or SpellFxVisual) || ended < t)
                continue;
            if (ShapeOf(thing, t) is { } shape)
                list.Add(new Placed(thing, shape, t, false));
        }
    }

    /// <summary>
    /// Where a body is drawn on drawn tick <paramref name="d"/> with <paramref name="b"/> starting on <paramref name="start"/>: in an
    /// episode of a scheduled action then, at that action's tick; else resting where its last episode drawn by then left it, or where
    /// it's drawn now. Null if it isn't on the field.
    /// </summary>
    private Placed? Place(Combatant body, int d, DrawnAction? b, int start)
    {
        var track = _tracks.GetValueOrDefault(body);
        int? rest = null;
        var restMine = false;
        var restEnd = int.MinValue;
        var episodes = _actions.EpisodesOf(body);
        for (var k = Math.Max(0, (track?.Episode ?? 0) - 1); k < episodes.Count; k++)
        {
            var e = episodes[k];
            var a = e.Action;
            int s;
            if (a == b)
                s = start;
            else if (a.Start is { } scheduled)
                s = scheduled;
            else
                continue;
            var from = s + (e.From - a.First);
            var to = s + (e.To - a.First);
            if (d >= from && d <= to)
                return At(body, e.From + (d - from), a == b);
            if (to < d && to > restEnd)
                (rest, restMine, restEnd) = (Math.Min(e.To + 1, _actions.Last), a == b, to);
        }
        var tick = rest ?? track?.Cursor ?? _actions.Last;
        return At(body, Math.Max(tick, _actions.Kept), restMine);

        Placed? At(Combatant c, int t, bool mine) =>
            t > _actions.Last || Centre(c, t) is not { } at
                ? null
                : new Placed(c, new Shape(at, at, c.Body.Sphere.Radius), t, mine);
    }

    /// <summary>Whether two shapes drawn from different ticks come within reach, nearer than either's tick recorded them.</summary>
    private bool Crosses(Placed x, Placed y)
    {
        if (x.Key == y.Key)
            return false;
        var reach = x.Shape.Radius + y.Shape.Radius + Margin;
        var drawn = Distance(x.Shape, y.Shape);
        if (drawn >= reach)
            return false;
        var mine = ShapeOf(y.Key, x.Tick) is { } yThen ? Distance(x.Shape, yThen) : float.PositiveInfinity;
        var theirs = ShapeOf(x.Key, y.Tick) is { } xThen ? Distance(xThen, y.Shape) : float.PositiveInfinity;
        return drawn < Math.Min(mine, theirs) - Slack;
    }

    /// <summary>The shapes looked up in a search, by key and tick.</summary>
    private readonly Dictionary<(object, int), Shape?> _shapes = [];

    /// <summary>A body's, missile's or spell's actor's shape on a tick, or null if it isn't there.</summary>
    private Shape? ShapeOf(object key, int t)
    {
        if (_shapes.TryGetValue((key, t), out var known))
            return known;
        return _shapes[(key, t)] = Look(key, t);
    }

    private Shape? Look(object key, int t)
    {
        if (t < _actions.Kept || t > _actions.Last)
            return null;
        switch (key)
        {
            case Combatant c:
                return Centre(c, t) is { } at ? new Shape(at, at, c.Body.Sphere.Radius) : null;
            case MissileFlight m:
                return _actions[t].Missile is { } flying && flying.Flight == m ? new Shape(flying.Drawn, flying.Drawn, MissileRadius) : null;
            case SpellFxVisual v:
                foreach (var drawn in _actions[t].Visuals)
                {
                    if (drawn.Visual != v || drawn.Played)
                        continue;
                    var other = v.Other is { } o && o != v.Owner ? Centre(o, t) : null;
                    return new Shape(drawn.Position, other ?? drawn.Position, VisualRadius);
                }
                return null;
        }
        return null;
    }

    private Vector3? Centre(Combatant c, int t)
    {
        foreach (var b in _actions[t].Bodies)
        {
            if (b.Combatant == c)
                return b.Centre;
        }
        return null;
    }

    private readonly record struct Shape(Vector3 A, Vector3 B, float Radius);

    private readonly record struct Placed(object Key, Shape Shape, int Tick, bool Mine);

    /// <summary>The distance between two capsules' axes.</summary>
    private static float Distance(Shape p, Shape q)
    {
        if (p.A == p.B && q.A == q.B)
            return Vector3.Distance(p.A, q.A);
        var (a, b) = ClosestPoints(p.A, p.B, q.A, q.B);
        return Vector3.Distance(a, b);
    }

    private static (Vector3, Vector3) ClosestPoints(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = Vector3.Dot(d1, d1);
        var e = Vector3.Dot(d2, d2);
        var f = Vector3.Dot(d2, r);
        float s, t;
        if (a <= 1e-8f && e <= 1e-8f)
            return (p1, p2);
        if (a <= 1e-8f)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            var c = Vector3.Dot(d1, r);
            if (e <= 1e-8f)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                var bb = Vector3.Dot(d1, d2);
                var denom = a * e - bb * bb;
                s = denom != 0 ? Math.Clamp((bb * f - c * e) / denom, 0, 1) : 0;
                t = (bb * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((bb - c) / a, 0, 1);
                }
            }
        }
        return (p1 + d1 * s, p2 + d2 * t);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Under Start Early: composing a drawn tick

    /// <summary>
    /// Draws the drawn tick now: schedules what can start, moves every timeline to it (an action's by its start; a body's with the action
    /// it's in an episode of, or by a tick at a time between them, no further than its next episode's first tick or the newest action's),
    /// plays what they reach once, in the recording's order, and draws the frame composed of them. Once the newest action has reached the
    /// battle's present, everything is put there.
    /// </summary>
    private BattleFrame Compose()
    {
        var played = new List<Moment>();
        Schedule(played);
        var last = _actions.Last;
        var newest = Newest();
        var newestTick = newest is null ? last : Math.Min(newest.TickAt(_drawn), last);

        for (var i = _live; i < _actions.Actions.Count; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start is not { } s || s > _drawn)
                continue;
            Advance(a, Math.Min(a.TickAt(_drawn), last), played);
        }
        foreach (var (body, track) in _tracks)
            Move(body, track, newestTick, played);

        if (newest is not null && newest.TickAt(_drawn) >= last && _unscheduled == _actions.Actions.Count)
            Snap(last, played);
        var frame = Composed(played);
        Retire();
        return frame;
    }

    /// <summary>The newest action drawing its own ticks now, or the newest started.</summary>
    private DrawnAction? Newest()
    {
        DrawnAction? started = null;
        for (var i = _unscheduled - 1; i >= _live; i--)
        {
            var a = _actions.Actions[i];
            if (a.Start is not { } s || s > _drawn)
                continue;
            if (a.TickAt(_drawn) <= a.Last || !a.Closed)
                return a;
            started ??= a;
        }
        return started ?? _actions.Actions[Math.Max(0, _unscheduled - 1)];
    }

    /// <summary>Plays an action's timeline to <paramref name="tick"/>.</summary>
    private static void Advance(DrawnAction a, int tick, List<Moment> played)
    {
        if (tick <= a.Played)
            return;
        a.Played = tick;
        while (a.MomentsPlayed < a.Moments.Count && a.Moments[a.MomentsPlayed].Tick <= tick)
            played.Add(a.Moments[a.MomentsPlayed++]);
    }

    /// <summary>Moves a body's timeline: with the action it's in an episode of, else a tick on, no further than its next episode or the newest action.</summary>
    private void Move(Combatant body, Track track, int newestTick, List<Moment> played)
    {
        var episodes = _actions.EpisodesOf(body);
        var cap = Math.Min(newestTick, _actions.Last);
        var to = -1;
        while (track.Episode < episodes.Count)
        {
            var e = episodes[track.Episode];
            if (e.Action.Start is { } s && s <= _drawn)
            {
                var at = e.Action.TickAt(_drawn);
                if (at > e.To)
                {
                    // Its episode has played: it rests where that left it.
                    to = Math.Max(to, Math.Min(e.To, _actions.Last));
                    track.Episode++;
                    continue;
                }
                if (at >= e.From)
                {
                    to = Math.Min(at, _actions.Last);
                    break;
                }
            }
            cap = Math.Min(cap, e.From - 1);
            break;
        }
        if (to < 0 || track.Episode >= episodes.Count || to < episodes[track.Episode].From)
            to = Math.Max(to, Math.Min(track.Cursor + 1, cap));
        Go(body, track, Math.Max(track.Cursor, to), played);
    }

    /// <summary>Moves a body's timeline to <paramref name="tick"/>, playing what it passes, and notes a jump.</summary>
    private void Go(Combatant body, Track track, int tick, List<Moment> played)
    {
        if (tick > track.Cursor + 1)
            track.Jumped = true;
        track.Cursor = tick;
        var items = _actions.MomentsOf(body);
        while (track.MomentsPlayed < items.Count && items[track.MomentsPlayed].Tick <= tick)
        {
            var item = items[track.MomentsPlayed++];
            played.Add(item);
            if (item.Payload is CombatantGone)
                track.Gone = true;
        }
    }

    /// <summary>
    /// Puts every timeline at <paramref name="tick"/> at least: each started action as if it had started that much earlier, each body's
    /// cursor there, what they pass played.
    /// </summary>
    private void Snap(int tick, List<Moment> played)
    {
        for (var i = _live; i < _unscheduled; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start is not { } s)
                continue;
            var at = s <= _drawn ? a.TickAt(_drawn) : a.First - 1;
            if (at < tick)
                a.Start = _drawn - (tick - a.First);
            Advance(a, Math.Min(tick, _actions.Last), played);
        }
        foreach (var (body, track) in _tracks)
        {
            var episodes = _actions.EpisodesOf(body);
            while (track.Episode < episodes.Count && episodes[track.Episode].To <= tick)
                track.Episode++;
            if (tick > track.Cursor)
                Go(body, track, tick, played);
        }
    }

    /// <summary>Lets go of the actions the drawing has finished with, and the frames before every timeline.</summary>
    private void Retire()
    {
        while (_live < _unscheduled - 1)
        {
            var a = _actions.Actions[_live];
            if (a.Start > _drawn || a.TailEnd is not { } end || a.TickAt(_drawn) <= end || a.MomentsPlayed < a.Moments.Count)
                break;
            _live++;
        }
        _actions.Forget(Slowest() - 1);
    }

    /// <summary>The earliest tick any timeline is drawn at.</summary>
    public int Slowest()
    {
        var slowest = _actions.Last;
        for (var i = _live; i < _actions.Actions.Count; i++)
        {
            var a = _actions.Actions[i];
            slowest = Math.Min(slowest, a.Start is { } s && s <= _drawn ? Math.Max(a.First, Math.Min(a.TickAt(_drawn), _actions.Last)) : a.First);
        }
        foreach (var track in _tracks.Values)
        {
            if (!track.Gone)
                slowest = Math.Min(slowest, track.Cursor);
        }
        return Math.Max(slowest, _actions.Kept);
    }

    private int Clamp(DrawnAction a) => Math.Clamp(a.TickAt(_drawn), Math.Max(a.First, _actions.Kept), _actions.Last);

    /// <summary>The tick of an action's own the drawing has now: its timeline's, no further than its last.</summary>
    private int Own(DrawnAction a) => Math.Clamp(a.TickAt(_drawn), Math.Max(a.First, _actions.Kept), a.Closed ? a.Last : _actions.Last);

    /// <summary>The frame composed of the actions and bodies where they are now, with <paramref name="played"/>'s one-offs.</summary>
    private BattleFrame Composed(List<Moment> played, bool emitters = true)
    {
        played.Sort((x, y) => x.Seq.CompareTo(y.Seq));
        var cues = new List<CombatCue>();
        var calls = new List<SoundCall>();
        var starts = new List<EmitterStart>();
        foreach (var item in played)
        {
            switch (item.Payload)
            {
                case CombatCue cue:
                    cues.Add(cue);
                    if (cue is LogLine line)
                        _log = line.Text;
                    break;
                case SoundCall call:
                    calls.Add(call);
                    break;
                case EmitterStart start when emitters:
                    starts.Add(start);
                    break;
            }
        }

        var newest = Newest();
        var lead = _actions[newest is null ? _actions.Last : Own(newest)];
        // A newer action that starts while an older one draws starts its camera from another place: it settles there.
        if (newest is not null && newest != _cameraFrom)
        {
            if (_cameraFrom is not null && lead.Tick != _leadTick + 1 && lead.Tick != _leadTick)
                _cameraLeft = CameraTicks;
            _cameraFrom = newest;
        }
        _leadTick = lead.Tick;

        // Each body from the tick its timeline has.
        var bodies = new List<DrawnBody>();
        var jumped = new HashSet<Combatant>();
        var where = new Dictionary<int, (DrawnBody Body, BattleFrame Frame)>();
        foreach (var (body, track) in _tracks)
        {
            if (track.Gone || track.Cursor < _actions.Kept)
                continue;
            var frame = _actions[Math.Min(track.Cursor, _actions.Last)];
            foreach (var drawn in frame.Bodies)
            {
                if (drawn.Combatant != body)
                    continue;
                bodies.Add(drawn);
                where[body.Slot] = (drawn, frame);
                if (track.Jumped)
                    jumped.Add(body);
                break;
            }
            track.Jumped = false;
        }
        bodies.Sort((x, y) => x.Combatant.Slot.CompareTo(y.Combatant.Slot));

        // Each number, missile and spell's actor from the action that began it; each lasting scene with its body.
        var missiles = new List<DrawnMissile>();
        var visuals = new List<DrawnVisual>();
        var numbers = new List<DrawnNumber>();
        for (var i = _live; i < _unscheduled; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start is not { } s || s > _drawn)
                continue;
            var frame = _actions[Clamp(a)];
            if (frame.Missile is { } m && _actions.BirthOf(m.Flight) == a)
                missiles.Add(m);
            foreach (var v in frame.Visuals)
            {
                if (v.Visual.IsActor && _actions.BirthOf(v.Visual) == a)
                    visuals.Add(v);
            }
            foreach (var n in frame.Numbers)
            {
                if (_actions.BirthOf(n.Number) == a)
                    numbers.Add(n);
            }
        }
        foreach (var (_, (body, frame)) in where)
        {
            foreach (var v in frame.Visuals)
            {
                if (!v.Visual.IsActor && v.Visual.Owner == body.Combatant)
                    visuals.Add(v);
            }
        }
        visuals.Sort((x, y) => x.Visual.Handle.CompareTo(y.Visual.Handle));

        return new BattleFrame
        {
            Tick = lead.Tick,
            Step = lead.Step,
            State = lead.State,
            Current = lead.Current,
            Bodies = [.. bodies],
            Missile = missiles.Count > 0 ? missiles[^1] : null,
            Missiles = missiles,
            Visuals = [.. visuals],
            Numbers = [.. numbers],
            Ring = lead.Ring,
            RingShown = lead.RingShown,
            Aim = lead.Aim,
            Fade = lead.Fade,
            MusicHeld = lead.MusicHeld,
            SkyDim = lead.SkyDim,
            FogLifted = lead.FogLifted,
            Camera = Settling(lead.Camera),
            Gui = Gui(lead, where),
            Menu = lead.Menu,
            Aura = lead.Aura,
            Cues = cues,
            Sounds = calls,
            Emitters = starts,
            Jumped = jumped,
        };
    }

    /// <summary>The drawn camera, settling over <see cref="CameraTicks"/> on a newer action's as it starts.</summary>
    private DrawnCamera Settling(DrawnCamera to)
    {
        if (_cameraLeft <= 0 || Frame is null)
            return to;
        var from = Frame.Camera;
        var k = 1f / _cameraLeft--;
        var look = Vector2.Lerp(from.LookXZ, to.LookXZ, k);
        return new DrawnCamera(Vector3.Lerp(from.Position, to.Position, k), Vector3.Lerp(from.Aim, to.Aim, k),
            look.LengthSquared() > 1e-6f ? Vector2.Normalize(look) : to.LookXZ);
    }

    /// <summary>
    /// The battle screen: the newest action's, with each slot's frame, portrait and HP bar from the tick its body is drawn at, the radar's
    /// dots where the bodies are drawn, and the log's last line drawn.
    /// </summary>
    private CombatGuiFrame Gui(BattleFrame lead, Dictionary<int, (DrawnBody Body, BattleFrame Frame)> where)
    {
        var g = lead.Gui;
        var frames = new List<(HudSprite, bool)>(g.Frames.Count);
        for (var i = 0; i < g.Frames.Count; i++)
        {
            var mine = g.Frames[i];
            if (i < g.FrameSlots.Count && g.FrameSlots[i] is { } slot && where.TryGetValue(slot, out var at) && at.Frame != lead)
            {
                var other = at.Frame.Gui;
                for (var j = 0; j < other.FrameSlots.Count; j++)
                {
                    if (other.FrameSlots[j] == slot)
                    {
                        mine = other.Frames[j];
                        break;
                    }
                }
            }
            frames.Add(mine);
        }
        var portraits = new List<CombatGuiPortrait>(g.Portraits.Count);
        foreach (var p in g.Portraits)
        {
            var mine = p;
            if (p.Slot is { } slot && where.TryGetValue(slot, out var at) && at.Frame != lead)
            {
                foreach (var q in at.Frame.Gui.Portraits)
                {
                    if (q.Slot == slot && q.Entity == p.Entity)
                    {
                        mine = q;
                        break;
                    }
                }
            }
            portraits.Add(mine);
        }
        var bars = new List<(HudFill, bool)>(g.HpBars.Count);
        var barSlots = new List<int>(g.HpSlots.Count);
        for (var i = 0; i < g.HpBars.Count; i++)
        {
            var mine = g.HpBars[i];
            if (i < g.HpSlots.Count && where.TryGetValue(g.HpSlots[i], out var at) && at.Frame != lead)
            {
                var other = at.Frame.Gui;
                // The same fill of the slot's pair: the black, then the colour.
                var nth = i > 0 && g.HpSlots[i - 1] == g.HpSlots[i] ? 1 : 0;
                for (var j = 0; j < other.HpSlots.Count; j++)
                {
                    if (other.HpSlots[j] == g.HpSlots[i] && j + nth < other.HpBars.Count)
                    {
                        mine = other.HpBars[j + nth];
                        break;
                    }
                }
            }
            bars.Add(mine);
            barSlots.Add(i < g.HpSlots.Count ? g.HpSlots[i] : -1);
        }
        var radar = g.Radar is { } entries
            ? CombatRadar.Dots(entries, 0, 0, e => where.TryGetValue(e.Slot, out var at) ? at.Body.Centre : e.Centre,
                where.TryGetValue(entries.Owner, out var owner) ? owner.Body.Centre : null)
            : g.RadarDots;
        return g with { Log = _log, RadarDots = radar, Frames = frames, Portraits = portraits, HpBars = bars, FrameSlots = g.FrameSlots, HpSlots = barSlots };
    }

    /// <summary>A body's timeline: the tick it's drawn at, the episode it's in or waits for, and what of its one-offs has played.</summary>
    private sealed class Track(int cursor)
    {
        public int Cursor { get; set; } = cursor;

        public int Episode { get; set; }

        public int MomentsPlayed { get; set; }

        public bool Jumped { get; set; }

        public bool Gone { get; set; }
    }
}
