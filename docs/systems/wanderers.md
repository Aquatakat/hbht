# Wanderers: monster parties' bodies (M2 session 5)

`Aidyn.Core.Trek` (`Wanderers`, `Wanderer`) and `Aidyn.Core.Movement` (`Body`, `Crowd`). Ported from `wander.cpp`, `PlayerMove.cpp` (`Actor::SetAiDest`, `SetFacing`, `MoveTo`, `Move`) and `PlayerData.cpp` (`ProcessPlayers`, `PlayerShadowAlpha`, `renderPlayers`). What a monster party holds is in [maps](../formats/maps.md#map-objects-in-the-base-plugin-06).

A monster party is an NPC to talk to, if it has a dialogue, or a group of enemies the player sees as its leader. Either way it's one body in the world, which wanders between wander nodes. Talking to it (5c) and what it does on its own (5b) are below as they're ported.

## Bodies as their chunk loads (5a)

- **When.** As a chunk loads, each of its monster parties that's active by its flags ([map-objects](map-objects.md#a-chunks-objects-at-run-time)) and not remembered as beaten gets a body (`look_for_monsterparties`, `AllocWanderer`); as it goes, they go (`FreeWandererFromZone`). The original skips this in dialogue mode and on the title screen.
- **No limit.** The original has room for 39 at once in trek (`WanderHead`) and crashes past it, at least in the debug build. At a new game the 3×3 around Overworld H13 holds 44 parties that would get one, the only block over 39 and the most anywhere; the user thinks the original leaves some out rather than crashing, but can't reach H13 to see. The port gives every party a body, as the user chose (Oct 2, 2026): mods will add their own, and there's no reason in play to leave any out. Across the Overworld's 172 chunks there are 441.
- **Towns behind walls.** Talewok's outskirts (I12) and the town (I13) are divided by a wall with a gate between them, a teleporter within the Overworld, and so are Port Saiid (C15) and C14; Gwernia and Erromon have no such wall. Both chunks are in the same 3×3 either way, so the wall doesn't keep their bodies from loading together. If it was for the original's sake, it was more likely to spare drawing a busy town from outside it.
- **Its look** is the party's entity's body ([actors](../formats/actors.md)): its actor, its collision radius, its scale and its height. Every entity has one, and 32 of the 37 dialogue entities; 28 parties are dialogue entities. The body is drawn at its scale, its animations carry it that much farther, and bodies push each other with spheres that much bigger; its sphere against the world keeps the radius as it is.
- **Where.** At the party's position, set down from the party's height as a teleport sets the player down (`Actor::CheckCollision`). All but one stand within 0.06 of the party's height; a bat in I08 is half a unit lower.
- **Its home node** must be one that wanderers walk to; every party's is. It takes the node's values (`WanderGetNextNode`), and if it's further from the node than the node's radius it sets off for it (`FUN_80012d44`), **walking if the party has flag 0x2 and running otherwise**. The decomp reads that flag as the node's; 1.0's code reads `voxelObjs[VoxelIndex] + 0x58`, the party's flags. 990 of the 1,079 parties walk. The user confirmed the 89 that run are the game's: a man in Port Saiid and a few monsters run about (Oct 2, 2026).
- **Kept in its chunk.** A body other than the player's is kept 0.05 inside its chunk's edges, since the original keeps it in its chunk's coordinates.

## Bodies moving (5a)

A `Body` is the original's `playerData` for a body other than Alaron's, stepped each tick as `ProcessPlayers` steps it:

- **Steering** (`MoveTo`). A body sent somewhere (`SetAiDest`) points its stick at the goal, at half tilt to walk or 0.95 to run, and the stick turns it 20° a frame as the player's does. The original routes the stick through the camera's turn and back, which cancels out. The stick follows the goal only while the body comes nearer than it's been, by 0.01; otherwise it keeps its last way, so a body that starts facing away walks a short curve. Within the goal's radius it stops. In trek a body that can't get nearer keeps trying; in cut-scenes the original gives up after 30 frames and puts it there.
- **Turning** (`SetFacing`) turns it 10° a frame where it stands (`MoveTo`'s `0x3E32B7FE`), half what it turns on the move. A body with neither a goal nor a turn lets go of its stick, and keeps whatever animation it was last asked for.
- **Then** the world's collision, the push down on plain collision, and its animation's root motion, as the player's ([movement](movement.md)). Bodies don't set off trigger triangles (`CSPHERE_NoTriggers`).
- **Only near the camera.** In trek, bodies more than 30 from the camera's aim don't move or animate at all (`PlayerHandler.float_0x68`; 128 in combat).

## Bodies pushing each other (5a)

After each body moves, the player first, it's pushed out of the others in its chunk within 3 in height (`ProcessPlayers`, `FUN_800b003c`; checked in 1.0's code). The push is across the ground only, straight away from the other until they touch, with each sphere at its scale, and the body's velocity bounces and dampens as off a wall. The other doesn't move.

- **The first push starts the turn over** from the second body, not the first, so the player, who is first, isn't weighed again. The body that pushed is weighed again, and touching counts, so whether the body stays or goes back depends on rounding.
- **A second push puts the body back** where it began the tick.
- **The nearest body** to each, across the ground, is kept for A (`playerData.index`, `unk108`). On a miss the original measures it with the other's radius at this body's scale, and so does the port.

## Drawing them (5a)

In the runtime, `WandererView` draws each body's actor at its feet, facing its heading. Bodies show within 20 of the camera's aim and fade over the last 2 units (`PlayerShadowAlpha`). The original also skips those more than 5 from the aim that are outside its view, which only saves drawing. Their shadows show within 10 of the aim, measured from the body's position in its own chunk's coordinates against the aim in the player's chunk's, so a body in a neighbouring chunk seldom has one ([movement](movement.md)).
