# 0001. The runtime never reads the ROM

Status: Accepted · Sep 27, 2026

## Context

Players bring their own ROM. Nothing derived from it can ship with the project, and the ROM layout is version-specific (see [0007](0007-rom-versions.md)). The mod system also has to express every piece of game content, or mods will hit walls that the base game doesn't.

OpenMW solves both problems by treating `Morrowind.esm` as just another content file.

## Decision

A one-time **importer** is the only code that reads the ROM. It converts the ROM into:

- **`aidyn.base`**, a plugin in the normal plugin format ([0005](0005-plugin-format.md)) that holds every record, and
- an **asset cache** of converted meshes, textures and audio in open formats, keyed by record ID.

The engine loads only plugins and the asset cache. It has no code path that opens a ROM.

The importer is a C# library with two front ends: a command-line tool and a first-run screen in the game. Its output goes to the user's data directory, never into the repository or a release.

The importer is **deterministic**. The same ROM and the same importer version always give byte-identical output. The output records the importer version and the ROM's hash. The runtime re-imports when either changes.

## Consequences

- The base game is a mod, so the plugin format is exercised from day one. There is no private back door for the base content.
- Content that the original hardcodes in engine code (roadmap track 1H) has to become records, or `aidyn.base` can't express it. This makes the 1H audit mandatory, not optional.
- Deterministic output makes importer changes reviewable. Diffing two imports shows exactly what a change did.
- Anything the importer gets wrong is fixed by re-importing, not by patching the runtime.
- Test fixtures that need real game data have to come from a local import. They can't be committed.
