# hbht

A PC reimplementation of Aidyn Chronicles: The First Mage (N64). It runs on content imported from a user-supplied ROM and has a plugin-based mod system.

## Where things are decided

- [docs/roadmap.md](docs/roadmap.md): the phased plan. Task numbers (0.3, 1E, 2.10, …) refer to it.
- [docs/adr/](docs/adr/README.md): accepted architecture decisions. They are binding. To change one, write a new ADR that supersedes it rather than editing the old one.
- [docs/reference-rig.md](docs/reference-rig.md): the emulator, the ROMs, the checkpoint save states, and `tools/`.

## Code

A .NET 10 solution (`Aidyn.slnx`, SDK pinned in `global.json`), laid out per ADR 0002.

- `src/Aidyn.Rom`: ROM identification and per-version offsets (`RomImage`, `RomVersions`), the LZB and LZ01x decompressors, the Borg archive (`BorgArchive`), the text cipher and string tables (`Text/`), the DB block's typed records (`Db/GameDb`), and N64 texel decoding, Borg1 textures, Borg8 UI images, a PNG writer, a minimal F3DEX2 interpreter and Borg2 geometry (`Graphics/`).
- `src/Aidyn.Cli`: the `aidyn` command-line tool, e.g. `dotnet run --project src/Aidyn.Cli -- borg dump <rom>`, `db dump <rom>` (every table as JSON), `textures <rom>` (Borg1 and Borg8 as PNG) or `strings <rom> <table>`. Output goes to the gitignored `out/`.
- `tests/Aidyn.Rom.Tests`: `dotnet test` from the repo root. Real-data tests find ROMs in `roms/` (or `AIDYN_ROMS`) by hash and skip when they're missing.

## Status

Update this section when a roadmap task finishes or starts.

- **Done:** 0.1 reference rig, 0.2 ROM identification, 0.3 Borg archive reader, 0.4 DB readers, 0.5 ADRs.
- **In progress:** 1A static graphics. Borg1 textures, Borg8 UI images and Borg2 geometry are done. Next are Borg5, Borg3 and Borg4, then the glTF export.
- **Next:** 1E quest atlas, in its own session after 1A.
- **Not started:** 0.6 asset inspector (needs Godot installed and pinned), and everything else after Phase 0.
- **Open rig follow-ups** (from ADR 0006): measure the frame `delta` distribution in trek and combat, and get gdb working after loading a save state.

## Ground rules

- Never commit ROMs or anything derived from them. `roms/` is gitignored.
- The decomp ([blackgamma7/Aidyn](https://github.com/blackgamma7/Aidyn)) isn't vendored. Clone it into scratch space to read it. It has no licence, so port from understanding and don't copy its code.
- The user reaches story checkpoints by playing the game. Don't drive gameplay in the emulator. Ask for in-game actions instead.
- Commit as you go, without asking: commit each finished slice of work once its tests pass, along with its doc and status updates. This is specific to this project. Pushing still waits for the user.
