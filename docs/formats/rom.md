# ROM layout and versions (roadmap 0.2, 0.3)

What the three ROMs share and where they differ, and what the Borg archive reader found. The ROMs themselves and their hashes are in the [reference rig](../reference-rig.md).

## The layout (NA 1.0)

| Region | Contents |
| --- | --- |
| `0x0001000`–`0x00E7220` | Code segment. It also holds data tables, such as the per-map-set chunk lists and the dialogue SFX tables ([hardcoded content](../hardcoded-content.md)). |
| `0x00E7220`–`0x1F89C10` | **Borg archive**: about 30 MB holding every art, level, audio and dialogue asset. |
| `0x1F89C10` | `Borg_List`: the archive index. Each entry has type, compression, compressed and uncompressed sizes, and offset. |
| `0x1FC2BB0`–`0x1FEEFD0` | Fixed databases ([db](db.md)): Entity, Weapon, Armor, Spell, Item, Chest, DialogEntity, GameStateMod, GameStateFunnel, Audiokey, Journal, Theatre menu, cinematic text, controller and combat strings, CampEncounters, cheats, credits, and the item, stat and spell string tables. |

Compression comes in two schemes, LZB (most Borgs) and LZ01x (text), and strings also go through a decrypt step ([text](text.md)).

## The Borg types

| # | Type | Notes |
| --- | --- | --- |
| 1 | Texture | N64 texel formats, with odd rows interleaved ([textures](textures.md)) |
| 2 | Geometry | Vertices plus F3DEX display lists, mostly `G_TRI1` ([geometry](geometry.md)) |
| 3 | Camera | FOV and clip planes |
| 4 | Light | |
| 5 | Scene | Transform tree, a small instruction list, and particle emitters ([scenes](scenes.md)) |
| 6 | Animation | Keyframe tracks bound to a Borg5 ([animation](animation.md)) |
| 7 | Actor | A Borg5 plus a set of Borg6 animations with branch and transition data ([actors](actors.md)) |
| 8 | UI image | |
| 9 | Map chunk | Collision triangles, ground types and map objects ([maps](maps.md)) |
| 11 | Sample | Mono PCM, signed 8-bit or 16-bit BE, with no rate of its own ([audio](audio.md)) |
| 12 | Sequence | Music and sound effects: rows of events over up to 16 tracks |
| 13 | Script | Dialogue and cut-scene bytecode with about 60 opcodes ([dialogue](dialogue.md)) |
| 0, 10, 14 | Unused | Named GEN, CollisionMaterial and GameState |

## NA 1.0 vs 1.1

1.1 is a real second revision. Compared with 1.0:

- **Borg archive and `Borg_List`:** byte-identical, shifted by `+0x40`. `Borg_List` is at `0x1F89C50` and the DB block starts at `0x1FC2BF0`.
- **Code segment:** changed (about 760k bytes differ even after allowing for the shift). Its data moves by `-0x10` up to the camp tables and by `+0x40` from the fonts (`0xDAF08` in 1.0) on, so the content tables are 1.0's moved (see below).
- **DB block:** every record is identical. The only change is new text: five Controller Pak error messages appended to `String/Common` and four to `String/Controller`. The two tables grew, which moves everything after them (see below).

So 1.1 contains code fixes and a few new messages, and all assets and records are shared. 1.0 is the primary target.

The debug ROM's `Borg_List` is at `0x1F98790` and its DB block starts at `0x1FD1730`.

## Where each version has its tables

`RomVersions` holds one table of what the code reads: each code-segment table and each table of the DB block, with its length and NA 1.0's offset, and for a code-segment table the debug ROM's offset too. The other versions are NA 1.0 moved: a list of steps, each moving the 1.0 offsets from where it starts up to the next step by one amount, and the string tables whose length changed.

| 1.0 offsets from | NA 1.1 moves them | What's there |
| --- | --- | --- |
| `0x008BAC4` | `-0xC4` | Code, through the spell visuals' refused and resisted actors |
| `0x008D710` | `-0xB4` | The spell visuals' functions |
| `0x008DDE4` | `-0x10` | The rest of the code, and the data up to the camp tables |
| `0x00DAF08` | `+0x40` | The data from the fonts on, the Borg archive, `Borg_List`, and the DB block to `String/Common`, which grew from `0x2830` bytes to `0x2960` |
| `0x1FEA3B0` | `+0x170` | `String/Copyright` to `String/Controller`, which grew from `0x410` bytes to `0x510` |
| `0x1FED950` | `+0x270` | `String/Combat` to the end |

Each step starts at the first 1.0 offset where 1.1's bytes match at that move. The change between them lies in the bytes before, which match neither move or are zeros (`0xD8ABC`–`0xDAF08` for `+0x40`). 1.1's code before `0x8BAC4` moves by other amounts, mostly `-0xD0`, which no table needs yet.

The debug ROM moves 1.0's Borg archive and DB block by `+0xEB80`, and `String/PotionNames` by `+0xEB50`, after its credits, which are `0x6F0` bytes, not `0x720`. Its code segment is a different build, whose data is in nearly the same order but moves by a different amount almost table by table (`+0xB058` to `+0xEB80`), so its code-segment tables, and the map sets' chunk lists at `0xEA850`, are their own column.

The tables' bytes are 1.0's in every version except where the build put addresses in them: the spell visuals' code and jump tables, and the pointer tables (`Cinematics`, `CinematicFades`, `MinimapSections`). `RomLayoutTests.EveryTableIsWhereTheRomHasIt` checks every table in every version against 1.0's: the same bytes, the same strings (but for the three that changed), the same instructions without their immediates, jump table entries landing at the same place in the same function, and pointers to the same bytes. 1.0's own tables are pinned by their readers' tests.

A new table is one row: its length, 1.0's offset and the debug ROM's. 1.1 needs nothing more unless the table is before `0x8BAC4`.

## What `Aidyn.Rom` found (0.2, 0.3)

- **All three ROMs share one Borg archive.** The debug ROM's archive and `Borg_List` are byte-identical to 1.0's, as 1.1's are. There are 14,585 entries.
- **The `Borg_List` header** is the entry count followed by the archive's size in bytes (`0x1EA29F0`), which equals `Borg_List − archive start` in every version.
- **Stored sizes are padded to 8 bytes** with zeros, and entries start on 16-byte boundaries. An LZB stream therefore ends 0–7 bytes before its stored size.
- **Compression:** 14,047 Borgs are LZB and 538 are stored raw. None use LZ01x, which is only used for text. LZB is UCL's NRV2B with an 8-bit bit buffer. LZ01x is LZO1X from LZO 1.06.
- **1.1's text changed.** `String/Common` sits at 1.0's offset plus `0x40`, but it was edited and now decompresses to `0x357E` bytes instead of `0x340C`. Everything after it moves by `+0x170`, and everything after `String/Controller`, which grew by `0x100`, by `+0x270`. `String/Combat` and `String/Cheats` are unchanged apart from the move. `String/Controller` has changed too. The debug ROM's four `RomString` tables match 1.0's.
