# hbht

A PC reimplementation of Aidyn Chronicles: The First Mage (N64). It runs on content imported from a user-supplied ROM and has a plugin-based mod system.

## Where things are decided

- [docs/roadmap.md](docs/roadmap.md): the phased plan. Task numbers (0.3, 1E, 2.10, …) refer to it.
- [docs/adr/](docs/adr/README.md): accepted architecture decisions. They are binding. To change one, write a new ADR that supersedes it rather than editing the old one.
- [docs/reference-rig.md](docs/reference-rig.md): the emulator, the pinned Godot, the ROMs, the checkpoint save states, and `tools/`.

## Code

A .NET 10 solution (`Aidyn.slnx`, SDK pinned in `global.json`), laid out per ADR 0002.

- `src/Aidyn.Rom`: ROM identification and per-version offsets (`RomImage`, `RomVersions`), the LZB and LZ01x decompressors, the Borg archive (`BorgArchive`), the text cipher and string tables (`Text/`), the DB block's typed records (`Db/GameDb`), and N64 texel decoding, Borg1 textures, Borg8 UI images, a PNG writer, a minimal F3DEX2 interpreter, Borg2 geometry, Borg5 scenes with Borg3 cameras and Borg4 lights, Borg6 animations and a player that steps them as the game does, Borg7 actors, Borg5 particle emitters, and a glTF writer (`Graphics/`); Borg13 dialogue scripts (`Dialogue/`); map chunk lists, map sets' chunk placement, and Borg9 collision, grids and map objects, with a collision glTF export (`Maps/`); the quest atlas, with the flag tables engine code holds (`Quests/`); and other content tables in the code segment, such as the cinematics and entities' models (`Engine/`).
- `src/Aidyn.Plugins`: the plugin format (ADRs 0004 and 0005): record IDs, plugin loading with field-level merges and the conflict report, asset lookup by record ID, and the typed records (`Records/`). The importer (`Aidyn.Rom/Import`) writes the base plugin with these types, and the runtime reads it.
- `src/Aidyn.Core`: the headless rules core (ADR 0003). Organised by subsystem, not game mode (ADR 0009). So far `Physics/` (the collision world and the original's sphere physics), `Movement/` (the player's movement) and `Cameras/` (the outdoor follow camera), stepped per 60 Hz tick with per-frame behaviour matched to 30 fps (`Cadence`).
- `src/Aidyn.Cli`: the `aidyn` command-line tool, e.g. `import <rom>` (the ROM as `aidyn.base`, in the data folder or `AIDYN_DATA`), `dotnet run --project src/Aidyn.Cli -- borg dump <rom>`, `db dump <rom>` (every table as JSON), `textures <rom>` (Borg1 and Borg8 as PNG), `scenes <rom>` (Borg5 as glTF, with their Borg6 animations), `actors <rom>` (Borg7 state machines as JSON), `dialogue <rom>` (Borg13 as text), `maps <rom>` (each map set's chunks and objects as JSON, and its collision as glTF), `atlas <rom>` (the quest atlas as JSON), `flag <rom> <n>` (what sets and reads a flag) or `strings <rom> <table>`. View exported scenes with `tools/scene-viewer.html`, map sets with `tools/map-viewer.html` and the atlas with `tools/quest-atlas.html` (see the reference rig). Output goes to the gitignored `out/`.
- `src/Aidyn.Game`: the Godot runtime (roadmap Phase 3). It loads plugins only, never the ROM. It streams a map set's chunks with their scenery and collision, and steps the core's player and camera each physics tick. Run it with `tools/godot.ps1 --path src/Aidyn.Game` after `aidyn import`.
- `src/Aidyn.Inspector`: the asset inspector (roadmap 0.6), a Godot app over `Aidyn.Rom.Inspection`: every Borg by type and index, with a preview, its decoded fields and a hex dump. Run it with `tools/godot.ps1 --path src/Aidyn.Inspector`.
- `tests/Aidyn.Core.Tests`, `tests/Aidyn.Plugins.Tests` and `tests/Aidyn.Rom.Tests`: `dotnet test` from the repo root. Real-data tests find ROMs in `roms/` (or `AIDYN_ROMS`) by hash and skip when they're missing.

## Status

Update this section when a roadmap task finishes or starts.

- **Done:** 0.1 reference rig, 0.2 ROM identification, 0.3 Borg archive reader, 0.4 DB readers, 0.5 ADRs, 1A static graphics (textures, UI images, geometry, scenes, glTF export; the Gwernia house and Brenna match emulator captures), 1E quest atlas (Borg13 dialogue, map objects, the flag network, journal links, the cross-reference and its viewer; checked against the four checkpoints), 1H hardcoded-content audit ([docs/hardcoded-content.md](docs/hardcoded-content.md): every item has its ADR 0008 layer; the tables the atlas needs are read from the ROM, the rest are located), 1B animation (Borg6 animations played as the game steps them, glTF animation export, Borg7 actors and entities' models, Borg5 particle emitters; the user confirmed all 13 opening shots and Brenna's animation set in the scene viewer), 1C maps (Borg9 collision and its grid, every map object type, Expansion Pak variants, chunk placement per map set, and the map viewer; the user confirmed the layout, terrain, objects and enemy spawns).
- **Left for later:** 1A's and 1B's leftovers for the renderer (lighting, billboards, texture filtering, scrolling textures, the particle simulation, the cinematics' clip planes) are in the reference rig. 1H's are at the end of its audit: flags 955–958 and 960–963 are unexplained, and the content tables marked To do are located but not read; the importer will need them.
- **In progress:** M1 Walk Gwernia. The importer's first slice writes `aidyn.base`: map sets, chunks, scene objects and actors as records, and every scene and chunk collision as glTF (see the reference rig). `Aidyn.Game` streams the chunks around Alaron, with scenery and actors. The user walked it to Gwernia; their notes are in the reference rig's runtime section. The player's movement, collision and outdoor camera are now ports of the original in `Aidyn.Core`. Next: the player's shadow, then the N64 material (vertex colour, combiners, fog, scrolling textures, billboards) and the sky. 0.6 asset inspector. Godot 4.7.2 (.NET) is pinned, and the first slice browses every Borg with previews, fields and hex (see the reference rig). It grows with each track from here.
- **Not started:** everything else after Phase 0.
- **Open rig follow-ups** (from ADR 0006): measure the frame `delta` distribution in trek and combat, and get gdb working after loading a save state.

## Ground rules

- Never commit ROMs or anything derived from them. `roms/` is gitignored.
- The decomp ([blackgamma7/Aidyn](https://github.com/blackgamma7/Aidyn)) isn't vendored. A local clone lives in the gitignored `ref/Aidyn`, next to `ref/AidynEditor`. Read it there and `git -C ref/Aidyn pull` to update it; don't re-clone into scratch space. If `ref/` is missing, clone both repos into it. The decomp has no licence, so port from understanding and don't copy its code.
- The user reaches story checkpoints by playing the game. Don't drive gameplay in the emulator. Ask for in-game actions instead.
- Commit as you go, without asking: commit each finished slice of work once its tests pass, along with its doc and status updates. This is specific to this project. Pushing still waits for the user.
- When a roadmap task is complete, evaluate the remaining ones and save a reasonable next step suggestion in CLAUDE.md under Status.
