# 0004. Record ID scheme

Status: Accepted · Sep 27, 2026

## Context

Every reference between records is an ID: a dialogue sets a flag, a chest holds an item, a voxel points at a script. Once mods and saves exist, changing the ID format breaks all of them, so this is the hardest decision to reverse.

The original has several separate numbering schemes:

| Space | Original form | Example |
| --- | --- | --- |
| Database records | `ItemID`, a u16. The high byte is the DB type (`DB_ENTITY`, `DB_WEAPON`, …) and the low byte is the index. | `0x023F`, entity 0x3F, the first party member in the rig saves |
| Event flags | A u16 index into `GameStateFunnel`, 5,627 entries | `47` (`FLAG_MeetOriana` in the decomp) |
| Borg assets | An index into `Borg_List`. There is one index space across all 15 types. | `0x0003` (`BORG1_PlayerShadow`) |

Mods need their own names that can never collide with each other or with the base game.

## Decision

A record ID is a string:

```
<namespace>:<type>/<key>
```

- **Namespace:** `[a-z0-9_]+`. `aidyn` is reserved for the base game. Each plugin owns one namespace, its plugin ID ([0005](0005-plugin-format.md)).
- **Type:** a record type from a fixed registry in `HBHT.Plugins`, such as `entity`, `weapon`, `flag`, `dialogue`, `texture` or `voxel`.
- **Key:** one or more `/`-separated segments. In mod namespaces, keys are names in `[a-z0-9_]+`, for example `mymod:flag/met_the_hermit`. In `aidyn`, keys mirror the original numbering, in one canonical spelling per type:

| Space | `aidyn` key | Example |
| --- | --- | --- |
| DB records | The full u16 `ItemID`, as `0x` plus 4 uppercase hex digits | `aidyn:entity/0x023F` |
| Event flags | Decimal index | `aidyn:flag/47` |
| Borg assets | `Borg_List` index, as `0x` plus 4 uppercase hex digits | `aidyn:dialogue/0x0B12` |
| Nested records | The parent's key, then the index within it | `aidyn:voxel/0x1A20/12` |

The ID for a DB record keeps the type byte even though the record type already implies it. That way the key matches the decomp, AidynEditor and memory dumps exactly.

The parser rejects non-canonical spellings, such as lowercase hex, a missing `0x`, or leading zeros on decimals. One record therefore has exactly one ID string.

Human-readable names are **labels**, not IDs. A record can carry a `label` field (for example "Met Oriana"), and tools show it, but references always use the ID. Renaming a label never breaks anything.

### Runtime handles

At load time, IDs resolve to dense integer handles for each record type. Base records keep their original numbers as handles: `aidyn:flag/n` is flag handle `n`, so ported code and emulator comparisons use the same numbers. Mod records get handles after the base range, allocated in load order.

**Handles never leave the running process.** Saves store record IDs, or a table that maps IDs to the handles used in that save. Changing the load order or removing a mod can't silently point a save at the wrong flag.

## Consequences

- Two mods can't collide on a flag or item, because each has its own namespace.
- A mod patches a base record by naming it (`aidyn:weapon/0x0712`). It adds records in its own namespace.
- Comparisons with the decomp, AidynEditor and `bst.py` dumps need no translation table for base content.
- The original's limits, such as 256 records per DB type and u16 flag indices, don't apply to mods. Only the original records are bound by them.
- Save files are larger than raw handle arrays, which doesn't matter at this scale.
