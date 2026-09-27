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

| Slot | Play time (60 Hz ticks, assumed) | Flags set | Map shorts | Party IDs |
| --- | --- | --- | --- | --- |
| A | ~18.1 h | 562 | 13, 6 | 23f 24c 243 24d |
| B | ~7.8 h | 315 | 16, 17 | 23f 24c 243 24a |
| C | ~17.3 h | 547 | 2, 1 | 23f 24c 243 24d |
| D | ~18.7 h | 580 | 7, 1 | 23f 24c 243 24d |

Slot D is the furthest. All slots are named "Alaron".

## Checkpoint library

To do. Save states are captured by the user playing, not scripted. Each state should record the ROM, the ares version, the slot, and a one-line story position.
