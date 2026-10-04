# Architecture decision records

These are the Phase 0 decisions from the [roadmap](../roadmap.md) (task 0.5). Each record states the context, the decision, and what follows from it. When a decision changes, add a new record that supersedes the old one rather than rewriting it.

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-importer-owns-the-rom.md) | The runtime never reads the ROM; an importer turns it into a base plugin | Accepted |
| [0002](0002-godot-and-csharp.md) | Engine: Godot 4 with C# (.NET) | Accepted |
| [0003](0003-headless-rules-core.md) | Game rules live in a headless C# library | Accepted |
| [0004](0004-record-ids.md) | Record ID scheme | Accepted |
| [0005](0005-plugin-format.md) | Plugin format: JSON with comments, field-level overrides | Accepted |
| [0006](0006-tick-model-and-rng.md) | Tick model and RNG | Accepted |
| [0007](0007-rom-versions.md) | ROM version support | Accepted |
| [0008](0008-content-logic-layers.md) | Content logic: records, triggers, engine features, then Lua scripts | Accepted |
| [0009](0009-code-organisation.md) | Code is organised by subsystem, and the Inspector is the one tool | Accepted, partly superseded by [0010](0010-editor-reads-plugins.md) and [0011](0011-one-follow-camera.md) |
| [0010](0010-editor-reads-plugins.md) | The editor reads only imported data | Accepted, partly superseded by [0012](0012-editor-holds-the-importer.md) |
| [0011](0011-one-follow-camera.md) | One follow camera, with the original's modes | Accepted |
| [0012](0012-editor-holds-the-importer.md) | The editor holds the importer and is the first run; code flows from the core to the runtime to the editor | Accepted |
| [0013](0013-settings-in-the-options-menu.md) | Graphics and fidelity settings live in the game's own options menu, kept per player rather than per save | Accepted |
| [0014](0014-menus-keep-the-originals-layouts.md) | The menus keep the original's layouts at every screen size; new layouts are a modding feature after 1.0 | Accepted |

Statuses: **Proposed** means drafted and waiting for review. **Accepted** means agreed. **Superseded** means replaced by a later record, which it links to.
