# Using map objects (M2 session 4)

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

## What A reaches

`MapObjectUse.Reach` (`obj_ref_func`): of the active objects that aren't tangible, those the player is within reach of by each type's test, up to 16:

| Type | In reach |
| --- | --- |
| Container | Within its radius plus the player's, and a herb, spice, gem or custom container always; a chest, sack, crate or barrel only until opened, and only if it holds gold, a loot table or an item (`container_obj_check`) |
| Teleporter, trigger, dialogue | Within its radius, while its Enabled holds |
| Save point | Within its radius, and using it does nothing |
| Monster party | Comes with wanderers (session 5) |

One alone is used. Of several, the one with the best (farthest − its distance) / farthest + (1 + the cosine of its bearing off the player's heading) / 2 is used, the later on a tie. The original walks its grid cell's object list rather than the chunk's; real-data checks show every container, teleporter, trigger and save point is listed in every cell it reaches, so the two agree, except for 11 dialogues whose radius reaches cells that don't list them.

## Walking into objects and onto triangles

- **Tangible objects** (`ProcessVoxelObjects`), each tick, while the player is within the radius of an active one whose Enabled holds: a teleporter takes him through, skipping its secret, trap and lock; a trigger goes off, every tick he's there; a dialogue starts its script (session 5).
- **Trigger triangles** (`check_trigger`), when the player touches one whose object is active: 1,532 name cameras (4c) and 317 dialogues, which start their script if he's within the dialogue's radius and its Enabled holds. A teleporter named by a triangle would take him through at once, whatever its gates, but none is. The collision asset carries each triangle's object as `_TARGET` ([maps](../formats/maps.md)).

## Triggers

`MapObjectUse.Trigger` (`trigger_vobject_func`): SetFlag sets its flag; Animate asks the actor its model links to for an animation; ChangeCollision changes its map's toggles with its id, ANDing their flags with one value and ORing in another. All nine in the base game keep every flag and add Passable, so they open a way, and each is tangible and gated on a flag, so it opens the way again each time the chunk loads and the player comes near. Then the trigger writes its Used flag.

## Teleporters

Using a door (`teleporter_func`):

1. **A secret door** whose secret flag is clear is found: "You've just discovered a secret door!", and the flag is set. Closing the message uses the door again. The eleven are in EhudUnder.
2. **Its trap**, while it has one and its trap flag is clear, opens the trap's menu, as a container's does (below). A failed disarm hurts the party and then goes on through the door; either way, closing the message sets its disarm flag and uses the door again.
3. **Its lock**, while it has one and its lock flag is clear, is picked as a container's is. Picked, closing the message sets its picked flag and uses the door again.
4. **Then the player goes through** (`TeleportArrival`): to the destination chunk, at its arrival point, facing the way the point faces, or at the teleporter's own position if it has none. He's set down by a sphere swept from 1 above to 1 below where his feet would be, then from 100 to −100 (`Actor::CheckCollision`), resting 0.01 above what it hits. Of the 894 teleporters, 888 settle so; four of the six that don't have a floor the sweep's front misses, and two arrive over nothing.

30 doors are locked and 4 trapped.

## Containers

Using a container (`loot_func`), once it's neither opened, destroyed nor remembered:

1. **A herb, spice or gem source** gives its reagent ([items](items.md#containers)) in the loot window, writes its Used flag and is used up, with its model. If its gates still held, it would be remembered for a while; none does.
2. **A custom container** (loot with its own model) writes its Used flag and is inactive before anything else.
3. **A trap** opens the trap's menu: "There's a slightly / moderately / very dangerous trap!" (below 3, below 6, then the rest) over Disarm and Exit, in pale green on a feathered panel at the screen's centre, 180 wide with 10 between the question and the choices. Exit, or B, leaves it. Disarm tries ([items](items.md#containers)): disarmed, "Trap sucessfully disarmed" (the original's spelling, wrapped at 120), and closing it uses the container again; failed, the container is destroyed, its model swapped, and a box lists "The trap exploded!" and what each living member took. 85 containers are trapped, all chests, sacks, crates or barrels; three have no exploded flag, so they're whole again when their chunk next loads.
4. **A lock** is picked ([items](items.md#containers)) or says why not: "The lock is a little / definitely / far beyond the ability of" the best thief "to pick.", "... is too weak to pick the lock." or "The party could not pick the lock!". Picked, "... successfully picks the lock.", and closing it uses the container again. 117 containers are locked, 61 of them trapped too.
5. **Opened**, it sets its opened and Used flags, shows its opened model (or hides a custom container's), rolls its loot and adds its gold to the party's. Without an opened flag it's remembered as emptied for 10 map loads. With nothing else, "You just picked up N gold!"; otherwise the loot window opens.

## Popups

A popup takes the player's input until it's closed (`TrekPopup`), and stops the clock, with the potions working, but not the world (`isPaused`): bodies move, animations and clouds go on, and tangible triggers still go off, while doors and dialogues wait for it to close (`vobject_pause_check`). The user confirmed it in the original on Oct 1, 2026: the sun stops while a popup is up (the sundial is hidden), and Alaron's animations, the clouds and enemies' movements go on, so a chasing enemy can keep running into the player through a conversation. Wanderers don't choose new goals under a popup either (`WanderTick` is skipped), which the user couldn't see as easily but takes as true; it comes with wanderers in session 5.

- **A message** (`TrekTextPopup`, `TextBox_Centered`): off-white text in a feathered black box at the screen's centre, as wide as the text up to 150, wrapping there, A or B closing it.
- **The trap's damage** (`TrapMenu::TakeDamage`): white, in a box 16 wider than its widest line and as tall as its lines at the first line's height plus 2.
- **The trap's menu** (`WidgetChoiceDia`): the stick moves the cursor, A chooses and B leaves.

## In the runtime

The runtime's chunk streamer keeps each loaded chunk's `LoadedMap` beside its collision and scenery, and builds a chunk's scenery again when a container's model changes. `Game` is the `IMapObjectHost`, and `TrekPopupView` draws the popups on a UI screen of their own, under the pause menu. On a new game, A beside the chest at the start gives 4 Healing Potions and shows it opened, and the door to Gwernia's house A (Overworld M07) leads into the Gwernia interiors' A03.

## Not ported yet

- Alaron's animations for opening a container (request 11) and a door (19), which the original asks for only when it chose among several objects.
- Sounds (containers' by kind, the coin jingle, the explosion), the explosion's lights and particles, and the popups' fades.
- Camera objects and the interiors' camera (4c), the loot window (4b), and monster parties and dialogues (session 5).
- The editor's map object pages don't use objects yet.
- The trap menu's layout follows the decomp's reading of `WidgetChoiceDia::Update`, the question above the choices, with the cursor's choice pulsing grey to white, the scroll menu's defaults. The user finds it looks right, and will confirm it in the ROM ([to-check](../to-check.md)).
