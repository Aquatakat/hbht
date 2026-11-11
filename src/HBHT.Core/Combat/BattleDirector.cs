using System.Globalization;
using System.Numerics;
using HBHT.Core.Cameras;

namespace HBHT.Core.Combat;

/// <summary>
/// Start Early's director (piece C, docs/systems/cameras.md, "Start Early's director"): Camera: Enhanced filming the drawing, the recording
/// played back with overlapping turns, rather than the live battle. Under Combat Turns: Start Early it's always the camera, whatever the
/// Camera setting says.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it films.</b> Each drawn tick it reads a scene of the drawing (<see cref="CameraScene"/>): every body where it's drawn, and the
/// rest (whose turn, its state, whom it acts on, its walk, its missile, its aim) from the tick of the action it leads with: the turn the
/// player plays while it's open, else the oldest action still drawing its own ticks. Camera: Enhanced (<see cref="EnhancedCamera"/>) films
/// that as it films a battle played turn by turn: the player's turn over the shoulder, an AI's turn one shot planned for it, its walk from
/// near where it ends, the loosing and the hit, the aims and the player's hand on the camera.
/// </para>
/// <para>
/// <b>Beats first.</b> Every beat playing (damage, a miss, a defeat, an effect added), and every beat to come that the camera would take
/// about as long as is left to reach, is held besides (<see cref="EnhancedCamera.Reframe"/>): the enhanced camera's shot drawn back the
/// same way round to hold them too, else the shot holding them all nearest its way round (<see cref="BeatShots.Find"/>). So the camera
/// starts toward a hit early, under its acceleration's caps, and arrives as it lands. A beat to come, once planned toward, stays planned
/// toward till it strikes, and a move toward one that would be a cut waits for the beat's own cut. Should a beat about to strike still be
/// off the shot shown (<see cref="CameraTuning.BeatCutAhead"/>), it cuts to one holding it; never while the player walks.
/// </para>
/// <para>
/// <b>The outcome hold.</b> A result that strikes on the screen is held there for <see cref="StartEarlyPacing.OutcomeHold"/>: the shot it
/// struck in is kept, and the camera's own cuts wait, before it goes on to the next turn.
/// </para>
/// <para>
/// <b>Walkers.</b> A body walking in the drawing outside the action led with is held besides where it can be, drawn back to, till
/// <see cref="CameraTuning.WalkerAfter"/> after it stops, so the player sees where it went.
/// </para>
/// <para>
/// <b>The shot question.</b> Whether one shot can hold beats that play at once is <see cref="Framing"/>'s to answer, which the composer
/// asks as it schedules (<see cref="BattleComposer.Framing"/>): the director frames what it said could be held.
/// </para>
/// <para>
/// <b>The stick.</b> In a turn the player plays, drawn on the present, its camera is handed to the battle (<see cref="CombatCamera.Hand"/>),
/// so the stick, the free cursor and a placed area are read against what the player sees (<see cref="BattleDrawing"/>).
/// </para>
/// </remarks>
public sealed class BattleDirector
{
    private readonly Battle _battle;
    private readonly CameraScene _scene = new();
    private readonly BeatShots _shots;

    /// <summary>The beats held this tick: playing (<c>In</c> 0 or less, ticks since it struck) or to come (ticks till it strikes).</summary>
    private readonly List<(FramedBeat Beat, int In)> _beats = [];

    private readonly List<ShotSubject> _extra = [], _walkers = [], _sources = [], _all = [], _strikes = [], _playing = [], _must = [];

    /// <summary>The beats to come planned toward, each held till it strikes, so whether it's held doesn't turn on where the camera is.</summary>
    private readonly HashSet<Beat> _planned = [], _stillComing = [];

    /// <summary>The beats seen to strike.</summary>
    private readonly HashSet<Beat> _struck = [];

    /// <summary>Each body's walk in the drawing: where it set off from, where it's drawn, and the drawn tick it last moved on.</summary>
    private readonly Dictionary<Combatant, (Vector3 From, Vector3 At, int Last, Vector3 Speed)> _walks = [];

    /// <summary>A shot cut to to hold the beats, kept while they play and it holds every beat held (<see cref="Reframe"/>).</summary>
    private Shot? _pin;

    /// <summary>A shot of its own taken to hold the beats, kept while it still holds them, so it doesn't swap with the goal it replaced.</summary>
    private Shot? _own;

    /// <summary>The outcome hold: the shot a result struck in, kept till the drawn tick given.</summary>
    private (Shot Shot, int Until)? _hold;

    /// <summary>The shot kept the moment before a result the shot shown sees strikes: chosen as that begins, and kept till it strikes.</summary>
    private Shot? _brace;

    /// <summary>How far the goal is drawn back for the walkers alone, kept while any walks (<see cref="Back"/>).</summary>
    private float? _back;

    private int _drawn;

    /// <param name="battle">The battle drawn: its arena, which the shots look across.</param>
    public BattleDirector(Battle battle)
    {
        _battle = battle;
        _shots = new BeatShots(battle.World, CameraScene.FieldOf(battle), Camera.Tuning);
        Camera.Reframe = Reframe;
    }

    /// <summary>The camera it films with: Camera: Enhanced, reading the drawing.</summary>
    public EnhancedCamera Camera { get; } = new();

    /// <summary>Whether one shot can hold beats that play at once: the composer's question (<see cref="IBeatFraming"/>).</summary>
    public BeatShots Framing => _shots;

    /// <summary>Every number it works by, Camera: Enhanced's among them.</summary>
    public CameraTuning Tuning
    {
        get => Camera.Tuning;
        set
        {
            Camera.Tuning = value;
            _shots.Tuning = value;
        }
    }

    /// <summary>The action it leads with this tick, or null for none.</summary>
    public DrawnAction? Lead { get; private set; }

    /// <summary>How many times it has cut to keep a beat about to strike in shot.</summary>
    public int BeatCuts { get; private set; }

    /// <summary>How many ticks the enhanced camera's shot was drawn back to hold beats, and how many another was taken.</summary>
    public int Widened { get; private set; }

    public int Regrouped { get; private set; }

    /// <summary>How many outcome holds it began, and how many drawn ticks it held.</summary>
    public int Holds { get; private set; }

    public int Held { get; private set; }

    /// <summary>Drawn ticks a walker's walk's end was held besides it.</summary>
    public int EndsHeld { get; private set; }

    /// <summary>The beats held this tick: playing (<c>In</c> 0 or less) or to come (ticks till it strikes).</summary>
    public IReadOnlyList<(FramedBeat Beat, int In)> Beats => _beats;

    /// <summary>What it made of the enhanced camera's goal this tick, in a word, for tests and F3.</summary>
    public string Framed { get; private set; } = "";

    /// <summary>The bodies walking in the drawing held besides this tick.</summary>
    public IReadOnlyList<ShotSubject> Walkers => _walkers;

    /// <summary>F3's line.</summary>
    public string Status
    {
        get
        {
            var playing = _beats.Count(b => b.In <= 0);
            var lead = Lead is { } a ? $"action {a.Index}{(a.Current is { } c ? $" ({c.Sheet.Name})" : "")}" : "nothing";
            var hold = _hold is { } h && h.Until >= _drawn ? $", holding the outcome {(h.Until - _drawn) / 60f:F1} s more" : "";
            return Camera.Status.Replace("camera: enhanced", "camera: Start Early's director", StringComparison.Ordinal) + "\n" +
                string.Create(CultureInfo.InvariantCulture,
                    $"director: leading {lead}; beats {playing} playing, {_beats.Count - playing} coming; walkers {_walkers.Count}{hold}; framed: {Framed}; drawn back {Widened}, regrouped {Regrouped}, beat cuts {BeatCuts}, holds {Holds}");
        }
    }

    /// <summary>
    /// A drawn tick (Start Early): films <paramref name="frame"/>, the drawing composed, and returns the camera drawn.
    /// </summary>
    public DrawnCamera Step(BattleComposer composer, BattleFrame frame)
    {
        var t = Tuning;
        var actions = composer.Actions;
        var d = _drawn = composer.Drawn;
        // The turn the player plays while it's open; else the oldest action still drawing its own ticks, whose results come first; else
        // the newest drawing its tail.
        var lead = composer.Live;
        if (lead is null)
        {
            foreach (var a in composer.DrawingNow())
            {
                if (a.TickAt(d) <= a.Last)
                {
                    lead = a;
                    break;
                }
                lead = a;
            }
        }
        Lead = lead;
        // A spell cast at several targets, their hits drawn at once: its hit is one shot of them all, not each target close in its turn.
        Camera.Together.Clear();
        if (lead is { Parts.Count: > 1 })
        {
            foreach (var part in lead.Parts)
            {
                if (part.Target is { } target && !Camera.Together.Contains(target))
                    Camera.Together.Add(target);
            }
        }
        var tick = lead is null ? frame.Tick : Math.Clamp(lead.TickAt(d), Math.Max(lead.First, actions.Kept), actions.Last);
        var facts = actions[tick].Facts ?? SceneFacts.None;
        _scene.Begin(_battle.World, CameraScene.FieldOf(_battle), _battle.Origin, facts);
        foreach (var b in frame.Bodies)
            _scene.Add(new SceneBody(b.Combatant, b.Centre, b.Combatant.Body.ScaledRadius, b.Heading, b.Standing));

        // The beats playing, and those to come that the camera would take about as long as is left to reach: once planned toward, held
        // till they strike, so that coming nearer doesn't let them go and send it back.
        _beats.Clear();
        var struckNow = new List<FramedBeat>();
        foreach (var f in composer.PlayingNow())
        {
            _beats.Add((f, f.Beat.From - f.Tick));
            if (_struck.Add(f.Beat))
                struckNow.Add(f);
        }
        var aim = Camera.Motion?.Current.Aim;
        _stillComing.Clear();
        // The result of the action led with, while the camera stages it (an AI's walk under way, a loosing shown), is the camera's to
        // come to: the walk's end frames whom it acts on, and the loosing gives way to the hit. Planned toward from the walk's wide
        // shot, it took a shot of its own on the target seconds before the throw left the hand (the user's, Nov 11).
        var staged = Camera.Staging ? lead : null;
        foreach (var (f, n) in composer.Coming(t.PlanMost))
        {
            _stillComing.Add(f.Beat);
            if (f.Beat.Action == staged && n > t.BeatCutAhead)
                continue;
            if (_planned.Contains(f.Beat) || n <= Ahead(f, aim))
            {
                _planned.Add(f.Beat);
                _beats.Add((f, n));
            }
        }
        _planned.IntersectWith(_stillComing);
        _extra.Clear();
        _sources.Clear();
        foreach (var (f, _) in _beats)
        {
            _extra.Add(BeatShots.Subject(f));
            if (f.Source is { } source && f.Beat.Source is { } who)
                _sources.Add(new ShotSubject(source, who.Body.ScaledRadius, t.SourceWeight));
        }
        if (_extra.Count == 0)
            _own = null;
        FindWalkers(composer, frame, lead, composer.Live, d);

        _strikes.Clear();
        _playing.Clear();
        foreach (var (f, n) in _beats)
        {
            if (n >= 0 && n <= t.BeatCutAhead)
                _strikes.Add(BeatShots.Subject(f));
            else if (n < 0)
                _playing.Add(BeatShots.Subject(f));
        }

        if (_hold is { } h && h.Until < d)
            _hold = null;
        // The outcome hold: a result that strikes on the screen, seen, keeps the shot it strikes in a moment, from this tick, so nothing
        // the camera would do this tick takes it away. Not in a turn the player has the stick in, which the composer opens only once the
        // results before it have been held.
        if (struckNow.Count > 0 && composer.Live is null && Camera.Motion is { } shown && Camera.Finder is { } sight)
        {
            if (struckNow.All(f => _shots.Sees(shown.Current, [BeatShots.Subject(f)], sight.Sight)))
            {
                // Where it's headed, if that holds them as well; else where it comes to rest, slowing from here; else where it is.
                // Kept where it was while it moved, it slid past the shot held and came back (the user's overshoot, Nov 11).
                var rest = shown.Rest;
                var keep = struckNow.All(f => _shots.Sees(Camera.Goal, [BeatShots.Subject(f)], sight.Sight)) ? Camera.Goal
                    : struckNow.All(f => _shots.Sees(rest, [BeatShots.Subject(f)], sight.Sight)) ? rest : shown.Current;
                _hold = (keep, Math.Max(d + composer.Pacing.OutcomeHold, _hold?.Until ?? 0));
                Holds++;
                Camera.Intent.Moved = ("holding the outcome", -1);
            }
        }
        // A result about to strike that the shot shown already sees: it's kept still till it strikes, so nothing takes it off the screen
        // in the moment before (the hold then follows).
        if (_hold is null && _strikes.Count > 0 && Camera.Motion is { } braced && Camera.Finder is { } braceSight
            && _shots.Sees(braced.Current, _strikes, braceSight.Sight))
        {
            // Chosen once and kept: where it was headed, if that sees them too, so a move under way carries on to its end; else where
            // it comes to rest, slowing from here; else where it is.
            _brace ??= _shots.Sees(Camera.Goal, _strikes, braceSight.Sight) ? Camera.Goal
                : braced.Rest is var resting && _shots.Sees(resting, _strikes, braceSight.Sight) ? resting : braced.Current;
        }
        else
        {
            _brace = null;
        }
        Camera.Holding = _hold is not null || _brace is not null;
        if (_hold is not null)
            Held++;

        if (Camera.Intent.Moved is { } moved)
            Camera.Intent.Moved = moved with { Ago = moved.Ago + 1 };
        Camera.Step(_scene, facts.Original, null);

        // A beat about to strike off the shot shown or hidden in it, or one playing off it: cut to a shot holding it, and every beat
        // playing if it can, and keep that till they've played (the pin). Never while the player walks: as their turn opens, before they
        // move, it may.
        if (_strikes.Count == 0 && _playing.Count == 0)
            _pin = null;
        var mayCut = !Camera.Released && (!Camera.Walking || Camera.Opening);
        if (mayCut && Camera.Motion is { } m && Camera.Finder is { } finder
            && (_strikes.Count > 0 && !_shots.Sees(m.Current, _strikes, finder.Sight) || !OnScreen(m.Current, _playing)))
        {
            _must.Clear();
            _must.AddRange(_strikes);
            _must.AddRange(_playing);
            var yaw = m.Current.Yaw;
            var first = _strikes.Count > 0 ? _strikes : _playing;
            if ((_shots.Find(_must, yaw, _sources, sight: finder.Sight) ?? _shots.Find(_must, yaw, _sources)
                    ?? _shots.Find(first, yaw, _sources, sight: finder.Sight) ?? _shots.Find(first, yaw, _sources)) is { } shot)
            {
                Camera.CutTo(shot);
                BeatCuts++;
                _pin = shot;
                // Cut to as it strikes, the result is held in the shot cut to.
                _hold = struckNow.Count > 0 && composer.Live is null ? (shot, d + composer.Pacing.OutcomeHold) : null;
                Holds += _hold is null ? 0 : 1;
                Camera.Intent.Moved = (_strikes.Count > 0 ? "cut to a beat about to strike" : "cut to hold the beats playing", 0);
            }
        }

        FillIntent();
        var drawn = Camera.Camera;
        return new DrawnCamera(drawn.Position, drawn.Aim, drawn.LookXZ);
    }

    /// <summary>
    /// The bodies walking in the drawing, outside the action led with and the turn the player plays: each held from when it has walked
    /// <see cref="CameraTuning.WalkerLeast"/> from where it set off till <see cref="CameraTuning.WalkerAfter"/> after it stops.
    /// </summary>
    private void FindWalkers(BattleComposer composer, BattleFrame frame, DrawnAction? lead, DrawnAction? live, int d)
    {
        var t = Tuning;
        _walkers.Clear();
        foreach (var b in frame.Bodies)
        {
            var c = b.Combatant;
            if (b.Alpha == 0 || (b.Standing & BodyState.Freed) != 0 && (b.Standing & BodyState.Dead) != 0)
            {
                _walks.Remove(c);
                continue;
            }
            if (!_walks.TryGetValue(c, out var w))
            {
                _walks[c] = (b.Centre, b.Centre, int.MinValue / 2, Vector3.Zero);
                continue;
            }
            var step = b.Centre - w.At;
            step.Y = 0;
            if (step.Length() > t.WalkerStep)
            {
                if (d - w.Last > t.WalkerAfter)
                    (w.From, w.Speed) = (w.At, Vector3.Zero);
                w.Last = d;
            }
            // Its speed across the ground, units a drawn tick, evened over its stride (the original's movement moves a body every other tick).
            w.Speed += (step - w.Speed) * MathF.Min(1, 1 / MathF.Max(t.WalkerSmoothing * 60, 1));
            w.At = b.Centre;
            _walks[c] = w;
            if (d - w.Last <= t.WalkerAfter && Across(w.At - w.From) >= t.WalkerLeast && c != lead?.Current && c != live?.Current)
            {
                // Where it is, and where its walk ends, the recording having it, so the camera draws back once rather than chasing it; with
                // that too far, where it will be a moment on, as the camera trails a goal that moves.
                var radius = c.Body.ScaledRadius;
                _walkers.Add(new ShotSubject(b.Centre, radius, t.WalkerWeight));
                if (composer.EpisodeEnd(c) is { } end && Across(end - b.Centre) is > 0.5f and var way && way <= t.WalkerEndReach)
                {
                    _walkers.Add(new ShotSubject(end, radius, t.WalkerWeight));
                    EndsHeld++;
                }
                else if (d - w.Last <= 2)
                    _walkers.Add(new ShotSubject(b.Centre + w.Speed * (t.WalkerLead * 60), radius, t.WalkerWeight));
            }
        }

        static float Across(Vector3 v) => new Vector2(v.X, v.Z).Length();
    }

    /// <summary>
    /// How many drawn ticks before a beat to come strikes it's held: as long as the aim takes to cross to it at its cap
    /// (<c>2√(d / a)</c>), by <see cref="CameraTuning.PlanSlack"/>, plus <see cref="CameraTuning.PlanMargin"/>, no more than
    /// <see cref="CameraTuning.PlanMost"/>.
    /// </summary>
    private int Ahead(FramedBeat f, Vector3? aim)
    {
        var t = Tuning;
        var way = aim is { } a ? new Vector2(f.Subject.X - a.X, f.Subject.Z - a.Z).Length() : 0;
        var seconds = t.PlanSlack * 2 * MathF.Sqrt(way / MathF.Max(t.AimAcceleration, 1e-3f));
        return Math.Min(t.PlanMost, (int)MathF.Ceiling(seconds * 60) + t.PlanMargin);
    }

    /// <summary>
    /// The enhanced camera's goal, holding the beats and the walkers besides (<see cref="EnhancedCamera.Reframe"/>): in an outcome hold, the
    /// shot held; else as it is if it holds them; else drawn back the same way round, aimed where it was, then at the middle of them all;
    /// else the shot holding them all nearest its way round, then the beats with whose turn it is. The walkers are let go before the beats
    /// are. With only beats to come and none about to strike, a goal so far off the shot shown that moving to it would be a cut isn't
    /// taken: the beat's own cut comes as it nears.
    /// </summary>
    private Shot Reframe(Shot goal, IReadOnlyList<ShotSubject> held)
    {
        // The outcome hold: the shot the result struck in, drawn back where that holds the walkers too.
        Framed = "goal";
        if (_brace is { } brace)
        {
            Framed = "brace";
            return brace;
        }
        if (_hold is { } hold)
        {
            Framed = "hold";
            return _walkers.Count == 0 || Camera.Finder is null ? hold.Shot : WithWalkers(hold.Shot, _playing);
        }
        if (_extra.Count == 0 && _walkers.Count == 0 || Camera.Finder is not { } finder)
        {
            _back = null;
            return goal;
        }
        // A shot cut to for the beats, kept while it holds them: so they play out in one steady shot. And a shot of its own, likewise.
        // Either is the director's, already shown or being moved to: the camera's own cut to a new shot of its own isn't made to it.
        if (_pin is { } pin)
        {
            if (Sees(pin, _extra))
            {
                Framed = "pin";
                Camera.Steady = true;
                return Kept(pin);
            }
            _pin = null;
        }
        if (_own is { } own)
        {
            if (_extra.Count > 0 && Sees(own, _extra))
            {
                Framed = "own kept";
                Camera.Steady = true;
                return Kept(own);
            }
            _own = null;
        }
        // Only walkers besides: the goal as it is, drawn back to hold each it can, and no nearer again while any still walks.
        if (_extra.Count == 0)
            return Back(goal, held);
        _back = null;
        var found = Hold(goal, held, _walkers.Count > 0) ?? (_walkers.Count > 0 && _extra.Count > 0 ? Hold(goal, held, false) : null);
        if (found is { } shot)
            return shot;
        return _extra.Count > 0 ? Own(goal, held, finder) : goal;

        Shot Kept(Shot shot) => _walkers.Count == 0 ? shot : WithWalkers(shot, _extra);
    }

    /// <summary>
    /// The goal with only walkers to hold besides: drawn back the same way round, aimed where it was, as far as holds each walker that
    /// one shot no farther than <see cref="CameraTuning.BeatDistance"/> can hold and see, and kept at least that far while any still
    /// walks. So the view widens once for them and stays, where it had gone between the goal, a shot drawn back and one aimed at the
    /// middle of them all as each walker came into reach, was hidden or stopped: the user's wobble as the bandits walked in Alaron's
    /// turn (Nov 11). It comes in again, under the caps, once none walks.
    /// </summary>
    private Shot Back(Shot goal, IReadOnlyList<ShotSubject> held)
    {
        var t = Tuning;
        var finder = Camera.Finder!;
        var distance = goal.Distance;
        foreach (var w in _walkers)
        {
            _all.Clear();
            _all.AddRange(held);
            _all.Add(w);
            var back = goal with { Distance = MathF.Max(goal.Distance, ShotFraming.DistanceToHold(_all, goal.Aim, goal.Yaw, goal.Pitch, t.Lens)) };
            _all.RemoveRange(0, _all.Count - 1);
            if (back.Distance <= t.BeatDistance && _shots.Sees(back, _all, finder.Sight))
                distance = MathF.Max(distance, back.Distance);
        }
        if (distance > (_back ?? goal.Distance) * 1.02f)
            Moved("drawn back to hold the walkers");
        _back = MathF.Max(distance, _back ?? 0);
        if (_back.Value <= goal.Distance * 1.001f)
        {
            Framed = "goal";
            return goal;
        }
        Framed = "back, walkers";
        Widened++;
        return goal with { Distance = _back.Value };
    }

    /// <summary>
    /// <paramref name="shot"/> drawn back the same way round, aimed where it is, to hold the walkers besides <paramref name="must"/>, if
    /// that's near enough and sees them all; else as it is.
    /// </summary>
    private Shot WithWalkers(Shot shot, List<ShotSubject> must)
    {
        if (Sees(shot, _walkers))
            return shot;
        _all.Clear();
        _all.AddRange(must);
        _all.AddRange(_walkers);
        var back = shot with { Distance = MathF.Max(shot.Distance, ShotFraming.DistanceToHold(_all, shot.Aim, shot.Yaw, shot.Pitch, Tuning.Lens)) };
        return back.Distance <= Tuning.BeatDistance && _shots.Sees(back, _all, Camera.Finder!.Sight) ? back : shot;
    }

    /// <summary>
    /// The goal holding what it must (the beats, and the walkers if <paramref name="walkers"/>): as it is; drawn back the same way round,
    /// aimed where it was; or aimed at the middle of them all. Null if none of those does.
    /// </summary>
    private Shot? Hold(Shot goal, IReadOnlyList<ShotSubject> held, bool walkers)
    {
        var t = Tuning;
        var finder = Camera.Finder!;
        _all.Clear();
        _all.AddRange(_extra);
        if (walkers)
            _all.AddRange(_walkers);
        if (_all.Count == 0)
            return goal;
        if (Sees(goal, _all))
        {
            Framed = walkers ? "goal holds all" : "goal holds the beats";
            return goal;
        }
        var must = _all.Count;
        _all.AddRange(held);
        var back = goal with { Distance = MathF.Max(goal.Distance, ShotFraming.DistanceToHold(_all, goal.Aim, goal.Yaw, goal.Pitch, t.Lens)) };
        if (back.Distance <= t.BeatDistance && _shots.Sees(back, _all, finder.Sight) && Reachable(back))
        {
            Framed = walkers ? "back, all" : "back, beats";
            Widened++;
            Moved(walkers ? "drawn back to hold the beats and walkers" : "drawn back to hold the beats");
            return back;
        }
        var middle = finder.Framed(_all, goal.Yaw, goal.Pitch, t.BeatDistance);
        if (_shots.Sees(middle, _all, finder.Sight) && Reachable(middle))
        {
            Framed = walkers ? "middle, all" : "middle, beats";
            Widened++;
            Moved(walkers ? "drawn back to hold the beats and walkers" : "drawn back to hold the beats");
            return middle;
        }
        _all.RemoveRange(must, _all.Count - must);
        Framed = back.Distance > t.BeatDistance ? $"too far ({back.Distance:F0})" : !_shots.Sees(back, _all, finder.Sight) ? "hidden" : "unreachable";
        return null;
    }

    /// <summary>A shot of its own holding the beats, nearest the goal's way round, kept while it holds them (<see cref="_own"/>).</summary>
    private Shot Own(Shot goal, IReadOnlyList<ShotSubject> held, ShotFinder finder)
    {
        _all.Clear();
        _all.AddRange(held);
        _all.AddRange(_extra);
        _must.Clear();
        _must.AddRange(_extra);
        if (held.Count > 0)
            _must.Add(held[0]);
        if ((_shots.Find(_all, goal.Yaw, _sources, sight: finder.Sight) ?? _shots.Find(_must, goal.Yaw, _sources, sight: finder.Sight)
                ?? _shots.Find(_extra, goal.Yaw, _sources, sight: finder.Sight) ?? _shots.Find(_extra, goal.Yaw, _sources)) is { } group
            && Reachable(group))
        {
            Regrouped++;
            Framed = "own";
            _own = group;
            Moved("a shot of its own to hold the beats");
            return group;
        }
        return goal;
    }

    /// <summary>
    /// Whether the camera may go to <paramref name="shot"/> for the beats held: always if one strikes within
    /// <see cref="CameraTuning.BeatCutAhead"/> or plays; with only beats to come, if moving there from the shot shown wouldn't be a cut.
    /// </summary>
    private bool Reachable(Shot shot)
    {
        if (_strikes.Count > 0 || _playing.Count > 0 || Camera.Motion is not { } m)
            return true;
        var t = Tuning;
        var now = m.Current;
        var turn = MathF.Max(now.Pitch, shot.Pitch) < ShotFinder.Rad(75) ? MathF.Abs(Shot.Wrap(shot.Yaw - now.Yaw)) : 0;
        return Vector3.Distance(shot.Aim, now.Aim) <= t.CutDistance && turn <= ShotFinder.Rad(t.CutTurn)
            && MathF.Abs(shot.Pitch - now.Pitch) <= ShotFinder.Rad(t.CutPitch);
    }

    /// <summary>Whether a shot holds every one of <paramref name="subjects"/> on the screen and sees it past the arena and the bodies.</summary>
    private bool Sees(Shot shot, List<ShotSubject> subjects)
    {
        var lens = Tuning.Lens;
        var finder = Camera.Finder!;
        foreach (var s in subjects)
        {
            if (!ShotFraming.Holds(shot, s with { Radius = 0 }, lens) || finder.Sight.Hidden(shot.Eye, s))
                return false;
        }
        return true;
    }

    /// <summary>Whether a shot holds every one of <paramref name="subjects"/> on the screen.</summary>
    private bool OnScreen(Shot shot, List<ShotSubject> subjects)
    {
        var lens = Tuning.Lens;
        foreach (var s in subjects)
        {
            if (!ShotFraming.Holds(shot, s with { Radius = 0 }, lens))
                return false;
        }
        return true;
    }

    /// <summary>Says why it moved, for F3, unless it said so this tick already.</summary>
    private void Moved(string why)
    {
        if (Camera.Intent.Moved is not { Ago: 0 })
            Camera.Intent.Moved = (why, 0);
    }

    /// <summary>F3's drawing of what it held: the beats, the walkers, and whom it leads with.</summary>
    private void FillIntent()
    {
        var intent = Camera.Intent;
        intent.Beats.Clear();
        foreach (var (f, n) in _beats)
            intent.Beats.Add((BeatShots.Subject(f), f.Beat.Kind, n));
        intent.Walkers.Clear();
        intent.Walkers.AddRange(_walkers);
        intent.Director = Lead is { } a ? $"leading action {a.Index}{(a.Current is { } c ? $", {c.Sheet.Name}'s" : "")}" : "leading nothing";
        if (_hold is { } h)
            intent.Director += string.Create(CultureInfo.InvariantCulture, $"; holding the outcome {(h.Until - _drawn) / 60f:F1} s");
    }
}
