# 0002. Engine: Godot 4 with C#

Status: Accepted · Sep 26, 2026

## Context

Rendering N64-era geometry is a small part of the work. The rest is a dense menu UI, audio mixing, input remapping, cross-platform builds, and eventually a mod editor. The importer ships to end users, because they bring their own ROM, so it can't depend on a Python install.

The alternatives were Godot with GDScript, which would need a second language for the importer and headless core, and a custom engine. A custom engine only pays off for exact N64 fidelity, and N64Recomp already serves that goal better.

## Decision

Use **Godot 4 with C# (.NET)** for the runtime and tools. One language covers the importer, the rules core, the runtime and the mod tools.

The code is split into plain .NET projects that the Godot project references:

| Project | Contents | Depends on Godot |
| --- | --- | --- |
| `HBHT.Rom` | ROM identification, Borg archive, DB readers, the importer | No |
| `HBHT.Plugins` | Plugin format, loading, merging, ID resolution | No |
| `HBHT.Core` | Rules core ([0003](0003-headless-rules-core.md)) | No |
| `HBHT.Game` | The Godot runtime: rendering, input, UI, audio | Yes |
| `HBHT.Editor` | The asset inspector (roadmap 0.6), later the mod editor | Yes |
| `*.Tests` | Unit and differential tests | No |

Pin the Godot version and the .NET SDK (with `global.json`) in the repository, the same way the rig pins ares.

## Consequences

- Most of the code can be built and tested with `dotnet test`, with no editor or GPU.
- Godot's C# support has platform gaps compared with GDScript. Web export is the notable one. A browser build is not a goal.
- Godot upgrades are deliberate, pinned changes. They are not picked up automatically.
