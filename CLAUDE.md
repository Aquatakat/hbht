# hbht

A PC reimplementation of Aidyn Chronicles: The First Mage (N64). It runs on content imported from a user-supplied ROM and has a plugin-based mod system. M1, M2, M3 (Fight) and M4 (the prologue, played side by side with the emulator) are playable, and 2.10's events and triggers carry the cinematics. Next is Phase 4, content parity. The roadmap's [Where things stand](docs/roadmap.md#where-things-stand) has the rest.

## Where to look

Read what your task needs, not all of it.

| For | Read |
| --- | --- |
| The plan, what a task number (0.3, 1E, 2.10, M1, …) means, and the one list of open work | [docs/roadmap.md](docs/roadmap.md) |
| Decisions. These are binding. To change one, write a new ADR that supersedes it rather than editing the old one. | [docs/adr/](docs/adr/README.md) |
| How a ROM format is laid out, and what decoding it found (`animation`, `actors` and `particles` also cover how the core plays them) | [docs/formats/](docs/formats/): `rom`, `code-tables` (the content tables in the code segment, and which record carries each), `db`, `textures`, `geometry`, `scenes`, `animation`, `actors`, `particles`, `dialogue`, `maps`, `quests`, `text`, `audio` |
| Content the port's code still holds that belongs in the plugins, and which ADR 0008 layer each piece goes in | [docs/hardcoded-content.md](docs/hardcoded-content.md) |
| How our code ports or runs a system | [docs/systems/](docs/systems/): `importer`, `plugins`, `runtime`, `movement`, `cameras`, `rendering`, `flags`, `scripts`, `characters`, `items`, `map-objects`, `wanderers`, `camping`, `combat` (with `combat-actions`, `combat-ai` and `combat-parity`), `magic`, `saves`, `editor`, `ui`, `pause-menu`, `audio`, `weather`, `cinematics`, `events` |
| How the packages are built and laid out, and how the release workflow numbers and describes releases | [docs/systems/releases.md](docs/systems/releases.md) |
| What only the user can settle, in the emulator or by playing | [docs/user-checks.md](docs/user-checks.md) |
| The emulator, gdb, the pinned Godot, Ghidra, the ROMs, checkpoint save states, setting a check up with a save written into the Controller Pak, `tools/`, the code index and the symbol map, and finding the original's code in its machine code | [docs/reference-rig.md](docs/reference-rig.md) |

When you learn something, write it in the doc for that format or system. Add a file only for a new format or subsystem, and add it to this table. The reference rig is only for the rig. A doc is for whoever changes that system next: what the original does, with the addresses that show it, which classes port it, where the port differs and that the user chose it, and what's left. How a session got there, the port's fixed bugs, and the measurements behind a settled choice aren't, unless they leave a trap.

## Glossary

Use these words in code and docs. Where the original's name clashes with ours, use ours and cite the original's in a `<c>` tag (ADR 0009). Older citations give the decomp's names, and stay (ADR 0015).

| Term | Means | Don't confuse with |
| --- | --- | --- |
| **Trek** | Free exploration, the original's out-of-combat mode (`gametrek.cpp`). | The systems trek shares with combat, which aren't trek's (ADR 0009). |
| **Combat** | Turn-based fighting (`gameCombat.cpp`, `GameMode_Combat`). The player-facing docs call it battle mode. Arenas are the Battle arenas map set. | |
| **Tick** | 1/60 s, the core's unit of time. The core steps one tick at a time (ADR 0017). | A **frame**: one rendered frame of the original, covering `delta` ticks (1–6). **Cadence** is how the port matches per-frame behaviour: a 30 fps frame, 2 ticks. |
| **Borg** | An asset in the ROM's archive, by type (Borg1 texture … Borg13 script) and archive index. | |
| **Scene** | A Borg5: a tree of transforms with models, a camera and lights. Record `aidyn:scene/0x…`. | A Godot scene (`.tscn`). Say "Godot scene" for those. |
| **Actor** | A Borg7: a scene with a state machine of animations. Record `aidyn:actor/0x…`. | The decomp's `Actor::` functions and `PlayerData`, which are a **body** moving through the world: the player, an NPC or a combatant. That code is in `Movement` and `Physics`. |
| **Request** | The animation number the game asks an actor for (0 idle, 2 walk, 3 run, …), a record `aidyn:request/n` (ADR 0033). | An actor's states. |
| **Entity** | Anyone who stands, talks or fights: a kind of character or creature, record `aidyn:entity/0x…`. The EntityDB's have a character **sheet** with their stats; the original's dialogue entities (`0x14xx`: signs, animals, passers-by) are entities with none, which can't fight or join (ADR 0032). | An ECS entity. |
| **Item** | An item record, `aidyn:item/0x…`: weapons (`0x07…`), body armor (`0x05…`) and shields (`0x06…`) among them, by `kind` (ADR 0031). | |
| **Map set** | One of the 17 grids of chunks: the Overworld, the Gwernia interiors, the Battle arenas and so on (`EnumMapDatA`). Record `aidyn:mapset/<n>`. | |
| **Chunk** | One cell of a map set, named by row letter and column (I08, M07; a cell past A–Z by row and column, `0_13`): a Borg9's collision and map objects and up to two scenes. Record `aidyn:chunk/<set>/<name>`, such as `aidyn:chunk/8/C05`, which holds all of it (ADR 0035). The 3×3 around the player are loaded (the decomp's `ZoneEngine`; ours is the chunk streamer, which our `ZoneEngine` holds). | A **Borg9**, which the original shares between cells (the sea's and some arenas'); the importer copies it into each, and its collision is an asset, `aidyn:collision/0x…`. |
| **Fill** | What other chunks' scenes drew over a chunk's cell, which the importer moved into it, drawn with it. Record `aidyn:fill/<n>`. A cell that had only that gets a chunk of its own. | A chunk's **shows**: the chunks whose own art replaced a copy it drew, drawn while it's loaded. |
| **Map object** | An object a chunk places: a scene, container, teleporter, tripwire (the original's trigger object), npc, encounter and so on. Record `aidyn:mapobject/<set>/<name>/<n>`, its chunk's key and its index (ADR 0035). The original calls it a voxel object. | A **voxel** alone: the original also uses the word for a chunk's collision (`CollideCollisionSphereWithVoxelPolys`). Say map object in code, docs and IDs, and voxel only to cite the original. |
| **Npc**, **encounter** | The two kinds of the original's monster party (`monsterparty_dat`), by whether it has a script: an npc talks (and can fight through its script), an encounter fights. Both wander as bodies (`Wanderers`). | The **map** of the pause menu's World tab, and the minimap, which is a map set's `minimap`. |
| **Tripwire** | A map object that goes off as the player walks into it or onto a trigger triangle naming it, or uses it: it sets a flag, animates its actor or changes its chunk's collision. The original's trigger object (`VObject_Triggers`). | A **trigger record** (`aidyn:trigger/n`, 2.10), which runs actions on a named event. A **trigger triangle**, the collision role that names a map object. |
| **World** | The worldspace: the loaded map set. `CollisionWorld` is its loaded collision. | The decomp's `World` and `TerrainPointer`, which are the calendar, time of day, weather and terrain. |
| **Flag** | One of the 5,627 event flags in `GameStateFunnel`. A flag is **plain** (set by scripts, map objects or code) or **derived** (computed from others). Record `aidyn:flag/<n>`. | The decomp's "game state" and "event flag" mean the same thing. |
| **Script** | A Borg13: a conversation, cut-scene, backdrop or sign. Record `aidyn:script/0x…`: nodes, each with its speaker and typed ops (`camera.cutTo`, `actor.walkTo`, `flag.set`), the vocabulary triggers share (ADR 0034). Its map object is a script object (the original's dialogue object). | A **Lua script**, ADR 0008's layer 4. Always say "Lua" for those; a plugin's Lua files go in `lua/`. |
| **Mark** | A reference point's number, which a script names, counted from the number the script was started with (a script object's `marks`). | A map object's index. |
| **Cut-scene** | A script that moves the camera and actors in the game world. | A **cinematic**: one of the four pre-animated scenes (`CinematicStruct`). |
| **Base plugin** | `aidyn.base`, the plugin the importer writes from the ROM. The runtime and the editor read only plugins (ADR 0001). | |
| **Checkpoint** | A save state the user captured by playing (see the reference rig). | |

## Code

A .NET 10 solution (`HBHT.slnx`, SDK pinned in `global.json`), laid out per ADR 0002, named `HBHT.*` for Hoo Boy, Here's Trouble (ADR 0028; `aidyn` stays only for the game's names and the imported data, such as the `aidyn:` record IDs and `aidyn.base`). Each system's doc names the classes that port it.

- `src/HBHT.Rom`: everything that reads the ROM (ADR 0001). Identification and per-version offsets (`RomImage`, `RomVersions`), the decompressors (`Compression/`), the Borg archive (`Borg/`), the DB block's records (`Db/`), the text cipher and string tables (`Text/`), the graphics Borgs from textures to actors and particles, with a glTF writer (`Graphics/`), Borg13 scripts (`Dialogue/`), Borg11 samples and Borg12 sequences (`Audio/`), map sets and Borg9 (`Maps/`), the flags engine code names and the tables it keeps them in (`Quests/`), the content tables in the code segment (`Engine/`), the importer (`Import/`), and a reader for the checkpoints' RAM, which needs no ROM and turns one into a save, with the original's saves and the Controller Pak they're kept in (`Rig/`).
- `src/HBHT.Plugins`: the plugin format (ADRs 0004, 0005): record IDs, loading with field-level merges and the conflict report, the typed records (`Records/`), the catalog the editor browses (`PluginCatalog`, with `MapObjectKinds` for what map objects are called), the chunks by their cell (`ChunkIndex`, ADR 0035; the base plugin's chunks and scripts are read only as they're asked for, ADR 0036), the quest atlas's index over the records (`QuestIndex`), the readers of the importer's glTFs (`Assets/`, all through `GlbReader`, with `SceneNames` and `ExtrasKeys` the names and keys the importer writes and the readers look for) and the save format (`Saves/`). The importer writes the base plugin with these types, and the runtime and editor read it.
- `src/HBHT.Core`: the headless rules core (ADR 0003), stepped one 60 Hz tick at a time with per-frame behaviour matched to 30 fps (`Cadence`). `GameState` at its root is the whole game, which a save snapshots and scripts run against. `GameSession` beside it plays one, by what each tick is handed of the settings (`SessionSettings`, ADR 0023): it steps the mode that runs (trek, a cut-scene, a menu, a backdrop, the credits, a cinematic, the title screen) and turns every ending into a `Transition` for the runtime to show, a battle's as a `BattleSetup`. Its folders are by subsystem, not game mode (ADR 0009):
  - `Physics`, `Movement` (bodies, the player's among them, setting them down, pushing, shadows), `Cameras`, and `Facing` at the root, turning across the ground.
  - `Chunks`: the loaded chunks with their collision, map objects, sounds and lights, in the zone engine (`ZoneEngine`) that trek and a cut-scene's stage play in. `Trek`: trek's loop (`TrekLoop`) and what's trek's alone, wanderers and conversations.
  - `Actors` (Borg7 state machines, `ActorMotion`), `Scenes` (billboards, scrolling textures, draw distance), `Sky`, `Lights`, `Particles`, `Calendar` (the clock and the weather), `Cinematics` (`CinematicPlayer`).
  - `Flags`, `Scripts` (the Borg13 VM over the typed ops, and cut-scenes), `Events` (the named events and the plugins' triggers on them, `Triggers`), `MapObjects`.
  - `Characters`, `Items`, `Magic` (casting, and where spells' and potions' effects land, ADR 0021), `Combat` (a battle, `Battle`, from its setup to its rewards, with the AI and the battle screen's state), `Rng`.
  - `Ui` (text layout and the menus' state, which the Godot views draw), `Controls` (bindings by situation, ADR 0026), `Audio` (the original's sequencer and mixer, `AudioEngine`, which renders the PCM the runtime plays).
- `src/HBHT.Cli`: the `hbht` tool, `dotnet run --project src/HBHT.Cli -- <command>`. `import <rom>` writes `aidyn.base` to the data folder's `plugins` (`HBHT_DATA`, else `%LOCALAPPDATA%\HBHT`). Given the ROM, `borg`, `db`, `textures`, `scenes`, `dialogue`, `audio` and `strings` dump it for research into the gitignored `out/`. Over the installed plugins: `flag <n>` (what sets, clears and reads a flag), `conflicts` (fields more than one plugin patches), `movement reach|compare` (where Modern movement and the original's part ways, [movement](docs/systems/movement.md)) and `schema <dir>` (a JSON Schema per record type, which editors validate mods by). `save` makes, shows and converts saves, the original's in the Controller Pak among them ([saves](docs/systems/saves.md)).
- `src/HBHT.Views`: what the runtime and the editor both draw with, a Godot class library (ADR 0022), in namespace `HBHT.Views` (not `HBHT.Godot`, which would hide Godot's own `Godot.Node` from every `HBHT.*` namespace): the N64 material (`N64Material`), the glTF loaders (`GlbFile`, `GlbScene`, `Models`), actors (`ActorAnimator`), the chunk scenery with its billboards, scrolling textures and mirrored meshes (`ChunkScenery`), particles (`ParticlesView`), the UI screen and its painter (`UiScreen`, `UiPainter`, `UiImages`), the conversation box and the Party tab (`ConversationBoxView`, `PartyTabView`), and the command line's parser and screenshot (`CommandLine`). It references `HBHT.Core` and `HBHT.Plugins`, and uses nothing of either Godot project. What a scene attaches by path (`Game.cs`, `Editor.cs`) stays in its project; the library's nodes are made by code.
- `src/HBHT.Game`: the Godot runtime (roadmap Phase 3, [runtime](docs/systems/runtime.md)). It loads plugins only, never the ROM, and draws what the core steps and carries out what it asks, keeping its loops thin (ADR 0012): trek's chunks, bodies, the sky, the HUD and the menus, over the views it shares with the editor (`HBHT.Views`). Run it with `tools/godot.ps1 --path src/HBHT.Game` after `hbht import`.
- `src/HBHT.Editor`: the editor (roadmap 0.6, [editor](docs/systems/editor.md)), which grows into the mod editor. It reads only the installed plugins, as OpenMW-CS does, and draws them with the runtime's code (ADR 0010). It holds the importer, and becomes the game's first run (ADR 0012). Code flows one way, from the core to the views to the runtime to the editor (ADR 0022): what both draw with is in `HBHT.Views`, and the runtime uses nothing of the editor's. Something built in the editor first, where it's easier to drive and screenshot, moves into the runtime once the runtime uses it, or into the views if the editor keeps it too. It holds a `GameState` of its own, a new game's, a checkpoint's or a save's, which scripts and pages play against and which it can save for the runtime. Run it with `tools/godot.ps1 --path src/HBHT.Editor`.
- `tests/HBHT.Core.Tests`, `tests/HBHT.Plugins.Tests` and `tests/HBHT.Rom.Tests`: `dotnet test` from the repo root. Real-data tests find ROMs in `roms/` (or `HBHT_ROMS`) by hash and skip when they're missing. `tests/HBHT.TestSupport` is what the three share: `TempPlugins` (a GUID-named temp folder of hand-written plugins), the small collision worlds in `Worlds`, and the record-ID shorthands (`Ids.Flag`, `Entity`, `Script`), which each test project imports globally. Rom.Tests' `TestRoms` also holds the checkpoint helpers, and `Na10Import` is the one NA 1.0 import its tests share.

## Questions over data

Many questions here are counts and cross-checks over thousands of records: how many containers have each reach, which talkers' cut-scenes name a point their chunk lacks, what every checkpoint holds at an address. Answer them with a script that does the looping and prints only the answer, rather than reading JSON or dumps into the context. One run replaces dozens of tool calls, and the script is the evidence behind the number that goes in a doc.

- **Over the plugins, in C#,** with the project's own types: a .NET 10 file-based app in the scratchpad, run with `dotnet run query.cs`. It takes about four seconds, and loading the base plugin about two.

  ```csharp
  #:project C:/path/to/hbht/src/HBHT.Plugins/HBHT.Plugins.csproj
  #:property PublishAot=false
  using HBHT.Plugins;
  using HBHT.Plugins.Records;

  var set = PluginSet.LoadInstalled(DataPaths.Plugins);
  var containers = set.All<ChunkRecord>("chunk").SelectMany(c => c.Objects).OfType<ContainerObject>();
  foreach (var g in containers.GroupBy(c => c.Reach).OrderBy(g => g.Key))
      Console.WriteLine($"reach {g.Key}: {g.Count()}");
  ```

  `#:project` needs the absolute path from the scratchpad. Without `PublishAot=false`, System.Text.Json refuses to load the records. Reference `HBHT.Core` as well to run the rules over the data (`GameState`, `ScriptRunner`, `MapObjectUse`), or `HBHT.Rom` for the ROM itself and the checkpoints (`AresCheckpoint`).
- **Over the original's code or the checkpoints,** loop `tools/n64dis.py` or `tools/bst.py` in one shell command, over the functions of a jump table or the eight states, or read the code index (`out/ghidra/<rom>.index.json`) and the pseudocode (`out/ghidra/<rom>.c`) in a script. `tools/ghidra.ps1 import` makes them.
- **A finding that should keep holding** becomes a real-data test instead.
- **Subagents** can work the same way: say so in the prompt when their question is about the data.

## Ground rules

- Never commit ROMs or anything derived from them. `roms/` is gitignored.
- **The project doesn't read the decomp** ([blackgamma7/Aidyn](https://github.com/blackgamma7/Aidyn)), in light of its author's views ([ADR 0015](docs/adr/0015-the-port-no-longer-reads-the-decomp.md)). Don't clone, fetch or search it, and tell subagents the same. Its names already cited in the docs and code stay, as credit. [AidynEditor](https://github.com/TiberGuy/AidynEditor) (MIT) lives in the gitignored `ref/AidynEditor`; if it's missing, clone it there.
- Settle what the original does from 1.0's machine code and the checkpoints, and cite the addresses in the doc. The [reference rig](docs/reference-rig.md#finding-the-originals-code) says how to find a system's code. The debug build's strings give the original's own names for its source files and some functions. Name the rest ourselves, and add each name settled to the symbol map (`tools/symbols/`) by [its rules](docs/reference-rig.md#the-symbol-map-roadmap-08): the original's own name from a string, else the port's, else one we coin. A name the docs cite only as the decomp's never goes in it.
- Where the port fixes one of the original's bugs, the user chose it, and the system's doc says so.
- The user reaches story checkpoints by playing the game. Don't drive gameplay in the emulator. Ask for in-game actions instead.
- Commit as you go, without asking: commit each finished slice of work once its tests pass, along with its doc and status updates.
- **Keep the main context for judgement and building.** Reading machine code fills a context fast, and Sonnet 5.5 subagents settle such questions well from the code. So send research into what the original does to a Sonnet subagent, batching what a slice needs into one run, and read the code yourself only for the lines you're porting. Point them at the docs, the symbol map and the rig's way of finding code, and have them return the names they settle for the map. Sonnet subagents also do well with well-scoped work: readers and importers over known layouts, indexes over plugin records, and real-data tests, which they check against the data before asserting. Give them the contract and the facts. Then review what comes back: spot-check a constant or two in the disassembly, and write findings into the system's doc straight away. They don't build the Godot projects, so build those yourself. Take an Opus subagent when Sonnet can't settle something, or two answers disagree.
