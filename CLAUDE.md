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
| How our code ports or runs a system | [docs/systems/](docs/systems/): `importer`, `runtime`, `movement`, `cameras`, `rendering`, `flags`, `scripts`, `inspector` |
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

- `src/Aidyn.Rom`: ROM identification and per-version offsets (`RomImage`, `RomVersions`), the LZB and LZ01x decompressors, the Borg archive (`BorgArchive`), the text cipher and string tables (`Text/`), the DB block's typed records (`Db/GameDb`), and N64 texel decoding, Borg1 textures, Borg8 UI images, a PNG writer, a minimal F3DEX2 interpreter, Borg2 geometry, Borg5 scenes with Borg3 cameras and Borg4 lights, Borg6 animations and a player that steps them as the game does, Borg7 actors, Borg5 particle emitters, and a glTF writer (`Graphics/`); Borg13 dialogue scripts (`Dialogue/`); map chunk lists, map sets' chunk placement, and Borg9 collision, grids and map objects, with a collision glTF export (`Maps/`); the quest atlas, with the flag tables engine code holds (`Quests/`); other content tables in the code segment, such as the cinematics, entities' models and the sky's gradients, objects, clouds and lens flare (`Engine/`); and a reader for the checkpoints' RAM, which needs no ROM (`Rig/`).
- `src/Aidyn.Plugins`: the plugin format (ADRs 0004 and 0005): record IDs, plugin loading with field-level merges and the conflict report, asset lookup by record ID, the typed records (`Records/`), the catalog the editor browses: every record and asset with a summary, and which records name which (`PluginCatalog`), and the quest atlas's index: what sets and reads each flag, and its graph (`QuestIndex`). The importer (`Aidyn.Rom/Import`) writes the base plugin with these types, and the runtime reads it.
- `src/Aidyn.Core`: the headless rules core (ADR 0003), organised by subsystem, not game mode (ADR 0009). So far `Physics/` (the collision world and the original's sphere physics), `Movement/` (the player's movement and bodies' shadows), `Cameras/` (the outdoor follow camera), `Calendar/` (the game clock), `Actors/` (actors' state machines, which the runtime and the editor both step), `Scenes/` (billboards, scrolling textures, which chunks the original draws, and how scene objects fade with distance and view), `Flags/` (the event flags, the network of derived flags, and the engine state written into flags), `Scripts/` (the Borg13 VM: it runs a script against the flags and an `IScriptWorld`, and sends its cameras, actors and sounds to an `IScriptHost`), `Rng/` (the original's generator, behind `IRandom`) and `Sky/` (the colours a terrain's sky gives the scenery by the time of day, the gradient, the sun's light on bodies, the sun, moon, stars and clouds, and the lens flare), stepped per 60 Hz tick with per-frame behaviour matched to 30 fps (`Cadence`).
- `src/Aidyn.Cli`: the `aidyn` command-line tool, e.g. `import <rom>` (the ROM as `aidyn.base`, in the data folder or `AIDYN_DATA`), `dotnet run --project src/Aidyn.Cli -- borg dump <rom>`, `db dump <rom>` (every table as JSON), `textures <rom>` (Borg1 and Borg8 as PNG), `scenes <rom>` (Borg5 as glTF, with their Borg6 animations), `actors <rom>` (Borg7 state machines as JSON), `dialogue <rom>` (Borg13 as text), `maps <rom>` (each map set's chunks and objects as JSON, and its collision as glTF), `atlas <rom>` (the quest atlas as JSON), `flag <rom> <n>` (what sets and reads a flag) or `strings <rom> <table>`. Output goes to the gitignored `out/`.
- `tools/scene-viewer.html`, `map-viewer.html` and `quest-atlas.html`: three.js viewers for the CLI's output (the formats docs say how to serve them). They get fixes only, no new features (ADR 0009), and are deleted once the editor shows what they show (ADR 0010).
- `src/Aidyn.Game`: the Godot runtime (roadmap Phase 3). It loads plugins only, never the ROM. It streams a map set's chunks with their scenery and collision, and steps the core's player and camera each physics tick, with his shadow. Scenes draw with the N64 material (`N64Material`, shared with the Inspector), tinted and fogged by the sky's colours, with bodies lit as the RSP lights them. Behind them it draws the sky's gradient, sun, moon, stars and clouds, and over them the lens flare. Run it with `tools/godot.ps1 --path src/Aidyn.Game` after `aidyn import`.
- `src/Aidyn.Inspector`: the asset inspector (roadmap 0.6), which becomes the editor. The editor reads and edits only the imported plugins, as OpenMW-CS does for its own content, and draws them with the runtime's code (ADR 0010). Its Plugins tab lists every record and asset in the installed plugins, with a preview, the merged fields and who set them, and what names each one, with record IDs as links and back and forward. Its other tab is the raw Borg browser over `Aidyn.Rom.Inspection`, which is frozen: fixes only, and it's deleted along with the HTML viewers. Run it with `tools/godot.ps1 --path src/Aidyn.Inspector`.
- `tests/Aidyn.Core.Tests`, `tests/Aidyn.Plugins.Tests` and `tests/Aidyn.Rom.Tests`: `dotnet test` from the repo root. Real-data tests find ROMs in `roms/` (or `AIDYN_ROMS`) by hash and skip when they're missing.

## Status

Update this section when a roadmap task finishes or starts. Findings go in the docs above, not here.

- **Done:** 0.1 reference rig, 0.2 ROM identification, 0.3 Borg archive reader, 0.4 DB readers, 0.5 ADRs, 1A static graphics, 1B animation, 1C maps, 1E quest atlas, 1H hardcoded-content audit, M1 Walk Gwernia. The user confirmed each against the emulator or the checkpoints where it's visible.
- **M1 Walk Gwernia, done Sep 29, 2026.** The importer writes `aidyn.base` with map sets, chunks, terrains, scene objects, actors, textures and UI images, and `Aidyn.Game` streams Gwernia's chunks with scenery, actors, and ports of the original's movement, collision, outdoor camera and player shadow. Scenery draws with the N64 material, tinted by the time of day's sky colours; bodies are lit by the sky and the sun as the RSP lights them; the sky's gradient, sun, moon, stars, clouds and lens flare are ported; billboards turn to the camera; water and fire scroll; and scene objects are drawn and faded by the player's distance, the camera's look and whether they come between the camera and the player, as `RenderVoxelScenes` does, with an option to draw further for the fidelity profile. The user checked each against the emulator: chests, trees, bushes, path lights, the see-through tree and the bonfire outside Gwernia all match. Comparing day and night lighting with the console waits for the weather pass, since nothing looks off in the game.
- **In progress: the editor (0.6), per ADR 0010.** The user chose (Sep 29, 2026) to bring the editor up to the HTML viewers over imported data before going on to M2. Do these in order:
  1. **Open plugins. Done Sep 29, 2026:** see [inspector](docs/systems/inspector.md). Terrains show no "used by", since chunks name them by number; a record ID there would fix that at the next importer change.
  2. **Scenes and actors. Done Sep 29, 2026:** see [inspector](docs/systems/inspector.md). The state machine is the core's `ActorStateMachine`, and the editor links the runtime's `ActorAnimator`.
  3. **2.1, the flag engine. Done Sep 29, 2026:** see [flags](docs/systems/flags.md). The importer writes the flags, journal and state flags. The core's network reproduces every derived and calendar flag in all eight checkpoints. The runtime gates scene objects as their chunk loads, and the editor has flag and journal pages that load a checkpoint's flags. In the original, the party's best stat and skill flags are never written, and the port keeps that.
  4. **2.4, the Borg13 VM. Done Sep 29, 2026:** see [scripts](docs/systems/scripts.md) and [inspector](docs/systems/inspector.md). The importer writes every script as an `aidyn:dialogue` record, and the names of the DB records scripts name. The core's `ScriptRunner` runs them, checked against the debug build's machine code, and every base-game script plays to an end with random states, apart from two data bugs. The editor draws a script as a graph and plays it against the editor's flags, and its Atlas tab is the quest atlas over plugin records. When a conversation ends, the original reloads the map and gates its objects again (`loadGameBorgScenes`), which the runtime will need with M2's dialogue triggers. For the user to check in the emulator: the lighthouse keeper's key and Dougal at the harbour reunion (see [scripts](docs/systems/scripts.md#to-check-in-the-emulator)).
  5. **The remaining map object types. Next.** Import containers, teleporters, triggers, monster parties, dialogue objects and the rest (M2 needs them). Dialogue objects start scripts, with a reference point and gates, so the atlas gets what starts each script, and its graph is complete. The runner's `ScriptStart` takes their reference point and ID.
  6. **Map sets (M–L), the map viewer's features.** The user put this after the flags and map objects (Sep 29, 2026): a map set's preview becomes a full render of all its chunks' scenery and scene objects, drawn with the runtime's `Models` and chunk code, replacing the chunk grid, which is hard to read. Then collision coloured by surface or by role, the Expansion Pak switch, a hover readout (chunk, surface, role, position), markers for every map object type, and picking an object to see it. Then delete the HTML viewers and the Borg browser.

  After that, M2 needs 1F (text and UI assets), 2.2 and 2.3. The weather (rain's sky, wind, fog) would finish the sky, with the day and night comparison, and belongs with the calendar's weather rules. [rendering](docs/systems/rendering.md) lists the renderer's leftovers.
- **Left for later:**
  - The renderer's leftovers from 1A and 1B: [docs/systems/rendering.md](docs/systems/rendering.md).
  - 1H's leftovers, at the end of its audit: flags 955–958 and 960–963 are unexplained, and the content tables marked To do are located but not read. The importer will need them.
- **Not started:** everything else after Phase 0.
- **Open rig follow-ups** (from ADR 0006): measure the frame `delta` distribution in trek and combat, and get gdb working after loading a save state.

## Ground rules

- Never commit ROMs or anything derived from them. `roms/` is gitignored.
- The decomp ([blackgamma7/Aidyn](https://github.com/blackgamma7/Aidyn)) isn't vendored. A local clone lives in the gitignored `ref/Aidyn`, next to `ref/AidynEditor`. Read it there and `git -C ref/Aidyn pull` to update it; don't re-clone into scratch space. If `ref/` is missing, clone both repos into it. The decomp has no licence, so port from understanding and don't copy its code.
- The user reaches story checkpoints by playing the game. Don't drive gameplay in the emulator. Ask for in-game actions instead.
- Commit as you go, without asking: commit each finished slice of work once its tests pass, along with its doc and status updates.
- When a roadmap task is complete, evaluate the remaining ones and save a reasonable next step suggestion in CLAUDE.md under Status.
- Hand a well-scoped, composable chunk of work to a Sonnet 5.5 subagent, synchronously: not to go faster, but to keep token usage low and your context clean. It does well at:
  - **Readers and importers over known layouts:** a ROM table, a Borg type or checkpoint RAM into typed records, given the offsets, the entry layout and the record type.
  - **Indexes and queries over plugin records,** given the types and method signatures and what each use means.
  - **Real-data tests,** given the facts to check. It checks a spec's facts against the data before asserting them, and says where the spec was wrong or left a gap instead of papering over it.

  Write the contract first (the record types, signatures and doc facts), then give it the semantics, the edge cases and the tests to write, and review what it hands back: its slips are small (test names with underscores, a lazily built list re-read on every access, keeping an unused parameter the spec named). It doesn't build the Godot projects, so build those yourself. Keep work that needs judgement across the codebase, or disassembly to settle what the original does, for yourself.
