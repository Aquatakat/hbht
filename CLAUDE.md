# hbht

A PC reimplementation of Aidyn Chronicles: The First Mage (N64). It runs on content imported from a user-supplied ROM and has a plugin-based mod system.

## Where things are decided

- [docs/roadmap.md](docs/roadmap.md): the phased plan. Task numbers (0.3, 1E, 2.10, …) refer to it.
- [docs/adr/](docs/adr/README.md): accepted architecture decisions. They are binding. To change one, write a new ADR that supersedes it rather than editing the old one.
- [docs/reference-rig.md](docs/reference-rig.md): the emulator, the ROMs, the checkpoint save states, and `tools/`.

## Status

Update this section when a roadmap task finishes or starts.

- **Done:** 0.1 reference rig, 0.5 ADRs.
- **Next:** 0.2 ROM identification and 0.3 Borg archive reader. This is the first C# code, so it starts with the solution layout in ADR 0002.
- **Not started:** 0.4 DB readers, 0.6 asset inspector, and everything after Phase 0.
- **Open rig follow-ups** (from ADR 0006): measure the frame `delta` distribution in trek and combat, and get gdb working after loading a save state.

## Ground rules

- Never commit ROMs or anything derived from them. `roms/` is gitignored.
- The decomp ([blackgamma7/Aidyn](https://github.com/blackgamma7/Aidyn)) isn't vendored. Clone it into scratch space to read it. It has no licence, so port from understanding and don't copy its code.
- The user reaches story checkpoints by playing the game. Don't drive gameplay in the emulator. Ask for in-game actions instead.
