# 0025. Chunks load off the main thread, where it can't change the game

Status: Accepted · Oct 24, 2026

## Context

Crossing into another chunk loads the chunks that come into the 3×3, and the original stalls as it does. The port stalled too, for longer than a frame: the core took the chunks' collision, map objects and drawn surfaces, and the runtime parsed their scenes' glTFs and built their scenery, their scene objects and the wanderers' bodies, all on the main thread in the frame of the crossing. The user wanted it smoothed. Draw Distance's Extended, which draws 16 chunks more round the 3×3, made it matter more.

Two things constrain how. The core is deterministic per tick ([ADR 0003](0003-headless-rules-core.md), [0017](0017-the-core-steps-every-tick.md)): what it does mustn't depend on how fast another thread runs. And Godot's renderer, in the thread model the projects use, serves other threads' calls on the main thread, between frames: a thread building a scene can be waiting on the main thread, so the main thread waiting on it never goes on.

## Decision

- **What changes the game stays on the main thread, where it was.** A chunk's collision, its map objects activated by the flags, its drawn surfaces, its sounds, lights and wanderers go in when the chunk loads, as before. So does anything that rolls on a stream of the game's.
- **What only reads, and gives the same whenever it's done, may run ahead on another thread:** parsing the scenes a chunk will draw, the triangles its surfaces will have, the actors of its monster parties. A cache filled early gives what it would have given filled late, so nothing the game does depends on when (`ChunkStreamer.Prepare`, `ChunkSurfaces.Warm`, `Models.Warm`). The runtime reads ahead what the next crossing could load.
- **Godot nodes may be built on another thread, outside the tree, and added to it on the main thread.** A chunk's scenery is built off the main thread where it can't be seen arriving, and added a frame or a few later, one chunk a frame. What the game asks of the scenery at once, in the chunk the player is in, is built at once. A wanderer's body has its motion, which the core steps it by, made at once, and its scene built off the thread.
- **The main thread never waits on another thread** while a frame runs. Shared caches hold their locks for the cache alone, never while the renderer is called; where the main thread wants an asset another thread is parsing, it parses its own copy. Stopping the builder serves the renderer while it waits.
- **One thread builds, one job at a time** (`ChunkBuilder`), a build before any reading ahead.

## Consequences

- A headless run, which has no builder, plays as the runtime does, and the tests don't change.
- Code that builds views may be called off the main thread: `Models`, `ChunkScenery`, `N64Material`, `GlbScene` and `TextureScrolls` are safe to. Something new they reach must be too: no Godot node in the tree, no state of the game, and no lock held across a call into the renderer.
- Something that needs a chunk's scenery at once, such as an actor a trigger animates, must be in the chunk the player is in, or build it at once.
- What a scene object's or a body's view starts on the main thread, such as an actor's emitters in the particles' pool, starts as the view is added, not as it's built.
- [docs/systems/runtime.md](../systems/runtime.md#loading-chunks) says what loads on which thread.
