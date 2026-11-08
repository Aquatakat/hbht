using System.Numerics;
using HBHT.Plugins.Records;

namespace HBHT.Core.Combat;

/// <summary>
/// A battle's recording cut into actions (Start Early), one a turn, as its frames come: what each touches, over which of its
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
    private readonly Dictionary<Combatant, Reaction> _reacting = [];
    private int _seq;

    /// <param name="first">The frame the battle's beginning drew, at once: tick 0, its one-offs already played.</param>
    /// <param name="pacing">How long a beat lasts (<see cref="StartEarlyPacing"/>); the defaults if none.</param>
    public BattleActions(BattleFrame first, StartEarlyPacing? pacing = null)
    {
        Pacing = pacing ?? new StartEarlyPacing();
        _base = first.Tick;
        _frames.Add(first);
        var action = new DrawnAction(0, first.Current, first.Tick);
        _actions.Add(action);
        foreach (var body in first.Bodies)
            Know(body.Combatant);
    }

    /// <summary>The actions, in the battle's order.</summary>
    public IReadOnlyList<DrawnAction> Actions => _actions;

    /// <summary>How long a beat lasts.</summary>
    public StartEarlyPacing Pacing { get; }

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
        Beats(frame, before, action);

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

    /// <summary>
    /// The frame's results, each a <see cref="Beat"/> on the body it lands on: a hit, a dodge, a fall or an ally's help asked of its
    /// actor, a number over it, and a spell's or potion's actor striking it (its target's, its drinker's, or a resisted or refused
    /// cast's, a miss). What lands on a body whose beat in this action still plays joins it. Then each beat playing ends once its
    /// reaction has: its actor back where it stood before the strike (its idle loop or the state it stood in) or still, as a body lies
    /// where it fell, once it has left it, or the body fading; or, if its actor never leaves, after
    /// <see cref="StartEarlyPacing.ReactionWait"/> ticks; never before <see cref="StartEarlyPacing.BeatLeast"/> ticks, and never past
    /// <see cref="StartEarlyPacing.BeatMost"/>.
    /// </summary>
    private void Beats(BattleFrame frame, BattleFrame before, DrawnAction action)
    {
        var t = frame.Tick;
        var struck = new List<(Combatant, BeatKind)>();
        foreach (var cue in frame.Cues)
        {
            switch (cue)
            {
                case CombatantAnimated { Request: Battle.HitRequest } a:
                    struck.Add((a.Combatant, BeatKind.Damage));
                    break;
                case CombatantAnimated { Request: Battle.DodgeRequest } a:
                    struck.Add((a.Combatant, BeatKind.Miss));
                    break;
                case CombatantAnimated { Request: Battle.DeathRequest } a:
                    struck.Add((a.Combatant, BeatKind.Defeat));
                    break;
                case CombatantAnimated { Request: Battle.HelpedRequest } a:
                    struck.Add((a.Combatant, BeatKind.Effect));
                    break;
                case CombatantDied d when _reacting.TryGetValue(d.Combatant, out var r) && r.Beat.Action == action:
                    r.Beat.Kind |= BeatKind.Defeat;
                    break;
                case NumberShown n when frame.Bodies.FirstOrDefault(b => b.Combatant.Slot == n.Number.Slot).Combatant is { } over:
                    struck.Add((over, n.Number.Healing ? BeatKind.Effect : BeatKind.Damage));
                    break;
            }
        }
        foreach (var v in frame.Visuals)
        {
            if (!v.Struck || !v.Visual.IsActor || v.Visual.Role == SpellFxRole.Caster)
                continue;
            var was = false;
            foreach (var w in before.Visuals)
            {
                if (w.Visual == v.Visual)
                {
                    was = w.Struck;
                    break;
                }
            }
            if (!was)
                struck.Add((v.Visual.Owner, v.Visual.Role is SpellFxRole.Resisted or SpellFxRole.Refused ? BeatKind.Miss : BeatKind.Effect));
        }

        var fresh = new HashSet<Combatant>();
        foreach (var (body, kind) in struck)
        {
            Know(body);
            fresh.Add(body);
            if (_reacting.TryGetValue(body, out var r))
            {
                if (r.Beat.Action == action)
                {
                    r.Beat.Kind |= kind;
                    continue;
                }
                // A newer action's result on a body still reacting to an older one's: that one ends where this one strikes.
                r.Beat.To = Math.Max(r.Beat.From, t - 1);
                _reacting.Remove(body);
            }
            var beat = new Beat(action, body, kind, t);
            action.AddBeat(beat);
            _reacting[body] = new Reaction(beat, Find(before.Bodies, body)?.ActorState);
        }

        foreach (var (body, r) in _reacting.ToList())
        {
            var beat = r.Beat;
            var length = t - beat.From + 1;
            if (Find(frame.Bodies, body) is not { } drawn)
            {
                beat.To = Math.Max(beat.From, t - 1);
                _reacting.Remove(body);
                continue;
            }
            var over = true;
            var was = Find(before.Bodies, body);
            if (body.Actor?.Machine.Actor is { } actor)
            {
                // Back where it stood before the strike: its idle loop, the state it stood in, or still, as a body lies where it fell.
                var back = Idle(actor).Contains(drawn.ActorState) || drawn.ActorState == r.Stood;
                var still = was is { } w && w.ActorState == drawn.ActorState && w.ActorClock == drawn.ActorClock;
                if (!back && !still)
                    r.Reacted = true;
                over = r.Reacted ? back || still : length > Pacing.ReactionWait;
            }
            // A body fading, dead or fled, has had its reaction.
            if (was is { } fading && drawn.Alpha < fading.Alpha)
                over = true;
            if (over && length >= Pacing.BeatLeast || length >= Pacing.BeatMost)
            {
                beat.To = t;
                _reacting.Remove(body);
            }
        }
    }

    /// <summary>
    /// A beat still playing: the state its body's actor stood in before it struck, and whether the actor has left that, and its idle
    /// loop, since.
    /// </summary>
    private sealed class Reaction(Beat beat, int? stood)
    {
        public Beat Beat { get; } = beat;

        public int? Stood { get; } = stood;

        public bool Reacted { get; set; }
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
/// One turn's frames as an action (Start Early): its combatant, its ticks, the bodies it involves (<see cref="Episodes"/>), the
/// numbers, missiles and spells' actors it began, and where the drawing has it: when it starts drawing and how far it has played.
/// </summary>
public sealed class DrawnAction
{
    private readonly List<Episode> _episodes = [];
    private readonly List<Beat> _beats = [];
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

    /// <summary>Its results, each a beat on the body it lands on, in the order they strike (<see cref="Beat"/>).</summary>
    public IReadOnlyList<Beat> Beats => _beats;

    /// <summary>The numbers, missiles and spells' actors it began, each with the tick it ended on, or null while it lives.</summary>
    public IReadOnlyDictionary<object, int?> Things => _things;

    /// <summary>The drawn tick it starts drawing on, once it's scheduled.</summary>
    public int? Start { get; internal set; }

    /// <summary>Whether it's scheduled to start, or has started.</summary>
    public bool Scheduled => Start is not null;

    /// <summary>
    /// Whether it's a turn the player plays, opened once its combatant was free in the drawing (Start Early): drawn on the
    /// battle's present while it waits on the player, the earlier actions drawing on behind it.
    /// </summary>
    public bool Opened { get; internal set; }

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

    internal void AddBeat(Beat b) => _beats.Add(b);

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

/// <summary>What a beat's result was: one result can be several, as a hit that kills is damage and a defeat.</summary>
[Flags]
public enum BeatKind
{
    /// <summary>Hit points lost: a hit asked of its actor, or a number over it.</summary>
    Damage = 1,

    /// <summary>A miss: a dodge asked of its actor, or a cast resisted or refused.</summary>
    Miss = 2,

    /// <summary>A defeat: a fall asked of its actor, or its death.</summary>
    Defeat = 4,

    /// <summary>An effect added: a spell's or potion's actor striking it, an ally's help, or hit points healed.</summary>
    Effect = 8,
}

/// <summary>
/// A result (Start Early): damage, a miss or dodge, a defeat or an effect added, on the body it lands on, from its strike to the end
/// of its reaction, by the ticks of the action whose frame it struck on (<see cref="BattleActions"/>). Beats of different actions play
/// at once only where one shot can hold them all (<see cref="IBeatFraming"/>), and a turn the player plays opens once the beats
/// before it have played.
/// </summary>
public sealed class Beat(DrawnAction action, Combatant subject, BeatKind kind, int from)
{
    /// <summary>The action it struck in, whose timeline it plays on.</summary>
    public DrawnAction Action { get; } = action;

    /// <summary>The body it lands on.</summary>
    public Combatant Subject { get; } = subject;

    /// <summary>Whose action it is: the attacker, the caster, the drinker; none in the intro.</summary>
    public Combatant? Source => Action.Current;

    public BeatKind Kind { get; internal set; } = kind;

    /// <summary>The tick it strikes on.</summary>
    public int From { get; } = from;

    /// <summary>Its last tick, once its reaction is over; null while it plays on in the recording.</summary>
    public int? To { get; internal set; }

    /// <summary>Its last tick, or <paramref name="last"/>, the recording's, while it plays on.</summary>
    public int End(int last) => To ?? Math.Max(From, last);

    public override string ToString() => $"{Kind} on {Subject} in {Action.Index}: {From}–{(To is { } to ? to.ToString() : "…")}";
}

/// <summary>Something a frame plays once (a cue, a sound call or an emitter start), with its tick and its place in the recording's order.</summary>
internal readonly record struct Moment(int Seq, int Tick, object Payload);
