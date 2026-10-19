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
/// <param name="Flee">The Shadow, with whom the party can flee.</param>
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
public sealed record CombatRecord(
    RecordId Id,
    RecordId Ally,
    IReadOnlyList<RecordId> Bystanders,
    RecordId Fallback,
    IReadOnlyList<RecordId> NoFlee,
    IReadOnlyList<RecordId> Flee,
    IReadOnlyList<EncounterFlee> FleeByEncounter,
    IReadOnlyList<RecordId>? Rooted = null,
    IReadOnlyList<Missile>? Missiles = null,
    IReadOnlyList<RecordId>? CloseRange = null,
    IReadOnlyList<BossFlag>? Bosses = null,
    IReadOnlyList<RecordId>? BossStyles = null,
    IReadOnlyList<WeaponClass>? DroppedClasses = null,
    IReadOnlyList<EncounterRule>? Encounters = null,
    GameOverScene? GameOver = null)
{
    /// <summary>The rules of the battle under encounter flag <paramref name="flag"/>, if it has any.</summary>
    public EncounterRule? Encounter(RecordId? flag) => flag is { } f ? Encounters?.FirstOrDefault(e => e.Flag == f) : null;
}

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
public sealed record EncounterRule(RecordId Flag, bool NonLethal = false, bool QuietVictory = false, RecordId? Speaker = null,
    EncounterEnd? End = null);

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
