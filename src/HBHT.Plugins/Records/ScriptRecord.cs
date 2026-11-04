using System.Text.Json.Serialization;

namespace HBHT.Plugins.Records;

/// <summary>
/// A script (<c>aidyn:script/0xHHHH</c>, a Borg13): a conversation, cut-scene, backdrop, sign or the help menu (ADR 0034).
/// It's a graph of nodes that starts at node 0, run by the core's script runner. The base plugin writes each in a file of its
/// own, <c>records/scripts/0xHHHH.json</c>.
/// </summary>
/// <param name="Number">The script's number in the original tools (1004–13147). The debug build prints it.</param>
/// <param name="Label">For tools: the number and the first speaker's name, or the mode where no one speaks.</param>
/// <param name="Actors">The entities a cut-scene directs, which <see cref="ScriptOp.ActorSpawn"/> spawns.</param>
/// <param name="DoneFlag">
/// Set when a cut-scene, backdrop or the help menu ends, however it ends. Conversations and signs
/// have none.
/// </param>
/// <param name="Backdrop">
/// Which picture a backdrop or the help menu shows behind its text (the original's
/// <c>libraryType</c>, read by <c>BigBGDialoug</c>).
/// </param>
/// <param name="BattleFlag">
/// Set by winning the battle this script starts. Only the Goblin Ambush and Lugash's fight have
/// one; the original keeps them in a table (<c>dialoug_func</c>).
/// </param>
/// <param name="BattleRules">
/// The rules of the battle this script starts when it's played from a script object, under its <paramref name="BattleFlag"/>
/// (ADR 0035): the Goblin Ambush's and Lugash's. A battle an npc's script starts is the npc's, with the npc's rules.
/// </param>
/// <param name="Rest">
/// Eight hours pass when it ends: the camp scenes and the Campsite's own script
/// (<c>isCampfireScene</c>). Left out when false.
/// </param>
public sealed record ScriptRecord(
    RecordId Id,
    int Number,
    ScriptMode Mode,
    [property: KeyedList(nameof(ScriptNode.Index)), JsonPropertyOrder(1)] IReadOnlyList<ScriptNode> Nodes,
    string? Label = null,
    IReadOnlyList<RecordId>? Actors = null,
    RecordId? DoneFlag = null,
    int? Backdrop = null,
    RecordId? BattleFlag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Rest = false,
    BattleRules? BattleRules = null);

/// <summary>How a script is presented (the original's <c>start_func</c>).</summary>
public enum ScriptMode
{
    /// <summary>Talking to someone on the map.</summary>
    Conversation,
    /// <summary>A scene on its own map, with actors and cameras.</summary>
    Cutscene,
    /// <summary>Text over a full-screen picture, such as a book. Choices can be taken again.</summary>
    Backdrop,
    Sign,
    /// <summary>The help menu. Like a backdrop, choices can be taken again.</summary>
    Help,
}

/// <summary>What a node does once reached.</summary>
public enum ScriptNodeKind
{
    /// <summary>Shows its text, then goes to its one link. With no link, it goes back to the last menu or case.</summary>
    Line,
    /// <summary>Evaluates its condition: exactly 1 goes to the first link, anything else to the second.</summary>
    If,
    /// <summary>
    /// Shows its text and evaluates its condition, then considers the links whose
    /// <see cref="ScriptNode.Selects"/> holds the value. One match is followed; several are offered as choices.
    /// </summary>
    Case,
    /// <summary>Shows its text and offers its links as choices. Choices already taken are hidden, except in backdrops and the help menu.</summary>
    Menu,
}

/// <summary>A node of a script.</summary>
/// <param name="Index">Its position, which links use.</param>
/// <param name="Text">
/// What it shows, with line breaks. <c>{player}</c> stands for the name of the party's first
/// member, and characters U+007F–U+0087 are the font's button pictures.
/// </param>
/// <param name="Speaker">
/// Who says its line, whose portrait shows and who talks as it does. None draws the fallback portrait, as the narrator,
/// books and signs do. Only a node that shows a line has one: one with text, or one whose effect is a random line.
/// </param>
/// <param name="Setup">Run in order when the node is reached: the camera, the actors, the battle to come.</param>
/// <param name="Condition">What an If or Case evaluates. Other kinds never do.</param>
/// <param name="Effect">Run as a line is left, or at once for menus and cases.</param>
/// <param name="Links">Node indices, in order. What they mean depends on the <see cref="Kind"/>.</param>
/// <param name="Selects">When a Case links to this node: the values that select it. A node without one is never selected.</param>
public sealed record ScriptNode(
    [property: JsonPropertyOrder(-1)] int Index,
    [property: JsonPropertyOrder(-1)] ScriptNodeKind Kind,
    string Text = "",
    [property: JsonPropertyOrder(-1)] RecordId? Speaker = null,
    IReadOnlyList<ScriptOp>? Setup = null,
    ScriptOp? Condition = null,
    ScriptOp? Effect = null,
    IReadOnlyList<int>? Links = null,
    ScriptRange? Selects = null)
{
    /// <summary>Its ops: the setup's in order, then the condition, then the effect.</summary>
    [JsonIgnore]
    public IEnumerable<ScriptOp> Ops => (Setup ?? []).Concat(Condition is { } c ? [c] : []).Concat(Effect is { } e ? [e] : []);
}

/// <summary>A range of values a Case, or a trigger's condition, matches.</summary>
/// <param name="Max">Inclusive. Null for no upper bound.</param>
public sealed record ScriptRange(int Min, int? Max = null)
{
    public bool Holds(int value) => Min <= value && (Max is not { } max || value <= max);
}
