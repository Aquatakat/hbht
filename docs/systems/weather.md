# Weather

How the weather changes, what it holds, and how the original draws and plays it. The rules are ported (`Aidyn.Core.Calendar.Weather`), and so is the look, the rain and snow among them (`Aidyn.Core.Particles.WeatherParticles`), but for rain and snow outside trek ([in the port](#in-the-port), [to port](#to-port)). Everything here was read from 1.0's machine code (Oct 17, 2026), with addresses in 1.0 unless marked, and the values checked against the checkpoints. The docs' earlier account, from the decomp's names, had the odds and the wind wrong.

## The records

- **The weather record** (`aidyn:weather/0`, `WeatherRecord`): each season's chance of precipitation, from the code's table (`0x800e3040`, ROM `0xE3C40`: 43, 48, 33, 25, 36, 40 for the six seasons, and two bytes of padding), and the literals of the roll, the wind and the drawing, carried as facts (`BaseImporter.BuildWeather`).
- **The terrain records** (`aidyn:terrain/n`): what each terrain adds to the season's chance (`precipitation`, s8s at `0x800e3048`), how hard it falls there (`precipitationScale`, floats at `0x800e3064`) and whether it's snow (`snow`, terrain 5 alone, a literal in the roll). The tables are identical in 1.0, 1.1 and the debug ROM (`WeatherTables`).

| Terrain | 0–2 | 3 | 4 | 5 | 6 | 7 | 8–17 | 18 | 19–22 | 23 | 24 | 25 | 26–27 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Chance added | 0 | −100 | 5 | 30 | 0 | 15 | 0, but 10 is 20 | 100 | 0 | 30 | 15 | 0 | −100 |
| Scale | 0.5, 0.5, 0.4 | 0 | 0.7 | 0.6 | 0.5 | 0.8 | 0.2 | 0.8 | 0.2 | 0.8 | 0.6 | 0.2 | 0 |

Terrains 3, 26 and 27 never get precipitation; terrain 18, in the Battlefields and one interior, always does when the weather changes.

## The state

Two structs hold it.

- **The world** (`World`, 0x38 bytes behind `TerrainPointer`, `0x800E303C`; debug `0x800F1BBC`): the calendar, the terrain and the weather as rolled. +0x2 the part of the day as the weather last saw it; +0x4 the class of bad weather combat reads (0 none, 1 fog, 2 storm: `WeatherKind`); +0x5 a lock on the part of the day, which combat's Day and Night spells raise; +0x6 what falls (0 none, 1 rain, 2 snow: `Precipitation`); +0x8 its intensity; +0xC the fog's thickness; +0x10 the storm, the lightning's chance; +0x14 the wind, a vector; +0x24 the terrain. (+0x3 is the moon's phase, +0x20 the clock, +0x28–0x30 the sun's and moon's offset, +0x34 time played.)
- **What's drawn** (`Weather.Data`, `gGlobals` + 0x394: `0x800D841C`; debug `0x800E6C3C`, 0x24 bytes, cleared by `InitZoneEngine` through `Weather.Init`, `0x8001e010`): +0x0 what the world wants falling, +0x2 what's falling, +0x4 how hard (`FallIntensity`), +0xC the fog as drawn (`fogTime`), +0x10 the sky to go back to, +0x14 the precipitation's emitter, +0x18–0x20 the rain loop's sound.

The port's `Weather` holds both, and the cloud cover isn't weather: it's set to 1 once (`0x80027390`) and nothing changes it.

## When it changes

`World.Update` (`0x8007bf5c`) runs on every frame of the clock: the calendar (`World.GetDate`, `0x8007ba30`), the moon's phase (`0x8007bc70`), the part of the day (`World.UpdatePartOfDay`, `0x8007bd3c`), which rolls the weather when it has changed, the wind's drift, then the calendar's flags (`StateFlags.SetTime`, `0x8001fdec`), the rain flags (`StateFlags.SetRain`, `0x8001fe9c`) and the sun's offset. The weather rolls:

- **When the part of the day changes**, at 6:00, 9:00, 12:00, 18:00 and 21:00, as trek's clock passes them (`TrekLoop.TickClock`, `0x80020cb4`, then `GameClock.Step`, `0x8007c054`). Afternoon runs to 18:00 in both builds, not 17:00, which the port had wrong for the flags.
- **On entering a chunk of another terrain** (`World::SetTerrain`, `0x8007b828`): it moves the part of the day it remembers on by one, so `World.Update` sees a change and rolls. A chunk of the same terrain rolls nothing. That covers walking, doors, cut-scenes staged elsewhere and combat's arenas.
- **After camping** (`GameState.Lapse`, `0x8007c098`, eight hours), once, if the part of the day is another.
- **At a new game** (`World.Init`, `0x8007b7a0`, then `World.SetDate`, `0x8007b914`, at noon): the world starts at part 0 and terrain 0, so setting noon is a change, and the weather may change by season 0's and terrain 0's chances before the first chunk's terrain rolls again.
- **Each combatant's turn** moves the clock on by 600 (`World.AdvanceTurn`, `0x8007c01c`), which can roll. Not ported: combat.
- **Nothing else.** No script command, map object or flag sets the weather. The end of a Wind enchantment (effect 0xB in `0x800707ec`) sets only the world's +0x4 to fog, the class combat reads. Not ported: spells.

While the lock (+0x5) is up, the part of the day doesn't change and the weather doesn't roll. Not ported: the spells.

## The roll

`Weather.Roll` (`0x8007bdb0`). Every die is `RollD(1, 100)`, 1 to 100:

1. At most 75 (`KeepAtMost`), nothing changes. So three times in four the weather stays as it was.
2. Otherwise it clears, and a second die at most the season's chance plus the terrain's (an s16 sum) brings precipitation.
3. Then a third under 70 (`FallBelow`) makes it rain, or snow on terrain 5, at the terrain's scale give or take 0.15 (`IntensitySpread`), at least 0, with fog from 0.1 to 0.35 (`FallFog`): class 2. 69% of the time.
4. Otherwise it's fog from 0.1 to 0.75 (`Fog`), with nothing falling: class 1.
5. The storm is a share of the rain's intensity, from 0 to it; snow and fog have none.

Every range is `ParticleHead.FloatRange` (`0x80093470`): a share from 0 to 1 times the span, from the lower; equal bounds draw nothing. The draws go: the first die; then the second; then the third, the intensity (rain or snow), the fog; then the storm (rain). The port rolls on the world's stream (ADR 0006). The checkpoints' one rain, before Rooughah, fits: terrain 8's scale of 0.2 gives 0.261, with fog 0.182 and a storm of 0.101.

## Over time

`ProcessWeather` (`0x8001e7c0`) runs every frame trek or a cut-scene draws (`HandleZoneEngineFrame`, `0x8000e784`, which a cut-scene's mode calls through `0x80050f50`), whatever stops the clock, before `TrekLoop.TickClock`, so a roll shows from the next frame. Its rates are per tick, times the frame's ticks, so the port steps it every tick (`Weather.Draw`):

- **What falls** sets in and dies away. When what's falling isn't what the world wants, it dies away at 1/300 a tick, and once it's at 0, what the world wants starts. While they agree and something falls, it rises at 1/300 a tick to the world's intensity, at most 1, and goes straight to the intensity when it's over. So rain takes up to five seconds to come and as long to go, and rain turning to snow dies away first.
- **The fog** (`fogTime`) moves toward the world's at 1/900 a tick, and takes it once within 0.01: 15 seconds from none to 0.75.
- **Where the sky isn't drawn** (sky type 4: terrains 6, 8, 9, 11 and 13–16), the fog is 0, the emitter and the rain loop are freed every frame, but the world's weather and the rain flags carry on. Coming into a chunk with the sky from one without, the fog is the world's at once (`LoadGameBorgScenes`, `0x8000b05c`).
- **When trek's world is built again** (`InitZoneEngine`: after a load, a cut-scene or combat), what's drawn starts from nothing: the rain sets in again from 0 and the fog is there at once (`Weather.StartDrawing`).

## The wind

`Weather.DriftWind` (`0x8007baa0`), once a frame of the clock from `World.Update`, with 0.05 for both chances, whatever the weather. The weather doesn't change it.

- On a share at most 0.05, the direction moves by −0.1 to 0.1 in X and Z and −0.004 to 0.001 in Y, and is normalised.
- On another, the speed moves by up to 1.5/648 either way, from 0 to 150/648 (`WindMaxSpeed`). A new game's is (1, −0.1, 1) normalised at 15/648.
- It blows the clouds (4 × it a frame) and the precipitation's emitter. In the checkpoints it has drifted to 0.021–0.053.

The port draws the drift from the cosmetic stream, every second tick (`Cadence.FrameTicks`), as the original draws it once a frame.

## Lightning

`RenderSky` (`0x8001d840`), in an outdoor sky only, counts its frames to 20 (`0x800DAED4`). While the storm is above 0 it draws a share from 0 to 1 each frame, and on the twentieth frame a share under the storm × 0.25 (`LightningShare`) strikes: the screen flashes and one of the four thunder sounds plays, `Range(0, 3)` (`Weather.SkyFrame`, on the cosmetic stream). See [how it's drawn](#lightning-and-thunder) for the flash and the sounds.

## Flags, saves and other readers

- **The rain flags** (2319–2320) hold what the world has falling, written every frame of the clock and when the weather is restored: 1 rain, 2 snow, 0 for clear and for fog ([flags](flags.md)).
- **Saving.** The original saves a snapshot taken as the pause menu opens (`Weather.Snapshot`, `0x8007bc4c`, from `Weather.Pause`, `0x8001a238`): what falls, its intensity, the fog and the storm, each × 1000 as a whole number (`SaveBlock.Write`, `0x80019810`). It doesn't save the class combat reads (+0x4), the wind or what's drawn. Loading restores the four (`Weather.Restore`, `0x8007bbfc`, then again after trek's world is built, `Weather.Resume`, `0x8001a4f0`), and writes the drawn intensity and the fog straight in, so the fog is there at once. The port's save keeps all of it, the class and the wind among them ([saves](saves.md#the-format)).
- **The wanderers** see less in fog: their perception × (1 − `fogTime` × 0.75), the drawn fog (`0x8000f6a4`).
- **Combat** (not ported): a thrown potion's chance (`Combat.WeatherPenalty`, `0x80062d78`) loses 30 at night, 20 in a storm (class 2) and 10 in fog (class 1), and in bad weather another (distance − 5) beyond 6, on top of the same distance penalty its caller already took; a ranged attack's chance (`Combat.RangedChance`, `0x80062f50`) is × (1 − the intensity × 0.3), and × 0.7 at night; a melee one × 0.9 at night.

## Differences from the original

- **One stream.** The original rolls the weather, drifts the wind and flashes the lightning from its one stream. The port rolls on the world's and draws the rest from the cosmetic one (ADR 0006).
- **Loading.** The port's save keeps the class, the wind and what's drawn, so a loaded game's weather looks as it did. In the original the class is whatever the fresh world's rolls left (the checkpoints show it stale: class 2 with nothing falling, and 0 in the rain), the wind starts again at a new game's, and by the code, rain that was falling dies away and sets in again (inferred, [to check](../to-check.md)). The original's fresh world also remembers noon's part of the day, so where the first chunk's terrain is 0, the first frame may roll; the port takes the loaded time's and rolls nothing.

## How the original draws it

From 1.0's machine code (a research run, Oct 17, 2026, and the sky's rules read again for the port the same day; the constants of `ProcessWeather`, the drops' callbacks, the lightning and `SetSceneColors` checked in the disassembly, the particles' display lists decoded by hand from the words they store). `HandleZoneEngineFrame` (`0x8000e784`) draws, in trek and in cut-scenes, in order: `RenderSky` (`0x8001d840`) with its lightning, the sun, moon and stars and the clouds while the sky is drawn, the scenery and bodies, then the particles (`ProcessAndRenderParticleHead`, `0x800a842c`, on `Particles.Head`, `0x800d84b8`), then `ProcessWeather`. So rain and snow are drawn last, before the HUD.

### Rain and snow

They aren't drawn by weather code of their own: `ProcessWeather` makes two emitters of the game's particle system (`WeatherParticles.Make`, `0x8001e62c`) and steers them each frame. `Weather.Data` + 0x14 holds the main one, B, the drops; the second, A, is for splashes. Ported (`Aidyn.Core.Particles.WeatherParticles`, which trek steps every tick before `Weather.Draw`, in the runtime's particle pool; read again from 1.0's machine code, Oct 17, 2026, every constant below checked in the disassembly).

- **When.** While what's falling is what the world wants and isn't nothing, the sky is drawn and there's no emitter, the pair is made, and the sky changes to [the rain sky](#the-rain-sky). When what's falling isn't what the world wants, the emitter is forgotten, not freed, and runs out its life; each frame both emitters' lives are set to 300 while it falls.
- **Rain**: colour (0.546875, 0.7421875, 0.8203125, 0.8) (`0x800ce0d0`), bytes (139, 189, 209, 204); drops of particle texture slot 4 (Borg1 2, an I4 8 × 8 streak two texels wide, intensities 102, 221, 221, 170, 153, 119, 85, 17 from the top), splashes of slot 16 (Borg1 12, an I8 16 × 16 ripple), and ambient splashes. **Snow**: white (1, 1, 1, 1) (`0x800ce0e0`), flakes of slot 0 (Borg1 3, an I4 16 × 16 soft disc), which land as discs of the same; no ambient splashes. The slots are the particle textures (`partTextInds`, ROM `0xCE460`). The importer carries all of this, and the fall, intervals and lives below, on the weather record (`Rain`, `Snow`: `PrecipitationLook`, importer 31).
- **The pair** (`WeatherParticles.Make`, `0x8001e62c`): A first, life 60, its head's vector (−1/120, −1/120, −1/120) shrinking every particle in its list (flag 0x400), spawning ambient splashes for rain and nothing for snow; then B, life 60, its head's vector moving every drop (flag 0x80), spawning drops and stepping them with `WeatherParticles.DropStep`. Each holds the other (`+0x1C8`). Without room for B, A is freed (`Particles.KillEmitter`) and nothing is made that frame. B's centre starts at 7 above the aim.
- **Each frame**, B's spread is 20, its centre 5 ahead of the camera's aim point (`0x800d82a8`) along the camera's level look (`FollowCamera.LookXZ`, `0x800d82cc`), and 7 − (I − 2) = 9 − I above it, where I is the drawn intensity, before this frame's change. Its fall is the wind, plus 0.075 a tick down for rain, or 0.02 × I + 0.03 for snow (doubles). Rain spawns a drop every `(short)(6 − 4 I)` ticks' worth of frames and lives 240 ticks, its splashes every 2 fewer; snow every `(short)(6 − 7 I)`, living `(short)(360 − 60 I)`; snow's A keeps its interval of 0 and has no spawn. The intervals are set even on the frame the pair is made.
- **Spawning** (`ProcessAndRenderParticleHead`, once a frame per emitter): an emitter's counter loses the frame's ticks, and at 0 or below it's set to the interval and the spawn callback runs once, or with an interval under 0, that many times. An interval of 0 spawns once every frame, which the port's particle pool had as never, and no Borg5 emitter reaches (their start gives an interval of 0 no spawn). So at 30 fps, rain makes about 7.5 drops a second at I = 0.25, 10 at 0.5 and 15 at 0.95, and ambient splashes 10, 15 and 30; snow 10 flakes a second at 0.25, 15 at 0.5 and 30 from 0.75. Nothing caps them but the pool: 16 emitters and 384 particles with the Expansion Pak (64 in trek without).
- **A drop** (`WeatherParticles.SpawnDrop`, `0x8001e444`) starts at a random distance from 0.01 to the spread (20) from B's centre in a random level direction (`ParticleHead.Direction2`, `0x800934dc`: a point in the square from −1 to 1, normalised), at scale 0.25, in B's colour, its vector the colour × −0.75 / its life (flag 0x200), so it fades to a quarter of its colour, alpha too. It sweeps a sphere of radius 0.1 from where it starts along its fall × its life, past triangles flagged 0x200 (`SpherePhysics.Sweep`, `0x800a42e8`, with its last argument 1), and keeps where that hits in its `+0x4C` (`Particle.Data`), with 100000 in W; without a hit, (0, −1000, 0).
- **Landing** (`WeatherParticles.DropStep`, `0x8001e138`, after each step of a live drop): while it's nearer to that point than ever, by the square of the distance, it notes how near. Once it's no nearer, each frame it sweeps from 5 above it to 5 below, past 0x200 triangles, and on a hit it's moved into A's list (`ParticleHead.MoveParticle`, `0x800a8b14`) and becomes a splash there (`WeatherParticles.MakeSplash`, `0x8001e04c`): 0.05 along the ground's normal, laid flat on it (its own matrix, flag 0x4, from `guAlign` of the normal with 10⁻⁶ added to its X), at 3 × the scale (0.75), its vector its colour × −1/120 so it fades to black over its new life of 120, while A's head shrinks it by 1/120 a tick to nothing at 90. Without a hit it tries again every frame, and lives out its life. If A has been freed, as a forgotten pair's can be before the last of B's drops lands, the drop is freed (`ParticleHead.Link` finds no head).
- **Ambient splashes** (rain only, `WeatherParticles.SpawnSplash`, `0x8001e274`, A's spawn): a point from 0.01 to half the spread (10) from B's centre, a sweep from 50 above it to 50 below, not skipping 0x200 triangles, and on a hit a particle in A's list made as a drop is and laid as a splash.
- **Motion.** Each tick a drop moves by its emitter's fall (`ParticleHead.ApplyVector`, `0x800a7fa0`): rain at 4.5 units a second. The wind slants the paths; the sprites never turn with it.
- **The sprite** (`Particles.DrawPrologue` `0x800a8f74`, `Particles.DrawMode` `0x800a91dc`, `ParticleHead.Draw` `0x800a92fc`): a textured quad, 0.3535 × the scale either side of its middle (a drop 0.177 across, a splash 0.53), facing the camera's look and upright (built from the look by `guAlign` with no roll), and a splash facing the ground's normal. The texture covers the quad once, its first row at the bottom, so a rain streak's bright head leads. Its vertices' colour is the particle's × 255 × the screen's brightness (`0x800da0c8`), so particles dim with fades. One cycle, `(TEXEL0 − 0) × SHADE + 0` for colour and alpha (`0xFC121824 FF33FFFF`), `G_RM_ZB_XLU_SURF` in both cycles (`0x00504A50`): depth tested, not written, blended normally. Smooth shade, **no fog**, bilinear, noise dither. An I texture gives its intensity as colour and alpha. The runtime's `ParticlesView` draws it so ([rendering](rendering.md#particles)).
- **Randomness.** The original draws three from its one stream (`gGlobals`, `0x800d8088`) for each drop and ambient splash. The port draws them from the cosmetic stream (ADR 0006).
- **A teleport** through a door (`TeleportPlayer`, `0x8000bb54`, given no position) forgets the pair (`0x800d8430` = 0) and then kills every emitter still spawning (`ParticleHead.KillActive`), the pair among them, so the next frame makes a new one about the player: there's no stale emitter. Walking into another chunk calls it with a position, which kills nothing, and moves every particle by the shift of the world's origin, and B's drops' landing points too (`Particles.Shift`, `0x80017918`); the port's world doesn't move, so it has nothing to shift. The zone engine's end frees the pool, and `Weather.Init` forgets the pair; the port frees the pair as trek's world goes (`TrekLoop.Free`).

#### Does it obey collision?

Yes, the collision of the chunk the player is in (`Zones.PlayerMap`, `0x800d8288`), not the chunks around it: the port tests the loaded chunk the 3×3 is centred on (`ChunkStreamer.Centre`), as the original does, so near a chunk's edge the drops falling over the next chunk find no ground, fall through it unseen (they're depth tested) and make no splash. The drops' sweeps pass see-through (0x200) triangles, the invisible walls; rain's own splashes land on them. Every sweep takes the first triangle facing it in the collision grid's order, not the nearest, so where one floor is over another a splash can land on either.

A wall only delays a drop: its sweep stops at the wall, and once past that point the drop finds the ground from 5 above to 5 below, wherever it has fallen to, or falls on. **Roofs** are in the collision only where the player can stand on them, and none of Gwernia's are: no collision triangle facing up in M07 or I08 stands above another (a script over the base plugin), so the rain falls through the houses' roofs, hidden by them, and splashes on the ground under them, unseen. Over the Overworld, 508 triangles facing up stand 2.5 or more above other ground, in 49 chunks (A05, D23, E23, P26, P27, Q16, Q17 and Q26 have the most: ledges and bridges), and 31 of them are see-through, which only rain's own splashes land on.

#### More rain than the original

The options menu's **Rain and snow** (`Settings.PrecipitationDensity`, `--precipitation <scale>` for a run) multiplies how many drops and ambient splashes each spawn makes: Original (1×), 2x, 4x and 8x. Their look, the spread and the rates are the original's; a fraction carries to the next spawn (`WeatherParticles.Density`). The pool hands out the original's 384 particles times the density, at least 384, from room for 8 × 384 (`ParticleHead.MaxParticles`, `Capacity`), so at 1× the pool caps the rain where the original's would. The port's pool has no Expansion Pak to lose, so it never drops to 64.

The spread needs no widening for 16:9. The drops fall from a disc of radius 20 about a point some 10 ahead of the camera, which spans the view's width out to about 22 units at 16:9 (25 at 4:3); what limits the rain on screen is the height they fall from, 9 − I above the aim, above the top of the view near the camera. At 1280 × 720 against 1280 × 960, snow at 0.75 and 8x showed 179 of its 700 flakes on screen against 137.

**Measured** in Gwernia (`--chunk M07 --at 40,55`, standing, 8 s in, the F3 overlay's count of what the last frame drew, and how much of it was on screen):

| Weather | Density | Drops (on screen) | Splashes (on screen) | Particles in use |
| --- | --- | --- | --- | --- |
| Rain 0.5 | 1× | 19 (5) | 48 (26) | 70 of 384 |
| Rain 0.5 | 4x | 86 (23) | 189 (119) | 281 of 1536 |
| Rain 0.95 | 1× | 29 (4) | 84 (63) | 117 of 384 |
| Rain 0.95 | 8x | 237 (67) | 675 (439) | 930 of 3072 |
| Snow 0.75 | 1× | 80 (16) | 56 (15) | 139 of 384 |
| Snow 0.75 | 8x | 700 (137) | 489 (143) | 1199 of 3072 |

The original's rain is sparse: a few streaks on screen, each 0.177 across, which at 10 away is 5 lines of the N64's 240 and a texel or two wide, and the ripples show more than the drops. A streak in the runtime at 0.95 and 8x peaked at (57, 91, 108) over sea at (33, 64, 85), which is what the code gives a drop about 100 ticks old (its colour (139, 189, 209) × the texel's 221 and the fade's 0.69, at alpha 0.8 × the same), its bright end at the bottom. Snow's flakes are white discs; where they land they fade to black as they shrink, so they hardly show on the ground.

### Where the sky isn't drawn

Sky type 4 (terrains 6, 8, 9, 11 and 13–16): nothing falls (the emitters are freed every frame), the rain loop stops at once, the weather's fog is 0 (the scenery keeps its 996–1000 in the terrain's fog colour), and there's no lightning. The world's weather and the drawn intensity carry on, so the rain is there as before outside, and `LoadGameBorgScenes` changes the sky to sky 2 at once on the way out if it rains or snows, and takes the fog at once.

### The fog

`fogTime` (`Weather.Data` + 0xC, `0x800d8428`) is the drawn fog. Fog isn't drawn as anything of its own.

- **The scenery's fog** (`Zones.SetSceneColors`, `0x8000c298`): from `996 − (short)trunc(fogTime × 40)` to 1000, in the sky's fog colour (`SkyColors.Fog`, `0x800d9500`), as `SkyView` sets it (`SkyColors.FogStart`). The far plane and the draw distance don't change. Fog at its thickest, 0.75, runs from 966, about 25 units out with trek's planes of 1 and 180 (996 is about 105); rain's 0.1 to 0.35 from 992 to 982. While a cut-scene plays (`CutScene.Playing`, `0x800dfe14`, which game mode 0xC's frame, `CutScene.ModeFrame`, `0x8005039c`, sets as the cut-scene starts and clears as it ends), the scenery's fog ignores the weather, and runs from 996; the sky's doesn't. Conversations in trek don't set it. Checked in the port at noon in Gwernia at `--weather fog,0.75` against clear: the near cobbles and the gatehouse stay at (88.8, 82.6, 75.4) and (101.3, 94.2, 83.7), the far end of the gate goes from (68, 65, 56) to (75, 98, 131), toward the fog colour (79, 109, 153), and the sky to (78, 108, 152), fully fogged. In the opening cut-scene the same fog changes nothing, within a tenth of a level.
- **The backdrop** (`SkyBackdrop.Draw`, `0x8001c204`): each line moves toward the fog colour by min(2 fogTime, 1), so it's fully fogged from 0.5. **The sun, moon and stars** (`SkyObjects.Draw`, `0x80029764`): alpha less alpha × fogTime, × 1.5 and × 2, at least 0. **The clouds** (`Clouds.Draw`, `0x80027654`): alpha less alpha × fogTime × 1.5, gone from 2/3. All of these are ported, and read the weather's `fogTime` now.
- **The colours.** Rain and snow change the sky to sky 2 ([the rain sky](#the-rain-sky)). Fog weather keeps the terrain's sky.

### The rain sky

Rain and snow bring sky 2 (Borg8 0x19E, the Battlefields' own, `WeatherRecord.RainSky`), crossfaded over 600 ticks, and the terrain's comes back the same way. At noon its tint is 158 against sky 1's 225, its sky (202, 202, 202) against (188, 205, 217), its fog (111, 111, 111) against (79, 109, 153), its cloud 214 against 228 and its sun 213 against 225; at midnight its tint is 71 and its fog 49. So the scenery darkens and greys: at the port's noon in Gwernia, the near cobbles go from 88.8 to 72.7 (0.82, as the tints' 181 over 222 predicts), and the sky from (60, 127, 162) to (82, 85, 91).

- **Bringing it** (`ProcessWeather`): the frame the emitters are made, `SetBackgroundType` (`0x8001dea8`) chooses sky 2 over 600 ticks under the type that's up, and `Weather.SkyOverridden` (`0x800daee0`) is set. So rain or snow that starts under no sky, in a cave, brings no sky change there.
- **Taking it away:** on the first frame the world wants nothing to fall, with `SkyOverridden` set, it's cleared and the terrain's sky (`Weather.Data` + 0x10, `Weather.TerrainSky`, which `GetMapTerrain`, `0x80025ca0`, sets on entering each chunk) comes back over 600 ticks, at once, not after the rain has died away. Rain turning to snow keeps sky 2: the snow chooses it again as it sets in, which does nothing.
- **Entering a chunk** (`LoadGameBorgScenes`, `0x8000b05c`, for the centre chunk, after `GetMapTerrain`). From an outdoor sky to another, the terrain's sky is chosen over 600 ticks, unless the world wants something to fall or something is falling (one word, `Weather.Data` + 0x0 and + 0x2: the debug build's "Different Precip types, Don't set sky", `0x8000e284` in it), when nothing is chosen, so the sky that's up stays, the rain sky or a change toward it. That holds while rain dies away too. Fog alone neither changes nor holds the sky. Otherwise, from or to a sky that isn't drawn, or from none at all, the sky is chosen at once, `SetBackgroundType` with 0 ticks: sky 2 if the world wants rain or snow and the sky is drawn, else the terrain's, and the fog is the world's at once. That's also how a cave's chunks choose among themselves, at once, where the port faded them.
- **Building trek's world again** (`InitZoneEngine`, `0x8000df84`: after a cut-scene, a load or combat, and a cut-scene's stage): `FreeZoneEngine` (`0x8000e6c8`) leaves sky type 2, no sky, and `Weather.Init` clears what's drawn, so what the world wants reads as nothing, and the first chunk takes its terrain's sky at once. Then the rain sets in again from nothing and brings sky 2 over 600 ticks. So in the rain, the sky drops to the terrain's on coming back from a cut-scene and greys again over ten seconds. `SkyOverridden` isn't cleared.

### Lightning and thunder

`RenderSky`, while the sky is drawn (ported as `Weather.SkyFrame`):

- **The flash** lasts the one frame, with no envelope: the backdrop is a full-screen white fill (`Screen.FillRect`, `0x8009cbe8`, 0, 0, 320, 240 in (255, 255, 255)) in place of the gradient's lines, and after the sky's colours are worked out, the scenery's tint (`SkyColors.Tint`, `0x800d94f8`) and the sky colour (`SkyColors.Sky`, `0x800d94fc`), half of which is bodies' ambient, are set to white. The fog, cloud and sun colours don't change, and the sun, moon, stars and clouds still draw over it. In the port, at noon in Gwernia under rain, the sky goes to 255, the cobbles from 72.7 to 102.4 (× 255 / 181, the rain sky's tint) and Alaron's shirt from 69 to 83.
- **The thunder** plays at the flash, with no delay and no distance: one of the four by `Range(0, 3)`, centred, at the sound volume (`Audio.PlaySound` on `0x800d9380`), with a lifetime of 240, 360, 420 and 360 ticks for 0x071C to 0x071F. The sounds are on the sounds record (`thunder`).
- **How often:** with a storm s, a share under s × 0.25 every 20 frames, so at 30 fps about 1.5 tries a second at 0.25 s each. The checkpoints' rain has s = 0.101: a flash every 26 seconds or so.

### The rain loop

0x071B (the sounds record's `rain`), looped, centred: it starts while rain is falling (not snow) and the sky is drawn, at volume 0, and each frame takes `min(255, trunc(I × 1.5 × 255)) × the sound volume`, as a byte. So it swells with the rain, full from I = 2/3, dies away with it, and stops once I is 0 (`Weather.StopRain`, `0x8001e770`), or at once where the sky isn't drawn. Snow and fog are silent. See [audio](audio.md).

- **On the sequencer itself**, not as a one-shot: `AudioEngine.Play` (`0x800907a4`) with volume 0, pan 0x80, looped and no time limit, the handle kept at `Weather.Data` + 0x18–0x20, and `AudioEngine.SetVolume` (`0x80090898`) every frame, in the same frame it starts. The volume is worked in doubles: `trunc(I × 1.5 × 255)`, at most 255, then × the sound volume as a float, truncated.
- **It stops** on a frame that starts with the drawn intensity at 0 or less, as what's falling changes to what's wanted, and starts again in the same frame if that's rain. Nothing fades it but the intensity. Coming out from under no sky in the rain starts it over from its beginning, as does the rain coming back after a dry spell.
- **Trek's world going** (`FreeZoneEngine`, through `Weather.Free`, `0x8001e030`) stops it, so it stops for a cut-scene's stage and starts again there, since `HandleZoneEngineFrame` runs `ProcessWeather` in cut-scenes as in trek. The pause menu runs neither, so it plays on there at its last volume.

## In the port

- **The rain sky.** `Weather.Draw` keeps what's wanted, whether the emitters are up (`Emitting`, which the precipitation will read) and `SkyOverridden`, and raises `SkyChange` as `ProcessWeather` calls `SetBackgroundType`; the runtime's `SkyView` changes `SkyState` to the weather record's `RainSky` or back to the terrain's. Entering a chunk is `SkyState.Enter`, with the hold (`Weather.HoldsSky`) and the choice at once (`Weather.WantsRainSky`). The cut-scene's stage and trek's world built again choose at once (`SkyView.ClearSky`), as after `FreeZoneEngine`. `--weather` chooses the sky again at once, so a screenshot shows the rain sky.
- **The flash.** For the frame `Weather.Flash` is set, `SkyBackdropView` fills the backdrop white and draws the sky's objects over it, and `SkyView` sets the tint and the sky colour white (`SkyColors.Flashed`), so bodies' ambient is half white.
- **The thunder and the rain loop** are the core's `WeatherSounds`, through `GameAudio`: the thunder a one-shot of `thunder` with its lifetime and the sound volume as its gain, and the loop `rain` on the sequencer, which `Weather` starts and stops (`RainLoop`, `RainLoopStarts`) and sets the level of (`RainLoopLevel`).
- **Cut-scenes** run the weather as trek does (`TrekLoop.StepWeather`): the lightning, the rain loop and the rain sky, under the stage's sky. The scenery's fog ignores the weather there (`SkyView.CutScene`).
- **Coming back from a cut-scene in the rain**, the sky drops to the terrain's at once and the rain sky comes back over ten seconds as the rain sets in again, as in the original ([the rain sky](#the-rain-sky)). That's the original's way of building trek's world again, ported as it is, though it looks like a glitch ([to check](../to-check.md)).

## To port

The core's `Weather` has what each of these reads: `Falling`, `FallIntensity`, `Emitting`, `FogTime`, `Wind`, `Storm`, `Flash` and `Thunder`. `--weather rain|snow|fog|clear[,intensity[,fog]]` sets it for a screenshot ([runtime](runtime.md)).

1. **Rain and snow outside trek.** `HandleZoneEngineFrame`, and so `ProcessWeather`, also runs in the dialogue mode's frames, which cut-scenes play in (`dialogmode.cpp`, calls at `0x80050270` and `0x80050f50`), in combat's (`gamecombat.cpp`, `0x80021564`) and at `0x80054f70`, each after `InitZoneEngine` has cleared what's drawn, so the rain sets in again there from nothing. The port's cut-scenes step the weather but not `WeatherParticles` yet (trek's pair is freed as its world goes); combat waits for M3.
2. **Combat's** readers, with combat.
