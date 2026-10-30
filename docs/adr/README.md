# Architecture decision records

These are the Phase 0 decisions from the [roadmap](../roadmap.md) (task 0.5). Each record states the context, the decision, and what follows from it. When a decision changes, add a new record that supersedes the old one rather than rewriting it.

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-importer-owns-the-rom.md) | The runtime never reads the ROM; an importer turns it into a base plugin | Accepted |
| [0002](0002-godot-and-csharp.md) | Engine: Godot 4 with C# (.NET) | Accepted, added to by [0022](0022-views-library.md) |
| [0003](0003-headless-rules-core.md) | Game rules live in a headless C# library | Accepted, partly superseded by [0015](0015-the-port-no-longer-reads-the-decomp.md) and [0018](0018-the-port-fixes-bugs-and-sets-enhancements.md) |
| [0004](0004-record-ids.md) | Record ID scheme | Accepted |
| [0005](0005-plugin-format.md) | Plugin format: JSON with comments, field-level overrides | Accepted, partly superseded by [0016](0016-sequences-keep-the-originals-events.md) |
| [0006](0006-tick-model-and-rng.md) | Tick model and RNG | Accepted, partly superseded by [0017](0017-the-core-steps-every-tick.md) |
| [0007](0007-rom-versions.md) | ROM version support | Accepted, partly superseded by [0018](0018-the-port-fixes-bugs-and-sets-enhancements.md) |
| [0008](0008-content-logic-layers.md) | Content logic: records, triggers, engine features, then Lua scripts | Accepted |
| [0009](0009-code-organisation.md) | Code is organised by subsystem, and the Inspector is the one tool | Accepted, partly superseded by [0010](0010-editor-reads-plugins.md), [0011](0011-one-follow-camera.md) and [0015](0015-the-port-no-longer-reads-the-decomp.md) |
| [0010](0010-editor-reads-plugins.md) | The editor reads only imported data | Accepted, partly superseded by [0012](0012-editor-holds-the-importer.md) |
| [0011](0011-one-follow-camera.md) | One follow camera, with the original's modes | Accepted |
| [0012](0012-editor-holds-the-importer.md) | The editor holds the importer and is the first run; code flows from the core to the runtime to the editor | Accepted, partly superseded by [0022](0022-views-library.md) |
| [0013](0013-settings-in-the-options-menu.md) | Graphics and fidelity settings live in the game's own options menu, kept per player rather than per save | Accepted, partly superseded by [0023](0023-the-core-is-handed-its-settings-each-tick.md) and [0024](0024-the-options-are-pages-by-kind.md) |
| [0014](0014-menus-keep-the-originals-layouts.md) | The menus keep the original's layouts at every screen size; new layouts are a modding feature after 1.0 | Accepted |
| [0015](0015-the-port-no-longer-reads-the-decomp.md) | The port no longer reads the decomp; the rig grows its own index of the code | Accepted |
| [0016](0016-sequences-keep-the-originals-events.md) | Sequences keep the original's event stream as an asset; samples are WAV | Accepted |
| [0017](0017-the-core-steps-every-tick.md) | The core steps every tick, and per-frame behaviour is matched to a frame each system names in `Cadence` | Accepted |
| [0018](0018-the-port-fixes-bugs-and-sets-enhancements.md) | The port fixes the original's bugs outright and puts enhancements behind settings; there's no fidelity toggle | Accepted, refined by [0023](0023-the-core-is-handed-its-settings-each-tick.md), added to for content by [0029](0029-content-fixes-wait-for-a-bugfix-mod.md) |
| [0019](0019-how-the-core-tells-the-views.md) | How the core tells the views what changed: host calls and cues, events for lifecycle, polling for continuous state, returned endings | Accepted |
| [0020](0020-one-effect-vocabulary.md) | One effect vocabulary for spells, potions and enchantments: records list effects of types the core implements, with Lua a reserved type | Accepted, partly superseded by [0021](0021-instant-effects-land-in-one-place.md) |
| [0021](0021-instant-effects-land-in-one-place.md) | Instant effects land in one place for any spell, potion or flask; a potion record says how it's used, and a spell record whether it's an area | Accepted |
| [0022](0022-views-library.md) | The runtime and the editor share a views library, `HBHT.Views`, in place of the editor linking the runtime's files; code flows from the core to the views to the runtime to the editor | Accepted |
| [0023](0023-the-core-is-handed-its-settings-each-tick.md) | The core is handed what it needs of the settings each tick, an immutable record made from the player's with the command line's over them; nothing in the core reads `Settings` | Accepted |
| [0024](0024-the-options-are-pages-by-kind.md) | The options are pages by kind (Audio, Video, Enhancements), in Title Case, and the sky's placement is off the menu | Accepted |
| [0025](0025-chunks-load-off-the-main-thread.md) | Chunks load off the main thread where it can't change the game: what changes it stays on the main thread, reading ahead and building nodes outside the tree go to one builder thread, and the main thread never waits on it | Accepted |
| [0026](0026-controls-are-bound-by-situation.md) | Controls are listed by what the player does in each situation, bound per device in the core, with conflicts checked within a situation and the essential controls never left unbound | Accepted |
| [0027](0027-conversations-take-the-menus-controls.md) | Cut-scenes and conversations take the menus' cursor, Accept, Back and Close Menu, and have no controls of their own | Accepted |
| [0028](0028-the-project-is-hbht.md) | The project is HBHT: the code is `HBHT.*`, the editor is `HBHT.Editor`, the tool is `hbht` and the data folder is `HBHT`; "aidyn" stays for the game's names and the imported data (record IDs, `aidyn.base`) | Accepted |
| [0029](0029-content-fixes-wait-for-a-bugfix-mod.md) | The base import fixes only content that breaks the game; other content fixes wait for a bugfix mod | Accepted |
| [0030](0030-modern-variants-are-held-to-the-originals-reach.md) | A modern variant of a system, such as Modern movement, is a setting beside the original's rules, which stay the default, and is held to the original's reach by a check over all the content | Proposed |

Statuses: **Proposed** means drafted and waiting for review. **Accepted** means agreed. **Superseded** means replaced by a later record, which it links to.
