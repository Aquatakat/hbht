using System.Numerics;
using HBHT.Plugins.Records;

namespace HBHT.Core.Combat;

/// <summary>
/// A battle's recording cut into actions (ADR 0040's stage 3), one a turn, as its frames come: what each touches, over which of its
/// ticks, and the timeline each of its one-offs plays on. <see cref="BattleDrawing"/> schedules and composes the actions from it.
/// </summary>
/// <remarks>
/// <para>
/// A turn's frames run from the frame whose <see cref="BattleFrame.Current"/> changed to the frame before the next change. Before the
/// first turn (the intro) is an action with no combatant.
/// </para>
/// <para>
/// A body is <b>involved</b> on a tick it moves (its centre more than <see cref="MoveEpsilon"/> from the tick before: the crowd's push
/// nudges bodies by less), turns, its actor plays outside its idle loop (the states its first reaches by the idle request and any
/// request), its alpha, what it holds or its hit points change, a cue names it (a request, a death, its joining or going, a number over
/// it, a spell's visual on it or aimed at it), or a spell's actor on it hasn't played out or aimed at it hasn't struck. An action's own
/// combatant is involved on every tick of it. A body's involvement is gathered into <see cref="Episode"/>s: a run of involved ticks, gaps
/// of up to <see cref="Gap"/> ticks bridged. A cue or the turn starts a new episode in the action whose frame it's on; involvement with
/// no cue carries on the body's last episode, even past its action's end (a dead body falling and fading as the next turn begins), for
/// up to <see cref="TailCap"/> ticks past that end, so it's drawn at the pace it began at.
/// </para>
/// <para>
/// What plays once (each cue, sound call and emitter start) is a <see cref="Moment"/> on a timeline: a body's, for what's about one
/// body (its cues, its keys' sounds, its actor's emitters, the lasting scene over its head), or an action's, for what that action began
/// (its numbers, missile and spells' actors, their cues and emitters) and what's about the battle (the log's lines, the pause, the end,
/// a sound no body's keys made). A body's timeline runs with the action it's in an episode of, and on its own between them.
/// </para>
/// </remarks>
public sealed class BattleActions
{
    /// <summary>How far a body's centre moves in a tick, at least, to be moving: the crowd's push moves bodies less.</summary>
    public const float MoveEpsilon = 0.004f;

    /// <summary>How far a body's heading turns in a tick, at least (the dot product's shortfall from 1), to be turning.</summary>
    public const float TurnEpsilon = 0.0001f;

    /// <summary>The ticks of no involvement bridged within an episode.</summary>
    public const int Gap = 8;

    /// <summary>How far past its action's last tick an episode carries on without a cue.</summary>
    public const int TailCap = 180;

    private readonly List<BattleFrame> _frames = [];
    private int _base;
    private readonly List<DrawnAction> _actions = [];
    private readonly Dictionary<Combatant, List<Episode>> _episodes = [];
    private readonly Dictionary<Combatant, List<Moment>> _bodyItems = [];
    private readonly Dictionary<object, DrawnAction> _births = [];
    private readonly Dictionary<Audio.SoundHandle, (DrawnAction? Action, Combatant? Body)> _soundTimelines = [];
    private readonly Dictionary<ActorRecord, HashSet<int>> _idle = [];
    private readonly HashSet<Combatant> _known = [];
    private int _seq;

    /// <param name="first">The frame the battle's beginning drew, at once: tick 0, its one-offs already played.</param>
    public BattleActions(BattleFrame first)
    {
        _base = first.Tick;
        _frames.Add(first);
        var action = new DrawnAction(0, first.Current, first.Tick);
        _actions.Add(action);
        foreach (var body in first.Bodies)
            Know(body.Combatant);
    }

    /// <summary>The actions, in the battle's order.</summary>
    public IReadOnlyList<DrawnAction> Actions => _actions;

    /// <summary>The last tick recorded.</summary>
    public int Last => _base + _frames.Count - 1;

    /// <summary>The first tick still kept.</summary>
    public int Kept => _base;

    /// <summary>The frame of tick <paramref name="tick"/>.</summary>
    public BattleFrame this[int tick] => _frames[tick - _base];

    /// <summary>The combatants recorded, each from its first frame or its joining.</summary>
    public IReadOnlyCollection<Combatant> Bodies => _known;

    /// <summary>A body's episodes, in order.</summary>
    public IReadOnlyList<Episode> EpisodesOf(Combatant body) => _episodes.TryGetValue(body, out var list) ? list : [];

    /// <summary>What plays on a body's timeline, in order.</summary>
    internal List<Moment> MomentsOf(Combatant body) => _bodyItems.TryGetValue(body, out var list) ? list : [];

    /// <summary>The action a number, missile or spell's actor began in, whose timeline it's drawn on; null for a lasting scene.</summary>
    public DrawnAction? BirthOf(object thing) => _births.GetValueOrDefault(thing);

    /// <summary>Forgets the frames before <paramref name="tick"/>, which nothing will draw again.</summary>
    public void Forget(int tick)
    {
        var n = Math.Min(tick - _base, _frames.Count - 1);
        if (n < 512)
            return;
        _frames.RemoveRange(0, n);
        _base += n;
    }

    /// <summary>Takes the next frame the battle recorded.</summary>
    public void Add(BattleFrame frame)
    {
        if (frame.Tick != Last + 1)
            throw new ArgumentException($"Frame {frame.Tick} after {Last}.", nameof(frame));
        var before = _frames[^1];
        _frames.Add(frame);
        var t = frame.Tick;

        var action = _actions[^1];
        if (frame.Current != action.Current)
        {
            action.Close(t - 1);
            action = new DrawnAction(_actions.Count, frame.Current, t);
            _actions.Add(action);
        }

        // What the frame's cues say, and whom.
        var cued = new HashSet<Combatant>();
        foreach (var cue in frame.Cues)
        {
            switch (cue)
            {
                case CombatantJoined j:
                    Know(j.Combatant);
                    cued.Add(j.Combatant);
                    break;
                case CombatantAnimated a:
                    cued.Add(a.Combatant);
                    break;
                case CombatantDied d:
                    cued.Add(d.Combatant);
                    break;
                case CombatantGone g:
                    cued.Add(g.Combatant);
                    break;
                case NumberShown n:
                    _births[n.Number] = action;
                    action.Begin(n.Number);
                    if (frame.Bodies.FirstOrDefault(b => b.Combatant.Slot == n.Number.Slot).Combatant is { } over)
                        cued.Add(over);
                    break;
                case NumberGone n when _births.GetValueOrDefault(n.Number) is { } born:
                    born.End(n.Number, t);
                    break;
                case MissileLaunched m:
                    _births[m.Flight] = action;
                    action.Begin(m.Flight);
                    break;
                case MissileLanded m when _births.GetValueOrDefault(m.Flight) is { } born:
                    born.End(m.Flight, t);
                    break;
                case SpellVisualStarted v:
                    cued.Add(v.Visual.Owner);
                    if (v.Visual.Other is { } other)
                        cued.Add(other);
                    if (v.Visual.IsActor)
                    {
                        _births[v.Visual] = action;
                        action.Begin(v.Visual);
                    }
                    break;
                case SpellVisualEnded v when _births.GetValueOrDefault(v.Visual) is { } born:
                    born.End(v.Visual, t);
                    break;
            }
        }
        if (action.Current is { } own)
            cued.Add(own);

        // Who is involved, and in which action's episode.
        foreach (var body in frame.Bodies)
        {
            var c = body.Combatant;
            var was = Find(before.Bodies, c);
            var busy = cued.Contains(c) || Busy(body, was, frame);
            if (busy)
                Involve(c, t, action, cued.Contains(c));
        }
        // A body gone this tick is no longer in the frame, but its going is its.
        foreach (var c in cued)
        {
            if (Find(frame.Bodies, c) is null)
                Involve(c, t, action, cue: true);
        }
        if (action.Current is { } current)
            action.Own = _episodes[current][^1];

        // The one-offs, each on its timeline.
        foreach (var cue in frame.Cues)
            Place(t, cue, action, Timeline(cue, action));
        foreach (var call in frame.Sounds)
            Place(t, call, action, SoundTimeline(call, action));
        foreach (var start in frame.Emitters)
        {
            var owner = start.Owner switch
            {
                Combatant c => (null, c),
                SpellFxVisual { IsActor: true } v => (_births.GetValueOrDefault(v) ?? action, null),
                SpellFxVisual v => ((DrawnAction?)null, (Combatant?)v.Owner),
                _ => (action, null),
            };
            Place(t, start, action, owner);
        }
        action.Recorded(t);
    }

    private void Know(Combatant c)
    {
        if (_known.Add(c))
        {
            _episodes[c] = [];
            _bodyItems[c] = [];
        }
    }

    /// <summary>Whether a body does anything but idle on this tick, by its drawn state against the tick before's.</summary>
    private bool Busy(DrawnBody body, DrawnBody? was, BattleFrame frame)
    {
        if (was is not { } w)
            return true;
        if (Vector3.DistanceSquared(body.Centre, w.Centre) > MoveEpsilon * MoveEpsilon)
            return true;
        if (1 - Vector2.Dot(body.Heading, w.Heading) > TurnEpsilon)
            return true;
        if (body.Alpha != w.Alpha || body.Hp != w.Hp || body.Arm != w.Arm || body.Hand != w.Hand || body.Notes != w.Notes)
            return true;
        if (body.Combatant.Actor?.Machine.Actor is { } actor && !Idle(actor).Contains(body.ActorState)
            && (body.ActorState != w.ActorState || body.ActorClock != w.ActorClock))
            return true;
        foreach (var v in frame.Visuals)
        {
            if (!v.Visual.IsActor)
                continue;
            if (v.Visual.Owner == body.Combatant && !v.Played || v.Visual.Other == body.Combatant && !v.Struck)
                return true;
        }
        return false;
    }

    /// <summary>The states an actor idles in: those its first state reaches by the idle request and by any.</summary>
    private HashSet<int> Idle(ActorRecord actor)
    {
        if (_idle.TryGetValue(actor, out var idle))
            return idle;
        idle = [];
        var todo = new Stack<int>([actor.InitialState]);
        while (todo.Count > 0)
        {
            var s = todo.Pop();
            if (s < 0 || s >= actor.States.Count || !idle.Add(s))
                continue;
            foreach (var b in actor.States[s].Branches)
            {
                if (b.Number is 0 or Requests.Any)
                    todo.Push(b.State);
            }
        }
        return _idle[actor] = idle;
    }

    /// <summary>A body is involved on tick <paramref name="t"/>: its last episode carries on, or one begins in <paramref name="action"/>.</summary>
    private void Involve(Combatant c, int t, DrawnAction action, bool cue)
    {
        Know(c);
        var list = _episodes[c];
        if (list.Count > 0 && list[^1] is var last && last.To >= t - Gap - 1)
        {
            if (last.Action == action)
            {
                last.To = t;
                return;
            }
            // Carried past its action without a cue, for a while.
            if (!cue && last.Action.Closed && t <= last.Action.Last + TailCap)
            {
                last.To = t;
                return;
            }
        }
        var episode = new Episode(c, action, t);
        list.Add(episode);
        action.Add(episode);
    }

    private static DrawnBody? Find(DrawnBody[] bodies, Combatant c)
    {
        foreach (var b in bodies)
        {
            if (b.Combatant == c)
                return b;
        }
        return null;
    }

    /// <summary>The timeline a cue plays on.</summary>
    private (DrawnAction? Action, Combatant? Body) Timeline(CombatCue cue, DrawnAction action) => cue switch
    {
        CombatantJoined j => (null, j.Combatant),
        CombatantAnimated a => (null, a.Combatant),
        CombatantDied d => (null, d.Combatant),
        CombatantGone g => (null, g.Combatant),
        NumberShown or NumberGone or MissileLaunched or MissileLanded => (Born(cue) ?? action, null),
        SpellVisualStarted { Visual: { IsActor: false } v } => (null, v.Owner),
        SpellVisualEnded { Visual: { IsActor: false } v } => (null, v.Owner),
        SpellVisualStarted or SpellVisualEnded => (Born(cue) ?? action, null),
        _ => (action, null),
    };

    private DrawnAction? Born(CombatCue cue) => cue switch
    {
        NumberShown n => _births.GetValueOrDefault(n.Number),
        NumberGone n => _births.GetValueOrDefault(n.Number),
        MissileLaunched m => _births.GetValueOrDefault(m.Flight),
        MissileLanded m => _births.GetValueOrDefault(m.Flight),
        SpellVisualStarted v => _births.GetValueOrDefault(v.Visual),
        SpellVisualEnded v => _births.GetValueOrDefault(v.Visual),
        _ => null,
    };

    /// <summary>
    /// The timeline a sound call plays on: its body's, if a body's keys made it; otherwise its sound's start's, so it never stops before
    /// it starts.
    /// </summary>
    private (DrawnAction? Action, Combatant? Body) SoundTimeline(SoundCall call, DrawnAction action)
    {
        if (call.Owner is { } owner && _known.Contains(owner))
            return (null, owner);
        var handle = call switch
        {
            SoundCall.Start s => s.Handle,
            SoundCall.Set s => s.Handle,
            SoundCall.Duration d => d.Handle,
            SoundCall.Stop s => s.Handle,
            _ => default,
        };
        if (handle.IsNone)
            return (action, null);
        if (call is SoundCall.Start)
            return _soundTimelines[handle] = (action, null);
        return _soundTimelines.TryGetValue(handle, out var timeline) ? timeline : (action, null);
    }

    private void Place(int t, object payload, DrawnAction frameAction, (DrawnAction? Action, Combatant? Body) timeline)
    {
        var item = new Moment(_seq++, t, payload);
        if (timeline.Body is { } body)
        {
            Know(body);
            _bodyItems[body].Add(item);
        }
        else
        {
            (timeline.Action ?? frameAction).Moments.Add(item);
        }
    }
}

/// <summary>
/// One turn's frames as an action (ADR 0040's stage 3): its combatant, its ticks, the bodies it involves (<see cref="Episodes"/>), the
/// numbers, missiles and spells' actors it began, and where the drawing has it: when it starts drawing and how far it has played.
/// </summary>
public sealed class DrawnAction
{
    private readonly List<Episode> _episodes = [];
    private readonly Dictionary<object, int?> _things = [];

    internal DrawnAction(int index, Combatant? current, int first)
    {
        Index = index;
        Current = current;
        First = first;
        Last = first;
        Played = first - 1;
    }

    /// <summary>Its place among the battle's actions.</summary>
    public int Index { get; }

    /// <summary>Whose turn it is, or none for the intro.</summary>
    public Combatant? Current { get; }

    /// <summary>Its first tick.</summary>
    public int First { get; }

    /// <summary>Its last tick: the last recorded while it's open.</summary>
    public int Last { get; private set; }

    /// <summary>Whether the next turn has begun, so its ticks are all known.</summary>
    public bool Closed { get; private set; }

    /// <summary>Its combatant's episode, over the whole of it.</summary>
    public Episode? Own { get; internal set; }

    /// <summary>The episodes it began, each of a body it involves.</summary>
    public IReadOnlyList<Episode> Episodes => _episodes;

    /// <summary>The numbers, missiles and spells' actors it began, each with the tick it ended on, or null while it lives.</summary>
    public IReadOnlyDictionary<object, int?> Things => _things;

    /// <summary>The drawn tick it starts drawing on, once it's scheduled.</summary>
    public int? Start { get; internal set; }

    /// <summary>Whether it's scheduled to start, or has started.</summary>
    public bool Scheduled => Start is not null;

    /// <summary>The last of its timeline's ticks played: its frames, then its tail.</summary>
    public int Played { get; internal set; }

    /// <summary>Whether its timeline has played to its end, or was caught up.</summary>
    public bool Done { get; internal set; }

    /// <summary>
    /// The last tick its timeline plays: its last tick, its episodes' and the end of what it began; null while what it began lives on.
    /// </summary>
    public int? TailEnd
    {
        get
        {
            var end = Last;
            foreach (var e in _episodes)
                end = Math.Max(end, e.To);
            foreach (var (_, ended) in _things)
            {
                if (ended is not { } at)
                    return null;
                end = Math.Max(end, at);
            }
            return end;
        }
    }

    /// <summary>The tick its timeline has on drawn tick <paramref name="drawn"/>, once it has started.</summary>
    public int TickAt(int drawn) => First + (drawn - Start!.Value);

    /// <summary>The drawn tick it draws tick <paramref name="tick"/> on, once it's scheduled.</summary>
    public int DrawnAt(int tick) => Start!.Value + (tick - First);

    /// <summary>What plays on its timeline, in order.</summary>
    internal List<Moment> Moments { get; } = [];

    /// <summary>How many of <see cref="Moments"/> have played.</summary>
    internal int MomentsPlayed { get; set; }

    internal void Close(int last)
    {
        Last = last;
        Closed = true;
    }

    internal void Recorded(int tick)
    {
        if (!Closed)
            Last = tick;
    }

    internal void Add(Episode e) => _episodes.Add(e);

    internal void Begin(object thing) => _things[thing] = null;

    internal void End(object thing, int tick) => _things[thing] = tick;

    public override string ToString() => $"action {Index} ({Current?.ToString() ?? "intro"}) {First}–{(Closed ? Last : "…")}";
}

/// <summary>A body's run of involved ticks in one action (<see cref="BattleActions"/>), which may carry on past the action's end.</summary>
public sealed class Episode(Combatant body, DrawnAction action, int from)
{
    public Combatant Body { get; } = body;

    public DrawnAction Action { get; } = action;

    /// <summary>Its first tick.</summary>
    public int From { get; } = from;

    /// <summary>Its last tick so far.</summary>
    public int To { get; internal set; } = from;

    public override string ToString() => $"{Body} in {Action.Index}: {From}–{To}";
}

/// <summary>Something a frame plays once (a cue, a sound call or an emitter start), with its tick and its place in the recording's order.</summary>
internal readonly record struct Moment(int Seq, int Tick, object Payload);
