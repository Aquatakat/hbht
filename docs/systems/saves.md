# Saves and the game state (roadmap 2.8)

`HBHT.Core.GameState` is the game as it stands, `HBHT.Plugins.Saves` the port's save format, and `HBHT.Rom.Rig.CheckpointSave` turns the user's checkpoints into saves. Ported from `QuestData.cpp`, `saveParty.cpp` and `voxelChart.cpp`.

## The game state

`GameState` holds the flags with the engine's state flags, the clock, the time played, the party with its inventory, what map objects remember, the random streams (ADR 0006) and where the player is. It's the `IScriptWorld` scripts run against, so the editor's script player and the runtime share it. `CharacterSaves` (`HBHT.Core.Characters`) is the mapping between the party and its sheets and `SaveParty` and `SaveCharacter`, saving and loading side by side (`SaveParty::SaveCharSheet`, `LoadCharSheet`, the spellbook's order); `GameState` calls it.

- **A new game** (`init_DBs`): the flags' initial values, noon on day 2 of week 6, and Alaron alone, wearing what his record gives him, with 1 gold. The new game record says so (`aidyn:newgame/0`, [hardcoded content](../hardcoded-content.md)), and where he arrives (`GameSession.Arrival`): the Overworld's I08 at (16, 62), 10 up.
- **Time passing** (`PassTime`) runs the clock and writes the calendar's flags, as trek does every frame. The time played is counted every tick, in every mode (`GameState.CountTick`, from `GameSession.Tick`).
- **Saving** (`Save`) takes a snapshot in the format below. **Loading** (`Load`) rebuilds the game from one with the rules, and lists in `Problems` what it couldn't use, such as a record no plugin has any more.

## Making and checking saves

The CLI makes saves without the game, with the installed plugins:

- `hbht save new [<save>]` writes a new game.
- `hbht save from <checkpoint> [<save>]` turns a checkpoint into a save (below).
- `hbht save show <save>` loads a save with the core and prints what it makes of it (time, place, flags, each member's sheet, gold, inventory, memories), then anything it couldn't use, with exit code 1.

They write to the data folder's `saves` unless given a path, where the System tab's save and load screens find them too, newest first. A save can be edited by hand, and `save show` says whether the game can use it.

Three more read and write the original's saves in a Controller Pak (below), for loading the original at a place the user is to check. They take the ROM, for the inventory's slots, and use the pak ares keeps beside it (`<rom basename>.pak`) unless given `--pak`:

- `hbht save pak <rom>` lists the pak's saves: slot, name, time played, place and party.
- `hbht save to-pak <rom> <save> [<slot>]` writes a save into a slot, A to P (default A), replacing the one there. It keeps the pak as it was beside it first (`<pak>.<time>.bak`), and makes a blank pak if there's none. A save without a place, a new game's, is written where a new game arrives, on the ground. Loading it skips the opening cinematic, but trek there starts the opening's script, whose dialogue object holds the arrival ([events](events.md#where-the-port-differs)). ares writes its pak back as it closes, so close it first.
- `hbht save from-pak <rom> [<slot>] [--out <save>]` turns a slot's save into one of the port's.

## The format

A save is JSON, like plugin source (ADR 0005), with records named by ID, so it can be read, diffed and edited by hand. Saves go in the data folder's `saves` (`DataPaths.Saves`). It holds what the original's save holds (`SaveDatStruct`), and no more: what the rules can work out again isn't kept, as the original's isn't.

A save made from the "Before final encounter" checkpoint, cut short:

```jsonc
{
 "format": 1,
 "label": "Before final encounter",
 "plugins": [{ "id": "aidyn", "version": "1.0.0" }],
 "playTime": 3905657,                  // 60 Hz ticks: 18:04:54
 "time": 354261576,                    // the in-game clock, in 60ths of an in-game second
 "place": { "mapSet": "aidyn:mapset/0", "chunk": "M06", "position": [69.065155, -19.997297, 72.22858], "heading": [-0.020800203, -0.99978364] },
 "flags": ["aidyn:flag/2", "aidyn:flag/3", "aidyn:flag/5", …],
 "party": {
  "gold": 321306,
  "members": [{
   "entity": "aidyn:entity/0x023F",   // Alaron
   "totalExp": 1602076, "spendableExp": 659576,
   "trained": { "intelligence": 21, "willpower": 15, "dexterity": 30, "endurance": 18, "strength": 21, "stamina": 79, "level": 20 },
   "current": { "intelligence": 21, "willpower": 15, "dexterity": 30, "endurance": 18, "strength": 4, "stamina": 78, "level": 20 },
   "skills": { "alchemist": 0, "loremaster": 2, "stealth": 10, "troubadour": 5, "warrior": 3, "wizard": 10, … },
   "weaponSkills": { "hafted": 1, "missile": 3, "pole": 0, "sword": 4, "thrown": 0 },
   "shieldSkill": 3,
   "weapon": { "item": "aidyn:weapon/0x0734", "charges": 3 },
   "armor": { "item": "aidyn:armor/0x0507" },
   "shield": { "item": "aidyn:armor/0x0612" },
   "gear": [],
   "spells": [{ "spell": "aidyn:spell/0x031D", "level": 10 }, …]
  }, …],
  "kept": [{ "entity": "aidyn:entity/0x0240", "totalExp": 628818, … }, …],   // Arturo, Becan and Godric, who left
  "inventory": [{ "item": "aidyn:weapon/0x0711", "quantity": 1 }, { "item": "aidyn:weapon/0x072B", "quantity": 11 }, …]
 },
 "memories": [
  { "mapObject": "aidyn:voxel/0x1063/2", "kind": "RandomLine", "value": 99, "lifetime": 7 },
  { "mapObject": "aidyn:voxel/0x1063/2", "kind": "SkillCheck", "skill": 1, "value": 2, "lifetime": 7 }
 ]
}
```

- **The place** is the map set, the chunk by name, the centre of the player's sphere in the chunk's coordinates, and the way he faces in X and Z. The original keeps the opposite of that, `facing`. Without a place, the game starts as a new game does (`GameState.StartsAsNewGame`): where a new game arrives, and in the runtime with the opening cinematic and its script, as the title's New Game. Such a save is a new game's, `hbht save new`'s or the editor's new game's, since every save made in play notes where the player stands (`GameSession.NotePlace`), as every one of the original's is made in trek. So the load screen's entry for one starts a new game whether it's loaded from the menu or the game began with it. The original's Continue goes straight to trek (`Title.ModeFrame`'s state 2 returns mode 1, `0x800551c0`), and its saves always have a place.
- **Flags** are the plain flags that are set, but for flag 1, which always is. Derived flags are evaluated on loading.
- **The journal's order:** its entries in the order they were added (`journal`). The original doesn't keep it, as it lists them in its table's order; the user chose to list them as they came. On loading, an entry that's on and not listed, as in a save made from a checkpoint, comes before those listed, in the table's order.
- **A member** is what the original saves of a character sheet (`SaveParty::SaveCharSheet`): experience, the trained and current stats, base skill levels, what's worn with each item's battery and charges, the spellbook, and the potions working on them, each with its power and what's left of its timer (`SavePotionEffect`). A skill that can't be learnt is left out. Loading applies the potions again after what's worn (`LoadCharSheetEffects`); the checkpoints' converter doesn't read them yet. The port also saves whether the member knows their True Name (`trueName`, from the sheet's bit in a checkpoint), as a plugin's trigger can give anyone theirs ([events](events.md)); the original's comes back from the record and Alaron's flag, 590, as it still does for a save without it. **Deviation:** the original saves none, since it saves its copies of the party's sheets (`SaveEntity::BenchParty`) and the copy (`SaveEntity::Copy`) leaves out the effects, so a potion's effect is gone after loading. The user chose to keep them.
- **Kept sheets** are those of members who left whose sheets the original keeps, saved as members are, which they rejoin with ([characters](characters.md#joining-and-leaving)). The original saves all nine of its slots (`QuestData::Save`): the party's four, then Abrecan's, Arturo's, Becan's, Godric's and Keelin's, each copied from the party for one in it. The port saves only those who left, and loading refuses a kept sheet for someone in the party.
- **Stacks and worn items** keep their battery and charges, where the item has them. Their price is the item's; only a shop's stock has its own.
- **Memories** are below.
- **The random streams** are the five streams' seeds, and the stars' twinkle's (`twinkle`, the port's own stream, [rendering](rendering.md#the-skys-objects)). Without them, the streams start as a new game's.
- **Alaron's name**, when the player renamed him in the options menu ([ui](ui.md#renaming-alaron)): `name` on his sheet, left out while it's the record's. The original saves his name with his spells (`SaveParty::SaveAlaron`, 20 bytes); the checkpoints' converter doesn't read it yet.
- **The screenshot** isn't in the save: the System tab writes it beside the save, a PNG of the same name, 208 × 156 ([ui](ui.md#saving-and-loading)). The original keeps a 64 × 32 one in the save. A save without one shows none.
- **The minimaps' seen pieces** (`minimaps`), each minimap's by their place in its record, where they're not as it starts ([ui](ui.md#the-minimap)). The original saves one bit a piece for all eight. Without them, the pieces start as a new game's.
- **The weather** (`weather`): what the roll left (its kind, what falls, its intensity, fog and storm, and the wind) and what's drawn as it sets in (what's falling, how hard, and the fog's thickness), as [weather](weather.md) has them. Without it, the weather starts as a new game's, and the rain flags stay as saved until it next changes.
- **Spells' effects** aren't saved, as the original's aren't: a battle's end takes every one off, and the only ones in trek are worn enchantments, which come back on as loading puts the gear on. **Deviation:** 1.0's load puts them on and then writes the save's empty effect slots over them, so they're gone until the item is put on again or a battle ends ([magic](magic.md#gear)). The user chose the fix. What a spell leaves on the sheet for good (Spirit Shield's protection, Wind's Stamina) is gone after a load, as in the original, which saves the trained and current stats only.
- **Not kept:** The original's volumes and resolution, which it saves too, are the player's settings in the port ([ADR 0013](../adr/0013-settings-in-the-options-menu.md)). The original also keeps three timers of how long the party has run, walked and sneaked (`Party::TickMoveCounters`), which nothing reads.

## Loading, as the original does

`GameState.Load` follows `QuestData::LoadFile`:

- **Flags.** Every plain flag is set as saved, but the new journal entry flag (3617), which `LoadFile` clears, and then the derived flags are evaluated. The original clears 3617 before it next evaluates them, lazily, from what its network held: a new game's values, when loading from the title screen. So every journal entry the save has turns on again, which sets 3617, and the last of them in the network's order becomes the newest (`newestJournal`), which isn't saved. The original nonetheless never says "A new journal entry has been added." after loading and highlights no entry in its journal, so something forgets both; what isn't known. The port clears 3617 once the save has loaded, and `FlagNetwork.Restore` leaves no entry the newest.
- **The clock** is set, and its flags written.
- **Each member** starts from their record (`Entity::Init`) with the save's experience, and trained and current stats, from which their base and modified values start (`CharStats::Load`). Base skills are the save's, with the modified levels at them; a skill the record can't learn stays so whatever the save says, with its modified level at 0, as the checkpoints hold it (`CharSkills::LoadOne`). Then the armor, shield, weapon and gear go on by the rules (`Entity::EquipArmor` and the rest, which check as `Party`'s equipping does), which applies what they do. One the rules refuse is lost, and listed. Gear goes in the first free slot, so loading closes gaps. The aspect, school, protection and damage come from the record, and the True Name is the save's, or without it the record's and the flags'.
- **The spellbook** is reordered, as the original saves spells school by school in the spell table's order (`SaveParty::SaveSpell`, `LoadSpellsFromSchool`): unschooled spells, then the character's school's (Star for Chaos), then for the party's first member, Alaron, the Elemental, Naming and Necromancy spells (`SaveAlaron`). Within a school, spells go in the table's order, which isn't their IDs' (Elemental runs 0x033D, 0x033C, 0x0338, then 0x0300 up), so the importer gives each spell its `position`. The original drops a spell of any other school, which no character in the base game can learn; the port keeps it, last. The user chose to keep it.
- **The inventory**'s stacks are added in order. A key item's flags are set again, which they already are.
- **Memories** are remembered in order, and the random streams seeded.

## Map objects' memory

`MapObjectMemory` (`HBHT.Core.MapObjects`) is the original's `VoxelChart`: what map objects remember for a while.

| Kind | Original's type | What | Lasts |
| --- | --- | --- | --- |
| `RandomLine` | 9 | A conversation's random line: the roll, 0–100 | 10 map loads |
| `SkillCheck` | 0x49 + the skill | A conversation's skill check: the party's step | 10 |
| `Opened` | 1 | A container emptied that has no opened flag | 10 |
| `Beaten` | 5 | A monster party beaten | 3 |
| `PutOut` | 0 | One of Oriana's path lights put out ([map-objects](map-objects.md#orianas-path-lights)) | 16 |

- It holds 19. When all are taken, it forgets the beaten parties, then the random lines, then everything, until there's room (`WriteTo_VoxelChart`).
- **A map load** (`MapObjectMemory.Age`, `0x80056990`, the decomp's `memset_voxelChart_entries`) takes one from each memory's lifetime, and forgets those at 0. Its one caller is `TeleportPlayer` (`0x8000BB54`), first thing: every teleport, a door's or a script's, and every chunk crossed, which `ConfirmPlayerWithinZone` queues as one. Trek built again where it was, after a battle, a cut-scene or the menu, doesn't age them. The port ages them in `TrekLoop.Enter` (a chunk crossed) and `TrekLoop.GoThrough` (a teleporter).
- **A monster party beaten** is remembered only when its encounter flag can't mark it: none, which fights under flag 0 that `FlagNetwork.Set` (`0x8001FDA8`) ignores and `FlagNetwork.Get` (`0x8001FD5C`) reads as 0, or a derived one ([combat](combat.md#in-and-out), `Combat.StateVictory`). In the base game that's 249 of the 1,079 monster parties: 240 with no flag (52 fights, among them the Cave Bears and wolves north of Erromon, and 188 talkers, whose script's battle would fight their party) and 9 with a derived one. Each stays away (`Wanderers.Load`) until three chunks have been crossed or teleports made and its chunk loads again; at three chunks away it's no longer loaded, so walking back brings it. Recalling a conversation's memory sets it back to 10 (`dialougSkillCheck`, the random line). A new game and loading a save clear it.
- **The type is the object's**, but for a conversation's. Loading a chunk looks every scene object up as type 0 (`LoadedMap.Activate`, `0x80017af8`), and keeps one it finds out, which only a path light put out writes. The chunk a memory names is the zone engine's current one moved by the chunk's place in the 3×3 (the original's `0x80056710`, which the symbol map calls `MapObjectMemory.OffsetChunk`: rows by the code's high nibble − 1, columns by its low bits − 1, `0x11` for none), so a lookup names the object's own chunk; every writer passes `0x11`. The port has no such offset, as it keys memories by record ID (below).
- **Conversations' memories are type 9 whoever talks**, as the checkpoints' show: theirs belong to monster parties with a script (`aidyn:voxel/0x1063/2`, and object 32 of Terminor G01).
- **The key.** The original keys a memory by the object's index in its map, the map set and the chunk. The port keys it by the object's record ID, which names its map, so a map placed in two chunks would share its objects' memories. In the base game only the battle arenas and an empty overworld map are placed twice, and they hold nothing that remembers.
- **The slots.** The original keeps a free list, so a new memory can land in any free slot. Only a lookup's order could tell, and no two memories share a key, so the port keeps them in the order they were made.

## Checkpoints as saves

`CheckpointSave.Convert` reads a checkpoint (`AresCheckpoint`) into a save: its time played, clock, place, plain flags (2 to 5000; the first 5,001 flags are plain), the party's sheets, gold and inventory, the chart and the weather. The random streams start as a new game's, since the original's saves don't keep its seed. Where the RAM holds them, in both builds:

| What | Where |
| --- | --- |
| The map set (`gameVars.mapDatA`) | `gGlobals` + 0x38C |
| The chunk's row and column (`mapShort1`, `mapShort2`) | `gGlobals` + 0x372, 0x374 |
| The position and facing (`gameVars.MapFloatDatEntry`, noted every two seconds for a save) | `gGlobals` + 0xFE4, + 0xFF0 |
| The clock and time played (`TerrainStruct.InGameTime`, `PlayTime`) | `*TerrainPointer` + 0x20, + 0x34 |
| The chart (`voxelChart`, 19 × 12 bytes) | 0x800E0A74 in 1.0, 0x800EF5E4 in the debug build |
| The weather as rolled: kind, what falls, intensity, fog, storm and wind | `*TerrainPointer` + 0x4, + 0x6, + 0x8, + 0xC, + 0x10, + 0x14 |
| The weather as drawn: what's falling, how hard, the fog (`fogTime`) | `gGlobals` + 0x394 + 0x2, + 0x4, + 0xC |

`gGlobals` is at 0x800D8088 in 1.0 and 0x800E68A8 in the debug build, and `TerrainPointer` at 0x800E303C and 0x800F1BBC.

Loading a converted save gives what the console holds for every member's stats, skills, protection, damage, worn items and spellbook order, the party's gold and inventory, and every flag but 3617. Every checkpoint keeps Arturo's, Becan's and Godric's sheets, with nothing worn, as leaving leaves them, and the converter carries them over (`AresCheckpoint.Kept`, from `gSaveEntity` at `0x800DA4E0` in 1.0 and `0x800E8F10` in the debug build).

## The original's save

`PakSave` (`HBHT.Rom.Rig`) is the original's save field by field, and round-trips the user's four saves byte for byte; `PakSaves` turns one into the port's save and back, and reads and writes them in a pak (`ControllerPak`, libultra's file system). 1.1's save code is 1.0's, instruction for instruction but for data addresses, so its saves are the same.

**The note.** A save is a note of 0x1C00 bytes, 28 pages: game code `NAYE`, company `78`, name `CHRONICLES` and extension the slot's letter, A to P, in the pak's character codes (`Pak.CharCodes`, `0x800E4220`). The save menu writes it in one `osPfsReadWriteFile` (`SaveMenu.WriteNote`, `0x8007EE34`), and a new one takes the first letter free (`SaveMenu.CreateNote`, `0x8007EB2C`). The load menu (`SaveMenu.ScanSlot`, `0x8007D9A4`) lists every note of that game code and company, whatever its name, and calls one whose CRC fails corrupt, offering to delete it. Nothing else is checked, so a save naming an entity or item that doesn't exist would break loading.

**The file**, built by `PakSave.Make` (`0x8001A08C`) and loaded by `PakSave.Load` (`0x8001A20C`). Outside the bit stream, values are big-endian; bytes not listed are zero.

| Offset | What |
| --- | --- |
| 0x0000 | Time played, in ticks. |
| 0x0004 | The four party slots' entity IDs, 0 for an empty one: the load menu's portraits. |
| 0x000C | Alaron's name, ending in a NUL. |
| 0x0040 | The place (`Zones.SavedPosition`, `0x800D906C`, which loading starts the zone engine from): the player's sphere's centre, the original's `facing` (X and Z), the camera's eye (5 out along `facing` and 2.3 up in every save), then halfwords: the row (A is 1), the column, the map set, and 0xFFFF twice. |
| 0x0080 | The plain flags, 0 to 5000, a bit each, lowest first in a byte (`GameStateFunnel.Pack`, `0x800CB58C`). Loading evaluates the derived ones and clears 3617. |
| 0x0300 | The load menu's picture, 64 × 32 RGBA 5551, from the pause menu's snapshot. |
| 0x1300 | The minimaps' pieces, a bit each, numbered across the eight in `CodeTable.MinimapSections`' order (map sets 4, 14, 7, 12, 11, 6, 9 and 10), 188 in all. Loading sets every piece from its bit, so one that starts seen needs its bit. |
| 0x1340 | The bit stream, below. |
| 0x1B00 | The chart as RAM holds it: the count of entries used, the 19 slot numbers (those in use first), and the 19 entries. |
| 0x1BFC | The CRC: CRC-32, polynomial 0x04C11DB7, MSB first, from 0 with no final XOR, over the file with this word zero, plus 8 (`Crc.Seal`, `0x800CBC0C`). |

**The bit stream** (`SaveBlock.Write`, `0x80019810`; `SaveBits`, `0x800026F0`–`0x800027F4`) packs each field's bits lowest first, from bit 0 of byte 0x1340. In order:

- The clock and the gold, 32 bits each, and the party's three movement timers, 12 bits each in units of 7,200 ticks.
- The party's four sheets, Alaron's first, each 0x500 bits (a null one zero), with Alaron's block after his: his name in 20 bytes, then 4-bit levels of the Elemental, Naming and Necromancy spells.
- The five kept sheets, of 720 bits: Abrecan's, Arturo's, Becan's, Godric's and Keelin's, by entity. A member in the party has their own there too, copied just before saving.
- The inventory (`Inventory.Save`, `0x800734C4`): 128 general stacks, each a quantity in 8 bits and an item; 80 quantities of 8 bits for the reserved items, by their first place in `CodeTable.ReservedItems` (which lists 0x110E twice); and the 13 key gear, an item each, by `CodeTable.KeyEquipment`. Key items aren't saved: loading gives each whose flag is set.
- The weather: the roll's intensity, fog and storm, each × 1000 in 32 bits, and what falls in 8.
- The sound and music volumes × 1000, 32 bits each, and the resolution in a bit.

**A sheet** (`PakSave.WriteSheet`, `0x8001918C`; a party member's, `PakSave.WritePartySheet`, `0x800193AC`): the entity less 0x200 in 8 bits (0 for none); total and spendable experience, 24 bits each; the trained, then the current stats, each in the order Stamina (8 bits), Endurance (6), Level (6), Willpower, Intelligence, Strength and Dexterity (5 each); 18 skills of 4 bits (the 12, Hafted, Missile, Pole, Sword, Thrown and the shield; 0xF can't be learnt); armor, shield, weapon and 12 gear, an item each; the spells' levels, 4 bits each, by their place in the spell table: the 10 unschooled, then 13 slots for the school's spells and zeroes after them. Chaos, whose 13 are the Star spells, pads 13 more, since the padding is 13 less the school's count and Chaos's is 0 (`PakSave.WriteSpells`, `0x80018DC4`). A party member's then has 15 spell effects of 28 bits and 7 potions of 20, which the original never fills, since it saves the kept copies.

**A value too wide for its field loses its high bits.** The writer copies a value's low bits one at a time and drops the rest (`SaveBits.Write`, `0x80002700`). Of the members' records only Sholeh's starts past a field, with an Intelligence of 35, which 1.0 saves in 5 bits as 3, so after a save and a load her Intelligence is 3. The user's own Sholeh has 3 in story saves A, C and D. `to-pak` writes it as 1.0 does and says so. **The port keeps it whole, the user's choice** (Oct 29, 2026), as a fix of the original's bug ([ADR 0018](../adr/0018-the-port-fixes-bugs-and-sets-enhancements.md)): its own save holds any stat at full width. Training stops at 30 for every stat but Endurance and Stamina, while the game gives some creatures more on purpose, Pochangarat among them, and they fight with it. A late recruit stronger than training can make anyone reads as meant, so the cut is the save's alone.

**An item** is 28 bits (`PakSave.WriteItem`, `0x80018970`): the DB ID in 13, the spell's charges in 5 and the stat mod in 10, `stat << 7 | (amount + 50) & 0x7F`, written for every item that has one, a battery's with the Stamina left in it.

**The port's save as the original's** (`PakSaves.FromSave`): what the port's save doesn't hold is written as a fresh save would have it: a black picture, the volumes' defaults, the timers at 0, the camera behind the player, and the memories in slots 0 on. The weather is the roll's, or clear without one. A reserved item's charges and battery are lost, as the original loses them. A spell of a school the character's sheet has no room for, and the potions working, are listed as not written. Back the other way (`ToSave`), the weather isn't read, as the save holds only part of it, and starts as a new game's.

**Not settled:** which timer is running, walking or sneaking (nothing reads them); a save made here loading in the original, which only the user can try; the camera's rule, read from the four saves.
