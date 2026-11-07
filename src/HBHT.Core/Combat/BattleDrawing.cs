using System.Diagnostics;
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
/// as many ticks a tick as <see cref="Budget"/> allows, up to <see cref="MaxAhead"/> frames ahead, and the drawing plays the frames back
/// in order, one a tick. Once it waits on the player it's stepped no further till the drawing has caught up, so a turn the player plays
/// opens, and the battle's end comes, only as the drawing reaches them; from there it steps a tick a tick, as under Original. The rules
/// run tick for tick in Original's order with the same random numbers either way; only when each tick is drawn differs.
/// </para>
/// </remarks>
public sealed class BattleDrawing
{
    private readonly Queue<BattleFrame> _ahead = new();
    private readonly ICombatHost? _host;
    private readonly ISoundOutput? _audio;
    private readonly Dictionary<SoundHandle, SoundHandle> _playing = [];

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
        Draw(recorder.Sample(battle, BattleStep.Ran), sounds: true);
    }

    /// <summary>
    /// Begins a battle drawn from its recording: <paramref name="begin"/> begins it with the recorder as its host, and the bodies' sounds go
    /// through the recorder from <paramref name="arena"/>'s keys (<see cref="AudioKeys.Output"/>, put back by <see cref="End"/>).
    /// </summary>
    public static BattleDrawing Begin(ZoneEngine arena, ICombatHost? host, Func<ICombatHost, Battle> begin)
    {
        var audio = arena.Keys.Output;
        var recorder = new BattleRecorder(audio);
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

    /// <summary>How its turns are drawn (<see cref="Battle.Turns"/>).</summary>
    public CombatTurns Turns
    {
        get => Battle.Turns;
        set => Battle.Turns = value;
    }

    /// <summary>The frame drawn now, which the views draw.</summary>
    public BattleFrame Frame { get; private set; } = null!;

    /// <summary>The frames stepped and not yet drawn: how far the battle is ahead of the drawing.</summary>
    public int Ahead => _ahead.Count;

    /// <summary>Whether the drawing has caught up with the battle.</summary>
    public bool CaughtUp => _ahead.Count == 0;

    /// <summary>The camera as it's drawn, which the arena (its weather and sounds) and the views go by.</summary>
    public FollowCamera Camera { get; } = new(default, System.Numerics.Vector2.UnitY);

    /// <summary>How long a tick may step the battle ahead under Start Early; none for no limit but <see cref="TicksAhead"/>.</summary>
    public TimeSpan? Budget { get; set; } = TimeSpan.FromMilliseconds(3);

    /// <summary>How many ticks a tick may step the battle ahead under Start Early, at most.</summary>
    public int TicksAhead { get; set; } = 600;

    /// <summary>How many frames the battle may be ahead of the drawing.</summary>
    public int MaxAhead { get; set; } = 60 * 60 * 5;

    /// <summary>
    /// One tick of the drawing: the battle steps as the setting says, and the next frame is drawn, then the arena's music hold, map sounds
    /// and weather step at the drawing's pace by its camera. Returns the drawn frame's step, but its end only once the drawing has caught
    /// up with the battle.
    /// </summary>
    /// <param name="skyDrawn">Whether the arena's sky is drawn, for its weather.</param>
    /// <param name="readInput">The player's controls, read only in a turn the player plays.</param>
    public BattleStep Tick(ZoneEngine arena, bool skyDrawn, Func<PlayerInput>? readInput)
    {
        if (CaughtUp)
        {
            if (Turns == CombatTurns.Original || Battle.WaitsOnPlayer)
                Step(arena, readInput);
            else
            {
                RunAhead(arena);
            }
        }
        else if (Turns == CombatTurns.StartEarly && !Battle.WaitsOnPlayer)
        {
            RunAhead(arena);
        }
        if (_ahead.TryDequeue(out var frame))
            Draw(frame, sounds: true);
        // Bgm.Update, ProcessAudioBubbles and the weather, as the zone engine draws the arena.
        arena.Audio.HoldMusic = Frame.MusicHeld;
        arena.StepSounds(Camera.Aim);
        arena.StepWeather(skyDrawn, arena.Precipitation, Frame.FogLifted);
        return Frame.Step is BattleStep.Ended && !CaughtUp ? BattleStep.Ran : Frame.Step;
    }

    /// <summary>Steps the battle ahead without drawing, till it waits on the player or the tick's budget is spent.</summary>
    private void RunAhead(ZoneEngine arena)
    {
        var clock = Budget is not null ? Stopwatch.StartNew() : null;
        for (var n = 0; n < TicksAhead && _ahead.Count < MaxAhead && !Battle.WaitsOnPlayer; n++)
        {
            Step(arena, readInput: null);
            if (clock is not null && clock.Elapsed > Budget)
                break;
        }
    }

    private void Step(ZoneEngine arena, Func<PlayerInput>? readInput)
    {
        Recorder.BeginTick();
        var step = Battle.Tick(arena, readInput);
        _ahead.Enqueue(Recorder.Sample(Battle, step));
    }

    /// <summary>
    /// A press, as the setting has it: to the battle, while the drawing has caught up; otherwise Start fast-forwards the drawing to the battle
    /// first (the Party tab reads the live sheets) and then pauses it, A in an AI's walk only fast-forwards the drawing, and the rest wait.
    /// </summary>
    public bool Press(MenuAction action)
    {
        if (CaughtUp)
            return Battle.Press(action);
        switch (action)
        {
            case MenuAction.Start:
                FastForward();
                return Battle.Press(action);
            case MenuAction.Accept when Frame.Walking:
                FastForward();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Draws every frame the battle is ahead by at once, to the last: their one-offs go to the host, so the views come and go as they
    /// would have, but their sounds and emitters don't start (a sound's stop still goes through).
    /// </summary>
    public void FastForward()
    {
        while (_ahead.TryDequeue(out var frame))
            Draw(frame, sounds: false);
    }

    private void Draw(BattleFrame frame, bool sounds)
    {
        Frame = frame;
        foreach (var cue in frame.Cues)
            _host?.Cue(cue);
        Play(frame.Sounds, sounds);
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
