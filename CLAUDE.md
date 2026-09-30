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
- `src/Aidyn.Plugins`: the plugin format (ADRs 0004 and 0005): record IDs, plugin loading with field-level merges and the conflict report, asset lookup by record ID, the typed records (`Records/`), the catalog the editor browses: every record and asset with a summary, and which records name which (`PluginCatalog`), and the quest atlas's index: what sets and reads each flag, what starts each script, and its graph (`QuestIndex`). The importer (`Aidyn.Rom/Import`) writes the base plugin with these types, and the runtime reads it.
- `src/Aidyn.Core`: the headless rules core (ADR 0003), organised by subsystem, not game mode (ADR 0009). So far `Physics/` (the collision world and the original's sphere physics), `Movement/` (the player's movement and bodies' shadows), `Cameras/` (the outdoor follow camera), `Calendar/` (the game clock), `Actors/` (actors' state machines, which the runtime and the editor both step), `Scenes/` (billboards, scrolling textures, which chunks the original draws, and how scene objects fade with distance and view), `Flags/` (the event flags, the network of derived flags, the engine state written into flags, and whether a map object is there by them), `Scripts/` (the Borg13 VM: it runs a script against the flags and an `IScriptWorld`, and sends its cameras, actors and sounds to an `IScriptHost`), `Rng/` (the original's generator, behind `IRandom`) and `Sky/` (the colours a terrain's sky gives the scenery by the time of day, the gradient, the sun's light on bodies, the sun, moon, stars and clouds, and the lens flare), stepped per 60 Hz tick with per-frame behaviour matched to 30 fps (`Cadence`).
- `src/Aidyn.Cli`: the `aidyn` command-line tool, e.g. `import <rom>` (the ROM as `aidyn.base`, in the data folder or `AIDYN_DATA`), `dotnet run --project src/Aidyn.Cli -- borg dump <rom>`, `db dump <rom>` (every table as JSON), `textures <rom>` (Borg1 and Borg8 as PNG), `scenes <rom>` (Borg5 as glTF, with their Borg6 animations), `actors <rom>` (Borg7 state machines as JSON), `dialogue <rom>` (Borg13 as text), `maps <rom>` (each map set's chunks and objects as JSON, and its collision as glTF), `atlas <rom>` (the quest atlas as JSON), `flag <rom> <n>` (what sets and reads a flag) or `strings <rom> <table>`. Output goes to the gitignored `out/`.
- `src/Aidyn.Game`: the Godot runtime (roadmap Phase 3). It loads plugins only, never the ROM. It streams a map set's chunks with their scenery (`ChunkScenery`, which the editor shares) and collision, and steps the core's player and camera each physics tick, with his shadow. Scenes draw with the N64 material (`N64Material`, shared with the Inspector), tinted and fogged by the sky's colours, with bodies lit as the RSP lights them. Behind them it draws the sky's gradient, sun, moon, stars and clouds, and over them the lens flare. Run it with `tools/godot.ps1 --path src/Aidyn.Game` after `aidyn import`.
- `src/Aidyn.Inspector`: the editor (roadmap 0.6), which grows into the mod editor. The editor reads and edits only the imported plugins, as OpenMW-CS does for its own content, and draws them with the runtime's code (ADR 0010). It lists every record and asset in the installed plugins, with a preview, the merged fields and who set them, and what names each one, with record IDs as links and back and forward. Map sets, maps and map objects preview as the map drawn whole with the runtime's chunk code. The HTML viewers and the raw Borg browser it replaced were deleted on Sep 30, 2026. Run it with `tools/godot.ps1 --path src/Aidyn.Inspector`.
- `tests/Aidyn.Core.Tests`, `tests/Aidyn.Plugins.Tests` and `tests/Aidyn.Rom.Tests`: `dotnet test` from the repo root. Real-data tests find ROMs in `roms/` (or `AIDYN_ROMS`) by hash and skip when they're missing.

## Status

Update this section when a roadmap task finishes or starts. Findings go in the docs above, not here.

- **Done:** 0.1 reference rig, 0.2 ROM identification, 0.3 Borg archive reader, 0.4 DB readers, 0.5 ADRs, 0.6 the editor, 1A static graphics, 1B animation, 1C maps, 1E quest atlas, 1H hardcoded-content audit, M1 Walk Gwernia. The user confirmed each against the emulator or the checkpoints where it's visible.
- **M1 Walk Gwernia, done Sep 29, 2026.** The importer writes `aidyn.base` with map sets, chunks, terrains, scene objects, actors, textures and UI images, and `Aidyn.Game` streams Gwernia's chunks with scenery, actors, and ports of the original's movement, collision, outdoor camera and player shadow. Scenery draws with the N64 material, tinted by the time of day's sky colours; bodies are lit by the sky and the sun as the RSP lights them; the sky's gradient, sun, moon, stars, clouds and lens flare are ported; billboards turn to the camera; water and fire scroll; and scene objects are drawn and faded by the player's distance, the camera's look and whether they come between the camera and the player, as `RenderVoxelScenes` does, with an option to draw further for the fidelity profile. The user checked each against the emulator: chests, trees, bushes, path lights, the see-through tree and the bonfire outside Gwernia all match. Comparing day and night lighting with the console waits for the weather pass, since nothing looks off in the game.
- **0.6 the editor, done Sep 30, 2026,** per ADR 0010: it shows everything the HTML viewers and the Borg browser did, over imported data only, and they're deleted. See [inspector](docs/systems/inspector.md). The user chose (Sep 29) to reach parity before M2; on the way the flag engine (2.1), the Borg13 VM (2.4) and every map object type were imported and ported. The map set view draws with the runtime's `Models` and `ChunkScenery`, and the user found it matched the map viewer. Its scroll zooms toward what's under the cursor, with WASD to fly, after the user found the orbit's zoom stalled and clipped the ground in a whole map set.
  - **For the user to check in the emulator:** the lighthouse keeper's key and Dougal at the harbour reunion (see [scripts](docs/systems/scripts.md#to-check-in-the-emulator)); and the patioL2 door (`aidyn:voxel/0x1871/0`, open it in the editor), which has no arrival point. It's map object 0 of `aidyn:map/0x1871`, the JundarInteriors chunk K07, at (10.2, 4.2, 13.8) in the chunk, beside the reference point MainDoorsPatio. It leads to A01 (`aidyn:map/0x1890`) at point 0x800E, which A01 lacks. K07 holds nothing else, and nothing leads into it, so it's probably a patio cut from Jundar, reachable only by a debug warp.
  - Terrains show no "used by", since chunks name them by number; a record ID there would fix that at the next importer change. When a conversation ends, the original reloads the map and gates its objects again (`loadGameBorgScenes`), which the runtime will need with M2's dialogue triggers.
- **Next: 1F, text and UI assets** (size S), the smallest of what M2 still needs (1F, 2.2 and 2.3; 1E, 2.1 and 2.4 are done). Import every string table, the font (`fontFace`), the portraits and the menu images, and make the gate the dialogue box: the editor's script player showing a line in the game's font with the speaker's portrait. Then 2.2 (characters) and 2.3 (items, chests and shops), which the script player's party and item controls stand in for today, and then M2 itself. The weather (rain's sky, wind, fog) would finish the sky, with the day and night comparison, and belongs with the calendar's weather rules.
- **Left for later:**
  - The renderer's leftovers from 1A and 1B: [docs/systems/rendering.md](docs/systems/rendering.md).
  - 1H's leftovers, at the end of its audit: the content tables marked To do are located but not read. The importer will need them.
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
