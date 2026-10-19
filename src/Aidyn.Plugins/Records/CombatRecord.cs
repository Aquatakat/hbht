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
    IReadOnlyList<RecordId>? CloseRange = null);

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
