# Hardcoded content (roadmap 1H)

This is the audit of content that lives in the game's code rather than its data. Each item is assigned to one of the four layers in [ADR 0008](adr/0008-content-logic-layers.md):

1. **Record:** a table-shaped fact.
2. **Trigger:** *on event, if condition, do effects*.
3. **Feature:** engine behaviour configured by data.
4. **Script:** Lua, for what's truly one-off.

**Status** says where the reimplementation gets the fact:

- **ROM:** read from the code segment at a per-version offset (`CodeTable` in `RomVersion.cs`). Tables that sit in the code verbatim are read rather than copied, so they stay right for every version. The flag tables are in `EngineTables`; the rest are in `Aidyn.Rom.Engine`.
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
| Cinematic done flags (3, 569, 590, 601) | `Cinematic::EndCase` | 1: cinematic record | ROM |
| The encounter flag of the Goblin Ambush and Lugash scripts' battles | `dialoug_func` | 1: script field | ROM |
| Flag 47 after losing the Goblin Ambush, with the heal and the move to Oriana's hut | `clear_combat_func` | 3: encounter `onDefeat` | Fact (flag only) |
| Flag 566 when the Shadow merges with Alaron | `shadowMerge.cpp` | 3 or 4: shadow merge | Fact (flag only) |
| Flag 622 when Sholeh dies in combat, as well as her died flag | `combatEntity.cpp` | 1: member record | Fact |
| First spell learned (3611) and first potion bought (3612), which show help | `WidgetSpellTrain`, `WidgetBarterControl` | 2: triggers | Fact |
| New journal entry waiting (3617) | `set_journalentry_flag` | 2: trigger on `journal.added` | Fact |
| Calendar, weather, terrain and best party stat and skill flags | `gameStateFunnel.cpp` | 3: state exposed as flags | Fact |
| Camp dialogue check (4240, set while it runs) | `Camp.cpp` | 3: camping | Fact |
| Cheat flags (3673, 3675, 3693, 3694) | `CheatCodes.cpp` | 2: cheat triggers (see below) | Fact (flags only) |

## Flags the engine reads

| What | Code | Layer | Status |
| --- | --- | --- | --- |
| Camp scenes: a dialogue object whose flag A is one of eight flags starts when the party camps near it | `Camp_CheckDialougFlags` | 1: dialogue object field | ROM |
| Camp ambushes only happen while derived flag 5004 holds: two of Abrecan, Godric and Rheda have joined | `Party::CampCheck` | 1: camping setting | Fact |
| Alaron has his True Name while 590 is set | `entityInfo.cpp` | 1: character condition | Fact |
| When the Rooughah scene (Borg13 1268) ends, the Roog cinematic plays if 3442 is set | `dialogmode.cpp` | 2: trigger on script end | Fact |
| World map pieces: each shows while its region flag is set | `WorldMapPieces` | 1: map piece record | ROM |
| The lighthouse chunks swap to their lit scenes while 5017 holds | `ZoneEngine.cpp` | 1: chunk alt-scene condition | Fact |
| Party skill checks in dialogue roll 0–21 while 3717 is set | `DialogEvalCallback` | 3: debug option | Fact |
| Minimap images chosen by flags: the Ehud underground (4222–4232), the Roog entrance (4233–4237) and the Barrows (4286–4294). Which Barrows piece each flag changes is code. | `minimap_dats_2`, `MiniMap::BarrowsFlags` | 1: minimap records | ROM (Barrows logic to do) |
| The Theatre menu lists a cinematic once its title's flag is set | `TheatreMenu.cpp` | 1: DB | ROM (DB block) |
| Key item flags restore the key items when a save loads | `LoadKeyItems` | 1: item record | ROM |
| Flag 5626, read after some map object actions; the result is never used | `objectJumpTables.cpp` | None | Fact |

## Scripts and cinematics the engine starts

| What | Code | Layer | Status |
| --- | --- | --- | --- |
| The four cinematics: each shot's Borg6 animation, Borg12 audio, caption list and clip planes, the fades, and the done flag | `Cinematics.cpp`, `CinematicFade.cpp` | 1: cinematic record | ROM |
| What starts each cinematic: a new game, the Shadow merging with Alaron, and the ends of the Rooughah and last scenes | `Cinematic::Load`'s callers | 2: triggers | Fact (text only) |
| What follows each cinematic: the opening starts script 866 from its object in Overworld I08, the Roog cinematic gives Alaron his True Name, and the ending goes to the credits | `Cinematic::EndCase` | 2: triggers on `cinematic.ended` | To do |
| Scripts whose end leads somewhere: the three death scenes end the game, 1268 plays the Roog cinematic, and 1278 plays the ending | `borg_short_list`, `dialogmode.cpp` | 2: triggers on script end | ROM |
| Scripts after which eight hours pass: the eight camp scenes and the Campsite's own script | `isCampfireScene` | 1: script field | ROM |
| Game over: script 1298 starts from its object in the Dreamscape Borg9 (3423) | `Gameover_func` | 1: game setting | ROM (script only) |
| Camping with no camp scene nearby starts the Campsite Borg9's (6236) first dialogue object | `campfire_func` | 1: game setting | To do |
| The help menu is script 1355 | `gametrek.cpp` | 1: game setting | Fact |

## Party and camping rules

- **Brenna never leaves because no script removes her.** Her row in the party table has no left flag (0), and nothing else in the code names her. It's data, not a rule.
- **Rheda has no rule in code.** Apart from the party tables, no code names her. Scripts add and remove her like the other recruits.
- **Camp ambushes** (`Party::CampCheck`): while 5004 holds, camping is ambushed if a d100 roll is at most (15 − the party's best Ranger, capped at 10) × 0.666 × the terrain's factor. Otherwise the party heals. The 15, 10 and 0.666 are camping settings (layer 1); the factor is a terrain field.
- **Camping is refused** on terrain whose factor is 0 ("The party cannot camp here").

## Terrain and weather

Terrain types are the 0–29 index that map chunks carry and the camp encounters are keyed by. These tables are terrain and season records (layer 1), identical in all three ROMs.

| What | Code | NA 1.0 | Status |
| --- | --- | --- | --- |
| Camp ambush factor per terrain type; 0 means no camping | `camp_float_array` | `0xE3B38` | To do |
| Precipitation chance per season (8) and per terrain type (28), and precipitation scale per terrain type | `sSeasonPrecipChance`, `sTerrainPrecipChance`, `sTerrainPrecipScale` | `0xE3C40`, `0xE3C48`, `0xE3C64` | To do |
| Snow falls instead of rain on terrain type 5 only | `World::SetWeather` | Literal | To do |
| The weather rolls again at each change in the time of day, with a 25% chance of clearing and then trying for precipitation | `World::SetWeather` | Literals | To do |

## Cheats

Cheats are typed as Alaron's name, in the title menu's name entry or the options menu (`Cheats::Check`). The name is lowercased first. A cheat only takes effect when the pause menu exists, which also stops the XP and gold cheats crashing the title screen. The codes are the `Cheats` string table, so they're in the ROM; which string runs which effect is code.

| Code | Effect | Layer |
| --- | --- | --- |
| `!balloon`, `!bigw` | Toggle drawing heads, or weapons, at double size | 3: render options |
| `!flea` | Toggles a quarter-size Alaron who jumps with A | 3: player option |
| `!slashing` | Sets 3673, which shows the Hockey Stick and its container in Gwernia Castle E03 | 2: trigger, set flag |
| `!darkside` | Sets 3675, which shows the Lightreaver and its container in MiscInteriors E03 | 2: trigger, set flag |
| `!crawdaddy`, `!fatboy`, `!tweety` | Change the player's model to the chaos trooper, ogre or gryphon, or back to Alaron if it already is | 3: player model |
| `!cheater` | Once (flag 3693): 500,000 XP to each party member. The decomp's comment says 750,000; the code and ROM say 500,000. | 2: trigger, add XP |
| `!bingo` | Once (flag 3694): 100,000 gold | 2: trigger, add gold |
| `Keepbusy`, `Imadoofus` | Each tells you to enter the other. The decomp's function names for these two are swapped. | 2: trigger, show a message |
| `!version` | Shows the version, compile time and code size, and turns on the coordinate display | 3: debug option |
| `!gene`, `!gotmilk` | Strings with no effect | None |

The debug ROM also has a game state cheats menu. Only its "appear" option is read, and only in the debug build.

## Assets engine code names

Code uses 1,990 of the 2,076 names in the decomp's `Borgindecies.h`. The map chunk lists account for 1,127 of them, and those are read from the ROM already (`MapChunks`). The rest fall into four groups.

**Content tables** pair records with their assets. Mods adding a monster, weapon or item need to set these, so each becomes a record field (layer 1). They're data in the code segment, identical in all three ROMs. The 1.0 offsets below are where each starts; they aren't read yet, so they aren't in `RomLayout`.

| What | Code | NA 1.0 | Status |
| --- | --- | --- | --- |
| Each entity's model (Borg7), portrait (Borg8), height, collision radius, scale and one unknown float, keyed by entity list position + 1. Entities without an entry get the bat model and a generic portrait. | `gEntityExtras` | `0xE1930` | To do |
| Each dialogue entity's model, portrait, height and scale | `dailougEnt_info_array` | `0xE3178` | To do |
| The position lists that code tables use for dialogue entities, spells, armor, weapons and items, like the entity list | `DialougEntList`, `SpellList`, `ArmorList`, `weaponList`, `itemID_array` | `0xE1900`, `0xE3740`, `0xE1850`, `0xE3790`, `0xE3590` | To do |
| The 66 weapon models (Borg5), by weapon position | `weapons_with_borg5`, `weapon_borg5_ID` | `0xE3520`, `0xD3B14` | To do |
| Models for six items: Txomin's letter, the banner, the Horn of Kynon, Kendal's helm, Stormbreaker and the Amulet of Haste | `D_800f14d4`, `ItemBorg5Search` | `0xE3564`, `0xD3B9C` | To do |
| Shield models | `ArmorDB::GetBorg5` | Switch | To do |
| Item icons (286, by item ID; others get a default, and every potion shares one), then skill, aspect, school and spell-symbol icons, each list followed by its count | `menuimages.cpp` | `0xDF700` | To do |
| Each spell's icon symbol, by spell position | `SpellDB::GetIcon` | `0xD3C58` | To do |
| Spell and potion effects: Borg7 and Borg5 per spell or potion, with a memory size | `spellvisuals.cpp` | Switches | To do |
| Models whose actors fly: bat, wyvern, harpy, dryads, Pandara, gryphon, manticore and scorpion | `Actor::IsFlyingModel` | `0xCE650` | To do |
| The 39 Borg12 sounds that Borg13 PlaySound operands index | `dialoug_SFX` | `0xE0960` | To do |
| Minimap pieces for eight interiors: chunk, group, image and position | `minimap.cpp` | | ROM |

**Settings and features** are engine behaviour that names specific assets. Each becomes a game setting (layer 1) or a field of the feature that uses it (layer 3).

- The six title-screen fly-throughs: a Borg6 and eight unknown u16s each (`gFlycamSequences`, `0xE15DC`).
- Battle music: one of four Borg12s at random without the Expansion Pak, or one of five with it. Gwernia castle and Chaos Isle choose their own music; Chaos Isle picks between two by a map field (`BorgMaps.cpp`).
- The player's model is Alaron's; three cheats swap it (see cheats, not audited yet).
- The camp, game over and new-game Borg9s (see above).
- Sky gradients, sun, moon and stars, clouds, lens flare, rain ambience and four thunder sounds (`sky.cpp`, `skyObjects.cpp`, `Clouds.cpp`, `Lensflare.cpp`, `weather.cpp`).
- Combat visuals: the arrow and thrown-potion models, target and spell markers, the stat indicator digits and the player shadow (`combatEntity.cpp`, `CombatMarkers.cpp`, `SpellMarker.cpp`, `StatIndicator.cpp`, `ZoneEngine.cpp`).
- The particle textures (`ZoneEngine.cpp`).
- Oriana's path lights: scene objects whose second model is `BORG5_OrianaLight` get special handling in the scene renderer. Not read closely yet.
- Coin, chime and level-up sounds in dialogue, shops and training.

**UI chrome** is the rest: fonts, the compass, menu backgrounds and titles, the calendar and sundial, logos and Controller Pak screens. These need no records. Mods override them as assets under their Borg IDs ([ADR 0004](adr/0004-record-ids.md), [ADR 0005](adr/0005-plugin-format.md)).

**Debug only:** the boulder that replaces scene models, and the debug menu's models.

## Findings

- **The code's tables are identical in all three ROMs.** Only their offsets differ.
- **The decomp has one wrong value.** Sholeh's left flag is 694 in the ROM, not 700.
- **Code tables refer to entities by list position, not ID.** A 221-byte `entityList` in the code maps each position to an entity. The party tables use these positions, and so does the boss switch, which is a jump table over positions 168–186.
- **Map 13's region flag is on Cradawgh's Body.** Gaining Map 13 reveals nothing. The decomp notes this as a bug.
- **Two world map entries can't apply.** They name overworld chunks G18 and R29, which don't exist.
- **Most map items are never given.** Only Map 2 has a source in the data, so the world map fills in as the party explores. The Skull Key has no source either; it's possibly cut content. The Blood Key is the only key item given as a monster party's direct loot.
- **Talking monster parties fight under flag C.** When a talking party's dialogue starts a battle, the battle's encounter flag is the party's flag C, and winning sets it. Most such parties have C equal to B. One names derived flag 5264, so the write does nothing.
- **Script battles don't set their flag on a win.** Only monster-party battles set the encounter flag on a win. The Goblin Ambush's flag, 978, is set when the party *loses*, and every special rule of that fight keys on it.
- **The ending cinematic has six shots.** The decomp lists five Borg6 animations and five Borg12s for it, but the ROM has six of each, which matches its six clip planes and flag entries.
- **Each cinematic shot names its own caption list.** The flag list the decomp calls `SeqenceFlags` holds a caption list per shot (or none), then the done flag. Each of the 15 caption lists shows over exactly one shot.
- **Only the first death scene plays.** All three have dialogue objects in the Dreamscape, but the game over code only looks for 1298.
- **Containers with their own model are gated like herb sources.** The code gates container kinds 4–7 by flags A and C: herbs, spices, gems and loot with its own model. The atlas only gated 4–6 before. 46 of the 49 kind-7 containers set a flag C, including the cheats' Hockey Stick and Lightreaver, Kendall's Hat and an Amaranth.
- **Bosses count only in the first four slots.** A monster party fills slots with each entry's minimum count, in order, then random extras, so a boss listed after four guaranteed enemies wouldn't set its flag. In the data, only three monster parties include a boss (Kitarak, and Lugash twice), each listed first. Scripts add the other bosses.

### Checked against the checkpoints

49 flags are set in the checkpoints that 1E found no data setter for. With the engine's flags in the atlas, all but eight have a setter. The eight are **955–958 and 960–963**, which turn on between Shamsuk's Tower and Rooughah:

- No map object, script, DB table or network input names them.
- No retail code loads them as a constant. The debug ROM has one unrelated match.
- The GameStateMod table gives them consecutive designer IDs (7763–7771), like their neighbours. Flags 944–987 are mostly the "talked to" flags of NPCs around Gwernia, the H13 and I13 village and Jundar, so these were probably allocated as talk flags too.

The hacked ROM the playthrough used only changed enemy stats (with AidynEditor), so it isn't the cause. They may come from code that computes a flag number. Nothing reads them.

## Not audited yet

- Special-case trainers (AidynEditor notes Becan).
- Where the map items come from. The atlas doesn't count shops as a source yet, and one shop sells Map 7.
- The bespoke behaviours ADR 0008 lists: the Goblin Ambush in full, Brenna's Ranger check in Gwernia, the Lugash fight, shadow merge, Sholeh's death and Alaron's True Name.
