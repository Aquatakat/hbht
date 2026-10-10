using Aidyn.Plugins.Records;

namespace Aidyn.Core.Audio;

/// <summary>A sequence <see cref="AudioEngine.Play"/> started. The default plays nothing.</summary>
public readonly record struct SoundHandle(int Player, int Stamp)
{
    public bool IsNone => Stamp == 0;
}

/// <summary>
/// The original's audio: its sequencer (<c>dcm.cpp</c>), 32 sequences at once, playing 32 voices on
/// libultra's synthesizer, mixed to stereo (roadmap 1D, docs/formats/audio.md).
/// </summary>
/// <remarks>
/// <para>
/// Both the sequencer (<c>Dcm.Step</c>, 1.0 <c>0x80090C94</c>) and the voice manager
/// (<c>Audio.VoiceUpdate</c>, <c>0x800900E4</c>) are synthesizer players called every 1000 µs of
/// audio, which at the original's 44,095 Hz is every 44 samples. This runs them on the same clock
/// at any output rate, so a sequence's timing doesn't depend on it. The voice manager applies the
/// sequencer's changes on its next call; this applies them at once, up to a millisecond sooner.
/// </para>
/// <para>
/// A voice plays raw 16-bit PCM at the rate its pitch gives in Hz, up to twice the output rate. Its
/// volume and pan go through libultra's envelope mixer: the volume is squared, the pan is equal
/// power, and both glide to new values over a millisecond. A stop is a hard cut. libultra's
/// four-tap resampler is approximated with cubic interpolation.
/// </para>
/// </remarks>
public sealed class AudioEngine
{
    /// <summary>The original's output rate: <c>osAiSetFrequency(44100)</c> on NTSC.</summary>
    public const int OriginalRate = 44095;

    /// <summary>Samples at <see cref="OriginalRate"/> per 1000 µs call (<c>_timeToSamplesNoRound</c>).</summary>
    private const int StepSamples = 44;

    public const int MaxPlayers = 32, MaxVoices = 32, MaxTracks = 32;

    /// <summary>A row every 20 calls, 125 in tracker tempo, until a tempo event (<c>Dcm.Play</c>).</summary>
    private const byte DefaultReload = 0x13;

    /// <summary>The highest pitch ratio libultra's resampler takes (<c>MAX_RATIO</c>).</summary>
    private const float MaxRatio = 1.99996f;

    /// <summary>Pitch events' bases by their top two bits, of which the original keeps the low 16 (<c>0x800D5284</c>).</summary>
    private static readonly int[] PitchBase = [0x0000, 0x4000, 0xC000, 0x1C000];

    /// <summary>libultra's equal-power pan table (<c>eqpower</c>, <c>0x800E5010</c>).</summary>
    private static readonly short[] EqPower = [.. Enumerable.Range(0, 128).Select(k => (short)Math.Round(32767 * Math.Cos(k * Math.PI / 2 / 127)))];

    private readonly Player[] _players = [.. Enumerable.Range(0, MaxPlayers).Select(_ => new Player())];
    private readonly int[] _freePlayers = [.. Enumerable.Range(0, MaxPlayers)];
    private int _playersInUse, _playerStamp;

    private readonly Voice[] _voices = [.. Enumerable.Range(0, MaxVoices).Select(_ => new Voice())];
    private readonly int[] _freeVoices = [.. Enumerable.Range(0, MaxVoices)];
    private int _voicesInUse, _voiceStamp;

    private readonly double _framesPerStep;
    private readonly int _rampFrames;
    private double _untilStep;

    public AudioEngine(int rate = OriginalRate)
    {
        Rate = rate;
        _framesPerStep = (double)rate * StepSamples / OriginalRate;
        _rampFrames = Math.Max(1, (int)Math.Round(_framesPerStep));
    }

    /// <summary>The output rate in Hz.</summary>
    public int Rate { get; }

    /// <summary>
    /// Starts <paramref name="sequence"/> (<c>Dcm.Play</c>, 1.0 <c>0x800907A4</c>), or plays
    /// nothing if 32 are playing.
    /// </summary>
    /// <param name="volume">Scales every track's volume by <c>volume / 16</c>.</param>
    /// <param name="pan">0x80 is the middle; towards either end the tracks' pans narrow towards it.</param>
    /// <param name="loop">Whether it starts again from its loop position when it ends, as music does.</param>
    /// <param name="durationMs">Stops it after this many milliseconds; -1 for no limit.</param>
    /// <param name="priority">Non-zero lets its notes take the oldest voice when all 32 are busy.</param>
    public SoundHandle Play(Sequence sequence, byte volume = 0xFF, byte pan = 0x80, bool loop = false, int durationMs = -1, byte priority = 0)
    {
        if (sequence.Tracks > MaxTracks)
            throw new ArgumentException($"A sequence has at most {MaxTracks} tracks, not {sequence.Tracks}.", nameof(sequence));
        if (_playersInUse == MaxPlayers)
            return default;

        var index = _freePlayers[_playersInUse++];
        var p = _players[index];
        p.Sequence = sequence;
        p.Counter = p.Reload = DefaultReload;
        p.Elapsed = 0;
        p.Position = 0;
        p.Volume = volume;
        p.Pan = pan;
        p.Loop = loop;
        p.Priority = priority;
        p.Duration = durationMs;
        // The original keeps the rest of a slot's track state from its last sequence. Every Borg12
        // sets a track's pitch, volume, pan and instrument before its first note, so it never shows.
        for (var t = 0; t < sequence.Tracks; t++)
            p.Tracks[t].Wait = 0;
        p.Playing = true;
        p.Stamp = ++_playerStamp;
        return new SoundHandle(index, p.Stamp);
    }

    public bool IsPlaying(SoundHandle handle) => Find(handle) is not null;

    /// <summary>Stops it and cuts its notes (<c>Dcm.Stop</c>, 1.0 <c>0x80090B18</c>).</summary>
    public void Stop(SoundHandle handle)
    {
        if (Find(handle) is { } p)
            Stop(handle.Player, p);
    }

    public void StopAll()
    {
        for (var i = 0; i < MaxPlayers; i++)
            if (_players[i].Playing)
                Stop(i, _players[i]);
    }

    /// <summary>Changes its volume, and its notes' at once (<c>Dcm.SetVolume</c>, 1.0 <c>0x80090898</c>).</summary>
    public void SetVolume(SoundHandle handle, byte volume)
    {
        if (Find(handle) is not { } p)
            return;
        p.Volume = volume;
        for (var t = 0; t < p.Sequence.Tracks; t++)
            SetVoiceVolume(p.Tracks[t], VoiceVolume(p, p.Tracks[t]));
    }

    /// <summary>Changes its pan, and its notes' at once (<c>Dcm.SetPan</c>, 1.0 <c>0x80090958</c>).</summary>
    public void SetPan(SoundHandle handle, byte pan)
    {
        if (Find(handle) is not { } p)
            return;
        p.Pan = pan;
        for (var t = 0; t < p.Sequence.Tracks; t++)
            SetVoicePan(p.Tracks[t], FinalPan(pan, p.Tracks[t].Pan));
    }

    /// <summary>Stops it <paramref name="ms"/> from now, or never for -1 (<c>Dcm.SetDuration</c>, 1.0 <c>0x80090A7C</c>).</summary>
    public void SetDuration(SoundHandle handle, int ms)
    {
        if (Find(handle) is not { } p)
            return;
        p.Duration = ms;
        p.Elapsed = 0;
    }

    /// <summary>Mixes the next frames into <paramref name="stereo"/>, interleaved left and right.</summary>
    public void Render(Span<short> stereo)
    {
        for (var f = 0; f + 1 < stereo.Length; f += 2)
        {
            if (_untilStep <= 0)
            {
                StepPlayers();
                UpdateVoices();
                _untilStep += _framesPerStep;
            }
            _untilStep--;

            float left = 0, right = 0;
            foreach (var v in _voices)
                if (v.Sounding)
                    v.Mix(ref left, ref right);
            stereo[f] = (short)Math.Clamp(MathF.Round(left), short.MinValue, short.MaxValue);
            stereo[f + 1] = (short)Math.Clamp(MathF.Round(right), short.MinValue, short.MaxValue);
        }
    }

    /// <summary>
    /// How long one play of <paramref name="sequence"/> lasts, in the sequencer's milliseconds: its
    /// rows' periods added up, which is when a looping play starts again. A play that doesn't loop
    /// stops on its last row, a row's period sooner.
    /// </summary>
    public static int Milliseconds(Sequence sequence)
    {
        var engine = new AudioEngine();
        var p = engine._players[0];
        p.Sequence = sequence;
        p.Reload = DefaultReload;
        var ms = 0;
        while (p.Position < sequence.Stream.Length)
        {
            engine.ReadRow(p, sound: false);
            ms += p.Reload + 1;
        }
        return ms;
    }

    private Player? Find(SoundHandle handle) =>
        handle is { IsNone: false, Player: >= 0 and < MaxPlayers } && _players[handle.Player] is { Playing: true } p && p.Stamp == handle.Stamp ? p : null;

    private void Stop(int index, Player p)
    {
        p.Playing = false;
        _freePlayers[--_playersInUse] = index;
        for (var t = 0; t < p.Sequence.Tracks; t++)
            StopVoice(p.Tracks[t]);
    }

    /// <summary>One call of <c>Dcm.Step</c>: each playing sequence's millisecond.</summary>
    private void StepPlayers()
    {
        for (var i = 0; i < MaxPlayers; i++)
        {
            var p = _players[i];
            if (!p.Playing)
                continue;
            if (p.Duration >= 0 && p.Elapsed >= p.Duration)
                Stop(i, p);
            else if (p.Counter < p.Reload)
                p.Counter++;
            else
            {
                ReadRow(p, sound: true);
                if (p.Position >= p.Sequence.Stream.Length)
                {
                    if (!p.Loop)
                        Stop(i, p);
                    else
                    {
                        p.Position = p.Sequence.LoopPosition;
                        for (var t = 0; t < p.Sequence.Tracks; t++)
                        {
                            StopVoice(p.Tracks[t]);
                            p.Tracks[t].Wait = 0;
                        }
                    }
                }
                p.Counter = 0;
            }
            p.Elapsed++;
        }
    }

    /// <summary>One row: an event, or a wait, for each track (docs/formats/audio.md).</summary>
    private void ReadRow(Player p, bool sound)
    {
        var s = p.Sequence.Stream;
        byte Next() => p.Position < s.Length ? s[p.Position++] : (byte)0;

        for (var t = 0; t < p.Sequence.Tracks; t++)
        {
            ref var track = ref p.Tracks[t];
            if (track.Wait != 0)
            {
                track.Wait--;
                continue;
            }

            var b = Next();
            if ((b & 0x80) != 0)
            {
                track.Wait = (byte)((b & 0x7F) + 1);
                continue;
            }
            var noteOn = (b & 0x02) != 0;
            if ((b & 0x40) != 0)
            {
                int w = Next() | Next() << 8;
                var shift = w >> 14;
                track.Pitch = (ushort)(PitchBase[shift] + ((w & 0x3FFF) << shift));
                if (sound && !noteOn)
                    SetVoicePitch(track, track.Pitch);
            }
            if ((b & 0x20) != 0)
            {
                track.Volume = (byte)(uint)MathF.Sqrt(Next());
                if (sound && !noteOn)
                    SetVoiceVolume(track, VoiceVolume(p, track));
            }
            if ((b & 0x10) != 0)
            {
                track.Pan = Next();
                if (sound && !noteOn)
                    SetVoicePan(track, FinalPan(p.Pan, track.Pan));
            }
            if ((b & 0x08) != 0)
                track.Instrument = Next();
            track.StartOffset = (b & 0x04) != 0 ? Next() << 8 : 0;
            if ((b & 0x01) != 0)
            {
                var e = Next();
                if ((e & 0x80) != 0 && sound)
                    StopVoice(track);
                if ((e & 0x40) != 0)
                {
                    var tempo = Next();
                    p.Reload = tempo == 0 ? (byte)0 : (byte)(125000 / (tempo * 25 * 2) - 1);
                }
                if ((e & 0x20) != 0)
                    Next();
            }
            if (noteOn && sound)
            {
                StopVoice(track);
                StartVoice(ref track, p.Sequence.Instruments[track.Instrument], VoiceVolume(p, track), FinalPan(p.Pan, track.Pan), p.Priority);
            }
        }
    }

    private static byte VoiceVolume(Player p, in Track t) => (byte)(t.Volume * p.Volume >> 4);

    /// <summary>A track's pan within its sequence's: the track's scaled into a window around the sequence's.</summary>
    private static byte FinalPan(byte sequencePan, byte trackPan)
    {
        var d = sequencePan - 0x80;
        var window = (0xFF - Math.Abs(d * 2)) * 0x10000;
        var half = (short)(window >> 17);
        var scaled = (short)((uint)trackPan * (uint)(window >> 16) >> 8);
        return (byte)(d < 0 ? sequencePan + half - scaled : scaled + sequencePan - half);
    }

    /// <summary><c>Audio.VoiceStart</c> (1.0 <c>0x8008F778</c>) and the voice manager's start of it.</summary>
    private void StartVoice(ref Track track, Instrument instrument, byte volume, byte pan, byte priority)
    {
        int index;
        if (_voicesInUse < MaxVoices)
            index = _freeVoices[_voicesInUse++];
        else if (priority != 0)
        {
            index = 0;
            for (var i = 1; i < MaxVoices; i++)
                if (_voices[i].Stamp < _voices[index].Stamp)
                    index = i;
        }
        else
            return;

        var v = _voices[index];
        v.Allocated = true;
        v.Stamp = ++_voiceStamp;
        v.Instrument = instrument;
        v.Position = track.StartOffset;
        v.Sounding = true;
        v.Left = v.Right = 0;
        SetRatio(v, track.Pitch);
        SetGains(v, volume, pan);
        track.Voice = index;
        track.VoiceStamp = v.Stamp;
    }

    private Voice? VoiceOf(in Track track) =>
        _voices[track.Voice] is { Allocated: true } v && v.Stamp == track.VoiceStamp ? v : null;

    private void StopVoice(in Track track)
    {
        if (VoiceOf(track) is { } v)
            FreeVoice(track.Voice, v);
    }

    private void FreeVoice(int index, Voice v)
    {
        v.Allocated = false;
        v.Sounding = false;
        _freeVoices[--_voicesInUse] = index;
    }

    private void SetVoicePitch(in Track track, ushort pitch)
    {
        if (VoiceOf(track) is { } v)
            SetRatio(v, pitch);
    }

    private void SetVoiceVolume(in Track track, byte volume)
    {
        if (VoiceOf(track) is { } v)
            SetGains(v, volume, v.PanByte);
    }

    private void SetVoicePan(in Track track, byte pan)
    {
        if (VoiceOf(track) is { } v)
            SetGains(v, v.VolumeByte, pan);
    }

    /// <summary>The resampler's ratio: the pitch in Hz over the original's rate, capped there, then made this engine's.</summary>
    private void SetRatio(Voice v, ushort pitch) =>
        v.Ratio = Math.Clamp((float)pitch / OriginalRate, 0, MaxRatio) * OriginalRate / Rate;

    /// <summary>
    /// The voice manager's volume (<c>min(volume &lt;&lt; 6, 0x7FFF)</c>) and pan (<c>pan &gt;&gt; 1</c>),
    /// through the envelope mixer: volume squared, equal-power pan, gliding over 1000 µs.
    /// </summary>
    private void SetGains(Voice v, byte volume, byte pan)
    {
        v.VolumeByte = volume;
        v.PanByte = pan;
        var synthVolume = Math.Min(volume << 6, 0x7FFF);
        var squared = synthVolume * synthVolume >> 15;
        var synthPan = Math.Min(pan >> 1, 127);
        var left = squared * EqPower[synthPan] / 32768f / 32768f;
        var right = squared * EqPower[127 - synthPan] / 32768f / 32768f;
        v.LeftStep = (left - v.Left) / _rampFrames;
        v.RightStep = (right - v.Right) / _rampFrames;
        v.Ramp = _rampFrames;
    }

    /// <summary>The voice manager frees a voice that played once and reached its sample's end.</summary>
    private void UpdateVoices()
    {
        for (var i = 0; i < MaxVoices; i++)
            if (_voices[i] is { Allocated: true } v && v.Instrument.Loop is null && v.Position >= v.Instrument.Samples.Length)
                FreeVoice(i, v);
    }

    private sealed class Player
    {
        public bool Playing;
        public int Stamp;
        public Sequence Sequence = null!;
        public int Position, Elapsed, Duration;
        public byte Counter, Reload, Volume, Pan, Priority;
        public bool Loop;
        public readonly Track[] Tracks = new Track[MaxTracks];
    }

    private struct Track
    {
        public int Voice, VoiceStamp, StartOffset;
        public ushort Pitch;
        public byte Volume, Pan, Instrument, Wait;
    }

    private sealed class Voice
    {
        public bool Allocated, Sounding;
        public int Stamp;
        public Instrument Instrument = null!;
        public double Position;
        public float Ratio;
        public byte VolumeByte, PanByte;
        public float Left, Right, LeftStep, RightStep;
        public int Ramp;

        public void Mix(ref float left, ref float right)
        {
            var i = (int)Math.Floor(Position);
            var x = (float)(Position - i);
            float a = At(i - 1), b = At(i), c = At(i + 1), d = At(i + 2);
            // Catmull-Rom through b and c.
            var sample = b + 0.5f * x * (c - a + x * (2 * a - 5 * b + 4 * c - d + x * (3 * (b - c) + d - a)));

            if (Ramp > 0)
            {
                Left += LeftStep;
                Right += RightStep;
                Ramp--;
            }
            left += sample * Left;
            right += sample * Right;

            Position += Ratio;
            if (Instrument.Loop is { } loop && loop.End > loop.Start && Position >= loop.End)
                Position -= loop.End - loop.Start;
        }

        /// <summary>A sample by index, through the loop; silence past the end.</summary>
        private float At(int i)
        {
            var samples = Instrument.Samples;
            if (Instrument.Loop is { } loop && loop.End > loop.Start && i >= loop.End)
                i = loop.Start + (i - loop.End) % (loop.End - loop.Start);
            return i >= 0 && i < samples.Length ? samples[i] : 0;
        }
    }
}
