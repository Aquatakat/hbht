# Code tables

The content 1.0 keeps in its code segment rather than its data, and how the importer carries it. A **table** is read from the ROM: a `CodeTable` (`RomTables.cs`) at the offset `RomVersions` gives for the version ([rom](rom.md#where-each-version-has-its-tables)), through `RomImage.Table`, by a reader in `HBHT.Rom.Engine` (the flag tables by `EngineTables` in `HBHT.Rom.Quests`). A **fact** is what the code holds as a switch or a literal: the importer carries it, checked against 1.0's code, and refuses a ROM where it doesn't hold ([importer](../systems/importer.md)). How each behaves is its system's doc; what the port still holds in code is [hardcoded content](../hardcoded-content.md).

Offsets are NA 1.0's in the ROM. The code segment is ROM `0x1000` loaded at `0x80000400`, so RAM is ROM + `0x7FFFF400`. Every flag engine code names carries its label on its flag record (`EngineFlags`), which the editor shows.

## Position lists

Code tables name entities, dialogue entities, spells, armor, weapons and items by their **position** in a list, not by ID. Each list is its DB table's own order: place p holds the ID of the DB's p-th record (its low byte for all but the items'), so a table keyed by position is keyed by the DB's record. `PositionLists` checks each against its DB before a position is used. They add no content and aren't imported.

| List | 1.0 | Length |
| --- | --- | --- |
| `EntityList` (`entityList`) | `0xE3440` | 221 |
| `DialogEntityList` (`DialougEntList`) | `0xE1900` | 37 |
| `SpellList` | `0xE3740` | 59 |
| `ArmorList` | `0xE1850` | 68 |
| `WeaponList` (`weaponList`) | `0xE3790` | 105 |
| `ItemList` (`itemID_array`) | `0xE3590` | 197 u16s |

## Flags

| What | 1.0 | Read by | Record |
| --- | --- | --- | --- |
| Party members' joined, left and died flags | `PartyFlags` `0xD42C4`, `PartyDeathFlags` `0xD4318` (`Party::CheckFlags`) | `EngineTables` | entity `Member` |
| Key items: 31 with an inventory slot, a "has" flag and maybe a world map region flag | `KeyItems` `0xE39F0`, `KeyItemFlags` `0xE39B0`, `KeyItemRegionFlags` `0xE3A30` (`PartyInventory.cpp`; `LoadKeyItems` restores them from the flags) | `EngineTables` | item `KeyItem` |
| World map regions: entering a chunk sets its region's flag, outdoors in a map set with no minimap | `RegionFlags` `0xD1E6C` (`set_map_event_flag`, `ZoneEngine.EnterRegion` `0x8004960c`) | `EngineTables` | chunk `Region` |
| World map pieces, each shown while its region flag is set | `WorldMapPieces` `0xDFC40` | `EngineTables` | `aidyn:worldmap/0` |
| The world map's towns' names | `WorldMapTowns` `0xD1720` | `WorldMapTowns` | `aidyn:worldmap/0` |
| A script's battle sets its flag when won: the Goblin Ambush's and Lugash's scripts | `ScriptBattleFlags` `0xD2A80` (`dialoug_func`) | `EngineTables` | script `BattleFlag` |
| Boss flags: winning with a boss in the enemy list's first four | `Combat.SetKillFlags` (`set_boss_flag`), jump table `0x800D2B60` by entity list position | Fact | entity `combat.bossFlag` |
| Sholeh's died flag (622) whenever she dies in battle, as an ally too | `Combatant.Death` `0x80062080` | Fact | entity `Member`, `combat.fightsForParty` |
| Cinematics' done flags (3, 569, 590, 601) | `Cinematics` `0xE1380` (`Cinematic::EndCase`) | `Cinematics` | cinematic |
| Alaron's True Name while 590 is set: entity DB position 153 | `CharExp::Init` (`entityInfo.cpp`) | Fact | entity `trueNameFlag` |
| First spell learned (3611), first potion bought (3612), and their help (the Common string table's lines 497 and 521) | `WidgetSpellTrain`, `WidgetBarterControl` | Fact | triggers 9 and 10 ([events](../systems/events.md#the-base-plugins)) |
| New journal entry waiting (3617), and the derived flags that add journal entries (5556 on, each its entry's flag counted above 0) | `set_journalentry_flag`, `GameStateFunnel.Write` `0x800cb33c` | Fact | state flags; each entry's own flag's `journal` (the 71 derived flags aren't imported) |
| Calendar, weather, terrain and best-party-stat flags (the stat and skill ones are never written: their setters' lookup always fails) | `gameStateFunnel.cpp` | Fact | state flags |
| Camp dialogue check (4240, set while it runs) | `Camp.cpp` | Fact | camping `Checking` |
| Camp scenes: the first script object in the player's chunk whose flag A is one of eight, and holds, however far | `CampDialogueFlags` `0xD85F0` (`Camp_CheckDialougFlags`) | `EngineTables` | camping `Scenes` |
| Camp ambushes only while derived 5004 holds (`FLAG_Gwernia2MembersNoBrenna`: two of Abrecan, Godric and Rheda joined) | `Party::CampCheck` | Fact | camping `Ambushes` |
| The lighthouse chunks' lit scenes while 5017 holds: the dark scenes tested, the flag and the lit scenes, as immediates | `CheckToggleZoneScene` `0x8000aa14`, from `0x8000aa3c` | `ToggledScenes` | map set chunks' `swap` ([maps](maps.md)) |
| Minimap pieces by flag: the Ehud underground (4222–4232), the Roog entrance (4233–4237), the Barrows (4286–4294) | `MinimapFlagImages` `0xE07C8` (`minimap_dats_2`); which Barrows piece each shows, `MiniMap::BarrowsFlags` `0x8004bfac` | `Minimaps`, the Barrows' as facts (`MinimapImporter`) | a map set's `minimap` |
| The Theatre lists a cinematic once its title's flag is set | `TheatreMenu.cpp` | DB block | cinematic |
| Flag 5626, read after every map object's flag write, its value unused ([map objects](../systems/map-objects.md#map-objects-write-flags)) | `objectJumpTables.cpp` | Fact | none |
| Party skill checks in dialogue roll 0–21 while 3717 is set | `DialogEvalCallback` | Fact | state flags |
| Cheat flags (3673, 3675, 3693, 3694) | `CheatCodes.cpp` | Fact (labels only) | none: the cheats aren't ported ([hardcoded content](../hardcoded-content.md#cheats)) |

## Scripts and cinematics

Which events the base plugin's triggers carry, with the call sites, is [events](../systems/events.md#the-base-plugins).

| What | 1.0 | Read by | Record |
| --- | --- | --- | --- |
| The four cinematics: each shot's Borg6, Borg12, caption list and clip planes, the fades, the done flag | `Cinematics` `0xE1380`, `CinematicFades` `0xE1598` (pointer tables) | `Cinematics` | `aidyn:cinematic/0`–`3` |
| What starts and follows each cinematic | `Cinematic.Load`'s callers, `Cinematic.EndCase` `0x80053c20` | Fact (`TriggerImporter`) | triggers; the Shadow's is combat `shadow` |
| Scripts whose end leads somewhere: the death scenes, 1268 (Roog's), 1278 (the ending) | `ScriptEndings` `0xE0A1C` (`borg_short_list`, `CutScene.Free`) | `EngineTables` | triggers on `script.ended` |
| Scripts after which eight hours pass: the eight camp scenes and the Campsite's | `CampfireScenes` `0xD8604` (`isCampfireScene`) | `EngineTables` | script `Rest` |
| The death scenes; only the first, 1298, ever starts, from its object in the Dreamscape's Borg9 (3423) | `GameOverScenes` `0xCEDE0` (`Gameover_func`) | `EngineTables`, `CombatImporter` | combat `GameOver` |
| Camping with no camp scene near starts the Campsite Borg9's (6236) first script object | `campfire_func` | Fact | camping `Campsite` |
| A new game: Overworld I08 at (16, 62), the body made 10 up and set down, Alaron alone with 1 gold | `Title.Leave` `0x80055718`, `Zones.SetMapSet` (`0x80026374`, `0x800ce7d8`), `InitGameTrek` `0x80021018`, `init_DBs` | Fact (`WorldImporter.BuildNewGame`) | `aidyn:newgame/0` |
| The help menu is script 1355 | `gametrek.cpp` | Fact (`EngineFlags.HelpScript`) | none: `SystemMenu.HelpScript` ([hardcoded content](../hardcoded-content.md#trek-and-the-world)) |

## Combat

[combat](../systems/combat.md) has how each is used; the bespoke fights are in [hardcoded content](../hardcoded-content.md#bespoke-behaviours).

| What | 1.0 | Read by | Record |
| --- | --- | --- | --- |
| Fleeing: a boss on the field forbids it (Kitarak, the Marquis, Pochanargat, Shamsuk, Sheridan, Ksathra, Shatrevar, Mehrdad, Golnar, Nasim, Behrooz, Assim), the Shadow allows it, the last on the field having the say; then 978 forbids it and 5250 (Lugash) and 3749 (the Marquis) allow it | `Combat.ForbidFleeForBosses` `0x8005CCCC`, `Combat.ApplyFleeRuleByFlag` (ROM `0xD3288`, `0xD32A0`) | Fact, by entity list position | entity `combat.flee`; `battleRules.flee` on the script, npc or encounter that starts the battle ([combat](../systems/combat.md#a-battles-own-rules)) |
| Sholeh, among an enemy list's first four, fights on the party's side | `Combat.FindSholeh` `0x8005C6C0`, position `0xA4` | Fact | entity `combat.fightsForParty` |
| Bystanders dropped from an enemy list | `Combat.IsExcludedEnemy` `0x8005C0C0`: positions 0–`0x56`, `0x82`–`0x90`, `0x92`–`0x96` | Fact | entity `combat.bystander` |
| An npc or encounter rolling none fights one Skeleton | `BattleSetup.FillEnemies` `0x80009374`, position `0xBB` | Fact | combat `fallback` |
| Pochanargat never moves: his field is 0 | `Combatant.UpdateRadius` `0x8005F29C`, position `0xAB` (`0x800E28EB`) | Fact | entity `combat.rooted` |
| Enemies with spells that use the boss style: 13, Lugash and the Wight among them | `EnemyAI.HasBossStyle` `0x8005BA40` (`CombatAI::IsBoss`), jump table `0x800D24B0` | Fact | entity `combat.bossStyle` |
| Bows (positions 53–60, flying as the arrow, Borg5 `0x348F`) and thrown weapons (94–99, 101, 102, flying as themselves), each with a muzzle speed and a turn (the Javelin's 90) | `BowKindWeapons` `0xE1808`, `ThrownKindWeapons` `0xE1814`; muzzle floats `0x800D2AE0`–`0x800D2B44` (`WeaponMuzzle` `0x80066DD0`) | `CombatImporter`; the numbers facts, checked by weapon name | item `weapon.missile` |
| Spikes (position 100), the one ranged weapon that may also strike in melee | `Weapon.AllowsMelee` `0x8007B730` | Fact | item `weapon.strikesInMelee` |
| Entities whose weapon isn't drawn in the hand: Shamsuk, the Gorgon, the Chaos Lieutenant, the Harpy, the Marquis, the Minotaur, the Minotaur Lord, Niesen | `Combatant.HasNoWeaponAttachment` `0x80066C5C`, positions at `0x800D2A0C` | Fact | entity `body.weaponsInModel` |
| Entities holding a bow at locator 0 and its arrow at 1: the bandit bosses and woodsmen, Becan, Keelin | `EntityBody.Hand` `0x8006C8BC`: positions `0x57`–`0x59`, `0x5C`–`0x5F`, `0x6C`, `0x7A`, `0x9C`, `0xA1` | Fact | entity `body.bowHand` |
| The arena by the starting chunk's terrain | `BattleSetup.ChooseArena` `0x80008698`, jump table `0x800CD400` | Fact (`Arenas`) | terrain `arenas` |
| The flask held's danger (Inferno 7, Fire 5, Sleep Gas or Acid 4) | `Combat.PartyPotionTier` `0x8005C750` | Fact | item `aiSpread` |
| The area spell the AI centres on its opponents (Fireball, kind 8) | `EnemyAI.AreaCentre`, `EnemyAI.AimAreaSpell` | Fact, checked at kind 8 | spell `targeting.blast` ([ADR 0021](../adr/0021-instant-effects-land-in-one-place.md)) |
| The combat menu's C strings: the skill rows (Lay On Hands, Heal With Herbs, Perform) and the empty lists' words | `CombatLayOnHands` `0xD5480`, `CombatHealWithHerbs` `0xD5490`, `CombatPerform` `0xD54A0`, `CombatNoSpells` `0xD5458`, `CombatNoItems` `0xD5434` | `CombatImporter.ReadText` | combat `text` |
| Trek's notice of a new journal entry, "A new journal entry has been added." (`NewJournalEntryPopup`, `0x80020eb0`) | `JournalNotice` `0xCEE8C` | `TriggerImporter` | trigger 11's `message.show` ([events](../systems/events.md#the-base-plugins)) |
| Sense Aura's lines (some printf formats): follower, leader, hit points, level, armor, and a resistance's Immune at 0 or less, Resistant below 2.0, Vulnerable from 2.0; the element name is Common's line 241 + the element byte (`Entity.ElementNames`, `0x800e0c00`, set at `0x80083620`) | `AuraFollower` `0xD3E50` to `AuraProtection` `0xD3E84`, `AuraImmune` `0xD3E14` to `AuraVulnerable` `0xD3E34`, the float `0xD3E48` (`Entity.SenseAura` `0x80070070`, `Entity.AppendResistanceLine` `0x8006FFB0`) | `CombatImporter.ReadText` | combat `text` |
| The Goblin Ambush's and the Shadow's rules | [hardcoded content](../hardcoded-content.md#bespoke-behaviours) | Facts (`CombatImporter`) | `battleRules` on script `0x0365` and its npcs; combat `shadow` |

## Characters

| What | 1.0 | Read by | Record |
| --- | --- | --- | --- |
| The experience each level starts at, 0–40 | `LevelExp` `0xE3804` (`EXP_TNL`) | `CharacterTables` | progression `Levels` |
| Each stat's base cap and cap | `StatCaps` `0xE3BE4` | `CharacterTables` | stat |
| Each stat's training cost (the Level's, 500, unused) | `StatTrainingCosts` `0xD4620` | `CharacterTables` | stat |
| Each skill's training cost | `SkillTrainingCosts` `0xD44D0` | `CharacterTables` | skill |
| Each weapon class's training cost | `WeaponTrainingCosts` `0xD4500` | `CharacterTables` | weapon skill |
| The shield's training cost (500) and name ("Shield") | `GetShieldXpPrice`, `WidgetSkillInfo` | Fact | `aidyn:weaponskill/11` |
| Skills' caps, 10 and 15 | `SKILLMAXBASE`, `SKILLMAXMOD` | Fact | skill |
| An award of experience × 1.5; an entity's reward byte × 50 | `Entity::AddExp`, `EntityDB::Load` | Fact | progression `ExpAward`, entity |
| Trainers: 200 gold from level 0, (n + 1) × 40 for level n, 80% of the experience | `GetSkillTrainGoldPrice`, `WidgetSkillTrain::Confirm` | Fact | progression `Training` |
| Spells stop at level 10, and cost their byte × 50 per level squared | `TempSpell::IsMaxRank`, `TempSpell::Init` | Fact | progression, spell |
| Oriana trains for no gold: entity list position 144. No other trainer is special-cased; a trainer teaches from its own entity record (`init_skill_trainer`) | `shopkeepNotOriana` | Fact | progression `Training.FreeTrainers` |
| Members whose sheets are kept when they leave: Abrecan, Arturo, Becan, Godric, Keelin | `SaveEntity::GetSlot`, jump table `0x800CDD88` | Fact (`CharacterImporter.KeptMemberPositions`) | entity `Member.Kept` |
| The Archmage Staff stays with a member who leaves, as when taking it off | `Party::StripMember`, `RemoveWeaponsFrom` | Fact | weapon `Fixed` |

## Magic

| What | 1.0 | Read by | Record |
| --- | --- | --- | --- |
| What each spell and potion does: the switches by kind, the dispel, charm and immobilise lists, whom charms and Banishing take, Restore's and Wraith Touch's lists, the damage spells' elements, the AI's value of each | `Entity.AddEffect`, `Combatant.ApplySpellEffect`, `Combatant.DrinkPotion`, `EnemyAI.SpellValue`, `0x800D29BC`–`0x800D34B4` | Facts | spell and potion `effects` ([magic](../systems/magic.md), [ADR 0020](../adr/0020-one-effect-vocabulary.md)) |
| Casting's floats: a Lunar caster's chance by moon phase, anyone else's by part of day, the improvement factor, the resist roll's aspect factors | `CastingFactors` `0xD35CC` | `CastingFactors` | `aidyn:magic/0` |
| Which potions are thrown and where the rest are drunk: kinds 0–3 thrown, never drunk in trek; C-up leaves out 16, Stealth | `CombatMenu.ListPotions` `0x80088190`, `Entity::CanUsePotion` `0x8006FBE8` | Fact | potion `use` ([ADR 0021](../adr/0021-instant-effects-land-in-one-place.md)) |
| Spell and potion visuals: six switches on the kind (a spell's caster actor, target actor, whether it stands at the head, aura scene; a potion's actor and aura scene), each actor with the memory it asks, which the port doesn't import; the refused and resisted casts' actors (`0x1CAA`, `0x37F1`). The build's addresses are in them, so they differ by version | functions `0x8D740`–`0x8DDDC`, jump tables `0xD5898`, the immediates `0x8C4D8` (`combatspellvisuals.cpp`, `CombatSpellFx.AddVisual`) | `SpellVisualTables` | spell and potion `Visuals`, magic `Refused`, `Resisted` |
| Each spell's icon symbol | `SpellSymbols` `0xD3C58` (`SpellDB::GetIcon`) | `MenuTables` | spell `Icon` |
| Each spell's cast sound (59) | `SpellSounds` `0xE10A0` | `BodySoundTables` | spell `presentation.sound` |

## Items and the menus

| What | 1.0 | Read by | Record |
| --- | --- | --- | --- |
| Weapons' and items' models (Borg5), by position | `WeaponsWithModels` `0xE3520`, `WeaponModels` `0xD3B14`; `ItemsWithModels` `0xE3564`, `ItemModels` `0xD3B9C` | `ItemTables` | `Model` |
| Shields' models | `ArmorDB::GetBorg5`, a switch | Fact (`ItemTables.ShieldModels`) | armor `Model` |
| Icons: items' (286 by ID, the rest a default, every potion one), skills', aspects', schools', spell symbols', each list followed by its count | `MenuIcons` `0xDF700` (`menuimages.cpp`) | `ItemTables` | item, skill and spell records; aspects' and schools' are UI chrome (`MenuImages`) |
| Potions' prices | `PotionPrices` `0xE38B0` | `ItemTables` | potion item |
| The Alchemist's 15 and the Mechanic's 5 recipes | `PotionRecipes` `0xD8560`, `ArmorRecipes` `0xD8540` | `ItemTables` | recipe |
| The 80 items with their own inventory slots, the 13 unique pieces of gear with theirs, and the 21 items loot can't be left behind with | `ReservedItems` `0xE3910`, `KeyEquipment` `0xE3A70`, `MustTakeItems` `0xE38E0` | `ItemTables` | item traits |
| Each item's, potion's and spell's description line (two potions, `0x100B` and `0x100C`, and four spells, `0x030F`, `0x0311`, `0x0327`, `0x0328`, have none) | `ItemDescriptions` `0xD03A0`, `PotionDescriptions` `0xD0360`, `SpellDescriptions` `0xD04EC` (`WidgetItemDetail`) | `MenuTables` | `Description` |
| The inventory's order of item kinds | `InventoryOrder` `0xDF600` (`D_800ed560`) | `MenuTables` | none: `InventoryOrder.KindOrder` holds it as UI |
| Each equipment slot's empty image (15) | `EquipmentSlotImages` `0xDF670` (`gItemSlotBorg8Indecies`) | `MenuTables` | none: UI chrome (`MenuImages`) |
| Three C-string messages: a potion that would do nothing now, a flask or potion with no effect outside combat, the Lighthouse scroll (no spell) | `PotionNotNow` `0xD3D50`, `PotionNotInTrek` `0xD3D78`, `ScrollNoSpell` `0xD435C` | `MenuTables` | inventory `Messages` |
| The credits' cards: the first line of each in the credits' string table, the first a heading; 12 show one at a time before the rest scroll (the card ending at line `0x1D`) | `CreditsCards` `0xCFC60` (`Credits.CardStarts`, `Credits.StepCards`) | `CreditsTable`; the 12 a fact | `aidyn:credits/0` |
| Fonts: sheet, grid, glyph cells and the buttons' colours | `FontFaces` `0xDAF08`, `FontGlyphs` `0xE56C0`, `FontColors` `0xE572C` | `Fonts` | `aidyn:font/0`, `/1` ([text](text.md)) |

## Audio

[audio](../systems/audio.md) has how each plays.

| What | 1.0 | Read by | Record |
| --- | --- | --- | --- |
| The 39 Borg12s a script's `PlaySound` names | `ScriptSounds` `0xE0960` (`dialoug_SFX`, `Dialogue.SoundTable`) | `AudioTables` | sounds `Script` |
| Containers' sounds: eight by kind, four for a trapped one exploding | `ContainerSounds` `0xCE5D0` (`MapObjectUse.ContainerSounds`) | `AudioTables` | sounds `Containers`, `Explosions` |
| Footsteps: 49 entries of four step ids, the sets by body kind and surface (26 × sneak, walk, run), the 96 Borg12s step ids name | `FootstepEntries` `0xE0A60`, `StepSets` `0xE0BE8`, `StepSounds` `0xE0D28` (`audiokey.cpp`) | `BodySoundTables` | sounds `Kinds` |
| The 126 Borg12s voices and weapons name, and each weapon model's entry | `VoiceSounds` `0xE0EA8`, `WeaponSounds` `0xD50D0` | `BodySoundTables` | entity `Sounds` (with the DB block's audio keys), a former dialogue entity's `Sounds` (the profile at its place in `DialogEntityList`), weapon `Sounds` |
| Each map set's music: literals for Gwernia castle, Erromon, the Jundar interiors, Ehud's underground, Ugarit and Chaos Isle (two by the chunk's column); the Overworld's two chunk lists by scene (with and without the Pak); the Jundar interiors' list; the battle music, one of five at random with the Pak or four without | `Zones.SetMapSet`; `OverworldMusicScenes` `0xCF31C`, `JundarMusicScenes` `0xCF368`, `BattleMusic` `0xCF378` | `AudioTables`; the literals facts (`AudioFacts`) | map set and chunk `Music` ([audio](../systems/audio.md#music)) |
| The title's music `0x0632` (`0x05D9` without the Pak), the credits' `0x05D9`, rain `0x071B`, thunder `0x071C`–`0x071F` | literals | Fact (`AudioFacts`) | `aidyn:sounds/0` |
| The menus' sounds: cursor `0x073B`, `0x073D`; A `0x0740`; B `0x0741`; tabs `0x074D`, `0x074E`; changing member `0x0739`; level-up page `0x05E8`; cheat code `0x0718`, `0x0721`; coins `0x072D`, `0x072E`; experience `0x05E7` | literals in the menus and `Dialogue.ExecuteCommand` | Fact (`AudioFacts`) | sounds `Menu` |

`Dialogue.SoundTable`, `AudioKeys.*`, `Audio.*` and `MapObjectUse.ContainerSounds` are the symbol map's names for 1.0's tables; the port reads them as `CodeTable`s of its own names.

## The world

| What | 1.0 | Read by | Record |
| --- | --- | --- | --- |
| Each map set's chunk list | | `MapChunks` | map set |
| Each map set's chunk spacing | `BorgMaps::LoadMap`, literals | Fact (`MapSets.CellSize`) | map set `CellSize` |
| Each map set's spawn points by portal (below) | `Zones.SetMapSet` `0x80026264` | Fact (`MapSets.Spawns`) | map set `Spawns`; `aidyn:newgame/0` takes the Overworld's |
| Camp ambush factor per terrain; 0 refuses camping | `CampFactors` `0xE3B38` (`camp_float_array`) | `CampFactors` | terrain `Camp` |
| Who ambushes a camp, by terrain and tier of the party's average level | DB block `0x1FEDE50` ([db](db.md), `CampEncounters.Load` `0x800096DC`); the tiers literals | DB; the tiers a fact | terrain `Ambushes`, camping `AmbushLevels` |
| Precipitation chance per season (6, then 2 bytes of padding) and per terrain (28), and its scale per terrain | `SeasonPrecipitation` `0xE3C40`, `TerrainPrecipitation` `0xE3C48`, `TerrainPrecipitationScale` `0xE3C64` | `WeatherTables` | weather `Seasons`; terrain `Precipitation`, `PrecipitationScale` |
| Snow instead of rain on terrain 5 only | `Weather.Roll` `0x8007bdb0` | Fact | terrain `Snow` |
| When the weather changes and to what, and the wind's wander | `Weather.Roll`, `World.Update`, `Weather.DriftWind`, `ProcessWeather` | Facts | `aidyn:weather/0` ([weather](../systems/weather.md)) |
| Rain's and snow's texture slots (rain 4 and 16, snow 0), colours, fall, spawn intervals and lives, and whether rain splashes of itself | `ProcessWeather`, `WeatherParticles.Make`, `.SpawnDrop` (`0x8001e04c`–`0x8001e7c0`); colours `0x800ce0d0`, `0x800ce0e0` | Facts | weather `Rain`, `Snow` ([weather](../systems/weather.md#rain-and-snow)) |
| The eight sky gradients | `SkyGradients` `0xCEB50` (`sky_borg8s`) | `Skies.ReadGradients` | sky |
| The sun, moon and 91 stars, 20 bytes each, ending with a type 3 entry | `SkyObjects` `0xDEE48` (`skyobjectStructs`) | `Skies.ReadSkyObjects` | sky |
| The clouds, 28 bytes each, 55 and an end | `Clouds` `0xDE808` (`gCloudData`) | `Skies.ReadClouds` | sky |
| The lens flare's eight pieces | `LensFlare` `0xDE770` (`gLensFlareData`) | `Skies.ReadLensFlare` | sky |
| The sky's images: the sun, star, six moon phases, three clouds, the flare's ring and disc; the stars' scale of 0.19, which `Skyobjects::Init` sets over the table's 0.2 | `skyObjects.cpp`, `Clouds.cpp`, `Lensflare.cpp`, literals | Facts (`Skies.SunImage`, `MoonImages`, …) | sky |
| Each terrain's sky, and whether it's drawn | `BorgMaps::GetMapTerrain`, a switch | Fact (`Skies.ForTerrain`) | terrain `Sky`, `SkyDrawn` |
| Particle textures: 17 slots of Borg1s 0–12, some empty | `ParticleTextures` `0xCE460` (`partTextInds`) | `ParticleTextures` | each emitter's texture (`aidyn:emitters`); the weather's on the weather record; a trapped container's explosion's (slot 2, a literal of `Explosion.MakeFire` and `Explosion.Burst`) on `aidyn:explosion/0` |
| Each entity's model (Borg7), portrait (Borg8), height, collision radius, scale and strike share, by entity list position + 1; the code's fallback to the bat and a generic portrait never runs | `EntityModels` `0xE1930` (`gEntityExtras`) | `EntityModels` | entity `Body` |
| A body's alpha in combat: 127 (translucent, body and shadow) for entity list places `0xAF`–`0xB5` (Ksathra, Shatrevar, Mehrdad, Golnar, Nasim, Behrooz, Assim) and `0xC0` (the Spirit Wolf), 255 for every other. Immediates in the function, not a table | `EntityBody.ShadowAlpha` `0x8006C798`, called only by `Combat.CreateBody` `0x8005C474` | Fact (`EntityModels.AlphaOf`) | entity `Body.Alpha` |
| Each dialogue entity's model, portrait, height and scale (32 of 37 have one) | `DialogEntityModels` `0xE3178` (`dailougEnt_info_array`) | `EntityModels` | entity with no sheet |
| The player's model, Alaron's actor `0x25F9`, which three cheats swap | `0x800557c0` into `Player.Actor` `0x800d8134` | none: the port gives the player the leader's actor (`EntityBody.Actor`, `GameSession.PlayerActor`) | entity `Body` |
| Alaron's head, scene `0x25AA` (`some_borg5`), drawn at locator 3 of his body, turned toward what the player is near, up to 80° | `Actor::ChangeAppearance`, the player renderer | Fact | actor `0x25F9`'s `Head` |
| The nine actors `ProcessPlayers` animates in combat while idle 13.0 or more from the camera's aim: the bat, wyvern, harpy, two dryads, Pandara, gryphon, manticore and scorpion (the decomp's flying models; no code treats them as flying) | `AlwaysAnimated` `0xCE650` (`Body.AlwaysAnimated`, by `Body.InAlwaysAnimated` `0x80011830`) | `ActorTables` | none: the port animates every body, and doesn't import it |
| Bodies' shadow texture, Borg1 `0x0003` (`BORG1_PlayerShadow`); drawn within 20 of the camera's aim in trek, 128 in combat, none on the title | `InitPlayerHandler`, `InitZoneEngine` | Fact | the runtime draws `aidyn:texture/0x0003`; the core holds the distances (`BodyShadow`) |
| Oriana's path lights: a scene object whose second model is `0x374A` (`BORG5_OrianaLight`) goes out | `RenderVoxelScenes` `0x8000cda4` | Fact | the 23 objects' `SceneObject.GoesOut` ([map objects](../systems/map-objects.md#orianas-path-lights)) |
| Minimap pieces for eight map sets: chunk, group, seen at start, image, position. Which map set each is for, and the pieces chosen by where the player stands (Gwernia's courtyard, walls and tower, the mage school's stairs, the Ehud underground's C11 and I09, the Barrows' E01), are code | `MinimapSections` `0xE07A4` (pointers; `minimap.cpp`, `BorgMaps::LoadMap`, `ShowingSpecialSection`) | `Minimaps`, with facts (`MinimapImporter`) | a map set's `minimap` |
| The six title fly-throughs, 20 bytes each: a Borg6, then the origin chunk's row and column and the row and column `InitZoneEngine(2, 0)` builds round; place 4 is passed over without the Pak (`0x800D7EA4`), which the port always plays as with | `FlycamSequences` `0xE15DC` (`gFlycamSequences`, `Title.StartScenery` `0x80054D70`, place at `Title.FlycamPlace` `0x800E0A54`) | `TitleTables` | `aidyn:title/0` ([ui](../systems/ui.md#the-title)) |

**Spawn points** (`Zones.SetMapSet`, called by `TeleportPlayer`, `InitZoneEngine` and `CutScene.LoadStage`): a chunk and a position per portal, the row and column as immediates (`addiu s7`, `s6`), x and z as floats into `Zones.SpawnX` and `Zones.SpawnZ`. By portal, (row, column) at (x, z): the Overworld's 0, I08 at (16, 62) (`0x80026374`, `0x800ce7d8`); MiscInteriors' 0, (5, 3) at (5.9, 3), and 1, (1, 1) at (5, 5); Erromon's 0, (1, 15) at (13, 25); the Barrows' 0, (4, 1) at (10, 10); the Jundar interiors' 0, (1, 1) at (16, 15); Ehud's underground's 0, (17, 1) at (5, 5); Ugarit's 0, (13, 5) at (10, 82); each battle arena's portal n, the set's n-th chunk (`Zones.ArenaChunks`, ROM `0xDD674`), at (20, 20). `InitGameTrek` makes the player's body 10 up (`0x80021018`) and sets it down from on high. The other sets have none, and a portal of −1 leaves the player where he is. The debug build's `Zones.SetMapSet` has other places and isn't imported. The runtime reaches the other portals' places through teleporters, a battle's own arena and the new game.

## Assets the code names

**UI chrome** needs no records: mods override it as assets under its Borg IDs ([ADR 0004](../adr/0004-record-ids.md), [ADR 0005](../adr/0005-plugin-format.md)). It's the conversation box's portrait border (`BORG8_PortraitHighlight`), the translucent band and feathering of popups and the conversation box (`BORG8_Pixel`, a texel of alpha 136; the gradients `0x1C0`–`0x1C3` and corners `0x1BC`–`0x1BF`: above a box `0x1C3`, below `0x1C2`, left `0x1C0`, right `0x1C1`), the overscan bars' sizes (`Graphics::EndList`), the compass, menu backgrounds and titles, the calendar and sundial, the logos, the intro cards and the Controller Pak screens. The port names them in `MenuImages`, `HudSprite`, `UiImages`, `TitleScreen` and `IntroCards`.

**Portraits:** a speaker with none shows `BORG8_PortraitNPCMale1` (`0x0125`), and Alaron's portrait shows over a conversation's choices when the line before them was the other speaker's alone (`DialougeClass::Setup`).

**Debug only:** the boulder that replaces scene models, and the debug menu's models. The debug ROM's game state cheats menu reads only its "appear" option.

## Findings in the tables

- **Map 13's region flag is on Cradawgh's Body.** Gaining Map 13 reveals nothing.
- **Two world map region entries can't apply:** they name Overworld chunks G18 and R29, which don't exist. The table names chunks by row and column alone, with no map set, so Ugarit's M06, outdoors, sets region 1 (788) too, which the party has had since the game's first chunk.
- **Most map items are never given.** Only Map 2 (in a container) and Map 7 (sold by shop 17, STTA05 in Talewok) have sources. Nothing gives Map 5; its region flag (791) is set on entering Talewok's chunks, which shows the same piece. The Skull Key and the Map to the Goblin Lair have no source either, possibly cut content. The Blood Key is the only key item that's an npc's or encounter's direct loot.
- **Each cinematic shot names its own caption list.** The flag list (`SeqenceFlags`) holds a caption list per shot (or none), then the done flag; each of the 15 lists shows over exactly one shot. The ending has six shots, each with its own Borg6 and Borg12, matching its six clip planes and flag entries.
