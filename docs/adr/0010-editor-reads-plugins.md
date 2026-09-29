# 0010. The editor reads only imported data

Status: Accepted · Sep 29, 2026 · Supersedes the part of [0009](0009-code-organisation.md) that keeps browsing raw Borgs as a research view

## Context

[0009](0009-code-organisation.md) made the Inspector the one tool and said it becomes the editor, reading and editing the imported plugins. It also kept the Inspector's first feature, browsing raw Borgs from the ROM, as "a research view while there's still decoding to do." That would make the Inspector read two sources: the ROM directly, and the plugins the importer writes.

That split is worth deciding on before the Inspector grows, because every view added to it has to pick a source. Adding the quest atlas and the whole-map view that the HTML viewers have would mean building them twice: once over the ROM's types now, and again over plugin records when the editor needs them.

There are two models to follow. Bethesda's Construction Set edits Morrowind's ESM files, which are the game's own moddable data. OpenMW-CS edits content in OpenMW's own terms. Our ROM is not a moddable data format. It's closer to a compiled binary, and nobody will ship a ROM patch through this tool. Everything the runtime uses already goes through the importer ([0001](0001-importer-owns-the-rom.md)), and `aidyn.base` is our master file. So OpenMW-CS is the model to follow.

## Decision

- **The Inspector is the editor, and it reads only plugins:** `aidyn.base` and any plugins over it, as the runtime loads them. It doesn't decode Borgs. It may run the importer, as the game's first-run screen does, but it doesn't read the ROM itself.
- **The editor sees exactly what the importer writes.** If the editor needs data to show something, that data is imported into the base plugin. Each format's validation gate is to decode it, import it, then view it in the editor.
- **The editor draws imported data with the runtime's code** wherever the runtime already draws it. `N64Material` is shared this way already. Scenes, actors and map sets should be too, so the editor and the game can't disagree.
- **The raw Borg browser is frozen.** Like the HTML viewers in `tools/`, it gets fixes only. New research into formats uses the CLI's dumps and exports, the tests and the decomp. The Borg browser is deleted along with the HTML viewers once the editor shows what they show.

## Consequences

- The feature-parity work 0009 listed changes. The editor catches up with the viewers over plugin records, not ROM types. It can do this only as fast as data is imported, so parity with the quest atlas waits for the flag table (2.1), the scripts (2.4) and the remaining map object types to be imported. The runtime needs all three anyway.
- `QuestAtlas` is rebuilt over plugin records, not ROM types. That also covers Phase 5's goal of an atlas that includes mod content.
- Until then, the HTML quest atlas stays in use for looking up flags and scripts.
- Viewing a format before it's imported is no longer possible in the editor. The CLI's exports (PNG, glTF, JSON, text) cover quick looks during decoding.
- The editor's project references `Aidyn.Plugins` and `Aidyn.Core`. It references `Aidyn.Rom` only for the frozen Borg browser and for running the importer, and the browser's reference goes when the browser is deleted.
