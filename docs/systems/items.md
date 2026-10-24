# Items and inventory (roadmap 2.3)

`Aidyn.Core.Items`, and the party's side in `Party.Items.cs` (equipping, taking items for scripts, reading a scroll). The DB tables' layouts are in [db](../formats/db.md); the tables in code are read by `ItemTables` (`Aidyn.Rom.Engine`). The menus are in [ui](ui.md).

- `ItemCatalog`: every item the rules can name (`Item`, whichever record it comes from) and the inventory's rules. `ItemInstance`: one held item, with its battery and charges.
- `Equipment`: what a character wears and wields, and what that does to the sheet.
- `PartyInventory` and `ItemPile`: the party's stacks, and a container's, beaten party's or shop's.
- `Potions`: drinking. `Shop` and `LootWindow`: the barter and loot windows. `Containers` and `Container`: locks, traps and loot. `Crafting`: the Alchemist, Mechanic and Healer.

The original's files these port: `item.cpp`, `generic.cpp`, `armor.cpp`, `weapon.cpp`, `entity.cpp` and `party.cpp`, the inventories, the barter widgets, `chestdb.cpp`, `lockpick.cpp`, `trapmenu.cpp`, `potionCraft.cpp`, `Armorcraft.cpp` and `HealMenu.cpp`.

## The records

- **Weapons** (`aidyn:weapon/0x07…`), **armor** (`aidyn:armor/0x05…`, shields `0x06…`) and **items** (`aidyn:item/…`, everything else, potions among them) carry their DB fields, an icon and what they do for whoever wears them (`GearBonuses`): an aspect only that aspect can wear, a stat and a skill they raise or lower, a spell the wearer can cast from them (level and charges), an enchantment that's on while they're worn, and a resistance.
  - A weapon's spell level is its own field (the ROM's byte at `0x1D`); armor's and gear's is in the bonuses. The bonuses' first spell byte is the charges for all three (`malloc_equip_spell`).
  - Range is the ROM's byte × 5 / 3, up to 255 (`WeaponDB::Load`).
  - Armor's Dexterity and Stealth are signed: penalties, or Stealth bonuses on leathers. Gear's stat byte is tested against `0xFF`, not for nonzero (`ItemInstance::InitGear`). The spell bytes are in [db](../formats/db.md).
  - Potions aren't in the item DB. Potion *n* (`aidyn:item/0x10nn`) is named by line *n* of its own string table and priced by a table in code (`potion_prices`); all share one icon. Drinking one is [below](#potions); what each does is its effects ([magic](magic.md#potions-and-flasks)).
  - The Archmage Staff is `fixed`: once equipped it can't be taken off. Niesen's entity has it as its sole weapon (`EntityRecord.SoleWeapon`), the only weapon he can equip.
- **Traits.** `ownSlot`: the item has an inventory slot of its own rather than one of the 128 general ones (reagents, hides, scrolls, potions, the key items and 13 unique pieces of gear). `mustTake`: loot can't be left behind while it holds the item (21 quest items and keys). Key items carry the flags they set (`keyItem`): `held` while the party has one, and a world map `region` they reveal.
- **Loot tables** (`aidyn:loot/0x08…`): gold, the chances an enemy drops its armor, shield and weapons, a reagent roll, and six item rolls. Only the first two rolls give a count between bounds. An empty roll still rolls its chance.
- **Shops** (`aidyn:shop/n`, by position in the shop DB): the shopkeeper, and the stock, each with a price multiplier. A priced slot is stocked only when its third byte is nonzero; the three extra slots sell at the base price.
- **Recipes** (`aidyn:recipe/0`–`19`): the Alchemist's 15 potions, then the Mechanic's 5 pieces of armor, with the skill, product, ingredients, the level needed and the least step of the check. The Mechanic's table names hides and armor by DB position, not ID.
- **The inventory's rules** (`aidyn:inventory/0`): the stack limit (99), the general slots (128), the three reagents, the default icon, the Merchant step and sell share, the item no loot or shop takes (the Cyclops Club), the Aspect potion that becomes a Healing potion, and the first-potion help flag (3612).
- **Skills** have their icons.

The reserved slot table in code lists 80 IDs: scroll `0x110E` twice, and scrolls `0x1106` and `0x110B`, which the item DB doesn't have.

## Equipment

A character (`Character.Equipment`) wields a weapon and wears body armor, a shield and up to 12 pieces of gear. Equipping applies what an item does to the sheet, and taking it off undoes it (`Entity::ApplyEquipment`, which applies the item's own stat bonus when it isn't given one).

- **Stats.** A bonus changes the modified value; Endurance's changes the base value too, so it raises maximum hit points. A bonus that would take Endurance to 0 or below leaves it at 1, as one to the Level does while Endurance is at 0. Taking a bonus off is bounded the same way, so the base can drift: taking off +5 Endurance with 3 left subtracts 2. A Stamina bonus isn't applied: it's the item's **battery**, which a spell's cost draws on before the caster's Stamina (weapon, armor, shield, then gear) and camping refills by the fraction it heals (`Entity::EquipStamina`, `ItemCampStamina`).
- **Skills.** A bonus applies only while the base level isn't 0. When training takes a skill from 0 to 1, what's worn adds its bonus then, each worn item's amount (`WidgetSkillTrain::Purchase`); weapon skills and the shield don't get this.
- **Armor** adds its Dexterity and Stealth whatever the levels. Its protection and the shield's are counted apart (`Entity::GetArmorProtect`); gear adds its protection and damage to the sheet's.
- **Enchantments** are on while armor and gear are worn, as effects that never run down; a weapon's is cast as it strikes. Charges go only from wands and scrolls worn as gear ([magic](magic.md#gear)).

What can be equipped (`Party::GetEquipError` and the item kinds' functions):

| Kind | Rule |
| --- | --- |
| Any | An aspect other than none must be the character's. |
| Weapon | Base Strength at least its need, its weapon skill above 0, and for Niesen only the Archmage Staff. Replaces the one wielded. |
| Body armor | Anyone. Replaces the one worn. |
| Shield | The shield skill above 0. Replaces the one carried. |
| Helmet, cloak, gloves, wand, belt, boots, amulet | A free gear slot, then base Strength and Intelligence at least its needs. Replaces one of its kind. |
| Ring | As gear, two at most, never swapped. |
| Scroll | In trek, reading one learns its spell (below); in combat, one with a spell is worn as a ring is, three at most. |
| Potion, key, other | Can't be equipped. |

A replaced item goes back to the inventory; if it can't, the new one does instead. An equipped item takes its stack's battery and charges, and taking it off gives the stack its own, even if the stack was there already (`UpdateItemStatCharges`). Taking a worn item off, by the item itself or by its ID for a script, is `Equipment.Remove`, which undoes its bonuses as putting it on applied them. Gear's free slot is checked before the old piece comes off, so with all 12 worn nothing can be swapped.

**One deviation.** The Archmage Staff can't be taken off (`Party::RemoveWeaponsFrom`), but the original lets whoever wields it equip another weapon, and the staff vanishes with its bonuses still applied. The port refuses to replace it. Only Niesen wields it in the base game, and he can wield nothing else.

**Starting gear** (`Entity::Init`, `EquipFunc0`): an entity wears its armor, carries its shield if it has the skill, and wields the first of its three weapons it can. The others go to the party's inventory as it joins, if it wields one; one it can't wield while it wields none is lost. Enemies choose their weapons another way ([combat](combat.md)).

**Reading a scroll** (`Party.ReadScroll`, the original's `Party::UseScroll`): a character who can't learn Wizard can't; nor can one who knows the spell, has less base Wizard than the spell, or is of another school when neither is Chaos nor the spell unschooled. Otherwise they learn it at level 1 and the scroll is used up.

## Potions

Potion *n* is `aidyn:item/0x10nn`, the original's `PotionEnum` *n*: the Fire, Inferno, Sleep Gas and Acid flasks, then Healing, Stamina, Curing, Antidote, Restore, Strength, Dexterity, the left-out Aspect and Jumping, Clarity, Charisma, Defence and Stealth. The core knows them only by their records ([ADR 0021](../adr/0021-instant-effects-land-in-one-place.md)): each has its effects and its `use`, so a plugin's potion is drunk or thrown as the base game's are. What each does, and when it can be drunk (`Potions.CanDrink`, `Entity::CanUsePotion`), are [magic](magic.md#potions-and-flasks)'s. Drinking one from the pause menu (`Potions.Drink`, `Entity::UsePotion`) uses one up if it does something now, and otherwise says why and uses nothing. A potion drunk changes the Party tab's header as a member change does, the hit points crossfading and the bar tweening ([ui](ui.md#changing-member-and-list)).

The two refusals, "cannot be used right now" and "cannot be used in Trek mode", are C strings in code, not string table lines; the importer reads them onto the inventory record.

## The inventory

The party's (`PartyInventory`, the original's name too) holds each item as one stack, up to 99. Most items share 128 general slots; reagents, hides, scrolls, potions, key items and 13 unique pieces of gear have slots of their own. A new stack takes the quantity it's given, uncapped; only adding to a stack caps it. A key item sets its region flag and its held flag when first gained, and clears the held flag when the last is taken. The Aspect potion becomes a Healing potion as it's gained.

Loot and shop stock are a **pile** (`ItemPile`, the original's `GenericInventory`) of up to 64 stacks, which refuses the Cyclops Club.

**Scripts** (`IScriptWorld`): an item is the party's if it's carried or equipped (`Party.HasItem`, the original's `Party::hasItem`), and taking one takes it from the inventory, or else destroys the first that's equipped (`Party.TakeItem`). Both look at gear only in the first slots as many as are worn, so a piece past a gap left by one taken off is missed.

## Shops

A script's shop command opens the first shop of whoever is speaking (`Shop.Open`, the original's `InitShop`). Its stock is a pile with one stack of each item at the item's price × the slot's multiplier, truncated to 16 bits; an item listed twice is one stack whose price is multiplied again. Shops never run out.

**The barter factor** is set as the window opens (`Party::Barter`): the shopkeeper's Merchant level, as their record has it, less the party's best, × 0.05. The shopkeeper's Intelligence is unused. The party's best is the living member with the highest modified Merchant above 0, the later on a tie, and bartering costs them 15 less that in Stamina; if they can't pay it, the party's Merchant counts as 0. Shopkeepers have 0 to 10.

| | Price |
| --- | --- |
| Buying | the stock's price × (1 + factor) |
| Selling | the item's price × (1 − factor) × ½ |

Both are worked in single precision and truncated to 16 bits (`WidgetInvShop::GetGoldPrice`). An item that would sell for 1 gold or less is refused; that's the only sell filter, and it's what keeps key items (price 0) out of shops. Equipped items must be taken off to sell. Selling offers 1, 5, 10 and 20 below what's held, and all; the shop doesn't take what it buys.

**Appraisal** (C-up on the shop's stock, `Party::ApraisePrice`): the member best at it, dead or not, judges by Intelligence + Merchant × 6 (x), as (−0.0186603x² + 2.366025x + 50) / 100. The code clamps it to 125 after dividing by 100, so the bound is never reached. The item's base price × that × 3 / the asking price, rounded, picks one of Common's lines 384–390 (counting from 0), from "This is highway robbery!" up, past 6 the last.

Buying or taking the first potion sets flag 3612, whose help is a trigger (2.10).

The shop's window is in [ui](ui.md#the-shop).

## Containers

`Container` is a chest, sack, crate or barrel, or a herb, spice or gem source, as it is in the world (`loot_func`). Using it:

1. Once its opened or exploded flag is set, it's gone. (Without an opened flag, the original remembers it for a while in its `VoxelChart`, which is the runtime's.)
2. A **source** gives the reagent of its kind (a herb source herbs, a spice source spice): its count, which the map holds as gold, times the Ranger's share, truncated to a byte and at least 1. The share is rolled as the chunk's objects are set up: the best Ranger's Intelligence + Ranger × 7, times d100 / 100, from 0 to 1 (`Party::HarvestSkill`). So the Ranger skill brings the count up to the source's own.
3. A **trap** comes first. Disarming it (`Party::DisarmSkill`): the member with the highest modified Mechanic above 0, the first on a tie, among those with the Stamina it costs (10 less their level, at least 1), rolls d100 against Intelligence × 4 + Mechanic × 10; passing, they pay, and the step must reach the trap's level. With no such member it fails. Failing sets it off ([the explosion](map-objects.md#containers)): the container is destroyed with what it held, its exploded flag set, and each living member takes the trap's level × d4 from Endurance, down to 1 (a first d4 giving less than 2 gives 1; otherwise a second d4 counts). A trapped door works the same.
4. A **lock** next. Picking it (`Party::Lockpicking`) needs no roll: the best thief, dead or not, can pick a lock below their Thief level counted up to 10, for 10 less that in Stamina, at least 1. Locks of 10 or more can't be picked. The messages tell how far beyond the thief it is: 1–2, 3–5 or more.
5. **Opened**, it sets its flags and rolls its loot table, if it has one, which replaces its own gold: gold rolled between the bounds and doubled; a reagent, spice, herb or gemstone picked before its chance is rolled; then the six drops. Then it adds the item it always holds. With no items it gives the gold alone; otherwise the loot window opens with the gold already given.

Picking and disarming last until the chunk is loaded again, but each leads straight to opening.

The **loot window** (`LootWindow`, and the menu over it, [ui](ui.md#the-loot-window)) takes a stack at a time, as many as the party's stack has room for, each new stack with the pile's battery and charges. The party can drop what it carries, which is lost, unless it's worth 1 gold or less at half price, or it's worn. It can't close while it holds a must-take item. `Container.Contents` and `Container.Reagent` give what opening and harvesting give without setting the flags, for the editor.

## Crafting

The skills used from the pause menu (`Crafting`; in a battle the Healer's two are combat's, through the same functions). A check here rolls d100 against a bonus, and a roll below it gives the step for what's left (`gSkillCheck`); some cost Stamina only when they pass.

| Task | Offered | Check | Cost | Result |
| --- | --- | --- | --- | --- |
| **Alchemist**: a potion | Recipes up to the modified Alchemist | Intelligence × 3 + Alchemist × 6 | The reagents, whatever happens | One potion if the step reaches the recipe's (0 for all) |
| **Mechanic**: armor | Recipes up to the base Mechanic, whose hide the party has | (Mechanic × 5 + Intelligence + Dexterity) × 2 | 5 less Mechanic Stamina, at least 1, on passing; the hide, whatever happens | The armor if the step reaches the recipe's |
| **Healer**: lay on hands | Another living member | None | 3 Stamina | Heals Healer × 2 + 5 |
| **Healer**: herbs | A living member | Intelligence × 4 + Healer × 11 | A herb, then 5 less Healer Stamina, at least 1 | Heals 4 × the step |
| **Healer**: restore | A living member's attribute damage has lowered | Intelligence × 2 + Healer × 5 | A herb, then 15 less Healer Stamina, at least 0 | Restores 2 × the step, up to its base value |

A potion isn't brewed while the party holds 99. The Mechanic's check rolls, and its Stamina is paid, before the hide is looked for. A herb is used up even when the Healer hasn't the Stamina.

The pause menu's popups for these, and what they say, are in [ui](ui.md#the-party-tabs-actions). The Mechanic's popup makes each recipe's own armor; the Alchemist's messages say Flask for the flasks and Potion for the rest.
