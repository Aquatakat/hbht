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
