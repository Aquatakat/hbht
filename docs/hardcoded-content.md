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
| Sholeh's died flag (622) whenever she dies in battle, including as an ally | `CombatEntity::Death` | 1: member record | Fact |
| First spell learned (3611) and first potion bought (3612), which show help | `WidgetSpellTrain`, `WidgetBarterControl` | 2: triggers | Fact |
| New journal entry waiting (3617), and which derived flags add journal entries (5556 on) | `set_journalentry_flag` | 1: flag records and the state flags record | Fact, carried as records |
| Calendar, weather and terrain flags. The best party stat and skill flags are never written: their setters' lookup always fails. | `gameStateFunnel.cpp` | 3: state exposed as flags | Fact, carried as the state flags record |
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
| The lighthouse chunks swap to their lit scenes while 5017 holds | `ZoneEngine.cpp` | 1: chunk scene condition | Fact |
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

## Bespoke behaviours

ADR 0008 names six behaviours as bespoke. Read closely, only two need layer 3 features: the Goblin Ambush and the Shadow. The other four are records and triggers. None needs a script.

### The Goblin Ambush (encounter flag 978)

`gameCombat.cpp`, `combatEntity.cpp`, `turn.cpp` and `combatEngine.cpp` key on the encounter flag. As encounter fields:

| Field | Value | Code |
| --- | --- | --- |
| `canFlee` | false | `encounter_id_check` |
| `onStart` | Heal Alaron to full | `init_combat_struct` |
| `keelOverAfterHits` | 2. Each hit on Alaron that does damage deals and shows 1 instead. The second drops his Strength to 0, so he keels over and can't move. | `GoblinAmbushAttack`, `func_keel_over_after_ambush` |
| `nonLethal` | When the party is down, there's no game over. The combat text shows the "PlotPoison" string, locked to Alaron. Then the encounter flag is set, the monster party is marked beaten if the battle came from one, and the clock moves to 6:00 the next day. | `CombatTurn::PartyDead`, `CombatStateFunc_13` |
| `suppressVictoryText` | true. Winning still sets boss flags and gives loot. | `CombatTurn::EnemiesDead` |
| `onEnd` | Win or lose: restore Alaron's Strength, heal him to full, set flag 47, and move the party to Oriana's hut (MiscInteriors E03) at (5.9, 0.5, 3.0), facing +x | `clear_combat_func` |

These are ADR 0008's example fields, except that the move to the hut happens on any ending, not only after a win.

### The Shadow (entity 0x0226)

A feature on the entity, with the Horn of Kynon as its item. The fields:

| Field | Value | Code |
| --- | --- | --- |
| `canFlee` | true while the Shadow is in the battle, before the encounter table has the last word | `look_for_boss_shadow` |
| `aura` | Radius 10. At the start of each turn, each living combatant within it that isn't chaos-aligned takes 10 − distance damage, squared without the item. A hit that would kill ends the battle instead, as if the Shadow reached Alaron. | `shadow_combat_func` |
| `slow` | Anyone within 10 has a move range of 1, with the "XShadow" message | `CombatEntity` turn setup, `movement_checking_shadow` |
| `noPotionHealing` | Potions heal nothing while the Shadow is in the battle | `Entity::HealByPotion` |
| `vanishAfterTurns` | 3, without the item. The "ShadowVanish" text shows and the battle ends. | `shadow_combat_func`, `merge_no_horn` |
| `merge` | When the Shadow's and Alaron's collision radii + 0.5 overlap: with the item, the Shadow cinematic plays and Alaron merges; without it, the Shadow vanishes | `alaron_shadow_merge_attempt` |
| `mergeBoost` | Alaron's base Strength +6, Intelligence, Willpower and Endurance +3, Stamina +10, protection +5. For each skill and weapon skill the Shadow has, Alaron gains the difference to the Shadow's, or 2 if that isn't positive. The shield skill is the same, but the code's test is inverted, so it applies when the Shadow has no shield skill. Each Shadow spell is learned, or raised by the difference or 2, up to 10. Then flag 566 is set. | `ShadowMergeBoost` |

### The other four

- **Brenna's Ranger check in Gwernia** is the camp ambush roll. The code only names Brenna in the flag `FLAG_Gwernia2MembersNoBrenna` (5004), which `Party::CampCheck` reads before rolling against the party's best Ranger. See "Party and camping rules".
- **The Lugash fight** has no code of its own. It's script battle 1233, whose encounter flag (5250) is derived, so a win never sets it. The encounter table lets the party flee, Lugash uses the AI's boss attack style, and beating him sets boss flag 533. All of these are records.
- **Sholeh** is two entity fields (layer 1). If an encounter lists her in its first four enemy slots, she fights on the party's side instead (`find_sholeh`). Her died flag, 622, is set whenever she dies in battle (`CombatEntity::Death`). The decomp's `find_sholeh` looks garbled, returning a slot the caller treats as one-based.
- **Alaron's True Name** is a character flag. The entity DB has it (`HasTrueName`), and spells marked True Name need it. Alaron gains it when the Roog cinematic ends, and again whenever his character sheet is built while 590, that cinematic's done flag, is set (`entityInfo.cpp`). As a record: Alaron's True Name comes from flag 590.

### Fleeing and bosses

| What | Code | NA 1.0 | Status |
| --- | --- | --- | --- |
| Fleeing: monster party battles allow it, camp ambushes don't, and script battles allow it unless the script is a cutscene. Then a boss among the enemies forbids it (Kitarak, the Marquis, Pochanargat, Shamsuk, Sheridan, Ksathra, Shatrevar, Mehrdad, Golnar, Nasim, Behrooz and Assim), the Shadow allows it, and last, three encounter flags override: 978 forbids it, 5250 (Lugash) and 3749 (the Marquis) allow it. | `look_for_boss_shadow`, `encounter_id_check` | `0xD3288`, `0xD32A0` | To do |
| Enemies with spells that use the AI's boss attack style: 13 entities, including Lugash and the Wight | `CombatAI::IsBoss` | Switch | To do |

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

## Trainers

The Train effect teaches from the record of the entity being talked to: its skills, shield skill and spells (`init_skill_trainer`). No trainer is special-cased in code. Most trainers are entities with code names, such as STER0904 in Erromon and STTA05 in Talewok; Becan, Oriana, a female dryad and a Jundar man train from their own records.

Becan is also a recruit, so one record sets both what he teaches and what his party character can learn. That's why AidynEditor treats him as a special case: it writes a blank skill as 255 for him, which a party character reads as "can't learn", and as 0 for other trainers. It's data (layer 1), read from the entity DB.

## Cheats

Cheats are typed as Alaron's name, in the title menu's name entry or the options menu (`Cheats::Check`). The name is lowercased first. A cheat only takes effect when the pause menu exists, which also stops the XP and gold cheats crashing the title screen. The codes are the `Cheats` string table, so they're in the ROM; which string runs which effect is code.

| Code | Effect | Layer |
| --- | --- | --- |
| `!balloon`, `!bigw` | Toggle drawing heads, or weapons, at double size | 3: render options |
| `!flea` | Toggles a quarter-size Alaron who jumps with A | 3: player option |
| `!slashing` | Sets 3673, which shows the Hockey Stick and its container in Gwernia Castle E03 | 2: trigger, set flag |
| `!darkside` | Sets 3675, which shows the Lightreaver and its container in Oriana's hut (MiscInteriors E03) | 2: trigger, set flag |
| `!crawdaddy`, `!fatboy`, `!tweety` | Change the player's model to the chaos trooper, ogre or gryphon, or back to Alaron if it already is | 3: player model |
| `!cheater` | Once (flag 3693): 500,000 XP to each party member. The decomp's comment says 750,000; the code and ROM say 500,000. | 2: trigger, add XP |
| `!bingo` | Once (flag 3694): 100,000 gold | 2: trigger, add gold |
| `Keepbusy`, `Imadoofus` | Each tells you to enter the other. The decomp's function names for these two are swapped. | 2: trigger, show a message |
| `!version` | Shows the version, compile time and code size, and turns on the coordinate display | 3: debug option |
| `!gene`, `!gotmilk` | Strings with no effect | None |

The debug ROM also has a game state cheats menu. Only its "appear" option is read, and only in the debug build.

## Assets engine code names

Code uses 1,990 of the 2,076 names in the decomp's `Borgindecies.h`. The map chunk lists account for 1,127 of them, and those are read from the ROM already (`MapChunks`). The rest fall into four groups.

**Content tables** pair records with their assets. Mods adding a monster, weapon or item need to set these, so each becomes a record field (layer 1). They're data in the code segment, identical in all three ROMs. The 1.0 offsets below are where each starts. The ones marked Read are in `RomLayout`; the rest aren't read yet.

| What | Code | NA 1.0 | Status |
| --- | --- | --- | --- |
| Each entity's model (Borg7), portrait (Borg8), height, collision radius, scale and one unknown float, keyed by entity list position + 1. Every entity has one; the code's fallback to the bat model and a generic portrait never runs. | `gEntityExtras` | `0xE1930` | Read (`EntityModels`) |
| Each dialogue entity's model, portrait, height and scale, keyed by dialogue entity list position + 1. 32 of the 37 dialogue entities have one. | `dailougEnt_info_array` | `0xE3178` | Read (`EntityModels`) |
| The position lists that code tables use for dialogue entities, spells, armor, weapons and items, like the entity list. The entity and dialogue entity lists are in the same order as their DB tables. | `DialougEntList`, `SpellList`, `ArmorList`, `weaponList`, `itemID_array` | `0xE1900`, `0xE3740`, `0xE1850`, `0xE3790`, `0xE3590` | Dialogue entities read; the rest to do |
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
- Alaron's head is its own scene, `0x25AA` (`some_borg5`). `Actor::ChangeAppearance` loads it only for his actor, whose body has a hidden one-triangle placeholder at the neck, and the player renderer draws it at locator 3 of his body's scene, turned toward the object the player is near, up to 80°. An actor record field (layer 1). Carried as a fact: the importer writes it on actor `0x25F9` (`ActorHead`).
- Each map set's spawn points: literals in `BorgMaps::LoadMap` giving a chunk and a position in it per portal number. A new game uses the Overworld's portal 0: I08 at (16, 62), dropped from a height of 10. A map set field (layer 1). To do: the runtime starts there by default.
- The camp, game over and new-game Borg9s (see above).
- Each map set's chunk spacing: literals in `BorgMaps::LoadMap`, a map set field (layer 1). Carried as a fact (`MapSets.CellSize`).
- Sky gradients, sun, moon and stars, clouds, lens flare, rain ambience and four thunder sounds (`sky.cpp`, `skyObjects.cpp`, `Clouds.cpp`, `Lensflare.cpp`, `weather.cpp`).
  - The eight sky gradients' Borg8s (`sky_borg8s`, `0xCEB50`). Read (`Skies.ReadGradients`).
  - The sun, moon and stars (`skyobjectStructs`, `0xDEE48`): 20 bytes each, ending with a type 3 entry. 91 stars, then the sun and the moon. Read (`Skies.ReadSkyObjects`).
  - The clouds (`gCloudData`, `0xDE808`): 28 bytes each, 55 and an end entry. Read (`Skies.ReadClouds`).
  - The lens flare's eight pieces (`gLensFlareData`, `0xDE770`). Read (`Skies.ReadLensFlare`).
  - The images those draw with: literals in `skyObjects.cpp`, `Clouds.cpp` and `Lensflare.cpp` (the sun, star, six moon phases, three clouds and the flare's ring and disc), and the stars' scale of 0.19, which `Skyobjects::Init` sets in place of the table's 0.2. Sky record fields (layer 1). Carried as facts (`Skies.SunImage`, `MoonImages` and so on), on the sky record.
  - Each terrain type's sky, and whether it's drawn: a switch in `BorgMaps::GetMapTerrain`, a terrain field (layer 1). Carried as a fact (`Skies.ForTerrain`, checked against the debug ROM's jump table), on the terrain records.
  - How the colours come from a gradient: the constants in `Sky::SetColors` (0.65, 0.15 and 0.2), and the change of sky over 600 ticks. Engine behaviour (layer 3), in `Aidyn.Core.Sky`.
  - A new game's date and time, noon on day 2 of week 6, and the 72 in-game ticks per tick (`InitGameGlobals`, `World::init`). Game settings (layer 1). Carried as facts (`GameClock`).
- Combat visuals: the arrow and thrown-potion models, target and spell markers, and the stat indicator digits (`combatEntity.cpp`, `CombatMarkers.cpp`, `SpellMarker.cpp`, `StatIndicator.cpp`).
- Bodies' shadows: the texture, Borg1 `0x0003` (`BORG1_PlayerShadow`, which `InitPlayerHandler` is given), and how far from the camera's aim they're drawn: 20 in trek, 128 in combat and none on the title screen (`InitZoneEngine`). A game setting (layer 1). Carried as a fact: the runtime draws `aidyn:texture/0x0003`, and the core holds the distances (`BodyShadow`).
- The particle textures: 17 slots of Borg1s 0–12, some empty (`partTextInds`, `ZoneEngine.cpp`, `0xCE460`). Read (`ParticleTextures`).
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
- **Most map items are never given.** Only Map 2 (in a container) and Map 7 (sold by shop 17, STTA05 in Talewok) have sources, so the world map fills in as the party explores. No script, container, monster party, loot table or shop gives Map 5. Its region flag (791) is set on entering Talewok's chunks, which shows the same piece of the world map, so that may be what's remembered as getting it. The checkpoints can't settle it: they hold no maps at all. The Skull Key and the Map to the Goblin Lair have no source either; they're possibly cut content. The Blood Key is the only key item given as a monster party's direct loot.
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

## Still open

Everything the first pass found is audited. What's left:

- Flags 955–958 and 960–963 (see above).
- Which Barrows minimap piece each of its flags changes (`MiniMap::BarrowsFlags`), and Oriana's path lights.
- The content tables and switches marked To do are located but not read. The importer will need them.
