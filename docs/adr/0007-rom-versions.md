# 0007. ROM version support

Status: Accepted · Sep 27, 2026

## Context

Players bring their own ROM, so the importer sees whatever version and byte order they have. The decomp's `us.yaml` targets NA 1.0. The rig ([reference-rig.md](../reference-rig.md)) found that NA 1.1 is a real revision:

- **Borg archive and `Borg_List`:** byte-identical to 1.0, shifted by `+0x40`.
- **Code segment:** changed. About 760 KB differs even after allowing for the shift.
- **DB block:** mostly the same, with small edits near `0x1FE7B80` and about 18 KB differing at the end. These aren't identified yet.

The debug ROM (02.01d-PRERELEASE) has its own layout and content. PAL versions exist but haven't been examined.

## Decision

- **Byte order:** normalise to big-endian (`.z64`) before anything else. Detect the order from the first word: `80 37 12 40` is `.z64`, `37 80 40 12` is `.v64` (byte-swapped), and `40 12 37 80` is `.n64` (word-swapped).
- **Identification:** identify the version by the SHA-1 of the normalised image. Hash tables live in `Aidyn.Rom` beside the offsets.
- **Offsets:** every ROM offset comes from a per-version table: `Borg_List`, the DB block and each table in it, and the code-segment data tables (map chunk lists, dialogue SFX). No offset is hardcoded outside the table.
- **Support order:**
  1. **NA 1.0** is the primary target (SHA-1 `47ae795a…fc826c`).
  2. **NA 1.1** follows once 0.4 can decode the DB tables and the differing ranges are identified. Its assets are shared with 1.0, so it gets the same `aidyn.base` records apart from those identified DB changes.
  3. **PAL** is later, after someone examines it.
- **Unknown ROMs are refused**, with a message that shows the hash. That includes hacks, such as the low-level-enemy ROM the rig saves were played on. Guessing offsets by signature risks importing a subtly wrong `aidyn.base`, and every later bug report would then be suspect.
- **The debug ROM is for research only.** `Aidyn.Rom` and the inspector can read it through its own offset table, but the importer never builds `aidyn.base` from it.

## Consequences

- Game rules are ported from 1.0 behaviour, because that's what the decomp describes. 1.1's code fixes, once identified, become fidelity toggles rather than silent changes.
- Content differences between versions show up as differences in `aidyn.base`, and deterministic imports ([0001](0001-importer-owns-the-rom.md)) make them easy to diff.
- A player with a ROM that isn't supported gets a clear error.
