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
- **Code segment:** changed (about 760k bytes differ even after allowing for the shift).
- **DB block:** every record is identical. The only change is new text: five Controller Pak error messages appended to `String/Common` and four to `String/Controller`. The two tables grew, which moves everything after them (see below).

So 1.1 contains code fixes and a few new messages, and all assets and records are shared. 1.0 is the primary target.

The debug ROM's `Borg_List` is at `0x1F98790` and its DB block starts at `0x1FD1730`.

## What `Aidyn.Rom` found (0.2, 0.3)

- **All three ROMs share one Borg archive.** The debug ROM's archive and `Borg_List` are byte-identical to 1.0's, as 1.1's are. There are 14,585 entries.
- **The `Borg_List` header** is the entry count followed by the archive's size in bytes (`0x1EA29F0`), which equals `Borg_List − archive start` in every version.
- **Stored sizes are padded to 8 bytes** with zeros, and entries start on 16-byte boundaries. An LZB stream therefore ends 0–7 bytes before its stored size.
- **Compression:** 14,047 Borgs are LZB and 538 are stored raw. None use LZ01x, which is only used for text. LZB is UCL's NRV2B with an 8-bit bit buffer. LZ01x is LZO1X from LZO 1.06.
- **1.1's text changed.** `String/Common` sits at 1.0's offset plus `0x40`, but it was edited and now decompresses to `0x357E` bytes instead of `0x340C`. Everything after it moves by `+0x270`. `String/Combat` and `String/Cheats` are unchanged apart from the move. `String/Controller` has changed too. The debug ROM's four `RomString` tables match 1.0's.
