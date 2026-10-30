# Hardcoded content (roadmap 1H)

The content the original keeps in its code rather than its data: where 1.0 has each piece, which of [ADR 0008](adr/0008-content-logic-layers.md)'s layers it goes in, and what carries it in the port. How each behaves is its system's doc.

1. **Record:** a table-shaped fact.
2. **Trigger:** *on event, if condition, do effects*.
3. **Feature:** engine behaviour configured by data.
4. **Script:** Lua, for what's truly one-off.

**Status** says where the port gets it:

- **ROM:** read from the code segment at the version's offset: a `CodeTable` (`RomTables.cs`) placed by `RomVersions`, read by a reader in `HBHT.Rom.Engine` (the flag tables by `EngineTables`). The tables' bytes are the same in all three ROMs and only their offsets differ, but for the spell visuals' code and the tables of pointers ([rom](formats/rom.md#where-each-version-has-its-tables)).
- **Fact:** carried by the importer (`EngineFlags` for flags), because the code expresses it as a switch or a literal. Each is checked against 1.0's code.

Every flag that engine code names carries its label on the flag record, which the editor shows.

## Flags the engine writes

| What | Code | Layer | Status |
| --- | --- | --- | --- |
| Party members' joined, left and died flags | `Party::CheckFlags` | 1: member record | ROM |
| Key items: each gets an inventory slot, a "has" flag, and sometimes a world map region flag | `PartyInventory.cpp` | 1: item record | ROM |
| World map regions: entering a chunk sets its region's flag, outdoors in a map set with no minimap | `set_map_event_flag` (`ZoneEngine.EnterRegion`, `0x8004960c`) | 1: the chunk's `region` | ROM |
| Boss flags: winning a battle with a boss in its first four enemy slots | `Combat.SetKillFlags` (`set_boss_flag`) | 1: the combat record's `bosses` | Fact, by entity list place (jump table `0x800D2B60`) |
| Cinematic done flags (3, 569, 590, 601) | `Cinematic::EndCase` | 1: cinematic record | ROM |
| The encounter flag of the Goblin Ambush and Lugash scripts' battles | `dialoug_func` | 1: script field | ROM |
| Flag 47 after the Goblin Ambush however it ends, with the heal and the move to Oriana's hut | `Combat.Exit` (`clear_combat_func`) | 1: the combat record's encounter `end` | Fact |
| Flag 566 when the Shadow merges with Alaron | `Combat.MergeShadow` (`0x800675C0`) | 3: the combat record's `shadow` boost | Fact |
| Sholeh's died flag (622) whenever she dies in battle, including as an ally | `Combatant.Death` (`0x80062080`, `CombatEntity::Death`) | 1: member record, and the combat record's `ally` | Fact |
| First spell learned (3611) and first potion bought (3612), which show help | `WidgetSpellTrain`, `WidgetBarterControl` | 2: triggers | Fact: the windows set the flag and show the help; the roadmap lists moving it onto triggers |
| New journal entry waiting (3617), and which derived flags add journal entries (5556 on) | `set_journalentry_flag` | 1: flag records and the state flags record | Fact |
| Calendar, weather and terrain flags. The best party stat and skill flags are never written: their setters' lookup always fails. | `gameStateFunnel.cpp` | 3: state exposed as flags | Fact, the state flags record |
| Camp dialogue check (4240, set while it runs) | `Camp.cpp` | 3: camping | Fact |
| Cheat flags (3673, 3675, 3693, 3694) | `CheatCodes.cpp` | 2: cheat triggers ([cheats](#cheats)) | Fact (the flags only; the cheats aren't triggers yet) |

## Flags the engine reads

| What | Code | Layer | Status |
| --- | --- | --- | --- |
| Camp scenes: the first dialogue object in the player's chunk whose flag A is one of eight flags, and holds, starts when the party camps, however far it is | `Camp_CheckDialougFlags` | 1: the camping record's `scenes` | ROM |
| Camp ambushes only happen while derived flag 5004 holds: two of Abrecan, Godric and Rheda have joined | `Party::CampCheck` | 1: the camping record's `ambushes` | Fact |
| Alaron has his True Name while 590 is set | `entityInfo.cpp` | 1: character condition | Fact, on his entity record |
| When the Rooughah scene (Borg13 1268) ends, the Roog cinematic plays if 3442 is set | `CutScene.Free` (`0x8005179c`) | 2: trigger on `script.ended` | Fact, `aidyn:trigger/2` ([events](systems/events.md)) |
| World map pieces: each shows while its region flag is set | `WorldMapPieces` | 1: the world map record (`aidyn:worldmap/0`), with the towns' names and the party's ring | ROM |
| The lighthouse chunks swap to their lit scenes while 5017 holds | `ZoneEngine.cpp` | 1: chunk scene condition | Fact |
| Party skill checks in dialogue roll 0–21 while 3717 is set | `DialogEvalCallback` | 3: debug option, in the state flags record | Fact |
| Minimap pieces chosen by flags: the Ehud underground (4222–4232), the Roog entrance (4233–4237) and the Barrows (4286–4294). Which Barrows piece each flag shows is code. | `minimap_dats_2`, `MiniMap::BarrowsFlags` (`0x8004bfac`) | 1: the minimap records' rules | ROM, the Barrows' as facts (`MinimapImporter`) |
| The Theatre menu lists a cinematic once its title's flag is set | `TheatreMenu.cpp` | 1: DB | ROM (DB block) |
| Key item flags restore the key items when a save loads | `LoadKeyItems` | 1: item record | ROM |
| Flag 5626, read after every map object's flag write; the result is never used, but the read brings the flags up to date, which the port does directly ([map objects](systems/map-objects.md#map-objects-write-flags)) | `objectJumpTables.cpp` | None | Fact |

## Scripts and cinematics the engine starts

| What | Code | Layer | Status |
| --- | --- | --- | --- |
| The four cinematics: each shot's Borg6 animation, Borg12 audio, caption list and clip planes, the fades, and the done flag | `Cinematics.cpp`, `CinematicFade.cpp` | 1: cinematic record | ROM |
| What starts each cinematic: a new game, the Shadow merging with Alaron, and the ends of the Rooughah and last scenes | `Cinematic.Load`'s callers | 2: triggers on `game.started` and `script.ended`; the Shadow's is 3, the combat record's `shadow` | Fact: the Shadow's on the combat record, the rest triggers ([events](systems/events.md)) |
| What follows each cinematic: the opening starts script 866 from its object in Overworld I08, the Roog cinematic gives Alaron his True Name, and the ending goes to the credits | `Cinematic.EndCase` (`0x80053c20`) | 2: triggers on `cinematic.ended` | Fact, as triggers |
| Scripts whose end leads somewhere: the three death scenes end the game, 1268 plays the Roog cinematic, and 1278 plays the ending | `borg_short_list`, `CutScene.Free` | 2: triggers on `script.ended` | ROM, as triggers |
| Scripts after which eight hours pass: the eight camp scenes and the Campsite's own script | `isCampfireScene` | 1: script field | ROM |
| Game over: script 1298 starts from its object in the Dreamscape Borg9 (3423) | `Gameover_func` | 1: game setting | ROM (script only) |
| A new game: the player arrives in Overworld I08 at (16, 62), his body made 10 up and set down, and the party is Alaron alone with 1 gold; the player's body is Alaron's actor | `Title.Leave` (`0x80055718`), `Zones.SetMapSet` (`0x80026374`, `0x800ce7d8`), `InitGameTrek` (`0x80021018`), `init_DBs` | 1: the new game record (`aidyn:newgame/0`) | Fact |
| Camping with no camp scene nearby starts the Campsite Borg9's (6236) first dialogue object | `campfire_func` | 1: the camping record's `campsite` | Fact |
| The help menu is script 1355 | `gametrek.cpp` | 1: game setting | Fact |

## Bespoke behaviours

ADR 0008 names six behaviours as bespoke. Only two need layer 3 features: the Goblin Ambush and the Shadow. The other four are records and triggers. None needs a script. [combat](systems/combat.md#the-special-battles) has how the two fights run.

### The Goblin Ambush (encounter flag 978)

`gameCombat.cpp`, `combatEntity.cpp`, `turn.cpp` and `combatEngine.cpp` key on the encounter flag. The port carries each rule on the combat record's encounter rule for 978 (`EncounterRule`), but the fleeing, which is the record's `fleeByEncounter`.

| Behaviour | Carried as | Code |
| --- | --- | --- |
| No fleeing | `fleeByEncounter` | `encounter_id_check` |
| The party's first member healed in full as it starts | `healsLeader` | `Combat.Init` (`0x80022D1C`, `init_combat_struct`) |
| Alaron's hold: each blow that would hurt him deals 1; the second drops his Strength to 0 and plays his fall, so the battle is lost with him alive | `keelOver` (Alaron, 2 hits) | `Combatant.ShadowFightTrick` (`0x80065DCC`), `Combat.ApplyHit` (`0x80021930`) |
| No game over: the "PlotPoison" line, locked to Alaron, then the fade, the flag set and the clock at 6:00 the next day | `nonLethal`, `speaker` | `TurnOrder.Defeat`, `Combat.StateDefeatWait`, `Combat.StateDefeat` (`CombatTurn::PartyDead`, `CombatStateFunc_13`) |
| No victory line; boss flags and loot still come | `quietVictory` | `TurnOrder.Victory` (`CombatTurn::EnemiesDead`) |
| Won, lost or fled: the leader's Strength restored, Alaron healed, flag 47 set, and the party moved to Oriana's hut (MiscInteriors E03) at (5.9, 0.5, 3.0) | `end` (`EncounterEnd`) | `Combat.Exit` (`clear_combat_func`) |

The original adds the base Strength to the current, doubling it when the second hit hasn't taken it to 0; the port sets it to the base, the user's choice.

### The Shadow (entity 0x0226)

A feature on the entity, with the Horn of Kynon as its item: the combat record's `shadow` (`ShadowRule`), with two lists of the combat record itself.

| Behaviour | Carried as | Code |
| --- | --- | --- |
| The party may flee while it's in the battle, before the encounter table has the last word | the combat record's `flee` | `look_for_boss_shadow` (`Combat.ForbidFleeForBosses`, `0x8005CCCC`) |
| No one attacks it or casts at it, and it casts nothing | the combat record's `untargetable` | `Combatant.CanAttack` (`0x80060590`), `Combatant.CanCast` (`0x80060C38`), through `Combat.ShadowSlot` |
| Its aura: at each round's end, 10 − distance damage to each combatant within 10 that isn't of chaos, squared without the item | `radius` (10) | `Combat.ShadowAura` (`0x80023AD0`, `shadow_combat_func`, from `TurnOrder.EndRound`) |
| Slowing: a move range of 1 within 10, with the vortex line; the AI's paths cut at 10 | `radius` | `Combat.NearShadow` (`0x80023F34`), `Combat.MovePlayerBody`, `EnemyAI.ShadowSmoothPath` (`movement_checking_shadow`) |
| Vanishing without the item: the aura works at four round ends and the Shadow goes at the fifth, with nothing won | `vanishRounds` (4) | `Combat.ShadowAura`, `Combat.ShadowDissipates`, states 29 and 30 (`merge_no_horn`) |
| Merging when its body and Alaron's touch: with the item, Alaron merges and its cinematic plays after; without, it vanishes. With the item it's made with base Dexterity 30 and walks at Alaron | `item`, `merges`, `mergeReach` (0.5), `dexterity` (30), `cinematic` | `Combat.CheckShadowMerge` (`alaron_shadow_merge_attempt`), `Combatant.Init`, `EnemyAI.RunStyle` |
| The merge's boost to Alaron's stats, protection, skills and spells, then flag 566 | `boost` (`ShadowBoost`) | `Combat.MergeShadow` (`0x800675C0`, `ShadowMergeBoost`) |
| Healing barred: potions heal nothing, the Healer is refused (Common line 437), and Teleportation fails | No field: `Battle.HealingBarred` and `TeleportBarred` hold while the Shadow is in the battle | `Entity.HealPercent` (`0x800715DC`), `Party.LayOnHands`, `Party.HealWithHerbs`, `Combatant.ResistRoll` |

### The other four

- **Brenna's Ranger check in Gwernia** is the camp ambush roll. The code only names Brenna in the flag `FLAG_Gwernia2MembersNoBrenna` (5004), which `Party::CampCheck` reads before rolling against the party's best Ranger. See [Party and camping rules](#party-and-camping-rules).
- **The Lugash fight** has no code of its own. It's script battle 1233, whose encounter flag (5250) is derived, so a win never sets it. The encounter table lets the party flee, Lugash uses the AI's boss attack style, and beating him sets boss flag 533. All of these are records; his second script battle (`0x04D6`) has no flag and can't be fled. **Pochanargat** is the same: rooted, no fleeing, boss flag 3204, and nothing else in the code names either.
- **Sholeh** is two entity fields (layer 1). If an encounter lists her in its first four enemy slots, she fights on the party's side instead (`find_sholeh`). Her died flag, 622, is set whenever she dies in battle (`CombatEntity::Death`). Her left flag is 694.
- **Alaron's True Name** is a character flag. The entity DB has it (`HasTrueName`), and spells marked True Name need it. Alaron gains it when the Roog cinematic ends, and again whenever his character sheet is built while 590, that cinematic's done flag, is set (`entityInfo.cpp`).

### Fleeing and bosses

| What | Code | NA 1.0 | Status |
| --- | --- | --- | --- |
| Fleeing: monster party battles allow it, camp ambushes don't, and script battles allow it unless the script is a cut-scene. Then a boss among the enemies on the field forbids it (Kitarak, the Marquis, Pochanargat, Shamsuk, Sheridan, Ksathra, Shatrevar, Mehrdad, Golnar, Nasim, Behrooz and Assim), the Shadow allows it, the last on the field having the say, and last, three encounter flags override: 978 forbids it, 5250 (Lugash) and 3749 (the Marquis) allow it. | `Combat.ForbidFleeForBosses`, `Combat.ApplyFleeRuleByFlag` | `0xD3288`, `0xD32A0` | Fact: the combat record (`aidyn:combat/0`), its entities by their places in the ROM's entity list |
| Sholeh, among an enemy list's first four, fights on the party's side | `Combat.FindSholeh` (`0x8005C6C0`) | entity list place 0xA4 | Fact: the combat record's `ally` |
| Bystanders: the townsfolk, shopkeepers, trainers and the like are dropped from an enemy list | `Combat.IsExcludedEnemy` (`0x8005C0C0`) | entity list places 0–0x56, 0x82–0x90, 0x92–0x96 | Fact: the combat record's `bystanders` |
| A monster party whose roll comes to none fights one Skeleton | `BattleSetup.FillEnemies` | entity list place 0xBB | Fact: the combat record's `fallback` |
| Pochanargat never moves in battle: his field of influence is 0 | `Combatant.UpdateRadius` (`0x8005F29C`) | entity list place 0xAB (`0x800E28EB`) | Fact: the combat record's `rooted` |
| The weapons that fly to their target rather than strike in melee: 8 bows (positions 53–60, flying as the arrow, Borg5 `0x348F`) and 8 thrown weapons (positions 94–99, 101 and 102, flying as their own models), each with a muzzle speed and a turn about the vertical (the Javelin's 90). Spikes (position 100) is the one ranged weapon that may strike in melee too. | `Combatant.UpdateDrawnFlag` (`0x80066A2C`), `LoadWeaponScene` (`0x80066CF8`), `WeaponMuzzle` (`0x80066DD0`), `Weapon.AllowsMelee` (`0x8007B730`) | lists `0x800E0C08` and `0x800E0C14` (weapon DB positions, `0xFF` ends), muzzle floats `0x800D2AE0`–`0x800D2B44` | ROM and Fact: the combat record's `missiles` (the lists read, the numbers carried and checked by weapon name) and `closeRange` |
| The entities whose weapon isn't drawn in the hand, their models having theirs: Shamsuk, the Gorgon, the Chaos Lieutenant, the Harpy, the Marquis, the Minotaur, the Minotaur Lord and Niesen | `Combatant.HasNoWeaponAttachment` (`0x80066C5C`) | entity list places at `0x800D2A0C`, `0xFF` ends | Fact: the combat record's `undrawnWeapons` |
| The entities who hold a bow at locator 0 and its arrow at 1, the other way round from the rest: the bandit bosses and woodsmen, Becan and Keelin | `EntityBody.Hand` (`0x8006C8BC`), a switch | entity list places 0x57–0x59, 0x5C–0x5F, 0x6C, 0x7A, 0x9C, 0xA1 | Fact: the combat record's `otherHand` |
| What a body holds in battle: the slots' first locators (the hand 0, the shield arm 2, the head 3), the flask's model (`0x34C5`) for a flask or potion, the arrow (`0x348F`) for a bow, and the Troubadour's notes (`0x1ABB`, a billboard) raised 1.0 | `Combatant.EquipVisuals` (`0x80066AEC`), `Combatant.StartTroubadour`, `RenderPlayers` | `0x800CDC0C`, literals | Core: `Combatant`'s `DefaultLocators`, `NotesScene` and `NotesRaise`, `Battle.FlaskScene`; the arrow is the missiles' |
| The arena a battle takes by the terrain of the chunk it starts in | `BattleSetup.ChooseArena`, a switch | jump table `0x800CD400` | Fact: each terrain record's `arenas` |
| Enemies with spells that use the AI's boss attack style: 13 entities, including Lugash and the Wight | `EnemyAI.HasBossStyle` (`0x8005BA40`, `CombatAI::IsBoss`) | jump table `0x800D24B0`, by entity list place | Fact: the combat record's `bossStyles` |
| The spell the AI centres on its opponents and aims (Fireball, spell kind 8), and the party's area danger: the flask held (Inferno 7, Fire 5, Sleep Gas or Acid 4) or twice a living member's Fireball level | `EnemyAI.AreaCentre`, `EnemyAI.AimAreaSpell`, `Combat.PartyPotionTier` (`0x8005C750`), `Combat.CreatePartyCombatants` | kind 8 by `GetIdIndex`; the item IDs and tiers in the code | Fact: the spell record's `area` ([ADR 0021](adr/0021-instant-effects-land-in-one-place.md)) and the combat record's `flaskDanger` |
| The combat menus' words and Sense Aura's, C strings in the code segment rather than a string table: the skill list's three rows, what C-down and C-up say with nothing to list, and Sense Aura's popup lines (some printf formats). A resistance's line is Immune at a multiplier of 0 or less, Resistant below 2.0 and Vulnerable at 2.0 or more; the element name is `String/Common`'s line 241 + the element byte. | `CombatMenu.PickLayOnHands`, `PickHealWithHerbs`, `PickTroubadour`, `OpenSpells`, `OpenItems`; `Entity.SenseAura` (`0x80070070`), `Entity.AppendResistanceLine` (`0x8006FFB0`) | `0xD5480`, `0xD5490`, `0xD54A0`, `0xD5458`, `0xD5434`; `0xD3E50`–`0xD3E84`, `0xD3E14`–`0xD3E34`, the float `0xD3E48`; the element names' pointer `0x800E0C00`, set by `0x80083620` | ROM (`CombatImporter.ReadText`): the combat record's `text`. The element names are Common's lines |

## Party and camping rules

- **Brenna never leaves because no script removes her.** Her row in the party table has no left flag (0), and nothing else in the code names her.
- **Rheda has no rule in code** beyond the party tables. Scripts add and remove her like the other recruits; the full-party template won't let her leave before flag 458, Beat Shamsuk ([characters](systems/characters.md#joining-and-leaving)).
- **Which members' sheets are kept when they leave** (`SaveEntity::GetSlot`, a jump table at 1.0 `0x800CDD88`): Abrecan, Arturo, Becan, Godric and Keelin, by their entity list positions. Layer 1, the member record (`MemberFlags.Kept`); a fact (`CharacterImporter.KeptMemberPositions`), as it's a switch.
- **The Archmage Staff stays with a member who leaves** (`Party::StripMember`, through `RemoveWeaponsFrom`'s check), as it does when taking it off. It's the weapon record's `Fixed`.
- **Camp ambushes and camping's refusal** (`Party::CampCheck`): the roll's 15, 10, 1/15 and the half heal are camping settings (layer 1); the terrain's factor is a terrain field, and a factor of 0 refuses camping. [camping](systems/camping.md) has the rule.

## Terrain and weather

Terrain types are the 0–29 index that map chunks carry and the camp encounters are keyed by. These tables are terrain and season records (layer 1).

| What | Code | NA 1.0 | Status |
| --- | --- | --- | --- |
| Camp ambush factor per terrain type; 0 means no camping | `camp_float_array` | `0xE3B38` | ROM, the terrain record's `camp` |
| Who ambushes a camp, per terrain type and tier of the party's average level | `CampEncounters.Load` (`0x800096DC`) | ROM `0x1FEDE50` (the DB block, [db](formats/db.md)); the tiers are literals | ROM, the terrain record's `ambushes`; the tiers the camping record's `ambushLevels` (Fact) |
| Precipitation chance per season (6, and 2 bytes of padding) and per terrain type (28), and precipitation scale per terrain type | `sSeasonPrecipChance`, `sTerrainPrecipChance`, `sTerrainPrecipScale` | `0xE3C40`, `0xE3C48`, `0xE3C64` | ROM (`WeatherTables`): the seasons' on the weather record (`aidyn:weather/0`), the terrains' as the terrain records' `precipitation` and `precipitationScale` |
| Snow falls instead of rain on terrain type 5 only | `Weather.Roll` (`0x8007bdb0`) | Literal | Fact, the terrain record's `snow` |
| When the weather changes and to what, and the wind's wander | `Weather.Roll`, `World.Update`, `Weather.DriftWind`, `ProcessWeather` | Literals | Fact, the weather record ([weather](systems/weather.md)) |
| How rain and snow are drawn: their particle texture slots (rain 4 and 16, snow 0), colours, fall, spawn intervals and lives, and whether rain splashes of itself; the spread, heights, sweeps and splash sizes are the engine's | `ProcessWeather`, `WeatherParticles.Make`, `WeatherParticles.SpawnDrop` and the rest (`0x8001e04c`–`0x8001e7c0`) | Literals; colours at `0x800ce0d0` and `0x800ce0e0` | Fact, the weather record's `Rain` and `Snow`, the slots looked up in `partTextInds`; the rest is engine behaviour (layer 3) in `WeatherParticles` ([weather](systems/weather.md#rain-and-snow)) |

## Characters

How characters level and train is tables and literals in code (roadmap 2.2; see [characters](systems/characters.md)).

| What | Code | NA 1.0 | Status |
| --- | --- | --- | --- |
| The experience each level starts at, levels 0–40 | `EXP_TNL` | `0xE3804` | ROM, on the progression record |
| Each stat's base cap and cap | `base_stat_cap`, `modded_stat_cap` | `0xE3BE4` | ROM, on the stat records |
| Each stat's training cost; the Level's (500) is unused, as the level isn't trained | `stat_multiplier` | `0xD4620` | ROM, on the stat records |
| Each skill's training cost | `CharSkills::GetSkillXpMod` | `0xD44D0` | ROM, on the skill records |
| Each weapon class's training cost | `CharSkills::GetWeaponXpMod` | `0xD4500` | ROM, on the weapon skill records |
| The shield's training cost (500) and name ("Shield") | `GetShieldXpPrice`, `WidgetSkillInfo` | Literals | Fact, on `aidyn:weaponskill/11` |
| Skills' caps, 10 and 15 | `SKILLMAXBASE`, `SKILLMAXMOD` | Literals | Fact, on the skill records |
| An award of experience is multiplied by 1.5; an entity's reward byte by 50 | `Entity::AddExp`, `EntityDB::Load` | Literals | Fact, on the progression and entity records |
| Trainers charge 200 gold from level 0, and (n + 1) × 40 for level *n*, and 80% of the experience | `GetSkillTrainGoldPrice`, `WidgetSkillTrain::Confirm` | Literals | Fact, on the progression record |
| Spells stop at level 10, and cost their byte × 50 per level squared | `TempSpell::IsMaxRank`, `TempSpell::Init` | Literals | Fact, on the progression and spell records |
| What each spell and potion does: the switches by spell kind, the dispel, charm and immobilise lists, the entities charms and Banishing may take, Restore's and Wraith Touch's lists, the damage spells' elements, the AI's value of each, and casting's numbers | `Entity.AddEffect`, `Combatant.ApplySpellEffect`, `Combatant.DrinkPotion`, `EnemyAI.SpellValue`, `0x800D29BC`–`0x800D34B4` | Switches, lists, floats | Fact and ROM: effects on the spell and potion records ([ADR 0020](adr/0020-one-effect-vocabulary.md)), and `aidyn:magic/0` ([magic](systems/magic.md)) |
| Which potions are thrown and where the rest are drunk: kinds 0–3 are flasks, thrown and never drunk in trek, and C-up's menu leaves out kind 16, Stealth | `CombatMenu.ListPotions` (`0x80088190`), `Entity::CanUsePotion` (`0x8006FBE8`), `Combatant.ThrowFlask` | Comparisons on the kind | Fact: the potion records' `use` ([ADR 0021](adr/0021-instant-effects-land-in-one-place.md)) |
| Oriana trains for no gold: entity list position 144 | `shopkeepNotOriana` | Literal | Fact, the progression record's free trainers |
| Alaron's True Name from flag 590: entity DB position 153 | `CharExp::Init` | Literals | Fact, on Alaron's entity record (`trueNameFlag`) |

## Trainers

The Train effect teaches from the record of the entity being talked to: its skills, shield skill and spells (`init_skill_trainer`). No trainer is special-cased in code, apart from Oriana charging no gold (above). Becan is also a recruit, so one record sets both what he teaches and what his party character can learn: a blank skill is 255 for him, which a party character reads as "can't learn", and 0 for other trainers. It's data (layer 1), read from the entity DB.

## Cheats

Cheats are typed as Alaron's name, in the title menu's name entry or the options menu (`Cheats::Check`). The name is lowercased first. A cheat only takes effect when the pause menu exists, which also stops the XP and gold cheats crashing the title screen. The codes are the `Cheats` string table, so they're in the ROM; which string runs which effect is code.

| Code | Effect | Layer |
| --- | --- | --- |
| `!balloon`, `!bigw` | Toggle drawing heads, or weapons, at double size | 3: render options |
| `!flea` | Toggles a quarter-size Alaron who jumps with A | 3: player option |
| `!slashing` | Sets 3673, which shows the Hockey Stick and its container in Gwernia Castle E03 | 2: trigger, set flag |
| `!darkside` | Sets 3675, which shows the Lightreaver and its container in Oriana's hut (MiscInteriors E03) | 2: trigger, set flag |
| `!crawdaddy`, `!fatboy`, `!tweety` | Change the player's model to the chaos trooper, ogre or gryphon, or back to Alaron if it already is | 3: player model |
| `!cheater` | Once (flag 3693): 500,000 XP to each party member. | 2: trigger, add XP |
| `!bingo` | Once (flag 3694): 100,000 gold | 2: trigger, add gold |
| `Keepbusy`, `Imadoofus` | Each tells you to enter the other. | 2: trigger, show a message |
| `!version` | Shows the version, compile time and code size, and turns on the coordinate display | 3: debug option |
| `!gene`, `!gotmilk` | Strings with no effect | None |

The debug ROM also has a game state cheats menu. Only its "appear" option is read, and only in the debug build.

## Assets engine code names

The map chunk lists are read from the ROM (`MapChunks`). The rest of the Borgs engine code names fall into four groups.

**Content tables** pair records with their assets. Mods adding a monster, weapon or item need to set these, so each becomes a record field (layer 1). The 1.0 offsets are where each starts, and the reader named in the status reads it.

| What | Code | NA 1.0 | Status |
| --- | --- | --- | --- |
| Each entity's model (Borg7), portrait (Borg8), height, collision radius, scale and the share of its attacks' animations at which the blow lands (`EntityBody.Strike`, [combat](systems/combat.md#an-attack)), keyed by entity list position + 1. Every entity has one; the code's fallback to the bat model and a generic portrait never runs. | `gEntityExtras` | `0xE1930` | ROM (`EntityModels`) |
| Each dialogue entity's model, portrait, height and scale, keyed by dialogue entity list position + 1. 32 of the 37 dialogue entities have one. | `dailougEnt_info_array` | `0xE3178` | ROM (`EntityModels`) |
| The position lists that code tables use for entities, dialogue entities, spells, armor, weapons and items. Each is its DB table's own order and nothing else: place p holds the ID of the DB's p-th record (its low byte for all but the items'), so a table keyed by position is keyed by the DB's record. | `EntityList`, `DialougEntList`, `SpellList`, `ArmorList`, `weaponList`, `itemID_array` | `0xE3440`, `0xE1900`, `0xE3740`, `0xE1850`, `0xE3790`, `0xE3590` | ROM (`PositionLists`), checked against each DB before a position is used. They add no content, so they aren't imported; the tables keyed by them read by the DB's order |
| Weapons', items' and shields' models (Borg5), by position ([db](formats/db.md)) | `weapons_with_borg5`, `weapon_borg5_ID`; `ItemBorg5Search`; `ArmorDB::GetBorg5`, a switch | `0xE3520`, `0xD3B14`; `0xE3564`, `0xD3B9C` | ROM (`ItemTables`), the shields' a fact (`ItemTables.ShieldModels`): each record's `Model` |
| Item icons (286, by item ID; others get a default, and every potion shares one), then skill, aspect, school and spell-symbol icons, each list followed by its count | `menuimages.cpp` | `0xDF700` | ROM (`ItemTables`): items' and skills' icons are on their records, spell symbols' on spell records. Aspects' and schools' are UI chrome (`HBHT.Core.Ui.MenuImages`) |
| Potions' prices, by potion | `potion_prices` | `0xE38B0` | ROM (`ItemTables`) |
| The Alchemist's and Mechanic's recipes | `potionRecipies`, `ArmorRecipies` | `0xD8560`, `0xD8540` | ROM (`ItemTables`), as recipe records |
| The items with inventory slots of their own, the unique gear with its own, and the items loot can't be left behind with | `PartyInventory.cpp`, `key_item_ids` | `0xE3910`, `0xE3A70`, `0xE38E0` | ROM (`ItemTables`), as item traits |
| Each spell's icon symbol, by spell position | `SpellDB::GetIcon` | `0xD3C58` | ROM (`MenuTables`), as spell records' `Icon` |
| Each item's, potion's and spell's line in the Items, PotionDetails and Spells string tables, for C-down's details. Two potions (`0x100B`, `0x100C`) and four spells (`0x030F`, `0x0311`, `0x0327`, `0x0328`) have none. | `WidgetItemDetail` | `0xD03A0`, `0xD0360`, `0xD04EC` | ROM (`MenuTables`), as item and spell records' `Description` |
| The order the inventory list shows item kinds in | `D_800ed560` | `0xDF600` | ROM (`MenuTables`); a UI fact in `HBHT.Core.Ui.InventoryOrder` |
| Three messages that are C strings in code rather than string table lines: a potion that would do nothing now, a flask or potion with no effect outside combat, and the Lighthouse scroll, which has no spell | `Entity::CanUsePotion`, `Party::UseScroll` | `0xD3D50`, `0xD3D78`, `0xD435C` | ROM (`MenuTables`), as the inventory record's `Messages` |
| Each equipment slot's empty image (15, by slot) | `gItemSlotBorg8Indecies` | `0xDF670` | ROM (`MenuTables`); a UI fact in `HBHT.Core.Ui.MenuImages` |
| The credits' cards: the first line of each in the credits' string table, the first of each a heading, and how many show one at a time before the rest scroll (the card ending at line `0x1D`, a literal) | `Credits.CardStarts`, `Credits.StepCards` | `0xCFC60` | ROM (`CreditsTable`), as the credits record (`aidyn:credits/0`); the 12 shown is a fact |
| Spell and potion visuals: five `switch` functions on the kind with jump tables (a spell's caster actor, target actor, whether the target's stands at the head, and aura scene; a potion's actor and aura scene), each actor with the memory it asks for, and the actors a refused and a resisted cast play (immediates `0x1CAA` and `0x37F1`). The same in all three ROMs ([magic](systems/magic.md)). | `combatspellvisuals.cpp`, `CombatSpellFx.AddVisual` | Functions `0x8008CB40`–`0x8008D1DC`, tables `0x800D4C98`–`0x800D50BC`, immediates `0x8008B8D8`, `0x8008B8E0` | ROM (`SpellVisualTables`, decoding the jump tables and case blocks), as spell records' and potion item records' `Visuals` and the magic record's `Refused` and `Resisted` |
| The actors `ProcessPlayers` keeps animating in combat while they idle 13.0 or more from the camera's aim, where any other's animation stops: nine Borg7s (the bat, wyvern, harpy, the two dryads, Pandara, gryphon, manticore and scorpion), u16s ending with a 0. They are the decomp's flying models, and no code treats them as flying. | `Body.AlwaysAnimated` (`Actor::IsFlyingModel`), searched by `Body.InAlwaysAnimated` (`0x80011830`) from `ProcessPlayers` (`0x80012784`) | `0xCE650` | ROM (`ActorTables`), as actor records' `AlwaysAnimated`. The port steps every body's animation, so nothing reads it |
| The 39 Borg12 sounds that Borg13 PlaySound operands index | `dialoug_SFX` (`Dialogue.SoundTable`) | `0xE0960` | ROM (`AudioTables`), as the sounds record's `Script` |
| Footsteps: 49 entries of four step ids each, the sets that pick them by body kind and surface (26 × sneak, walk and run), and the 96 Borg12s step ids name ([audio](systems/audio.md#bodies-sounds)) | `audiokey.cpp` (`AudioKeys.FootstepEntries`, `AudioKeys.StepSets`, `Audio.StepSounds`) | `0xE0A60`, `0xE0BE8`, `0xE0D28` | ROM (`BodySoundTables`), as the sounds record's `Kinds` |
| The 126 Borg12s the audio keys' voices and weapons name, each weapon model's entry in them (8 bytes a weapon), and each spell's cast (59) | `audiokey.cpp` (`Audio.VoiceSounds`, `AudioKeys.WeaponSoundTable`, `Audio.SpellSounds`) | `0xE0EA8`, `0xD50D0`, `0xE10A0` | ROM (`BodySoundTables`), as entity records' `Sounds` (with the audio keys in the DB block), weapon records' `Sounds` and spell records' `Cast` |
| The containers' sounds: eight by kind, then four for a trapped one exploding | `MapObjectUse.ContainerSounds` | `0xCE5D0` | ROM (`AudioTables`), as the sounds record's `Containers` and `Explosions` |
| The menus' sounds: the cursor `0x073B` and `0x073D`, A `0x0740`, B `0x0741`, the tabs `0x074D` and `0x074E`, changing member `0x0739`, the level-up page `0x05E8`, the cheat code `0x0718` and `0x0721`, the coins `0x072D` and `0x072E`, experience `0x05E7` | Literals in the menus and `Dialogue.ExecuteCommand` | | Fact (`AudioFacts`), as the sounds record's `Menu` and its gold and experience |
| Minimap pieces for eight map sets: chunk, group, whether seen from the start, image and position. Which map set each is for, and the pieces chosen by where the player stands (Gwernia's courtyard, walls and tower, the mage school's stairs, the Ehud underground's C11 and I09, the Barrows' E01), are code. | `minimap.cpp`, `BorgMaps::LoadMap`, `ShowingSpecialSection` | | ROM, with facts (`MinimapImporter`), as the minimap records (`aidyn:minimap/<map set>`) |

`Dialogue.SoundTable`, the `AudioKeys` and `Audio` tables and `MapObjectUse.ContainerSounds` are the symbol map's coined names for the original's tables; the port reads them as `CodeTable`s of its own names (`ScriptSounds`, `FootstepEntries`, `ContainerSounds` and so on).

**Settings and features** are engine behaviour that names specific assets. Each becomes a game setting (layer 1) or a field of the feature that uses it (layer 3).

- **The six title-screen fly-throughs** (`gFlycamSequences`, ROM `0xE15DC`, RAM `0x800E09DC`): 20 bytes each, a u32 Borg6 and four u32s each holding a u16. `Title.StartScenery` (`0x80054D70`) builds the title's scenery from entry p (`Title.FlycamPlace`, `0x800E0A54`): 0 for the first title, then the next, modulo 6, each time a fly-through ends, passing over 4 for 5 without the Expansion Pak (`0x800D7EA4`). It loads the Borg6 as the camera's animation, stores the four u16s at `0x800D90DC`, `0x800D90DE`, `Zones.Row` and `Zones.Column`, and calls `InitZoneEngine(2, 0)`: the Overworld's chunks round that row and column, with no place for the player. No instruction names the first two but the stores; they're the chunk whose corner the animation's coordinates are from, as every fly-through's camera runs a few units over the ground from there and from no other ([ui](systems/ui.md#the-title)). ROM (`TitleTables`), as the title record (`aidyn:title/0`): each entry's `Animation` with its `Length`, `Scene`, `Row`, `Column`, `OriginRow` and `OriginColumn`, and `NeedsExpansionPak` on place 4, a fact. The title screen reads it (`TitleScreen`).
- **Each map set's music**, set as it's entered (`Zones.SetMapSet`): literals for Gwernia castle, Erromon, the Jundar interiors, Ehud's underground, Ugarit and Chaos Isle, which picks between two by the chunk's column; two Overworld lists of chunks by their scenes (`0xCF31C`, `0xCF338`), each with a piece with the Expansion Pak and one without; the Jundar interiors' list (`0xCF368`); and the battle music, one of five at random with the Pak or four without (`0xCF378`, `0xCF384`). The lists are ROM (`AudioTables`) and the literals facts (`AudioFacts`), on the map set records' `Music` and their chunks' ([audio](systems/audio.md#music)).
- **The title's and the credits' music**, `0x0632` (`0x05D9` without the Pak) and `0x05D9`, and rain's `0x071B` and thunder's `0x071C`–`0x071F`. Facts (`AudioFacts`), on the sounds record (`aidyn:sounds/0`).
- **The player's model** is Alaron's, a literal the title screen sets (`0x25F9` at `0x800557c0`, in `Player.Actor`, `0x800d8134`), which three cheats swap ([cheats](#cheats)). The port gives the player the party leader's actor (`EntityBody.Actor`, `GameSession.PlayerActor`), which for Alaron is the same.
- **Alaron's head** is its own scene, `0x25AA` (`some_borg5`). `Actor::ChangeAppearance` loads it only for his actor, and the player renderer draws it at locator 3 of his body's scene, turned toward the object the player is near, up to 80°. A fact on actor `0x25F9`'s record (`Head`, an `ActorHead`).
- **Each map set's spawn points**: literals in `Zones.SetMapSet` (`0x80026264`, in `borgmaps.cpp`; called by `TeleportPlayer`, `InitZoneEngine` and `CutScene.LoadStage`), a chunk and a position per portal number: the row and column as immediates (`addiu s7` and `s6`), x and z as floats in the code (into `Zones.SpawnX` and `Zones.SpawnZ`). By portal, (row, column) at (x, z): the Overworld's 0, I08 at (16, 62) (`0x80026374`, `0x800ce7d8`); MiscInteriors' 0, (5, 3) at (5.9, 3), and 1, (1, 1) at (5, 5); Erromon's 0, (1, 15) at (13, 25); the Barrows' 0, (4, 1) at (10, 10); the Jundar interiors' 0, (1, 1) at (16, 15); Ehud's underground's 0, (17, 1) at (5, 5); Ugarit's 0, (13, 5) at (10, 82); and each battle arena's, portal n the set's n-th chunk (a table at `0x800dca74`, ROM `0xDD674`), at (20, 20). `InitGameTrek` makes the player's body 10 up (`0x80021018`) and sets it down from on high. The other sets have none, and a portal of -1 leaves the player where he is. Facts (`MapSets.Spawns`), as the map set records' `Spawns`; the new game record (`aidyn:newgame/0`) takes the Overworld's. The debug build's `Zones.SetMapSet` has other places and can't be imported. Nothing reads the other portals: the runtime reaches those places through teleporters' destinations, a battle's own arena and the new game.
- **The camp, game over and new-game Borg9s** (above).
- **Each map set's chunk spacing**: literals in `BorgMaps::LoadMap`, a map set field (layer 1). A fact (`MapSets.CellSize`).
- **The sky**: gradients, sun, moon and stars, clouds, lens flare, rain ambience and four thunder sounds (`sky.cpp`, `skyObjects.cpp`, `Clouds.cpp`, `Lensflare.cpp`, `weather.cpp`).
  - The eight sky gradients' Borg8s (`sky_borg8s`, `0xCEB50`). ROM (`Skies.ReadGradients`).
  - The sun, moon and stars (`skyobjectStructs`, `0xDEE48`): 20 bytes each, ending with a type 3 entry. 91 stars, then the sun and the moon. ROM (`Skies.ReadSkyObjects`).
  - The clouds (`gCloudData`, `0xDE808`): 28 bytes each, 55 and an end entry. ROM (`Skies.ReadClouds`).
  - The lens flare's eight pieces (`gLensFlareData`, `0xDE770`). ROM (`Skies.ReadLensFlare`).
  - The images those draw with: literals in `skyObjects.cpp`, `Clouds.cpp` and `Lensflare.cpp` (the sun, star, six moon phases, three clouds and the flare's ring and disc), and the stars' scale of 0.19, which `Skyobjects::Init` sets in place of the table's 0.2. Facts (`Skies.SunImage`, `MoonImages` and so on), on the sky record.
  - Each terrain type's sky, and whether it's drawn: a switch in `BorgMaps::GetMapTerrain`. A fact (`Skies.ForTerrain`), on the terrain records.
  - How the colours come from a gradient: the constants in `Sky::SetColors` (0.65, 0.15 and 0.2), and the change of sky over 600 ticks. Engine behaviour (layer 3), in `HBHT.Core.Sky`.
  - A new game's date and time, noon on day 2 of week 6, and the 72 in-game ticks per tick (`InitGameGlobals`, `World::init`). Facts (`GameClock`).
  - A new game's party: Alaron alone, with 1 gold (`init_DBs`). Facts, on the new game record (`aidyn:newgame/0`).
- **Combat visuals** (the arrow's model is the missiles', above): the thrown-potion models, target and spell markers, and the stat indicator digits (`combatEntity.cpp`, `CombatMarkers.cpp`, `SpellMarker.cpp`, `StatIndicator.cpp`).
- **Bodies' shadows**: the texture, Borg1 `0x0003` (`BORG1_PlayerShadow`, which `InitPlayerHandler` is given), and how far from the camera's aim they're drawn: 20 in trek, 128 in combat and none on the title screen (`InitZoneEngine`). A fact: the runtime draws `aidyn:texture/0x0003`, and the core holds the distances (`BodyShadow`).
- **The particle textures**: 17 slots of Borg1s 0–12, some empty (`partTextInds`, `ZoneEngine.cpp`, `0xCE460`). ROM (`ParticleTextures`), resolved into each emitter's texture on its `aidyn:emitters` record, and for code's emitters by the slot their code names: the weather's on the weather record, a trapped container's explosion's (slot 2, a literal of `Explosion.MakeFire` and `Explosion.Burst`) on `aidyn:explosion/0`. The explosion's fires, burst and lights are engine behaviour (layer 3) in `HBHT.Core.Particles.Explosion` ([map objects](systems/map-objects.md#containers)).
- **Oriana's path lights**: a scene object whose second model is `BORG5_OrianaLight` (`0x374A`, compared as a literal in `RenderVoxelScenes`, `0x8000cda4`) goes out. A fact: the importer marks the 23 objects (`SceneObject.GoesOut`, [map-objects](systems/map-objects.md#orianas-path-lights)).
- **Coin, chime and level-up sounds** in dialogue, shops and training (the menus' sounds, above).

**Fonts** are records (layer 1), since each has a layout: its sheet, grid and glyph widths, the character map and the buttons' colours (`font_face`, `struct_unk_.textIndexies`, `extraFontColors`). ROM (`Fonts`), as `aidyn:font/0` and `/1` ([text](formats/text.md)).

**Portraits:** the fallback for a speaker with none, `BORG8_PortraitNPCMale1` (`0x0125`), and Alaron's entity, whose portrait shows over a conversation's choices when the line before them was the other speaker's alone (`DialougeClass::Setup`). Facts held by the runtime's conversation box.

**UI chrome** is the rest: the conversation box's portrait border (`BORG8_PortraitHighlight`), the translucent band and feathering of popups and the conversation box (`BORG8_Pixel`, a texel of alpha 136; the gradients `0x1C0`–`0x1C3` and the corners `0x1BC`–`0x1BF`; the image above a box is `0x1C3`, below `0x1C2`, left `0x1C0` and right `0x1C1`), the overscan bars' sizes (`Graphics::EndList`), the compass, menu backgrounds and titles, the calendar and sundial, logos and Controller Pak screens. These need no records. Mods override them as assets under their Borg IDs ([ADR 0004](adr/0004-record-ids.md), [ADR 0005](adr/0005-plugin-format.md)).

**Debug only:** the boulder that replaces scene models, and the debug menu's models.

## Corrections to the ROM's maps

Where the ROM's own data is in the way of play, the importer corrects it, each the user's choice and a departure from the original (ADR 0018). Each is the player's choice too, at import, on by default (`ImportOptions`, which the base plugin's manifest records as `import.options`; [importer](systems/importer.md#the-players-choices)). `MapCorrections` makes them on the parsed Borg9, before the map record and its collision are written, so a model and the triangles that are its body move together. Each names its map set, chunk and object, and the importer refuses a ROM where they aren't what it corrects (the three ROMs agree). They land in layer 1: a mod can put one back by patching the object's position and replacing the map's collision asset.

- **The Roog Cave's mushroom** (`aidyn:voxel/0x1205/12`, scene `0x0BCB`), in D01 at (11.23, −0.11, 22.54), stands in the neck of the passage between two leaning rocks. Its collision is a box of 8 of the chunk's triangles (926–933), 0.71 square and 1.07 tall, about twice the height of its small model. The box all but touches the rock east of it, so the way past is west of it, against the corner of the other rock at (10.34, 22.29). There, the best way past brings the middle of Alaron's sphere within 0.32 of a wall, where his radius is 0.5. Walking or sneaking north to south, he wedges there from every start; northward, and running, he passes. On the console, players squeeze past or don't, or resort to tricks. The user chose to keep the collision as the original has it and move the mushroom instead (Oct 24, 2026). It's slid by (−0.124, 0, 0.696), 0.71 in all: 0.1 toward the rock east of it, leaving its model 5 cm in front of the rock's face, and 0.7 north along that face, past the corner opposite. The best way past then keeps him 0.43 from the walls, and he walks, runs or sneaks past either way (`GameSessionTests.Mushroom.cs`). The floor is flat there, so the mushroom stays on the ground. The player can keep it where the ROM has it (`ImportOptions.FixMushroom` off: the editor's import page's **Don't fix the mushroom**, or `hbht import --keep-mushroom`). The page's words for that choice, the user's, say it's still possible to get by, but more difficult than in the original.

## Still in code

Content the port's core and runtime still hold as literals, with the layer each belongs in. The rest of the core's literals are engine vocabulary (the animation request numbers, 5 hit, 7 dodge, 8 death and so on, which every actor's state machine shares), text-line indices, and the menus' 320 × 240 layouts and colours, which are the UI's own (layer 3) and stay in code.

- **The backdrop pictures' styles** (`BackdropStyle.For`): a script's picture number 1–4 and 9–12 picks a Borg8 (`0x3602`–`0x3608`) and the page's text, pulse and shadow colours. Layer 1, a record per picture.
- **The protagonist's entity as a code constant** (`GameState.Alaron`, `aidyn:entity/0x023F`), read in about ten places: the leader fallback, the rename and save-name menus, the shadow's player test in the combatant and cut-scene views, the conversation's speaker and trek's sounds. The new game record already carries the leader (`NewGameRecord.Leader`); each use should ask the party's leader or that record. Layer 1.
- **A chest byte's loot table** (`Battle.Rewards.ChestOf`): loot record `0x7FF` + the entity's chest byte. The importer could write each entity's chest as the loot record's ID instead. Layer 1.
- **The skill checks' numbers** (`Party.SkillCheck`, `Crafting`): Diplomat's pool of 10 and 50 with a second roll under 3 giving 2, Loremaster's 6, Mechanic's (skill × 5 + Int + Dex) × 2 and the lock's `max(5 − mechanic, 1)`, Healer's Int × 3 + skill × 10, Ranger's d100 against skill × 10 + 80. Rules (layer 3); the numbers could sit on the skill records.
- **Every dialogue entity's perception is 10** (`Wanderers.DialoguePerception`, `EntityDB::GetPerception`). A user or rig check: if the original reads it per entity, it's a record field (layer 1).
- **The combat menu's action icons** (`CombatMenu`: HUD sprites `0x0185` Healer, `0x018C` Perform, `0x35E5` the potion) and the pause menu's scroll actor in the runtime (`PauseScrollView`, `aidyn:actor/0x0E6B`). UI chrome, with the rest the [assets](#assets-engine-code-names) section's last paragraph lists.

## Findings

- **Code tables refer to entities by list position, not ID.** A 221-byte `entityList` in the code maps each position to an entity. The party tables use these positions, and so does the boss switch, which is a jump table over positions 168–186.
- **Map 13's region flag is on Cradawgh's Body.** Gaining Map 13 reveals nothing.
- **Two world map entries can't apply.** They name overworld chunks G18 and R29, which don't exist. The table names chunks by row and column alone, with no map set, so Ugarit's M06, outdoors, sets region 1 (788) too, which the party has had since the game's first chunk.
- **Most map items are never given.** Only Map 2 (in a container) and Map 7 (sold by shop 17, STTA05 in Talewok) have sources. No script, container, monster party, loot table or shop gives Map 5; its region flag (791) is set on entering Talewok's chunks, which shows the same piece of the world map. The Skull Key and the Map to the Goblin Lair have no source either; they're possibly cut content. The Blood Key is the only key item given as a monster party's direct loot.
- **Talking monster parties fight under flag C.** When a talking party's dialogue starts a battle, the battle's encounter flag is the party's flag C, and winning sets it. Most such parties have C equal to B. One names derived flag 5264, so the write does nothing.
- **Script battles don't set their flag on a win.** Only monster-party battles set the encounter flag on a win. The Goblin Ambush's flag, 978, is set when the party *loses*, and every special rule of that fight keys on it.
- **A script's battle can pay out the last monster party's bonus loot again.** The setup's bonus loot (`0x800D9470`) is written by a monster party's battle and a camp ambush (as none), never cleared, and not written by a script's battle, which then rolls it again on a win. Of the 38 scripts that start a battle, the 18 cut-scenes' fight their own enemies and so can; the 20 conversations' fight the talker's monster party, which writes its own. 21 monster parties carry bonus loot, among them the Minotaur Lord's Blood Key, Lugash's Spellbreaker Axe and the Marquis' Amulet. The port gives a script's battle none, the user's choice.
- **The ending cinematic has six shots,** each with its own Borg6 animation and Borg12, matching its six clip planes and flag entries.
- **Each cinematic shot names its own caption list.** The flag list (`SeqenceFlags`) holds a caption list per shot (or none), then the done flag. Each of the 15 caption lists shows over exactly one shot.
- **Only the first death scene plays.** All three have dialogue objects in the Dreamscape, but the game over code only looks for 1298.
- **Containers with their own model are gated like herb sources.** The code gates container kinds 4–7 by flags A and C: herbs, spices, gems and loot with its own model. 46 of the 49 kind-7 containers set a flag C, including the cheats' Hockey Stick and Lightreaver, Kendall's Hat and an Amaranth.
- **Bosses count only in the first four slots.** A monster party fills slots with each entry's minimum count, in order, then random extras, so a boss listed after four guaranteed enemies wouldn't set its flag. In the data, only three monster parties include a boss (Kitarak, and Lugash twice), each listed first. Scripts add the other bosses.
- **Secret doors.** Flags 955–958 and 960–963, which no data sets, are the secret doors in Ehud's underground (EhudUnder C11 and E11). A teleporter is secret whenever its secret flag isn't 0 (its secret level is 0 in all the data); the code sets the flag on first use. See [map-objects](systems/map-objects.md#teleporters).
