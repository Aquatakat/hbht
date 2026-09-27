# hbht

A PC reimplementation of Aidyn Chronicles: The First Mage (N64). It runs on content imported from a user-supplied ROM and has a plugin-based mod system.

## Where things are decided

- [docs/roadmap.md](docs/roadmap.md): the phased plan. Task numbers (0.3, 1E, 2.10, …) refer to it.
- [docs/adr/](docs/adr/README.md): accepted architecture decisions. They are binding. To change one, write a new ADR that supersedes it rather than editing the old one.
- [docs/reference-rig.md](docs/reference-rig.md): the emulator, the ROMs, the checkpoint save states, and `tools/`.

## Code

A .NET 10 solution (`Aidyn.slnx`, SDK pinned in `global.json`), laid out per ADR 0002.

- `src/Aidyn.Rom`: ROM identification and per-version offsets (`RomImage`, `RomVersions`), the LZB and LZ01x decompressors, the Borg archive (`BorgArchive`), the text cipher and string tables (`Text/`), the DB block's typed records (`Db/GameDb`), and N64 texel decoding, Borg1 textures, Borg8 UI images, a PNG writer, a minimal F3DEX2 interpreter, Borg2 geometry, Borg5 scenes with Borg3 cameras and Borg4 lights, and a glTF writer (`Graphics/`); Borg13 dialogue scripts (`Dialogue/`); map chunk lists and Borg9 map objects (`Maps/`); and the quest atlas (`Quests/`).
- `src/Aidyn.Cli`: the `aidyn` command-line tool, e.g. `dotnet run --project src/Aidyn.Cli -- borg dump <rom>`, `db dump <rom>` (every table as JSON), `textures <rom>` (Borg1 and Borg8 as PNG), `scenes <rom>` (Borg5 as glTF), `dialogue <rom>` (Borg13 as text), `atlas <rom>` (the quest atlas as JSON), `flag <rom> <n>` (what sets and reads a flag) or `strings <rom> <table>`. View exported scenes with `tools/scene-viewer.html` and the atlas with `tools/quest-atlas.html` (see the reference rig). Output goes to the gitignored `out/`.
- `tests/Aidyn.Rom.Tests`: `dotnet test` from the repo root. Real-data tests find ROMs in `roms/` (or `AIDYN_ROMS`) by hash and skip when they're missing.

## Status

Update this section when a roadmap task finishes or starts.

- **Done:** 0.1 reference rig, 0.2 ROM identification, 0.3 Borg archive reader, 0.4 DB readers, 0.5 ADRs, 1A static graphics (textures, UI images, geometry, scenes, glTF export; the Gwernia house and Brenna match emulator captures), 1E quest atlas (Borg13 dialogue, map objects, the flag network, journal links, the cross-reference and its viewer; checked against the four checkpoints).
- **Next:** 1H hardcoded-content audit. Start with the 49 flags the checkpoints set that no data sets (listed under the quest atlas in the reference rig), and add what it finds to `EngineFlags` so the atlas shows it.
- **Left for later:** 1A's leftovers for the renderer (lighting, billboards, texture filtering) are in the reference rig.
- **Not started:** 0.6 asset inspector (needs Godot installed and pinned), and everything else after Phase 0.
- **Open rig follow-ups** (from ADR 0006): measure the frame `delta` distribution in trek and combat, and get gdb working after loading a save state.

## Ground rules

- Never commit ROMs or anything derived from them. `roms/` is gitignored.
- The decomp ([blackgamma7/Aidyn](https://github.com/blackgamma7/Aidyn)) isn't vendored. A local clone lives in the gitignored `ref/Aidyn`, next to `ref/AidynEditor`. Read it there and `git -C ref/Aidyn pull` to update it; don't re-clone into scratch space. If `ref/` is missing, clone both repos into it. The decomp has no licence, so port from understanding and don't copy its code.
- The user reaches story checkpoints by playing the game. Don't drive gameplay in the emulator. Ask for in-game actions instead.
- Commit as you go, without asking: commit each finished slice of work once its tests pass, along with its doc and status updates. This is specific to this project. Pushing still waits for the user.
