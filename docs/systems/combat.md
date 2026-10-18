# Combat: battle mode (2.5, 2.6, M3)

This is the map of the original's combat that M3 is planned from ([roadmap](../roadmap.md#next)): its code, its states and what it keeps in RAM. Each part gets its detail as the session that ports it reads it. What the player sees is in [engine-behaviour](../engine-behaviour.md#battle-mode). [The port](#the-port) says what's ported so far and by which classes: a battle into its arena and out, with the combat camera and the pause menu's Flee.

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
- **`Combat.Init`** (`0x80022D1C`, on the first frame, while `0x800DB0A4` is 1): takes down trek's HUD, allocates the combat struct (`Combat.Reset`) and loads the combat strings, sets map set 16 (`0x800D8414`) and the arena's chunk (`0x800D8416`), and starts the zone engine in mode 1 (`InitZoneEngine(1, 0)`, `Zones.Mode`): trek's chunk streamer, collision, lights, sky and weather, over the Battle arenas, with the zone engine's point at (20, 20) in the arena chunk, the shadows' distance 128, and the battle's song chosen in `Zones.SetMapSet` (one `Below(5)` of the map set's five, or `Below(4)` of four without the Expansion Pak). `Combat.Setup` (`0x8005D15C`) fills the struct: the counts, the arena grid, a combatant and a body for each living party member and enemy on the field, Sholeh moved to the party's side if she's in the first four, the leader, whether there are flee points, the turn order and the flee rules by flag. Then the GUI, a body for each combatant (`Combat.CreateBody`, `0x8005C474`), the first turn's line, the camera on the current combatant's body (`Camera.SetEye`, `GiveCameraToPlayer`, `CombatCamera.Reset`), the ring and the effects, and state 31 with the fade in, a sixtieth a tick (`0x800CE308`). The Goblin Ambush's flag (978) makes it non-lethal, gives the Shadow two turns and heals the first member. Its `0x800E6850` and `0x800E6860` are `Combat.StateMoving`'s walk positions, not cameras.
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
- **The ring's teardrops** are drawn by code, not placed (`CombatMarkers`, `combatmarkers.cpp`): a pool of 20 copies of `aidyn:scene/0x0794` (`0x800854A8`), an unlit, blended billboard about 0.41 × 1.0 with a scrolling texture (Borg1 1937). `CombatMarkers.Update` (`0x80085548`) puts 6, 16 or 20 round the combatant at its field's radius by the radius (`0x8008580C`), each set on the ground by a ray (`0x80085240`), and doesn't draw them while an action plays (`0x800DB0CC`).
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
| `+0x4FC4`, `+0x50C4`, `+0x51C4` | three log lines of 0x100: shown, queued, and a third |
| `+0x52C4` | the log's flags; `+0x52C8`, `+0x52CC` its timers |
| `+0x52D0` | the 14 combatants |
| `+0x5318` | f32 x, z, the AI's goal; `+0x5320` its path, up to 144 cells as byte pairs |
| `+0x543C` | 1 when a queued line holds the frame; `+0x5440` the arena has flee points; `+0x5444`, `+0x5445` the last flask's cell; `+0x5448` the frame counter |

Beside it: the combatants' bodies, `Combat.Bodies` (`0x800D9488`, 14 by slot, 0x780 each), `Combat.State` and `Combat.NextState` (`0x800D9454`, `0x800D9455`), the Shadow's and Alaron's slots (`0x800D94CC`, `0x800D94CD`) and the AI's scores (`0x800E0BE0`).

### The combatant

| Offset | Holds | |
| --- | --- | --- |
| `+0x00`, `+0x04` | f32 x, z, in arena cells (Alaron starts at 52, 52) | checked |
| `+0x08`, `+0x0C` | f32 x, z at the turn's start | checked |
| `+0x10` | s32, the last target, −1 | checked |
| `+0x14` | direction 0–7, N to NW | checked |
| `+0x18`, `+0x1C` | f32 facing | checked |
| `+0x20` | its slot, its body in `Combat.Bodies` | checked |
| `+0x21` | the field's radius | checked: 9, 10, 6 |
| `+0x22` | acted or moved this turn | |
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

**A target set** (0xA8, `TargetSet`): `+0x00` 14 candidate flags, `+0x48` 14 selected, `+0x90` the candidates in the cursor's order, `+0xA2` the mode (0 attacks, 1 spells), `+0xA3` candidates, `+0xA4` selected, `+0xA5` the most that may be, `+0xA6` the cursor, `+0xA7` its place. Checked in 05: the cursor on combatant 7, one selected.

## The states

`Combat.State` (`0x800D9454`), 32 states in `Combat.StateTable` (`0x800DB0D4`); `NextState` (`0x800D9455`) is where a walking or turning state goes on to. Input is its own pass first each frame (`Combat.Controls`, `0x80083B00`, by state through `0x800850D4`). There's no machine of the turn's own: choosing, targeting, the menus, the pause and the AI's turn are all states.

| State | Function | What it is |
| --- | --- | --- |
| 0 | `Combat.StateEnd` `0x800216F4` | leave the game, mode 0 |
| 1 | `Combat.StateDraw` `0x80022860` | **a turn**: the stick moves the combatant in its field (`Combat.InputTurn`, `0x80084DB0`). A attacks (9, or 18 with a flask), Z or L skips, C-up and C-down open the menus (23, 22), Start pauses. An enemy's turn goes to 8 |
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
| 18 | `Combat.StateTargetThrown` `0x8002272C` | aiming a flask (`0x80084BA0`) |
| 19 | `Combat.StateEndTurn` `0x80022764` | ends the turn: a potion drunk in the pause menu |
| 20 | `Combat.StateLeaveFled` `0x800227B8` | leave, with `0x800D9376` set |
| 21 | `Combat.StateLeaveToScript` `0x800227D4` | leave for the game over script, mode `0xC` |
| 22, 23 | `Combat.StateMenuSpells` `0x800227E8`, `StateMenuItems` `0x80022844` | C-down's spells and skills, C-up's potions and item spells |
| 24, 26 | `Combat.StateRest` `0x80022898`, `0x800228F8` | 120 ticks, then the turn ends: nothing to do, or a performance begun |
| 25 | (draws) | the Troubadour's prompt, A to start (`0x8008508C`) |
| 27 | `Combat.StatePauseRequested` `0x80022930` | Start from any state, keeping it in `0x800D94D4` |
| 28 | `Combat.StateDefeatWait` `0x80022998` | 120 frames, then 13 |
| 29, 30 | `0x800229D8`, `0x80022A2C` | the Shadow vanishes without the Horn: text, fade, leave |
| 31 | `Combat.StateIntro` `0x80022A78` | the start: once the fade ends, 1 or 8 |

**Checked** along the battle: "X begins action" and state 1 or 8 (02–04); A, target set A and state 9 with the cursor on rat 7 (05); the turn, the attack, 2, then 4 with the rat dead (06); C-down, target set B and 10 (07), then 2 for the cast (08); reserves joining as the round's list is built again, through 5 and 8 (09); a rat's attack on Brenna in 2 (10); a fleeing rat walking in 5 with 8 next (11); and 12 once the last enemy's gone (12). 13 and 14 are mode 6 with the struct freed.

## A turn

1. **It begins** (`TurnOrder.BeginTurn`, `0x8006A9C8`): the turn's line, the camera, the radius (`Combatant.UpdateRadius`, `0x8005F29C`), the ring, the body set down, the clock on 600 (`World.AdvanceTurn`, [weather](weather.md)), the Shadow's aura, and an enemy's check to flee (`EnemyAI.CheckFlee`, `0x800572AC`).
2. **The field's radius** (`+0x21`) is the modified Dexterity's band in `Combat.RadiusByDexterity` (`0x800E0C20`: 3 for 1–3, 4 for 4–7, up to 13), plus Haste or less Slow, at most 13; but **1 while engaged**, an opponent within the larger of the two bodies' radii plus 0.75 (flag 7, checked on Brenna and a rat in 09 and 10), 1 while performing, 1 near the Shadow, and 0 for Pochanargat.
3. **The player** moves the body with the stick, trek's player handler moving it (`Combatant.UpdateFromBody` keeps the cell and the moved flag).
4. **The action** ends the turn: an attack, melee or ranged by the weapon (`Combatant.ResolveAction`, `0x80065ACC`); a flask thrown or a potion drunk (action 3: `ThrowFlask` `0x80065600`, `DrinkPotion` `0x80064C84`); a spell (`CastCurrentSpell`, `0x80065C08`); Lay On Hands or Heal With Herbs (`ResolveHealer`, `0x8006591C`); the Troubadour (`StartTroubadour`, `0x800662F4`); a weapon changed in the pause menu; or a skip. `Combatant.ExecuteAction` (`0x80065F6C`) ends a performance, carries it out, applies the damage, checks for death (`CheckDeath`, `0x80065E98`) and starts state 2.
5. **It ends** (`Combatant.EndTurn`, `0x80061300`): the flags cleared, a spell's choice cancelled, the weapon put back, then `TurnOrder.Advance` (`0x8006AC4C`) to the next combatant in the list that's alive and in play. When the list runs out, `TurnOrder.EndRound` (`0x8006A62C`) ages the effects a round, takes a Stamina point from each performer, brings in reserves, flips the toggle and builds the next list.

**Turn order** (`TurnOrder.Build`, `0x8006A16C`). A combatant's turns a round are by its **base** Dexterity (`Combatant.TurnsPerRound`, `0x8005F4D0`): 1–17 one, 18–24 one and two by turns (the toggle), 25–29 two, 30–34 two and three by turns, 35 and up three. The list is grouped: every third turn first, then every second, then every first, each group in random order (`Rng.Range`, drawing again on a repeat). Checked in 02: four party members and four rats gave `[1,3,0,2, 3,2,1,5,4,7,0,6]`.

**Win and loss** (`TurnOrder.CheckEnd`, `0x8006A97C`): no party member active is a loss (`TurnOrder.Defeat`: the party's line, state 13 or the non-lethal ending), and no enemy active with no reserve left a win (`TurnOrder.Victory`: `Combat.SetKillFlags` `0x80067860`, `Combat.TallyRewards` `0x8005E2AC`, state 12). Alaron's death is a loss alone (`TurnOrder.Advance`).

**Rewards** (`Combat.TallyRewards`): for every entry of the enemy list, its experience (the entity's `+0x7C`) and its chest (`+0x7E`, `Combat.AddChestLoot` `0x8005E0F0`: gold, drops, a random item, six slots), and the bonus loot. Each living member gets the whole pool (`0x80071528`). Read but not checked: the gold pool is doubled at the end, and looted items add to the experience pool.

**Morale and fleeing.** An enemy's morale (the AI's `+6`, from the entity's `+0x1B`) is changed by `EnemyAI.Morale` (`0x80056FA8`) as [engine-behaviour](../engine-behaviour.md#enemy-ai) has it. `EnemyAI.MayCheckMorale` (`0x8005722C`) holds the check back unless its Stamina's gone or half the enemies are; the roll is 2d6, and losing it is `EnemyAI.StartFleeing` (`0x8005B9E4`), which needs flee points and the enemy not engaged: four steps toward the nearest flee point (`0x8005AE3C`), then `Combatant.Leave` (`0x80061480`). A reserve can flee as it arrives (`EnemyAI.ReserveFleesOnArrival`, `0x80056E18`). Bosses can't (`Combat.NoFleeEntities`).

**Reserves** (`Combat.SpawnReserves`, `0x8005D914`, at a round's end, checked in 09): while fewer than four enemies are on the field, the list's next fill the free slots (a dead one's too) with flag `0x400` and "joins the battle", at the slot's own `bad0` point (`Combat.ChooseSpawn`, `0x8005D748`), or if it's taken, the first free of sets 0–4, their side's first (`0x8005D5B0`). Nothing picks the point farthest from the party.

## The rest, by entry point

- **Targeting** (`TargetSet`, `0x80068FE0`–`0x8006A0B0`): `Rebuild` (`0x80069264`) asks `Combatant.CanAttack` (`0x80060590`) or `CanCast` (`0x80060C38`) of each combatant and orders the candidates by angle and distance from the facing (`0x8006975C`); the controls move the cursor and the selection. A spell's count is one, all, or half its level (`0x80060818`), with its kinds' rules (`0x8006088C`, `0x80060988`), range and sight.
- **Melee and ranged**: `Combatant.MeleeChance` (`0x800622C4`); `AdjustRoll` (`0x80062594`): the d100 stands unless it's at or under the skill, capped at a fifth of the chance, when a second d100 comes off it; `ResolveMelee` (`0x800628A4`): a hit is the adjusted roll under the chance, and "scores a critical hit" when the roll was adjusted; `MeleeDamage` (`0x80062AE4`): the margin through the degree table (`0x800DA348`), the weapon's dice and the Strength tier's d6 (`0x800E0C30`), less protection (`0x8005F570`, `0x8005F71C`); `ApplyResistance` (`0x80062704`); the aspect's, Troubadour's and height's modifiers; `Combat.RangedChance` (`0x80062F50`, with `Combat.WeatherPenalty`, [weather](weather.md)); `ResolveRanged` (`0x80063324`); then skill experience (`0x80062650`).
- **Spells, effects, potions and flasks** are [magic](magic.md)'s: casting (`Combatant.CastSpell`, `0x800648CC`, through `CheckSpellAvailable`, `PrepareCast`, `CastChance`, `ResistRoll` and `ApplySpellEffect`), the 15 timed effects and 7 potion records on the entity and their ageing by round and by trek's clock, Cheat Death, the kind lists (`Combat.DispelKinds`, `LimitedKinds`, the charms that count toward the disable limit, `ImmobilisingKinds`), potions drunk, and the flasks' damage and blast. The port's is `Battle.Cast`, `Battle.Strike`, `Battle.AgeEffects` and `Potions.Drink`. A spell's kind is its place in the spell table, not the combatant's `+0x27`, which holds a potion's.
- **The AI** (`EnemyAI.Step`, `0x8005B314`, each frame of state 8): flee, plan a path (`PlanPath`, `0x8005A974`), walk it (`WalkPath`, `0x8005A0D8`), or act on its target (`ChooseWeapon`, `Execute` `0x8005B594`). The target and the action come from one of 11 styles (`EnemyAI.StyleTable`; the rats' is 0), which score cells and action slots (`AiScores`, `aiscores.cpp`; `0x80057908`, `0x800581C8`, `0x80058C7C`, `EnemyAI.SpellValue` `0x80057498` with a case per spell kind) and take the best (`0x800590B0`). 13 entities get the boss style (`EnemyAI.HasBossStyle`, `0x8005BA40`). The walking states' stuck timer (`+0x4FBC`) ends a turn whose walk gets nowhere.
- **The GUI** (`combatgui.cpp`, `CombatGui.Create` `0x80085B80`): the enemies' portraits in a column at x 21 and the party's at x 270, from y 18, 28 apart, then the rest as small icons 12 apart, each with an HP bar 3 wide; a reserve's portrait slides into a free slot, and the dead and fled are crossed out (`MarkDead`, `MarkFled`). Portraits are tinted by targeting (`TintByState`, `0x80086EB4`), and C-left toggles them. The log box (`0x80087A78`) is a `TextBand`, the conversation box's own, from (0, 180) to (246, 240), with the current combatant's portrait at (26, 180).
- **The radar** (`CombatRadar.Draw`, `0x8008DF04`) at (274, 199), in the frame image 0x1B: a dot for every body, the party green, enemies red, the current one white.
- **Visuals**: the target marks (`0x80089830`, a bobbing cursor over the candidates), an arrow or flask in flight (`CombatAttackFx.Add`, `0x80089B80`), the numbers over heads (`CombatStatIndicator`, `0x8008A128`), and the spells' (`CombatSpellFx.Frame` `0x8008C754`, `ProcessSpellVisuals` `0x8008BCA0`).
- **The camera** isn't trek's follow camera: `ProcessCombatCamera` (`0x8008D8BC`, from `ProcessPlayers` in mode 2) puts the camera in mode 4 and runs the interiors' `Camera::Mode1` (`0x800A69D8`) with combat's numbers on the body that owns it: near and far 4 and 10 (8 and 16 about the focus in states 11 and 18), the goal 2 above the aim, the aim 1.05 above the body, easing 1/16 a tick at most 1, the eye 1/32 at most 0.5. On its first frame it's put 8 behind the current party member (or the first combatant's body on an enemy's turn), away from the nearest enemy, 1.5 up (`CombatCamera.PlaceBehind`, `0x8008D78C`); a party member's turn puts only its goal there. Through an enemy's turn the eye goes back to its goal and stays, at least 4 from the aim. `CombatCamera.KeepInArena` (`0x8008D29C`) keeps the eye a unit above the floor. In state 9 with the target over 5 away, the eye goes 4 behind the attacker and 1.5 up (`CombatCamera.PullBack`, `0x8008D22C`). A spell's cast glides (`CombatCamera.StartGlide`, `0x8008D1F0`; `CombatCamera.CastGlide`, `0x8008D3C8`, checked in 08). No combat code sets the field of view or the clip planes. **Checked** on 02–04.
- **Random numbers** are the one stream (`Rng.Range` `0x80093214`, its state at `0x800D8088`): the turn order, d100s, damage dice, 2d6 for morale and casting, and loot. The checkpoints hold the stream's state, so a battle's draws can be replayed from one (2.9).

## What it shares with trek

- **The zone engine**, in mode 1, framed each combat frame (`Combat.DrawFrame`) and freed after: the chunks, collision, scenes, lights, sky, rain and the terrain (`World.SetTerrain` every frame), with trek built again after. The port's `ZoneEngine` was made for an arena to be a stage as a cut-scene's is.
- **Bodies**: `AllocPlayer` and `FreePlayer`, `Body.SetDown`, `Body.Face`, the player handler with a goal for the AI's walks, shadows to 128, and the actors' requests (7 hit, 9 death).
- **The UI**: `TextBand`, the pause menu in modes 3, 5 and 6 (the flee option only in state 14), `MenuTween`.
- **Particles and lights**: actors' emitters, the party lit by the map's lights (`+0x70A`, [rendering](rendering.md)).
- **Scripts and flags**: the game over scripts, the encounter, boss and Sholeh's flags, the beaten monster party remembered, the Shadow's cinematic, the battle music with the map set.

**The combat strings** (String/Combat, 64 lines, [text](../formats/text.md)) are taken by byte offset: `0x00` "begins action", `0x04` the party flees, `0x08` victorious, `0x10`–`0x1C` healing, `0x20`–`0x38` avoid, attack, hit, critical, miss, `0x3C`–`0x4C` potions, `0x50` killed, `0x58`–`0x70` spells, `0x74` the Shadow, `0x78` flees, `0x7C` joins, `0x80` the vortex, `0x84` rests, `0x88` changes weapons, `0x8C`–`0xBC` a combatant's details (nothing in combat's files shows them), `0xC0`–`0xD0` performing, `0xD4` the party incapacitated, `0xD8` `flee%d`, `0xDC` `%s%u%c`, `0xE8` banished, `0xEC` "Enemy reinforcements flee", `0xF0` the Shadow dissipates, `0xF4` and `0xF8` `good` and `bad`, `0xFC` "cheats death".

## The port

`Aidyn.Core.Combat` ports what's above as far as session 2 of M3 went. Its API, for the sessions after:

- **`Battle`** is `Combat.Current` and its machine: `Begin(game, setup, arena, world)` builds one as `Combat.Init` and `Combat.Setup` do, and `Tick` steps it. It holds the `Arena`, the enemy list (`Enemies`, the field the first `EnemiesOnField`, `NextReserve` the next), the combatants by slot (`Slots`, `Combatants`; the party's from 0, `PartySize`), the `Leader`, `HasFleePoints`, the `Grid`, the `Order`, whose turn it is (`Current`), the camera's `Focus`, the `State` (`CombatState`, the 32) and `NextState`, `CanFlee`, the `Log`'s lines, `Problems`, the `Fade` and the camera (`CombatCamera`, `Camera`). `Pause` and `Unpause(flee)` are Start and the pause menu's close; `Over` and `Fled` say it has left; `Leave` is `Combat.Exit`'s part on the party.
- **`Combatant`** is the 0x8C record: its `Slot`, `Sheet` (a party member's own `Character`, or a fresh one from the entity's record for an enemy or Sholeh), `Look`, `Body`, `Flags` (`CombatantFlags`), `Party`, `Hostile`, `Controlled` (it has an AI record: the enemies and Sholeh), `Cell`, `TurnStart`, `Direction`, `Radius`, `Footprint`, `LastTarget`, and its body's sounds.
- **`TurnOrder`** builds the round (`Build`, `TurnsPerRound`) and says whose turn it is; **`ArenaGrid`** is the grid (`Init`, `Fill`, `Occupy`); **`CombatCamera`** is `ProcessCombatCamera`, on `FollowCamera.StepLeashed` (the interiors' camera with its numbers passed, `LeashNumbers`); **`Arena`** is an arena's chunk and map, with its points by name.
- **`BattleSetup`** is the setup block: `EnemyList` (`FillEnemies` for a monster party, a script's as given), `ChooseArena`, and the ambush and animation bytes. Trek cues a battle rather than handing it to the host (`TrekLoop.CueBattle`, `TrekLoop.QueueState(2)`'s fade at 1/30 a tick), and `GameSession` begins and ends it (`BeginBattle`, `EndBattle`) with the arena as a stage of trek's zone engine (`ZoneEngine.Stage`), its music held until the fade ends; its mode is `GameMode.Combat`, under the menu. The host hears `Transition.StartedBattle`, `PauseBattle` (with the Party tab and Flee from a turn's pause, the System tab alone from state 27's), `EndBattle` and `ResumeTrek`. `TrekLoop.Resume(fled)` puts the player at the farthest breadcrumb (`Breadcrumbs`).
- **Content** is records: the arena by terrain (`TerrainRecord.Arenas`), and `aidyn:combat/0` (`CombatRecord`): Sholeh, the bystanders an enemy list drops (`Combat.IsExcludedEnemy`, `0x8005C0C0`: the townsfolk, shopkeepers and the like, entity list places 0–0x56, 0x82–0x90 and 0x92–0x96), the Skeleton, the bosses who forbid fleeing and the Shadow who allows it, and the three encounter flags with the last word.
- **The states so far**: 31 fades in over a second, then 1 for a combatant the player controls or 8 for one the AI plays; 8 ends the turn at once, a stand-in until the AI, passing to the next in the round and building another when it runs out; 1 does nothing but Start; Start from 1 is 14, the pause with the Party tab and Flee, and from any other state 27, the System tab alone, each waiting two frames for the menu; the menu's close goes back to the turn (or 27's kept state), or with Flee to 20, which leaves. The rest are sessions 3 on.
- **The runtime** draws the arena's chunks and sky, each combatant as a body with its actor idling (`CombatantView`, faded over combat's 128 and the party lit as the party), the camera and the fade, and opens the combat pause menu (`PauseMenu.OpenCombat`). `--battle <monster party>[,<arena>][,ambush]` starts one from the command line, and F3's overlay shows the battle's state and combatants.
- **Checked against the checkpoints** (`BattleCheckpointTests`): from checkpoint 01's random state, the port's setup of 02's battle (the ten rats, arena 3, an ambush, animation 2) draws the Ranger's roll (Sholeh's Ranger 6: a 12 against 69, step 3, a Stamina point), the eight directions and the turn order `[1,3,0,2, 3,2,1,5,4,7,0,6]` as the original did, and every combatant's slot, cell, direction, footprint, flags and body position, the leader, the flee bytes and the whole grid match 02's.

**Where the port differs:**

- The camera acts every tick rather than once a frame; only its goal and holds are once-a-frame things, so it's the same at 30 fps.
- A bystander in the first four leaves the original a null slot that it counts as an enemy on the field; the port leaves it out and says so. No monster party that fights has one.
- `Talk`'s collision byte isn't ported: a monster party used is collision 0, so its enemies face 5–7 even used from behind (1 would face them 1–3).
- Combat's frame counter's limit on pausing (`+0x5448 < 4`) isn't ported.
- The weapon in hand and its side (`+0x84`), the entities whose shadows are half (`0x8006C798`), the AI's area danger (`+0x12`) and the morale (`+0x11`) wait for their sessions.

## Not settled

- The AI's 11 styles, its scores, and the record's `+0x12` and `+0x13`.
- The popup at `+0x4FB8`, and what the three menu units draw beyond their roles.
- Exactly which cone `Talk`'s collision byte tests (`0x80010AE8`–`0x80010B74`).
- Whether the doubled gold and the loot in the experience pool show in play.
- Which targeting state tints a portrait dim red.
