# Hardcoded content (roadmap 1H)

This is the audit of content that lives in the game's code rather than its data. Each item is assigned to one of the four layers in [ADR 0008](adr/0008-content-logic-layers.md):

1. **Record:** a table-shaped fact.
2. **Trigger:** *on event, if condition, do effects*.
3. **Feature:** engine behaviour configured by data.
4. **Script:** Lua, for what's truly one-off.

**Status** says where the reimplementation gets the fact:

- **ROM:** read from the code segment at a per-version offset (`CodeTable` in `RomVersion.cs`). Tables that sit in the code verbatim are read rather than copied, so they stay right for every version.
- **Fact:** carried in `EngineFlags`, because the code expresses it as a switch or a literal. Each is checked against 1.0's code.
- **To do:** found, but not carried yet.

The quest atlas shows every item below that involves a flag. Search for a name such as "Brenna" or "Kitarak" to see its flags.

## Flags the engine writes

| What | Code | Layer | Status |
| --- | --- | --- | --- |
| Party members' joined, left and died flags | `Party::CheckFlags` | 1: member record | ROM |
| Key items: each gets an inventory slot, a "has" flag, and sometimes a world map region flag | `PartyInventory.cpp` | 1: item record | ROM |
| World map regions: entering an overworld chunk sets its region's flag | `set_map_event_flag` | 1: chunk field | ROM |
| Boss flags: winning a battle with a boss in its first four enemy slots | `set_boss_flag` | 1: entity field | Fact |
| Cinematic done flags (3, 569, 590, 601) | `Cinematic::EndCase` | 1: cinematic record | Fact |
| The encounter flag of the Goblin Ambush and Lugash scripts' battles | `dialoug_func` | 1: script field | ROM |
| Flag 47 after losing the Goblin Ambush, with the heal and the move to Oriana's hut | `clear_combat_func` | 3: encounter `onDefeat` | Fact (flag only) |
| Flag 566 when the Shadow merges with Alaron | `shadowMerge.cpp` | 3 or 4: shadow merge | Fact (flag only) |
| Flag 622 when Sholeh dies in combat, as well as her died flag | `combatEntity.cpp` | 1: member record | Fact |
| First spell learned (3611) and first potion bought (3612), which show help | `WidgetSpellTrain`, `WidgetBarterControl` | 2: triggers | Fact |
| New journal entry waiting (3617) | `set_journalentry_flag` | 2: trigger on `journal.added` | Fact |
| Calendar, weather, terrain and best party stat and skill flags | `gameStateFunnel.cpp` | 3: state exposed as flags | Fact |
| Camp dialogue check (4240, set while it runs) | `Camp.cpp` | 3: camping | Fact |
| Cheat flags (3673, 3675, 3693, 3694) | `CheatCodes.cpp` | 1: cheat records | Fact (flags only) |

## Flags the engine reads

| What | Code | Layer | Status |
| --- | --- | --- | --- |
| Camp scenes: a dialogue object whose flag A is one of eight flags starts when the party camps near it | `Camp_CheckDialougFlags` | 1: dialogue object field | ROM |
| Camp ambushes only happen while derived flag 5004 holds | `Party::CampCheck` | 1: camping setting | Fact |
| Alaron has his True Name while 590 is set | `entityInfo.cpp` | 1: character condition | Fact |
| When the Rooughah scene (Borg13 1268) ends, the Roog cinematic plays if 3442 is set | `dialogmode.cpp` | 2: trigger on script end | Fact |
| The lighthouse chunks swap to their lit scenes while 5017 holds | `ZoneEngine.cpp` | 1: chunk alt-scene condition | Fact |
| Party skill checks in dialogue roll 0–21 while 3717 is set | `DialogEvalCallback` | 3: debug option | Fact |
| Interior minimaps swap images on 4286–4294 | `MiniMap::BarrowsFlags` | 1: minimap records | Fact (flags only) |
| The Theatre menu lists a cinematic once its title's flag is set | `TheatreMenu.cpp` | 1: DB | ROM (DB block) |
| Key item flags restore the key items when a save loads | `LoadKeyItems` | 1: item record | ROM |
| Flag 5626, read after some map object actions; the result is never used | `objectJumpTables.cpp` | None | Fact |

## Findings

- **The code's tables are identical in all three ROMs.** Only their offsets differ.
- **The decomp has one wrong value.** Sholeh's left flag is 694 in the ROM, not 700.
- **Code tables refer to entities by list position, not ID.** A 221-byte `entityList` in the code maps each position to an entity. The party tables use these positions, and so does the boss switch, which is a jump table over positions 168–186.
- **Map 13's region flag is on Cradawgh's Body.** Gaining Map 13 reveals nothing. The decomp notes this as a bug.
- **Two world map entries can't apply.** They name overworld chunks G18 and R29, which don't exist.
- **Most map items are never given.** Only Map 2 has a source in the data, so the world map fills in as the party explores. The Skull Key has no source either; it's documented cut content. The Blood Key is the only key item given as a monster party's direct loot.
- **Talking monster parties fight under flag C.** When a talking party's dialogue starts a battle, the battle's encounter flag is the party's flag C, and winning sets it. Most such parties have C equal to B. One names derived flag 5264, so the write does nothing.
- **Script battles don't set their flag on a win.** Only monster-party battles set the encounter flag on a win. The Goblin Ambush's flag, 978, is set when the party *loses*, and every special rule of that fight keys on it.
- **Bosses count only in the first four slots.** A monster party fills slots with each entry's minimum count, in order, then random extras, so a boss listed after four guaranteed enemies wouldn't set its flag. In the data, only three monster parties include a boss (Kitarak, and Lugash twice), each listed first. Scripts add the other bosses.

### Checked against the checkpoints

49 flags are set in the checkpoints that 1E found no data setter for. With the engine's flags in the atlas, all but eight have a setter. The eight are **955–958 and 960–963**, which turn on between Shamsuk's Tower and Rooughah:

- No map object, script, DB table or network input names them.
- No retail code loads them as a constant. The debug ROM has one unrelated match.
- The GameStateMod table gives them consecutive designer IDs (7763–7771), like their neighbours. Flags 944–987 are mostly the "talked to" flags of NPCs around Gwernia, the H13 and I13 village and Jundar, so these were probably allocated as talk flags too.

The hacked ROM the playthrough used only changed enemy stats (with AidynEditor), so it isn't the cause. They may come from code that computes a flag number. Nothing reads them.

## Not audited yet

- The asset indices in `Borgindecies.h` that code names directly.
- `isCampfireScene` (eight scripts that pass eight hours) and the death-scene table (`borg_short_list`: three game-over scripts, the ending, and the Rooughah scene).
- The rest of the cinematic tables: Borg6 scenes, Borg12 music and clip planes.
- The world map pieces (flag, Borg8 image, position).
- Cheat codes and what each does.
- Party rules in code, if any: Brenna never leaving, Rheda's condition.
- Special-case trainers (AidynEditor notes Becan).
- Where the map items come from. The atlas doesn't count shops as a source yet, and one shop sells Map 7. The user remembers getting Map 5, which no data source gives.
- The bespoke behaviours ADR 0008 lists: the Goblin Ambush in full, Brenna's Ranger check in Gwernia, the Lugash fight, shadow merge, Sholeh's death and Alaron's True Name.
