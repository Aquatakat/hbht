# Saves and the game state (roadmap 2.8)

`Aidyn.Core.GameState` is the game as it stands, `Aidyn.Plugins.Saves` the port's save format, and `Aidyn.Rom.Rig.CheckpointSave` turns the user's checkpoints into saves. Ported from `QuestData.cpp`, `saveParty.cpp` and `voxelChart.cpp`.

## The game state

`GameState` holds the flags with the engine's state flags, the clock, the time played, the party with its inventory, what map objects remember, the random streams (ADR 0006) and where the player is. It's the `IScriptWorld` scripts run against, so the editor's script player and the runtime share it. `CharacterSaves` (`Aidyn.Core.Characters`) is the mapping between the party and its sheets and `SaveParty` and `SaveCharacter`, saving and loading side by side (`SaveParty::SaveCharSheet`, `LoadCharSheet`, the spellbook's order); `GameState` calls it.

- **A new game** (`init_DBs`): the flags' initial values, noon on day 2 of week 6, and Alaron alone, wearing what his record gives him, with 1 gold. The runtime starts him at the Overworld's I08, (16, 62).
- **Time passing** (`PassTime`) runs the clock and writes the calendar's flags, as trek does every frame. The time played is the runtime's to count.
- **Saving** (`Save`) takes a snapshot in the format below. **Loading** (`Load`) rebuilds the game from one with the rules, and lists in `Problems` what it couldn't use, such as a record no plugin has any more.

## Making and checking saves

The CLI makes saves without the game, with the installed plugins:

- `aidyn save new [<save>]` writes a new game.
- `aidyn save from <checkpoint> [<save>]` turns a checkpoint into a save (below).
- `aidyn save show <save>` loads a save with the core and prints what it makes of it (time, place, flags, each member's sheet, gold, inventory, memories), then anything it couldn't use, with exit code 1.

They write to the data folder's `saves` unless given a path, where the System tab's save and load screens find them too, newest first. A save can be edited by hand, and `save show` says whether the game can use it.

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

- **The place** is the map set, the chunk by name, the centre of the player's sphere in the chunk's coordinates, and the way he faces in X and Z. The original keeps the opposite of that, `facing`. Without a place, the game starts where a new game does.
- **Flags** are the plain flags that are set, but for flag 1, which always is. Derived flags are evaluated on loading.
- **The journal's order:** its entries in the order they were added (`journal`). The original doesn't keep it, as it lists them in its table's order; the user chose to list them as they came. On loading, an entry that's on and not listed, as in a save made from a checkpoint, comes before those listed, in the table's order.
- **A member** is what the original saves of a character sheet (`SaveParty::SaveCharSheet`): experience, the trained and current stats, base skill levels, what's worn with each item's battery and charges, the spellbook, and the potions working on them, each with its power and what's left of its timer (`SavePotionEffect`). A skill that can't be learnt is left out. Loading applies the potions again after what's worn (`LoadCharSheetEffects`); the checkpoints' converter doesn't read them yet. **Deviation:** the original saves none, since it saves its copies of the party's sheets (`SaveEntity::BenchParty`) and the copy (`SaveEntity::Copy`) leaves out the effects, so a potion's effect is gone after loading. The user chose to keep them.
- **Kept sheets** are those of members who left whose sheets the original keeps, saved as members are, which they rejoin with ([characters](characters.md#joining-and-leaving)). The original saves all nine of its slots (`QuestData::Save`): the party's four, then Abrecan's, Arturo's, Becan's, Godric's and Keelin's, each copied from the party for one in it. The port saves only those who left, and loading refuses a kept sheet for someone in the party.
- **Stacks and worn items** keep their battery and charges, where the item has them. Their price is the item's; only a shop's stock has its own.
- **Memories** are below.
- **The random streams** are the five streams' seeds. Without them, the streams start as a new game's.
- **Alaron's name**, when the player renamed him in the options menu ([ui](ui.md#renaming-alaron)): `name` on his sheet, left out while it's the record's. The original saves his name with his spells (`SaveParty::SaveAlaron`, 20 bytes); the checkpoints' converter doesn't read it yet.
- **The screenshot** isn't in the save: the System tab writes it beside the save, a PNG of the same name, 208 × 156 ([ui](ui.md#saving-and-loading)). The original keeps a 64 × 32 one in the save. A save without one shows none.
- **The minimaps' seen pieces** (`minimaps`), each minimap's by their place in its record, where they're not as it starts ([ui](ui.md#the-minimap)). The original saves one bit a piece for all eight. Without them, the pieces start as a new game's.
- **The weather** (`weather`): what the roll left (its kind, what falls, its intensity, fog and storm, and the wind) and what's drawn as it sets in (what's falling, how hard, and the fog's thickness), as [weather](weather.md) has them. Without it, the weather starts as a new game's, and the rain flags stay as saved until it next changes.
- **Spells' effects** aren't saved, as the original's aren't: a battle's end takes every one off, and the only ones in trek are worn enchantments, which come back on as loading puts the gear on. What a spell leaves on the sheet for good (Spirit Shield's protection, Wind's Stamina) is gone after a load, as in the original, which saves the trained and current stats only.
- **Not kept:** The original's volumes and resolution, which it saves too, are the player's settings in the port ([ADR 0013](../adr/0013-settings-in-the-options-menu.md)). The original also keeps three timers of how long the party has run, walked and sneaked (`Party::TickMoveCounters`), which nothing reads.

## Loading, as the original does

`GameState.Load` follows `QuestData::LoadFile`:

- **Flags.** Every plain flag is set as saved, but the new journal entry flag (3617), which `LoadFile` clears, and then the derived flags are evaluated. The original clears 3617 before it next evaluates them, lazily, from what its network held: a new game's values, when loading from the title screen. So every journal entry the save has turns on again, which sets 3617, and the last of them in the network's order becomes the newest (`newestJournal`), which isn't saved. The original nonetheless never says "A new journal entry has been added." after loading and highlights no entry in its journal, so something forgets both; what isn't known. The port clears 3617 once the save has loaded, and `FlagNetwork.Restore` leaves no entry the newest.
- **The clock** is set, and its flags written.
- **Each member** starts from their record (`Entity::Init`) with the save's experience, and trained and current stats, from which their base and modified values start (`CharStats::Load`). Base skills are the save's, with the modified levels at them; a skill the record can't learn stays so whatever the save says, with its modified level at 0, as the checkpoints hold it (`CharSkills::LoadOne`). Then the armor, shield, weapon and gear go on by the rules (`Entity::EquipArmor` and the rest, which check as `Party`'s equipping does), which applies what they do. One the rules refuse is lost, and listed. Gear goes in the first free slot, so loading closes gaps. The aspect, school, protection and damage come from the record, and the True Name from the record and the flags.
- **The spellbook** is reordered, as the original saves spells school by school in the spell table's order (`SaveParty::SaveSpell`, `LoadSpellsFromSchool`): unschooled spells, then the character's school's (Star for Chaos), then for the party's first member, Alaron, the Elemental, Naming and Necromancy spells (`SaveAlaron`). Within a school, spells go in the table's order, which isn't their IDs' (Elemental runs 0x033D, 0x033C, 0x0338, then 0x0300 up), so the importer gives each spell its `position`. The original drops a spell of any other school, which no character in the base game can learn; the port keeps it, last. The user chose to keep it.
- **The inventory**'s stacks are added in order. A key item's flags are set again, which they already are.
- **Memories** are remembered in order, and the random streams seeded.

## Map objects' memory

`MapObjectMemory` (`Aidyn.Core.MapObjects`) is the original's `VoxelChart`: what map objects remember for a while.

| Kind | Original's type | What | Lasts |
| --- | --- | --- | --- |
| `RandomLine` | 9 | A conversation's random line: the roll, 0–100 | 10 map loads |
| `SkillCheck` | 0x49 + the skill | A conversation's skill check: the party's step | 10 |
| `Opened` | 1 | A container emptied that has no opened flag | 10 |
| `Beaten` | 5 | A monster party beaten | 3 |
| `PutOut` | 0 | One of Oriana's path lights put out ([map-objects](map-objects.md#orianas-path-lights)) | 16 |

- It holds 19. When all are taken, it forgets the beaten parties, then the random lines, then everything, until there's room (`WriteTo_VoxelChart`).
- **A map load**, a teleport or a conversation's end (`memset_voxelChart_entries`), takes one from each memory's lifetime, and forgets those at 0. Recalling a conversation's memory sets it back to 10 (`dialougSkillCheck`, the random line). A new game and loading a save clear it.
- **The type is the object's**, but for a conversation's. Loading a chunk looks every scene object up as type 0 (`LoadedMap.Activate`, `0x80017af8`), and keeps one it finds out, which only a path light put out writes. The chunk a memory names is the zone engine's current one moved by the chunk's place in the 3×3 (`MapObjectMemory.OffsetChunk`, `0x80056710`: rows by the code's high nibble − 1, columns by its low bits − 1, `0x11` for none), so a lookup names the object's own chunk; every writer passes `0x11`.
- **Conversations' memories are type 9 whoever talks.** The two in the checkpoints belong to monster parties with a script: object 2 of Overworld L08 (`aidyn:voxel/0x1063/2`) remembers a roll of 99 and a Diplomat step of 2, and object 32 of Terminor G01 a roll of 100.
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
