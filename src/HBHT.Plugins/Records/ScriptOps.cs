using System.Text.Json.Serialization;

namespace HBHT.Plugins.Records;

/// <summary>Where an op can run: a script node's setup, condition or effect, or a trigger's <c>do</c>.</summary>
[Flags]
public enum OpUse
{
    None = 0,
    /// <summary>A node's <see cref="ScriptNode.Setup"/>, run as it's reached.</summary>
    Setup = 0x1,
    /// <summary>A node's <see cref="ScriptNode.Condition"/>, or a trigger's <c>if</c>.</summary>
    Condition = 0x2,
    /// <summary>A node's <see cref="ScriptNode.Effect"/>, run as it's left.</summary>
    Effect = 0x4,
    /// <summary>A trigger's <c>do</c>.</summary>
    Trigger = 0x8,
    /// <summary>A trigger's <c>if</c> alone: a condition on what the event is about, which a script node has none of.</summary>
    EventCondition = 0x10,
}

/// <summary>
/// One op of a script or a trigger (ADR 0034): a type the core implements, with named parameters, written with its <c>op</c>.
/// docs/systems/scripts.md lists every op with the original's command number. Each type says where it can run
/// (<see cref="Use"/>); validation refuses it anywhere else, and the core passes over it there. A type no build knows loads as
/// a plain <see cref="ScriptOp"/>, which does nothing and is reported.
/// </summary>
/// <remarks>
/// <para>
/// A <b>mark</b> is a reference point's <see cref="ReferencePointObject.Mark"/>, counted from the number the script was
/// started with (a script object's <see cref="ScriptObject.Marks"/>), and looked for in the cut-scene's stage. An
/// <b>actor</b> is an entity the cut-scene has spawned, or in a conversation the one talking.
/// </para>
/// <para>
/// Ops that take time wait for it, as 1.0's do, unless their <c>wait</c> is false. The runner runs a node's effect and then
/// its wait; a camera move doesn't wait (<see cref="CameraWait"/> does).
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "op", IgnoreUnrecognizedTypeDiscriminators = true)]
// Setup.
[JsonDerivedType(typeof(CameraCutTo), "camera.cutTo")]
[JsonDerivedType(typeof(CameraLookAt), "camera.lookAt")]
[JsonDerivedType(typeof(CameraTurnTo), "camera.turnTo")]
[JsonDerivedType(typeof(CameraMoveTo), "camera.moveTo")]
[JsonDerivedType(typeof(CameraFollow), "camera.follow")]
[JsonDerivedType(typeof(CameraTime), "camera.time")]
[JsonDerivedType(typeof(ActorSpawn), "actor.spawn")]
[JsonDerivedType(typeof(ActorFace), "actor.face")]
[JsonDerivedType(typeof(BattleAddEnemy), "battle.addEnemy")]
[JsonDerivedType(typeof(BattleArena), "battle.arena")]
[JsonDerivedType(typeof(BattleCollisionByte), "battle.collisionByte")]
[JsonDerivedType(typeof(BattleAnimationByte), "battle.animationByte")]
[JsonDerivedType(typeof(ScriptResetChoices), "script.resetChoices")]
// Conditions.
[JsonDerivedType(typeof(CheckMember), "check.member")]
[JsonDerivedType(typeof(CheckFlag), "check.flag")]
[JsonDerivedType(typeof(CheckSkill), "check.skill")]
[JsonDerivedType(typeof(CheckItem), "check.item")]
[JsonDerivedType(typeof(CheckBestStat), "check.bestStat")]
[JsonDerivedType(typeof(CheckWorstStat), "check.worstStat")]
[JsonDerivedType(typeof(CheckStat), "check.stat")]
[JsonDerivedType(typeof(CheckPartySize), "check.partySize")]
[JsonDerivedType(typeof(CheckGold), "check.gold")]
[JsonDerivedType(typeof(CheckSubjectKind), "check.subjectKind")]
// Effects.
[JsonDerivedType(typeof(FlagSet), "flag.set")]
[JsonDerivedType(typeof(FlagClear), "flag.clear")]
[JsonDerivedType(typeof(ScriptEnd), "script.end")]
[JsonDerivedType(typeof(ScreenDip), "screen.dip")]
[JsonDerivedType(typeof(PartyAddGold), "party.addGold")]
[JsonDerivedType(typeof(PartyTakeGold), "party.takeGold")]
[JsonDerivedType(typeof(PartyAddItem), "party.addItem")]
[JsonDerivedType(typeof(PartyTakeItem), "party.takeItem")]
[JsonDerivedType(typeof(ActorWalkTo), "actor.walkTo")]
[JsonDerivedType(typeof(ActorRunTo), "actor.runTo")]
[JsonDerivedType(typeof(ActorAnimate), "actor.animate")]
[JsonDerivedType(typeof(SoundPlay), "sound.play")]
[JsonDerivedType(typeof(ShopOpen), "shop.open")]
[JsonDerivedType(typeof(BattleStart), "battle.start")]
[JsonDerivedType(typeof(TrainingOpen), "training.open")]
[JsonDerivedType(typeof(PartyAddMember), "party.addMember")]
[JsonDerivedType(typeof(PartyRemoveMember), "party.removeMember")]
[JsonDerivedType(typeof(ScriptRandomLine), "script.randomLine")]
[JsonDerivedType(typeof(ActorHold), "actor.hold")]
[JsonDerivedType(typeof(ActorPutAway), "actor.putAway")]
[JsonDerivedType(typeof(PartyCampHeal), "party.campHeal")]
[JsonDerivedType(typeof(PartyAddExp), "party.addExp")]
[JsonDerivedType(typeof(ScriptWait), "script.wait")]
[JsonDerivedType(typeof(CameraWait), "camera.wait")]
// A trigger's alone.
[JsonDerivedType(typeof(CinematicPlay), "cinematic.play")]
[JsonDerivedType(typeof(StartScript), "script.start")]
[JsonDerivedType(typeof(CreditsRoll), "credits.roll")]
[JsonDerivedType(typeof(GameOver), "game.over")]
[JsonDerivedType(typeof(PartyLearnTrueName), "party.learnTrueName")]
[JsonDerivedType(typeof(MessageShow), "message.show")]
// Reserved.
[JsonDerivedType(typeof(Lua), "lua")]
public record ScriptOp
{
    /// <summary>Where it can run. None for a type this build doesn't know.</summary>
    [JsonIgnore]
    public OpUse Use => Places();

    /// <summary>Each type's <see cref="Use"/>: a method, so that no type's is written.</summary>
    protected virtual OpUse Places() => OpUse.None;

    /// <summary>The op's name as plugins write it, or null for one this build doesn't know.</summary>
    [JsonIgnore]
    public string? Name => Names.GetValueOrDefault(GetType());

    private static readonly Dictionary<Type, string> Names = typeof(ScriptOp)
        .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), false).Cast<JsonDerivedTypeAttribute>()
        .ToDictionary(a => a.DerivedType, a => (string)a.TypeDiscriminator!);

    // ---- Setup -------------------------------------------------------------------------------------------------------------

    /// <summary>A move of the cut-scene's camera: to a mark, or to an actor's eyes. Exactly one of the two.</summary>
    public abstract record CameraMove : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup;
        public int? Mark { get; init; }
        public RecordId? Actor { get; init; }
    }

    /// <summary>The camera jumps to the mark, or to the actor's eyes, hiding the actor (Borg13 4 and 5).</summary>
    public sealed record CameraCutTo : CameraMove;

    /// <summary>The camera turns at once to look at the mark or the actor (6 and 7).</summary>
    public sealed record CameraLookAt : CameraMove;

    /// <summary>
    /// The camera turns to look at the mark or the actor over <see cref="Ticks"/>, eased, or at a steady speed if
    /// <see cref="Steady"/> (8–11: 1.0's angle-to a point and pan-to an actor are steady, the other two eased).
    /// </summary>
    /// <param name="Ticks">How long it takes; without, the latest <see cref="CameraTime"/>'s.</param>
    public sealed record CameraTurnTo(
        int? Ticks = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Steady = false) : CameraMove;

    /// <summary>The camera moves to the mark or the actor's eyes over <see cref="Ticks"/>, eased (12 and 13).</summary>
    /// <param name="Ticks">How long it takes; without, the latest <see cref="CameraTime"/>'s.</param>
    public sealed record CameraMoveTo(int? Ticks = null) : CameraMove;

    /// <summary>The camera keeps aiming at the actor (14, which follows the speaker).</summary>
    public sealed record CameraFollow(RecordId Actor) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup;
    }

    /// <summary>How long the camera moves after it take, until another says otherwise, across nodes. It starts at 60 (15).</summary>
    public sealed record CameraTime(int Ticks) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup;
    }

    /// <summary>The actor, one of the script's <see cref="ScriptRecord.Actors"/>, appears standing at the mark (18).</summary>
    public sealed record ActorSpawn(RecordId Actor, int Mark) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup;
    }

    /// <summary>The actor turns to face the mark (17).</summary>
    public sealed record ActorFace(RecordId Actor, int Mark) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup;
    }

    /// <summary>Adds an enemy to the battle <see cref="BattleStart"/> starts, up to 12 (16).</summary>
    public sealed record BattleAddEnemy(RecordId Entity) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup;
    }

    /// <summary>
    /// The battle's arena, a chunk of the Battle arenas (21). The original names it by its number (<c>Zones.ArenaChunks</c>, the order of
    /// map set 16's chunks), and one above 27 is arena 0, which the importer resolves.
    /// </summary>
    public sealed record BattleArena(RecordId Arena) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup;
    }

    /// <summary>The battle's collision byte (20).</summary>
    public sealed record BattleCollisionByte(int Value) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup;
    }

    /// <summary>The battle's animation byte (22).</summary>
    public sealed record BattleAnimationByte(int Value) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup;
    }

    /// <summary>Every choice counts as not taken again (60).</summary>
    public sealed record ScriptResetChoices : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup;
    }

    // ---- Conditions --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A value an If or Case branches on, or a trigger tests. A node's If takes its first link at exactly 1, a Case the
    /// links whose selects hold it; a trigger's holds at exactly 1, or in <see cref="Range"/>.
    /// </summary>
    public abstract record Check : ScriptOp
    {
        protected override OpUse Places() => OpUse.Condition;

        /// <summary>In a trigger's <c>if</c>: the values it holds at. A node's condition has none.</summary>
        public ScriptRange? Range { get; init; }
    }

    /// <summary>1 if the entity is in the party (23).</summary>
    public sealed record CheckMember(RecordId Entity) : Check;

    /// <summary>The flag's value (24).</summary>
    public sealed record CheckFlag(RecordId Flag) : Check;

    /// <summary>
    /// The party's best level in the skill, rolled 0–21 while the random flag is set (25). A script keeps its first check of
    /// each skill, and the map object that started it remembers it.
    /// </summary>
    public sealed record CheckSkill(RecordId Skill) : Check;

    /// <summary>1 if the party has the item, carried or equipped (26).</summary>
    public sealed record CheckItem(RecordId Item) : Check;

    /// <summary>The party's best value of the stat (27).</summary>
    public sealed record CheckBestStat(RecordId Stat) : Check;

    /// <summary>The party's worst value of the stat (28).</summary>
    public sealed record CheckWorstStat(RecordId Stat) : Check;

    /// <summary>The member's value of the stat (29, the speaker's).</summary>
    public sealed record CheckStat(RecordId Member, RecordId Stat) : Check;

    /// <summary>How many are in the party (30).</summary>
    public sealed record CheckPartySize : Check;

    /// <summary>The party's gold, truncated to 16 bits (31).</summary>
    public sealed record CheckGold : Check;

    /// <summary>
    /// 1 if the event is about an item of this kind, such as the potion <c>item.gained</c> is about. A trigger's alone: a script
    /// node's condition has no event.
    /// </summary>
    public sealed record CheckSubjectKind(ItemKind Kind) : Check
    {
        protected override OpUse Places() => OpUse.EventCondition;
    }

    // ---- Effects -----------------------------------------------------------------------------------------------------------

    public sealed record FlagSet(RecordId Flag) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect | OpUse.Trigger;
    }

    public sealed record FlagClear(RecordId Flag) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect | OpUse.Trigger;
    }

    /// <summary>Ends the script (34).</summary>
    public sealed record ScriptEnd : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>The screen dips to black over a second and comes back over another, while the script carries on (35).</summary>
    public sealed record ScreenDip : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>Gives the party gold, up to 65,535 at a time (36).</summary>
    public sealed record PartyAddGold(int Amount) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect | OpUse.Trigger;
    }

    /// <summary>Takes gold, or does nothing if the party has less (37).</summary>
    public sealed record PartyTakeGold(int Amount) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect | OpUse.Trigger;
    }

    public sealed record PartyAddItem(RecordId Item) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect | OpUse.Trigger;
    }

    /// <summary>Takes the item, if the party has it; the debug build crashes if it hasn't (39).</summary>
    public sealed record PartyTakeItem(RecordId Item) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect | OpUse.Trigger;
    }

    /// <summary>The actor walks to the mark, and the script waits until it's there unless <see cref="Wait"/> is false (40).</summary>
    public sealed record ActorWalkTo(RecordId Actor, int Mark, bool? Wait = null) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>The actor runs to the mark, and the script waits until it's there unless <see cref="Wait"/> is false (41).</summary>
    public sealed record ActorRunTo(RecordId Actor, int Mark, bool? Wait = null) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>
    /// The actor plays the request's animation, and a cut-scene waits until it ends unless <see cref="Wait"/> is false (42). In a
    /// conversation the speaker of the line plays it as the line shows, in place of the talk, and nothing waits: the port's fix.
    /// </summary>
    public sealed record ActorAnimate(RecordId Actor, RecordId Request, bool? Wait = null) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>Plays an entry of the script sound table (<c>sounds.script</c>) (43).</summary>
    public sealed record SoundPlay(int Sound) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>Ends the script and opens the keeper's shop (44). Talking to a wanderer, the wanderer keeps it.</summary>
    public sealed record ShopOpen(RecordId Keeper) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>Ends the script and starts a battle with the enemies <see cref="BattleAddEnemy"/> added (45).</summary>
    public sealed record BattleStart : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>Ends the script and opens the trainer's training (46). Talking to a wanderer, the wanderer trains.</summary>
    public sealed record TrainingOpen(RecordId Trainer) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>The entity joins the party, if there's room (47).</summary>
    public sealed record PartyAddMember(RecordId Entity) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect | OpUse.Trigger;
    }

    /// <summary>A living member leaves the party without dying (48).</summary>
    public sealed record PartyRemoveMember(RecordId Entity) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect | OpUse.Trigger;
    }

    /// <summary>
    /// Shows a random line of another script, under this node's speaker, and waits for the player (50). The roll sticks to the
    /// map object that started the script, so talking again shows the same line.
    /// </summary>
    /// <param name="FirstLineWeight">
    /// How many chances the other script's first node has, against one each for the rest: 2 where 1.0 counted the empty node a
    /// script ended with, which shows the first node's text. Left out for 1.
    /// </param>
    public sealed record ScriptRandomLine(RecordId From, int? FirstLineWeight = null) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>The actor holds the item's model (51).</summary>
    public sealed record ActorHold(RecordId Actor, RecordId Item) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>The actor holds nothing (52).</summary>
    public sealed record ActorPutAway(RecordId Actor) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>Heals the party and ends the script, and eight hours pass (54).</summary>
    public sealed record PartyCampHeal : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>Gives a member experience (56, the speaker).</summary>
    public sealed record PartyAddExp(RecordId Member, int Amount) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect | OpUse.Trigger;
    }

    /// <summary>Waits this many ticks (57).</summary>
    public sealed record ScriptWait(int Ticks) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    /// <summary>Waits for the camera to finish moving (58).</summary>
    public sealed record CameraWait : ScriptOp
    {
        protected override OpUse Places() => OpUse.Effect;
    }

    // ---- A trigger's alone ---------------------------------------------------------------------------------------------------

    /// <summary>The cinematic plays, over everything; trek comes back after it, unless its own end leads elsewhere. Takes over.</summary>
    public sealed record CinematicPlay(RecordId Cinematic) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Trigger;
    }

    /// <summary>
    /// The script a script object starts (<paramref name="From"/>, an <c>aidyn:mapobject/…</c>) plays as walking into it would,
    /// a cut-scene on the object's stage from its marks, but at once, with no trek before it. Takes over.
    /// </summary>
    public sealed record StartScript(RecordId From) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Trigger;
    }

    /// <summary>The credits roll; the ending's can't be skipped, end with "The End", and end the game. Takes over.</summary>
    public sealed record CreditsRoll([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Ending = false) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Trigger;
    }

    /// <summary>The game is over: the original goes back to the title. Takes over.</summary>
    public sealed record GameOver : ScriptOp
    {
        protected override OpUse Places() => OpUse.Trigger;
    }

    /// <summary>The party member who is <paramref name="Member"/> knows his True Name (as Roog's cinematic gives Alaron his).</summary>
    public sealed record PartyLearnTrueName(RecordId Member) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Trigger;
    }

    /// <summary>
    /// Shows <paramref name="Text"/> where the event was fired: over a menu, as its help on the parchment, which A, B or Start
    /// closes; in trek, in the message box. An event fired where nothing can be shown passes over it, and says so.
    /// </summary>
    public sealed record MessageShow(string Text) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Trigger;
    }

    // ---- Reserved ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reserved for Phase 5's Lua (ADR 0008): a function in one of the plugin's <c>lua/</c> files, called where the op runs. It
    /// does nothing until then, and loading reports it.
    /// </summary>
    public sealed record Lua(string File, string Handler) : ScriptOp
    {
        protected override OpUse Places() => OpUse.Setup | OpUse.Condition | OpUse.Effect | OpUse.Trigger;
    }
}
