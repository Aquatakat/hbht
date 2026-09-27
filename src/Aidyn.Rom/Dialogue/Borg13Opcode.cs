namespace Aidyn.Rom.Dialogue;

/// <summary>
/// A Borg13 op's command (the decomp's <c>B13_Commands</c>). Values are one byte; each op carries
/// one u16 operand. "The speaker" is the entity the latest <see cref="SetSpeaker"/> named.
/// </summary>
/// <remarks>
/// A node's ops are read in three passes, each stopping at the first <see cref="None"/>: every setup
/// op when the node is reached, the first condition op when a branch is taken, and the first effect
/// op (from <see cref="SetFlag"/> on) when a line is left, or at once for menus and cases. Only four
/// nodes in the game have a second effect op, which never runs.
/// </remarks>
public enum Borg13Opcode : byte
{
    None = 0,

    /// <summary>Never read by the game. The operand is usually 0xCCCC or an entity ID.</summary>
    Unknown1 = 1,
    /// <summary>Never read by the game. The operand is a small number.</summary>
    Unknown2 = 2,

    // Setup: run when the node is reached.

    /// <summary>The speaker of this and later lines, and the subject of actor ops, stat checks and XP.</summary>
    SetSpeaker = 3,
    /// <summary>The camera cuts to a reference point (operand + the dialogue's base reference point).</summary>
    CameraCutTo = 4,
    /// <summary>The camera cuts to the actor with this entity ID.</summary>
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
    /// <summary>How many frames later camera moves take. The default is 60.</summary>
    CameraTime = 15,
    /// <summary>Adds this entity to the enemies of a later <see cref="Battle"/>, up to 12.</summary>
    AddEnemy = 16,
    /// <summary>The speaker turns to face a reference point.</summary>
    FaceRefPoint = 17,
    /// <summary>Spawns the speaker's actor at a reference point.</summary>
    CreateActor = 18,
    /// <summary>
    /// The first op of every <see cref="Borg13StartMode.Backdrop"/> script, equal to its
    /// <see cref="Borg13Script.Backdrop"/>. The game reads the header's copy, not this.
    /// </summary>
    Backdrop = 19,
    /// <summary>Copied to the encounter of a later <see cref="Battle"/>.</summary>
    SetCollideByte = 20,
    /// <summary>The arena of a later <see cref="Battle"/>.</summary>
    SetBattlefield = 21,
    /// <summary>Copied to the encounter of a later <see cref="Battle"/>.</summary>
    SetAniByte = 22,

    // Conditions: the first one in an If or Case node is evaluated.

    /// <summary>1 if this entity is in the party.</summary>
    CheckMember = 23,
    /// <summary>The event flag's value.</summary>
    CheckFlag = 24,
    /// <summary>The party's best level in this skill (random 0–21 while flag 3717 is set).</summary>
    CheckSkill = 25,
    /// <summary>1 if the party has this item.</summary>
    CheckItem = 26,
    CheckBestStat = 27,
    CheckWorstStat = 28,
    /// <summary>The speaker's value of this stat.</summary>
    CheckSpeakerStat = 29,
    CheckPartySize = 30,
    /// <summary>The party's gold, truncated to 16 bits.</summary>
    CheckGold = 31,

    // Effects.

    SetFlag = 32,
    ClearFlag = 33,
    EndDialogue = 34,
    FadeOut = 35,
    AddGold = 36,
    /// <summary>Does nothing if the party has less.</summary>
    TakeGold = 37,
    AddItem = 38,
    /// <summary>The debug build crashes if the party doesn't have the item.</summary>
    TakeItem = 39,
    /// <summary>The speaker walks to a reference point. The dialogue waits.</summary>
    WalkTo = 40,
    RunTo = 41,
    /// <summary>The speaker plays an animation.</summary>
    PlayAnimation = 42,
    /// <summary>An index into the dialogue sound table.</summary>
    PlaySound = 43,
    /// <summary>Ends the dialogue and opens the speaker's shop.</summary>
    Shop = 44,
    /// <summary>Ends the dialogue and starts a fight with the enemies from <see cref="AddEnemy"/>.</summary>
    Battle = 45,
    /// <summary>Ends the dialogue and opens the speaker's training menu.</summary>
    Train = 46,
    AddMember = 47,
    /// <summary>Removes a living member without killing them.</summary>
    RemoveMember = 48,
    /// <summary>Crashes the game.</summary>
    Unimplemented = 49,
    /// <summary>Shows a random line of another Borg13. The choice sticks to the map object.</summary>
    RandomLine = 50,
    /// <summary>The speaker holds this item's model.</summary>
    ShowItem = 51,
    HideItem = 52,
    /// <summary>Handled like an effect, but does nothing.</summary>
    Unknown53 = 53,
    /// <summary>Heals the party and ends the dialogue. Eight hours pass.</summary>
    CampHeal = 54,
    /// <summary>Never read by the game.</summary>
    Unknown55 = 55,
    /// <summary>Gives the speaker experience.</summary>
    AddExp = 56,
    /// <summary>Waits this many frames.</summary>
    Wait = 57,
    /// <summary>Waits for the scripted camera to finish.</summary>
    WaitForCamera = 58,
    /// <summary>Never read by the game.</summary>
    Unknown59 = 59,
    /// <summary>A setup op: marks every node unvisited again.</summary>
    ResetVisited = 60,
}

public static class Borg13Opcodes
{
    public static bool IsCondition(this Borg13Opcode op) => op is >= Borg13Opcode.CheckMember and <= Borg13Opcode.CheckGold;

    public static bool IsEffect(this Borg13Opcode op) => op is >= Borg13Opcode.SetFlag and <= Borg13Opcode.Unknown59;

    /// <summary>Whether the operand is a reference point, relative to the dialogue's own.</summary>
    public static bool TakesRefPoint(this Borg13Opcode op) => op is Borg13Opcode.CameraCutTo or Borg13Opcode.CameraOn
        or Borg13Opcode.CameraAngleTo or Borg13Opcode.CameraPanTo or Borg13Opcode.CameraSineTo
        or Borg13Opcode.FaceRefPoint or Borg13Opcode.CreateActor or Borg13Opcode.WalkTo or Borg13Opcode.RunTo;
}
