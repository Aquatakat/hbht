# 0028. The project is HBHT, and keeps "aidyn" only where it names the game or the data

Status: Accepted · Oct 25, 2026

## Context

The project was called Aidyn, after the game it ports. "Aidyn" is a trademark, and the project is a port, not the game. It is now **Hoo Boy, Here's Trouble**, HBHT for short. The name was in the solution, every project and namespace, the data folder, the environment variables, the command line tool and the docs. Some of those spell the name of the game (Aidyn Chronicles, the ROM's files, the decomp), and some are part of what the importer wrote and what players already have: record IDs, the base plugin, saves and settings.

## Decision

- **The code is `HBHT.*`.** The solution is `HBHT.slnx`. The projects are `HBHT.Rom`, `HBHT.Plugins`, `HBHT.Core`, `HBHT.Cli`, `HBHT.Views`, `HBHT.Game` and `HBHT.Editor`, with tests in `HBHT.Core.Tests`, `HBHT.Plugins.Tests`, `HBHT.Rom.Tests` and `HBHT.TestSupport`. Folders, csproj files, root namespaces and assembly names match. [0002](0002-godot-and-csharp.md), [0003](0003-headless-rules-core.md), [0009](0009-code-organisation.md), [0010](0010-editor-reads-plugins.md), [0012](0012-editor-holds-the-importer.md) and [0022](0022-views-library.md) name projects and namespaces, and are updated to the new names without changing what they decided. `HBHT.Views` is not `HBHT.Godot` for the reason [0022](0022-views-library.md) gives: inside any `HBHT.*` namespace, `Godot.Node` would find `HBHT.Godot` first.
- **The Inspector is the editor.** `src/Aidyn.Inspector` is `src/HBHT.Editor`, in namespace `HBHT.Editor`, and its Godot project is "HBHT Editor" (its executable will be `hbht-editor`). The docs call it the editor, and `docs/systems/inspector.md` is `docs/systems/editor.md`. [0009](0009-code-organisation.md) and [0010](0010-editor-reads-plugins.md) keep their words, "the Inspector", as the history of how it became the editor.
- **The tool is `hbht`.** The command line's assembly is `hbht`, its usage and the messages that tell a player to import say `hbht import <rom>`. The runtime's Godot project is "HBHT", which is its window's title.
- **The data folder is `HBHT`.** `DataPaths.Root` is `HBHT_DATA` if set, else `HBHT` in the local application data folder. The tests' ROMs come from `HBHT_ROMS`, and the other debugging variables are `HBHT_*` too. A player who imported under the old name moves or points `HBHT_DATA` at `%LOCALAPPDATA%\Aidyn`; nothing is read from the old folder on its own.
- **What keeps "aidyn".** Names of the game and of the imported data stay, so that existing imports, saves and settings load without importing again, and `BasePlugin.ImporterVersion` is not bumped:
  - the record ID namespace `aidyn:` (`aidyn:flag/…`, `aidyn:voxel/…`), `RecordId.BaseNamespace`, and the paths made from it, `assets/aidyn/<type>/<key>` in a plugin;
  - the base plugin `aidyn.base`: its folder and its manifest ID `aidyn`, which saves list among the plugins they were made with and mods name as a dependency;
  - everything written into plugins, saves and settings: no serializer writes a .NET type or namespace name (the polymorphic records use explicit discriminators, such as an effect's `type`), so the rename changes none of their text;
  - the game's own names: "Aidyn Chronicles: The First Mage" (the base plugin's display name, the ROM and checkpoint file names, `AidynDebug.z64`), the ROM header's `AIDYN CHRONICLES`, the decomp (`blackgamma7/Aidyn`, `ref/Aidyn`) and AidynEditor as credit, the symbol map's names of the original's code;
  - the Ghidra scripts' bookmark category and project name `Aidyn`, which the existing databases under `out/ghidra` hold;
  - the test support's `TempPlugins.Aidyn`, which writes the plugin `aidyn`.

## Consequences

- Anything that built or ran the old paths (`src/Aidyn.*`, `--project src/Aidyn.Inspector`, `aidyn import`) uses the new ones. The Godot projects' gitignored `.godot` folders move with their folders, and `.godot/mono` is deleted if a build complains of stale caches.
- A Godot project's name sets its `user://` folder, which neither Godot project uses; settings and saves are in the data folder.
- Messages that tell the player to run `hbht import <rom>` are text, not data, and are tested as such.
