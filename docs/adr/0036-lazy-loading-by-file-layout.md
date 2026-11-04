# 0036. Lazy loading by file layout

Status: Accepted · Nov 3, 2026 · Supersedes [0005](0005-plugin-format.md)'s compiled cache as what keeps loading fast

## Context

0005 planned a compiled binary cache of the merged plugin set to keep loading fast. It was never built. Loading parses every `records/**/*.json` into JSON nodes and clones each before any read, which takes the base plugin about half a second, most of it in two types: the 680 chunks (0035) and the 892 scripts (0034), which together are most of the base plugin's bytes. Both are read a few at a time: the 3×3 chunks round the player, and the script a conversation or a cut-scene starts.

## Decision

- **The base plugin writes one record per file for its chunks and scripts**: `records/chunks/<map set>/<name>.json` holds `aidyn:chunk/<map set>/<name>`, and `records/scripts/<key>.json` holds `aidyn:script/<key>`. The path is the record's ID.
- **`PluginSet` indexes those files by their path without reading them,** and reads and merges a record the first time it's asked for. A file whose record isn't the one its path names is an error when it's read.
- **Mods' records load as before, eagerly,** in any file layout the author likes (0005). A mod's patch to a lazily indexed record reads that record as the patch loads, so patches apply and the conflict report sees them as before.
- **`All<T>()` still reads every record of a type,** and validation reads every record; those are the editor's, the quest atlas's and the CLI's, off the game's hot path. The chunk index (0035) comes from the chunks' IDs, so finding the chunk in a cell reads no file.
- **The compiled cache becomes an optimisation** that may come later, not something loading needs.

## Consequences

- The game reads the chunks and scripts it plays, not all of them, and the editor reads what it shows.
- Any other type could follow the convention later by being listed with its folder; it's the base plugin's layout, which only the importer writes, so nothing else depends on it.
- A record read lazily is read on whichever thread first asks for it; the set reads it once under a lock.
