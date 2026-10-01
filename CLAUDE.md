# hbht

A PC reimplementation of Aidyn Chronicles: The First Mage (N64). It runs on content imported from a user-supplied ROM and has a plugin-based mod system.

## Where to look

Read what your task needs, not all of it.

| For | Read |
| --- | --- |
| The plan, and what a task number (0.3, 1E, 2.10, M1, …) means | [docs/roadmap.md](docs/roadmap.md) |
| Decisions. These are binding. To change one, write a new ADR that supersedes it rather than editing the old one. | [docs/adr/](docs/adr/README.md) |
| How the original plays, from the player's side, written from memory | [docs/engine-behaviour.md](docs/engine-behaviour.md) |
| How a ROM format is laid out, and what decoding it found | [docs/formats/](docs/formats/): `rom`, `db`, `textures`, `geometry`, `scenes`, `animation`, `actors`, `particles`, `dialogue`, `maps`, `quests`, `text` |
| Content the engine hardcodes, and which ADR 0008 layer it goes in | [docs/hardcoded-content.md](docs/hardcoded-content.md) |
| How our code ports or runs a system | [docs/systems/](docs/systems/): `importer`, `runtime`, `movement`, `cameras`, `rendering`, `flags`, `scripts`, `characters`, `items`, `inspector`, `ui` |
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
| **Map object** | An object a Borg9 places: a scene, container, teleporter, trigger, monster party and so on. Record `aidyn:voxel/0x…/<n>`, after the original's own name, a voxel object. | A **voxel** alone: the original also uses the word for a chunk's collision (`CollideCollisionSphereWithVoxelPolys`). Say map object in code and docs, and voxel only in record IDs. |
| **World** | The worldspace: the loaded map set. `CollisionWorld` is its loaded collision. | The decomp's `World` and `TerrainPointer`, which are the calendar, time of day, weather and terrain. |
| **Flag** | One of the 5,627 event flags in `GameStateFunnel`. A flag is **plain** (set by scripts, map objects or code) or **derived** (computed from others). Record `aidyn:flag/<n>`. | The decomp's "game state" and "event flag" mean the same thing. |
| **Script** | A Borg13: a conversation, cut-scene, backdrop or sign. | A **Lua script**, ADR 0008's layer 4. Always say "Lua" for those. |
| **Cut-scene** | A script that moves the camera and actors in the game world. | A **cinematic**: one of the four pre-animated scenes (`CinematicStruct`). |
| **Base plugin** | `aidyn.base`, the plugin the importer writes from the ROM. The runtime and the editor read only plugins (ADR 0001). | |
| **Checkpoint** | A save state the user captured by playing (see the reference rig). | |

## Code

A .NET 10 solution (`Aidyn.slnx`, SDK pinned in `global.json`), laid out per ADR 0002.

- `src/Aidyn.Rom`: ROM identification and per-version offsets (`RomImage`, `RomVersions`), the LZB and LZ01x decompressors, the Borg archive (`BorgArchive`), the text cipher and string tables (`Text/`), the DB block's typed records (`Db/GameDb`), and N64 texel decoding, Borg1 textures, Borg8 UI images, a PNG writer, a minimal F3DEX2 interpreter, Borg2 geometry, Borg5 scenes with Borg3 cameras and Borg4 lights, Borg6 animations and a player that steps them as the game does, Borg7 actors, Borg5 particle emitters, and a glTF writer (`Graphics/`); Borg13 dialogue scripts (`Dialogue/`); map chunk lists, map sets' chunk placement, and Borg9 collision, grids and map objects, with a collision glTF export (`Maps/`); the quest atlas, with the flag tables engine code holds (`Quests/`); other content tables in the code segment, such as the cinematics, entities' models and portraits, the level and training tables, the item tables (potion prices, recipes, icons, inventory slots), the fonts, and the sky's gradients, objects, clouds and lens flare (`Engine/`); and a reader for the checkpoints' RAM, which needs no ROM (`Rig/`).
- `src/Aidyn.Plugins`: the plugin format (ADRs 0004 and 0005): record IDs, plugin loading with field-level merges and the conflict report, asset lookup by record ID, the typed records (`Records/`), the catalog the editor browses: every record and asset with a summary, and which records name which (`PluginCatalog`), and the quest atlas's index: what sets and reads each flag, what starts each script, and its graph (`QuestIndex`). The importer (`Aidyn.Rom/Import`) writes the base plugin with these types, and the runtime reads it.
- `src/Aidyn.Core`: the headless rules core (ADR 0003), organised by subsystem, not game mode (ADR 0009). So far `Physics/` (the collision world and the original's sphere physics), `Movement/` (the player's movement and bodies' shadows), `Cameras/` (the outdoor follow camera), `Calendar/` (the game clock), `Actors/` (actors' state machines, which the runtime and the editor both step), `Scenes/` (billboards, scrolling textures, which chunks the original draws, and how scene objects fade with distance and view), `Flags/` (the event flags, the network of derived flags, the engine state written into flags, and whether a map object is there by them), `Scripts/` (the Borg13 VM: it runs a script against the flags and an `IScriptWorld`, and sends its cameras, actors and sounds to an `IScriptHost`), `Rng/` (the original's generator, behind `IRandom`), `Characters/` (character sheets: stats, skills, hit points, experience and spells, training, and the party's skill checks), `Items/` (equipment, the inventory, shops, containers, locks and traps, and crafting), `Ui/` (the font engine's layout and wrapping, and the conversation box) and `Sky/` (the colours a terrain's sky gives the scenery by the time of day, the gradient, the sun's light on bodies, the sun, moon, stars and clouds, and the lens flare), stepped per 60 Hz tick with per-frame behaviour matched to 30 fps (`Cadence`).
- `src/Aidyn.Cli`: the `aidyn` command-line tool, e.g. `import <rom>` (the ROM as `aidyn.base`, in the data folder or `AIDYN_DATA`), `dotnet run --project src/Aidyn.Cli -- borg dump <rom>`, `db dump <rom>` (every table as JSON), `textures <rom>` (Borg1 and Borg8 as PNG), `scenes <rom>` (Borg5 as glTF, with their Borg6 animations), `actors <rom>` (Borg7 state machines as JSON), `dialogue <rom>` (Borg13 as text), `maps <rom>` (each map set's chunks and objects as JSON, and its collision as glTF), `atlas <rom>` (the quest atlas as JSON), `flag <rom> <n>` (what sets and reads a flag) or `strings <rom> [<table>]` (one string table, or every one to files). Output goes to the gitignored `out/`.
- `src/Aidyn.Game`: the Godot runtime (roadmap Phase 3). It loads plugins only, never the ROM. It streams a map set's chunks with their scenery (`ChunkScenery`, which the editor shares) and collision, and steps the core's player and camera each physics tick, with his shadow. Scenes draw with the N64 material (`N64Material`, shared with the Inspector), tinted and fogged by the sky's colours, with bodies lit as the RSP lights them. Behind them it draws the sky's gradient, sun, moon, stars and clouds, and over them the lens flare. UI draws on a low-resolution screen, 240 lines as the original's or more for smaller UI, scaled up as the console's video output does (`UiScreen`), and `ConversationBoxView` draws a conversation's box in the game's font. Run it with `tools/godot.ps1 --path src/Aidyn.Game` after `aidyn import`.
- `src/Aidyn.Inspector`: the editor (roadmap 0.6), which grows into the mod editor. The editor reads and edits only the imported plugins, as OpenMW-CS does for its own content, and draws them with the runtime's code (ADR 0010). It lists every record and asset in the installed plugins, with a preview, the merged fields and who set them, and what names each one, with record IDs as links and back and forward. Map sets, maps and map objects preview as the map drawn whole with the runtime's chunk code. Entities preview as their character sheet, with training priced by the core and equipment to put on and take off; items, shops, loot tables and recipes preview with what they do for the editor's party; and scripts play against a party of the core's characters, with its inventory, a checkpoint's or a new game's. The HTML viewers and the raw Borg browser it replaced were deleted on Sep 30, 2026. Run it with `tools/godot.ps1 --path src/Aidyn.Inspector`.
- `tests/Aidyn.Core.Tests`, `tests/Aidyn.Plugins.Tests` and `tests/Aidyn.Rom.Tests`: `dotnet test` from the repo root. Real-data tests find ROMs in `roms/` (or `AIDYN_ROMS`) by hash and skip when they're missing.

## Status

Update this section when a roadmap task finishes or starts. Findings go in the docs above, not here.

- **Done:** Phase 0 (0.1–0.6, the editor last, on Sep 30, 2026), 1A, 1B, 1C, 1E, 1F, 1H and M1 Walk Gwernia; 2.1 (the flag engine) and 2.4 (the Borg13 VM) came with the editor. 2.2 (characters) on Sep 30, 2026. The user confirmed each against the emulator or the checkpoints where it's visible. 2.3 (items and inventory) on Oct 1, 2026, with the editor's item, shop, loot and recipe pages ([items](docs/systems/items.md)); the checkpoints confirm its equipment and inventory, and its shop prices wait for the user (below).
- **Next: M2 Talk and trade**, now that 2.1–2.4 are done: map objects at run time (containers, teleporters, triggers, dialogue), conversations with the conversation box, shops with the barter window, and the pause menu, in the runtime, on the core's rules and the editor's pages as reference. It brings joining and leaving with the member flags (the quest index's `MemberAdded`/`MemberRemoved` are still unindexed). 2.7 (time and camping) and 2.8 (saves) are small and can slot in around it. Boss flags, which battles set, come with 2.5.
- **For the user to check in the emulator:** the lighthouse keeper's key and Dougal at the harbour reunion ([scripts](docs/systems/scripts.md#to-check-in-the-emulator)); and a few shop prices, buying and selling, with a checkpoint's party, to compare with the port's barter factor ([items](docs/systems/items.md#shops)).
- **Decided, for the fidelity profile at the end:** the UI draws a floating conversation box on a screen of any shape and size; what a faithful mode would take is in [ui](docs/systems/ui.md#a-faithful-mode-later).
- **Left for later:** the renderer's leftovers ([rendering](docs/systems/rendering.md#whats-left)), the weather among them, and 1H's content tables marked To do, which the importer will need.
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
