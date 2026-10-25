using System.Text.Json.Serialization;

namespace HBHT.Plugins.Records;

/// <summary>
/// A trigger (ADR 0008's layer 2, roadmap 2.10): <i>on</i> one of the core's named events, <i>if</i> its conditions hold,
/// <i>do</i> its effects, in order. The base plugin's carry what the original's engine code does at an event's place, such
/// as a cinematic after a script's end (<c>aidyn:trigger/0</c> on); a plugin adds its own or patches these like any record.
/// docs/systems/events.md lists the events and what each effect does.
/// </summary>
/// <param name="On">The event's name, as <c>GameEvents</c> in the core names it: <c>script.ended</c>, <c>cinematic.ended</c>…</param>
/// <param name="Subject">
/// What the event must be about for the trigger to go off: the script that ended, the cinematic that ended. Null for any,
/// and for an event about nothing, such as <c>game.started</c>.
/// </param>
/// <param name="If">Every one must hold, in Borg13's condition vocabulary. None always holds.</param>
/// <param name="Do">What it does, in order. An effect that takes over from trek, such as a cinematic, ends the list.</param>
public sealed record TriggerRecord(
    RecordId Id,
    string On,
    RecordId? Subject,
    IReadOnlyList<TriggerCondition>? If,
    IReadOnlyList<TriggerEffect> Do);

/// <summary>
/// A trigger's condition: one of Borg13's condition ops (<see cref="DialogueCommand.CheckFlag"/> to
/// <see cref="DialogueCommand.CheckGold"/>), which holds when its value is in <paramref name="Range"/>, or exactly 1 without
/// one, as an If node's first link takes.
/// </summary>
/// <param name="Value">The skill or stat it checks.</param>
/// <param name="Target">The flag, member or item it checks.</param>
public sealed record TriggerCondition(DialogueCommand Command, int? Value = null, RecordId? Target = null, DialogueRange? Range = null);

/// <summary>
/// One of a trigger's effects: a type the core implements, with its parameters, written with its <c>type</c>. A type no
/// build knows loads as a plain <see cref="TriggerEffect"/>, which does nothing and is reported.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(Op), "op")]
[JsonDerivedType(typeof(TrueName), "trueName")]
[JsonDerivedType(typeof(Cinematic), "cinematic")]
[JsonDerivedType(typeof(Script), "script")]
[JsonDerivedType(typeof(Credits), "credits")]
[JsonDerivedType(typeof(GameOver), "gameOver")]
public record TriggerEffect
{
    /// <summary>
    /// One of Borg13's effect ops that needs no speaker or actor, run as a script's node runs it: <c>SetFlag</c>,
    /// <c>ClearFlag</c>, <c>AddGold</c>, <c>TakeGold</c>, <c>AddItem</c>, <c>TakeItem</c>, <c>AddMember</c> and
    /// <c>RemoveMember</c>, and <c>AddExp</c> with the member as its target. Others are reported and do nothing.
    /// </summary>
    public sealed record Op(DialogueCommand Command, int? Value = null, RecordId? Target = null) : TriggerEffect;

    /// <summary>The party member who is <paramref name="Member"/> knows his True Name (as Roog's cinematic gives Alaron his).</summary>
    public sealed record TrueName(RecordId Member) : TriggerEffect;

    /// <summary>The cinematic plays, over everything; trek comes back after it, unless its own end leads elsewhere. Takes over.</summary>
    public sealed record Cinematic(RecordId Play) : TriggerEffect;

    /// <summary>
    /// The script a dialogue object starts (<paramref name="From"/>, an <c>aidyn:voxel/…</c>) plays as walking into it would,
    /// a cut-scene on the object's stage from its reference points, but at once, with no trek before it. Takes over.
    /// </summary>
    public sealed record Script(RecordId From) : TriggerEffect;

    /// <summary>The credits roll; the ending's can't be skipped, end with "The End", and end the game. Takes over.</summary>
    public sealed record Credits(bool Ending = false) : TriggerEffect;

    /// <summary>The game is over: the original goes back to the title. Takes over.</summary>
    public sealed record GameOver : TriggerEffect;
}
