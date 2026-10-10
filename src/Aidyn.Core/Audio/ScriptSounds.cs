using Aidyn.Core.Scripts;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Audio;

/// <summary>What a script's sounds play (<c>Dialogue.ExecuteCommand</c>): all for five seconds.</summary>
public static class ScriptSounds
{
    /// <summary>The one-shot a cue plays, or none if the sounds don't have it.</summary>
    public static OneShot? For(SoundsRecord sounds, SoundCue cue) => cue.Sound switch
    {
        ScriptSound.Table when cue.Index >= 0 && cue.Index < sounds.Script.Count => new OneShot(sounds.Script[cue.Index], OneShot.LongTicks),
        ScriptSound.GoldGained => new OneShot(sounds.GoldGained, OneShot.LongTicks, TimesSoundVolume: true),
        ScriptSound.GoldPaid => new OneShot(sounds.GoldPaid, OneShot.LongTicks, TimesSoundVolume: true),
        ScriptSound.Experience => new OneShot(sounds.Experience, OneShot.LongTicks),
        _ => null,
    };
}
