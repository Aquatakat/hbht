using HBHT.Plugins;

namespace HBHT.Core.Audio;

/// <summary>
/// A sound object the game keeps up (<c>Audio.UpdateSound</c>, 1.0 <c>0x8004E900</c>), which map objects' sounds
/// (<see cref="MapSounds"/>) and one-shots (<see cref="GameAudio.Play(OneShot)"/>) both are: a fade, a pan and a
/// volume that its playing sequence follows, started when it needs starting.
/// </summary>
/// <param name="sequence">What it plays.</param>
/// <param name="volume">Its own volume, which its fade scales.</param>
/// <param name="loop">Whether it is started again whenever it has ended.</param>
public abstract class KeptSound(RecordId sequence, float volume, bool loop)
{
    /// <summary>The sequence playing for it, or none.</summary>
    public SoundHandle Handle;

    /// <summary>Its fade: its loudness, or rising to 1, or falling to 0.</summary>
    public float Fade;

    /// <summary>Its pan, from −1 left to 1 right (<see cref="AudioLevels.Pan"/>), or 0 for the middle.</summary>
    public float Pan;

    /// <summary>Whether it waits to be started, once <c>there</c> lets it.</summary>
    public bool StartNeeded = true;

    /// <summary>
    /// Keeps a playing sequence up at its volume and pan, or, while none plays, reads <paramref name="there"/> and starts one
    /// that needs starting if it lets it. One that loops is started again whenever it has ended; any other plays once.
    /// </summary>
    /// <param name="audio">What it plays through.</param>
    /// <param name="level">The sound or music volume it scales by (<see cref="GameAudio.SoundVolume"/>).</param>
    /// <param name="there">Whether its flags let it play, read on every update while it isn't playing.</param>
    public void Update(GameAudio audio, float level, Func<bool>? there = null)
    {
        var volumeByte = AudioLevels.Volume(Fade, volume, level);
        var pan = AudioLevels.PanByte(Pan);
        if (audio.IsPlaying(Handle))
        {
            audio.SetDuration(Handle, -1);
            audio.Set(Handle, volumeByte, pan);
            return;
        }
        // 1.0 reads the gate on every update it isn't playing, before it looks at whether it needs starting (`0x8004ea10`, then the bit at `0x8004ea24`): a
        // read that, of a derived flag, brings the flag network up to date, which is what tells a journal entry near most of the
        // ambient sounds outdoors, gated by the day or the night (docs/systems/flags.md#when-the-network-comes-up-to-date).
        if (there is not null && !there() || !StartNeeded)
            return;
        Handle = audio.Start(sequence, volumeByte, pan, loop, AudioLevels.FirstDurationMs);
        StartNeeded = loop;
    }
}
