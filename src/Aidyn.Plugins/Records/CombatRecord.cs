namespace Aidyn.Plugins.Records;

/// <summary>
/// What combat keeps of content, there being one, <c>aidyn:combat/0</c>: who fights on the party's side from an enemy list,
/// who never fights, the enemy a roll that comes to none fights, and who and which encounters decide whether the party can
/// flee. docs/systems/combat.md says where each is in the original.
/// </summary>
/// <param name="Ally">
/// The entity who, among an enemy list's first four, fights on the party's side instead (<c>Combat.FindSholeh</c>,
/// <c>0x8005C6C0</c>): Sholeh.
/// </param>
/// <param name="Bystanders">
/// Entities dropped from an enemy list, who never fight (<c>Combat.IsExcludedEnemy</c>, <c>0x8005C0C0</c>): the townsfolk,
/// the shopkeepers and trainers and the like.
/// </param>
/// <param name="Fallback">The enemy a monster party's roll fights when it comes to none (<c>BattleSetup.FillEnemies</c>): a Skeleton.</param>
/// <param name="NoFlee">
/// Enemies on the field who keep the party from fleeing (<c>Combat.NoFleeEntities</c>, <c>0x800D2688</c>), and
/// <paramref name="Flee"/> those who let it, the last on the field having the say (<c>Combat.ForbidFleeForBosses</c>).
/// </param>
/// <param name="Flee">
/// The Shadow, with whom the party can flee (<c>Combat.ForbidFleeForBosses</c>, <c>0x8005CCCC</c>, which compares the entity's place
/// with <c>0xAC</c>). Who can't be attacked is <paramref name="Untargetable"/>.
/// </param>
/// <param name="FleeByEncounter">
/// Encounter flags that have the last word on fleeing (<c>Combat.ApplyFleeRuleByFlag</c>, <c>0x800D26A0</c>): the Goblin
/// Ambush's forbids it, Lugash's and the Marquis's allow it.
/// </param>
/// <param name="Rooted">
/// Entities whose field of influence is 0, who never move in battle (<c>Combatant.UpdateRadius</c>, by the entity list's
/// place in <c>0x800E28EB</c>): Pochanargat. Null in a plugin from before it.
/// </param>
/// <param name="Missiles">
/// The weapons that fly to their target in battle (<c>Combatant.UpdateDrawnFlag</c>, <c>0x80066A2C</c>, through the bow list
/// <c>Combat.BowKindWeapons</c> <c>0x800E0C08</c> and the thrown list <c>Combat.ThrownKindWeapons</c> <c>0x800E0C14</c>): aimed
/// freely rather than at a target in reach, and drawn in flight. Null in a plugin from before it.
/// </param>
/// <param name="CloseRange">
/// Ranged weapons that may strike in melee too (<c>Weapon.AllowsMelee</c>, <c>0x8007B730</c>, by the weapon's place): Spikes.
/// Null in a plugin from before it.
/// </param>
/// <param name="Bosses">
/// The flags a win sets for a boss among the enemy list's first four (<c>Combat.SetKillFlags</c>, <c>0x80067860</c>, by the
/// jump table <c>0x800D2B60</c>), however the battle started and whether the boss fell or not. Null in a plugin from before it.
/// </param>
/// <param name="BossStyles">
/// Those whose AI takes the bosses' styles when they have spells (<c>EnemyAI.HasBossStyle</c>, <c>0x8005BA40</c>), 9 and 10 rather
/// than 6 and 8. Null in a plugin from before it.
/// </param>
/// <param name="DroppedClasses">
/// The weapon classes an enemy's weapons can drop as loot (<c>Combat.DropWeaponClasses</c>, <c>0x800D26C8</c>): its natural
/// weapons never do. Null in a plugin from before it.
/// </param>
/// <param name="Encounters">
/// The battles with rules of their own, by their encounter flag (the Goblin Ambush's 978): a loss that isn't the game's end,
/// no victory line, and what happens after it however it ends. Null in a plugin from before it.
/// </param>
/// <param name="GameOver">
/// What a lost battle plays (<c>Combat.StartGameOverScript</c>, <c>0x8001F860</c>): the script, from the first dialogue
/// object that starts it in the map. Null in a plugin from before it.
/// </param>
/// <param name="Text">
/// The combat menus' and Sense Aura's words, which the code keeps rather than a string table. Null in a plugin from before it.
/// </param>
/// <param name="FlaskDanger">
/// The flasks the party may hold, and the radius each makes the AI spread out by, the first held counting
/// (<c>Combat.PartyPotionTier</c>, <c>0x8005C750</c>). Null in a plugin from before it.
/// </param>
/// <param name="Shadow">The Shadow's rules in any battle it's in (<c>gamecombat.cpp</c>'s Shadow functions). Null in a plugin from before it.</param>
/// <param name="UndrawnWeapons">
/// Entities whose weapon, arrow or flask isn't drawn in the hand, their models having their weapons already
/// (<c>Combatant.HasNoWeaponAttachment</c>, <c>0x80066C5C</c>, the entity list's places at <c>0x800D2A0C</c>): Shamsuk, the Gorgon,
/// the Chaos Lieutenant, the Harpy, the Marquis, the Minotaur, the Minotaur Lord and Niesen. A shield or a bow is drawn all the same.
/// Null in a plugin from before it.
/// </param>
/// <param name="OtherHand">
/// Entities who hold a bow at their actor's locator 0 and its arrow at 1, the other way round from the rest (<c>EntityBody.Hand</c>,
/// <c>0x8006C8BC</c>, a switch on the entity list's place): the bandit bosses and woodsmen, Becan and Keelin. Null in a plugin from before it.
/// </param>
/// <param name="Untargetable">
/// Entities no one can attack or cast at, and who cast nothing themselves: the Shadow. <c>Combatant.CanAttack</c> (<c>0x80060590</c>)
/// refuses a target whose entity is the entity list's place <c>0xAC</c> (the byte at <c>0x800E28EC</c>), and <c>Combatant.CanCast</c>
/// (<c>0x80060C38</c>) a caster or target in <c>Combat.ShadowSlot</c> (<c>0x800D94CC</c>), which <c>Combat.CreateBody</c> sets for that
/// place; the original keeps no list. Null in a plugin from before it, which has the Shadow in <paramref name="Flee"/> alone.
/// </param>
public sealed record CombatRecord(
    RecordId Id,
    RecordId Ally,
    IReadOnlyList<RecordId> Bystanders,
    RecordId Fallback,
    IReadOnlyList<RecordId> NoFlee,
    IReadOnlyList<RecordId> Flee,
    [property: KeyedList(nameof(EncounterFlee.Flag))] IReadOnlyList<EncounterFlee> FleeByEncounter,
    IReadOnlyList<RecordId>? Rooted = null,
    [property: KeyedList(nameof(Missile.Weapon))] IReadOnlyList<Missile>? Missiles = null,
    IReadOnlyList<RecordId>? CloseRange = null,
    [property: KeyedList(nameof(BossFlag.Entity))] IReadOnlyList<BossFlag>? Bosses = null,
    IReadOnlyList<RecordId>? BossStyles = null,
    IReadOnlyList<WeaponClass>? DroppedClasses = null,
    [property: KeyedList(nameof(EncounterRule.Flag))] IReadOnlyList<EncounterRule>? Encounters = null,
    GameOverScene? GameOver = null,
    CombatText? Text = null,
    [property: KeyedList(nameof(Records.FlaskDanger.Item))] IReadOnlyList<FlaskDanger>? FlaskDanger = null,
    ShadowRule? Shadow = null,
    IReadOnlyList<RecordId>? UndrawnWeapons = null,
    IReadOnlyList<RecordId>? OtherHand = null,
    IReadOnlyList<RecordId>? Untargetable = null)
{
    /// <summary>The rules of the battle under encounter flag <paramref name="flag"/>, if it has any.</summary>
    public EncounterRule? Encounter(RecordId? flag) => flag is { } f ? Encounters?.FirstOrDefault(e => e.Flag == f) : null;
}

/// <summary>
/// The Shadow's rules, in any battle it's in (<c>Combat.ShadowSlot</c>, <c>0x800D94CC</c>, set as its combatant is made): ADR 0008's
/// layer 3, a feature on the entity with its item. docs/hardcoded-content.md's "The Shadow" has the original's.
/// </summary>
/// <param name="Entity">The Shadow.</param>
/// <param name="Item">What the party carries for it to merge rather than vanish: the Horn of Kynon.</param>
/// <param name="Merges">Whom it merges with: Alaron.</param>
/// <param name="Radius">
/// How far its aura reaches, and within which a combatant's field is held to 1 (<c>Combat.NearShadow</c>, <c>0x80023F34</c>) and the
/// AI's paths are cut (<c>EnemyAI.ShadowSmoothPath</c>, <c>0x8005A734</c>): 10.
/// </param>
/// <param name="VanishRounds">
/// Round ends its aura works without the item; at the next it vanishes (<c>Combat.ShadowAura</c>, <c>0x80023AD0</c>): 4.
/// </param>
/// <param name="MergeReach">What its body's and the merger's radii are lengthened by for them to touch, and merge.</param>
/// <param name="Dexterity">Its base Dexterity as it's made while the party carries the item, which gives it the turns to reach him.</param>
/// <param name="Cinematic">What a merge plays (<c>Combat.MergeShadow</c>, <c>0x800675C0</c>, then game mode <c>0xE</c>).</param>
/// <param name="Boost">What the merger gains.</param>
public sealed record ShadowRule(RecordId Entity, RecordId Item, RecordId Merges, float Radius, int VanishRounds, float MergeReach,
    int Dexterity, RecordId Cinematic, ShadowBoost Boost);

/// <summary>
/// What merging with the Shadow gives (<c>Combat.ShadowMergeBoost</c>): <paramref name="Stats"/> added to the merger's base,
/// <paramref name="Protection"/> to its protection; for each skill the Shadow has, the merger's raised by the difference, or by
/// <paramref name="Least"/> if that isn't positive; each of the Shadow's spells learned, or raised the same way to at most
/// <paramref name="SpellCap"/>; then <paramref name="Flag"/> set.
/// </summary>
public sealed record ShadowBoost(IReadOnlyList<StatGain> Stats, int Protection, int Least, int SpellCap, RecordId Flag);

/// <summary>A stat (<c>aidyn:stat/n</c>) raised by so much.</summary>
public sealed record StatGain(RecordId Stat, int Amount);

/// <summary>A flask the party holds, and how far apart it makes the AI's combatants keep (<c>Combat+0x12</c>).</summary>
public sealed record FlaskDanger(RecordId Item, int Radius);

/// <summary>Winning a battle with <paramref name="Entity"/> among the enemy list's first four sets <paramref name="Flag"/>.</summary>
public sealed record BossFlag(RecordId Entity, RecordId Flag);

/// <summary>The game over: <paramref name="Script"/>, started from its dialogue object in <paramref name="Map"/> (the Dreamscape's).</summary>
public sealed record GameOverScene(RecordId Map, RecordId Script);

/// <summary>
/// A battle's own rules, by its encounter flag (<c>Combat.NonLethal</c>, <c>0x800DA0DC</c>, and <c>Combat.Exit</c>'s branch,
/// <c>0x80023060</c>, for the Goblin Ambush).
/// </summary>
/// <param name="NonLethal">
/// Losing isn't the game's end: the party's defeat line is <paramref name="Speaker"/>'s own (the combat strings' <c>0x74</c>),
/// then after 120 frames the screen fades, the encounter flag is set, the monster party fought is remembered, and the clock
/// goes on to 6:00 the next day (<c>Combat.StateDefeat</c>, <c>0x80022590</c>).
/// </param>
/// <param name="QuietVictory">A win shows no victory line (<c>TurnOrder.Victory</c>).</param>
/// <param name="Speaker">Whose portrait the non-lethal defeat line shows: Alaron.</param>
/// <param name="End">What follows the battle however it ends, won, lost or fled.</param>
/// <param name="HealsLeader">The party's first member is healed in full as the battle starts (<c>Combat.Init</c>, <c>0x80022D1C</c>).</param>
/// <param name="KeelOver">A member who keels over after so many hits rather than dying (<c>Combatant.ShadowFightTrick</c>, <c>0x80065DCC</c>).</param>
public sealed record EncounterRule(RecordId Flag, bool NonLethal = false, bool QuietVictory = false, RecordId? Speaker = null,
    EncounterEnd? End = null, bool HealsLeader = false, KeelOver? KeelOver = null);

/// <summary>
/// A member held up in a battle with its own rules (<c>Combatant.ShadowFightTrick</c>, <c>0x80065DCC</c>, the Goblin Ambush's hold on
/// Alaron): each blow that would hurt <paramref name="Member"/> takes 1 hit point instead, and the <paramref name="Hits"/>th takes its
/// Strength to 0, so it can't act.
/// </summary>
public sealed record KeelOver(RecordId Member, int Hits);

/// <summary>
/// What follows a battle with its own rules, however it ends (<c>Combat.Exit</c>): the party's leader's Strength back to its base,
/// <paramref name="Flag"/> set, the party taken to <paramref name="Place"/>, and <paramref name="Healed"/> healed in full.
/// </summary>
/// <param name="Position">Where in the chunk trek starts again.</param>
/// <param name="Heading">The way the player faces there, across the ground.</param>
public sealed record EncounterEnd(RecordId? Flag, ChunkRef Place, System.Numerics.Vector3 Position, System.Numerics.Vector2 Heading,
    RecordId? Healed, bool RestoresStrength);

/// <summary>
/// A weapon that flies (<c>Combatant.LoadWeaponScene</c>, <c>0x80066CF8</c>, and <c>Combatant.WeaponMuzzle</c>, <c>0x80066DD0</c>).
/// </summary>
/// <param name="Weapon">The weapon.</param>
/// <param name="Scene">What flies: a bow's arrow (<c>aidyn:scene/0x348F</c>), or null for the weapon's own model (<see cref="WeaponRecord.Model"/>), as a thrown one does.</param>
/// <param name="Speed">Its muzzle number, the first of <c>WeaponMuzzle</c>'s: the higher, the faster and flatter it flies.</param>
/// <param name="Turn">Degrees it's turned about the vertical as it flies, <c>WeaponMuzzle</c>'s fourth: 90 for the Javelin, else 0.</param>
public sealed record Missile(RecordId Weapon, RecordId? Scene, float Speed, float Turn = 0);

/// <summary>Whether the party can flee a battle under the encounter flag <paramref name="Flag"/>.</summary>
public sealed record EncounterFlee(RecordId Flag, bool CanFlee);

/// <summary>
/// The combat menus' words and Sense Aura's, which 1.0 keeps as C strings in its code segment, not in a string table. A Sense Aura
/// line's <c>%d</c>, <c>%lu</c> and <c>%s</c> are printf's, as the ROM has them.
/// </summary>
/// <param name="LayOnHands">The skill list's row for laying on hands (<c>CombatMenu.PickLayOnHands</c>), <c>0x800D4880</c>: "Lay On Hands".</param>
/// <param name="HealWithHerbs">Its row for healing with herbs (<c>CombatMenu.PickHealWithHerbs</c>), <c>0x800D4890</c>: "Heal With Herbs".</param>
/// <param name="Perform">The Troubadour's row (<c>CombatMenu.PickTroubadour</c>), <c>0x800D48A0</c>: "Perform".</param>
/// <param name="NoSpells">What C-down's list says when it has no spells or skills (<c>CombatMenu.OpenSpells</c>), <c>0x800D4858</c>.</param>
/// <param name="NoItems">What C-up's list says when it has no potions or item spells (<c>CombatMenu.OpenItems</c>), <c>0x800D4834</c>.</param>
/// <param name="Aura">Sense Aura's popup.</param>
public sealed record CombatText(
    string LayOnHands,
    string HealWithHerbs,
    string Perform,
    string NoSpells,
    string NoItems,
    SenseAuraText Aura);

/// <summary>
/// The lines of Sense Aura's popup (<c>Entity.SenseAura</c>, <c>0x80070070</c>), a line more at each power over 0, 2, 4, 6 and 8
/// (<c>Entity.AppendResistanceLine</c>, <c>0x8006FFB0</c>, builds a resistance's).
/// </summary>
/// <param name="Follower">The line for a party member who isn't the leader, <c>0x800D3250</c>.</param>
/// <param name="Leader">The line for the party's leader, <c>0x800D325C</c>.</param>
/// <param name="Hitpoints">The total hitpoints, printf's <c>%d</c>, <c>0x800D3264</c>.</param>
/// <param name="Level">The level, printf's <c>%lu</c>, <c>0x800D3278</c>.</param>
/// <param name="Protection">The armor's total protection, printf's <c>%d</c>, <c>0x800D3284</c>.</param>
/// <param name="Immune">A resistance line, printf's <c>%s</c> the element, for a multiplier of 0 or less, <c>0x800D3214</c>.</param>
/// <param name="Resistant">A resistance line for a multiplier above 0 and below <paramref name="ResistantBelow"/>, <c>0x800D3224</c>.</param>
/// <param name="Vulnerable">A resistance line for a multiplier of <paramref name="ResistantBelow"/> or more, <c>0x800D3234</c>.</param>
/// <param name="ResistantBelow">
/// The float the resistance's multiplier (<c>Resistance.Damage</c>) is compared with (<c>0x800D3248</c>): 2.0. A line shows only for an
/// element the entity has a resistance entry for.
/// </param>
/// <param name="Elements">
/// The element names, by element: <c>String/Common</c>'s lines 241 to 257 (<c>0x800E0C00</c> points at the first, set by the loader
/// <c>0x80083620</c>, and the element byte indexes it).
/// </param>
public sealed record SenseAuraText(
    string Follower,
    string Leader,
    string Hitpoints,
    string Level,
    string Protection,
    string Immune,
    string Resistant,
    string Vulnerable,
    float ResistantBelow,
    IReadOnlyDictionary<Element, string> Elements);
