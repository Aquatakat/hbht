using HBHT.Plugins;

namespace HBHT.Core.Audio;

/// <summary>
/// The game's side of audio, which tells the sequencer what to play: the background music (<c>Bgm</c>), one-shot
/// sounds (<c>Audio.PlaySound</c>) and the volumes (docs/systems/audio.md). Map objects' sounds are
/// <see cref="MapSounds"/>, which plays through this.
/// </summary>
/// <remarks>
/// Everything here takes a lock, so <see cref="Render"/> can be called from an audio thread while the game steps the
/// rest.
/// </remarks>
public sealed class GameAudio
{
    /// <summary>One-shots playing at once (<c>Audio.Init</c>).</summary>
    public const int MaxSounds = 16;

    private readonly Lock _sync = new();
    private readonly Slot?[] _sounds = new Slot?[MaxSounds];
    private RecordId? _musicPlaying;
    private SoundHandle _music;

    public GameAudio(SoundBank bank, int rate = AudioEngine.OriginalRate)
    {
        Bank = bank;
        Engine = new AudioEngine(rate);
    }

    /// <summary>A game's audio that plays nothing: for tests and tools.</summary>
    public static GameAudio Silent(PluginSet plugins) => new(new SoundBank(plugins));

    public AudioEngine Engine { get; }

    public SoundBank Bank { get; }

    /// <summary>
    /// The settings the volumes come from: the session hands it each tick's (<see cref="GameSession.Tick"/>), and it's the
    /// original's until then.
    /// </summary>
    public SessionSettings Settings { get; set; } = SessionSettings.Original;

    /// <summary>The sound effects' volume, 0 to 1 (<c>VolSFX</c>), or 0 while <see cref="Muted"/>.</summary>
    public float SoundVolume => Muted ? 0 : (float)Settings.SoundVolume / Core.Settings.MaxVolume;

    /// <summary>The music's volume, 0 to 1 (<c>VolBGM</c>), or 0 while <see cref="Muted"/>.</summary>
    public float MusicVolume => Muted ? 0 : (float)Settings.MusicVolume / Core.Settings.MaxVolume;

    /// <summary>
    /// Whether the game's music and sounds are silent, though they go on, as through the credits, which zero both
    /// volumes and set them back after (<c>Credits.Start</c>, <c>Credits.Destroy</c>).
    /// </summary>
    public bool Muted { get; set; }

    /// <summary>
    /// The background music asked for, or none. <see cref="Step"/> starts it from its beginning when it changes, and
    /// stops what played before.
    /// </summary>
    public RecordId? Music { get; set; }

    /// <summary>How many one-shots are playing or waiting to.</summary>
    public int SoundsPlaying
    {
        get
        {
            lock (_sync)
                return _sounds.Count(s => s is not null);
        }
    }

    /// <summary>Whether the music waits to change to what <see cref="Music"/> asks for, as through a fade.</summary>
    public bool HoldMusic { get; set; }

    /// <summary>The music playing, which <see cref="Music"/> asked for.</summary>
    public RecordId? MusicPlaying => _musicPlaying;

    /// <summary>
    /// Plays a one-shot (<c>Audio.PlaySound</c>, 1.0 <c>0x8004E150</c>), centred, without falloff, from the next
    /// <see cref="Step"/>. It's a sound object of its own, at the sound volume. It holds its slot for its whole lifetime,
    /// and with all <see cref="MaxSounds"/> held, it doesn't play.
    /// </summary>
    public void Play(OneShot sound)
    {
        lock (_sync)
        {
            var slot = Array.IndexOf(_sounds, null);
            if (slot < 0)
                return;
            _sounds[slot] = new Slot(sound)
            {
                Fade = 1,
                Lifetime = sound.Lifetime + sound.Delay,
                Delay = sound.Delay,
            };
        }
    }

    /// <inheritdoc cref="Play(OneShot)"/>
    public void Play(RecordId sequence, int lifetime, int delay = 0) => Play(new OneShot(sequence, lifetime, delay));

    /// <summary>
    /// Starts a sequence that something else keeps up, such as a map object's sound, at a volume and pan
    /// (<see cref="AudioLevels"/>).
    /// </summary>
    public SoundHandle Start(RecordId sequence, byte volume, byte pan, bool loop, int durationMs = -1)
    {
        lock (_sync)
            return Bank.Get(sequence) is { } s ? Engine.Play(s, volume, pan, loop, durationMs) : default;
    }

    public bool IsPlaying(SoundHandle handle)
    {
        lock (_sync)
            return Engine.IsPlaying(handle);
    }

    public void Set(SoundHandle handle, byte volume, byte pan)
    {
        lock (_sync)
        {
            Engine.SetVolume(handle, volume);
            Engine.SetPan(handle, pan);
        }
    }

    public void SetDuration(SoundHandle handle, int ms)
    {
        lock (_sync)
            Engine.SetDuration(handle, ms);
    }

    public void Stop(SoundHandle handle)
    {
        lock (_sync)
            Engine.Stop(handle);
    }

    /// <summary>The byte a sequence plays at for a loudness from 0 to 1, as the original truncates it.</summary>
    public static byte Volume(float loudness) => (byte)Math.Clamp((int)(loudness * 255), 0, 255);

    /// <summary>
    /// A tick: the background music follows <see cref="Music"/> at the music volume (<c>Bgm.Update</c>, 1.0
    /// <c>0x8000E570</c>), and the one-shots start once their delay is past, once, and stop at the end of their
    /// lifetime (<c>ProcessAudioBubbles</c>).
    /// </summary>
    public void Step()
    {
        lock (_sync)
        {
            StepMusic();
            for (var i = 0; i < MaxSounds; i++)
            {
                if (_sounds[i] is not { } sound)
                    continue;
                sound.Fade = MathF.Min(1, sound.Fade + 1f / AudioLevels.FadeTicks);
                if (--sound.Delay <= 0)
                {
                    sound.Delay = 0;
                    sound.Update(this, SoundVolume);
                }
                if (--sound.Lifetime <= 0)
                {
                    Engine.Stop(sound.Handle);
                    _sounds[i] = null;
                }
            }
        }
    }

    /// <summary>
    /// Cuts the music at once and asks for none (<c>Bgm.Stop(1)</c>, 1.0 <c>0x8000E4C4</c>, from <c>FreeZoneEngine(0)</c> as trek or a
    /// battle's arena goes): no fade, and the sounds play on. The next piece asked for starts from its beginning.
    /// </summary>
    public void StopMusic()
    {
        lock (_sync)
        {
            Engine.Stop(_music);
            _music = default;
            _musicPlaying = null;
            Music = null;
        }
    }

    /// <summary>Stops everything: the music and every sound.</summary>
    public void StopAll()
    {
        lock (_sync)
        {
            Engine.StopAll();
            Array.Clear(_sounds);
            _musicPlaying = null;
            _music = default;
        }
    }

    /// <summary>Mixes the next frames, interleaved left and right. Safe to call from an audio thread.</summary>
    public void Render(Span<short> stereo)
    {
        lock (_sync)
            Engine.Render(stereo);
    }

    private void StepMusic()
    {
        if (Music != _musicPlaying && !HoldMusic)
        {
            Engine.Stop(_music);
            _music = default;
            _musicPlaying = Music;
            if (Music is { } id && Bank.Get(id) is { } sequence)
                _music = Engine.Play(sequence, Volume(MusicVolume), 0x80, loop: true);
        }
        // Sent again every frame, so the options' slider acts at once.
        Engine.SetVolume(_music, Volume(MusicVolume));
    }

    /// <summary>
    /// A one-shot is a <see cref="KeptSound"/> of volume 1, times the sound volume now, centred, a pan of 0. The original
    /// keeps the sound volume at the call as its volume, and some callers pass it as its gain too, a fade rising to 1
    /// over 90 ticks, so it counts twice or three times; the port's, the user's fix, counts it once
    /// (docs/systems/audio.md#volumes).
    /// </summary>
    private sealed class Slot(OneShot sound) : KeptSound(sound.Sequence, 1, false)
    {
        public int Lifetime, Delay;
    }
}

/// <summary>A one-shot sound: <c>Audio.PlaySound</c>'s arguments.</summary>
/// <param name="Lifetime">Ticks after its delay that it's stopped, whether or not it has ended.</param>
/// <param name="Delay">Ticks before it starts.</param>
public readonly record struct OneShot(RecordId Sequence, int Lifetime, int Delay = 0)
{
    /// <summary>The lifetime of the menus' cursors, a second of the menus' others, and five of the gold and experience sounds, in ticks.</summary>
    public const int ShortTicks = 30, MenuTicks = 60, LongTicks = 300;
}
