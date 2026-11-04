# 0037. Save format 2

Status: Accepted · Nov 4, 2026

## Context

A save (`HBHT.Plugins.Saves`, [saves](../systems/saves.md)) names records by their IDs. The data format overhaul (0031–0036) changed IDs a save names, and one field's shape: weapons and armor became items (0031), dialogue entities became entities, four of them merged into the entity of the same name (0032), dialogue records became scripts (0034), and map objects, which were keyed by their Borg9, are keyed by their chunk, with minimaps folded into their map sets (0035). The save's own shape barely moved: one field, a minimap's `minimap`, became `mapSet`.

Saves made before the overhaul are in players' hands, as format 1: the format the port wrote from its first save to the commit before the overhaul's first slice (`aca1a8b`, importer 66). Since 0035, such a save with any memory doesn't read at all, as `aidyn:voxel/…` and `aidyn:minimap/…` aren't record types any more, and the rest of it loads with its weapons and armor missing. The user's call: don't break players' saves, and upconvert them silently.

## Decision

- **The save's format number is 2.** `SaveGame.CurrentFormat` is 2, and the port writes only format 2: `GameState.Save`, `hbht save new`, the checkpoint reader (`CheckpointSave`) and the Controller Pak reader (`PakSaves`).
- **A format-1 save is upconverted on read,** silently: `SaveFile.Parse` rewrites its JSON before the typed read (`SaveUpconverter`), so the typed save, and everything after it, sees format 2. A save with no `format` is format 1, as every writer of format 1 wrote one. The game loads, plays and saves it again as format 2, with no prompt or warning to the player. A format newer than the port's is refused, as before.
- **The rules,** over every string in the save that is a record ID, whatever its namespace but where said:
  - `<ns>:weapon/K` and `<ns>:armor/K` → `<ns>:item/K` (0031; the keys don't collide).
  - `aidyn:dialogentity/K` → `aidyn:entity/K`, but the four twins, which go to their entity: `0x1404` → `0x02DD` (Cadme), `0x1406` → `0x0224` (Pochanargat), `0x1418` → `0x0214` (Tamberlain), `0x1419` → `0x02DB` (Xibid) (0032). A mod's `dialogentity` goes to its `entity` of the same key.
  - `<ns>:dialogue/K` → `<ns>:script/K` (0034). Format 1 names no script, but the rule costs nothing.
  - `aidyn:voxel/<borg>/<n>` → `aidyn:mapobject/<set>/<name>/<n>`, the same index (0035). The chunk is the one the installed plugins' chunks say came from that Borg9 (each base chunk's `borg`). Where a Borg9 was shared between cells (nine: the Overworld's sea, `0x3559`, and eight Battlefields arenas), it's the save's own place if that's one of them. Otherwise, for a Borg9 no chunk names, and for a mod's map object (format 1 named it by its map, which no chunk records), the memory is dropped.
  - `aidyn:minimap/n` → `aidyn:mapset/n`, and a minimap's `minimap` field → `mapSet` (0035).
  - The place's chunk, for a row outside A–Z: `"<row>,<column>"` → `"<row>_<column>"` (0035's spelling; only the importer's northern sea has such cells).
- **What can't be converted is dropped, never a failure,** and each drop is a line in the load's problems (`SaveGame.Upconverted`, which `GameState.Load` adds to `Problems`): `hbht save show` prints them, the editor shows them, the runtime logs them. Format 1 names nothing else that went: flags are named by ID and only set plain ones are saved, so the derived journal flags the overhaul dropped were never saved, and a flag no plugin has is already a load problem.
- **Reading a format-2 save pays nothing for this.** The Borg9 → chunks table is built from the plugins only when a format-1 save names a voxel.
- **The upconverter stays** as long as there are format-1 saves to read. A later format adds its own step after it, so a save is brought up one format at a time.

## Consequences

- A player's save from before the overhaul loads into the port after it, and the next save they make is format 2. A format-2 save doesn't load in a port from before the overhaul, which refuses a format newer than its own.
- Parsing a save takes the plugin set, for the map objects' chunks. A format-2 save reads without one; a format-1 save read without one drops its memories, and says so.
- The rules are by ID, not by field, so a format-1 save's IDs move wherever they are, a field the port adds later included; only the minimap's field and the place's chunk are renamed by place.
