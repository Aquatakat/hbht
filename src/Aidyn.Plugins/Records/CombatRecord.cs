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
public sealed record CombatRecord(
    RecordId Id,
    RecordId Ally,
    IReadOnlyList<RecordId> Bystanders,
    RecordId Fallback,
    IReadOnlyList<RecordId> NoFlee,
    IReadOnlyList<RecordId> Flee,
    IReadOnlyList<EncounterFlee> FleeByEncounter,
    IReadOnlyList<RecordId>? Rooted = null);

/// <summary>Whether the party can flee a battle under the encounter flag <paramref name="Flag"/>.</summary>
public sealed record EncounterFlee(RecordId Flag, bool CanFlee);
