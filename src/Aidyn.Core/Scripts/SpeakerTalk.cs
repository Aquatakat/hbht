using Aidyn.Core.Movement;

namespace Aidyn.Core.Scripts;

/// <summary>
/// The speaker's talking as the player moves a script's box on, which conversations and cut-scenes share
/// (<c>run_dialougemode_funcs2</c> in trek, <c>funcs2[1]</c>, <c>0x80051ed4</c>, in a cut-scene): the script moves on by
/// the press, and unless it played an animation as it did (<c>ApplyStimulus</c>, which sets <c>unkac</c>), whoever speaks
/// is asked for <see cref="Body.Talk"/>. Each mode asks its bodies to stand again after they next step, so the
/// talking lasts a tick.
/// </summary>
public sealed class SpeakerTalk
{
    private bool _animated;

    /// <summary>The script played an animation (<c>ApplyStimulus</c>), so this press's speaker doesn't talk.</summary>
    public void Animated() => _animated = true;

    /// <summary>Moves the script on by <paramref name="advance"/>.</summary>
    /// <returns>Whether the speaker talks: unless the script played an animation as it moved on.</returns>
    public bool Press(Action advance)
    {
        _animated = false;
        advance();
        return !_animated;
    }
}
