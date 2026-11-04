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

/// <summary>What a scripted camera move does (<c>DialoguePreCallback</c>'s camera ops).</summary>
public enum CameraShot
{
    /// <summary>The camera jumps there (<see cref="ScriptOp.CameraCutTo"/>).</summary>
    CutTo,
    /// <summary>The camera's aim jumps there (<see cref="ScriptOp.CameraLookAt"/>).</summary>
    LookAt,
    /// <summary>The camera's aim turns there over the move's ticks (<see cref="ScriptOp.CameraTurnTo"/>).</summary>
    TurnTo,
    /// <summary>The camera moves there over the move's ticks, eased (<see cref="ScriptOp.CameraMoveTo"/>).</summary>
    MoveTo,
    /// <summary>The camera keeps aiming at the actor (<see cref="ScriptOp.CameraFollow"/>).</summary>
    Follow,
}

/// <summary>A scripted camera move (<c>DialoguePreCallback</c>): to a mark of the stage, or to an actor's eyes.</summary>
/// <param name="Mark">For a move to a mark: the stage's, the script's mark plus the number it was started with.</param>
/// <param name="Actor">For a move to an actor, or one that follows it.</param>
/// <param name="Ticks">How long the move takes: 1 for cuts, otherwise the op's or the latest <see cref="ScriptOp.CameraTime"/>.</param>
/// <param name="Steady">A turn at a steady speed rather than eased.</param>
public sealed record CameraCue(CameraShot Shot, int? Mark, RecordId? Actor, int Ticks, bool Steady = false) : ScriptCue;

/// <summary>An actor turns to face a mark of the stage.</summary>
public sealed record FaceCue(RecordId Actor, int Mark) : ScriptCue;

/// <summary>An actor is spawned at a mark of the stage. It must be one of the script's actors.</summary>
public sealed record SpawnCue(RecordId Actor, int Mark) : ScriptCue;

/// <summary>An actor walks or runs to a mark of the stage. The script waits until it's there, unless the op says not to.</summary>
public sealed record MoveCue(RecordId Actor, int Mark, bool Run) : ScriptCue;

/// <summary>
/// An actor plays an animation (the request's number, <see cref="Requests.Number"/>). In a cut-scene the script waits until it
/// ends; with no actor, nothing plays, and the script waits for the player to press A with the last line showing again. A
/// conversation's animated line sends its actor's as the line shows, and waits for nothing: the speaker plays it in place of
/// the talk, or talks if their actor has no state for it. That's the port's fix: 1.0 looks the actor up from the line it has
/// just cleared (<c>FUN_800591e4</c>), so no one animates, and the line shows again until A.
/// </summary>
public sealed record AnimateCue(RecordId? Actor, int Request) : ScriptCue;

/// <summary>An actor holds an item's model, or nothing when <paramref name="Item"/> is null.</summary>
public sealed record HoldCue(RecordId Actor, RecordId? Item) : ScriptCue;

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
