# Items and inventory (roadmap 2.3)

`Aidyn.Core.Items`. The DB tables' layouts are in [db](../formats/db.md); the tables in code are read by `ItemTables` (`Aidyn.Rom.Engine`).

Ported from `gameclasses/` (`item.cpp`, `generic.cpp`, `armor.cpp`, `weapon.cpp`, the equipment half of `entity.cpp` and `party.cpp`), the inventories (`PartyInventory.cpp`, `GenericInventory.cpp`), shops (`WidgetBarter*.cpp`, `WidgetInvShop.cpp`, `InitShop`), containers (`objectJumpTables.cpp`, `chestdb.cpp`, `lockpick.cpp`, `trapmenu.cpp`) and crafting (`potionCraft.cpp`, `Armorcraft.cpp`, `HealMenu.cpp`). Every formula was checked against the debug build's machine code; where the decomp is wrong is listed [below](#checked-against-the-machine-code). The menus come with the UI.

## The records

- **Weapons** (`aidyn:weapon/0x07…`), **armor** (`aidyn:armor/0x05…`, shields `0x06…`) and **items** (`aidyn:item/…`, everything else, potions among them) carry their DB fields, an icon and what they do for whoever wears them (`GearBonuses`): an aspect only that aspect can wear, a stat and a skill they raise or lower, a spell the wearer can cast from them (level and charges), an enchantment that's on while they're worn, and a resistance.
  - A weapon's spell level is its own field (the ROM's byte at `0x1D`); armor's and gear's is in the bonuses. The bonuses' first spell byte is the charges for all three (`malloc_equip_spell`).
  - Range is the ROM's byte × 5 / 3, up to 255 (`WeaponDB::Load`).
  - Armor's Dexterity and Stealth are signed: penalties, or Stealth bonuses on leathers.
  - Potions aren't in the item DB. Potion *n* (`aidyn:item/0x10nn`) is named by line *n* of its own string table and priced by a table in code (`potion_prices`); all share one icon. What they do comes with combat (2.5).
  - The Archmage Staff is `fixed`: once equipped it can't be taken off. Niesen's entity has it as its `soleWeapon`, the only weapon he can equip.
- **Traits.** `ownSlot`: the item has an inventory slot of its own rather than one of the 128 general ones (reagents, hides, scrolls, potions, the key items and 13 unique pieces of gear). `mustTake`: loot can't be left behind while it holds the item (21 quest items and keys). Key items carry the flags they set (`keyItem`): `held` while the party has one, and a world map `region` they reveal.
- **Loot tables** (`aidyn:loot/0x08…`): gold, the chances an enemy drops its armor, shield and weapons, a reagent roll, and six item rolls. Only the first two rolls give a count between bounds. An empty roll still rolls its chance.
- **Shops** (`aidyn:shop/n`, by position in the shop DB): the shopkeeper, and the stock, each with a price multiplier. A priced slot is stocked only when its third byte is nonzero; the three extra slots sell at the base price.
- **Recipes** (`aidyn:recipe/0`–`19`): the Alchemist's 15 potions, then the Mechanic's 5 pieces of armor, with the skill, product, ingredients, the level needed and the least step of the check. The Mechanic's table names hides and armor by DB position, not ID.
- **The inventory's rules** (`aidyn:inventory/0`): the stack limit (99), the general slots (128), the three reagents, the default icon, the Merchant step and sell share, the item no loot or shop takes (the Cyclops Club), the Aspect potion that becomes a Healing potion, and the first-potion help flag (3612).
- **Skills** have their icons.

The reserved slot table in code lists 80 IDs: scroll `0x110E` twice, and scrolls `0x1106` and `0x110B`, which the item DB doesn't have.
