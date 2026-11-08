using HBHT.Core.Audio;
using HBHT.Plugins;

namespace HBHT.Core.Combat;

/// <summary>
/// What records a battle as it's drawn (Start Early): the battle's host, which keeps its one-offs for the frame in the making; the bodies'
/// sounds' output (<see cref="ISoundOutput"/>, set as the arena's <see cref="AudioKeys.Output"/>), which keeps their calls; what hears the
/// combatants' and the spells' visuals' actors start their emitters; and <see cref="Sample"/>, which makes a tick's frame of all of it
/// with everything the views show. <see cref="BattleDrawing"/> drives it.
/// </summary>
/// <remarks>
/// A sound it takes plays when its frame is drawn. Whether one plays on is answered from its own book in the battle's ticks
/// (<see cref="IsPlaying"/>), as <see cref="GameAudio"/>'s is in the game's, so it never waits on the drawing; nothing a battle steps reads
/// it back (the bodies' keys only start and stop).
/// </remarks>
public sealed class BattleRecorder : ICombatHost, ISoundOutput
{
    private readonly GameAudio? _audio;
    private readonly ISoundOutput _out;
    private List<CombatCue> _cues = [];
    private List<SoundCall> _sounds = [];
    private List<EmitterStart> _emitters = [];
    private readonly List<Combatant> _onField = [];
    private readonly Dictionary<SoundHandle, (double Out, double Cut)> _book = [];
    private readonly Dictionary<SoundHandle, Combatant> _owners = [];
    private int _tick;
    private int _handles;
    private System.Numerics.Vector3[] _ring = [];

    /// <param name="output">Where the sounds would have gone, whose volumes it reports: the game's audio, which books their lengths.</param>
    public BattleRecorder(ISoundOutput output)
    {
        _out = output;
        _audio = output as GameAudio;
    }

    /// <summary>
    /// The bodies' keys whose sounds it takes, which say whose body each sound is (<see cref="AudioKeys.Keying"/>), so a drawing that
    /// draws bodies from different ticks plays each body's sounds with it; none to leave every sound to its tick's action.
    /// </summary>
    public AudioKeys? Keys { get; set; }

    /// <summary>The combatant whose body's keys are being processed now, if any.</summary>
    private Combatant? Keying => Keys?.Keying is { } body ? _onField.FirstOrDefault(c => c.Audio == body) : null;

    /// <summary>The battle's ticks recorded.</summary>
    public int Ticks => _tick;

    /// <summary>The battle's clock in the sequencer's milliseconds: 1000/60 a tick, as <see cref="GameAudio"/>'s.</summary>
    private double Now => _tick * 1000.0 / 60;

    // ---- The one-offs ----

    public void Cue(CombatCue cue)
    {
        _cues.Add(cue);
        switch (cue)
        {
            case CombatantJoined { Combatant: var c }:
                if (!_onField.Contains(c))
                    _onField.Add(c);
                // Its actor's emitters start as it's made and on every state it enters, which the view starts its own from.
                c.Actor?.Starts = r => _emitters.Add(new EmitterStart(c, r));
                break;
            case CombatantGone { Combatant: var c }:
                _onField.Remove(c);
                c.Actor?.Starts = null;
                break;
            case SpellVisualStarted { Visual: { Actor: { } motion } visual }:
                // Those for its first request start as it's made, before the core asks it for its cast.
                motion.Starts = r => _emitters.Add(new EmitterStart(visual, r));
                motion.Made();
                break;
            case SpellVisualEnded { Visual.Actor: { } motion }:
                motion.Starts = null;
                break;
        }
    }

    // ---- The sounds ----

    public float SoundVolume => _out.SoundVolume;

    public float MusicVolume => _out.MusicVolume;

    public SoundHandle Start(RecordId sequence, byte volume, byte pan, bool loop, int durationMs = -1)
    {
        var length = _audio is { } audio ? audio.Length(sequence) : 0;
        if (length is not { } ms)
            return default;
        var handle = new SoundHandle(-2, ++_handles);
        _book[handle] = (loop ? double.PositiveInfinity : Now + ms, Cut(durationMs));
        var owner = Keying;
        if (owner is not null)
            _owners[handle] = owner;
        _sounds.Add(new SoundCall.Start(handle, sequence, volume, pan, loop, durationMs) { Owner = owner });
        return handle;
    }

    public bool IsPlaying(SoundHandle handle)
    {
        if (!_book.TryGetValue(handle, out var heard))
            return false;
        if (Now < Math.Min(heard.Out, heard.Cut))
            return true;
        _book.Remove(handle);
        return false;
    }

    public void Set(SoundHandle handle, byte volume, byte pan)
    {
        if (!handle.IsNone)
            _sounds.Add(new SoundCall.Set(handle, volume, pan) { Owner = Of(handle) });
    }

    public void SetDuration(SoundHandle handle, int ms)
    {
        if (handle.IsNone)
            return;
        if (_book.TryGetValue(handle, out var heard))
            _book[handle] = heard with { Cut = Cut(ms) };
        _sounds.Add(new SoundCall.Duration(handle, ms) { Owner = Of(handle) });
    }

    public void Stop(SoundHandle handle)
    {
        if (handle.IsNone)
            return;
        _book.Remove(handle);
        _owners.Remove(handle, out var owner);
        _sounds.Add(new SoundCall.Stop(handle) { Owner = owner });
    }

    public void Play(OneShot sound) => _sounds.Add(new SoundCall.Play(sound) { Owner = Keying });

    /// <summary>Whose body started the sound <paramref name="handle"/>, if a body's did.</summary>
    private Combatant? Of(SoundHandle handle) => _owners.GetValueOrDefault(handle);

    private double Cut(int ms) => ms < 0 ? double.PositiveInfinity : Now + ms + 1;

    // ---- The frame ----

    /// <summary>A tick of the battle begins: its clock moves on (call before <see cref="Battle.Tick"/>).</summary>
    public void BeginTick() => _tick++;

    /// <summary>
    /// The frame of the tick just stepped, with what <paramref name="battle"/> shows now and what it cued, sounded and started since the
    /// last frame; <paramref name="step"/> is what its tick returned.
    /// </summary>
    public BattleFrame Sample(Battle battle, BattleStep step)
    {
        var bodies = new DrawnBody[_onField.Count];
        var i = 0;
        foreach (var c in _onField.OrderBy(c => c.Slot))
        {
            var held = c.Held;
            var actor = c.Actor?.Machine;
            bodies[i++] = new DrawnBody(c, c.Body.Sphere.Position, c.Body.Heading, c.Body.Movement, c.Alpha, c.Tint.State, c.Unfogged,
                actor?.State ?? 0, actor?.Clock ?? 0, held[0], held[1], held[2], c.Sheet.Hp);
        }
        var camera = battle.Camera;
        var frame = new BattleFrame
        {
            Tick = _tick,
            Step = step,
            State = battle.State,
            Current = battle.Current,
            Bodies = bodies,
            Missile = battle.Flight is { } f ? new DrawnMissile(f, f.Drawn, f.Rotation, f.Scale) : null,
            Visuals = [.. battle.Fx.Visuals.Select(v => new DrawnVisual(v, v.Position, v.Showing, v.Alpha, v.Actor?.Machine.State ?? 0,
                v.Actor?.Machine.Clock ?? 0, v.Played, v.Struck))],
            Numbers = [.. battle.Numbers.Shown.Select(n => new DrawnNumber(n, n.Ticks))],
            Ring = Ring(battle.Ring.Points),
            RingShown = !battle.ActionActive,
            Aim = battle.Aim,
            Fade = battle.Fade.Brightness,
            MusicHeld = battle.MusicHeld,
            SkyDim = battle.Fx.Sky.Value,
            FogLifted = battle.FogLifted,
            Camera = new DrawnCamera(camera.Position, camera.Aim, camera.LookXZ),
            Gui = battle.Gui.Snapshot(battle),
            Menu = battle.Menu,
            Aura = battle.Aura,
            Cues = _cues,
            Sounds = _sounds,
            Emitters = _emitters,
            Field = battle.Current is { Controlled: false } player && player.Body.Field is var (centre, radius)
                ? new TurnField(centre, radius, GreatestTravel(player))
                : null,
        };
        _cues = [];
        _sounds = [];
        _emitters = [];
        return frame;
    }

    private readonly Dictionary<Combatant, float> _runs = [];

    /// <summary>
    /// The farthest a combatant's body can go in a tick (Start Early): the fastest tick of its actor's run, the most its root moves
    /// forward in a tick over the states the run request reaches, by its scale and its pace (the brisk pace plays it quicker). Under
    /// the original's movement a body moves by its root's ticks summed, a frame at a time; under Modern by their mean over a stride,
    /// which is never more: so the fastest tick bounds both. A body with no run to measure takes <paramref name="fallback"/>.
    /// </summary>
    public float GreatestTravel(Combatant c, float fallback = 0.15f)
    {
        if (!_runs.TryGetValue(c, out var run))
            _runs[c] = run = FastestRunTick(c);
        return (run > 0 ? run : fallback) * c.Body.Scale * MathF.Max(1, c.Body.Pace);
    }

    /// <summary>The most its actor's root moves forward in a tick over the states the run request reaches, or 0 for none.</summary>
    private static float FastestRunTick(Combatant c)
    {
        if (c.Actor is not { } motion)
            return 0;
        var actor = motion.Machine.Actor;
        var seen = new HashSet<int>();
        var todo = new Stack<int>();
        foreach (var state in actor.States)
        {
            foreach (var b in state.Branches)
            {
                if (b.Number == Movement.Body.Run)
                    todo.Push(b.State);
            }
        }
        var fastest = 0f;
        while (todo.Count > 0)
        {
            var s = todo.Pop();
            if (s < 0 || s >= actor.States.Count || !seen.Add(s))
                continue;
            if (motion.Clips.Clip(actor.States[s].Animation)?.Root is { Count: > 1 } root)
            {
                for (var i = 1; i < root.Count; i++)
                    fastest = MathF.Max(fastest, root[i].Z - root[i - 1].Z);
            }
            foreach (var b in actor.States[s].Branches)
            {
                if (b.Number is Movement.Body.Run or HBHT.Plugins.Records.Requests.Any)
                    todo.Push(b.State);
            }
        }
        return fastest;
    }

    /// <summary>The ring's points, the last frame's array while they haven't moved.</summary>
    private System.Numerics.Vector3[] Ring(ReadOnlySpan<System.Numerics.Vector3> points)
    {
        if (!points.SequenceEqual(_ring))
            _ring = points.ToArray();
        return _ring;
    }
}
