# 0031. One item type: weapons and armor are items with a kind

Status: Accepted · Nov 3, 2026

## Context

The original keeps weapons, armor and the other items in three DB tables, and the base plugin carried that over as three record types: `aidyn:weapon/0x07xx`, `aidyn:armor/0x05xx` and `0x06xx`, and `aidyn:item/…`. To the player they're all items: each has a name, an icon and a price, sits in the inventory, is bought, looted and crafted, and most are worn. The three records repeated the same fields under different names (`weapon.expModifier`, `armor.expBonus` and `item.expMultiplier` are one thing, loot experience), and every reference to an item had to say which table it was in. Rules that take any item, such as the inventory, shops and loot, looked in all three.

Facts about one item were also kept elsewhere, in lists on singletons: which weapons fly (`combat.missiles`), which ranged weapons strike in melee (`closeRange`), which flasks the AI spreads out against (`flaskDanger`), which items no inventory takes (`inventory.refused`) or that become another (`replaced`), and which potions outlast a battle (`magic.keptAfterBattle`). Two mods that each add to one of those lists conflict, since a list is replaced whole (0005).

## Decision

- **There's one `item` type.** A weapon is `aidyn:item/0x07xx`, body armor `aidyn:item/0x05xx` and a shield `aidyn:item/0x06xx`: the key is the original's `ItemID`, as before, and the DB type byte keeps the three apart. `kind` gains `Weapon`, `Body` (the original's armor type, 5) and `Shield`, the original's numbers.
- **Every item has the same fields**, each optional but its name, icon, price and kind: `model`, `traits`, `description`, `requirements`, `bonuses`, `lootExperience`, `fixed`, `damage` and `protection` (added while worn), a shield's `defense`, and a potion's `effects`, `duration`, `presentation` and `use`, a key item's `keyItem`.
- **What only a weapon has is grouped in `weapon`**: `{ skill, damage, range, hit, animation, damageType?, missile?, strikesInMelee, sounds? }`, required on a `Weapon` and refused on any other kind. Grouped rather than flat, so that a ring's record shows no weapon fields, and because a weapon's `damage` (its dice) isn't the worn `damage` every item has. `skill` is the weapon skill it uses (the original's weapon class, 0033).
- **`requirements`** is a map from a stat's ID to the least base value that wears it, which replaces `requiredStrength` and `requiredIntelligence`. **`bonuses`** is `{ aspect, stats, skills, spell, enchantment, resistances }`, with `stats` and `skills` maps from the stat's or skill's ID to an amount and `resistances` a map from an element's ID to a multiplier. Armor's `dexterity` is a negative `stats` entry on Dexterity and its `stealth` a `skills` entry on Stealth.
- **A gear skill bonus counts only while the wearer's base skill is above 0**, but armor's stealth counted always. That exception is kept as data: the `inventory` record's `skillBonusesApplyUnlearnt` lists the kinds whose skill bonuses count even for a skill the wearer hasn't learnt, `["Body", "Shield"]`.
- **The lists about one item are fields on it**: `weapon.missile`, `weapon.strikesInMelee`, `aiSpread` (the flask's), `refused`, `becomes` and `keptAfterBattle`.

## Consequences

- Every reference to a weapon or armor changes its ID: entities' gear, shops' stock, recipes, loot, containers, scripts' item ops, the combat and magic records' items, and saves. The base plugin is re-imported; saves made before this need the save format's upconverter (format 2).
- Rules that take any item look up one type. Loot experience is one field, so `Battle.ExperienceValue` no longer dispatches on the record's type.
- A mod can make a sword that also carries a spell, or a ring with a requirement on any stat, from the same fields, and its items' rules don't conflict with another mod's.
- The equip slot stays the `ItemKind` enum, since the menus are laid out per slot (0014).
