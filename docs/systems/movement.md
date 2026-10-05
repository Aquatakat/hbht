# Movement and collision

`Aidyn.Core.Physics` and `Aidyn.Core.Movement`. The user's timings they were checked against are in [playtests](../playtests.md).

Ported (`Aidyn.Core.Physics` and `Movement`, from `Actor::Move`, the player loop in `PlayerData.cpp`, `collide*.cpp` and `physics.cpp`). The formulas that matter were checked against the 1.0 ROM.

- Alaron is a sphere of radius 0.5 with gravity of 0.003 a tick², about 10.8 m/s². Each tick, the sphere steps by its velocity. Then it's pushed out of each triangle it's in, and out of the edges and corners the triangle flags as open. A ledge lower than the radius lifts it: the face below the sphere's centre misses, and the top pushes it up. Higher ledges stop it.
- His animations move him. Each tick adds the root's forward motion, except on the tick a state is entered and the two after. That makes his walk about 1.26 units a second and his run about 3.6. Those fit the user's timings of 24 s and 9 s over the same 30-odd units. The stand-in's 1.9 and 5 were too fast.
- **Pace** (F5, or `--pace`) plays him faster, steps and all. 1 is the original's, and the brisk profile is 1.3.
- He turns toward the stick at **20° a frame**, which is 10° a tick. The decomp reads 2°, but the ROM's constant is `0x3EB2B7FE`, 20° in radians.
- Most surfaces keep no velocity on contact. The plain collision surfaces 1 and 2 keep all of it and get an extra push down each frame.
- Falling faster than 0.048 a tick stops his steering. Past 0.3 a tick, he's put back where he last stood.
- Quirks kept from the ROM:
  - The edge test places its closest point with the sweep's parameter instead of the edge's.
  - A bounce off an edge or corner only turns velocity already heading away from it.
  - A point exactly on a triangle's edge counts as inside for only half the triangles. A sphere balanced exactly on some shared edges can drop through.
- Other bodies, wanderers for now, move by the same rules, steered by a goal instead of a stick, and push each other and the player apart: see [wanderers](wanderers.md).
- Left out: the original only collides with the chunk the player is in, and keeps him 0.05 from its edges. The port collides with every loaded chunk.

## Shadows

`BodyShadow` (`Aidyn.Core.Movement`), from `Actor::CalculateShadow`, `renderPlayerShadows` and `PlaneObj_Render`, checked against the ROM. The runtime draws it.

- Each tick, after he and the camera move, it sweeps a sphere of radius 0.5 from his centre down 3, skipping see-through triangles. It lies where that hits, directly below him, turned to the triangle's normal.
- **Unlike the original, it skips walls.** The original takes the first triangle its collision grid lists that the sweep touches. Against a leaning wall that's often the wall, and the shadow stands up on it. The user has seen this in the original, but rarely. Our grid's order differs from the original's, and so do the spots where it happens. The port takes the nearest triangle no steeper than 60°, and treats walls as no ground. With Alaron settled by the physics at every half unit of I08, the original's rule tilts the shadow past 45° at 263 of 23,039 spots and the port's at 12. At 28, nothing under 60° is in reach, so it keeps its last placement.
- It's a square whose corners are twice his radius from its middle, of the shadow texture (`aidyn:texture/0x0003`, `BORG1_PlayerShadow`) in black, with the texture's intensity as alpha, times 80 of 255. It shrinks and fades as he rises. With no ground in reach, it keeps its last height and follows him in X and Z.
- Shadows are drawn within 20 of the camera's aim in trek and 128 in combat. Over the last 2 units, the fade starts from 255 rather than 128, so a shadow darkens before it goes. Other bodies' shadows show only within 10 of the aim, and come with the bodies.
- The original draws a flat square without a depth test, before the bodies and the centre chunk's objects, so it never sinks into the ground. The runtime projects the same square as a decal, which bodies' visual layer (2) is left out of. It looks the same, but can't sink where the visible ground and the collision differ. The decal's box reaches 0.75 above and below the ground and fades toward its ends, so where the ground bends there's no hard edge. A wall or tree right beside him still takes a little, as the original's square would.
- It blends in on-screen values, as the N64's does (see [rendering](rendering.md#on-screen-values)). Before, it took a corrected alpha for Godot's linear light, which was exact only where the texture is opaque.
