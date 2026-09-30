namespace Aidyn.Plugins.Records;

/// <summary>
/// A script (<c>aidyn:dialogue/0xHHHH</c>, a Borg13): a conversation, cut-scene, backdrop, sign or
/// the help menu. It's a graph of nodes that starts at node 0, run by the core's script runner.
/// </summary>
/// <param name="Number">The script's number in the original tools (1004–13147). The debug build prints it.</param>
/// <param name="Actors">The entities a cut-scene directs. <see cref="DialogueCommand.CreateActor"/> spawns them.</param>
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
/// <param name="Rest">
/// Eight hours pass when it ends: the camp scenes and the Campsite's own script
/// (<c>isCampfireScene</c>).
/// </param>
/// <param name="BlankNodes">
/// Nodes the original counts after the last, which hold nothing: 153 scripts end with one. They
/// matter only when another script shows a random line from this one
/// (<see cref="DialogueCommand.RandomLine"/>), which counts them, and a blank node shows node 0's text.
/// </param>
public sealed record DialogueRecord(
    RecordId Id,
    int Number,
    DialogueMode Mode,
    IReadOnlyList<DialogueNode> Nodes,
    IReadOnlyList<RecordId>? Actors = null,
    RecordId? DoneFlag = null,
    int? Backdrop = null,
    RecordId? BattleFlag = null,
    bool Rest = false,
    int BlankNodes = 0);

/// <summary>How a script is presented (the original's <c>start_func</c>).</summary>
public enum DialogueMode
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
public enum DialogueNodeKind
{
    /// <summary>Shows its text, then goes to its one link. With no link, it goes back to the last menu or case.</summary>
    Line,
    /// <summary>Evaluates its condition: exactly 1 goes to the first link, anything else to the second.</summary>
    If,
    /// <summary>
    /// Shows its text and evaluates its condition, then considers the links whose
    /// <see cref="DialogueNode.Selects"/> holds the value. One match is followed; several are offered as choices.
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
/// <param name="Ops">
/// Run in three passes: every setup op when the node is reached, the first condition op when an
/// If or Case branches, and only the first effect op when a line is left, or at once for menus
/// and cases.
/// </param>
/// <param name="Links">Node indices, in order. What they mean depends on the <see cref="Kind"/>.</param>
/// <param name="Selects">When a Case links to this node: the values that select it. A node without one is never selected.</param>
public sealed record DialogueNode(
    int Index,
    DialogueNodeKind Kind,
    string Text = "",
    IReadOnlyList<DialogueOp>? Ops = null,
    IReadOnlyList<int>? Links = null,
    DialogueRange? Selects = null);

/// <summary>A range of values a Case matches.</summary>
/// <param name="Max">Inclusive. Null for no upper bound.</param>
public sealed record DialogueRange(int Min, int? Max)
{
    public bool Holds(int value) => Min <= value && (Max is not { } max || value <= max);
}

/// <summary>One op: a command, and the number or record it acts on.</summary>
/// <param name="Value">A number: a reference point, a skill, an amount, a time in ticks and so on. Null when the command takes none.</param>
/// <param name="Target">The flag, entity, item or script it names.</param>
public sealed record DialogueOp(DialogueCommand Command, int? Value = null, RecordId? Target = null)
{
    public override string ToString() => $"{Command}({(Target is { } t ? t.ToString() : Value?.ToString(System.Globalization.CultureInfo.InvariantCulture))})";
}

/// <summary>
/// What an op does (the original's <c>B13_Commands</c>, whose numbers these keep). "The speaker"
/// is the entity the latest <see cref="SetSpeaker"/> named. Reference points are numbered from the
/// one the script was started with.
/// </summary>
public enum DialogueCommand
{
    // Setup: run when the node is reached.

    /// <summary>The speaker of this and later lines, and the subject of actor ops, stat checks and experience.</summary>
    SetSpeaker = 3,
    /// <summary>The camera cuts to a reference point.</summary>
    CameraCutTo = 4,
    /// <summary>The camera cuts to an actor.</summary>
    CameraCutToActor = 5,
    CameraOn = 6,
    CameraOnActor = 7,
    CameraAngleTo = 8,
    CameraAngleToActor = 9,
    CameraPanTo = 10,
    CameraPanToActor = 11,
    CameraSineTo = 12,
    CameraSineToActor = 13,
    CameraTrackOn = 14,
    /// <summary>How many ticks later camera moves take. It starts at 60.</summary>
    CameraTime = 15,
    /// <summary>Adds an entity to the enemies of a later <see cref="Battle"/>, up to 12.</summary>
    AddEnemy = 16,
    /// <summary>The speaker turns to face a reference point.</summary>
    FaceRefPoint = 17,
    /// <summary>Spawns the speaker, one of the script's actors, at a reference point.</summary>
    CreateActor = 18,
    /// <summary>The battle's collision byte.</summary>
    SetCollideByte = 20,
    /// <summary>The battle's arena. Arenas above 27 become 0.</summary>
    SetBattlefield = 21,
    /// <summary>The battle's animation byte.</summary>
    SetAniByte = 22,

    // Conditions: an If or Case evaluates its first one.

    /// <summary>1 if the entity is in the party.</summary>
    CheckMember = 23,
    /// <summary>The flag's value.</summary>
    CheckFlag = 24,
    /// <summary>The party's best level in the skill (random 0–21 while flag 3717 is set).</summary>
    CheckSkill = 25,
    /// <summary>1 if the party has the item, carried or equipped.</summary>
    CheckItem = 26,
    CheckBestStat = 27,
    CheckWorstStat = 28,
    /// <summary>The speaker's value of the stat.</summary>
    CheckSpeakerStat = 29,
    CheckPartySize = 30,
    /// <summary>The party's gold, truncated to 16 bits.</summary>
    CheckGold = 31,

    // Effects: only a node's first runs.

    SetFlag = 32,
    ClearFlag = 33,
    EndDialogue = 34,
    /// <summary>The screen fades to black over a second.</summary>
    FadeOut = 35,
    AddGold = 36,
    /// <summary>Does nothing if the party has less.</summary>
    TakeGold = 37,
    AddItem = 38,
    /// <summary>The debug build crashes if the party doesn't have the item.</summary>
    TakeItem = 39,
    /// <summary>The speaker walks to a reference point. The script waits.</summary>
    WalkTo = 40,
    RunTo = 41,
    /// <summary>The speaker plays an animation (the request). The script waits.</summary>
    PlayAnimation = 42,
    /// <summary>An index into the dialogue sound table.</summary>
    PlaySound = 43,
    /// <summary>Ends the script and opens the speaker's shop.</summary>
    Shop = 44,
    /// <summary>Ends the script and starts a battle with the enemies from <see cref="AddEnemy"/>.</summary>
    Battle = 45,
    /// <summary>Ends the script and opens the speaker's training.</summary>
    Train = 46,
    AddMember = 47,
    /// <summary>Removes a living member without killing them.</summary>
    RemoveMember = 48,
    /// <summary>Crashes the original.</summary>
    Unimplemented = 49,
    /// <summary>Shows a random line of another script. The choice sticks to the map object.</summary>
    RandomLine = 50,
    /// <summary>The speaker holds the item's model.</summary>
    ShowItem = 51,
    HideItem = 52,
    /// <summary>Counts as the node's effect, but does nothing.</summary>
    Unknown53 = 53,
    /// <summary>Heals the party and ends the script. Eight hours pass.</summary>
    CampHeal = 54,
    /// <summary>Gives the speaker experience.</summary>
    AddExp = 56,
    /// <summary>Waits this many ticks.</summary>
    Wait = 57,
    /// <summary>Waits for the scripted camera to finish.</summary>
    WaitForCamera = 58,
    /// <summary>A setup op: every node counts as not taken again.</summary>
    ResetVisited = 60,
}

public static class DialogueCommands
{
    public static bool IsCondition(this DialogueCommand command) => command is >= DialogueCommand.CheckMember and <= DialogueCommand.CheckGold;

    public static bool IsEffect(this DialogueCommand command) => command is >= DialogueCommand.SetFlag and <= DialogueCommand.WaitForCamera;

    /// <summary>Whether the value is a reference point, counted from the script's own.</summary>
    public static bool TakesRefPoint(this DialogueCommand command) => command is DialogueCommand.CameraCutTo or DialogueCommand.CameraOn
        or DialogueCommand.CameraAngleTo or DialogueCommand.CameraPanTo or DialogueCommand.CameraSineTo
        or DialogueCommand.FaceRefPoint or DialogueCommand.CreateActor or DialogueCommand.WalkTo or DialogueCommand.RunTo;
}
