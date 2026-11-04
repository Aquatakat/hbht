# 0033. Closed enums become records: schools, elements, weapon skills and animation requests

Status: Accepted · Nov 3, 2026

## Context

Some of the original's numbering came into the plugin as C# enums: the magic schools, the 17 elements, the 11 weapon classes, and the animation request a body asks its actor for (a plain number). A mod can't add a value to an enum. A new school of spells, a new damage type, a new kind of weapon or a new animation would each need the port's code changed.

Weapon classes were also a copy of the `weaponskill` records, one per class, kept apart from them. And facts about each value were kept elsewhere: Sense Aura's element names in the combat record's text, the weapon classes that can drop as loot in a combat list, the school icons in code.

## Decision

- **Four record types**, each keyed by the original's number, so `aidyn:school/5` is Star and existing effects and ops keep their numbers:
  - **`school`** (`aidyn:school/0`–`5`): its name, icon, whether a character of it learns every school's spells (Chaos), and whether its effects can be dispelled (not the unschooled, 4).
  - **`element`** (`aidyn:element/1`–`16`): its name, which Sense Aura shows, and the elements a resistance to it covers too (Physical covers Cutting and Smashing; Magic the magical ones). The original's element 0, none, is no record: a weapon with no damage type leaves it out.
  - **`weaponskill`** takes the weapon classes' place: the `WeaponClass` enum is gone, a weapon names its `weaponskill`, and each says whether its weapons can drop as loot (`drops`).
  - **`request`** (`aidyn:request/0`–`25`): its name and what the engine asks it for. A weapon's `animation` names the request its attack asks for; actors' branches, emitters, sound keys, the tripwire's `animate` and the body's strikes name requests. A branch for any request leaves its request out.
- **Entities', spells' and effects' `school`, `element` and `damageType` are IDs.**
- Whether an animation loops or can be interrupted belongs to the actor's states and branches, not the request, so request records don't carry it.

## Consequences

- A mod adds a school, an element, a weapon skill or a request as a record in its namespace, and its spells, weapons and actors name it.
- Where the original's rules name one value, the core still names its base ID: Chaos's spellbook order, which element meets which spell, the Thrown skill a flask trains, and the requests the engine asks for (idle, walk, cast and the rest). [hardcoded-content](../hardcoded-content.md) lists them.
- Scripts' PlayAnimation ops keep their numbers until the scripts are typed; the request records are keyed by the same numbers.
