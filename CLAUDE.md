# hbht

A PC reimplementation of Aidyn Chronicles: The First Mage (N64). It runs on content imported from a user-supplied ROM and has a plugin-based mod system.

## Where to look

Read what your task needs, not all of it.

| For | Read |
| --- | --- |
| The plan, and what a task number (0.3, 1E, 2.10, M1, …) means | [docs/roadmap.md](docs/roadmap.md) |
| Decisions. These are binding. To change one, write a new ADR that supersedes it rather than editing the old one. | [docs/adr/](docs/adr/README.md) |
| How the original plays, from the player's side, written from memory | [docs/engine-behaviour.md](docs/engine-behaviour.md) |
| How a ROM format is laid out, and what decoding it found | [docs/formats/](docs/formats/): `rom`, `db`, `textures`, `geometry`, `scenes`, `animation`, `actors`, `particles`, `dialogue`, `maps`, `quests` |
| Content the engine hardcodes, and which ADR 0008 layer it goes in | [docs/hardcoded-content.md](docs/hardcoded-content.md) |
| How our code ports or runs a system | [docs/systems/](docs/systems/): `importer`, `runtime`, `movement`, `cameras`, `rendering`, `inspector` |
| The user's notes from playing the runtime and timing the original | [docs/playtests.md](docs/playtests.md) |
| The emulator, gdb, the pinned Godot, the ROMs, checkpoint save states and `tools/` | [docs/reference-rig.md](docs/reference-rig.md) |

When you learn something, write it in the doc for that format or system. Add a file only for a new format or subsystem, and add it to this table. The reference rig is only for the rig.

## Glossary

Use these words in code and docs. Where the decomp's name clashes with ours, use ours and cite the decomp's in a `<c>` tag (ADR 0009).

| Term | Means | Don't confuse with |
| --- | --- | --- |
| **Trek** | Free exploration, the original's out-of-combat mode (`gametrek.cpp`). | The systems trek shares with combat, which aren't trek's (ADR 0009). |
| **Combat** | Turn-based fighting (`gameCombat.cpp`, `GameMode_Combat`). The player-facing docs call it battle mode. Arenas are the Battle arenas map set. | |
| **Tick** | 1/60 s, the core's unit of time. `Step(ticks)` takes these (ADR 0006). | A **frame**: one rendered frame of the original, covering `delta` ticks (1–6). **Cadence** is how the port matches per-frame behaviour: a 30 fps frame, 2 ticks. |
| **Borg** | An asset in the ROM's archive, by type (Borg1 texture … Borg13 script) and archive index. | |
| **Scene** | A Borg5: a tree of transforms with models, a camera and lights. Record `aidyn:scene/0x…`. | A Godot scene (`.tscn`). Say "Godot scene" for those. |
| **Actor** | A Borg7: a scene with a state machine of animations. Record `aidyn:actor/0x…`. | The decomp's `Actor::` functions and `PlayerData`, which are a **body** moving through the world: the player, an NPC or a combatant. That code is in `Movement` and `Physics`. |
| **Request** | The animation number the game asks an actor for (0 idle, 2 walk, 3 run, …). | An actor's states. |
| **Entity** | An EntityDB record: a kind of character or creature, with its stats. Record `aidyn:entity/0x…`. | An ECS entity. |
| **Map set** | One of the 17 grids of chunks: the Overworld, the Gwernia interiors, the Battle arenas and so on (`EnumMapDatA`). Record `aidyn:mapset/<n>`. | |
| **Chunk** | One cell of a map set, named by row letter and column (I08, M07): a Borg9 and up to two scenes. The 3×3 around the player are loaded (the decomp's `ZoneEngine`; ours is the chunk streamer). | |
| **Map** | Record `aidyn:map/0x…`: one Borg9, a chunk's collision and map objects. | The world map in the pause menu, and the minimap. |
| **Map object** | An object a Borg9 places: a scene, container, teleporter, trigger, monster party and so on. | The decomp's **voxel**. Don't use that word. |
| **World** | The worldspace: the loaded map set. `CollisionWorld` is its loaded collision. | The decomp's `World` and `TerrainPointer`, which are the calendar, time of day, weather and terrain. |
| **Flag** | One of the 5,627 event flags in `GameStateFunnel`. A flag is **plain** (set by scripts, map objects or code) or **derived** (computed from others). Record `aidyn:flag/<n>`. | The decomp's "game state" and "event flag" mean the same thing. |
| **Script** | A Borg13: a conversation, cut-scene, backdrop or sign. | A **Lua script**, ADR 0008's layer 4. Always say "Lua" for those. |
| **Cut-scene** | A script that moves the camera and actors in the game world. | A **cinematic**: one of the four pre-animated scenes (`CinematicStruct`). |
| **Base plugin** | `aidyn.base`, the plugin the importer writes from the ROM. The runtime and the editor read only plugins (ADR 0001). | |
| **Checkpoint** | A save state the user captured by playing (see the reference rig). | |

## Code

A .NET 10 solution (`Aidyn.slnx`, SDK pinned in `global.json`), laid out per ADR 0002.

- `src/Aidyn.Rom`: ROM identification and per-version offsets (`RomImage`, `RomVersions`), the LZB and LZ01x decompressors, the Borg archive (`BorgArchive`), the text cipher and string tables (`Text/`), the DB block's typed records (`Db/GameDb`), and N64 texel decoding, Borg1 textures, Borg8 UI images, a PNG writer, a minimal F3DEX2 interpreter, Borg2 geometry, Borg5 scenes with Borg3 cameras and Borg4 lights, Borg6 animations and a player that steps them as the game does, Borg7 actors, Borg5 particle emitters, and a glTF writer (`Graphics/`); Borg13 dialogue scripts (`Dialogue/`); map chunk lists, map sets' chunk placement, and Borg9 collision, grids and map objects, with a collision glTF export (`Maps/`); the quest atlas, with the flag tables engine code holds (`Quests/`); and other content tables in the code segment, such as the cinematics, entities' models and the sky's gradients, objects, clouds and lens flare (`Engine/`).
- `src/Aidyn.Plugins`: the plugin format (ADRs 0004 and 0005): record IDs, plugin loading with field-level merges and the conflict report, asset lookup by record ID, and the typed records (`Records/`). The importer (`Aidyn.Rom/Import`) writes the base plugin with these types, and the runtime reads it.
- `src/Aidyn.Core`: the headless rules core (ADR 0003), organised by subsystem, not game mode (ADR 0009). So far `Physics/` (the collision world and the original's sphere physics), `Movement/` (the player's movement and bodies' shadows), `Cameras/` (the outdoor follow camera), `Calendar/` (the game clock) and `Sky/` (the colours a terrain's sky gives the scenery by the time of day, the gradient, the sun's light on bodies, the sun, moon, stars and clouds, and the lens flare), stepped per 60 Hz tick with per-frame behaviour matched to 30 fps (`Cadence`).
- `src/Aidyn.Cli`: the `aidyn` command-line tool, e.g. `import <rom>` (the ROM as `aidyn.base`, in the data folder or `AIDYN_DATA`), `dotnet run --project src/Aidyn.Cli -- borg dump <rom>`, `db dump <rom>` (every table as JSON), `textures <rom>` (Borg1 and Borg8 as PNG), `scenes <rom>` (Borg5 as glTF, with their Borg6 animations), `actors <rom>` (Borg7 state machines as JSON), `dialogue <rom>` (Borg13 as text), `maps <rom>` (each map set's chunks and objects as JSON, and its collision as glTF), `atlas <rom>` (the quest atlas as JSON), `flag <rom> <n>` (what sets and reads a flag) or `strings <rom> <table>`. Output goes to the gitignored `out/`.
- `tools/scene-viewer.html`, `map-viewer.html` and `quest-atlas.html`: three.js viewers for the CLI's output (the formats docs say how to serve them). They get fixes only, no new features (ADR 0009).
- `src/Aidyn.Game`: the Godot runtime (roadmap Phase 3). It loads plugins only, never the ROM. It streams a map set's chunks with their scenery and collision, and steps the core's player and camera each physics tick, with his shadow. Scenes draw with the N64 material (`N64Material`, shared with the Inspector), tinted and fogged by the sky's colours, with bodies lit as the RSP lights them. Behind them it draws the sky's gradient, sun, moon, stars and clouds, and over them the lens flare. Run it with `tools/godot.ps1 --path src/Aidyn.Game` after `aidyn import`.
- `src/Aidyn.Inspector`: the asset inspector (roadmap 0.6), a Godot app over `Aidyn.Rom.Inspection`: every Borg by type and index, with a preview, its decoded fields and a hex dump. It grows into the editor, which reads and edits the imported plugins, like Bethesda's Creation Kit (ADR 0009). Run it with `tools/godot.ps1 --path src/Aidyn.Inspector`.
- `tests/Aidyn.Core.Tests`, `tests/Aidyn.Plugins.Tests` and `tests/Aidyn.Rom.Tests`: `dotnet test` from the repo root. Real-data tests find ROMs in `roms/` (or `AIDYN_ROMS`) by hash and skip when they're missing.

## Status

Update this section when a roadmap task finishes or starts. Findings go in the docs above, not here.

- **Done:** 0.1 reference rig, 0.2 ROM identification, 0.3 Borg archive reader, 0.4 DB readers, 0.5 ADRs, 1A static graphics, 1B animation, 1C maps, 1E quest atlas, 1H hardcoded-content audit. The user confirmed each against the emulator or the checkpoints where it's visible.
- **In progress:** M1 Walk Gwernia. The importer writes `aidyn.base` with map sets, chunks, terrains, scene objects, actors, textures and UI images, and `Aidyn.Game` streams Gwernia's chunks with scenery, actors, and ports of the original's movement, collision, outdoor camera and player shadow. Scenery draws with the N64 material (vertex colour, combiner, fog), tinted by the time of day's sky colours, and bodies are lit by the sky and the sun as the RSP lights them. The sky is ported: its gradient, sun, moon, stars, clouds and lens flare. The user called bodies' lighting correct at night, and the sky's sprites now turn with the scenery (projected as directions at any pitch, past the original's stretching toward the edges, with the original's placement on F6). Also 0.6, the asset inspector, which grows with each track.
- **Next:** the user checks the projected clouds, then the sky against the emulator at dawn, noon and dusk (a detailed pass across the weathers can wait). Then scrolling textures, then draw-distance tiers, which M1 still lists ([rendering](docs/systems/rendering.md) lists what's left). The weather (rain's sky, wind, fog) would finish the sky, but belongs with the calendar's weather rules.
- **Left for later:**
  - The renderer's leftovers from 1A and 1B: [docs/systems/rendering.md](docs/systems/rendering.md).
  - 1H's leftovers, at the end of its audit: flags 955–958 and 960–963 are unexplained, and the content tables marked To do are located but not read. The importer will need them.
  - Feature parity between the Inspector and the HTML viewers, after which the viewers are deleted (ADR 0009).
- **Not started:** everything else after Phase 0.
- **Open rig follow-ups** (from ADR 0006): measure the frame `delta` distribution in trek and combat, and get gdb working after loading a save state.

## Ground rules

- Never commit ROMs or anything derived from them. `roms/` is gitignored.
- The decomp ([blackgamma7/Aidyn](https://github.com/blackgamma7/Aidyn)) isn't vendored. A local clone lives in the gitignored `ref/Aidyn`, next to `ref/AidynEditor`. Read it there and `git -C ref/Aidyn pull` to update it; don't re-clone into scratch space. If `ref/` is missing, clone both repos into it. The decomp has no licence, so port from understanding and don't copy its code.
- The user reaches story checkpoints by playing the game. Don't drive gameplay in the emulator. Ask for in-game actions instead.
- Commit as you go, without asking: commit each finished slice of work once its tests pass, along with its doc and status updates.
- When a roadmap task is complete, evaluate the remaining ones and save a reasonable next step suggestion in CLAUDE.md under Status.
- If you have a well-scoped, composable chunk of work, use a Sonnet 5.5 subagent. Sonnet 5.5 benchmarks at levels similar to GPT's flagship models. Use subagents sychronously: not as a method to speed up development, but as a way to keep token usage low and your context window clean. Since Sonnet 5.5 is new, document your experiences with it and the types of tasks it completed successfully here.
  - **Experiences so far.** Reading three code-segment tables into a record the importer writes, from a spec that gave the record type, the ROM offsets and entry layouts, and the image facts (the sky's tables, Sep 28, 2026): done in one pass, with tests over all three ROMs and doc updates in the house style. It checked the offsets and images itself, and reported what differed from the spec (the table's order, a star with alpha 0) instead of papering over it. A precise spec, with the contract type written first, worked well.
