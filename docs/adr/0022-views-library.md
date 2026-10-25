# 0022. The runtime and the editor share a views library

Status: Accepted · Oct 21, 2026 · Supersedes the part of [0012](0012-editor-holds-the-importer.md) that has the editor link the runtime's files, and adds a project to [0002](0002-godot-and-csharp.md)'s table

## Context

[0010](0010-editor-reads-plugins.md) has the editor draw with the runtime's code, and [0012](0012-editor-holds-the-importer.md) has code flow from the core to the runtime to the editor, with the editor linking the runtime's files into its `.csproj`. By October that was 25 files: the N64 material, the scene and actor loaders, the chunk scenery, particles, billboards, scrolling textures, the UI screen and painter, the conversation box and the Party tab, and the command line's parser and screenshot.

Linking compiled them twice, once into each assembly, and both times in the `HBHT.Game` namespace. That hid three problems:

- **Two copies of every type.** If Phase 6 ships the runtime and the editor as one program, with two scenes ([0012](0012-editor-holds-the-importer.md)'s consequences), the two assemblies' `HBHT.Game.N64Material` collide, and so does every other.
- **`internal` worked by accident.** The runtime called the shared files' `internal` members (`N64Material`, `Models.InstantiateActor`) and the editor called its own copy's (`GlbFile`), which only compiled because each project had the source.
- **Nothing showed what was shared.** The list lived in the editor's `.csproj`, and a file moved into it only when the editor first needed it. A shared file that came to use one of the runtime's own types would break the editor's build, not the runtime's.

## Decision

- **A Godot class library, `src/HBHT.Views`, holds what the runtime and the editor both draw with.** It references `HBHT.Core` and `HBHT.Plugins`, never `HBHT.Rom` ([0001](0001-importer-owns-the-rom.md)). The runtime and the editor reference it, and neither links another project's files.
- **Its namespace is `HBHT.Views`.** Not `HBHT.Godot`: inside any `HBHT.*` namespace, `Godot.Node` would then find `HBHT.Godot` first and stop resolving to Godot's. The runtime's classes that draw the core are its views ([0019](0019-how-the-core-tells-the-views.md)), and this is where the shared ones live.
- **Code flows one way: core, then views, then runtime, then editor.** The library uses nothing of either Godot project. Something the runtime draws moves into the library when the editor first draws it too, and something built in the editor first moves into the runtime, or into the library if the editor keeps it as well, once the runtime uses it. The runtime still uses nothing of the editor's.
- **What the other projects call is `public`.** A member stays `internal` only when the library alone uses it.
- **Scripts that a Godot scene attaches stay in their project.** Godot finds those by path (`res://Game.cs`, `res://Editor.cs`). The library's nodes are made by code, and Godot 4.7 gives each a script of its own as it's first made, so they need no path. The library uses Godot's SDK for its source generators, without which no `_Ready` or `_Process` is called, and turns off the one that writes script paths, which would name files in the library's folder that neither project has.

## Consequences

- `HBHT.Views` is a seventh row in 0002's table: the views both Godot projects draw with, depending on Godot.
- The pause menu's tab interface (`IPauseTab`) is public in the library, beside the Party tab, which is one of the tabs and which the editor shows on its own. The runtime's System and World tabs implement it there.
- The library adds no assembly a Phase 6 build lacks: the runtime and the editor carry the same `HBHT.Views.dll`, which one program would load once.
- Moving a file into the library is a `git mv` and a namespace line. A shared file can't reach the runtime's own types any more, so the build says when one would.
