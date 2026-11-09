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
/// starts toward a hit early, under its acceleration's caps, and arrives as it lands. Should a beat about to strike still be off the shot
/// shown (<see cref="CameraTuning.BeatCutAhead"/>), it cuts to one holding it; never while the player walks.
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

    private readonly List<ShotSubject> _extra = [], _sources = [], _all = [], _strikes = [], _playing = [], _must = [];

    /// <summary>A shot cut to to hold the beats, kept while they play and it holds every beat held (<see cref="Reframe"/>).</summary>
    private Shot? _pin;

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

    /// <summary>The beats held this tick: playing (<c>In</c> 0 or less) or to come (ticks till it strikes).</summary>
    public IReadOnlyList<(FramedBeat Beat, int In)> Beats => _beats;

    /// <summary>F3's line.</summary>
    public string Status
    {
        get
        {
            var playing = _beats.Count(b => b.In <= 0);
            var lead = Lead is { } a ? $"action {a.Index}{(a.Current is { } c ? $" ({c.Sheet.Name})" : "")}" : "nothing";
            return Camera.Status.Replace("camera: enhanced", "camera: Start Early's director", StringComparison.Ordinal) + "\n" +
                string.Create(CultureInfo.InvariantCulture,
                    $"director: leading {lead}; beats {playing} playing, {_beats.Count - playing} coming; drawn back {Widened}, regrouped {Regrouped}, beat cuts {BeatCuts}");
        }
    }

    /// <summary>
    /// A drawn tick (Start Early): films <paramref name="frame"/>, the drawing composed, and returns the camera drawn.
    /// </summary>
    public DrawnCamera Step(BattleComposer composer, BattleFrame frame)
    {
        var t = Tuning;
        var actions = composer.Actions;
        var d = composer.Drawn;
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
        var tick = lead is null ? frame.Tick : Math.Clamp(lead.TickAt(d), Math.Max(lead.First, actions.Kept), actions.Last);
        var facts = actions[tick].Facts ?? SceneFacts.None;
        _scene.Begin(_battle.World, CameraScene.FieldOf(_battle), _battle.Origin, facts);
        foreach (var b in frame.Bodies)
            _scene.Add(new SceneBody(b.Combatant, b.Centre, b.Combatant.Body.ScaledRadius, b.Heading, b.Standing));

        // The beats playing, and those to come that the camera would take about as long as is left to reach.
        _beats.Clear();
        foreach (var f in composer.PlayingNow())
            _beats.Add((f, f.Beat.From - f.Tick));
        var aim = Camera.Motion?.Current.Aim;
        foreach (var (f, n) in composer.Coming(t.PlanMost))
        {
            if (n <= Ahead(f, aim))
                _beats.Add((f, n));
        }
        _extra.Clear();
        _sources.Clear();
        foreach (var (f, _) in _beats)
        {
            _extra.Add(BeatShots.Subject(f));
            if (f.Source is { } source && f.Beat.Source is { } who)
                _sources.Add(new ShotSubject(source, who.Body.ScaledRadius, t.SourceWeight));
        }

        if (Camera.Intent.Moved is { } moved)
            Camera.Intent.Moved = moved with { Ago = moved.Ago + 1 };
        Camera.Step(_scene, facts.Original, null);

        // A beat about to strike off the shot shown or hidden in it, or one playing off it: cut to a shot holding it, and every beat
        // playing if it can, and keep that till they've played (the pin). Never while the player walks: as their turn opens, before they
        // move, it may.
        _strikes.Clear();
        _playing.Clear();
        foreach (var (f, n) in _beats)
        {
            if (n >= 0 && n <= t.BeatCutAhead)
                _strikes.Add(BeatShots.Subject(f));
            else if (n < 0)
                _playing.Add(BeatShots.Subject(f));
        }
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
                Camera.Intent.Moved = (_strikes.Count > 0 ? "cut to a beat about to strike" : "cut to hold the beats playing", 0);
            }
        }
        FillIntent();
        var shown = Camera.Camera;
        return new DrawnCamera(shown.Position, shown.Aim, shown.LookXZ);
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
    /// The enhanced camera's goal, holding the beats besides (<see cref="EnhancedCamera.Reframe"/>): as it is if it holds them; else drawn
    /// back the same way round, aimed where it was, then at the middle of them all; else the shot holding them all nearest its way round,
    /// then the beats with whose turn it is.
    /// </summary>
    private Shot Reframe(Shot goal, IReadOnlyList<ShotSubject> held)
    {
        if (_extra.Count == 0 || Camera.Finder is not { } finder)
            return goal;
        var t = Tuning;
        var lens = t.Lens;
        if (Sees(goal, _extra))
            return goal;
        // A shot cut to for the beats, kept while it holds them: so they play out in one steady shot.
        if (_pin is { } pin)
        {
            if (Sees(pin, _extra))
                return pin;
            _pin = null;
        }
        _all.Clear();
        _all.AddRange(held);
        _all.AddRange(_extra);
        // The same way round, drawn back: aimed where it was (the character, the reticle), then at the middle of them all.
        var back = goal with { Distance = MathF.Max(goal.Distance, ShotFraming.DistanceToHold(_all, goal.Aim, goal.Yaw, goal.Pitch, lens)) };
        if (back.Distance <= t.BeatDistance && _shots.Sees(back, _all, finder.Sight))
        {
            Widened++;
            Moved("drawn back to hold the beats");
            return back;
        }
        var middle = finder.Framed(_all, goal.Yaw, goal.Pitch, t.BeatDistance);
        if (_shots.Sees(middle, _all, finder.Sight))
        {
            Widened++;
            Moved("drawn back to hold the beats");
            return middle;
        }
        _must.Clear();
        _must.AddRange(_extra);
        if (held.Count > 0)
            _must.Add(held[0]);
        if ((_shots.Find(_all, goal.Yaw, _sources, sight: finder.Sight) ?? _shots.Find(_must, goal.Yaw, _sources, sight: finder.Sight)
                ?? _shots.Find(_extra, goal.Yaw, _sources, sight: finder.Sight) ?? _shots.Find(_extra, goal.Yaw, _sources)) is { } group)
        {
            Regrouped++;
            Moved("a shot of its own to hold the beats");
            return group;
        }
        return middle;

        bool Sees(Shot shot, List<ShotSubject> subjects)
        {
            foreach (var s in subjects)
            {
                if (!ShotFraming.Holds(shot, s with { Radius = 0 }, lens) || finder.Sight.Hidden(shot.Eye, s))
                    return false;
            }
            return true;
        }
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

    /// <summary>F3's drawing of what it held: the beats, and whom it leads with.</summary>
    private void FillIntent()
    {
        var intent = Camera.Intent;
        intent.Beats.Clear();
        foreach (var (f, n) in _beats)
            intent.Beats.Add((BeatShots.Subject(f), f.Beat.Kind, n));
        intent.Director = Lead is { } a ? $"leading action {a.Index}{(a.Current is { } c ? $", {c.Sheet.Name}'s" : "")}" : "leading nothing";
    }
}
