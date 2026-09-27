# Reference rig (roadmap 0.1)

The emulator setup used as ground truth for the life of the project.

## ROMs

ROMs live in `roms/` (gitignored). Never commit ROM-derived data.

| File | Rev byte `0x3F` | SHA-1 |
| --- | --- | --- |
| `Aidyn Chronicles - The First Mage (U) (V1.0) [!].z64` | `00` | `47ae795a2b12783a13e2b8d03439ce9a39fc826c` |
| `Aidyn Chronicles - The First Mage (U) (V1.1).z64` | `01` | `f80e135e7e962308f51bb9f7600b25213549c688` |
| `AidynDebug.z64` (02.01d-PRERELEASE) | n/a (blank header) | `8169959ae059869b46461712a829ca0fe15c902c` |

All three are big-endian `.z64`, 32 MB, with the retail CIC-6102 IPL3 and valid header checksums. Retail game code is `NAYE`. The debug ROM has no internal name or game code.

### NA 1.0 vs 1.1

There is a real second revision. Compared with 1.0:

- **Borg archive and `Borg_List`:** byte-identical, shifted by `+0x40`. `Borg_List` is at `0x1F89C50` and the DB block starts at `0x1FC2BF0`.
- **Code segment:** changed (about 760k bytes differ even after allowing for the shift).
- **DB block:** mostly identical after the shift. There are small edits near `0x1FE7B80` (1.0 offset), and about 18 KB differs from `0x1FEA3AA` to the end of the block. Not yet identified.

So 1.1 contains code fixes plus some table changes, and all assets are shared. The decomp's `us.yaml` targets 1.0, which stays the primary target. 1.1 needs its own offset table, and its DB changes are worth diffing properly once 0.4 can decode the tables.

The debug ROM's `Borg_List` is at `0x1F98790` and its DB block starts at `0x1FD1730`.

### What `Aidyn.Rom` found (0.2, 0.3)

- **All three ROMs share one Borg archive.** The debug ROM's archive and `Borg_List` are byte-identical to 1.0's, as 1.1's are. There are 14,585 entries.
- **The `Borg_List` header** is the entry count followed by the archive's size in bytes (`0x1EA29F0`), which equals `Borg_List − archive start` in every version.
- **Stored sizes are padded to 8 bytes** with zeros, and entries start on 16-byte boundaries. An LZB stream therefore ends 0–7 bytes before its stored size.
- **Compression:** 14,047 Borgs are LZB and 538 are stored raw. None use LZ01x, which is only used for text. LZB is UCL's NRV2B with an 8-bit bit buffer. LZ01x is LZO1X from LZO 1.06.
- **1.1's text changed.** `String/Common` sits at 1.0's offset plus `0x40`, but it was edited and now decompresses to `0x357E` bytes instead of `0x340C`. Everything after it moves by `+0x270`. `String/Combat` and `String/Cheats` are unchanged apart from the move. `String/Controller` has changed too. The debug ROM's four `RomString` tables match 1.0's.

## Emulator: ares v148

Installed with winget (`ares-emulator.ares`). Launch through the script, which enables the GDB server for that run only:

```powershell
tools/ares.ps1 debug      # or v10, v11; -SaveState 1..9 to load a slot
```

- The GDB server listens on port 9123. Note that it binds `0.0.0.0`, so it is reachable from the LAN while running.
- `Input/Defocus` is overridden to `Allow`. The user setting is `Pause`, which freezes emulation as soon as gdb takes focus. If the PC is stuck in IPL3 (`0xA40014xx`), this setting is the cause.
- The Expansion Pak is on (the ares default). This matches the voxel set the reimplementation targets.
- **Pin ares at v148.** Save states are tied to the emulator version.

## Debugger: gdb-multiarch 18.1 (MSYS2 UCRT64)

Located at `C:\msys64\ucrt64\bin\gdb-multiarch.exe`.

```
gdb-multiarch -ex "set architecture mips:4300" -ex "set endian big" -ex "target remote 127.0.0.1:9123"
```

Resolve addresses against the decomp's `symbol_addrs.debug.txt` (debug ROM) or `symbol_addrs.us.txt` (1.0). A first attach to the debug ROM sampled `InitProc+0x1fc` and libultra thread code, which confirms the game is running.

## Existing saves

Mupen64Plus saves are in `%APPDATA%\Mupen64Plus\save`. Only `AIDYN_CHRONICLES (unknown rom).mpk` (Oct 2025) holds Aidyn data. The other Aidyn `.mpk` files are empty paks.

Mupen `.mpk` files are four 32 KB paks back to back. The saves are in pak 1, in four 28-page notes (`NAYE`/`78`, "CHRONICLES" A–D). The `SaveDatStruct` header (see `include/savefiles.h` in the decomp) decodes as:

Play time is `PlayTime` in 60 Hz ticks (`TICKSPERSECOND`, `world.h`), advanced by the frame delta. The frame delta counts 60 Hz polls and is capped at 1–6. The map position is the chunk the debug overlay shows as `mapShort1 + 'A'-1` followed by `mapShort2`.

| Slot | Play time | Flags set | Chunk | Party IDs | Story position (user) |
| --- | --- | --- | --- | --- | --- |
| B | 7:45:26 | 315 | P17 | 23f 24c 243 24a | Before Shamsuk's Tower |
| C | 17:17:43 | 547 | B01 | 23f 24c 243 24d | Before Rooughah |
| A | 18:04:28 | 562 | M06 | 23f 24c 243 24d | Before the final encounter |
| D | 18:44:19 | 580 | G01 | 23f 24c 243 24d | Before Rooughah, in Terminor. This is the latest save; the user walked back across the map to stand by a horse. |

All slots are named "Alaron". The playthrough used a hacked ROM with low-level enemies, so party stats, equipment and spells are below par for the story position. These saves are good for flag, map and quest state, but not for combat parity. All four load on the debug ROM too.

### Converting Mupen `.mpk` to ares `.pak`

ares stores one 32 KB Controller Pak next to the ROM as `<rom basename>.pak`. It uses the same raw big-endian layout as Mupen, so converting means taking the first 32 KB:

```bash
head -c 32768 "AIDYN_CHRONICLES (unknown rom).mpk" > "roms/Aidyn Chronicles - The First Mage (U) (V1.0) [!].pak"
```

This was checked against an ares-written `AidynDebug.pak`. Note table, inode table and save data layout are identical. The only difference is the page-0 ID block: Mupen writes a fixed template with serial `051a5f13`, and ares writes its own. Both pass libultra's ID checksums in all four copies (`0x20/0x60/0x80/0xC0`) and the inode-table checksums. The game's own save checksum sits inside the note data, so it is unaffected.

The debug and retail saves use the same note (`NAYE`/`78`, "CHRONICLES"), the same 28-page size, and the same header layout. Whether the debug ROM can load a retail save, which would put the flag viewer on a late-game state, is untested.

## Debug ROM controls

These are gated on `gDebugFlag` (`0x800E68A0`), which is on in the debug build. Sources are in the decomp.

| Input (overworld) | Effect | Source |
| --- | --- | --- |
| C-up | Cycle the performance overlay (6 states): memory bars, object count, ground type, chunk plus position, fps | `PlayerMove.cpp`, `app.cpp` |
| **L + Start** | **Open the big debug menu** | `gametrek.cpp` |
| R + A (minimap open) | Minimap shows everything | `gametrek.cpp` |
| L + D-left | Load only the centre map chunk | `PlayerMove.cpp` |
| R + D-right | Replace the next loaded scene voxels with boulders (reference objects) | `PlayerMove.cpp` |

Big debug menu entries (`debug_menu_funcs` in `debug.cpp`; labels come from `COMMONSTRING_DebugBigMenu00+i`):

| # | Function |
| --- | --- |
| 0 | `debug_gamestatefunnel`: the event-flag viewer and editor |
| 2 | `gamestste_cheats` |
| 3 | `debug_add_party_member` |
| 14 | Clear inventory |
| 15 | Play the credits |
| 16 | Unused debug toggle widget |
| 17 | Toggle enemies hostile |
| 18 | Toggle "only teleporters active" |
| 19 | Item DB browser: armor, shield, item, weapon, potion; A adds the item to the inventory |
| 20 | Bad-reference object hunt (voxel index display) |

Entries 1 and 4–13 are null in the function table.

## Checkpoint library

Save states are captured by the user playing, not scripted. They live in `roms/` and are named `<rom basename> - <label>.bsN`. They were made with ares v148, whose state header reads `BST1` with serializer tag `v152`. Each state's `PlayTime` was read back with `tools/bst.py`, and all eight match their pak slot plus the time spent before saving the state.

| Label | Pak slot | 1.0 state `PlayTime` | Debug state `PlayTime` |
| --- | --- | --- | --- |
| Before Shamsuk's Tower | B | 7:45:33 | 7:45:42 |
| Before Rooughah | C | 17:18:24 | 17:18:00 |
| Before final encounter | A | 18:04:54 | 18:06:44 |
| Before Rooughah, in Terminor | D | 18:45:11 | 18:44:32 |

Load a checkpoint by label:

```powershell
tools/ares.ps1 debug -Checkpoint "Before final encounter"
```

This copies the named state to the ROM's `.bs9` slot and passes `--save-state 9`. Slot 9 is scratch, and the named files are never written. The ares slot convention is `<rom basename>.bsN`.

### Reading state RAM offline: `tools/bst.py`

ares states store RDRAM uncompressed as 32-bit little-endian words. `bst.py` locates RDRAM by fingerprinting the code segment against the matching ROM, found by filename prefix. It then reads it as big-endian KSEG0 addresses:

```bash
python tools/bst.py "<state>" 0x800f1bbc 16     # hexdump
python tools/bst.py "<state>" --word 0x800f1bbc # one u32
python tools/bst.py "<state>" --dump out.bin    # 8 MB RDRAM image
```

This is the preferred way to inspect checkpoints: it's deterministic, repeatable, and doesn't need a live emulator. Symbols useful so far:

| Symbol | Debug | 1.0 |
| --- | --- | --- |
| `TerrainPointer` | `0x800F1BBC` | `0x800E303C` |
| `TerrainStruct.PlayTime` | `+0x34` | `+0x34` |
| `TerrainStruct.InGameTime` | `+0x20` | `+0x20` |

### Known issue: GDB after loading a state

When the debug ROM was booted with `--save-state`, gdb got `Ignoring packet error` on connect. After the failed handshake the GDB port stopped listening, and on one attempt ares stopped responding. A plain boot with no state attached fine earlier. The cause isn't isolated yet: it could be state loading, or the stub not recovering from a client that exits without detaching. Until it's fixed, use `bst.py` for checkpoint inspection and keep gdb for live sessions started from a cold boot.
