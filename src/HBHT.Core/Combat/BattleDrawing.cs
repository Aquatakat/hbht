using System.Diagnostics;
using System.Numerics;
using HBHT.Core.Audio;
using HBHT.Core.Cameras;
using HBHT.Core.Chunks;
using HBHT.Core.Ui;
using HBHT.Core.Trek;

namespace HBHT.Core.Combat;

/// <summary>
/// Draws a battle from its recording (ADR 0040): it steps the battle, records each tick as a frame (<see cref="BattleRecorder"/>), and
/// draws one frame a tick, handing its one-offs to whatever presents the battle, playing its sounds, and moving its drawn camera, which
/// the arena's map sounds, weather and the views go by. The views read <see cref="Frame"/> alone.
/// </summary>
/// <remarks>
/// <para>Under Original each tick's frame is drawn on the tick it's made, and the battle plays as it always has.</para>
/// <para>
/// Under Start Early, while the battle doesn't wait on the player (<see cref="Battle.WaitsOnPlayer"/>), it's stepped ahead of the drawing,
/// as many ticks a tick as <see cref="Budget"/> allows, up to <see cref="MaxAhead"/> frames ahead. The recording is cut into actions, one
/// a turn (<see cref="BattleActions"/>), and each starts drawing as soon as it shares no body still drawing with an earlier one and no
/// body it draws comes within reach of one another action draws where the recording didn't have them so. Each drawn tick is composed
/// (<see cref="BattleComposer"/>): every body from the action it's involved in then, or idling on its own clock; the
/// numbers, missiles and spells' actors from the action that began them; the camera, the fade and the rest of the battle's own from the
/// newest action drawing; and every one-off once, as its timeline reaches it. Once the battle waits on the player it's stepped no further
/// till the drawing has caught up, so a turn the player plays opens, and the battle's end comes, only as the drawing reaches them. The
/// rules run tick for tick in Original's order with the same random numbers either way; only when each tick is drawn differs.
/// </para>
/// </remarks>
public sealed class BattleDrawing
{
    private readonly ICombatHost? _host;
    private readonly ISoundOutput? _audio;
    private readonly Dictionary<SoundHandle, SoundHandle> _playing = [];
    private readonly BattleComposer _composer;
    private int _stepped = -1;

    /// <param name="battle">The battle, begun with <paramref name="recorder"/> as its host.</param>
    /// <param name="host">What presents the battle's one-offs as they're drawn: the runtime, or none.</param>
    /// <param name="audio">Where the drawn sounds play: the game's audio, or none.</param>
    public BattleDrawing(Battle battle, BattleRecorder recorder, ICombatHost? host, ISoundOutput? audio)
    {
        Battle = battle;
        Recorder = recorder;
        _host = host;
        _audio = audio;
        // What Battle.Begin cued, the bodies made: drawn at once, before the first tick.
        var first = recorder.Sample(battle, BattleStep.Ran);
        _composer = new BattleComposer(first, () => Battle.WaitsOnPlayer);
        Draw(first);
    }

    /// <summary>
    /// Begins a battle drawn from its recording: <paramref name="begin"/> begins it with the recorder as its host, and the bodies' sounds go
    /// through the recorder from <paramref name="arena"/>'s keys (<see cref="AudioKeys.Output"/>, put back by <see cref="End"/>).
    /// </summary>
    public static BattleDrawing Begin(ZoneEngine arena, ICombatHost? host, Func<ICombatHost, Battle> begin)
    {
        var audio = arena.Keys.Output;
        var recorder = new BattleRecorder(audio) { Keys = arena.Keys };
        arena.Keys.Output = recorder;
        var battle = begin(recorder);
        battle.Host = recorder;
        return new BattleDrawing(battle, recorder, host, audio) { Arena = arena };
    }

    /// <summary>The battle has gone: the arena's bodies' sounds go to the game's audio again.</summary>
    public void End()
    {
        if (Arena is { } arena && arena.Keys.Output == Recorder && _audio is not null)
            arena.Keys.Output = _audio;
    }

    private ZoneEngine? Arena { get; init; }

    public Battle Battle { get; }

    public BattleRecorder Recorder { get; }

    /// <summary>What schedules and composes the battle's actions as they're drawn.</summary>
    public BattleComposer Composer => _composer;

    /// <summary>The recording cut into actions, with where the drawing has each.</summary>
    public BattleActions Actions => _composer.Actions;

    /// <summary>How its turns are drawn (<see cref="Battle.Turns"/>).</summary>
    public CombatTurns Turns
    {
        get => Battle.Turns;
        set => Battle.Turns = value;
    }

    /// <summary>The frame drawn now, which the views draw: the battle's own under Original, composed of its actions under Start Early.</summary>
    public BattleFrame Frame { get; private set; } = null!;

    /// <summary>The drawn ticks so far: the session's ticks of the battle.</summary>
    public int Drawn => _composer.Drawn;

    /// <summary>Called with each frame the battle records, as it's recorded.</summary>
    public Action<BattleFrame>? Recorded { get; set; }

    /// <summary>How many ticks the battle is ahead of the slowest part of the drawing.</summary>
    public int Ahead => _composer.Ahead;

    /// <summary>Whether the drawing has caught up with the battle: every action and every body drawn to its present.</summary>
    public bool CaughtUp => _composer.CaughtUp;

    /// <summary>The camera as it's drawn, which the arena (its weather and sounds) and the views go by.</summary>
    public FollowCamera Camera { get; } = new(default, System.Numerics.Vector2.UnitY);

    /// <summary>How long a tick may step the battle ahead under Start Early; none for no limit but <see cref="TicksAhead"/>.</summary>
    public TimeSpan? Budget { get; set; } = TimeSpan.FromMilliseconds(3);

    /// <summary>How many ticks a tick may step the battle ahead under Start Early, at most.</summary>
    public int TicksAhead { get; set; } = 600;

    /// <summary>How many frames the battle may be ahead of the drawing.</summary>
    public int MaxAhead { get; set; } = 60 * 60 * 5;

    /// <summary>The longest a drawn tick has taken to schedule and compose under Start Early, stepping the battle aside.</summary>
    public TimeSpan SlowestCompose { get; private set; }

    /// <summary>Whether an action drawn now is an AI's walk (state 5), which A only fast-forwards under Start Early.</summary>
    public bool Walking => _composer.Walking;

    /// <summary>
    /// One tick of the drawing: the battle steps as the setting says, and the next frame is drawn, then the arena's music hold, map sounds
    /// and weather step at the drawing's pace by its camera. Returns the drawn frame's step, but its end only once the drawing has caught
    /// up with the battle.
    /// </summary>
    /// <param name="skyDrawn">Whether the arena's sky is drawn, for its weather.</param>
    /// <param name="readInput">The player's controls, read only in a turn the player plays.</param>
    public BattleStep Tick(ZoneEngine arena, bool skyDrawn, Func<PlayerInput>? readInput)
    {
        BattleStep step;
        if (Turns == CombatTurns.Original && CaughtUp)
        {
            // Drawn as it's made, as the battle has always been.
            var frame = Step(arena, readInput);
            _composer.Settle(frame);
            Draw(frame);
            step = frame.Step;
        }
        else
        {
            if (Turns == CombatTurns.StartEarly && !Battle.WaitsOnPlayer)
                RunAhead(arena);
            else if (CaughtUp || Turns == CombatTurns.StartEarly && _composer.AtPresent)
                // The battle waits on the player: it steps with the drawing once the drawing has caught up, or under Start Early once
                // the player's turn is drawn up to the present (ADR 0040's stage 4).
                Step(arena, readInput);
            Touch();
            var clock = Stopwatch.StartNew();
            var next = _composer.Next();
            SlowestCompose = clock.Elapsed > SlowestCompose ? clock.Elapsed : SlowestCompose;
            Draw(next);
            step = Frame.Step switch
            {
                BattleStep.Ended when !CaughtUp => BattleStep.Ran,
                // A pause is returned once, as its tick is drawn.
                BattleStep.Paused when !CaughtUp || _stepped == Frame.Tick => BattleStep.Ran,
                var s => s,
            };
            _stepped = Frame.Tick;
        }
        // Bgm.Update, ProcessAudioBubbles and the weather, as the zone engine draws the arena.
        arena.Audio.HoldMusic = Frame.MusicHeld;
        arena.StepSounds(Camera.Aim);
        arena.StepWeather(skyDrawn, arena.Precipitation, Frame.FogLifted);
        return step;
    }

    /// <summary>Steps the battle ahead without drawing, till it waits on the player or the tick's budget is spent.</summary>
    private void RunAhead(ZoneEngine arena)
    {
        var clock = Budget is not null ? Stopwatch.StartNew() : null;
        for (var n = 0; n < TicksAhead && _composer.Actions.Last - _composer.Slowest() < MaxAhead && !Battle.WaitsOnPlayer; n++)
        {
            Step(arena, readInput: null);
            if (clock is not null && clock.Elapsed > Budget)
                break;
        }
    }

    private BattleFrame Step(ZoneEngine arena, Func<PlayerInput>? readInput)
    {
        Recorder.BeginTick();
        var step = Battle.Tick(arena, readInput);
        var frame = Recorder.Sample(Battle, step);
        _composer.Record(frame);
        Recorded?.Invoke(frame);
        return frame;
    }

    /// <summary>
    /// Whether the battle takes the player's presses now: the drawing has caught up with it, or under Start Early the player's turn is
    /// drawn up to the present (<see cref="BattleComposer.AtPresent"/>), though earlier actions may still draw behind it.
    /// </summary>
    public bool TakesPresses => CaughtUp || Turns == CombatTurns.StartEarly && _composer.AtPresent;

    /// <summary>
    /// A press, as the setting has it: Start fast-forwards the drawing to the battle first, whatever is drawing (the Party tab reads the
    /// live sheets), then goes to the battle; the rest go to the battle while it takes presses (<see cref="TakesPresses"/>), and what the
    /// player's turn then touches is fast-forwarded (<see cref="Touch"/>); otherwise A in an AI's walk fast-forwards the walks drawing
    /// (<see cref="BattleComposer.SkipWalks"/>), and the rest wait.
    /// </summary>
    public bool Press(MenuAction action)
    {
        if (action == MenuAction.Start && !CaughtUp)
        {
            FastForward();
            return Battle.Press(action);
        }
        if (TakesPresses)
        {
            var taken = Battle.Press(action);
            Touch();
            return taken;
        }
        if (action == MenuAction.Accept && Walking)
        {
            _composer.SkipWalks();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Under Start Early, what the player's turn touches that's still drawing is fast-forwarded first, so the player sees it as it is
    /// (ADR 0040's stage 4): the target the cursor is on while an attack, a throw or a spell is aimed and every target it selects, and
    /// while an area is placed every body in it. The menus touch nothing; the player's walk, its missile and its spell's actor coming near
    /// a body or missile drawn behind are the composer's (<see cref="BattleComposer"/>'s guard).
    /// </summary>
    private void Touch()
    {
        if (Turns != CombatTurns.StartEarly || _composer.Live is null)
            return;
        var battle = Battle;
        var set = battle.State switch
        {
            CombatState.TargetAttack or CombatState.TargetThrown => battle.TargetsA,
            CombatState.TargetSpell or CombatState.PlaceArea => battle.TargetsB,
            _ => null,
        };
        if (set is null)
            return;
        var touched = new List<Combatant>();
        foreach (var slot in set.SelectedSlots.Append(set.Cursor))
        {
            if (slot >= 0 && slot < battle.Slots.Count && battle.Slots[slot] is { } c)
                touched.Add(c);
        }
        if (battle.State == CombatState.PlaceArea && battle.Aim is { } area)
        {
            foreach (var c in battle.Combatants)
            {
                if (System.Numerics.Vector2.Distance(c.Cell, area.Centre) <= area.Radius + c.Body.ScaledRadius)
                    touched.Add(c);
            }
        }
        _composer.Release(touched);
    }

    /// <summary>
    /// Draws every action to the battle's present at once: the one-offs go to the host, so the views come and go as they would have, but
    /// the sounds and emitters don't start (a sound's stop still goes through).
    /// </summary>
    public void FastForward()
    {
        if (CaughtUp)
            return;
        Draw(_composer.FastForward());
    }


    private void Draw(BattleFrame frame)
    {
        Frame = frame;
        foreach (var cue in frame.Cues)
            _host?.Cue(cue);
        // What a fast-forward passed over first: a sound already started still stops, but none starts.
        Play(frame.SkippedSounds, heard: false);
        Play(frame.Sounds, heard: true);
        Camera.Position = frame.Camera.Position;
        Camera.Aim = frame.Camera.Aim;
        Camera.LookXZ = frame.Camera.LookXZ;
    }

    /// <summary>The bodies' sounds of a frame drawn, each handle the recorder gave mapped to the one the audio gave.</summary>
    private void Play(IReadOnlyList<SoundCall> calls, bool heard)
    {
        if (_audio is not { } audio)
            return;
        foreach (var call in calls)
        {
            switch (call)
            {
                case SoundCall.Start start when heard:
                    if (_playing.Count > 64)
                        Prune(audio);
                    _playing[start.Handle] = audio.Start(start.Sequence, start.Volume, start.Pan, start.Loop, start.DurationMs);
                    break;
                case SoundCall.Set set when heard && _playing.TryGetValue(set.Handle, out var h):
                    audio.Set(h, set.Volume, set.Pan);
                    break;
                case SoundCall.Duration d when _playing.TryGetValue(d.Handle, out var h):
                    audio.SetDuration(h, d.Ms);
                    break;
                case SoundCall.Stop stop when _playing.Remove(stop.Handle, out var h):
                    audio.Stop(h);
                    break;
                case SoundCall.Play play when heard:
                    audio.Play(play.Sound);
                    break;
            }
        }
    }

    /// <summary>Forgets the sounds that have played out.</summary>
    private void Prune(ISoundOutput audio)
    {
        foreach (var (mine, theirs) in _playing.ToList())
        {
            if (!audio.IsPlaying(theirs))
                _playing.Remove(mine);
        }
    }
}
