using System.Numerics;
using HBHT.Core.Ui;
using HBHT.Plugins;

namespace HBHT.Core.Combat;

/// <summary>
/// Schedules and composes a battle's actions as they're drawn (Start Early), over its recording cut into actions
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
    /// <summary>What a fast-forward passed over since the last frame composed: its cues are handed over, its sounds and emitters skipped.</summary>
    private readonly List<Moment> _skipped = [];
    private readonly Func<bool> _waits;
    private int _drawn;
    private int _unscheduled = 1;
    private int _live;
    private string _log;

    /// <summary>Whose portrait the log's box shows with <see cref="_log"/>: the one the battle screen had on the tick the line was written.</summary>
    private RecordId? _logPortrait;

    /// <summary>The tick <see cref="_log"/> was written on, and the line written latest of those played, with its portrait and tick.</summary>
    private int _logTick;
    private (int Seq, string Text, RecordId? Portrait, int Tick)? _newestLog;
    private DrawnAction? _cameraFrom;
    private int _cameraLeft;
    private int _leadTick;

    /// <param name="first">The frame the battle's beginning drew, at once, its one-offs played.</param>
    /// <param name="waits">Whether the battle waits on the player, so no more ticks come till the drawing catches up.</param>
    /// <param name="pacing">How long beats last and how much room the player's reach keeps; the defaults if none.</param>
    public BattleComposer(BattleFrame first, Func<bool> waits, StartEarlyPacing? pacing = null)
    {
        _actions = new BattleActions(first, pacing);
        _actions.Actions[0].Start = 0;
        (_log, _logPortrait) = (first.Gui.Log, first.Gui.LogPortrait);
        _waits = waits;
        Frame = first;
        foreach (var body in _actions.Bodies)
            _tracks[body] = new Track(first.Tick);
    }

    /// <summary>The recording cut into actions, with where the drawing has each.</summary>
    public BattleActions Actions => _actions;

    /// <summary>How long beats last and how much room the player's reach keeps.</summary>
    public StartEarlyPacing Pacing => _actions.Pacing;

    /// <summary>
    /// Whether one shot can hold beats that play at once, which the composer asks before two actions' results overlap and before a
    /// turn the player plays opens while an earlier result plays. The stand-in, <see cref="SpreadFraming"/>, until piece C's director.
    /// </summary>
    public IBeatFraming Framing { get; set; } = new SpreadFraming();

    /// <summary>The drawn ticks so far.</summary>
    public int Drawn => _drawn;

    /// <summary>The frame drawn last.</summary>
    public BattleFrame Frame { get; private set; }

    /// <summary>Takes the frame the battle just recorded.</summary>
    public void Record(BattleFrame frame)
    {
        // The battle's first line is on its first frame, before the battle screen has a portrait for it: it takes its next tick's.
        if (_logPortrait is null && frame.Gui.Log == _log)
            _logPortrait = frame.Gui.LogPortrait;
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
                if (a.Start > _drawn || Behind(a, last))
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
            if (a.Start <= _drawn && Draws(a))
                yield return a;
        }
    }

    /// <summary>Whether an action started is still drawing: its last timeline not past its end, or what it began still living.</summary>
    private bool Draws(DrawnAction a) => a.EndOffset is { } o ? _drawn <= a.Start!.Value + o : a.Base(_drawn) <= _actions.Last;

    /// <summary>Whether a timeline of an action started is drawn before <paramref name="cap"/> and its own end.</summary>
    private bool Behind(DrawnAction a, int cap)
    {
        foreach (var p in a.Timelines)
        {
            if (a.TickAt(_drawn, p) < Math.Min(a.EndOf(p) ?? cap, cap))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a body is still drawn behind the battle's present: idling behind it, or in an episode of an action the drawing hasn't
    /// finished. What a turn the player plays can fast-forward (Start Early).
    /// </summary>
    public bool Behind(Combatant body) => _tracks.TryGetValue(body, out var t) && !t.Gone && t.Cursor < _actions.Last;

    /// <summary>The tick of the recording a body is drawn from now, or null for one not drawn.</summary>
    public int? DrawnTickOf(Combatant body) => _tracks.TryGetValue(body, out var t) && !t.Gone ? t.Cursor : null;

    /// <summary>
    /// The turn the player plays, opened early and drawn on the battle's present (Start Early): the newest action, opened once its
    /// combatant was free, while its turn goes on. Null if none.
    /// </summary>
    public DrawnAction? Live
    {
        get
        {
            var all = _actions.Actions;
            return all[^1] is { Opened: true, Closed: false, Start: { } s } a && s <= _drawn ? a : null;
        }
    }

    /// <summary>
    /// Whether the player's turn is drawn up to the battle's present, so the battle steps with the drawing and takes the player's presses.
    /// </summary>
    public bool AtPresent => Live is { } live && live.TickAt(_drawn) >= _actions.Last;

    /// <summary>
    /// Whether a body has nothing left to draw in an action before the turn being played (the player's, or whichever is still open):
    /// every earlier episode of it drawn.
    /// </summary>
    public bool Free(Combatant body)
    {
        foreach (var e in _actions.EpisodesOf(body))
        {
            if (!e.Action.Closed)
                break;
            if (!Played(e))
                return false;
        }
        return true;
    }

    /// <summary>How many times an action has been fast-forwarded on its own (<see cref="Release"/>, <see cref="SkipWalks"/>, <see cref="SkipHold"/>).</summary>
    public int Forwarded { get; private set; }

    /// <summary>Whether an episode has been drawn to its end: its action is past its last tick.</summary>
    private bool Played(Episode e) =>
        e.Action.Start is { } s && s <= _drawn && e.Action.Decided(e.Part) && e.Action.TickAt(_drawn, e.Part) > e.To;

    /// <summary>
    /// What the player's turn touches is drawn where it is (Start Early): each action still drawing one of these bodies, before the
    /// player's, is fast-forwarded to its end, with what it depends on. What they pass is handed over with the next frame, its sounds
    /// and emitters skipped.
    /// </summary>
    public void Release(IEnumerable<Combatant> bodies)
    {
        foreach (var body in bodies)
            Release(body);
    }

    /// <summary>
    /// Brings a body on: the actions holding its earlier parts to their ends, then, idling, as far as its next part or the newest action.
    /// Whether it moved.
    /// </summary>
    private bool Release(Combatant body)
    {
        if (!_tracks.TryGetValue(body, out var track) || track.Gone)
            return false;
        var was = track.Cursor;
        Episode? next = null;
        foreach (var e in _actions.EpisodesOf(body))
        {
            if (!e.Action.Closed)
            {
                next = e;
                break;
            }
            if (!Played(e))
                Finish(e.Action);
        }
        var newest = Newest();
        var cap = Math.Min(newest is null || newest.Start > _drawn ? _actions.Last : Own(newest), _actions.Last);
        if (next is not null)
            cap = Math.Min(cap, next.From - 1);
        if (cap > track.Cursor && !track.Gone)
            Go(body, track, cap, _skipped);
        return track.Cursor > was;
    }

    /// <summary>
    /// A in an AI's walk under Start Early: each action drawing that's in its walk is fast-forwarded to its walk's last tick, as Original's
    /// A skips one walk, and plays on from there; what one depends on first is brought as far as it needs. The rest plays on.
    /// </summary>
    public void SkipWalks()
    {
        var live = Live;
        foreach (var a in DrawingNow().ToList())
        {
            var t = a.TickAt(_drawn);
            if (a == live || t > a.Last || t < Math.Max(a.First, _actions.Kept) || !_actions[t].Walking)
                continue;
            var end = t;
            while (end < Math.Min(a.Last, _actions.Last) && _actions[end + 1].Walking)
                end++;
            Forward(a, a.First + a.Offset(end, DrawnAction.OwnPart));
        }
    }

    /// <summary>Fast-forwards an action past its end: its timelines and its bodies' to the last tick each plays.</summary>
    private void Finish(DrawnAction a) => Forward(a, a.First + (a.EndOffset ?? a.Offset(_actions.Last, 0)) + 1);

    /// <summary>
    /// Puts an action's timeline at <paramref name="to"/> now, and each body it has reached there at that tick (or its episode's end),
    /// what they pass handed over with the next frame (<see cref="_skipped"/>). The whole timeline moves, so each of its episodes is
    /// reached sooner: every earlier part of each body it involves, in an earlier action, is brought far enough first to be drawn by the
    /// time it's reached (started sooner, if it was yet to start), so no body is in two actions at once and none goes back.
    /// </summary>
    private void Forward(DrawnAction a, int to)
    {
        if (a.Start is null)
            return;
        // Its base tick now (<paramref name="to"/>'s kind): before its first while it's yet to start.
        if (a.Base(_drawn) >= to)
            return;
        _search = null;
        foreach (var e in a.Episodes)
        {
            // It reaches this episode's first tick so many drawn ticks from now.
            var lead = Math.Max(0, a.First + a.Offset(e.From, e.Part) - to);
            foreach (var earlier in _actions.EpisodesOf(e.Body))
            {
                if (earlier.Action.Index >= a.Index)
                    break;
                var ea = earlier.Action;
                Forward(ea, ea.First + ea.Offset(earlier.To, earlier.Part) + 1 - lead);
            }
        }
        a.Start = _drawn - (to - a.First);
        Forwarded++;
        if (to < a.First)
            return;
        Decide(a);
        Advance(a, _skipped);
        foreach (var e in a.Episodes)
        {
            if (!a.Decided(e.Part) || !_tracks.TryGetValue(e.Body, out var track))
                continue;
            var at = a.At(to, e.Part);
            if (e.From > at)
                continue;
            var t = Math.Min(Math.Min(at, e.To), _actions.Last);
            if (t > track.Cursor)
                Go(e.Body, track, t, _skipped);
        }
    }

    /// <summary>The next drawn tick: what can start is scheduled, every timeline moves to it, and the frame composed of them is returned.</summary>
    public BattleFrame Next()
    {
        _drawn++;
        return Frame = Compose();
    }

    /// <summary>
    /// Every action at the battle's present at once: the frame composed there, with the one-offs passed on the way handed over but their
    /// sounds and emitters skipped (<see cref="BattleFrame.SkippedSounds"/>).
    /// </summary>
    public BattleFrame FastForward()
    {
        _search = null;
        for (var i = _unscheduled; i < _actions.Actions.Count; i++)
            _actions.Actions[i].Start = _drawn;
        _unscheduled = _actions.Actions.Count;
        Snap(_actions.Last, _skipped);
        return Frame = Composed([]);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Under Original: everything at the present

    /// <summary>A drawn tick at the battle's present, <paramref name="drawn"/>, drawn as it was recorded: every timeline is put there, its one-offs already played.</summary>
    public void Settle(BattleFrame drawn)
    {
        _drawn++;
        Frame = drawn;
        (_log, _logPortrait) = (drawn.Gui.Log, drawn.Gui.LogPortrait);
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
            foreach (var part in a.Parts)
                part.MomentsPlayed = part.Moments.Count;
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
                // Its parts' shifts first, all its ticks known. The search takes at most a share of a tick; one not done goes on next
                // tick, from the starts not yet passed.
                Decide(b);
                if (Search(b, budget) is not { } start)
                    return;
                b.Start = start;
            }
            else if (PlayersTurn(b))
            {
                // A turn the player plays opens once its combatant, and any body it has reached, has drawn its earlier parts, the
                // results before it have played, and no body still walking in the drawing is within its combatant's reach.
                if (!Opens(b))
                    return;
                b.Start = _drawn;
                b.Opened = true;
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

    /// <summary>Whether an action not yet settled is a turn the player plays, the battle waiting on them in it.</summary>
    private bool PlayersTurn(DrawnAction b) =>
        !b.Closed && b.Index == _actions.Actions.Count - 1 && b.Current is not null && _actions[_actions.Last].PlayersTurn && _waits();

    /// <summary>
    /// Whether the player's turn can open now: every earlier action has started, every body the turn has reached has drawn its
    /// episodes in them, every beat before it has played or can stay in shot with the turn (<see cref="BeatsBefore"/>), and no body
    /// still walking in the drawing could be touched once the turn takes the stick (<see cref="Walkers"/>).
    /// </summary>
    private bool Opens(DrawnAction b) => Started(b) && Reached(b) && BeatsBefore(b, null) && Walkers(b, null);

    /// <summary>Whether every action before <paramref name="b"/> has started drawing.</summary>
    private bool Started(DrawnAction b)
    {
        for (var i = _live; i < b.Index; i++)
        {
            if (_actions.Actions[i].Start is not { } s || s > _drawn)
                return false;
        }
        return true;
    }

    /// <summary>Whether every body <paramref name="b"/> has reached has drawn its episodes in earlier actions.</summary>
    private bool Reached(DrawnAction b)
    {
        foreach (var e in b.Episodes)
        {
            foreach (var earlier in _actions.EpisodesOf(e.Body))
            {
                if (earlier.Action.Index >= b.Index)
                    break;
                if (!Played(earlier))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Whether every beat of an action before the player's turn <paramref name="b"/> has played, or those still playing can stay in
    /// one shot with the turn's combatant (<see cref="Framing"/>); a beat yet to strike holds it. Those holding it are added to
    /// <paramref name="holding"/>, if given.
    /// </summary>
    private bool BeatsBefore(DrawnAction b, List<(DrawnAction Action, Beat Beat)>? holding)
    {
        var last = _actions.Last;
        var playing = new List<FramedBeat>();
        var waiting = false;
        for (var i = _live; i < b.Index; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start is not { } s || s > _drawn)
            {
                foreach (var beat in a.Beats)
                    holding?.Add((a, beat));
                waiting |= a.Beats.Count > 0;
                continue;
            }
            foreach (var beat in a.Beats)
            {
                var at = a.TickAt(_drawn, beat.Part);
                // A result is held on the screen a moment once it strikes, ended or not (the outcome hold); once held, it has played.
                if (at >= beat.From && at - beat.From < Pacing.OutcomeHold)
                {
                    waiting = true;
                    holding?.Add((a, beat));
                    continue;
                }
                if (at > Shown(beat))
                    continue;
                if (at < beat.From)
                {
                    waiting = true;
                    holding?.Add((a, beat));
                }
                else if (Framed(beat, at) is { } framed)
                {
                    playing.Add(framed);
                }
            }
        }
        if (waiting)
            return false;
        if (playing.Count == 0)
            return true;
        if (b.Current is { } c && CentreAt(c, last) is { } turn && Framing.CanHold(_drawn, playing, turn))
            return true;
        foreach (var f in playing)
            holding?.Add((f.Beat.Action, f.Beat));
        return false;
    }

    /// <summary>
    /// The last tick of its action a beat must be shown on: its end, or with an outcome hold, the hold's last tick, after which the camera
    /// may go on though its reaction plays on.
    /// </summary>
    private int Shown(Beat beat) =>
        Pacing.OutcomeHold > 0 ? Math.Min(beat.End(_actions.Last), beat.From + Pacing.OutcomeHold - 1) : beat.End(_actions.Last);

    /// <summary>A beat as a shot would hold it at tick <paramref name="tick"/> of its action, or null if its body isn't drawn there.</summary>
    private FramedBeat? Framed(Beat beat, int tick)
    {
        var t = Math.Clamp(tick, beat.From, beat.End(_actions.Last));
        if (CentreAt(beat.Subject, t) is not { } subject)
            return null;
        return new FramedBeat(beat, t, subject, beat.Source is { } source ? CentreAt(source, t) : null);
    }

    /// <summary>
    /// Whether no body still walking in the drawing could be touched by the player's combatant once its turn <paramref name="b"/>,
    /// opening now, takes the stick at drawn tick T, as it reaches the battle's present (Start Early). For each drawn tick t from T
    /// till a body is drawn out to where the core has it, the combatant could have got anywhere within its greatest travel a tick
    /// times t − T + 1 of where it stands, kept in its field; the body, drawn where the recording has it on the tick it's drawn from
    /// then (as the drawing would move it, its speed never asked), must stay farther from all of that than both their radii and
    /// <see cref="StartEarlyPacing.Buffer"/> of its own radius. A body drawn where the core has it can't be walked through, and one
    /// whose path never comes near the field blocks nothing. Those holding it are added to <paramref name="holding"/>, if given.
    /// </summary>
    private bool Walkers(DrawnAction b, List<Combatant>? holding)
    {
        var last = _actions.Last;
        if (b.Current is not { } mover || _actions[last].Field is not { } field || CentreAt(mover, last) is not { } at)
            return true;
        var control = _drawn + Math.Max(0, last - b.First);
        var p = new Vector2(at.X, at.Z);
        var centre = new Vector2(field.Centre.X, field.Centre.Z);
        var clear = true;
        foreach (var (body, track) in _tracks)
        {
            if (body == mover || track.Gone || track.Cursor >= last || CentreAt(body, last) is not { } present)
                continue;
            var need = mover.Body.Sphere.Radius + body.Body.Sphere.Radius * (1 + Pacing.Buffer);
            var episodes = _actions.EpisodesOf(body);
            var (cursor, next) = (track.Cursor, track.Episode);
            for (var d = _drawn; d - _drawn <= Pacing.ReachTicks; d++)
            {
                cursor = Projected(body, b, d, cursor, ref next, episodes);
                if (cursor >= last)
                    break;
                if (d < control || CentreAt(body, cursor) is not { } drawn)
                    continue;
                var e = new Vector2(drawn.X, drawn.Z);
                if (Vector2.DistanceSquared(e, new Vector2(present.X, present.Z)) < Slack * Slack)
                    continue;
                if (Gap(e, p, field.Travel * (d - control + 1), centre, field.Radius) <= need)
                {
                    clear = false;
                    holding?.Add(body);
                    break;
                }
            }
            if (!clear && holding is null)
                return false;
        }
        return clear;
    }

    /// <summary>
    /// The caster of a spell cast at several targets, their hits drawn at once (<see cref="DrawnAction.Parts"/>), faces them all
    /// (the user's, Nov 11). In the recording the caster turns to each target as its cast comes, as 1.0 has it, and the action's own
    /// timeline is the first part's and then the last's, so drawn as recorded the caster faced one target while hits landed all round.
    /// While the caster's tick is in the cast (its facing, the cast and its result), it's drawn turned, at a body's facing turn
    /// (<see cref="Body.FaceTurnPerFrame"/>), to the middle of the arc its targets spread over about it, where that's no wider than
    /// <see cref="StartEarlyPacing.CastArc"/>; where they spread wider, surrounding the caster, it stays as it stood before the cast.
    /// Out of the cast, or its action over, it turns back to the way the recording has it, and is drawn from the recording again once
    /// it's there. A target that's the caster counts for nothing, and one target alone is faced as recorded.
    /// </summary>
    private void FaceCasts(List<DrawnBody> bodies)
    {
        var turn = Cadence.PerTick(Movement.Body.FaceTurnPerFrame);
        _facingNow.Clear();
        for (var i = _live; i < _unscheduled; i++)
        {
            var a = _actions.Actions[i];
            if (a.Parts.Count < 2 || a.Current is not { } caster || a.Start is not { } s || s > _drawn
                || !_tracks.TryGetValue(caster, out var track) || track.Cursor < Math.Max(a.First, _actions.Kept) || track.Cursor > a.Last)
                continue;
            var at = bodies.FindIndex(b => b.Combatant == caster);
            if (at < 0)
                continue;
            var frame = _actions[Math.Min(track.Cursor, _actions.Last)];
            var recorded = bodies[at].Heading;
            if (frame.Current != caster || frame.State is not (CombatState.Facing or CombatState.Action or CombatState.Cast or CombatState.Result))
            {
                if (!_facing.ContainsKey(caster))
                    a.Stood = recorded;
                continue;
            }
            var stood = a.Stood ?? recorded;
            _facing[caster] = Facing.Turn(_facing.GetValueOrDefault(caster, stood), CastFacing(a, frame, caster, stood), turn);
            _facingNow.Add(caster);
        }
        // Each caster so turned is drawn that way; one whose cast is over turns back to the recording's way, till it's there.
        foreach (var caster in _facing.Keys.ToList())
        {
            var at = bodies.FindIndex(b => b.Combatant == caster);
            if (at < 0)
            {
                _facing.Remove(caster);
                continue;
            }
            if (!_facingNow.Contains(caster))
            {
                var back = Facing.Turn(_facing[caster], bodies[at].Heading, turn);
                if (Facing.Arrived(back, bodies[at].Heading))
                {
                    _facing.Remove(caster);
                    continue;
                }
                _facing[caster] = back;
            }
            bodies[at] = bodies[at] with { Heading = _facing[caster] };
        }
    }

    /// <summary>The casters drawn turned to their targets' middle (<see cref="FaceCasts"/>), each the way it's drawn facing; and those in a cast this tick.</summary>
    private readonly Dictionary<Combatant, Vector2> _facing = [];

    private readonly HashSet<Combatant> _facingNow = [];

    /// <summary>
    /// The way the caster of <paramref name="a"/>, a spell cast at several targets, faces through its cast (<see cref="FaceCasts"/>):
    /// the middle of the arc the targets spread over about it, as <paramref name="frame"/> has them; <paramref name="stood"/> where
    /// that's wider than <see cref="StartEarlyPacing.CastArc"/>, or it has no target but itself.
    /// </summary>
    private Vector2 CastFacing(DrawnAction a, BattleFrame frame, Combatant caster, Vector2 stood)
    {
        Vector3? me = null;
        foreach (var b in frame.Bodies)
        {
            if (b.Combatant == caster)
                me = b.Centre;
        }
        if (me is not { } from)
            return stood;
        var ways = new List<float>();
        foreach (var part in a.Parts)
        {
            if (part.Target is not { } target || target == caster)
                continue;
            foreach (var b in frame.Bodies)
            {
                if (b.Combatant == target && new Vector2(b.Centre.X - from.X, b.Centre.Z - from.Z) is var way && way.LengthSquared() > 1e-6f)
                    ways.Add(MathF.Atan2(way.Y, way.X));
            }
        }
        if (ways.Count == 0)
            return stood;
        ways.Sort();
        // The arc they spread over: all the way round less the widest gap between two neighbours.
        var (gap, after) = (MathF.Tau - (ways[^1] - ways[0]), 0);
        for (var i = 1; i < ways.Count; i++)
        {
            if (ways[i] - ways[i - 1] > gap)
                (gap, after) = (ways[i] - ways[i - 1], i);
        }
        var arc = MathF.Tau - gap;
        if (arc > Pacing.CastArc * MathF.PI / 180 + 1e-4f)
            return stood;
        var middle = ways[after] + arc / 2;
        return new Vector2(MathF.Cos(middle), MathF.Sin(middle));
    }

    /// <summary>
    /// The tick a body would be drawn from on drawn tick <paramref name="d"/>, the player's turn <paramref name="live"/> opened now,
    /// from <paramref name="cursor"/>, where it's drawn on the drawn tick before, by <see cref="Move"/>'s rules: with the episode of
    /// a started action it's in; between them a tick on, no further than its next episode or the turn's tick; and on the present once
    /// it has nothing left to draw before the turn.
    /// </summary>
    private int Projected(Combatant body, DrawnAction live, int d, int cursor, ref int next, IReadOnlyList<Episode> episodes)
    {
        var last = _actions.Last;
        var cap = Math.Min(last, live.First + (d - _drawn));
        var to = -1;
        while (next < episodes.Count)
        {
            var e = episodes[next];
            if (e.Action == live)
                break;
            if (e.Action.Start is { } s && s <= d && e.Action.Decided(e.Part))
            {
                var at = e.Action.TickAt(d, e.Part);
                if (at > e.To)
                {
                    to = Math.Max(to, Math.Min(e.To, last));
                    next++;
                    continue;
                }
                if (at >= e.From)
                {
                    to = Math.Min(at, last);
                    break;
                }
            }
            cap = Math.Min(cap, e.From - 1);
            break;
        }
        if (to < 0 || next >= episodes.Count || to < episodes[next].From)
        {
            var free = next >= episodes.Count || episodes[next].Action == live;
            to = Math.Max(to, free ? last : Math.Min(cursor + 1, cap));
        }
        return Math.Max(cursor, to);
    }

    /// <summary>
    /// How far a point <paramref name="e"/> is, across the ground, from everywhere within <paramref name="reach"/> of
    /// <paramref name="p"/> that's also within <paramref name="radius"/> of <paramref name="centre"/>: a body's reach kept in its field.
    /// </summary>
    internal static float Gap(Vector2 e, Vector2 p, float reach, Vector2 centre, float radius)
    {
        var (dp, dc) = (Vector2.Distance(e, p), Vector2.Distance(e, centre));
        if (dp <= reach && dc <= radius)
            return 0;
        var best = float.PositiveInfinity;
        // The nearest point of either disc, if it's in the other.
        var q = dp <= reach ? e : p + (e - p) / dp * reach;
        if (Vector2.Distance(q, centre) <= radius + 1e-4f)
            best = Vector2.Distance(e, q);
        var r = dc <= radius ? e : centre + (e - centre) / dc * radius;
        if (Vector2.Distance(r, p) <= reach + 1e-4f)
            best = MathF.Min(best, Vector2.Distance(e, r));
        // Else the corners where their edges cross.
        var apart = Vector2.Distance(p, centre);
        if (apart > 1e-6f && apart <= reach + radius && apart >= MathF.Abs(reach - radius))
        {
            var along = (reach * reach - radius * radius + apart * apart) / (2 * apart);
            var half = MathF.Sqrt(MathF.Max(0, reach * reach - along * along));
            var way = (centre - p) / apart;
            var mid = p + way * along;
            var side = new Vector2(-way.Y, way.X) * half;
            best = MathF.Min(best, MathF.Min(Vector2.Distance(e, mid + side), Vector2.Distance(e, mid - side)));
        }
        // A reach that misses the field altogether (it can't: the body stands in it) is the reach alone.
        return float.IsPositiveInfinity(best) ? MathF.Max(0, dp - reach) : best;
    }

    /// <summary>
    /// The turn the player plays that's waiting to open (Start Early): the battle waits on the player in it, and the drawing holds it
    /// till its combatant is free, the results before it have played and no body walking in the drawing is within its reach. Null if
    /// none waits.
    /// </summary>
    public DrawnAction? Pending =>
        _unscheduled < _actions.Actions.Count && _actions.Actions[_unscheduled] is var b && !Settled(b) && PlayersTurn(b) ? b : null;

    /// <summary>
    /// A while the player's turn waits to open (Start Early): whatever holds it is fast-forwarded, each holder to the end of its part
    /// and only that: its combatant's earlier parts, every beat before it that's yet to play or can't share its shot, and each body
    /// walking within its reach. Whether anything held it.
    /// </summary>
    public bool SkipHold()
    {
        if (Pending is not { } b)
            return false;
        var any = false;
        for (var pass = 0; pass < 4; pass++)
        {
            var moved = false;
            if (b.Current is { } c && !Free(c))
                moved |= Release(c);
            foreach (var e in b.Episodes)
            {
                if (!Free(e.Body))
                    moved |= Release(e.Body);
            }
            var beats = new List<(DrawnAction Action, Beat Beat)>();
            if (!BeatsBefore(b, beats))
            {
                foreach (var (a, beat) in beats)
                {
                    if (a.Start is null)
                        continue;
                    var was = a.Base(_drawn);
                    Forward(a, a.First + a.Offset(Shown(beat), beat.Part) + 1);
                    moved |= a.Base(_drawn) > was;
                }
            }
            var walkers = new List<Combatant>();
            if (!Walkers(b, walkers))
            {
                foreach (var body in walkers)
                    moved |= Release(body);
            }
            any |= moved;
            if (!moved || Started(b) && Reached(b) && BeatsBefore(b, null) && Walkers(b, null))
                break;
        }
        return any;
    }

    /// <summary>
    /// The beats playing on the drawn tick now, each as a shot would hold it, for the director (piece C): those of every action drawing
    /// whose timeline is between a beat's strike and the end of its showing (its end, or its outcome hold's).
    /// </summary>
    public IEnumerable<FramedBeat> PlayingNow()
    {
        foreach (var a in DrawingNow())
        {
            foreach (var beat in a.Beats)
            {
                var at = a.TickAt(_drawn, beat.Part);
                if (a.Decided(beat.Part) && at >= beat.From && at <= Shown(beat) && Framed(beat, at) is { } framed)
                    yield return framed;
            }
        }
    }

    /// <summary>
    /// Where <paramref name="body"/> is drawn at the end of the episode it's drawn in now, as far as it's recorded (Start Early's director
    /// holds where a walk ends from its start), or null if it's in none.
    /// </summary>
    public Vector3? EpisodeEnd(Combatant body)
    {
        foreach (var a in DrawingNow())
        {
            foreach (var e in a.Episodes)
            {
                var at = a.TickAt(_drawn, e.Part);
                if (e.Body == body && a.Decided(e.Part) && e.From <= at && at <= e.To)
                    return CentreAt(body, Math.Min(e.To, _actions.Last));
            }
        }
        return null;
    }

    /// <summary>
    /// The beats to come of every action scheduled (Start Early's director plans ahead from them): those that strike after the drawn tick
    /// now and no more than <paramref name="horizon"/> drawn ticks after it, each as a shot would hold it as it strikes, with how many drawn
    /// ticks off it strikes. Every one is recorded already; an action not yet scheduled has none here.
    /// </summary>
    public IEnumerable<(FramedBeat Beat, int In)> Coming(int horizon)
    {
        for (var i = _live; i < _actions.Actions.Count; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start is null)
                continue;
            foreach (var beat in a.Beats)
            {
                if (!a.Decided(beat.Part))
                    continue;
                var strike = a.DrawnAt(beat.From, beat.Part);
                if (strike > _drawn && strike <= _drawn + horizon && Framed(beat, beat.From) is { } framed)
                    yield return (framed, strike - _drawn);
            }
        }
    }

    /// <summary>How long a drawn tick may spend searching for actions' starts.</summary>
    public static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(2);

    /// <summary>Whether all of an action's ticks are known, and all its episodes' that could carry on past it.</summary>
    private bool Settled(DrawnAction b) =>
        b.Closed && (_actions.Last >= b.Last + BattleActions.TailCap || _waits());

    /// <summary>
    /// Whether any action is due to start, or has ticks before <paramref name="b"/>'s not yet drawn on an earlier drawn tick: its own, or its tail's (what it began
    /// that lives into <paramref name="b"/>'s ticks is drawn alongside it).
    /// </summary>
    private bool Busy(DrawnAction b)
    {
        for (var i = _live; i < _unscheduled; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start > _drawn)
                return true;
            foreach (var p in a.Timelines)
            {
                if (a.TickAt(_drawn, p) <= Math.Min(a.EndOf(p) ?? int.MaxValue, b.First - 1))
                    return true;
            }
        }
        return false;
    }

    /// <summary>A search for an action's start, which may go on over several drawn ticks.</summary>
    private sealed class Searching(DrawnAction action, int start, int window, List<Placed>[] mine, List<Pair> pairs,
        List<(Beat Beat, int From, int To)> beats)
    {
        public DrawnAction Action { get; } = action;
        public int Start { get; } = start;
        public int Window { get; } = window;
        public List<Placed>[] Mine { get; } = mine;
        public List<Pair> Pairs { get; } = pairs;

        /// <summary>The earlier actions' beats that end on the search's first drawn tick or after, by the drawn ticks they play on.</summary>
        public List<(Beat Beat, int From, int To)> Beats { get; } = beats;

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
                    var end = s + earlier.Action.Offset(earlier.To, earlier.Part);
                    start = Math.Max(start, end + 1 - b.Offset(e.From, e.Part));
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
            var beats = new List<(Beat, int, int)>();
            for (var i = _live; i < b.Index; i++)
            {
                var a = _actions.Actions[i];
                foreach (var beat in a.Beats)
                {
                    var (from, to) = (a.DrawnAt(beat.From, beat.Part), a.DrawnAt(Shown(beat), beat.Part));
                    if (to >= start || from + Pacing.OutcomeHold > start)
                        beats.Add((beat, from, to));
                }
            }
            _search = new Searching(b, start, window, mine, Pairs(theirs, mine), beats);
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
        if (!BeatsClear(search, search.Start + delay))
            return false;
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
    /// Whether the action searched for, starting on drawn tick <paramref name="start"/>, strikes no beat where one shot can't hold it
    /// with the earlier actions' beats playing then: on the drawn tick each of its beats would overlap one of theirs, every beat
    /// playing then, its own and theirs, is put to <see cref="Framing"/>.
    /// </summary>
    private bool BeatsClear(Searching search, int start)
    {
        var b = search.Action;
        if (b.Beats.Count == 0 || search.Beats.Count == 0)
            return true;
        var last = _actions.Last;
        var hold = Pacing.OutcomeHold;
        foreach (var mine in b.Beats)
        {
            var (from, to) = (start + b.Offset(mine.From, mine.Part), start + b.Offset(Shown(mine), mine.Part));
            foreach (var (theirs, theirFrom, theirTo) in search.Beats)
            {
                // Each result is held on the screen a moment before another action's strikes (the outcome hold), either way round.
                if (Math.Abs(from - theirFrom) < hold)
                    return false;
                if (theirTo < from || theirFrom > to)
                    continue;
                var d = Math.Max(from, theirFrom);
                var playing = new List<FramedBeat>();
                foreach (var m in b.Beats)
                {
                    var mf = start + b.Offset(m.From, m.Part);
                    if (mf <= d && d <= start + b.Offset(Shown(m), m.Part) && Framed(m, m.From + (d - mf)) is { } f)
                        playing.Add(f);
                }
                foreach (var (o, of, ot) in search.Beats)
                {
                    if (of <= d && d <= ot && Framed(o, o.From + (d - of)) is { } f)
                        playing.Add(f);
                }
                if (!Framing.CanHold(d, playing, null))
                    return false;
            }
        }
        return true;
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

    /// <summary>The last drawn tick an earlier action draws on, or one of its beats plays to.</summary>
    private int Window(DrawnAction b)
    {
        var end = int.MinValue;
        for (var i = _live; i < b.Index; i++)
        {
            var a = _actions.Actions[i];
            end = Math.Max(end, a.Start!.Value + (a.EndOffset ?? a.Offset(_actions.Last, 0)));
            foreach (var beat in a.Beats)
                end = Math.Max(end, a.DrawnAt(beat.End(_actions.Last), beat.Part));
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
            var t = a.Base(d);
            if (t < a.First || t < _actions.Kept)
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
        if (t < _actions.Kept)
            return list;
        var last = _actions.Last;
        foreach (var e in b.Episodes)
        {
            // Each timeline has its own tick k drawn ticks in (a later part's ahead of the first's).
            var (from, to) = (b.Offset(e.From, e.Part), b.Offset(e.To, e.Part));
            var at = from <= k && k <= to ? b.At(t, e.Part) : to < k && LastBefore(b, e, k) ? Math.Min(e.To + 1, last) : -1;
            if (at >= 0 && at <= last && Centre(e.Body, at) is { } centre)
                list.Add(new Placed(e.Body, new Shape(centre, centre, e.Body.Body.Sphere.Radius), at, true));
        }
        AddThings(b, t, list);
        return list;

        // Whether an episode drawn before k is the body's last in b drawn by then, and it's in none of b's at k.
        static bool LastBefore(DrawnAction b, Episode episode, int k)
        {
            Episode? latest = null;
            var latestTo = int.MinValue;
            foreach (var e in b.Episodes)
            {
                if (e.Body != episode.Body)
                    continue;
                var (from, to) = (b.Offset(e.From, e.Part), b.Offset(e.To, e.Part));
                if (from <= k && k <= to)
                    return false;
                if (to < k && to > latestTo)
                    (latest, latestTo) = (e, to);
            }
            return latest == episode;
        }
    }

    /// <summary>An action's missiles and spells' actors living at base tick <paramref name="t"/>, each at its part's tick then.</summary>
    private void AddThings(DrawnAction a, int t, List<Placed> list)
    {
        foreach (var (thing, ended) in a.Things)
        {
            if (thing is not (MissileFlight or SpellFxVisual))
                continue;
            var part = a.PartOf(thing);
            var at = a.At(t, part);
            if (!a.Decided(part) || ended < at || at > _actions.Last || at < _actions.Kept)
                continue;
            if (ShapeOf(thing, at) is { } shape)
                list.Add(new Placed(thing, shape, at, false));
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
            else if (a.Start is { } scheduled && a.Decided(e.Part))
                s = scheduled;
            else
                continue;
            var from = s + a.Offset(e.From, e.Part);
            var to = s + a.Offset(e.To, e.Part);
            if (d >= from && d <= to)
                return At(body, Math.Max(a.At(a.First + (d - s), e.Part), _actions.Kept), a == b);
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

    /// <summary>Where a body's centre is at tick <paramref name="t"/>, or null if it isn't on the field or the tick isn't kept.</summary>
    private Vector3? CentreAt(Combatant c, int t) => t < _actions.Kept || t > _actions.Last ? null : Centre(c, t);

    /// <summary>Whether a body is drawn freed, dead or fled and fading, on tick <paramref name="t"/>.</summary>
    private bool FreedAt(Combatant c, int t)
    {
        if (t < _actions.Kept || t > _actions.Last)
            return false;
        foreach (var b in _actions[t].Bodies)
        {
            if (b.Combatant == c)
                return (b.Standing & BodyState.Freed) != 0;
        }
        return false;
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
        var live = Live;
        if (live is not null)
            Bring(live);
        var last = _actions.Last;
        var newest = Newest();
        var newestTick = newest is null ? last : Math.Min(newest.TickAt(_drawn), last);

        for (var i = _live; i < _actions.Actions.Count; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start is not { } s || s > _drawn)
                continue;
            Decide(a);
            Advance(a, played);
        }
        foreach (var (body, track) in _tracks)
            Move(body, track, newestTick, live, played);
        Resolve();

        // Once the newest action reaches the present, everything is put there; but the player's turn draws on the present while the
        // earlier actions draw on behind it.
        if (newest is not null && newest != live && newest.Base(_drawn) >= last && _unscheduled == _actions.Actions.Count)
            Snap(last, played);
        var frame = Composed(played);
        Retire();
        return frame;
    }

    /// <summary>
    /// What the player's turn reaches on the present is drawn where it is first (Start Early): a body it involves (a target hit, one
    /// pushed) that still has an earlier part to draw has the actions holding it fast-forwarded to their ends. The turn opens only
    /// once no body walking in the drawing is within its reach, and its aim is held while what it could pick still draws behind
    /// (<see cref="BattleDrawing"/>), so this is seldom needed.
    /// </summary>
    private void Bring(DrawnAction live)
    {
        var touched = new List<Combatant>();
        foreach (var e in live.Episodes)
        {
            if (live.Decided(e.Part) && e.From <= Math.Min(live.TickAt(_drawn, e.Part), _actions.Last) && !Free(e.Body))
                touched.Add(e.Body);
        }
        foreach (var body in touched)
            Release(body);
    }

    /// <summary>
    /// The last word on a drawn tick (Start Early): two bodies drawn from different ticks that touch nearer than either tick
    /// recorded them, as a fast-forward of one action can leave them (a walk skipped to its end where a body drawn behind still stands),
    /// have the one drawn behind brought on (<see cref="Release"/>) till none do.
    /// </summary>
    private void Resolve()
    {
        for (var pass = 0; pass < 8; pass++)
        {
            var drawn = new List<(Combatant Body, int Tick, Vector3 At)>();
            foreach (var (body, track) in _tracks)
            {
                if (!track.Gone && track.Cursor >= _actions.Kept && Centre(body, Math.Min(track.Cursor, _actions.Last)) is { } at)
                    drawn.Add((body, Math.Min(track.Cursor, _actions.Last), at));
            }
            Combatant? behind = null;
            for (var i = 0; i < drawn.Count && behind is null; i++)
            {
                for (var j = i + 1; j < drawn.Count; j++)
                {
                    var (x, y) = (drawn[i], drawn[j]);
                    if (x.Tick == y.Tick)
                        continue;
                    var apart = Vector3.Distance(x.At, y.At);
                    if (apart >= x.Body.Body.Sphere.Radius + y.Body.Body.Sphere.Radius)
                        continue;
                    var then = Math.Min(Between(x.Body, y.Body, x.Tick), Between(x.Body, y.Body, y.Tick));
                    if (apart < then - Slack)
                    {
                        behind = x.Tick < y.Tick ? x.Body : y.Body;
                        break;
                    }
                }
            }
            if (behind is null || !Release(behind))
                return;
            Resolved++;
        }

        float Between(Combatant x, Combatant y, int t) =>
            Centre(x, t) is { } a && Centre(y, t) is { } b ? Vector3.Distance(a, b) : float.PositiveInfinity;
    }

    /// <summary>How many times the last word has brought a body on: never, if the scheduling and the holds did their part.</summary>
    public int Resolved { get; private set; }

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

    /// <summary>Plays each of an action's parts decided to the tick it has now, no further than the battle's present.</summary>
    private void Advance(DrawnAction a, List<Moment> played)
    {
        var last = _actions.Last;
        a.Played = Math.Max(a.Played, Math.Min(a.Base(_drawn), last));
        foreach (var part in a.Parts)
        {
            if (part.Shift is null)
                continue;
            var tick = Math.Min(a.TickAt(_drawn, part.Index), last);
            while (part.MomentsPlayed < part.Moments.Count && part.Moments[part.MomentsPlayed].Tick <= tick)
                played.Add(part.Moments[part.MomentsPlayed++]);
        }
    }

    /// <summary>
    /// Decides the shifts of an action's later parts (a spell cast at several targets), each once, in order: so far ahead of the first
    /// part that its result strikes on the drawn tick the first's does, no further than its distance from the action's first tick; for
    /// an action drawing, no further than lets its first tick be drawn now or later, nor than lets each body it involves finish its
    /// episodes in earlier actions first. A part whose strike isn't recorded yet waits, while its first tick is still to come, then
    /// takes the part before's shift, as one with no strike does.
    /// </summary>
    /// <remarks>
    /// An action not yet started has that last from its search (<see cref="Search"/>), which places it whole. One drawing already, the
    /// turn the player plays, has its later parts decided as they're recorded: the player's Dragon Flames at four bandits, cast in a
    /// turn opened while one of them still drew its own action, drew that bandit's hit over its walk (a body in two actions' episodes).
    /// </remarks>
    private void Decide(DrawnAction a)
    {
        if (a.Parts.Count < 2)
            return;
        var first = a.Parts[0];
        for (var k = 1; k < a.Parts.Count; k++)
        {
            var part = a.Parts[k];
            if (part.Shift is not null)
                continue;
            var most = part.First - a.First;
            int? want = part.Strike is { } strike && first.Strike is { } struck
                ? strike - struck
                : k + 1 < a.Parts.Count || a.Closed ? a.Parts[k - 1].Shift : null;
            if (a.Start is { } s && s <= _drawn)
            {
                // Its first tick drawn now at the soonest.
                var latest = s + (part.First - a.First) - _drawn;
                if (want is null && latest > 0)
                    return;
                most = Math.Min(most, latest);
                foreach (var e in a.Episodes)
                {
                    if (e.Part != k)
                        continue;
                    foreach (var earlier in _actions.EpisodesOf(e.Body))
                    {
                        if (earlier == e)
                            break;
                        if (earlier.Action != a && earlier.Action.Start is { } begun)
                            most = Math.Min(most, s + (e.From - a.First) - (begun + earlier.Action.Offset(earlier.To, earlier.Part) + 1));
                    }
                }
            }
            else if (want is null)
            {
                return;
            }
            part.Shift = Math.Max(0, Math.Min(want ?? 0, most));
        }
    }

    /// <summary>
    /// Moves a body's timeline: with the action it's in an episode of, else a tick on, no further than its next episode or the newest
    /// action. While the player's turn (<paramref name="live"/>) draws on the present, a body with nothing left to draw before it is
    /// drawn there too, its pose blending from where it idled.
    /// </summary>
    private void Move(Combatant body, Track track, int newestTick, DrawnAction? live, List<Moment> played)
    {
        var episodes = _actions.EpisodesOf(body);
        var cap = Math.Min(newestTick, _actions.Last);
        var to = -1;
        while (track.Episode < episodes.Count)
        {
            var e = episodes[track.Episode];
            if (e.Action.Start is { } s && s <= _drawn && e.Action.Decided(e.Part))
            {
                var at = e.Action.TickAt(_drawn, e.Part);
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
        {
            // Free of every action but the player's turn, it's drawn where the turn is, but a body fading out plays its fade: the turn's
            // own timeline jumps to a spell's last target as it goes to it, and took a target fading behind with it (the battle won on a
            // cast at several rats, Nov 11).
            var free = live is not null && (track.Episode >= episodes.Count || episodes[track.Episode].Action == live)
                && !FreedAt(body, track.Cursor);
            to = Math.Max(to, free ? cap : Math.Min(track.Cursor + 1, cap));
        }
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
            var at = s <= _drawn ? a.Base(_drawn) : a.First - 1;
            if (at < tick)
                a.Start = _drawn - (tick - a.First);
            Decide(a);
            Advance(a, played);
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
            if (a.Start > _drawn || a.EndOffset is not { } end || _drawn <= a.Start + end
                || a.Parts.Any(p => p.MomentsPlayed < p.Moments.Count)
                || a.Beats.Any(beat => a.TickAt(_drawn, beat.Part) <= beat.End(_actions.Last)))
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
            slowest = Math.Min(slowest, a.Start is { } s && s <= _drawn ? Math.Max(a.First, Math.Min(a.Base(_drawn), _actions.Last)) : a.First);
        }
        foreach (var track in _tracks.Values)
        {
            if (!track.Gone)
                slowest = Math.Min(slowest, track.Cursor);
        }
        return Math.Max(slowest, _actions.Kept);
    }

    private int Clamp(DrawnAction a, int part) => Math.Clamp(a.TickAt(_drawn, part), Math.Max(a.First, _actions.Kept), _actions.Last);

    /// <summary>The tick of an action's own the drawing has now: its timeline's, no further than its last.</summary>
    private int Own(DrawnAction a) => Math.Clamp(a.TickAt(_drawn), Math.Max(a.First, _actions.Kept), a.Closed ? a.Last : _actions.Last);

    /// <summary>
    /// The frame composed of the actions and bodies where they are now, with <paramref name="played"/>'s one-offs, and those a
    /// fast-forward passed over (<see cref="_skipped"/>): their cues handed over in the recording's order, their sounds and emitters skipped.
    /// </summary>
    private BattleFrame Composed(List<Moment> played)
    {
        var all = new List<(Moment Item, bool Skipped)>(played.Count + _skipped.Count);
        foreach (var m in _skipped)
            all.Add((m, true));
        foreach (var m in played)
            all.Add((m, false));
        _skipped.Clear();
        all.Sort((x, y) => x.Item.Seq.CompareTo(y.Item.Seq));
        var cues = new List<CombatCue>();
        var calls = new List<SoundCall>();
        var starts = new List<EmitterStart>();
        var skippedCalls = new List<SoundCall>();
        var skippedStarts = new List<EmitterStart>();
        foreach (var (item, skipped) in all)
        {
            switch (item.Payload)
            {
                case CombatCue cue:
                    cues.Add(cue);
                    // The line with the portrait its own tick had beside it, whose turn it was written in: taken from the newest
                    // action's tick, a bandit's line drawn in the player's turn stood by the player's portrait (the user's, Nov 11).
                    if (cue is LogLine line)
                    {
                        var portrait = item.Tick >= _actions.Kept && item.Tick <= _actions.Last ? _actions[item.Tick].Gui.LogPortrait ?? _logPortrait : _logPortrait;
                        (_log, _logPortrait, _logTick) = (line.Text, portrait, item.Tick);
                        if (_newestLog is not { } newestLine || item.Seq > newestLine.Seq)
                            _newestLog = (item.Seq, line.Text, portrait, item.Tick);
                    }
                    break;
                case SoundCall call:
                    (skipped ? skippedCalls : calls).Add(call);
                    break;
                case EmitterStart start:
                    (skipped ? skippedStarts : starts).Add(start);
                    break;
            }
        }

        // An older line drawn after a newer one (an enemy's, from an action drawing on behind the player's turn opened early) stands
        // while what wrote it draws; then the newest is back, with its portrait. It stood on through the player's turn, the box beside
        // an enemy's portrait rather than Sholeh's (the user's, Nov 11).
        if (_newestLog is { } latest && _logTick < latest.Tick && !DrawingNow().Any(a => a.First <= _logTick))
            (_log, _logPortrait, _logTick) = (latest.Text, latest.Portrait, latest.Tick);

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
        var where = new Dictionary<Combatant, (DrawnBody Body, BattleFrame Frame)>();
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
                where[body] = (drawn, frame);
                if (track.Jumped)
                    jumped.Add(body);
                break;
            }
            track.Jumped = false;
        }
        bodies.Sort((x, y) => x.Combatant.Slot.CompareTo(y.Combatant.Slot));
        FaceCasts(bodies);

        // Each number, missile and spell's actor from the action that began it; each lasting scene with its body.
        var missiles = new List<DrawnMissile>();
        var visuals = new List<DrawnVisual>();
        var numbers = new List<DrawnNumber>();
        for (var i = _live; i < _unscheduled; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start is not { } s || s > _drawn)
                continue;
            // Each from its part's tick.
            foreach (var part in a.Parts)
            {
                if (part.Shift is null)
                    continue;
                var p = part.Index;
                var frame = _actions[Clamp(a, p)];
                if (frame.Missile is { } m && _actions.BirthOf(m.Flight) == a && a.PartOf(m.Flight) == p)
                    missiles.Add(m);
                foreach (var v in frame.Visuals)
                {
                    if (v.Visual.IsActor && _actions.BirthOf(v.Visual) == a && a.PartOf(v.Visual) == p)
                        visuals.Add(v);
                }
                foreach (var n in frame.Numbers)
                {
                    if (_actions.BirthOf(n.Number) == a && a.PartOf(n.Number) == p)
                        numbers.Add(n);
                }
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
            SkippedSounds = skippedCalls,
            SkippedEmitters = skippedStarts,
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
    /// The battle screen (Start Early). Its frames, portraits and HP bars are those of one tick, the oldest any action drawing is at
    /// (<see cref="Oldest"/>), so they're laid out as one moment had them; over that, each combatant in play then has the skull, the
    /// frame's colour and the HP bar of the tick its own body is drawn from, and one drawn past its leaving its slot (dead, and
    /// its slot since taken) is under its skull with no bar. The frames an aim colours take the newest action's colours, the
    /// player's own aim's. The radar is the newest action's, its dots where the bodies are drawn, and the log's last line drawn stands
    /// with the portrait of its own tick.
    /// </summary>
    /// <remarks>
    /// It was the newest action's screen, with each slot's frame, portrait and bar swapped for those of the tick the slot's body was
    /// drawn from, places and all. The newest action is the furthest on, so its screen had deaths and reserves' joinings not drawn
    /// yet; a slot is one combatant's and then, once it's dead, the reserve's who takes it, and the two were told apart only by slot;
    /// and a portrait swapped in came with the place it had on its own tick, mid-slide or in a reserve's small frame. So a frame
    /// stood empty with its bar, reserves' portraits came and went, and they flickered through the enemies' turns (the user's, Nov 11).
    /// </remarks>
    private CombatGuiFrame Gui(BattleFrame lead, Dictionary<Combatant, (DrawnBody Body, BattleFrame Frame)> where)
    {
        var from = Oldest(lead);
        var g = from.Gui;
        // Who has a slot's portrait on the screen laid out, and the tick their own body is drawn from if it's another: null where
        // the slot is theirs on both; the frame, where they've left it there.
        (Combatant? Who, BattleFrame? Own, bool Left) Of(int slot)
        {
            if (Occupant(from, slot) is not { } who || !where.TryGetValue(who, out var at) || at.Frame == from)
                return (null, null, false);
            return (who, at.Frame, Occupant(at.Frame, slot) != who || at.Frame.Gui.Portraits.All(q => q.Slot != slot));
        }

        var frames = new List<(HudSprite, bool)>(g.Frames.Count);
        for (var i = 0; i < g.Frames.Count; i++)
        {
            var mine = g.Frames[i];
            if (i < g.FrameSlots.Count && g.FrameSlots[i] is { } slot)
            {
                var (who, own, left) = Of(slot);
                if (own is not null && !left && Sprite(own.Gui, slot) is { } theirs)
                    mine = (mine.Sprite with { Color = theirs.Sprite.Color }, mine.Party);
                // An aim's colours are the newest action's: the turn the player plays.
                if (lead != from && Occupant(lead, slot) == (who ?? Occupant(from, slot)) && Sprite(lead.Gui, slot) is { } aimed
                    && aimed.Sprite.Color != CombatGui.FrameColor && aimed.Sprite.Color != CombatGui.FledColor)
                    mine = (mine.Sprite with { Color = aimed.Sprite.Color }, mine.Party);
            }
            frames.Add(mine);
        }
        var portraits = new List<CombatGuiPortrait>(g.Portraits.Count);
        foreach (var p in g.Portraits)
        {
            var mine = p;
            if (p.Slot is { } slot)
            {
                var (_, own, left) = Of(slot);
                if (own is not null && left)
                    mine = p with { Skull = true, SkullAlpha = 255 };
                else if (own is not null)
                {
                    foreach (var q in own.Gui.Portraits)
                    {
                        if (q.Slot == slot)
                        {
                            mine = p with { Skull = q.Skull, SkullAlpha = q.SkullAlpha };
                            break;
                        }
                    }
                }
            }
            portraits.Add(mine);
        }
        var bars = new List<(HudFill, bool)>(g.HpBars.Count);
        var barSlots = new List<int>(g.HpSlots.Count);
        for (var i = 0; i < g.HpBars.Count; i++)
        {
            var slot = i < g.HpSlots.Count ? g.HpSlots[i] : -1;
            var mine = g.HpBars[i];
            var (_, own, left) = Of(slot);
            if (own is not null)
            {
                // The same fill of the slot's pair there, the black and then the colour; none where it has none (it's dead there).
                var nth = i > 0 && g.HpSlots[i - 1] == slot ? 1 : 0;
                var at = left ? -1 : IndexOf(own.Gui.HpSlots, slot);
                if (at < 0 || at + nth >= own.Gui.HpBars.Count)
                    continue;
                mine = own.Gui.HpBars[at + nth];
            }
            bars.Add(mine);
            barSlots.Add(slot);
        }
        var radar = lead.Gui.Radar is { } entries
            ? CombatRadar.Dots(entries, 0, 0, e => Occupant(lead, e.Slot) is { } who && where.TryGetValue(who, out var at) ? at.Body.Centre : e.Centre,
                Occupant(lead, entries.Owner) is { } owner && where.TryGetValue(owner, out var centre) ? centre.Body.Centre : null)
            : lead.Gui.RadarDots;
        return g with
        {
            Log = _log, LogPortrait = _logPortrait, RadarDots = radar, RadarRing = lead.Gui.RadarRing, Radar = lead.Gui.Radar, Frames = frames,
            Portraits = portraits, HpBars = bars, FrameSlots = g.FrameSlots, HpSlots = barSlots,
        };

        static (HudSprite Sprite, bool Party)? Sprite(CombatGuiFrame gui, int slot)
        {
            for (var j = 0; j < gui.FrameSlots.Count && j < gui.Frames.Count; j++)
            {
                if (gui.FrameSlots[j] == slot)
                    return gui.Frames[j];
            }
            return null;
        }

        static int IndexOf(IReadOnlyList<int> slots, int slot)
        {
            for (var j = 0; j < slots.Count; j++)
            {
                if (slots[j] == slot)
                    return j;
            }
            return -1;
        }
    }

    /// <summary>
    /// The frame of the oldest tick any action drawing is at, no later than <paramref name="lead"/>'s: what the battle screen is laid
    /// out by (<see cref="Gui"/>), so it shows nothing the drawing hasn't come to.
    /// </summary>
    private BattleFrame Oldest(BattleFrame lead)
    {
        var tick = lead.Tick;
        for (var i = _live; i < _unscheduled; i++)
        {
            var a = _actions.Actions[i];
            if (a.Start is { } s && s <= _drawn && Draws(a))
                tick = Math.Min(tick, Math.Clamp(a.Base(_drawn), Math.Max(a.First, _actions.Kept), _actions.Last));
        }
        return tick == lead.Tick || tick < _actions.Kept ? lead : _actions[tick];
    }

    /// <summary>Who has a slot on a recorded tick: the body on the field with it, one still standing before one freed.</summary>
    private static Combatant? Occupant(BattleFrame frame, int slot)
    {
        Combatant? found = null;
        foreach (var b in frame.Bodies)
        {
            if (b.Combatant.Slot != slot)
                continue;
            if ((b.Standing & BodyState.Freed) == 0)
                return b.Combatant;
            found ??= b.Combatant;
        }
        return found;
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
