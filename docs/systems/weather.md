# Weather

How the weather changes, what it holds, and how the original draws and plays it. The rules are `Aidyn.Core.Calendar.Weather`, and the rain and snow `Aidyn.Core.Particles.WeatherParticles`, in trek and in cut-scenes; combat's waits for M3 ([to port](#to-port)). Addresses are 1.0's unless marked.

## The records

- **The weather record** (`aidyn:weather/0`, `WeatherRecord`, `WorldImporter.BuildWeather`): each season's chance of precipitation, from the code's table (`0x800e3040`, ROM `0xE3C40`: 43, 48, 33, 25, 36, 40 for the six seasons), the literals of the roll, the wind and the drawing, and the rain's and snow's look (`Rain`, `Snow`: `PrecipitationLook`).
- **The terrain records** (`aidyn:terrain/n`): what each terrain adds to the season's chance (`precipitation`, s8s at `0x800e3048`), how hard it falls there (`precipitationScale`, floats at `0x800e3064`) and whether it's snow (`snow`, terrain 5 alone, a literal in the roll). The tables are identical in 1.0, 1.1 and the debug ROM (`WeatherTables`).

| Terrain | 0–2 | 3 | 4 | 5 | 6 | 7 | 8–17 | 18 | 19–22 | 23 | 24 | 25 | 26–27 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Chance added | 0 | −100 | 5 | 30 | 0 | 15 | 0, but 10 is 20 | 100 | 0 | 30 | 15 | 0 | −100 |
| Scale | 0.5, 0.5, 0.4 | 0 | 0.7 | 0.6 | 0.5 | 0.8 | 0.2 | 0.8 | 0.2 | 0.8 | 0.6 | 0.2 | 0 |

Terrains 3, 26 and 27 never get precipitation; terrain 18, in the Battlefields and one interior, always does when the weather changes.

## The state

Two structs hold it, and the port's `Weather` holds both.

- **The world** (`World`, 0x38 bytes behind `TerrainPointer`, `0x800E303C`; debug `0x800F1BBC`): +0x2 the part of the day as the weather last saw it; +0x4 the class of bad weather combat reads (0 none, 1 fog, 2 storm: `WeatherKind`); +0x5 a lock on the part of the day, which combat's Day and Night spells raise; +0x6 what falls (0 none, 1 rain, 2 snow: `Precipitation`); +0x8 its intensity; +0xC the fog's thickness; +0x10 the storm, the lightning's chance; +0x14 the wind, a vector; +0x24 the terrain. (+0x3 is the moon's phase, +0x20 the clock, +0x28–0x30 the sun's and moon's offset, +0x34 time played.)
- **What's drawn** (`Weather.Data`, `gGlobals` + 0x394: `0x800D841C`; debug `0x800E6C3C`, 0x24 bytes, cleared by `InitZoneEngine` through `Weather.Init`, `0x8001e010`): +0x0 what the world wants falling, +0x2 what's falling, +0x4 how hard (`FallIntensity`), +0xC the fog as drawn (`fogTime`), +0x10 the sky to go back to, +0x14 the precipitation's emitter, +0x18–0x20 the rain loop's sound.

The cloud cover isn't weather: it's set to 1 once (`0x80027390`) and nothing changes it.

## When it changes

`World.Update` (`0x8007bf5c`) runs on every frame of the clock: the calendar (`World.GetDate`, `0x8007ba30`), the moon's phase (`0x8007bc70`), the part of the day (`World.UpdatePartOfDay`, `0x8007bd3c`), which rolls the weather when it has changed, the wind's drift, then the calendar's flags (`StateFlags.SetTime`, `0x8001fdec`), the rain flags (`StateFlags.SetRain`, `0x8001fe9c`) and the sun's offset. The weather rolls:

- **When the part of the day changes**, at 6:00, 9:00, 12:00, 18:00 and 21:00, as trek's clock passes them (`TrekLoop.TickClock`, `0x80020cb4`, then `GameClock.Step`, `0x8007c054`). Afternoon runs to 18:00.
- **On entering a chunk of another terrain** (`World::SetTerrain`, `0x8007b828`): it moves the part of the day it remembers on by one, so `World.Update` sees a change and rolls. A chunk of the same terrain rolls nothing. That covers walking, doors, cut-scenes staged elsewhere and combat's arenas.
- **After camping** (`GameState.Lapse`, `0x8007c098`, eight hours), once, if the part of the day is another.
- **At a new game** (`World.Init`, `0x8007b7a0`, then `World.SetDate`, `0x8007b914`, at noon): the world starts at part 0 and terrain 0, so setting noon is a change, and the weather may change by season 0's and terrain 0's chances before the first chunk's terrain rolls again.
- **Each combatant's turn** moves the clock on by 600 (`World.AdvanceTurn`, `0x8007c01c`), which can roll. Not ported: combat.
- **Nothing else.** No script command, map object or flag sets the weather. The end of a Wind enchantment (effect 0xB in `0x800707ec`) sets only the world's +0x4 to fog. Not ported: spells.

While the lock (+0x5) is up, the part of the day doesn't change and the weather doesn't roll. Not ported: the spells.

## The roll

`Weather.Roll` (`0x8007bdb0`). Every die is `RollD(1, 100)`:

1. At most 75 (`KeepAtMost`), nothing changes.
2. Otherwise it clears, and a second die at most the season's chance plus the terrain's (an s16 sum) brings precipitation.
3. Then a third under 70 (`FallBelow`) makes it rain, or snow on terrain 5, at the terrain's scale give or take 0.15 (`IntensitySpread`), at least 0, with fog from 0.1 to 0.35 (`FallFog`): class 2.
4. Otherwise it's fog from 0.1 to 0.75 (`Fog`), with nothing falling: class 1.
5. The storm is a share of the rain's intensity, from 0 to it; snow and fog have none.

Every range is `ParticleHead.FloatRange` (`0x80093470`): a share from 0 to 1 times the span, from the lower; equal bounds draw nothing. The draws go: the first die; the second; the third, the intensity, the fog; then the storm (rain). The port rolls on the world's stream (ADR 0006).

## Over time

`ProcessWeather` (`0x8001e7c0`) runs every frame trek or a cut-scene draws (`HandleZoneEngineFrame`, `0x8000e784`, which a cut-scene's mode calls through `0x80050f50`), whatever stops the clock, before `TrekLoop.TickClock`, so a roll shows from the next frame. Its rates are per tick, so the port steps it every tick (`Weather.Draw`):

- **What falls** sets in and dies away. When what's falling isn't what the world wants, it dies away at 1/300 a tick, and once it's at 0, what the world wants starts. While they agree and something falls, it rises at 1/300 a tick to the world's intensity, at most 1, and goes straight to the intensity when it's over. So rain turning to snow dies away first.
- **The fog** (`fogTime`) moves toward the world's at 1/900 a tick, and takes it once within 0.01.
- **Where the sky isn't drawn** (sky type 4: terrains 6, 8, 9, 11 and 13–16), the fog is 0, the emitter and the rain loop are freed every frame, but the world's weather and the rain flags carry on. Coming into a chunk with the sky from one without, the fog is the world's at once (`LoadGameBorgScenes`, `0x8000b05c`).
- **When trek's world is built again** (`InitZoneEngine`: after a load, a cut-scene or combat), the original's drawing starts from nothing. The port carries it on ([below](#trek-built-again)).

## The wind

`Weather.DriftWind` (`0x8007baa0`), once a frame of the clock from `World.Update`, with 0.05 for both chances, whatever the weather:

- On a share at most 0.05, the direction moves by −0.1 to 0.1 in X and Z and −0.004 to 0.001 in Y, and is normalised.
- On another, the speed moves by up to 1.5/648 either way, from 0 to 150/648 (`WindMaxSpeed`). A new game's is (1, −0.1, 1) normalised at 15/648.
- It blows the clouds (4 × it a frame) and the precipitation's emitter.

The port draws the drift from the cosmetic stream every second tick (`Cadence.FrameTicks`).

## Lightning

`RenderSky` (`0x8001d840`), in an outdoor sky only, counts its frames to 20 (`0x800DAED4`). While the storm is above 0 it draws a share from 0 to 1 each frame, and on the twentieth frame a share under the storm × 0.25 (`LightningShare`) strikes: the screen flashes and one of the four thunder sounds plays, `Range(0, 3)` (`Weather.SkyFrame`, on the cosmetic stream). See [lightning and thunder](#lightning-and-thunder).

## Flags, saves and other readers

- **The rain flags** (2319–2320) hold what the world has falling, written every frame of the clock and when the weather is restored: 1 rain, 2 snow, 0 for clear and for fog ([flags](flags.md)).
- **Saving.** The original saves a snapshot taken as the pause menu opens (`Weather.Snapshot`, `0x8007bc4c`, from `Weather.Pause`, `0x8001a238`): what falls, its intensity, the fog and the storm, each × 1000 as a whole number (`SaveBlock.Write`, `0x80019810`). It doesn't save the class, the wind or what's drawn. Loading restores the four (`Weather.Restore`, `0x8007bbfc`, then again after trek's world is built, `Weather.Resume`, `0x8001a4f0`), and writes the drawn intensity and the fog straight in. The port's save keeps all of it ([below](#differences-from-the-original)).
- **The wanderers** see less in fog: their perception × (1 − `fogTime` × 0.75) (`0x8000f6a4`).
- **Combat** (not ported): a thrown potion's chance (`Combat.WeatherPenalty`, `0x80062d78`) loses 30 at night, 20 in a storm (class 2) and 10 in fog (class 1), and in bad weather another (distance − 5) beyond 6, on top of the distance penalty its caller already took; a ranged attack's chance (`Combat.RangedChance`, `0x80062f50`) is × (1 − the intensity × 0.3), and × 0.7 at night; a melee one × 0.9 at night.

## Differences from the original

All of these are the user's choices.

- **Streams.** The original rolls the weather, drifts the wind and flashes the lightning from its one stream. The port rolls on the world's and draws the rest from the cosmetic one (ADR 0006).
- **Saving the whole weather.** The port's save keeps the class, the wind and what's drawn, so a loaded game's weather looks as it did. In the original the class is whatever the fresh world's rolls left (the checkpoints show it stale), and the wind starts again at a new game's.
- **Loading resumes from the save:** what the world wants falling is taken as the save has it (`Weather.ResumeDrawing`), so the first chunk chooses the rain sky at once where it rains, and the rain and its loop go on at the intensity saved. The original's fresh world remembers noon's part of the day, so the first frame may roll; the port takes the loaded time's and rolls nothing.
- <a id="trek-built-again"></a>**Trek built again keeps the weather.** The original's `FreeZoneEngine` stops the rain loop (`Weather.Free`, `0x8001e030`) and `InitZoneEngine` clears what's drawn (`Weather.Init`) whenever trek's world is built: for a cut-scene's stage and after it, after a backdrop or the help menu, after a load, and for combat's arena and after. So in the rain the sky drops to the terrain's at once, greys again over ten seconds, and the rain loop starts over. The port carries what's falling, how hard, the fog, the rain sky (`SkyOverridden` and what's wanted) and the rain loop through all of these. Only the emitters go, with the particle pool (`ZoneEngine.Free` and `End` forget them), and the next tick makes a pair about the new view. The stage's and trek's sky, chosen at once as the world is built (`SkyView.ClearSky`), is then the rain sky. Combat's arena, with M3, is a stage of trek's zone engine (`ZoneEngine.Stage`), with trek's world freed and built again through `TrekLoop.Free` and `Rebuild`, and so keeps it too.
- **A new pair fills the air at once while it's already falling.** The original's new pair starts empty, so after a door or a rebuild in the rain its drops take about two seconds to fall into view. When `Weather.Draw` makes the pair while `FallIntensity` is above 0, `WeatherParticles.FillAir` runs the pair alone on, frame by frame, for as long as a drop lives (240 ticks for rain, 360 − 60 I for snow), before it's first drawn. No other emitter moves (`ParticleHead.Frame` with a filter). Rain setting in from nothing still starts empty. `WeatherParticles.FillsAir` turns it off.
- **Rain and snow land on what's drawn**, not the collision: [what the port's lands on](#what-the-ports-lands-on).
- **Rain spawns at 20 fps:** [spawning](#rain-and-snow).
- **More rain than the original**, by an option: [below](#more-rain-than-the-original).

## How the original draws it

`HandleZoneEngineFrame` (`0x8000e784`) draws, in trek and in cut-scenes, in order: `RenderSky` (`0x8001d840`) with its lightning, the sun, moon and stars and the clouds while the sky is drawn, the scenery and bodies, then the particles (`ProcessAndRenderParticleHead`, `0x800a842c`, on `Particles.Head`, `0x800d84b8`), then `ProcessWeather`. So rain and snow are drawn last, before the HUD.

### Rain and snow

`ProcessWeather` makes two emitters of the game's particle system (`WeatherParticles.Make`, `0x8001e62c`) and steers them each frame: B, the drops (`Weather.Data` + 0x14), and A, for splashes. In the port they're in the runtime's particle pool, which `Weather.Draw` makes, steers, keeps, forgets and frees every tick through `IPrecipitationEmitters`.

- **When.** While what's falling is what the world wants and isn't nothing, the sky is drawn and there's no emitter, the pair is made, and the sky changes to [the rain sky](#the-rain-sky). When what's falling isn't what the world wants, the emitter is forgotten, not freed, and runs out its life; each frame both emitters' lives are set to 300 while it falls.
- **Rain**: colour (0.546875, 0.7421875, 0.8203125, 0.8) (`0x800ce0d0`); drops of particle texture slot 4 (Borg1 2, an I4 8 × 8 streak), splashes of slot 16 (Borg1 12, an I8 16 × 16 ripple), and ambient splashes. **Snow**: white (`0x800ce0e0`), flakes of slot 0 (Borg1 3, an I4 16 × 16 soft disc), which land as discs of the same; no ambient splashes. The slots are the particle textures (`partTextInds`, ROM `0xCE460`).
- **The pair** (`WeatherParticles.Make`): A first, life 60, its head's vector (−1/120, −1/120, −1/120) shrinking every particle in its list (flag 0x400), spawning ambient splashes for rain and nothing for snow; then B, life 60, its head's vector moving every drop (flag 0x80), spawning drops and stepping them with `WeatherParticles.DropStep`. Each holds the other (`+0x1C8`). Without room for B, A is freed (`Particles.KillEmitter`) and nothing is made that frame. The sky is asked for just before the pair is made, so while there's no room, `SetBackgroundType` runs again every frame. The port keeps that one decision in `Weather.Emitting`, which the particles, the sky and the rain loop all follow. B's centre starts at 7 above the aim.
- **Each frame**, B's spread is 20, its centre 5 ahead of the camera's aim point (`0x800d82a8`) along the camera's level look (`FollowCamera.LookXZ`, `0x800d82cc`), and 9 − I above it, where I is the drawn intensity before this frame's change. Its fall is the wind, plus 0.075 a tick down for rain, or 0.02 × I + 0.03 for snow (doubles). Rain spawns a drop every `(short)(6 − 4 I)` ticks' worth of frames and lives 240 ticks, its splashes every 2 fewer; snow every `(short)(6 − 7 I)`, living `(short)(360 − 60 I)`; snow's A keeps its interval of 0 and has no spawn. The intervals are set even on the frame the pair is made.
- **Spawning** (`ProcessAndRenderParticleHead`, once a frame per emitter): while an emitter's counter is above 0 it loses the frame's ticks and nothing spawns; at 0 or below it's set to the interval and the spawn callback runs once, or with an interval under 0, that many times. An interval of N spawns every 1 + ⌈N / delta⌉ frames, so how many drops come a second depends on the frame rate. Nothing caps them but the pool: 16 emitters and 384 particles with the Expansion Pak (64 in trek without).
  - **The port spawns the pair by frames of 20 fps** (`Cadence.PrecipitationFrameTicks`, `Emitter.SpawnTicks`), the console's average, as textures scroll, while the rest of the pool steps and spawns at 30. The user chose 20 as the middle ground: the console runs at about 15 fps on the beach at L08 and faster indoors, so no one rate matches everywhere. The core's tests count the spawns (`RainSpawnsOnceAFrameOfTheConsoles20Fps`).
- **A drop** (`WeatherParticles.SpawnDrop`, `0x8001e444`) starts at a random distance from 0.01 to the spread from B's centre in a random level direction (`ParticleHead.Direction2`, `0x800934dc`: a point in the square from −1 to 1, normalised), at scale 0.25, in B's colour, its vector the colour × −0.75 / its life (flag 0x200), so it fades to a quarter of its colour, alpha too. It sweeps a sphere of radius 0.1 from where it starts along its fall × its life, past triangles flagged 0x200 (`SpherePhysics.Sweep`, `0x800a42e8`, last argument 1), and keeps where that hits in its `+0x4C`, with 100000 in W; without a hit, (0, −1000, 0).
- **Landing** (`WeatherParticles.DropStep`, `0x8001e138`): while the drop is nearer to that point than ever, by the square of the distance, it notes how near. Once it's no nearer, each frame it sweeps from 5 above it to 5 below, past 0x200 triangles, and on a hit it's moved into A's list (`ParticleHead.MoveParticle`, `0x800a8b14`) and becomes a splash (`WeatherParticles.MakeSplash`, `0x8001e04c`): 0.05 along the ground's normal, laid flat on it (flag 0x4, from `guAlign` of the normal with 10⁻⁶ added to its X), at scale 0.75, its vector its colour × −1/120 so it fades to black over its new life of 120, while A's head shrinks it to nothing at 90. Without a hit it tries again every frame. If A has been freed, the drop is freed.
- **Ambient splashes** (rain only, `WeatherParticles.SpawnSplash`, `0x8001e274`): a point from 0.01 to 10 from B's centre, a sweep from 50 above it to 50 below, not skipping 0x200 triangles, and on a hit a particle in A's list laid as a splash.
- **Motion.** Each tick a drop moves by its emitter's fall (`ParticleHead.ApplyVector`, `0x800a7fa0`). The wind slants the paths; the sprites never turn with it.
- **The sprite** (`Particles.DrawPrologue` `0x800a8f74`, `Particles.DrawMode` `0x800a91dc`, `ParticleHead.Draw` `0x800a92fc`): a textured quad, 0.3535 × the scale either side of its middle, facing the camera's look and upright (`guAlign` with no roll), and a splash facing the ground's normal. The texture covers the quad once, its first row at the bottom, so a rain streak's bright head leads. Its vertices' colour is the particle's × 255 × the screen's brightness (`0x800da0c8`), so particles dim with fades. One cycle, `(TEXEL0 − 0) × SHADE + 0` for colour and alpha (`0xFC121824 FF33FFFF`), `G_RM_ZB_XLU_SURF` (`0x00504A50`): depth tested, not written. Smooth shade, **no fog**, bilinear. The runtime's `ParticlesView` draws it ([rendering](rendering.md#particles)).
- **In a cut-scene** the dialogue mode's frame (`CutScene.PlayFrame`, `0x80050f50`) and its ending's fade-out (`CutScene.EndFrame`, `0x80050270`) run `HandleZoneEngineFrame`, so the rain falls there as in trek, in the pool the stage's `InitZoneEngine` made. It falls about the script camera's aim, and its level look is written only by `FollowCamera.Orient` (`0x800a6628`: the target less the eye, X and Z normalised), which `ProcessVoxelCamera` (`0x800160d4`) calls after every script camera move that sets the place or aim (flags & 0xC000). Before the first move it's (1, 0) (`Camera.Init`, `0x800a64f8`). The drops sweep `Zones.PlayerMap`, which `CutScene.LoadStage` (`0x80051984`) makes the stage's centre chunk. The port's stage, a zone engine of its own (`ZoneEngine.Stage`), has its own `WeatherParticles` about the cut-scene's camera, landing on what its centre chunk draws.
- **Randomness.** The original draws three from its one stream for each drop and ambient splash; the port from the cosmetic stream.
- **A teleport** through a door (`TeleportPlayer`, `0x8000bb54`, given no position) forgets the pair and kills every emitter still spawning (`ParticleHead.KillActive`), so the next frame makes a new one about the player and asks for the rain sky again, while the rain loop plays on. The port does the same through `Weather.Forget` (`TrekLoop.GoThrough`). Walking into another chunk shifts every particle by the world origin's shift (`Particles.Shift`, `0x80017918`); the port's world doesn't move, so it has nothing to shift.

#### What the original's lands on

The collision of the chunk the player is in (`Zones.PlayerMap`, `0x800d8288`), not the chunks around it. Near a chunk's edge the drops over the next chunk find no ground and make no splash. The drops' sweeps pass see-through (0x200) triangles; rain's own splashes land on them. So rain splashes in the air wherever the collision stands over what's drawn: on invisible walls and over the sea's edge. Every sweep takes the first triangle facing it in the collision grid's order, not the nearest. A wall only delays a drop: past that point it finds the ground from 5 above to 5 below.

#### What the port's lands on

**The port lands rain and snow on what's drawn**, the user's choice, so splashes never float on invisible walls. It tests every loaded chunk, the 3×3, rather than the player's alone (also the user's choice); the cut-scene's stage tests its centre chunk's alone (`WeatherParticles` given a chunk's key). The chunk streamer keeps a second world beside the collision, `VisibleWorld`, under the same keys, filled as each chunk loads and again as a scene object comes or goes, a container's model changes or a path light goes out (`ChunkStreamer.Refresh`). What goes in is `ChunkSurfaces`':

- **Every draw** of the chunk's scene, its see-through scene (`AltScene`) and each scene object it places, at its nearest level of detail with the model a container shows, placed as `ChunkScenery.Place` places it, before anything animates it.
- **What splashes:** solid draws (glTF `OPAQUE`, from the N64's render mode; see [rendering](rendering.md#the-n64-material)) whose texture doesn't scroll.
- **What absorbs** (`DrawnTriangle.Absorbs`, `ChunkSurfaces.Absorbs`): everything else. Cut-outs (`MASK`: canopies, bushes, fences), translucent draws (`BLEND`: water, glass, light), and scrolling textures (Borg1 flag 0x8: water, lava, fire, magic). A drop that reaches one ends there with no splash. A canopy absorbs as whole triangles, gaps between the leaves and all.
- **Not billboards or bodies:** their cards and Alaron, the wanderers and cut-scene actors are left out, so rain passes through them, as the original's falls through bodies.

Every query is a ray, not the original's sphere of 0.1, and takes the nearest surface from either side (`VisibleWorld.Raycast`). A drop casts its fall for its life as it's made. Once past where it would land, each frame it looks along its own fall from 5 back to 5 on, so a drop that passed beside an eave never splashes on it. Where it lands on a surface steeper than 60° (`WeatherParticles.SplashSlope`, a normal under 0.5 in Y), or one that absorbs, it ends without a splash. Rain's own splashes take the highest surface from 50 above to 50 below, and none where that's steeper or absorbs them. `ChunkSurfacesTests` checks the drawn surfaces line up with the collision over M07 and I08.

#### More rain than the original

The options menu's **Rain and snow** (`Settings.PrecipitationDensity`) multiplies how many drops each spawn makes: Original (1×), 2x, 4x and 8x. A fraction carries to the next spawn (`WeatherParticles.Density`). The pool hands out the original's 384 particles times the density, at least 384, from room for 8 × 384 (`ParticleHead.MaxParticles`, `Capacity`). The port's pool never drops to the no-pak 64.

**The splashes stop at 1.5× the original's** (`WeatherParticles.SplashCap`), whatever the density, the user's choice, so the denser settings don't carpet the ground in ripples. Rain's own splashes are min(density, 1.5) times the original's, and past 1.5 each drop that lands splashes by a share of 1.5 / density, carried from one landing to the next. Snow's flakes land by the same share. The core's tests check the counts (`DensityKeepsItsDropsButHoldsTheSplashesToOneAndAHalfTimesTheOriginals`).

The spread needs no widening for 16:9: the disc of radius 20 spans the view's width out to about 22 units.

### Where the sky isn't drawn

Sky type 4 (terrains 6, 8, 9, 11 and 13–16): nothing falls (the emitters are freed every frame), the rain loop stops at once, the weather's fog is 0 (the scenery keeps its 996–1000 in the terrain's fog colour), and there's no lightning. The world's weather and the drawn intensity carry on, and `LoadGameBorgScenes` changes the sky to sky 2 at once on the way out if it rains or snows, and takes the fog at once.

### The fog

`fogTime` (`Weather.Data` + 0xC, `0x800d8428`) is the drawn fog. Fog isn't drawn as anything of its own.

- **The scenery's fog** (`Zones.SetSceneColors`, `0x8000c298`): from `996 − (short)trunc(fogTime × 40)` to 1000, in the sky's fog colour (`SkyColors.Fog`, `0x800d9500`; `SkyColors.FogStart` in `SkyView`). The far plane and the draw distance don't change. Fog at its thickest, 0.75, runs from 966, about 25 units out with trek's planes of 1 and 180 (996 is about 105). While a cut-scene plays (`CutScene.Playing`, `0x800dfe14`, set by game mode 0xC's frame, `CutScene.ModeFrame`, `0x8005039c`), the scenery's fog ignores the weather and runs from 996; the sky's doesn't. Conversations in trek don't set it.
- **The backdrop** (`SkyBackdrop.Draw`, `0x8001c204`): each line moves toward the fog colour by min(2 fogTime, 1). **The sun, moon and stars** (`SkyObjects.Draw`, `0x80029764`): alpha less alpha × fogTime, × 1.5 and × 2, at least 0. **The clouds** (`Clouds.Draw`, `0x80027654`): alpha less alpha × fogTime × 1.5.
- **The colours.** Rain and snow change the sky to sky 2 ([the rain sky](#the-rain-sky)). Fog keeps the terrain's sky.

### The rain sky

Rain and snow bring sky 2 (Borg8 0x19E, the Battlefields' own, `WeatherRecord.RainSky`), crossfaded over 600 ticks, and the terrain's comes back the same way. At noon its tint is 158 against sky 1's 225, its fog (111, 111, 111) against (79, 109, 153); at midnight its tint is 71 and its fog 49. So the scenery darkens and greys.

- **Bringing it** (`ProcessWeather`): the frame the emitters are made, `SetBackgroundType` (`0x8001dea8`) chooses sky 2 over 600 ticks, and `Weather.SkyOverridden` (`0x800daee0`) is set. Rain that starts under no sky brings no sky change there.
- **Taking it away:** on the first frame the world wants nothing to fall, with `SkyOverridden` set, it's cleared and the terrain's sky (`Weather.Data` + 0x10, `Weather.TerrainSky`, which `GetMapTerrain`, `0x80025ca0`, sets on entering each chunk) comes back over 600 ticks, at once, not after the rain has died away. Rain turning to snow keeps sky 2.
- **Entering a chunk** (`LoadGameBorgScenes`, `0x8000b05c`, for the centre chunk, after `GetMapTerrain`). From an outdoor sky to another, the terrain's sky is chosen over 600 ticks, unless the world wants something to fall or something is falling (one word, `Weather.Data` + 0x0 and + 0x2), when the sky that's up stays. Fog alone neither changes nor holds the sky. From or to a sky that isn't drawn, or from none, the sky is chosen at once, `SetBackgroundType` with 0 ticks: sky 2 if the world wants rain or snow and the sky is drawn, else the terrain's, and the fog is the world's at once. That's also how a cave's chunks choose among themselves.
- **Building trek's world again** (`InitZoneEngine`, `0x8000df84`): `FreeZoneEngine` (`0x8000e6c8`) leaves no sky, and `Weather.Init` clears what's drawn, so the first chunk takes its terrain's sky at once, and then the rain brings sky 2 again over 600 ticks. `SkyOverridden` isn't cleared. The port keeps the rain sky up ([differences](#trek-built-again)).

### Lightning and thunder

`RenderSky`, while the sky is drawn (`Weather.SkyFrame`):

- **The flash** lasts one frame, with no envelope: the backdrop is a full-screen white fill (`Screen.FillRect`, `0x8009cbe8`) in place of the gradient, and the scenery's tint (`SkyColors.Tint`, `0x800d94f8`) and the sky colour (`SkyColors.Sky`, `0x800d94fc`), half of which is bodies' ambient, are set to white. The fog, cloud and sun colours don't change, and the sun, moon, stars and clouds still draw over it.
- **The thunder** plays at the flash, with no delay and no distance: one of the four by `Range(0, 3)`, centred, at the sound volume (`Audio.PlaySound` on `0x800d9380`), with a lifetime of 240, 360, 420 and 360 ticks for 0x071C to 0x071F (the sounds record's `thunder`).
- **How often:** with a storm s, a share under s × 0.25 every 20 frames.

### The rain loop

0x071B (the sounds record's `rain`), looped, centred: it starts while rain is falling (not snow) and the sky is drawn, at volume 0, and each frame takes `min(255, trunc(I × 1.5 × 255)) × the sound volume`, as a byte (the product is in doubles, `trunc.w.d`, as `ColorBytes.Double` makes a byte, and capped as an unsigned compare; `ProcessWeather`, `0x8001ec90`). So it swells with the rain, full from I = 2/3, and stops once I is 0 (`Weather.StopRain`, `0x8001e770`), or at once where the sky isn't drawn. Snow and fog are silent. See [audio](audio.md).

- **On the sequencer itself**, not as a one-shot: `AudioEngine.Play` (`0x800907a4`) with volume 0, pan 0x80, looped, the handle at `Weather.Data` + 0x18–0x20, and `AudioEngine.SetVolume` (`0x80090898`) every frame. The volume is worked in doubles, then × the sound volume as a float, truncated.
- **It stops** on a frame that starts with the drawn intensity at 0 or less, and starts again in the same frame if what's wanted is rain. Coming out from under no sky in the rain starts it over from its beginning.
- **Trek's world going** (`FreeZoneEngine`, `Weather.Free`) stops it. The pause menu runs neither, so it plays on there at its last volume.

## In the port

- **The rain sky.** `Weather.Draw` keeps what's wanted, `Emitting` and `SkyOverridden`, and raises `SkyChange` as `ProcessWeather` calls `SetBackgroundType`; the runtime's `SkyView` changes `SkyState` to the weather record's `RainSky` or back. Entering a chunk is `SkyState.Enter`, with the hold (`Weather.HoldsSky`) and the choice at once (`Weather.WantsRainSky`). The cut-scene's stage and trek's world built again choose at once (`SkyView.ClearSky`).
- **The flash.** For the frame `Weather.Flash` is set, `SkyBackdropView` fills the backdrop white, and `SkyView` sets the tint and the sky colour white (`SkyColors.Flashed`).
- **The thunder and the rain loop** are the core's `WeatherSounds`, through `GameAudio`: the thunder a one-shot with its lifetime, the loop on the sequencer, which `Weather` starts, stops and sets the level of (`RainLoop`, `RainLoopStarts`, `RainLoopLevel`).
- **Cut-scenes** run the weather as trek does (`ZoneEngine.StepWeather`, from `CutScene.Tick`), under the stage's sky, with the sky's frames counted on from trek's; the scenery's fog ignores the weather there (`SkyView.CutScene`).
- **Debugging:** the core's `Weather` exposes `Falling`, `FallIntensity`, `Emitting`, `FogTime`, `Wind`, `Storm`, `Flash` and `Thunder`; `--weather` and `--precipitation` set it for a run ([runtime](runtime.md)).

## To port

- **Combat's** rain and readers, with M3: `HandleZoneEngineFrame` runs in combat's frames too (`gamecombat.cpp`, `0x80021564`), so the arena would be a stage of trek's zone engine with a `WeatherParticles` of its own about combat's camera, as the cut-scene's is (`ZoneEngine.Stage`); trek's world goes and comes back through `TrekLoop.Free` and `Rebuild`, which carry the weather. (`0x80054f70`, the other caller, is the title screen's scenery, `InitZoneEngine` in mode 2.)
