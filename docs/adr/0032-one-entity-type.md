# 0032. One entity type: a character sheet is optional, and dialogue entities are entities

Status: Accepted · Nov 3, 2026

## Context

The original has two tables of characters. The entity DB (`0x02xx`) holds everyone with a character sheet: party members, enemies, trainers and townsfolk. The dialogue entity table (`0x14xx`, 37 of them) holds simple speakers with a name, a portrait and a body: signs, animals and some people. The base plugin carried that over as `entity` and `dialogentity`. Four people are in both: Cadme (`0x1404` and `0x02DD`), Xibid (`0x1419` and `0x02DB`), Tamberlain (`0x1418` and `0x0214`) and Pochanargat (`0x1406` and `0x0224`). Scripts name one or the other, so a cut-scene's actors and a talker's body can be two different people with one name.

The sheet itself was fixed C# shapes: `attributes` with six named fields, `skills` and `weaponSkills` with one per skill, and the shield's skill apart. A mod could add a skill record but not give anyone a level in it.

Facts about one entity were kept in lists on the combat and progression singletons: who fights for the party, who never fights, who forbids or allows fleeing, who never moves, who can't be targeted, the bosses' flags and styles, whose model holds its weapon, who holds a bow the other way, and who trains for free. Two mods that each add to one conflict.

## Decision

- **There's one `entity` type.** An entity has a name, a portrait, a body, its sounds and its `perception`, and optionally a **`sheet`**: its category, aspect, school, level, attributes, skills, spells, gear, protection, damage, morale, resistances, experience and loot. An entity without a sheet can talk and walk but can't fight or join the party. Loaders and the core treat a missing sheet as that, and the editor shows it.
- **Dialogue entities become entities.** The 33 with no twin keep their keys under the entity type, `aidyn:entity/0x14xx`, with no sheet and a perception of 10 (what the port gave them). The four twins merge into their entity, whose body and portrait win, and the importer points every reference to them at the entity: SetSpeaker ops, dialogues' actors, camera ops on an actor, and NPC map objects.
- **The sheet's skills and stats are maps keyed by record ID**: `attributes: { "aidyn:stat/0": 14, … }`, `skills: { "aidyn:skill/6": 3 }` (a skill left out can't be learnt), and `weaponSkills: { "aidyn:weaponskill/8": 4 }`, with the shield as `aidyn:weaponskill/11`.
- **The weapon slots are named**: `weapons: { joins, spare, fights }`. A member wields `joins` on joining and carries the rest; an enemy wields `fights`, else `joins`; loot rolls each with its own chance (`loot.weaponChances`, keyed the same way).
- **The lists about one entity are fields on it**: `combat: { fightsForParty, bystander, flee, rooted, untargetable, bossFlag, bossStyle }`, `body.weaponsInModel` and `body.bowHand`, and `trainsFree`. `combat` and `trainsFree` need a sheet.

## Consequences

- References to the four twins' dialogue entity IDs change, and `dialogentity` is gone. Saves don't name dialogue entities, so only the item IDs (0031) need the save format's upconverter.
- One person is one record, so a cut-scene can't start with a body that isn't the speaker the scene names.
- A mod gives its characters levels in its own skills and stats, and its entities' combat traits don't conflict with another mod's.
- The core still holds the original's 24 skill slots and six attributes in a row; a mod's own skills and stats are read from the record but not yet played. Scripts' skill and stat checks still take the original's numbers until the scripts are typed.
