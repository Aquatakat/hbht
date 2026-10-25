# Using map objects (M2)

`Aidyn.Core.MapObjects`. What map objects are and how the base plugin holds them is in [maps](../formats/maps.md); the rules for containers, locks and traps are in [items](items.md#containers).

Ported from `objectJumpTables.cpp`, `voxObjects.cpp`, `voxObjects2.cpp` (`obj_ref_func`), `voxelProcess.cpp` (`ProcessVoxelObjects`), `lockpick.cpp`, `trapmenu.cpp` and the zone engine's `check_trigger` and `TeleportPlayer`.

## A chunk's objects at run time

`LoadedMap` is a chunk's objects while it's loaded, as the original's `Borg9Data` is: which are active, its containers' and teleporters' locks and traps, and which model each container shows. Loading the chunk activates its objects by the flags (`VoxelObjects_Activate`), and what using them changes lasts until it's loaded again.

- **Active** objects are those there by their flags ([maps](../formats/maps.md#map-objects-in-the-base-plugin-06)) and not only for a console without the Expansion Pak. Using a container, or a trap going off, makes it inactive.
- **A chest, sack, crate or barrel** found opened, by its flag or because it's remembered as emptied ([saves](saves.md)), loads inactive with no lock, trap or gold, its model showing it opened; found destroyed, its model shows that. Its model is there either way. A herb, spice, gem or custom container's model is there only while the container is.
- **A teleporter's** lock and trap are gone while their flags are set. A secret door that needs a skill to be there is there once found, or if the party's search reaches its level (`Party::SecretLock`): the best Intelligence × 3 + Ranger × 10 against d100. No base-game door needs one.
- **The Ranger's share** of what sources give is rolled as the chunk loads.
- **Links.** A container's model, or the actor a trigger animates, is found by following links from object to object (`Linked`).
- **Only the chunk the player is in counts.** The original keeps only its objects at hand (`borg9DatPointer`), for A, for walking into objects and for trigger triangles. It looks up a trigger triangle's object in that chunk even when the triangle is a neighbour's, where they overlap; the port looks it up in the triangle's own chunk.
- **Crossing into another chunk is a teleport** in the original (`ConfirmPlayerWithinZone`): it ages what map objects remember, and loads the chunks that come into the 3×3, keeping the rest as they are.
- **Beyond the 3×3** (the port's Draw Distance Extended, [rendering](rendering.md#draw-distance)), the Overworld's outer 16 chunks of the 5 × 5 are drawn but not loaded: their maps are read for which scene objects show, by the same flags and the same memory (`LoadedMap.Scenery`, through `FlagNetwork.IsSetAsStored`, which doesn't bring the network up to date), with nothing rolled, written or aged, and none of their objects can be used, walked into or trodden on. The ring gates its scene objects again as the menu closes and as the 3×3 loads again where it was.

## What A reaches

`MapObjectUse.Reach` (`obj_ref_func`): of the objects the grid cell under the player lists ([maps](../formats/maps.md#collision-the-grid-and-placement-1c)), the active ones that aren't tangible and that the player is within reach of by each type's test, up to 16:

| Type | In reach |
| --- | --- |
| Container | Within its reach plus the player's radius (0.5), from the player's centre in 3D: 2.5 for most. Its reach isn't its radius, which is 10 for most (see [maps](../formats/maps.md#map-objects-in-the-base-plugin-06)). Then a herb, spice, gem or custom container always; a chest, sack, crate or barrel only until opened, and only if it holds gold, a loot table or an item (`container_obj_check`) |
| Teleporter, trigger, dialogue | Within its radius, while its Enabled holds |
| Save point | Within its radius, and using it does nothing |
| Monster party | Only the player's nearest body, if it's a wanderer's and its party isn't silent: within the two bodies' scaled radii plus 0.5 across the ground, and less than 2 apart in height. It's weighed by that distance and its body's position. See [wanderers](wanderers.md#talking) |

One alone is used. Of several, the one with the best (farthest − its distance) / farthest + (1 + the cosine of its bearing off the player's heading) / 2 is used, the later on a tie. The original walks its grid cell's object list rather than the chunk's; every container, teleporter, trigger and save point is listed in every cell it reaches, so the two agree, except for the 11 dialogues that trigger triangles name, which no cell lists (below).

## Walking into objects and onto triangles

- **Tangible objects** (`ProcessVoxelObjects`, `0x80083838`), each tick: of the objects the grid cell under the player lists (`ObjectGrid.CellAt` `0x800a4110` and `ObjectGrid.Cell` `0x800a568c`, at `0x800838a4`), as A's reach takes them, each active one he's within the radius of (3D, inclusive) whose Enabled holds, read then: a teleporter takes him through, skipping its secret, trap and lock; a trigger goes off, every tick he's there; a dialogue starts its script. Nothing starts while a script runs (`Script.Running`, `0x800520bc`), and a dialogue or teleporter only while no popup, menu or dialogue is up, no trek state is queued and trek is in its state 3 (`MapObjects.CanStart`, `0x80010d08`); none of it waits for a fade, so trek's first frame after a cut-scene or a battle can start one ([scripts](scripts.md#the-world-it-plays-in)). `MapObjectUse.Touch` ports it, through trek's fade-in but not a teleport's fade or a fade-out into a cut-scene or a battle.
- **Trigger triangles** (`MapObjectUse.Tread`, `0x8000a3ec`, which `PerformCallback`'s type 8 calls through `Zones.TriggerHook`), when the player touches one whose object is active: 1,532 name cameras (below) and 317 dialogues, which start their script if its Enabled holds, **however far he is**: the gate's distance test is skipped (`MapObjects.TouchesBody` with 0, `0x8000a52c`). A teleporter named by a triangle would take him through at once, whatever its gates, but none is. The collision asset carries each triangle's object as `_TARGET` ([maps](../formats/maps.md)).
- **The 11 dialogues the triangles name** are listed in no grid cell, so walking within their radius starts nothing: only their triangles do. 10 have radius 200 and one 25 (P25's, `0x1126` #2, whose 24 triangles reach past it). They're story cut-scenes set off on patches of floor: Overworld J15, M08, P22, P25, Q27, R26, R27 (four) and Erromon A14. **The camp south of Gwernia castle** is one: reaching it, M08's trigger (object 0, radius 5) sets flag 58, whose journal entry's notice comes up the next tick ([below](#map-objects-write-flags)) and which enables the camp's dialogue (object 1, by 5521). The Chaos minions' cut-scene (`0x04E0`) starts when he treads on one of its 11 triangles round the camp with the notice closed, as he leaves: west of it at x 36–43, east and south-east at x 52–59, and a patch to the north-east. The user's play agrees: they ring the camp so that leaving it any way starts the cut-scene.

## Camera objects

`MapObjectUse.Cameras.cs`; what a camera object does to the camera is in [cameras](cameras.md#camera-objects-processvoxelcamera). There are 244, reached two ways, and none both:

- **182 the grid lists,** each with a radius under 200. Each tick, before the camera steps (`ProcessVoxelObjects`), those the player's grid cell lists take the camera while he's within their radius and they're active. They're mostly one to a room: each of Gwernia's houses has one that covers it, cutting to a point near the ceiling and tracking him.
- **62 trigger triangles name,** 1,532 triangles between them. Each sits at its chunk's origin with a radius of 200, and no cell lists it. Touching one of its triangles (`set_camera_voxel_pointer`) gives the camera to it for 60 ticks, counting that one, and each touch starts the count again; it steps after the player moves (`ProcessScriptCamera`). A later triangle's camera takes over from an earlier one's, and going through a teleporter lets go (`clear_camera_voxel_pointer`). Gwernia's inn has four, which ease from point to point as he crosses it.

216 move the camera or its aim to a reference point in their own map; the other 28 set how the outdoor camera follows. The original looks the reference point up in the chunk the player is in, and the port in the camera's own, as for trigger triangles. Camera objects work under popups, as the original runs `ProcessVoxelObjects` then; that a trigger triangle's does too is a guess. They work through a teleport's fade too, when nothing else does ([cameras](cameras.md#which-mode)). Teleporters' camera points are where a teleport puts the camera ([cameras](cameras.md#which-mode)); a house's door names the same point its room's camera cuts to.

## Triggers

`MapObjectUse.Trigger` (`trigger_vobject_func`, `0x80010bc4`): SetFlag sets its flag; Animate asks the actor its model links to for an animation; ChangeCollision changes its map's toggles with its id, ANDing their flags with one value and ORing in another. Then the trigger writes its Used flag.

**ChangeCollision** walks the triangles of the chunk the player is in (`Zones.PlayerMap`, `0x800d8288`: its count at `+0x24`, its 32-byte triangles at `+0x3c`), and changes those whose role (`+0x1e & 0xF000`) is `0x1000`, a toggle, and whose id (`+0x1e >> 5 & 0x7F`) is the trigger's (`+0x2c`): the flags at `+0x1c` are ANDed with `+0x2e` and ORed with `+0x30`. They stay so in the loaded Borg9 until the chunk loads again. All nine in the base game keep every flag and add Passable, so they open a way, and each is tangible with no Used flag, gated on a flag, so it opens the way again on every tick the player is within its radius, each time the chunk loads. They're the game's only barriers of collision a flag opens:

| Where | Opens on | What it is |
| --- | --- | --- |
| Overworld P24, object 1 | flag 480, which the riddle's cut-scene (`0x04A3`, node 116) sets | The Jundar gate to the desert, a wall at each end of its passage |
| Overworld T27, object 0 | flag 3699, which its Animate trigger (object 1, the cliff's actor `0x0E74`, request 19) writes as it's used | A slab across the way, beside the cliff teleporter that 3699 enables |
| Overworld D14, object 0 | derived flag 5533 (any of 247, 3460, 3461) | A see-through wall, one quad |
| Chaos Isle C01, object 0 | flag 3437, which its Animate trigger (object 6) writes | The door whose actor `0x0CD9` (object 71) is there while 3437 is clear |
| Chaos Isle A07, object 3 | derived flag 5498 (3269 and 3270, the two levers' Animate triggers 4 and 5) | The door whose actor `0x0CD9` (object 61) is there while 5498 is clear |
| Barrows G02, object 0 | there once flag 533 is set (Lugash beaten), as the chunk loads | The doorway whose actor `0x0CD9` (object 5) is there while 533 is clear |
| Barrows G03, object 5 | flag 533 clear | A doorway open until Lugash is beaten, where actor `0x0CD9` (object 21) stands once he is |
| Barrows G03, object 6 | flag 4279, which the dragon's cut-scene (`0x18FC`) sets | The doorway whose actor `0x0CD9` (object 20) is there while 4279 is clear |
| Barrows H03, object 4 | flag 533 | The doorway whose actor `0x0CD9` (object 10) is there while 533 is clear |

`CollisionWorld.ChangeToggles` ports it. The port collides with every loaded chunk where the original collides only with the player's ([movement](movement.md)), and where chunks overlap a neighbour can hold a plain copy of a toggle: O24 has one of each of the Jundar gate's four triangles in P24, which the original never collides with from P24, and which kept the gate shut in the port after the riddle. So the other loaded chunks' triangles with the same corners as a toggle (within 0.01) change with it, on every call, so a neighbour that loads after the toggles opened follows them the next time the trigger goes off. The gate's is the only copy in the base game; the other eight open with their neighbours loaded (`MapObjectUseTests`).

## Teleporters

Using a door (`teleporter_func`):

1. **A secret door** whose secret flag is clear is found: "You've just discovered a secret door!", and the flag is set. Closing the message uses the door again. The eleven are in EhudUnder.
2. **Its trap**, while it has one and its trap flag is clear, opens the trap's menu, as a container's does (below). A failed disarm hurts the party and goes on through the door; either way, closing the message sets its disarm flag and uses the door again.
3. **Its lock**, while it has one and its lock flag is clear, is picked as a container's is. Picked, closing the message sets its picked flag and uses the door again.
4. **Then the player goes through** (`TeleportArrival`): to the destination chunk, at its arrival point, facing the way the point faces, or at the teleporter's own position if it has none. He's set down as `TeleportPlayer` (`0x8000bb54`) sets him down: near the arrival's height, with his feet there, then from on high, and if that misses too, from on high again (`Body.SetDown`, [movement](movement.md#setting-a-body-down)). With no arrival point, the original sweeps near the height he had before the teleport rather than the teleporter's (`0x8000bd90`); the port uses the teleporter's.

The screen fades out at 2/3 of full brightness a tick, stays black for 3 frames, and fades in at 1/15 a tick (`TrekLoop`), loading another map set if the door leads to one; the camera goes with him, in the interiors' mode or the outdoor one by where he arrives ([cameras](cameras.md#which-mode)). 30 doors are locked and 4 trapped.

## Containers

Using a container (`loot_func`), once it's neither opened, destroyed nor remembered:

1. **A herb, spice or gem source** gives its reagent ([items](items.md#containers)) in the loot window, writes its Used flag and is used up, with its model. If its gates still held, it would be remembered for a while; none does.
2. **A custom container** (loot with its own model) writes its Used flag and is inactive before anything else.
3. **A trap** opens the trap's menu: "There's a slightly / moderately / very dangerous trap!" (below 3, below 6, then the rest) over Disarm and Exit, in pale green on a feathered panel at the screen's centre, 180 wide with 10 between the question and the choices. Exit, or B, leaves it. Disarm tries ([items](items.md#containers)): disarmed, "Trap sucessfully disarmed" (the original's spelling, wrapped at 120), and closing it uses the container again; failed, the container is destroyed, its model swapped, and a box lists "The trap exploded!" and what each living member took. 85 containers are trapped, all chests, sacks, crates or barrels; three have no exploded flag, so they're whole again when their chunk next loads.
4. **A lock** is picked ([items](items.md#containers)) or says why not: "The lock is a little / definitely / far beyond the ability of" the best thief "to pick.", "... is too weak to pick the lock." or "The party could not pick the lock!". Picked, "... successfully picks the lock.", and closing it uses the container again. 117 containers are locked, 61 of them trapped too.
5. **Opened**, it sets its opened and Used flags, shows its opened model (or hides a custom container's), rolls its loot and adds its gold to the party's. Without an opened flag it's remembered as emptied for 10 map loads. With nothing else, "You just picked up N gold!"; otherwise the loot window opens ([ui](ui.md#the-loot-window)), which is the pause menu and stops trek until it closes.

**The explosion** (`Containers.Explode`, `0x8000ffe4`, from the trap's menu at `0x80056364`) starts four fires and a burst where the container stands, each an emitter of the particle pool that code makes ([particles](../formats/particles.md#the-simulation)) and each with its dynamic light ([rendering](rendering.md)), then swaps the model, sets the exploded flag (and reads flag 5626) and plays one of the four explosion sounds at random for 240 ticks (the sounds record's `Explosions`). All draw with particle texture slot 2, Borg1 1, which the base plugin's `aidyn:explosion/0` holds.

- **A fire** (`Explosion.Fire`, `0x800174f8`, over `Explosion.MakeFire`, `0x8001732c`): two flames a frame (interval −2) for the fire's life, each living 120 ticks. A flame is born at scale 0.1 within 0.05 of the fire across the ground, rising at up to 0.4 over its life and drifting at up to 0.0025 a tick, pulled up by 6×10⁻⁵ a tick², growing by the fire's growth over its life, and fading from yellow (1, 1, 0, 1) to a clear dark red (0.5, 0, 0, 0), with up to 0.2 of red and 0.1 of green of its own added over its life (clamped, so it shows only once it's faded below full) (`Explosion.SpawnFlame`, `0x80017138`). Its light drifts between dark red and yellow within 5 for as long.
- **The one that stays** burns 600 ticks, growing its flames by 1. **The three thrown** burn 60, growing theirs by 0.35: each is an emitter that moves itself (emitter flag `0x2000`), a sphere of radius 0.25 leaving the container at 0.036 across the ground every way and 0.12 up, falling at 0.006 a tick² (the pool's one gravity, `0x800d58e4`) and colliding with the world each frame (`ProcessCollisionSphere`), so it rises about 1.2 and lands about 1.5 away, where the ground stops it. Its flames are born where it is, and its light goes with it while it's lit.
- **The burst** (`Explosion.Burst`, `0x80016c00`): 75 particles of grey (0.35, 0.3, 0.25, 1) at scale 0.25, all at once from the container, every way at 0.04 to 0.06 a tick, falling at 6/7000 a tick², bouncing off the container's height (keeping 0.6), and fading to nothing over 420 ticks (over its life less one, `Particles.InverseLife`). Its light, orange fading to nothing within 10, lasts 120. Side by side with the emulator, the whole explosion looks as the original's.

An emitter the pool has no room for comes without its light. `Aidyn.Core.Particles.Explosion` ports it, from `TrekLoop`'s `IMapObjectHost.Explode`; the runtime's `ParticlesView` draws it with the rest of the pool. The pool collides a thrown fire with every loaded chunk's collision (`ParticleHead.World`, which the zone engine gives it), where the original's head holds the one `LoadGameBorgScenes` sets (`+0x338`). With no pool the lights come alone (`DynamicLights.Explosion`).

## Map objects write flags

Every flag a map object writes is followed at once by a read of derived flag 5626 (`getEventFlag(0x15FA)`), whose value nothing uses, and reading a derived flag while anything has changed brings the network up to date (`GameStateFunnel.Read`, `0x800cb3ac`; [flags](flags.md#the-network)). So what the write turns on, a dialogue's gate or a journal entry and its notice, is there before the next tick. The original does it after a trigger's flag (`MapObjectUse.Trigger`, `0x80010bc4`), every Used flag (`MapObjects.WriteUsed`, `0x80011760`, from triggers, harvests and custom containers), a container's opened flag (`MapObjectUse.Loot`, `0x800105ec`), an exploded flag (`Containers.Explode`, `0x8000ffe4`), a secret door's (`Teleporters.FindSecret`, `0x800117c0`) and a talker's (`IMapObjectHost.Talk`, `0x800109fc`). The read comes whether or not the object has a flag in that slot, since `setEventFlag` only refuses 0, so every trigger going off reads it. The port writes them all through `FlagNetwork.TrySetAndProcess`, its empty slots too; written plainly, a new journal entry would wait for the next read of a derived flag ([ui](ui.md#the-journal)), so the camp south of Gwernia castle's notice would come only after its cut-scene. A door's disarmed and picked flags, which the trap's menu and the lock write, aren't followed by the read, and the port writes them plainly.

## Popups

A popup (`TrekPopup`, over the menus' `MenuMessage` and `MenuChoice`) takes the player's input until it's closed, and stops the clock and the potions working (the sundial is hidden), but not the world (`isPaused`): bodies move, animations and clouds go on, a chasing enemy can keep running into the player, and tangible triggers still go off, while doors and dialogues wait for it to close (`vobject_pause_check`). Wanderers don't choose new goals under it (`WanderTick` is skipped; [wanderers](wanderers.md#wandering-noticing-and-chasing)). Its choice pulses a trek frame at a time, where the menus' step every tick, so it isn't a `MenuOverlay`. `TrekPopupView` draws it on a UI screen of its own, under the pause menu.

- **A message** (`TrekTextPopup`, `TextBox_Centered`): off-white text in a feathered black box at the screen's centre, as wide as the text up to 150, wrapping there, A or B closing it.
- **The trap's damage** (`TrapMenu::TakeDamage`): white, in a box 16 wider than its widest line and as tall as its lines at the first line's height plus 2.
- **The trap's menu** (`WidgetChoiceDia::Update`): the question above the choices, the cursor's choice pulsing grey to white, the scroll menu's defaults. The stick moves the cursor, A chooses and B leaves.

## Oriana's path lights

The 23 lights along the path from Oriana's hut (15 in I08, 7 in J08, 1 in K08) go out as the player reaches them, and stay out. Each is a scene object of two levels of detail: scene `0x1910`, an empty scene, to 5, then `0x374A`, the glowing billboard, to 24, so the billboard is drawn from 3 to 24 away and nothing nearer ([draw distance](rendering.md#draw-distance)). They're there once flag 50 is set, by Oriana's conversation (derived flag 5514). The rule is in `RenderVoxelScenes` (`0x8000cda0`–`0x8000ce08`):

- **The test** is the object's second model's index against the literal `0x374A`, as it draws the first level (`0x8000cda4`): when the first is drawn this frame and the second wasn't, the object's active bit (`0x8000` of its flags) is cleared. Drawing the first level needs the object within its radius and in the camera's view, so the light goes out within 3 of the player (2 short of the first level's 5) and in view. The near model is empty, so it vanishes from one frame to the next, with no fade.
- **The memory**: it's written to the chart as type 0, a scene object's, for 16 map loads (`MapObjectMemory.Write`, `0x80056950`). Loading a chunk keeps any scene object the chart names out (`LoadedMap.Activate`, `0x80017af8`), so the light stays out when its chunk loads again, as after the pause menu, until 16 map loads have passed ([saves](saves.md#map-objects-memory)); then it's back. Walking away doesn't bring it back while its chunk stays loaded.
- **The key** is the chunk the player is in, with the light's index in its own map, wherever the light is: the call passes the zone engine's current map set, row and column, with no offset (`0x11`). A light in a neighbour, reached from across the edge, goes out until its own chunk loads again, and the memory names whatever object has its index in the player's chunk, which stays out the next time that chunk loads if it's a scene object. The port does the same (`LoadedMap.PutOut`). It can hardly happen: only object 48 of J08 is within 3 of its chunk's edge.

`SceneObject.GoesOut` carries the test as a fact the importer sets ([hardcoded content](../hardcoded-content.md)). The core decides it in trek's tick (`TrekLoop.PutOut`), once a 30 fps frame, by the original's distances and its 4:3 cone of view (`ViewCone.Original`) whatever the draw distance or the window's shape, so it's the same on every machine and in a headless run. The runtime only takes the light's node out of the scenery (`ITrekHost.PutOut`). It doesn't happen while the free camera flies, which freezes trek. Nor while a cut-scene's camera passes one, which 1.0's code wasn't read for; play never brings one past a lit light, as the player walks through them before any cut-scene's trigger. The user confirmed the lights go out as 1.0's do.

## In the runtime

`TrekLoop` is the `IMapObjectHost`: it handles doors, dialogues and talkers itself and passes the rest to the runtime, which builds a chunk's scenery again when a container's model changes.

## What's left

- Alaron's animations for opening a container (request 11) and a door (19), which the original asks for only when it chose among several objects.
- The explosion's particles, and the popups' fades. Its lights ([rendering](rendering.md#point-lights)) and the containers', gold's and explosion's sounds are ported.
- The editor's map object pages don't use doors, triggers or teleporters; a container's opens in the loot window ([inspector](inspector.md)).
