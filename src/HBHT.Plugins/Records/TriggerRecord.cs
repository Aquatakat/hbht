namespace HBHT.Plugins.Records;

/// <summary>
/// A trigger (ADR 0008's layer 2, roadmap 2.10): <i>on</i> one of the core's named events, <i>if</i> its conditions hold,
/// <i>do</i> its ops, in order. The base plugin's carry what the original's engine code does at an event's place, such
/// as a cinematic after a script's end (<c>aidyn:trigger/0</c> on); a plugin adds its own or patches these like any record.
/// Conditions and ops are scripts' (ADR 0034): a trigger runs the ops whose <see cref="ScriptOp.Use"/> has
/// <see cref="OpUse.Trigger"/>, the ones that need no stage, actor or running script, and a few of its own, such as
/// <see cref="ScriptOp.CinematicPlay"/>. docs/systems/events.md lists the events, and docs/systems/scripts.md the ops.
/// </summary>
/// <param name="On">The event's name, as <c>GameEvents</c> in the core names it: <c>script.ended</c>, <c>cinematic.ended</c>…</param>
/// <param name="Subject">
/// What the event must be about for the trigger to go off: the script that ended, the cinematic that ended. Null for any,
/// and for an event about nothing, such as <c>game.started</c>.
/// </param>
/// <param name="If">
/// Every one must hold: condition ops (<see cref="ScriptOp.Check"/>), each holding at exactly 1, or in its range. None
/// always holds.
/// </param>
/// <param name="Do">
/// What it does, in order. An op that takes over from trek (a cinematic, a script, the credits, the game over) ends the list.
/// </param>
public sealed record TriggerRecord(
    RecordId Id,
    string On,
    RecordId? Subject,
    IReadOnlyList<ScriptOp>? If,
    IReadOnlyList<ScriptOp> Do);
