# Combat: battle mode (2.5, 2.6, M3)

This is the map of the original's combat that M3 is planned from ([roadmap](../roadmap.md#next)): its code, its states and what it keeps in RAM. Each part gets its detail as the session that ports it reads it. What the player sees is in [engine-behaviour](../engine-behaviour.md#battle-mode). [The port](#the-port) says what's ported so far and by which classes: a battle into its arena and out, with the combat camera and the pause menu's Flee, its turns, walking in the field and skipping, the battle screen, and attacking, death and reserves.

Addresses are 1.0's. Names are in the symbol map, prefixed `Combat.`, `Combatant.`, `TurnOrder.`, `ArenaGrid.`, `TargetSet.`, `EnemyAI.`, `AiScores.`, `CombatGui.`, `CombatMenu.`, `CombatMarkers.`, `CombatSpellFx.`, `CombatCamera.`, `CombatRadar.` and so on, all coined but `ProcessCombatCamera`. Fields marked **checked** were read in the combat checkpoints ([rig](../reference-rig.md#checkpoint-library)), one battle against ten rats; the rest are read from the code.

Combat is game mode 2 (`Game.Mode`, `0x800D80C8`). `ScreenFade.Step` (`0x8001F330`) calls `Combat.Frame` (`0x80022BA4`) each frame, and the mode it returns is the next: 2 to stay, 1 trek, 3 the pause menu, 6 the post-battle windows, `0xC` a cut-scene, `0xE` a cinematic, 0 leaves the game.

## The code

About 34,000 instructions, in the debug build's combat files and a dozen unnamed units around them. One global holds a battle, one machine of 32 states drives it, and everything hangs off a combatant's 0x8C-byte record.

| Cluster | 1.0 range | Debug file | Fns | Instructions |
| --- | --- | --- | --- | --- |
| The frame, the 32 states, entry, exit, the Shadow | `0x80021270`–`0x80024028` | `src/gamecombat.cpp` | 58 | 2,837 |
| Setup, spawns, rewards, the log, the arena's heights | `0x8005C0C0`–`0x8005EAD0` | `combatengine/combat.cpp` | 41 | 2,690 |
| Turn order, a round's end, win and loss | `0x8006A130`–`0x8006AF14` | `combatengine/turn.cpp` | 11 | 889 |
| Combatant: record, flags, position, field radius, turns | `0x8005EAD0`–`0x80060818` | `combatengine/combatEntity.cpp` | 60 | 1,874 |
| Combatant: spell targeting, a turn's start and end, flee, resistances, death | `0x80060818`–`0x800622C4` | the same | 27 | 1,707 |
| Combatant: melee and ranged chance, damage | `0x800622C4`–`0x80063548` | the same | 14 | 1,185 |
| Combatant: casting | `0x80063548`–`0x80064C84` | the same | 28 | 1,487 |
| Combatant: potions, flasks, Troubadour, Healer, carrying out an action | `0x80064C84`–`0x80066100` | the same | 14 | 1,311 |
| Combatant: what the body holds, messages, helpers | `0x80066100`–`0x80067498` | the same | 28 | 1,254 |
| The arena grid and paths | `0x800674A0`–`0x80068FE0` | three unnamed | 30 | 1,737 |
| Target sets | `0x80068FE0`–`0x8006A130` | one unnamed | 27 | 1,105 |
| The enemy AI | `0x80056E10`–`0x8005C0C0` | one unnamed (4,800), `aiInfo.cpp`, `aiscores.cpp` | 73 | 5,247 |
| Controls by state | `0x80083B00`–`0x80085240` | one unnamed | 27 | 1,488 |
| The C-up and C-down menus | `0x80087C60`–`0x80089780` | three unnamed | 42 | 1,730 |
| The GUI: portraits, bars, the log box | `0x80085A80`–`0x80087C60` | `src/combatgui.cpp` | 42 | 2,164 |
| The field's ring | `0x80085240`–`0x80085A80` | `src/combatmarkers.cpp` | 8 | 526 |
| Target marks, attacks in flight, numbers | `0x80089780`–`0x8008AA90` | `combattargetvisuals.cpp`, `combatattackvisuals.cpp`, `combatstatindicator.cpp` | 19 | 1,217 |
| Spell visuals | `0x8008AA90`–`0x8008D1F0` | `src/combatspellvisuals.cpp` | 38 | 2,514 |
| The camera | `0x8008D1F0`–`0x8008DD30` | one unnamed | 8 | 717 |
| The radar | `0x8008DD30`–`0x8008E3C0` | one unnamed | 6 | 419 |

Besides these, combat calls `gameclasses/entity.cpp` (`0x8006E110`–`0x80072700`, 4,510) and `spell.cpp` (445) for hit points, stats, spells and effects, which [characters](characters.md) has partly ported, and three of `party.cpp`'s: `0x800753F0` and `0x800758CC` (a potion drunk or a weapon changed in the pause menu, which end the turn) and `0x80078BA4`.

## In and out

**The setup block** (`0x800D9456`–`0x800D9485`, **checked**: empty in checkpoint 01, filled in 02) is what every source fills, and `BattleSetup` is the port's:

| Address | Holds |
| --- | --- |
| `0x800D9456` | 1 when filled; 0 makes the debug build's random test battle |
| `0x800D9458` | 12 entity IDs, 0 ending them (ten rats, `0x026E`): the first four start on the field, the rest are reserves |
| `0x800D9470` | bonus loot: an item, or a chest if its type is 8 |
| `0x800D9472` | the encounter flag (1321 for the rats; the Goblin Ambush's is 978) |
| `0x800D9474` | 1 if winning remembers the monster party's map object; then its index (`0x800D9476`), map set (`0x800D9478`), row and column (`0x800D947A`, `0x800D947C`) |
| `0x800D947E` | the collision byte, 0–2: how the party's placed. 2 is an ambush |
| `0x800D947F` | the arena, 0–27 |
| `0x800D9480` | the animation byte, 0–3: the enemies' spawn set is 4 less |
| `0x800D9481` | can flee |
| `0x800D9482`, `0x800D9484` | the monster party object's flag slot A (`+0x1C`) and its flags (`+0x14`), which `Combat.StateVictory` evaluates (`MapObjects.GateHolds`, `0x800116AC`) before it remembers the party |

- **Filled by** `BattleSetup.FromMonsterParty` (`0x800094B4`: `Wanderer.Tick`, `IMapObjectHost.Talk` and a script's battle op, `0x80050804`), `CampEncounters.Load` (`0x800096DC`: collision byte 2, animation 0, no fleeing, no loot or flag) and the script ops. `Wanderer.Tick` writes collision byte 2 and the animation byte by what the player was doing as it noticed him (3 running, 2 walking, 1 sneaking, else 0); `Talk` writes animation 0 and collision 0 with the player in a cone ahead of the party's body, else 1 (`0x80010AE8`–`0x80010B74`).
- **The enemy list** (`BattleSetup.FillEnemies`, `0x80009374`) is three passes over a party's eight kinds: each kind's least, in order, stopping at the party's most or 12 (`BattleSetup.SureKinds`, `0x80008EC8`); then, unless that reached the most, a `Range(0, most − least)` more of each kind whose most is above its least, in order, stopping the same way, and none at all if it comes to under the party's least (`BattleSetup.ChooseKinds`, `0x800090A4`); then, where the party's average level is below the enemies', each kind loses 0.3, 0.2 or 0.1 of its count by the party's level × 3 over the enemies' (`BattleSetup.TrimByLevel`, `0x800088E4`, the shares at `0x800CD470`), removed from the front, an entry shifted into a removed one's place being passed over. A list come to nothing is one Skeleton (`EntityList[0xBB]`, `0x0263`). So the field is the sure ones first, not a shuffle. `Combat.ForbidFleeForBosses` (`0x8005CCCC`) clears the flee byte for 13 entities (`0x800D2688`), and `Combat.ApplyFleeRuleByFlag` (`0x8005D054`) has the last word by flag ([hardcoded-content](../hardcoded-content.md)).
- **The arena** (`BattleSetup.ChooseArena`, `0x80008698`, for every battle but a script's) is by the terrain of the chunk the player's in (`World.GetTerrain`), through a jump table at `0x800CD400`. A range is a roll between them, inclusive (`Rng.Range`):

  | Terrain | Arena | Terrain | Arena |
  | --- | --- | --- | --- |
  | 0, 7 | 6–7 | 11 | 23 |
  | 1, 24 | 2–3 | 12 | 22 |
  | 2 | 0–1 | 13, 25 | 27 |
  | 3, 27 | 14–15 | 14 | 24 |
  | 4 | 19 | 15 | 25 |
  | 5, 17 | 4–5 | 16 | 26 |
  | 6 | 18 | 18 | 20 |
  | 8 | 12–13 | 19, 23, 26 | 8–11 |
  | 9 | 17 | 20 | one in two 6–7, else 8–11 |
  | 10 | 16 | 21 | one in two 2–3, else 8–11 |
  | | | 22 | one in two 0–1, else 8–11 |

  A terrain of 28 or more writes nothing, so the last battle's arena stays. A script's arena is its own, above 27 made 0 (`0x80050804`). Nothing but the debug build's random battle reaches arena 21. Terrains 20–22 draw `RollD(1, 2)` first, 1 for the first range. The importer carries the table as a fact on each terrain record (`TerrainRecord.Arenas`, `Aidyn.Rom.Engine.Arenas`).
- **The switch.** `TrekLoop.QueueState(2)` (`0x8001FF04`) fades out; trek's state 2 marks trek to be built again on return (`0x800DB067`) and returns mode 2.
- **`Combat.Init`** (`0x80022D1C`, on the first frame, while `0x800DB0A4` is 1): takes down trek's HUD, allocates the combat struct (`Combat.Reset`) and loads the combat strings, sets map set 16 (`0x800D8414`) and the arena's chunk (`0x800D8416`), and starts the zone engine in mode 1 (`InitZoneEngine(1, 0)`, `Zones.Mode`): trek's chunk streamer, collision, lights, sky and weather, over the Battle arenas, with the zone engine's point at (20, 20) in the arena chunk, the shadows' distance 128, and the battle's song chosen in `Zones.SetMapSet` (one `Below(5)` of the map set's five, or `Below(4)` of four without the Expansion Pak). `Combat.Setup` (`0x8005D15C`) fills the struct: the counts, the arena grid, a combatant and a body for each living party member and enemy on the field, Sholeh moved to the party's side if she's in the first four, the leader, whether there are flee points, the turn order and the flee rules by flag. Then the GUI, a body for each combatant (`Combat.CreateBody`, `0x8005C474`), the first turn's line, the camera on the current combatant's body (`Camera.SetEye`, `GiveCameraToPlayer`, `CombatCamera.Reset`), the first turn (`Combat.StartFirstTurn`, `0x80022C9C`: its line, the radius, the ring and the field, but not the clock), the effects, and state 31 with the fade in, a sixtieth a tick (`0x800CE308`). The Goblin Ambush's flag (978) makes it non-lethal, gives the Shadow two turns and heals the first member. Its `0x800E6850` and `0x800E6860` are `Combat.StateMoving`'s walk positions, not cameras.
- **`Combat.Exit`** (`0x80023060`, after every frame, acting when a state asks, `0x800DB0A8`): marks the setup block used, takes every effect off every combatant (`Entity.ClearEffects`, `0x80071830`: the potions' but Stealth's, kind 0x10, and the 15 timed spells'; hit points stay), frees the GUI, the effects, the bodies, the struct and the zone engine, and takes the dead out of the party (`Party.AfterBattle`, `0x80074EE8`). After the Goblin Ambush (978), however it ends, the leader's Strength goes back to its base, flag 47 is set, Alaron is healed and trek's kept place becomes Oriana's hut (map set 6, E03, (5.9, 0.5, 3.0), facing +x). Where it goes is the mode the state returned:
  - **A win** (state 12): mode 6, the post-battle windows, pause states 5 (experience and training) and 6 (loot) ([ui](ui.md); `PauseMenu.NextState`).
  - **A loss** (state 13): the game over script (state 21, mode `0xC`), or for a non-lethal battle (`0x800DA0DC`) the flag, the clock to the next morning and mode 1.
  - **Fleeing**: the pause menu's Flee, offered only in state 14 and while `0x800D9481` is set, with no confirmation, roll or line in the log (the combat strings' "The Party flees" is never read): it sets `Combat.NextState` to 20 (`pauseMenuSelectCallBack`, `0x800403AC`), and once the menu has rolled up, `Combat.StateLeaveFled` sets `Zones.Fled` (`0x800D9376`) and returns mode 1. State 16 is a win's, after the post-battle windows (`Combat.StateVictory` sets `NextState` 16).
- **Back in trek.** As trek faded out, `TrekLoop.Free` kept where the player stood, his heading, the camera's eye and the chunk (`Zones.SavedPosition`, `0x800D906C`, 0x2C bytes). Trek is built again there (`InitGameTrek`, `0x80020F40`), the body put at the kept place exactly, not set down. Having fled, it's the **breadcrumb** farthest from there instead: trek's zone engine keeps where the player stands every 120 ticks, unless the nearest kept in the map set is under 10 away (the places within their chunks compared, as though in one), in a ring of 15 (`Zones.Breadcrumbs`, `0x800D90E0`; `HandleZoneEngineFrame`, `0x8000E784`), and `Zones.FindBreadcrumb` (`0x80009A48`) takes the farthest in the same map set, a chunk counting 100 a row or column, the last of equals. Every time trek is built, all 15 are where the player stands (`Zones.ResetBreadcrumbs`, `0x8000DF00`), so they aren't saved. Checked in checkpoints 01 and 02. The monster party that started the battle is remembered only on a win; after fleeing it's back at its home node with every wanderer, and can hunt again.
  - **The Shadow merging**: state 17, `Combat.MergeShadow` (`0x800675C0`), mode `0xE` (cinematic 4, [cinematics](cinematics.md)).

## The arenas

The Battle arenas are `aidyn:mapset/16` ("Battlefields", 150 to a cell): 28 chunks, the arena number being the chunk's place in the map set's list (`Zones.ArenaChunks`, `0x800DCA74`, which the importer reads), but only 15 maps. Chunks sharing a map are the same arena, terrain, scenes and music alike. `Zones.SetMapSet` puts the zone engine's point at (20, 20) in it.

| Arenas | Chunks | Map | Collision (x × z) | Flee points |
| --- | --- | --- | --- | --- |
| 0–1 | A01, A03 | 0x0E87 | 76 × 77 | 8 |
| 2–3, 6–7, 21 | C01, C03, G01, G03, K11 | 0x0E88 | 85 × 98 | 8 |
| 4–5 | E01, E03 | 0x0E89 | 61 × 70 | 2 |
| 8–11 | I01, I03, I05, I07 | 0x0E8A | 53 × 64 | 5 |
| 12–13 | K01, K03 | 0x3629 | 46 × 36 | 1 |
| 14–15 | A11, A13 | 0x363B | 59 × 59 | 3 |
| 16 | C11 | 0x3632 | 69 × 57 | 4 |
| 17 | C13 | 0x3630 | 46 × 46 | 0 |
| 18, 25 | E11, M17 | 0x3675 | 46 × 46 | 4 |
| 19 | G11 | 0x366F | 79 × 88 | 4 |
| 20 | I11 | 0x366C | 12 × 48 | 0 |
| 22 | M11 | 0x3626 | 43 × 45 | 8 |
| 23 | M13 | 0x3644 | 42 × 44 | 3 |
| 24, 27 | M15, M21 | 0x3672 | 46 × 46 | 4 |
| 26 | M19 | 0x3678 | 46 × 46 | 4 |

- **What they place:** reference points and up to 12 scenes, all always there (flag 0 inverted), some only with the Expansion Pak. No lights, sounds or other objects. The 46 × 46 arenas are a flat disc of 24 triangles; the scenery reaches far past the collision, to ±300 in I11.
- **The points, by name** (`FindReferencePointName`, `0x8000B7C8`, with the combat strings' `%s%u%c`, `good`, `bad` and `flee%d`): every arena has the 40 spawns `good0a`–`good4d` and `bad0a`–`bad4d`, and `flee0` up to `flee7`. Arenas 17 and 20 have no flee points, so enemies there can't flee (`0x8005E76C` sets `+0x5440`).
- **Which spawn** (`Combat.FindSpawnPoint`, `0x8005C210`; `Combat.SpawnPosition`, `0x8005C294`): `%s%u%c`, the side's word, a set number and a letter. The party takes `good` and the enemies `bad`, swapped in an ambush (collision byte 2). Both sides start in set 4 − the animation byte, and reserves in set 0; in an ambush the party's set moves by the Ranger's roll (`Party.BattleAmbushRoll`, `0x80076C60`): the member, dead or not, with the strictly best modified Ranger who has the Stamina for it (5 less the Ranger, at least 1) rolls a d100 against Intelligence × 3 + Ranger × 10; failing, the set is 4 (3 with animation 3); under it, the margin's step (`Party.SkillCheckStep`) and the Stamina spent: step 1 moves it a set up (not with animation 0), 2 and 3 leave it, 4 or more a set down (not with animation 3); with no one to roll, no draw and no move. The party's letters are the living members' count so far, Sholeh's always `d`, and an enemy's its place in the list. A missing point stops the original (`FindReferencePointName` asserts). The spawn's x and z, truncated, are the combatant's cell (**checked**: Alaron's `bad2a` at (52.9, 52.3) is cell (52, 52)), and the body is made a unit up there and set down (mode 0).
- **Which way they face**: one draw each, as they're made: `Range(1, 3)` for the party, and for the enemies `Range(5, 7)` with collision byte 0, else `Range(1, 3)` too; the direction's vector is `Combat.DirectionVectors` (`0x800D27A8`, north (0, −1) round to north-west), and the body's heading is that way (the original's body field is its negation).
- **The ring's teardrops** are drawn by code, not placed (`CombatMarkers`, `combatmarkers.cpp`): a pool of 20 copies of `aidyn:scene/0x0794` (`CombatMarkers.Init`, `0x800854A8`, at `0x800E3138`, 0x1C each), a blended billboard about 0.41 × 1.0 with a scrolling texture (Borg1 1937); the code sets no scale, turn or scroll, and draws each with the zone's colours and fog (`Zones.SetSceneColors`). `CombatMarkers.Update` (`0x80085548`) works the radius out first, then lays the ring round where the combatant **began its turn** (`+0x08`), not where its body is: its radius R is the field's plus the body's scaled radius (`+0x34`), plus that again for one with an AI record; 6 teardrops up to R 3, 16 up to 7, else 20 (`CombatMarkers.CountFor`, `0x8008580C`, the floats at `0x800D43C8` and `0x800D43CC`); the first due +Z and the rest round toward −X by `(360 / N) × 0.017453`, the way round kept in two statics never reset (`0x800E3130`, `0x800E3134`, from (0, 1)), so each ring starts a ten-thousandth further round than the last. Each is set on the ground as a body is (`CombatMarkers.RingPoint`, `0x80085240`): a sphere of 0.5 swept from 100 to −100 past see-through triangles rests 0.01 above the first facing it; a miss tries again from a little to the side of the ring's point (two draws, up to 0.25 each way, not adding up), four times, and one that misses every time takes the body's height and then half way between its neighbours'. The ring is laid out only as a turn begins (and as the field follows a combatant near the Shadow), and drawn every frame (`CombatMarkers.Draw`, `0x80085848`, from `Combat.DrawFrame`) but from `Combatant.ExecuteAction` to the turn's end (`Combat.ActionActive`, `0x800DB0CC`) and from `Combat.StateVictory`; there's no fade in. **Checked** in 02, 03, 04, 09 and 10: every teardrop, where 03's sixteenth missed once and was tried again to the side.
- **The grid** (`ArenaGrid`, `0x800674A0`–`0x80068F7C`) is 100 × 100 cells of a byte a unit across in the arena chunk's own coordinates, index x × 100 + z, the height × 2 in the low bits and bit 7 for taken. `ArenaGrid.Init` (`0x80067960`) sizes it by the map's object grid (cell size × columns and rows, rounded half away from zero). `ArenaGrid.Fill` (`0x8005E900`) sweeps a sphere of 0.5 from 100 down to −100 through each cell's centre over the arena chunk's collision, see-through triangles included, the first facing it in the grid's order: the byte is twice the height it touches, truncated, and a miss is taken (0x80). Each combatant takes its footprint (`Occupy`, `0x8006820C`: from its cell on in +x and +z, none if any cell is outside), twice its scaled radius rounded half away from zero, at least 1 (the party 1, rats 2). **Checked**: the port's grid equals checkpoint 02's in all 8,330 cells. `WalkClear` (`0x80067E6C`) is a line over free cells with steps under 2; `HasSight` (`0x800684C4`) is the line in 3D for ranged attacks and spells, through the collision and the bodies between; `FindPath` (`0x80068CD4`) is a greedy walk of at most the grid's diagonal, which `StraightenPath` trims.

## The combat struct

`Combat.Current` (`0x800E0BF0`) points to 0x544C bytes (at `0x80282230` in the checkpoints; **checked** but where said):

| Offset | Holds |
| --- | --- |
| `+0x00` | the part of the day as it began |
| `+0x01` | combatants in play (8, growing as reserves join, to 14) |
| `+0x02` | of them, active, able to act (8, then 7 after a rat died) |
| `+0x03`, `+0x04` | the party's size, and its members active |
| `+0x05`, `+0x06` | enemies on the field at the start, and enemies active |
| `+0x07` | the next reserve's place in the enemy list, `0xC` with none left (4, then 7 once three joined) |
| `+0x08` | u32, 1 after the first enemy died |
| `+0x0C` | u32, 1 once the leader's gone |
| `+0x10` | the leader: the first enemy with the highest base Intelligence, a later one needing strictly more |
| `+0x11` | the leader's morale (`EnemyAI.Morale`, each round) |
| `+0x12` | the AI's area danger: the party's flask tier (`Combat.PartyPotionTier`), or twice its best effective Fireball level, a radius the enemies spread out by (`EnemyAI.AreaDanger`, `0x80058A48`) |
| `+0x13` | the Troubadour's bonus in play, 0 with none |
| `+0x14` | the last target |
| `+0x18`, `+0x1C` | f32, the camera's focus: the combatant's, or its target's |
| `+0x20`, `+0x24` | u32, the gold and experience pools (0 and 1000 for the rats) |
| `+0x28` | the enemy list (the setup block's) |
| `+0x2C` | the current combatant |
| `+0x30` | the turn order: u8 turns in the round, u8 the one now, u32 `+0x34` the alternation toggle, `+0x38` the list of combatant indexes |
| `+0x3C` | the arena grid, 0x4E28 bytes: two 100 × 100 layers (heights and occupied; path marks), `+0x4E20` u16 area, `+0x4E22` and `+0x4E23` width and depth (85 × 98 for the rats'), `+0x4E24` and `+0x4E25` their halves, `+0x4E26` the diagonal |
| `+0x4E64` | target set A, for attacks |
| `+0x4F0C` | target set B, for spells |
| `+0x4FB4` | the loot, an inventory made at victory |
| `+0x4FB8` | a message popup |
| `+0x4FBC` | s32, the walking states' stuck timer |
| `+0x4FC0` | the combat strings |
| `+0x4FC4`, `+0x50C4`, `+0x51C4` | three log lines of 0x100: shown, queued, and a third ([ui](ui.md#the-log-box)) |
| `+0x52C4` | the log's flags (only bit 2, the third waiting); `+0x52C8` the hold's timer and two Shadow states'; `+0x52CC` the action timer (`Combat.StepActionTimer`, `0x80021708`) |
| `+0x52D0` | the 14 combatants |
| `+0x5318` | f32 x, z, the AI's goal; `+0x5320` its path, up to 144 cells as byte pairs |
| `+0x543C` | 1 while "Enemy reinforcements flee" holds the battle, 60 ticks ([ui](ui.md#the-log-box)); `+0x5440` the arena has flee points; `+0x5444`, `+0x5445` the last flask's cell; `+0x5448` the frame counter |

Beside it: the combatants' bodies, `Combat.Bodies` (`0x800D9488`, 14 by slot, 0x780 each), `Combat.State` and `Combat.NextState` (`0x800D9454`, `0x800D9455`), the Shadow's and Alaron's slots (`0x800D94CC`, `0x800D94CD`) and the AI's scores (`0x800E0BE0`).

### The combatant

| Offset | Holds | |
| --- | --- | --- |
| `+0x00`, `+0x04` | f32 x, z, in arena cells (Alaron starts at 52, 52) | checked |
| `+0x08`, `+0x0C` | f32 x, z at the turn's start | checked |
| `+0x10` | s32, the last target, −1 | checked |
| `+0x14` | direction 0–7, N to NW | checked |
| `+0x18`, `+0x1C` | f32 facing: the body's heading as the port has it, the negation of the body's own field | checked |
| `+0x20` | its slot, its body in `Combat.Bodies` | checked |
| `+0x21` | the field's radius | checked: 9, 10, 6 |
| `+0x22` | moves counted this round, to 3; the fourth sets flag 3 (`Combatant.UpdateFromBody`); a round's end clears both | checked |
| `+0x23` | footprint in cells: the party 1, rats 2 | checked |
| `+0x24` | the action before a spell or flask | |
| `+0x25` | the action: 0 none, 1 melee, 2 ranged, 3 a flask or a potion drunk, 4 melee or thrown | checked 1 |
| `+0x26`, `+0x27` | a level, and the potion's or flask's kind (a spell goes by the entity's `+0x3C`–`+0x3E`: the margin, the source and its index) | |
| `+0x28` | Mirror's counter (`Combatant.Mirror`) | |
| `+0x29`, `+0x2A` | the damage, and the healing, to show | |
| `+0x2C` | u16 flags: 0 acted, 1 active, 2 performing, 3 moved, 4 the hostile side, 5 from the enemy list, 6 gone (dead or fled), 7 engaged, 8 Lay On Hands, 9 Heal With Herbs, 10 a newcomer | checked: the party 0x02, rats 0x32, engaged Brenna 0x82 and rat 0xB2, newcomers 0x432 |
| `+0x30`–`+0x3F` | two resistances, element and multiplier (Alaron's element 3 × 0.75) | checked |
| `+0x40` | the entity, its character sheet | checked |
| `+0x44` | the AI record, enemies only | checked |
| `+0x84`–`+0x8A` | what the hand holds, a weapon or a flask | |

**The AI record** (0x1C, `EnemyAI.Init`, `0x8005BA7C`): `+0` a copy of the spells (0x2C, or none), `+4` and `+7` the style (0 melee, 1 ranged, 2 caster), `+5` the target (0xFF none), `+6` morale (5 for rats), `+8` the attack style 0–10 (`EnemyAI.StyleTable`, `0x800E0B80`), `+9` flags (0 fleeing, 1 a morale check due, 4 hit lately), `+0xA`–`+0x11` eight action slots, `+0x12` and `+0x13` the path's place and length, `+0x14` the combatant, `+0x18` a flight's steps left (4). Checked in 02, 09 and 11, where the fleeing rat's flags went from 8 to 9.

**A target set** (0xA8, `TargetSet`): `+0x00` 14 candidate flags (u32 each), `+0x48` 14 selected, `+0x90` the candidates in the cursor's order, `+0xA2` the mode (0 attacks, 1 spells), `+0xA3` candidates, `+0xA4` selected, `+0xA5` the most that may be, `+0xA6` the cursor, `+0xA7` its place. Checked in 05: the cursor on combatant 7, one selected.

## The states

`Combat.State` (`0x800D9454`), 32 states in `Combat.StateTable` (`0x800DB0D4`); `NextState` (`0x800D9455`) is where a walking or turning state goes on to. Input is its own pass first each frame (`Combat.Controls`, `0x80083B00`, by state through `0x800850D4`). There's no machine of the turn's own: choosing, targeting, the menus, the pause and the AI's turn are all states.

| State | Function | What it is |
| --- | --- | --- |
| 0 | `Combat.StateEnd` `0x800216F4` | leave the game, mode 0 |
| 1 | `Combat.StateDraw` `0x80022860` | **a turn**: the stick moves the combatant in its field (`Combat.InputTurn`, `0x80084DB0`). A attacks (9, or 18 with a bow, a thrown weapon or a flask), Z or L with the stick centred ends the turn at once (`Combat.SkipTurn`, `0x80083E90`), C-up and C-down open the menus (23, 22), Start pauses. A combatant with an AI record goes to 8 |
| 2 | `Combat.StateAction` `0x80021774` | an attack's or spell's animation; at its end the hit or miss and the numbers (`Combat.ShowDamage`), then 4, a spell through 3 |
| 3 | `Combat.StateCast` `0x80021B24` | the camera glides to the spell |
| 4 | `Combat.StateResult` `0x80021F30` | the numbers; 60 ticks on, `Combat.RemoveFallen` takes the dead and fled, and the turn ends |
| 5 | `Combat.StateMoving` `0x80021FD0` | a body walks to a point (the AI's path, a reserve coming in), then `NextState` |
| 6 | `Combat.StateFacing` `0x800221B0` | turns to the target, then carries out the action |
| 7 | `Combat.StateWaitIdle` `0x8002235C` | waits for the body to stop, then 5 |
| 8 | `Combat.StateAI` `0x800223F8` | `EnemyAI.Step` each frame |
| 9 | (draws) | an attack's target: the stick cycles, A confirms (`0x800842C4`) |
| 10 | (draws) | a spell's target (`0x800844BC`) |
| 11 | (draws) | placing an area: the stick moves its centre (`0x80084964`) |
| 12 | `Combat.StateVictory` `0x80022450` | after the fade: the flags, the monster party remembered, the loot, mode 6 |
| 13 | `Combat.StateDefeat` `0x80022590` | after the fade: the game over script, or the non-lethal ending |
| 14 | `Combat.StatePause` `0x80022678` | the pause menu, mode 3 |
| 15 | (draws) | the pause menu sliding out |
| 16 | `Combat.StateLeave` `0x800226E8` | leave, mode 1 |
| 17 | `Combat.StateShadowMerge` `0x800226F8` | after the Shadow's cinematic |
| 18 | `Combat.StateTargetThrown` `0x8002272C` | aiming a bow, a thrown weapon that flies or a flask freely (`0x80084BA0`) |
| 19 | `Combat.StateEndTurn` `0x80022764` | ends the turn: a potion drunk in the pause menu |
| 20 | `Combat.StateLeaveFled` `0x800227B8` | leave, with `0x800D9376` set |
| 21 | `Combat.StateLeaveToScript` `0x800227D4` | leave for the game over script, mode `0xC` |
| 22, 23 | `Combat.StateMenuSpells` `0x800227E8`, `StateMenuItems` `0x80022844` | C-down's spells and skills, C-up's potions and item spells |
| 24, 26 | `Combat.StateRest` `0x80022898`, `0x800228F8` | 120 ticks (`Combat.RestTicks`, `0x800DB0D0`, counted down by the frame's ticks), then the turn ends: the AI's with nothing to do, a weapon changed or a flight's end (24, `Combat.EnterRest` `0x80023418`), or a performance begun (26). The player's skip doesn't rest, and the combat strings' "rests" (`0x84`) is never read |
| 25 | (draws) | the Troubadour's prompt, A to start (`0x8008508C`) |
| 27 | `Combat.StatePauseRequested` `0x80022930` | Start from any state, keeping it in `0x800D94D4` |
| 28 | `Combat.StateDefeatWait` `0x80022998` | 120 frames, then 13 |
| 29, 30 | `0x800229D8`, `0x80022A2C` | the Shadow vanishes without the Horn: text, fade, leave |
| 31 | `Combat.StateIntro` `0x80022A78` | the start: once the fade ends, 1 or 8 |

**Checked** along the battle: "X begins action" and state 1 or 8 (02–04); A, target set A and state 9 with the cursor on rat 7 (05); the turn, the attack, 2, then 4 with the rat dead (06); C-down, target set B and 10 (07), then 2 for the cast (08); reserves joining as the round's list is built again, through 5 and 8 (09); a rat's attack on Brenna in 2 (10); a fleeing rat walking in 5 with 8 next (11); and 12 once the last enemy's gone (12). 13 and 14 are mode 6 with the struct freed.

## A turn

1. **It ends** (`Combatant.EndTurn`, `0x80061300`): `CombatCamera.Reset` (`0x8008D210`, a glide stopped), the AI's target forgotten (`EnemyAI.ClearTarget`, `0x8005B998`), the spell chosen cancelled (the entity's `+0x3C`), the acted flag cleared, an item spent (the entity's `+0x3D` of 5 with an item of type `0x11` or `0x0D`, `0x8006F5C4`, not settled), state 1 and the ring shown again (`Combat.ActionActive` 0), the body asked to stand, the action put back (`Combatant.RevertAction`, `0x8006679C`) and a flask still in hand stowed (`Party.StowHeldItem`, `0x800763B0`), the last target forgotten if it's dead, then `TurnOrder.Advance`. Every end of a turn comes here: the skip, the action's result (state 4, 60 ticks on), the AI's rest, a potion drunk in the pause menu (19), and the AI's walk or turn with nothing to do (5, 6).
2. **The next** (`TurnOrder.Advance`, `0x8006AC4C`): nothing once the battle's won or lost (states 12, 13); the party's first member dead (`*GameState.Party`, not `Combat.AlaronSlot`) loses it (`Combat.Defeat`, `0x80023F98`: state 13 and the fade, or 28 in a non-lethal battle); the last combatant's body let go of its field (`TurnOrder.StopBody`, `0x8006A5C4`); then on through the round, its end building the next (step 7). Before each candidate the battle's end is checked (`TurnOrder.CheckEnd`, `0x8006A97C`, over `TurnOrder.CountActive`, `0x8006A880`, a reserve still to come counting as an enemy), and the candidate's radius, place and whether it can act are worked out (`Combatant.UpdateActive`, `0x8006615C`: not with no Intelligence, Willpower, Dexterity or Strength, nor immobilised), skipping it dead, unable or gone. Then the focus is on it, every combatant reads its body (`TurnOrder.ClearActions`, `0x8006AE64`, clearing the numbers to show), its turn begins, the clock moves on 600 (`World.AdvanceTurn`, `0x8007C01C`: `World.WrapYear` and `World.Update`, so the weather rolls if the part of the day changed; it ages no effects; **checked**, +600 a turn in 02–04), and a reserve that fled on arriving says "Enemy reinforcements flee" (`+0x543C`).
3. **It begins** (`TurnOrder.BeginTurn`, `0x8006A9C8`): the camera's given to its body (`GiveCameraToPlayer`, `0x80015E30`, which drops an R orbit) and anchored there; its line, "%s begins action", a newcomer's "joins the battle" (`0x7C`, clearing flag 10), or either overwritten by a performer's (`0xD0`); it reads its body (`Combatant.SyncFromBody`) and keeps where it starts (`SaveStartPosition`); the ring (which works the radius out again); its field (`Combatant.PlaceRingAndBody`, `0x8006627C`); then for a combatant with an AI record, its check to flee (`EnemyAI.CheckFlee`, `0x800572AC`: the leader's morale and its own against `RollD(2, 6)`) and state 8, otherwise a flask or spell carried over looked up and state 1, as `State` and `NextState` both, after the target sets are built. The Shadow's aura is the round's, not the turn's, and nothing sets a body down: bodies are set down once, as they're made.
4. **The field's radius** (`+0x21`, `Combatant.UpdateRadius`, `0x8005F29C`) is the first place in `Combat.RadiusByDexterity` (`0x800E0C20`: 0, 0, 0, 3, 7, 12, 16, 21, 26, 28, 30, 33, 36, 38) whose byte reaches the **modified** Dexterity, 14 past it (3 for 1–3, 4 for 4–7, … 13 for 37–38). Then each timed effect in its slot's order: Haste (kind `0x1E`) adds its level, and Stellar Gravity (`0x38`) takes its level away, leaving 1 if that's all or more; an effect's kind is its spell ID's low byte's place in the code's spell list (`0x800E2B40`). Then **1 at most while engaged**: an opponent alive and able within the larger of each body's radii, scaled (`+0x34`) or not (`+0x38`), plus 0.75 (`0x800D2810`), across the ground, both bodies read again first (`Combatant.IsWithinReach`, `0x8006011C`, from `IsFreeOfOpponents`, `0x8005F1B8`); this sets or clears flag 7 and stops a performance. Not engaged, 1 at most while performing, or 1 near the Shadow (`Combat.NearShadow`, `0x80023F34`: within 10, strictly, which logs the vortex line on the current one's). Then 13 at most, and 0 for Pochanargat (entity list place `0xAB`, `0x800E28EB`). It's worked out as each body is made, before each candidate in `Advance`, with each ring, and in the Shadow's battle as the combatant walks, so flag 7 is as old as that: **checked** in 09, where Brenna is engaged with a rat whose own flag is clear until its turn (10). Every radius in 02, 03, 04, 09 and 10 matches.
5. **The player** moves the body with trek's stick handler (`Body.ApplyInput`, `0x80014FEC`, fed each poll): R orbits the camera as in trek, and otherwise up on the stick is the way the camera looks, walking, running from 0.8 or sneaking with Z held, a party member turning 10° a poll. The field holds it: `Body.SetGoal` (`0x80015BD4`) keeps a centre (the combatant's place and the body's height as its turn began, `+0x274`) and a radius (`+0x280`) with body flag 4, and `ProcessPlayers` (`Body.ClampToGoal`, `0x80014518`) puts the body's X and Z back on that **sphere** after it moves, before the bodies push each other, whenever it's at the radius or past it in 3D. So it slides round the edge, and on a slope reaches a little less far across the ground. **Checked**: 03's walked bodies are 10.0000 from their centres in 3D. Every body gets its field as it's made, about where it stands, and loses it as its turn ends. Nothing in the grid stops a body; the bodies' push does (`Crowd`). After each poll the combatant reads its body (`Combatant.UpdateFromBody`, `0x8005F784`): its footprint leaves the grid and takes it again where the body stands (`ArenaGrid.Vacate`, `0x800682E0`, and `Occupy`, the cell rounded half away from zero), its place is the body's X and Z exactly, the focus follows and its facing is the body's; a change counts a move (`+0x22`), the fourth sets flag 3 (**checked** in 02–04), and the target sets are built again. The radius isn't worked out again as it walks up to an enemy: `Combat.MovePlayerBody` (`0x80084CE0`) does that only near the Shadow, centring the field on the body.
6. **The action** ends the turn: an attack, melee or ranged by the weapon (`Combatant.ResolveAction`, `0x80065ACC`); a flask thrown or a potion drunk (action 3: `ThrowFlask` `0x80065600`, `DrinkPotion` `0x80064C84`); a spell (`CastCurrentSpell`, `0x80065C08`); Lay On Hands or Heal With Herbs (`ResolveHealer`, `0x8006591C`); the Troubadour (`StartTroubadour`, `0x800662F4`); a weapon changed in the pause menu; or a skip. `Combatant.ExecuteAction` (`0x80065F6C`) ends a performance, hides the ring, carries it out, applies the damage, checks for death (`CheckDeath`, `0x80065E98`) and starts state 2.
7. **The round ends** (`TurnOrder.EndRound`, `0x8006A62C`) when the list runs out: the Shadow's aura first (`Combat.ShadowAura`, `0x80023AD0`, which can end the battle); each performer pays a Stamina point or stops with none; every combatant's moves and flag 3 are cleared and its effects aged a round (`Character.AgeEffects`, `0x80071444`: the potions', the timed spells', and what they do); the reserves come in (`Combat.SpawnReserves`); the toggle flips and the next list is built. The leader's morale moves at each of the AI's turns, not here. Wins and losses are only ever found at a turn's change, not at a death.

**Turn order** (`TurnOrder.Build`, `0x8006A16C`). A combatant's turns a round are by its **base** Dexterity (`Combatant.TurnsPerRound`, `0x8005F4D0`): 1–17 one, 18–24 one and two by turns (the toggle), 25–29 two, 30–34 two and three by turns, 35 and up three. The list is grouped: every third turn first, then every second, then every first, each group in random order (`Rng.Range`, drawing again on a repeat). Checked in 02: four party members and four rats gave `[1,3,0,2, 3,2,1,5,4,7,0,6]`.

**Win and loss** (`TurnOrder.CheckEnd`, `0x8006A97C`): no party member active is a loss (`TurnOrder.Defeat`: the party's line, state 13 or the non-lethal ending), and no enemy active with no reserve left a win (`TurnOrder.Victory`: `Combat.SetKillFlags` `0x80067860`, `Combat.TallyRewards` `0x8005E2AC`, state 12). Alaron's death is a loss alone (`TurnOrder.Advance`).

**Rewards** (`Combat.TallyRewards`): for every entry of the enemy list, its experience (the entity's `+0x7C`) and its chest (`+0x7E`, `Combat.AddChestLoot` `0x8005E0F0`: gold, drops, a random item, six slots), and the bonus loot. Each living member gets the whole pool (`0x80071528`). Read but not checked: the gold pool is doubled at the end, and looted items add to the experience pool.

**Morale and fleeing.** An enemy's morale (the AI's `+6`, from the entity's `+0x1B`) is changed by `EnemyAI.Morale` (`0x80056FA8`) as [engine-behaviour](../engine-behaviour.md#enemy-ai) has it. `EnemyAI.MayCheckMorale` (`0x8005722C`) holds the check back unless its Stamina's gone or half the enemies are; the roll is 2d6, and losing it is `EnemyAI.StartFleeing` (`0x8005B9E4`), which needs flee points and the enemy not engaged: four steps toward the nearest flee point (`0x8005AE3C`), then `Combatant.Leave` (`0x80061480`). A reserve can flee as it arrives (`EnemyAI.ReserveFleesOnArrival`, `0x80056E18`). Bosses can't (`Combat.NoFleeEntities`).

**Reserves** (`Combat.SpawnReserves`, `0x8005D914`, at a round's end, checked in 09) are [An attack](#an-attack)'s.

## An attack

Read from 1.0's code, checked by running the original's own functions on the checkpoints' RAM, and replayed by the port (`AttackCheckpointTests`).

**Target set A** (`+0x4E64`; `TargetSet.Rebuild`, `0x80069264`) is built at a turn's start, as the combatant moves (`UpdateFromBody`), as an aim is put away and by other callers; while a player aims (states 9, 10, 18) it isn't rebuilt on idle polls. `Rebuild` empties it, finds the candidates (`FindAttackTargets`, `0x80069304`: everyone in play `Combatant.CanAttack`, `0x80060590`, allows; one may be chosen), selects the first, and keeps the cursor on the last target (`+0x10`), else where it was (`RestoreCursor`, `0x8006A05C`). Without an action, and not healing, it stays empty.

- **`CanAttack`**: never the Shadow (entity `0x0226`); not healing, a target on the other side, not dead or gone, while the attacker has an action; its weapon reaching (`CanReachWithWeapon`): a flask always; melee and healing within reach (`IsWithinReach`, the bodies' radii plus 0.75); ranged from distance 3 to the weapon's range, truncated, with no opponent within reach; melee-or-ranged anywhere in range. Then **`ArenaGrid.WalkClear`** (`0x80067E6C`) over the grid between the two cells, or failing that **`ArenaGrid.HasSight`** (`0x800684C4`) between the tops of their heads (the feet plus the entity's height): a sphere of 0.1 swept through the collision, see-through triangles included, and no other living body within its unscaled radius of the line, which runs on past both ends.
- **`WalkClear`** steps the line's longer axis cell by cell: each must be inside, not taken unless the end is a step from it, and a step from the last unless the end isn't. A step is a rise under 2 in the grid's half units (`IsStep`, `HeightDifference`). But it steps only toward +X or +Z: going the other way its loop never starts and the line is clear unchecked, so a target with a smaller X (or Z, for a line longer in Z) never needs the 3D test.
- **The action** (`+0x25`, `Combatant.SetDefaultAction`, `0x800672D4`): no weapon, none, so **unarmed party members can't attack**; range 0, melee; else ranged, or melee-or-ranged for a Breath, a Spit or Spikes (`Weapon.AllowsMelee`, `0x8007B730`). Enemies have weapons too: a rat's is Rat Bite (`aidyn:weapon/0x0702`).

**Aiming** (state 1's A, `Combat.InputTurn`, with the stick centred and R let go): set A is built again, and with candidates `TargetSet.BeginAttack` (`0x800694A8`) orders them (`OrderByAngle`, `0x8006975C`) and puts the cursor on the last target, else where it was, else the first; state 9, the camera given to the cursor's body. A bow, a thrown weapon that flies or a flask (`+0x8A`, `Combatant.UpdateDrawnFlag`, `0x80066A2C`, through the bow list `0x800E0C08` and the thrown list `0x800E0C14`) takes state 18 instead, a free cursor at the target. The order is by where each body stands from the attacker's against its body's own heading field, the opposite of where it looks: ahead and to one side by the sine's size, descending; then behind and the other side, ascending; then ahead and the other side, ascending; then behind and the one side, descending (`Vec2.Cosine`, `Vec2.SignedSine`, a selection sort whose ties go to the later). It sets only the cycling order and the cursor's place.

**State 9** (`Combat.InputAttackTarget`, `0x800842C4`), each poll, edge-triggered, with no repeat:

- **Start** pauses, and the pause comes back to state 1, the camera to the combatant.
- **B, Z or L** put it away (`Combat.ReturnToTurn`, `0x80083C70`): state 1, the healing modes off, the focus on the combatant, both sets built again, the camera back. The body stays turned.
- **A** confirms (`ConfirmAttackTarget`, below).
- **The stick or the D-pad**, left, steps the cursor +1 through the order, and right −1 (`0x80084420`), the camera to the new target and the attacker's body turning toward it 10° a frame (`Combat.AimAtCursor`, `0x80084334`).
- **The camera**: with the cursor's body over 5 from the attacker's, the eye's goal is 4 behind the attacker, away from it, and 1.5 up (`CombatCamera.PullBack`, `0x8008D22C`), with `Camera::Mode1`'s last flag clear, which skips its rise, its leash and its rescue.

**State 18** (`Combat.InputThrownTarget`, `0x80084BA0`): C-left and C-right step the candidates; the stick moves a free cursor 0.8 a frame, turned by the camera and kept on the grid and within the weapon's range, and set A selects the candidate whose body is nearest it, within a unit (`0x80084A24`); A confirms the same way.

**The turn to face** (`ConfirmAttackTarget`, `0x80084078`): the target's slot goes in `+0x14`, set B is emptied and A keeps its selection but loses its candidates, the camera goes back to the attacker, its facing points at the target and its body's turn starts (`Combatant.FaceCombatant`, `0x8005FD28`), state 6. State 6's input (`Combat.InputFacing`, `0x80085018`) snaps the heading to the goal on every poll while the body's turning flag (`0x20`) is set, so the 10° turn never plays: **state 6 takes two frames whatever the angle**, then `Combat.StateFacing` (`0x800221B0`) carries out the attack.

**`Combatant.ExecuteAction`** (`0x80065F6C`), in order: a performance ends; the target is the last (`+0x10`); set B is emptied; the attack is resolved (`ResolveAction`, `0x80065ACC`: melee, ranged, or for melee-or-ranged melee if `CanAttack` holds as melee; then the attacker's request by its weapon's animation byte, `Combat.AnimationByWeaponClass` `0x800D27E0`: 0 → 16, 1 → 17, 2 → 13, 3 → 12, 4 → 15, 5 → 14, else 18); the Goblin Ambush's hold on Alaron (`Combat.ShadowFightTrick`, `0x80065DCC`); **`CheckDeath`** (`0x80065E98`) at once, before any hit point goes: a blow that takes them queues "%s killed" (`0x50`) and calls **`Combatant.Death`** now, or with a Cheat Death on the target queues "%s cheats death" (`0xFC`); the attacker's moves set to 1; **the damage stored on the target** (`+0x29`, a byte); the camera's glide from the attacker to the target (`CombatCamera.StartGlide`); and state 2 with `ActionActive`. The acted flag stays down (only `PrepareCast` sets it), so a weapon's spell rolls its own margin.

**Melee** (`Combatant.MeleeChance`, `0x800622C4`, every step truncated to 16 bits):

- The attacker's modified Strength and Endurance, base Level, the weapon's hit, 6 × its weapon skill (the modified one, `Skills.WeaponModified`) and 2 × Warrior.
- Less the target's 2 × modified Dexterity, base Level and shield defence (`Combatant.ShieldDefence`, `0x8005F6B0`: the shield's defence × the shield skill + Warrior, a byte, so Warrior counts without a shield). The Dexterity and shield don't count against a target held fast (`IsImmobilised`).
- 20 more from behind, and Thief × 10 (`SkillTerm`, `0x80062ED8`).
- × 1.15 (`0x800D2964`) against a target with no Stamina; the height, c + h × 0.05 × c, with h the attacker's cell's height byte less the target's (`0x7F` off the grid); × 0.8 with no Stamina of its own; × 0.9 at night; the aspect (`AspectScale`: a Lunar attacker × 1.25 at night and × 0.75 by day, a Solar one the other way round, `0x800D2990`); the Troubadour (`TroubadourScale`); at least 5.
- **From behind** (`Combatant.IsBehind`, `0x8005FB54`): the point the distance away along the attacker's facing lies within half a unit of the target in X and Z (`IsAimedAt`, `0x8005FA44`), and the two facings' dot product, rounded half away from zero to six places, is at least 0.7071.

**`ResolveMelee`** (`0x800628A4`): a d100, which `AdjustRoll` (`0x80062594`, magic's) turns with Warrior: at or under min(Warrior, a fifth of the chance), a second d100 comes off it. A hit is the adjusted roll under the chance.

- **A hit** draws the damage, then a d100 for the weapon skill (`Combatant.GainSkillExperience`, `0x80062650`: at or under a tenth of (8 − skill)² for a skill under 6, else 5, the base trains a level; half the points on a miss). It shows and queues "%s attacks %s" (`0x24`) or, the roll adjusted, "%s scores a critical hit on %s" (`0x2C`), and queues "%s loses %d hit points" (`0x30`; `0x34` for 1).
- **A miss** draws the experience, shows "attacks" and queues "%s avoids the attack" (`0x20`).
- So a melee hit draws the roll, [the adjustment], the dice, [the weapon's spell], the experience. **A critical is only its line**: the second d100's bigger margin is its only effect.

**`MeleeDamage`** (`0x80062AE4`): (the weapon's dice + the Strength tier) d6 (`Combatant.StrengthTier`, `0x80062008`: the first of 4, 9, 17, 24, 28, 32, 36, 39 at `0x800E0C30` the modified Strength is at or under, else 8); plus the margin's step (`SkillCheckStep`, `0x800DA348`) and the attacker's base Level; less protection (`ProtectionVsAttack`, `0x8005F71C`: `TotalProtection`, `0x8005F570`, the sheet's, body armor's and a fifth of Warrior, 0 if under 1, plus the shield's unless from behind, a byte); plus Thief × 2 from behind; at least 1. Then **`ApplyResistance`** (`0x80062704`): the product of every resistance of the target's own and its gear's that meets the weapon's element (`ResistanceApplies`, `0x80061A30`: the element, Physical also Cutting and Smashing, Magic every magical one), truncated, at least 1 unless one is 0; a weakness over 1 makes an enemy's morale check due. Then the weapon's spell's damage (`Combatant.WeaponSpellProc`, `0x800627D0`). The sheet's own damage byte (`CharExp+3`) and the entity's damage aren't read.

**Ranged** (`Combat.RangedChance`, `0x80062F50`): the attacker's modified Intelligence and Dexterity, base Level, the hit, 6 × the skill and 2 × Warrior; less (distance − 5) past 6; less the target's base Level, and unless it's held, its 2 × Dexterity and shield defence; 20 from behind (no Thief); × 1.2 against a target that hasn't moved or acted (`+0x22` 0), × 1.15 against one with no Stamina, × (h × 0.05 + 1), × 0.9 with no Stamina of its own, × (1 − the rain's intensity × 0.3), × 0.7 at night, × 0.8 against one that has moved; the aspect, the Troubadour, at least 5. `ResolveRanged` (`0x80063324`) is `ResolveMelee` with no Strength dice and no blow from behind in the damage (so the shield's protection always counts), the experience after the lines, and "%s misses %s" (`0x38`). No ammunition is spent. `Combat.WeatherPenalty` (`0x80062D78`) is `ThrowChance`'s, not this; `Combatant.HeightModifier` (`0x80062E0C`) has no caller.

**The action's states** (ticks are the frame's delta; the action timer is `+0x52CC`):

- **State 2** (`Combat.StateAction`, `0x80021774`) waits for the swing (`Combat.StepActionTimer`, `0x80021708`: the attacker's body's `+0x18` back to 0, and over 29 ticks). Then an arrow or thrown weapon flies (below), or the blow lands: with damage, `Combat.ApplyHit` (`0x80021930`) takes it through `Character.TakeDamage`, sets the target's request 7 and shows the number over its head (`Combat.ShowDamage`, `0x80021294`); with none, an opponent dodges (request 8) and an ally is helped (10, with healing's number); a dead target falls (`PlayDeathAnimation`, request 9 at once). Then state 4, with the queued line shown (`FlushQueuedLog`). So **the numbers come after the swing**: 111 ticks for Alaron's sword (request 12).
- **State 4** (`Combat.StateResult`, `0x80021F30`): `Combat.RemoveFallen` (`0x80021D2C`) waits for the target's body's `+0x18` to be 0 and 60 ticks (a rat's fall is 239), then frees a dead one's body (`Combat.FreeBody`, `0x80021C6C`: its effects and potions off, its numbers gone, its portrait skulled, `Combat.Bodies[slot]` emptied, the record kept), gives the camera back to the attacker and deselects the target. With no target left, the turn ends.
- **The one-shot requests**: after every frame `Combat.AfterFrame` (`0x80023360`) zeroes each body's request but 0–3, `0x14`–`0x16` and `0x19`, and `ProcessPlayers` copies the request to `+0x18` whenever the actor enters a state, so `+0x18` goes back to 0 when the swing or the reaction ends and the actor idles. A dead body (`+0x18` 9, its request no longer 9) holds its last frame (`+0x77C`), where `+0x18` takes 0.

**`Combatant.Death`** (`0x80062080`): the immobilising effects come off, its footprint leaves the grid, the active counts fall (`+0x02`, and `+0x04` or `+0x06`), Sholeh's death sets flag 622, a party member stops performing, and the first enemy's death sets `+0x08`. It doesn't set flag 6 or free the body: a dead combatant is known by its hit points. A death from the effects at a round's end (`Entity.ShowAgeResult`, `0x8007138C`) shows the number, then `Death` and `FreeBody` at once: no fall, no line, no wait.

**Arrows and thrown weapons** fly as the spells' cast scene (`Combat.CastScene`, `0x800DB0C8`, state 3). After the swing, `Combat.StartCastCamera` loads the arrow (Borg5 `0x348F`) for a bow, else the weapon's own model (`Combatant.LoadWeaponScene`, `0x80066CF8`), from the hand's locator to the target's middle (its feet plus half its height). It goes in a straight line in X and Z, `v` a tick, raised by sin(travelled / L × π) × `arc`, with `f = muzzle + (L ≤ 40 ? L/40 × 4 − 2 : 2)`, `v = f > 10 ? 0.5 : f ≥ 1 ? f × 0.05 : 0.05`, `a = 10 − muzzle + (L ≤ 40 ? L/40 × 8 − 4 : 4)` and `arc = a > 10 ? 7 : a ≥ 1 ? a × 0.7 : 0.7`, the muzzle number by weapon (`Combatant.WeaponMuzzle`, `0x80066DD0`, the combat record's `missiles`). It's scaled as the attacker and not turned along its way. The blow lands 30 ticks after it arrives (`Combat.EndCast`, `0x800219D8`). **`CombatAttackFx.Add` (`0x80089B80`) has no caller** in either build.

**The numbers over heads** (`CombatStatIndicator`, `0x8008A000`–`0x8008A8D4`): a pool of 16. Each is its number (a byte, 1–3 digits) put into a 64 × 16 texture from the digit textures (Borg1 22, 17, 21, 20, 15, 14, 19, 18, 13, 16 for 0–9, upside down), centred, at the body's X and Z and its head's height (half a unit higher for healing), and stays there. It's a billboard of 0.5 × 0.2 (healing 1.5 × 0.6), growing by s × scale across and s × 0.4 × scale up and rising s × 0.5 × scale, with s = sin(t / 120 × 90°), negative for healing, and scale its distance from the camera × 0.15; coloured (255, 255, 128) for damage and (128, 128, 255) for healing, its alpha `(u8)(base − (s8)(s × 255))` from 255 (0 for healing); gone at 120 ticks. Nothing is shown for a miss, a critical or a death.

**The target marks** (`CombatTargetMarks.Draw`, `0x80089830`) aren't a cursor. Every frame, in every state, each combatant selected in set A (set B in states 10 and 11) has its body's light colour go toward a grey of |sin(n × 3°)|, n its ticks marked (`Body.SetTint`, `0x8001371C`): a pulse from dark to full once a second, ending at once when it's deselected (`Body.ClearTint`, `0x80013774`). So the selected enemy pulses in state 1 as soon as one is in reach.

**Reserves** (`Combat.SpawnReserves`, `0x8005D914`, at a round's end after the effects age; running it on 08 gives 09's cells). It refills **every** enemy slot whose combatant is dead or gone, or that's empty, from the party's size to `+0x01`, while the list has a next (`+0x07`):

- A reserve that flees as it arrives (`EnemyAI.ReserveFleesOnArrival`, session 6) is marked and skipped, its slot left empty, with the hold's line.
- The leader's slot taken sets `+0x0C`.
- The newcomer stands at `Combat.ChooseSpawn` (`0x8005D748`, through `SpawnPosition`: one draw for its direction, `Range(5, 7)` with collision byte 0, else `Range(1, 3)`; set 0, the enemies' side, the slot's letter, `a` + slot − party size), or if that cell is taken, or a body's centre is within its radius and the newcomer's, the first free and clear point of sets 0–4 in turn, its side's letters first (`0x8005D5B0`, `0x8005D474`).
- Its body, radius, field, portrait and action are made as `Combat.CreateBody` makes them, with flag `0x400`, and `+0x07` moves on. The counts aren't changed: `CountActive` finds the newcomers at the next turn's change. Checkpoint 09's newcomers walking is their own AI turn, not the spawn's.

**The random stream between checkpoints**: besides the battle's own draws, `Weather.DriftWind` (`0x8007BAA0`, from `Combat.AfterFrame` through `World.SetTerrain` and `World.Update`) draws 2 every drawn frame, plus 3 with chance 0.05 and 1 more with 0.05. It accounts for every draw from 04 to 05 (1,907 in 873 frames). The attack's own draws between 05 and 06 can be counted but not placed from two states.

## The rest, by entry point

- **Targeting** (`TargetSet`, `0x80068FE0`–`0x8006A0B0`): set A, for attacks, is [An attack](#an-attack)'s. Set B asks `CanCast` (`0x80060C38`) of each combatant for the current one's selected spell; a spell's count is one, all, or half its level (`0x80060818`), with its kinds' rules (`0x8006088C`, `0x80060988`), range and sight (session 9).
- **Melee and ranged**: [An attack](#an-attack).
- **Spells, effects, potions and flasks** are [magic](magic.md)'s: casting (`Combatant.CastSpell`, `0x800648CC`, through `CheckSpellAvailable`, `PrepareCast`, `CastChance`, `ResistRoll` and `ApplySpellEffect`), the 15 timed effects and 7 potion records on the entity and their ageing by round and by trek's clock, Cheat Death, the kind lists (`Combat.DispelKinds`, `LimitedKinds`, the charms that count toward the disable limit, `ImmobilisingKinds`), potions drunk, and the flasks' damage and blast. The port's is `Battle.Cast`, `Battle.Strike`, `Battle.AgeEffects` and `Potions.Drink`. A spell's kind is its place in the spell table, not the combatant's `+0x27`, which holds a potion's.
- **The AI** (`EnemyAI.Step`, `0x8005B314`, each frame of state 8): flee, plan a path (`PlanPath`, `0x8005A974`), walk it (`WalkPath`, `0x8005A0D8`), or act on its target (`ChooseWeapon`, `Execute` `0x8005B594`). The target and the action come from one of 11 styles (`EnemyAI.StyleTable`; the rats' is 0), which score cells and action slots (`AiScores`, `aiscores.cpp`; `0x80057908`, `0x800581C8`, `0x80058C7C`, `EnemyAI.SpellValue` `0x80057498` with a case per spell kind) and take the best (`0x800590B0`). 13 entities get the boss style (`EnemyAI.HasBossStyle`, `0x8005BA40`). The walking states' stuck timer (`+0x4FBC`) ends a turn whose walk gets nowhere.
- **The GUI** (`combatgui.cpp`, `CombatGui.Create` `0x80085B80`) and **the radar** (`CombatRadar.Draw`, `0x8008DF04`): the portraits in their frames down the top left and right with their HP bars, tinted by the target sets, the log box (a `TextBand`, the cinematics' captions' widget) and the radar, centred on the camera's owner and turned with the camera. [ui](ui.md#the-battle-screen) has them, ported with session 4. C-left toggles a flag nothing reads.
- **Visuals**: the target marks, arrows in flight and the numbers over heads are [An attack](#an-attack)'s; the spells' are `CombatSpellFx.Frame` (`0x8008C754`) and `ProcessSpellVisuals` (`0x8008BCA0`), session 10's.
- **The camera** isn't trek's follow camera: `ProcessCombatCamera` (`0x8008D8BC`, from `ProcessPlayers` in mode 2) puts the camera in mode 4 and runs the interiors' `Camera::Mode1` (`0x800A69D8`) with combat's numbers on the body that owns it: near and far 4 and 10 (8 and 16 about the focus in states 11 and 18), the goal 2 above the aim, the aim 1.05 above the body, easing 1/16 a tick at most 1, the eye 1/32 at most 0.5. On its first frame it's put 8 behind the current party member (or the first combatant's body on an enemy's turn), away from the nearest enemy, 1.5 up (`CombatCamera.PlaceBehind`, `0x8008D78C`); a party member's turn puts only its goal there. Through an enemy's turn the eye goes back to its goal and stays, at least 4 from the aim. `CombatCamera.KeepInArena` (`0x8008D29C`) keeps the eye a unit above the floor. In state 9 with the target over 5 away, the eye goes 4 behind the attacker and 1.5 up (`CombatCamera.PullBack`, `0x8008D22C`). A spell's cast glides (`CombatCamera.StartGlide`, `0x8008D1F0`; `CombatCamera.CastGlide`, `0x8008D3C8`, checked in 08). No combat code sets the field of view or the clip planes. **Checked** on 02–04. The body it aims at is the camera's owner, which `GiveCameraToPlayer` makes the current combatant's at a turn's start and the cursor's target in states 9 and 10; so in state 9 the far branch puts the goal at the attacker's body and `PullBack` (`0x8008D22C`) raises it 1.5 and pushes it 4 farther from the aim, with Mode1's last flag clear, which skips its rise, its leash and its rescue alike. States 1, 5, 8, 24 and 26 have no branch of their own: the hold on an enemy's turn is by whose turn it is. R orbits it in state 1 as in trek.
- **Random numbers** are the one stream (`Rng.Range` `0x80093214`, its state at `0x800D8088`): the turn order, d100s, damage dice, 2d6 for morale and casting, and loot. The checkpoints hold the stream's state, so a battle's draws can be replayed from one (2.9).

## What it shares with trek

- **The zone engine**, in mode 1, framed each combat frame (`Combat.DrawFrame`) and freed after: the chunks, collision, scenes, lights, sky, rain and the terrain (`World.SetTerrain` every frame), with trek built again after. The port's `ZoneEngine` was made for an arena to be a stage as a cut-scene's is.
- **Bodies**: `AllocPlayer` and `FreePlayer`, `Body.SetDown`, `Body.Face`, the player handler with a goal for the AI's walks, shadows to 128, and the actors' requests (7 hit, 9 death).
- **The UI**: `TextBand`, the pause menu in modes 3, 5 and 6 (the flee option only in state 14), `MenuTween`.
- **Particles and lights**: actors' emitters, the party lit by the map's lights (`+0x70A`, [rendering](rendering.md)).
- **Scripts and flags**: the game over scripts, the encounter, boss and Sholeh's flags, the beaten monster party remembered, the Shadow's cinematic, the battle music with the map set.

**The combat strings** (String/Combat, 64 lines, [text](../formats/text.md)) are taken by byte offset: `0x00` "begins action", `0x04` the party flees, `0x08` victorious, `0x10`–`0x1C` healing, `0x20`–`0x38` avoid, attack, hit, critical, miss, `0x3C`–`0x4C` potions, `0x50` killed, `0x58`–`0x70` spells, `0x74` the Shadow, `0x78` flees, `0x7C` joins, `0x80` the vortex, `0x84` rests, `0x88` changes weapons, `0x8C`–`0xBC` a combatant's details (nothing in combat's files shows them), `0xC0`–`0xD0` performing, `0xD4` the party incapacitated, `0xD8` `flee%d`, `0xDC` `%s%u%c`, `0xE8` banished, `0xEC` "Enemy reinforcements flee", `0xF0` the Shadow dissipates, `0xF4` and `0xF8` `good` and `bad`, `0xFC` "cheats death".

## The port

`Aidyn.Core.Combat` ports what's above as far as sessions 2–5 and 8 of M3 went. Its API, for the sessions after:

- **`Battle`** is `Combat.Current` and its machine: `Begin(game, setup, arena, world)` builds one as `Combat.Init` and `Combat.Setup` do, and `Tick` steps it. It holds the `Arena`, the enemy list (`Enemies`, the field the first `EnemiesOnField`, `NextReserve` the next), the combatants by slot (`Slots`, `Combatants`; the party's from 0, `PartySize`), the `Leader`, `HasFleePoints`, the `Grid`, the `Order`, whose turn it is (`Current`), the camera's `Focus`, the `State` (`CombatState`, the 32) and `NextState`, `CanFlee`, the `Log`'s lines, `Problems`, the `Fade` and the camera (`CombatCamera`, `Camera`). `Pause` and `Unpause(flee)` are Start and the pause menu's close; `Over` and `Fled` say it has left; `Leave` is `Combat.Exit`'s part on the party.
- **`Combatant`** is the 0x8C record: its `Slot`, `Sheet` (a party member's own `Character`, or a fresh one from the entity's record for an enemy or Sholeh), `Look`, `Body`, `Flags` (`CombatantFlags`), `Party`, `Hostile`, `Controlled` (it has an AI record: the enemies and Sholeh), `Cell`, `TurnStart`, `Direction`, `Radius`, `Footprint`, `LastTarget`, and its body's sounds.
- **`TurnOrder`** builds the round (`Build`, `TurnsPerRound`) and says whose turn it is; **`ArenaGrid`** is the grid (`Init`, `Fill`, `Occupy`); **`CombatCamera`** is `ProcessCombatCamera`, on `FollowCamera.StepLeashed` (the interiors' camera with its numbers passed, `LeashNumbers`); **`Arena`** is an arena's chunk and map, with its points by name.
- **`BattleSetup`** is the setup block: `EnemyList` (`FillEnemies` for a monster party, a script's as given), `ChooseArena`, and the ambush and animation bytes. Trek cues a battle rather than handing it to the host (`TrekLoop.CueBattle`, `TrekLoop.QueueState(2)`'s fade at 1/30 a tick), and `GameSession` begins and ends it (`BeginBattle`, `EndBattle`) with the arena as a stage of trek's zone engine (`ZoneEngine.Stage`), its music held until the fade ends; its mode is `GameMode.Combat`, under the menu. The host hears `Transition.StartedBattle`, `PauseBattle` (with the Party tab and Flee from a turn's pause, the System tab alone from state 27's), `EndBattle` and `ResumeTrek`. `TrekLoop.Resume(fled)` puts the player at the farthest breadcrumb (`Breadcrumbs`).
- **Content** is records: the arena by terrain (`TerrainRecord.Arenas`), and `aidyn:combat/0` (`CombatRecord`): Sholeh, the bystanders an enemy list drops (`Combat.IsExcludedEnemy`, `0x8005C0C0`: the townsfolk, shopkeepers and the like, entity list places 0–0x56, 0x82–0x90 and 0x92–0x96), the Skeleton, the bosses who forbid fleeing and the Shadow who allows it, and the three encounter flags with the last word.
- **The states so far**: 31 fades in over a second, then 1 for a combatant the player controls or 8 for one the AI plays; 8 ends the turn at once, a stand-in until the AI; 1 is a turn the player walks in, skips or attacks in, and 9, 18, 6, 2, 3 and 4 the attack (below); Start from 1, 9 or 18 is 14, the pause with the Party tab and Flee, and from any other state 27, the System tab alone, each waiting two frames for the menu; the menu's close goes back to the turn (or 27's kept state), or with Flee to 20, which leaves. 12 and 13 leave for trek once faded out, a stand-in until session 7. The rest are sessions 6 on.
- **The battle screen** (session 4) is `CombatGui` (`Battle.Gui`), with the log's lines in `Battle.Lines` (`CombatLog`), the target sets' marks the tints read in `Battle.Marks(slot)` (`TargetMarks`, from `TargetsA` and `TargetsB`) and the AI's fleeing flag in `Combatant.Fleeing`: [ui](ui.md#the-battle-screen).
- **The runtime** draws the arena's chunks and sky, each combatant as a body with its actor idling (`CombatantView`, faded over combat's 128 and the party lit as the party), the camera and the fade, and opens the combat pause menu (`PauseMenu.OpenCombat`). `--battle <monster party>[,<arena>][,ambush]` starts one from the command line, and F3's overlay shows the battle's state and combatants.
- **Checked against the checkpoints** (`BattleCheckpointTests`): from checkpoint 01's random state, the port's setup of 02's battle (the ten rats, arena 3, an ambush, animation 2) draws the Ranger's roll (Sholeh's Ranger 6: a 12 against 69, step 3, a Stamina point), the eight directions and the turn order `[1,3,0,2, 3,2,1,5,4,7,0,6]` as the original did, and every combatant's slot, cell, direction, footprint, flags and body position, the leader, the flee bytes and the whole grid match 02's.

- **Turns** (session 3: `Battle.Turns.cs`, `Combatant.Turn.cs`, `CombatMarkers`). `Battle.Begin` ends with the first turn (`StartFirstTurn`), after each body has worked out its radius and taken its field. In state 1, `Battle.Tick` reads the controls first (`InputTurn`): R orbits the camera as trek's does, the stick drives the body (`Body.Drive`, trek's stick on a body that isn't trek's player), and the combatant reads it back (`Combatant.UpdateFromBody`, with the grid's `Vacate` and `Occupy` and `ArenaGrid.CellOf`). The bodies then step, each held in its field (`Body.Field`, `Body.KeepInField`) and pushed out of the others (`Crowd.Push`), then the camera and the state. `Battle.Skip` is Z (the runtime's `menu_tab_previous`: Z or the left trigger), and ends a turn the player plays at once. `Battle.EndTurn` ports `Combatant.EndTurn`, which goes on through `Advance`, `CheckEnd`, `EndRound` and `BeginTurn` as the original's does, with the clock on 600 (`GameState.AdvanceTurn`). State 8 still ends an enemy's turn at once, through `EndTurn`. The ring is `Battle.Ring` (`CombatMarkers.Update`, `Points`, `Count`, `RingRadius`), hidden while `Battle.ActionActive`. Who never moves is the combat record's `Rooted` (importer 40).
- **Seams for the sessions after:** `Battle.NearShadow` (false until session 12), `Combatant.StopPerforming` (session 9's Troubadour), and the places each method's remarks name: in `EndTurn` the AI's target, a spell chosen, and a flask put back; in `Advance` the reinforcements' line; in `BeginTurn` the AI's check to flee and a carried flask; in `EndRound` the Shadow's aura. The radius takes Haste and Stellar Gravity from the sheet's effects (`Combatant.RadiusWithEffects`), whether a combatant can act asks `Combatant.IsImmobilised`, and `EndRound` ages every combatant's effects (`Battle.AgeEffects`), all [magic](magic.md)'s. A win or a loss sets state 12 or 13 with the fade and the victory or the party's line, and until session 7, the battle then leaves for trek.
- **The runtime** draws the ring (`CombatRingView`: the pool's 20 copies, each with its own scrolling texture, faced to the camera), takes Z to skip, and F3's overlay shows the turn in the round, the field's radius, the ring, the moves and how far from its start the combatant stands.
- **Checked against the checkpoints:** every radius, field and the first ring in 02; ending Rheda's turn gives 03's Sholeh: the turn now, the states, her start, radius, field and ring, Rheda's field let go, and the clock 600 on (`RhedasTurnEndingBeginsSholehsAsTheCheckpointsNext`); and a round of twelve ended turns builds the next, of sixteen, with the toggle set.

- **Attacking** (session 5: `TargetSet`, `Battle.Attack.cs`, `Battle.Strikes.cs`, `Battle.Reserves.cs`, `Combatant.Action.cs`, `CombatNumbers`, `MissileFlight`), [An attack](#an-attack)'s:
  - **The sets**: `Battle.TargetsA` and `TargetsB` (`TargetSet`: `Rebuild`, `BeginAttack`, `MoveCursor`, `Select`, `Deselect`, `Candidates`, `Selected`, `Cursor`, `Order`), rebuilt at a turn's start and as the combatant moves (`Battle.RebuildTargets`); set B's `Rebuild` does nothing until session 9. `Battle.Marks(slot)` reads them for the tints. `Battle.CanAttack(a, t)` is `Combatant.CanAttack`, with `HasSight` and the grid's `WalkClear`, `HeightDifference`, `IsStep`, `IsOutside` and `IsOccupied`.
  - **The controls**: `Battle.Attack()` is state 1's A (state 9, or 18 for a combatant that's `Drawn`), `StepTarget(by)` the cursor (state 9's stick and D-pad, 18's C buttons), `Confirm()` A and `Cancel()` B; `Skip()` cancels while aiming, and `Pause()` from aiming comes back to the turn. State 18's free cursor is moved by the stick in `Tick`.
  - **The rules**: `MeleeChance`, `RangedChance`, `IsBehind`, `ShieldDefence`, `Protection`, `ApplyResistance`, `StrengthTier`, `AttackRequest`, and `ResolveMelee` and `ResolveRanged`, which draw on the combat stream, write the log's lines and return an `AttackOutcome` (chance, roll, adjusted, damage, critical). A hit's weapon spell is `Battle.Strike`'s, session 8's.
  - **The action**: `ExecuteAction(a, t)` (internal; state 6 calls it), `CheckDeath`, `Death`, `FreeBody`; states 2, 3 and 4 in `Tick` (`StepAction`, `StepFlight`, `StepResult`, `RemoveFallen`). `Combatant.Action` (`CombatAction`), `Drawn`, `Damage`, `Healed`, `Freed` and `Mark` (the target mark's pulse). A body's one-shot requests and `Body.Playing` (`+0x18`, which `IActorMotion.Entered` sets) are `Battle.AfterBodies`; a fallen body holds its last frame. `Battle.Target`, `LastAttack`, `FirstEnemyDied`, `Numbers` (`CombatNumbers`, the pool of 16) and `Flight` (`MissileFlight`).
  - **The camera**: `Battle.CameraOwner` is `GiveCameraToPlayer`'s owner, which `CombatCamera` follows and the radar centres on; state 9's far target pulls the eye back (`CombatCamera.PullBack`, with `StepLeashed`'s `rise` clear, which now also skips the leash and the rescue); `StartGlide` and `Reset` are the action's glide (`CastGlide`), which session 9's casts share.
  - **Reserves**: `Battle.SpawnReserves()` at a round's end, after the effects' deaths (their numbers shown, `Death` and `FreeBody` at once), with `ChooseSpawn`, `LeaderGone` and the newcomer's portrait (`CombatGui.Assign`).
  - **Content**: the combat record's `Missiles` (the weapons that fly: the arrow or the weapon's model, the muzzle's speed and turn) and `CloseRange` (Spikes), importer 42.
- **The runtime** takes A, B, the stick's or D-pad's left and right and C-left and C-right in a battle; draws a view for each body on the field (a reserve's as it comes, none once a body's gone), the numbers over heads (`CombatNumbersView`, the digit textures composed and billboarded), an arrow or thrown weapon in flight (`MissileView`), and the target mark as the body's lights scaled by its pulse. The debug key F4 and `--tints` went with the real sets. `--press` takes `wait:<seconds>` and `<action>:<seconds>` (held) to script a battle for the movie writer.
- **Checked against the checkpoints** (`AttackCheckpointTests`, over an overlay of a later checkpoint's combatants, stats, bodies and grid on 01's battle, itself checked against RAM): every melee chance between the eight combatants at 05 and the rat's row at 10; `ResolveMelee`'s rolls, damage, critical, lines and the stream after for seven seeds of the original's, and `ResolveRanged`'s with a bow; Alaron's kill of rat 7 from 05 (the counts, the queued "killed", the grid freed at once, the request, then through `Tick` the hit, the number, the fall and the freed body); `SpawnReserves` on 08 giving 09's cells; the cursor's order and start; and the last enemy's death winning the battle at the turn's change, with the line and the fade, then leaving. The values compared came from the original's own functions run on the checkpoints' RAM.

**Where the port differs:**

- The camera acts every tick rather than once a frame; only its goal and holds are once-a-frame things, so it's the same at 30 fps.
- A bystander in the first four leaves the original a null slot that it counts as an enemy on the field; the port leaves it out and says so. No monster party that fights has one.
- `Talk`'s collision byte isn't ported: a monster party used is collision 0, so its enemies face 5–7 even used from behind (1 would face them 1–3).
- Combat's frame counter's limit on pausing (`+0x5448 < 4`) isn't ported.
- The weapon in hand and its side (`+0x84`), the entities whose shadows are half (`0x8006C798`), the AI's area danger (`+0x12`) and the morale (`+0x11`) wait for their sessions.
- The ring starts each layout due +Z rather than a hair further round than the last (the original's statics drift by float error).
- A body keeps its cell while it hasn't moved: the port's bodies are in the world's coordinates and the original's in the arena chunk's, and taking the chunk's origin off loses a little.
- The action's timers are counted and tested every tick, not once a frame, and state 6's snap takes two ticks rather than two frames.
- An arrow leaves the attacker's middle (its feet plus half its height), not its hand's locator, which the core doesn't have.
- The glide aims by the attacker's entity height less 0.2 and its radius; the original's `EyeHeight` (`0x8006C748`) isn't read.
- The target mark scales the body's lights by its pulse; how the original's light colour is drawn isn't traced ([Not settled](#not-settled)).
- Held, the runtime's left and right repeat the cursor's step, as the menus' do; the original steps once a press.
- An enemy wields the first of its entity's weapons it can (`Character.Create`); the original takes the third slot's, else the first's (`0x8006E434`), which matters only for enemies with more than one (session 6).
- Sholeh's died flag at her death (622), the Goblin Ambush's hold on Alaron, a reserve fleeing as it arrives and the hold on its line are sessions 7, 12 and 6's.

## Not settled

- The AI's 11 styles, its scores, and the record's `+0x12` and `+0x13`.
- The popup at `+0x4FB8`, and what the three menu units draw beyond their roles.
- Exactly which cone `Talk`'s collision byte tests (`0x80010AE8`–`0x80010B74`).
- Whether the doubled gold and the loot in the experience pool show in play.
- `Combatant.EndTurn`'s item branch (`0x8006F5C4`): what it spends.
- Body flags `0x200` (set on enemies' bodies by `Combat.CreateBody`), `0x400` (skipped by the bodies' push) and `0x800` (`ProcessPlayers` calls `0x80015FEC`), which `TurnOrder.ClearActions` clears.
- Exactly where in 05 to 06 the attack's draws fall, which a watchpoint on `0x800D8088` from 05 would show.
- How the target mark's light colour reaches the body's drawing (`Bodies.SetLights`' use of `+0x770`), and how the healing numbers look in play (read as sinking, shrinking and fading in): [user-checks](../user-checks.md).
