using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Scripts;

/// <summary>
/// Where a script's presentation goes: cameras, actors, sounds and fades (ADR 0003). The runtime
/// carries them out, and the editor and tests record them.
/// </summary>
/// <remarks>
/// A cut-scene carries out the cues on its stage (<see cref="CutScene"/>), and drops the player's input
/// while the scripted camera moves (<c>cutScene_control_func</c>).
/// </remarks>
public interface IScriptHost
{
    void Cue(ScriptCue cue);
}

/// <summary>Something a script asks the runtime to show or play.</summary>
public abstract record ScriptCue;

/// <summary>
/// A scripted camera move (<c>DialoguePreCallback</c>). Moves that aim at a reference point frame
/// it from the speaker's actor, if the speaker is one.
/// </summary>
/// <param name="Command">One of the <c>Camera</c> commands.</param>
/// <param name="RefPoint">For the commands that take one: the map's reference point.</param>
/// <param name="Actor">For the <c>…ToActor</c> commands: the actor it cuts, turns or moves to.</param>
/// <param name="Ticks">How long the move takes: 1 for cuts, otherwise the latest <see cref="DialogueCommand.CameraTime"/>.</param>
public sealed record CameraCue(DialogueCommand Command, int? RefPoint, RecordId? Actor, RecordId? Speaker, int Ticks) : ScriptCue;

/// <summary>The speaker's actor turns to face a reference point.</summary>
public sealed record FaceCue(RecordId? Actor, int RefPoint) : ScriptCue;

/// <summary>The speaker's actor is spawned at a reference point. It must be one of the script's actors.</summary>
public sealed record SpawnCue(RecordId? Actor, int RefPoint) : ScriptCue;

/// <summary>The speaker's actor walks or runs to a reference point. The script waits until it's there.</summary>
public sealed record MoveCue(RecordId? Actor, int RefPoint, bool Run) : ScriptCue;

/// <summary>
/// An actor plays an animation (the request). In a cut-scene the script waits until it ends; with
/// no actor, nothing plays, and the script waits for the player to press A with the last line
/// showing again. A conversation's animated line sends the speaker's as the line shows, and waits for
/// nothing: the speaker plays it in place of the talk, or talks if their actor has no state for it.
/// That's the port's fix: 1.0 looks the actor up from the line it has just cleared (<c>FUN_800591e4</c>),
/// so no one animates, and the line shows again until A.
/// </summary>
public sealed record AnimateCue(RecordId? Actor, int Request) : ScriptCue;

/// <summary>The speaker's actor holds an item's model, or nothing when <paramref name="Item"/> is null.</summary>
public sealed record HoldCue(RecordId? Actor, RecordId? Item) : ScriptCue;

/// <param name="Index">For <see cref="ScriptSound.Table"/>, the entry of the dialogue sound table.</param>
public sealed record SoundCue(ScriptSound Sound, int Index = 0) : ScriptCue;

/// <summary>
/// The screen dips to black over 60 ticks and comes back over 60 more, by itself; the script carries on meanwhile
/// (<c>DialogCallbackC</c>, fade mode 3).
/// </summary>
public sealed record FadeOutCue : ScriptCue;

public enum ScriptSound
{
    /// <summary>An entry of the dialogue sound table (<c>dialoug_SFX</c>).</summary>
    Table,
    /// <summary>Gold was gained (<c>BORG12_CoinJingle</c>).</summary>
    GoldGained,
    /// <summary>Gold was paid (<c>BORG12_CoinJingle2</c>).</summary>
    GoldPaid,
    /// <summary>A member gained experience (<c>BORG12_ChimeScale</c>).</summary>
    Experience,
}
