# 0012. The editor holds the importer, and code flows from the runtime to the editor

Status: Accepted · Oct 3, 2026 · Supersedes the last consequence of [0010](0010-editor-reads-plugins.md), that the editor's reference to `HBHT.Rom` goes when the Borg browser is deleted

## Context

[0010](0010-editor-reads-plugins.md) made the editor read only plugins, and expected its reference to `HBHT.Rom` to go with the Borg browser. The browser is gone, but the reference stayed, for two things: the editor runs the importer (**Import ROM…**), and it loads checkpoints, which `HBHT.Rom.Rig` reads from an emulator's save state without a ROM.

That raised what a player sees on first running the game, and where the importer lives at the end: in a third utility, in the runtime, or in the editor.

Separately, the runtime and the editor share code by linking files into each other's projects. Sharing had come to run both ways: the editor linked 14 of the runtime's files, and the runtime linked the editor's `N64Material`. Features are often built in the editor first, since it's easier to drive and screenshot there, and then moved to the runtime. Nothing said which way the code should end up flowing.

## Decision

### First run goes through the editor

1. The player runs the game. With no base plugin, or one an older importer wrote, the runtime starts the editor and closes.
2. The editor finds no plugins and offers a "Pick your ROM" screen, filling the window.
3. It imports the ROM into the data folder's `aidyn.base`.
4. It offers **Play**, which closes the editor and starts the runtime, or **Editor**, which brings up the editor's controls in the same window.

The importer stays a library in `HBHT.Rom.Import`, which the editor and `hbht import` both run. There's no third program.

### Which projects read the ROM

- **The runtime never references `HBHT.Rom`** ([0001](0001-importer-owns-the-rom.md)).
- **The editor references it** for the importer and for reading checkpoints. It still never decodes Borgs to show them: what it shows is what the importer wrote ([0010](0010-editor-reads-plugins.md)).

### Code flows one way: core, then runtime, then editor

- **Rules go in the core** ([0003](0003-headless-rules-core.md)), including the order a game mode steps its subsystems in. The runtime's loops stay thin: for example, the core's `TrekLoop` runs trek, and the runtime draws it.
- **The editor inherits from the runtime, never the reverse.** It links the runtime's files. The runtime uses nothing of the editor's.
- **Building something in the editor first is fine.** Once the runtime uses it, it moves into the runtime, and the editor links it from there.

## Consequences

- Until the first-run screen exists, the runtime tells the player to run `hbht import`.
- How the runtime starts the editor depends on how the two Godot projects ship, which is Phase 6's packaging. Exporting them as one program with two scenes would also do.
- The editor's **Import ROM…** becomes the first-run screen's import, with progress shown.
- `N64Material` moved to the runtime, and nothing in `HBHT.Game` uses `HBHT.Editor`.
