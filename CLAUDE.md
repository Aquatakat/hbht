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
| How our code ports or runs a system | [docs/systems/](docs/systems/): `importer`, `runtime`, `movement`, `cameras`, `rendering`, `flags`, `scripts`, `characters`, `items`, `map-objects`, `wanderers`, `saves`, `inspector`, `ui` |
| The user's notes from playing the runtime and timing the original | [docs/playtests.md](docs/playtests.md) |
| What only the user can settle, in the emulator or the ROM | [docs/to-check.md](docs/to-check.md) |
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

- `src/Aidyn.Rom`: ROM identification and per-version offsets (`RomImage`, `RomVersions`), the LZB and LZ01x decompressors, the Borg archive (`BorgArchive`), the text cipher and string tables (`Text/`), the DB block's typed records (`Db/GameDb`), and N64 texel decoding, Borg1 textures, Borg8 UI images, a PNG writer, a minimal F3DEX2 interpreter, Borg2 geometry, Borg5 scenes with Borg3 cameras and Borg4 lights, Borg6 animations and a player that steps them as the game does, Borg7 actors, Borg5 particle emitters, and a glTF writer (`Graphics/`); Borg13 dialogue scripts (`Dialogue/`); map chunk lists, map sets' chunk placement, and Borg9 collision, grids and map objects, with a collision glTF export (`Maps/`); the quest atlas, with the flag tables engine code holds (`Quests/`); other content tables in the code segment, such as the cinematics, entities' models and portraits, the level and training tables, the item tables (potion prices, recipes, icons, inventory slots), the fonts, and the sky's gradients, objects, clouds and lens flare (`Engine/`); and a reader for the checkpoints' RAM, which needs no ROM, and turns one into a save (`Rig/`).
- `src/Aidyn.Plugins`: the plugin format (ADRs 0004 and 0005): record IDs, plugin loading with field-level merges and the conflict report, asset lookup by record ID, the typed records (`Records/`), the catalog the editor browses: every record and asset with a summary, and which records name which (`PluginCatalog`), the quest atlas's index: what sets and reads each flag, what starts each script, and its graph (`QuestIndex`), and the save format (`Saves/`). The importer (`Aidyn.Rom/Import`) writes the base plugin with these types, and the runtime reads it.
- `src/Aidyn.Core`: the headless rules core (ADR 0003), organised by subsystem, not game mode (ADR 0009). So far `Physics/` (the collision world and the original's sphere physics), `Movement/` (the player's movement, other bodies steered to a goal, bodies pushing each other, and bodies' shadows), `Cameras/` (the outdoor follow camera), `Calendar/` (the game clock), `Actors/` (actors' state machines, which the runtime and the editor both step), `Scenes/` (billboards, scrolling textures, which chunks the original draws, and how scene objects fade with distance and view), `Flags/` (the event flags, the network of derived flags, the engine state written into flags, and whether a map object is there by them), `Scripts/` (the Borg13 VM: it runs a script against the flags and an `IScriptWorld`, and sends its cameras, actors and sounds to an `IScriptHost`), `Rng/` (the original's generator, behind `IRandom`), `Characters/` (character sheets: stats, skills, hit points, experience and spells, training, and the party's skill checks), `Items/` (equipment, the inventory, potions, shops, containers, locks and traps, and crafting), `Ui/` (the font engine's layout and wrapping, the conversation box, and the pause menu's Party tab with its lists, C-down's details, and what A does with its popups, and its barter state, the loot window, `BarterMenu`), `MapObjects/` (what map objects remember, the original's `VoxelChart`; a loaded chunk's objects, and using them: what A reaches, containers, locks, traps, doors, secret doors and triggers, with their popups) `Trek/` (what belongs to trek alone: the wanderers, monster parties' bodies in the loaded chunks, wandering, noticing and chasing the player) and `Sky/` (the colours a terrain's sky gives the scenery by the time of day, the gradient, the sun's light on bodies, the sun, moon, stars and clouds, and the lens flare), stepped per 60 Hz tick with per-frame behaviour matched to 30 fps (`Cadence`). `GameState` at its root is the whole game, which a save snapshots and scripts run against.
- `src/Aidyn.Cli`: the `aidyn` command-line tool, e.g. `import <rom>` (the ROM as `aidyn.base`, in the data folder or `AIDYN_DATA`), `dotnet run --project src/Aidyn.Cli -- borg dump <rom>`, `db dump <rom>` (every table as JSON), `textures <rom>` (Borg1 and Borg8 as PNG), `scenes <rom>` (Borg5 as glTF, with their Borg6 animations), `actors <rom>` (Borg7 state machines as JSON), `dialogue <rom>` (Borg13 as text), `maps <rom>` (each map set's chunks and objects as JSON, and its collision as glTF), `atlas <rom>` (the quest atlas as JSON), `flag <rom> <n>` (what sets and reads a flag), `strings <rom> [<table>]` (one string table, or every one to files), or `save new|from <checkpoint>|show <save>` (a new game or a checkpoint as a save, and what the game makes of one). Output goes to the gitignored `out/`, and saves to the data folder's `saves`.
- `src/Aidyn.Game`: the Godot runtime (roadmap Phase 3). It loads plugins only, never the ROM. It streams a map set's chunks with their scenery (`ChunkScenery`, which the editor shares) and collision, and steps the core's player and camera each physics tick, with his shadow, and the wanderers' bodies (`WandererView`). Scenes draw with the N64 material (`N64Material`, shared with the Inspector), tinted and fogged by the sky's colours, with bodies lit as the RSP lights them. Behind them it draws the sky's gradient, sun, moon, stars and clouds, and over them the lens flare. UI draws on a low-resolution screen, 240 lines as the original's or more for smaller UI, scaled up as the console's video output does (`UiScreen`), `ConversationBoxView` draws a conversation's box in the game's font, and `PartyTabView` the pause menu's Party tab with the menus' widget kit (`UiPainter`), in the original's layout or a wide one where the screen has room, and its barter state, the loot window, which containers open. It plays the core's `GameState`, a new game's or a save's (`--save`), and Start opens the pause menu (`PauseMenu`). Run it with `tools/godot.ps1 --path src/Aidyn.Game` after `aidyn import`.
- `src/Aidyn.Inspector`: the editor (roadmap 0.6), which grows into the mod editor. The editor reads and edits only the imported plugins, as OpenMW-CS does for its own content, and draws them with the runtime's code (ADR 0010). It lists every record and asset in the installed plugins, with a preview, the merged fields and who set them, and what names each one, with record IDs as links and back and forward. Map sets, maps and map objects preview as the map drawn whole with the runtime's chunk code. Entities preview as their character sheet, with training priced by the core and equipment to put on and take off; items, shops, loot tables and recipes preview with what they do for the editor's party, and a loot table's roll or a container on the map opens the loot window against the editor's game; and scripts play against the editor's game, the core's `GameState`: a new game's, a checkpoint's or a save's, which it can save for the runtime. Run it with `tools/godot.ps1 --path src/Aidyn.Inspector`.
- `tests/Aidyn.Core.Tests`, `tests/Aidyn.Plugins.Tests` and `tests/Aidyn.Rom.Tests`: `dotnet test` from the repo root. Real-data tests find ROMs in `roms/` (or `AIDYN_ROMS`) by hash and skip when they're missing.

## Status

Keep this section to where things stand and what's next. When a roadmap task finishes or starts, update it, and suggest a next step when one finishes. What was found and built goes in the docs, not here.

- **Done:** Phase 0 (0.1–0.6), 1A, 1B, 1C, 1E, 1F, 1H, M1 Walk Gwernia, 2.1 (flags), 2.2 (characters), 2.3 (items), 2.4 (the Borg13 VM) and 2.8 (saves; the System tab's Save and Load come with M2 session 8). Shop prices wait for the user (see to-check).
- **Now: M2 Talk and trade**, in eight sessions, each ending playable or previewable with its docs. 2.7 (camping) slots in, and boss flags come with 2.5 (combat). Sounds wait for 1D, so cues that play one are logged.
  1. ~~The Party tab, read-only~~ ([ui](docs/systems/ui.md#the-pause-menu-m2))
  2. ~~A game in the runtime, and saves~~ ([saves](docs/systems/saves.md), [runtime](docs/systems/runtime.md))
  3. ~~The Party tab's actions~~ ([ui](docs/systems/ui.md#the-party-tabs-actions))
  4. ~~Using map objects, the loot window and the interiors' camera~~ ([map-objects](docs/systems/map-objects.md), [ui](docs/systems/ui.md#the-loot-window), [cameras](docs/systems/cameras.md))
  5. ~~Conversations: bodies, wandering, talking, joining and leaving~~ ([wanderers](docs/systems/wanderers.md), [characters](docs/systems/characters.md#joining-and-leaving-m2-session-5d))
  6. ~~Cut-scenes~~ ([scripts](docs/systems/scripts.md#cut-scenes-m2-session-6), [cameras](docs/systems/cameras.md#cut-scenes-cameras-session-6)). Backdrops and the help menu, the endings' cinematics, and the editor showing cut-scenes on its map are left; a later script type running ops side by side can share a `CutScene`'s stage.
  7. **Barter and training, next,** from a conversation's end: the shop window (buying, selling, quantities, appraisal, on `BarterMenu`) and the training lists.
  8. **The System and World tabs, and the HUD:** saving and loading, options; the calendar, camping (2.7, its ambush waiting for 2.5), the world map with its fog, and the journal; the compass and the party's portraits.
- **For the user to check,** in the emulator or the ROM: [to-check](docs/to-check.md). Add to it whatever only the user can settle.
- **UI fit and finish, for a later session** (the user's notes, [playtests](docs/playtests.md)):
  - The Party tab's shield icon on the silhouette sits 2 pixels too high.
  - The menu's animations (the list sliding in, the portraits moving, the details fading) draw the eye to where the controller has moved. Keep the top face button for details and the shoulders for the member.
  - The Healer's popup needs a gap between its members' portraits, which overlap by 2 pixels ([capture](roms/menu-party-skills-healer-layonhands.png)).
- **Maybe:** the editor showing wanderers on its map view.
- **Decided, for the fidelity profile at the end:** the UI draws a floating conversation box on a screen of any shape and size; what a faithful mode would take is in [ui](docs/systems/ui.md#a-faithful-mode-later). Where the port fixes one of the original's bugs, the user chose it, and the system's doc says so.
- **Left for later:** the renderer's leftovers ([rendering](docs/systems/rendering.md#whats-left)), the weather among them, and 1H's content tables marked To do, which the importer will need.
- **Open rig follow-ups** (from ADR 0006): measure the frame `delta` distribution in trek and combat, and get gdb working after loading a save state.

## Ground rules

- Never commit ROMs or anything derived from them. `roms/` is gitignored.
- The decomp ([blackgamma7/Aidyn](https://github.com/blackgamma7/Aidyn)) isn't vendored. A local clone lives in the gitignored `ref/Aidyn`, next to `ref/AidynEditor`. Read it there and `git -C ref/Aidyn pull` to update it; don't re-clone into scratch space. If `ref/` is missing, clone both repos into it. The decomp has no licence, so port from understanding and don't copy its code.
- The decomp is a map, not the truth: its pseudocode is often wrong in the details. Settle what the original does from 1.0's machine code (`tools/n64dis.py`), and cite the addresses in the doc.
- The user reaches story checkpoints by playing the game. Don't drive gameplay in the emulator. Ask for in-game actions instead.
- Commit as you go, without asking: commit each finished slice of work once its tests pass, along with its doc and status updates.
- **Keep the main context for judgement and building.** Reading decomp files and machine code fills a context fast, and Sonnet 5.5 subagents settle such questions well from the code. So send research into what the original does to a Sonnet subagent, batching what a slice needs into one run, and read the code yourself only for the lines you're porting. Sonnet subagents also do well with well-scoped work: readers and importers over known layouts, indexes over plugin records, and real-data tests, which they check against the data before asserting. Give them the contract and the facts. Then review what comes back: spot-check a constant or two in the disassembly, and write findings into the system's doc straight away. They don't build the Godot projects, so build those yourself. Take an Opus subagent when Sonnet can't settle something, or two answers disagree.
