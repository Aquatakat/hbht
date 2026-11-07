using HBHT.Plugins;

namespace HBHT.Core.Audio;

/// <summary>
/// What the game's sounds go through to the sequencer: bodies' keys (<see cref="AudioKeys"/>), the map's sounds (<see cref="MapSounds"/>,
/// <see cref="KeptSound"/>), the weather's (<see cref="WeatherSounds"/>) and one-shots. <see cref="GameAudio"/> plays them; something
/// else can take them instead, as a battle's recording will (ADR 0040). Whether a sound plays on is answered in the game's ticks, never
/// from the audio thread, so nothing the game does hangs on how far the mixer has got.
/// </summary>
public interface ISoundOutput
{
    /// <summary>The sound effects' volume, 0 to 1.</summary>
    float SoundVolume { get; }

    /// <summary>The music's volume, 0 to 1.</summary>
    float MusicVolume { get; }

    /// <summary>Starts a sequence that something else keeps up, at a volume and pan; none if the sequence isn't there.</summary>
    SoundHandle Start(RecordId sequence, byte volume, byte pan, bool loop, int durationMs = -1);

    /// <summary>
    /// Whether a sequence it started plays on: not stopped, not past its duration and, if it doesn't loop, not played to its end, by the
    /// game's ticks.
    /// </summary>
    bool IsPlaying(SoundHandle handle);

    void Set(SoundHandle handle, byte volume, byte pan);

    /// <summary>Stops it <paramref name="ms"/> from now, or never for −1.</summary>
    void SetDuration(SoundHandle handle, int ms);

    void Stop(SoundHandle handle);

    /// <summary>Plays a one-shot (<c>Audio.PlaySound</c>).</summary>
    void Play(OneShot sound);
}
