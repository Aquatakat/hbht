# Reference rig (roadmap 0.1)

The emulator setup used as ground truth for the life of the project.

## ROMs

ROMs live in `roms/` (gitignored). Never commit ROM-derived data.

| File | Rev byte `0x3F` | SHA-1 |
| --- | --- | --- |
| `Aidyn Chronicles - The First Mage (U) (V1.0) [!].z64` | `00` | `47ae795a2b12783a13e2b8d03439ce9a39fc826c` |
| `Aidyn Chronicles - The First Mage (U) (V1.1).z64` | `01` | `f80e135e7e962308f51bb9f7600b25213549c688` |
| `AidynDebug.z64` (02.01d-PRERELEASE) | n/a (blank header) | `8169959ae059869b46461712a829ca0fe15c902c` |

All three are big-endian `.z64`, 32 MB, with the retail CIC-6102 IPL3 and valid header checksums. Retail game code is `NAYE`. The debug ROM has no internal name or game code. How the versions differ is in [formats/rom.md](formats/rom.md).

## Emulator: ares v148

Installed with winget (`ares-emulator.ares`). Launch through the script, which enables the GDB server for that run only:

```powershell
tools/ares.ps1 debug      # or v10, v11; -SaveState 1..9 to load a slot
```

- The GDB server listens on port 9123. Note that it binds `0.0.0.0`, so it is reachable from the LAN while running.
- `Input/Defocus` is overridden to `Allow`. The user setting is `Pause`, which freezes emulation as soon as gdb takes focus. If the PC is stuck in IPL3 (`0xA40014xx`), this setting is the cause.
- The Expansion Pak is on (the ares default). This matches the set of map objects the reimplementation targets.
- **Pin ares at v148.** Save states are tied to the emulator version.

## Debugger: gdb-multiarch 18.1 (MSYS2 UCRT64)

Located at `C:\msys64\ucrt64\bin\gdb-multiarch.exe`.

```
gdb-multiarch -ex "set architecture mips:4300" -ex "set endian big" -ex "target remote 127.0.0.1:9123"
```

Resolve addresses against the project's symbol map, `tools/symbols/` (roadmap 0.8). A first attach to the debug ROM sampled `InitProc+0x1fc` and libultra thread code, which confirms the game is running.

## Engine: Godot 4.7.2 (.NET)

ADR 0002 has the Godot version pinned in the repository. `tools/godot.ps1` runs the pinned editor. On first use it downloads `Godot_v4.7.2-stable_mono_win64.zip` from the official GitHub release, checks it against the release's SHA-512 and unpacks it into the gitignored `.tools/godot/`. The editor runs self-contained (the `._sc_` file beside it), so its settings and caches stay in `.tools/` and don't touch another Godot install. The script runs the console build, so Godot's output and exit code reach the terminal.

```powershell
tools/godot.ps1 --path src/Aidyn.Inspector --editor   # the editor
tools/godot.ps1 --path src/Aidyn.Inspector            # run the project
tools/godot.ps1 -ExePath                              # print the executable's path
```

- **The pin is in two places:** the version and hash in `tools/godot.ps1`, and `Godot.NET.Sdk/4.7.2` in each Godot project's `.csproj`. Change them together.
- **Godot projects state `TargetFramework` themselves.** The editor doesn't see `Directory.Build.props`, and if the `.csproj` has no `TargetFramework` it writes in its own default, `net8.0`, which can't reference the net10 libraries. Godot 4.7.2 runs net10 assemblies without trouble.
- **No `.sln` is needed.** The editor builds the `.csproj`, and `dotnet build` builds Godot projects like any other, into `.godot/mono/temp/bin/`. The project's `.godot/` folder is a cache and is gitignored. The `.uid` files Godot writes beside scripts are committed.

## Ghidra 12.1.4 (roadmap 0.7, 0.14)

Ghidra analyses the original's code headless, and the project's scripts export what the rest of the rig reads. It's unpacked into the gitignored `tools/ghidra_12.1.4_PUBLIC/` (or `GHIDRA_HOME`) and runs on the JDK 21 already installed (Eclipse Adoptium). Everything it writes goes to `out/ghidra/`, because it's derived from the ROMs.

```powershell
tools/ghidra.ps1 import          # both ROMs, about a minute: import, analyse, then export
tools/ghidra.ps1 export          # after changing tools/symbols: re-apply names, re-write the index and pseudocode
tools/ghidra.ps1 run us Decompile.java 0x80078d14   # a script from tools/ghidra, on us or debug
```

- **The import.** The code segment is extracted to `out/ghidra/<rom>.bin` and loaded raw at `0x80000400` as `MIPS:BE:64:64-32addr` with the o32 compiler spec. Its length comes from the boot code at `0x80000408`, whose BSS clear starts where the loaded segment ends: 1.0 loads `0x80000400`–`0x800E6620` (ROM `0x1000`–`0xE7220`) with BSS to `0x800F0EA0`, and the debug build loads to `0x800F51A0` with BSS to `0x800FFA50`. `SetupN64.java` maps the BSS, low memory, the rest of RDRAM and the hardware registers so references to them resolve.
- **Finding functions.** Analysis follows calls from the entry point. `FindPointedFunctions.java` then makes a function wherever data or a `lui` pair points into undisassembled text (the vtables and callbacks, 105 in 1.0), and then from each remaining run that disassembles cleanly and returns, bookmarked as unreferenced (175 in 1.0: dead code the linker kept, or code only the debug build calls). 1.0 has 3,741 functions in a text that ends at `0x800CCEC0`, with data after it. The debug build has 3,889, with text ending at `0x800D6F80`.
- **What stays undisassembled** in 1.0's text is about 5,500 words: chiefly the RSP microcode at `0x80000470`–`0x80002700`, whose `jal`s go to the RSP's IMEM at `0x84001xxx`.
- **The export** writes `<rom>.index.json`, the code index, and `<rom>.c`, every function's pseudocode, after `ApplySymbols.java` names functions and globals from the symbol map. It takes about half a minute a ROM.
- **The pseudocode** is readable, and cheaper to research from than the disassembly. Since the CPU is modelled as 64-bit, ints often show as `longlong`, and an address above `0x80000000` that's built by arithmetic can show as negative (`-0x7ff1d0c8` is `0x800E2F38`). The index's references and `n64dis.py` have the real address. Check a constant or a branch that matters against the disassembly.
- **Ghidra misses many `lui` pairs,** a jump table's own address among them, so `ExportIndex.java` follows each `lui` through its function to the `addiu`, `ori`, load or store that completes it.

## Existing saves

Mupen64Plus saves are in `%APPDATA%\Mupen64Plus\save`. Only `AIDYN_CHRONICLES (unknown rom).mpk` (Oct 2025) holds Aidyn data. The other Aidyn `.mpk` files are empty paks.

Mupen `.mpk` files are four 32 KB paks back to back. The saves are in pak 1, in four 28-page notes (`NAYE`/`78`, "CHRONICLES" A–D). Their save headers decode as the table below ([saves](systems/saves.md)). Play time is `PlayTime` in 60 Hz ticks (`TICKSPERSECOND`, `world.h`), advanced by the frame delta. The frame delta counts 60 Hz polls and is capped at 1–6. The map position is the chunk the debug overlay shows as `mapShort1 + 'A'-1` followed by `mapShort2`.

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

These are gated on `gDebugFlag` (`0x800E68A0`), which is on in the debug build. The sources named are the decomp's, from before [ADR 0015](adr/0015-the-port-no-longer-reads-the-decomp.md).

| Input (overworld) | Effect | Source |
| --- | --- | --- |
| C-up | Cycle the performance overlay (6 states): memory bars, object count, ground type, chunk plus position, fps | `PlayerMove.cpp`, `app.cpp` |
| **L + Start** | **Open the big debug menu** | `gametrek.cpp` |
| R + A (minimap open) | Minimap shows everything | `gametrek.cpp` |
| L + D-left | Load only the centre map chunk | `PlayerMove.cpp` |
| R + D-right | Replace the next loaded scene objects with boulders (reference objects) | `PlayerMove.cpp` |

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

### Emulator captures

The user's screenshots of the original, for matching UI against. They live in `roms/` with the checkpoints, named for what they show, such as `menu-party-inventory-info.png` (the pause menu's Party tab with an item's details open). ares saves them at 2560 × 960, four times the 640 × 240 its video output gives (see [ui](systems/ui.md#the-screen)), so taking every fourth pixel both ways recovers that exactly; doubling its lines then shows it at 4:3.

### Reading the original's code: `tools/n64dis.py`

When a value or a branch matters, read the machine code. `n64dis.py` disassembles a function by its name in the symbol map (`tools/symbols/<rom>.txt`, roadmap 0.8) or by its address, naming the functions it calls and the floats it loads, and reads data as floats and doubles. Without a map it works by address alone. It needs `pip install capstone`.

```bash
python tools/n64dis.py --find wander                 # symbols whose names hold the text
python tools/n64dis.py 0x8000f5b4 --count 60         # from an address, in 1.0 (or --rom debug)
python tools/n64dis.py --data 0x800cdb80 --count 2   # words as hex, float and double
```

With the [Ghidra](#ghidra-1214-roadmap-07-014) export in `out/ghidra/`, it disassembles a whole function from any address inside it, and answers from the code index. A function is a symbol or any address inside it:

```bash
python tools/n64dis.py --callers 0x800788cc          # who calls it, and data words (vtables) pointing at it
python tools/n64dis.py --callees 0x80078d14          # what it calls
python tools/n64dis.py --refs 0x800e2f38..0x800e2f60 # who reads (r), writes (w) or takes (a) an address or range
python tools/n64dis.py --strings gamecombat          # strings holding the text, and who uses them
python tools/n64dis.py --c 0x80078d14                # Ghidra's pseudocode
```

Calls through a pointer (a vtable, a callback) aren't in `--callers`. Find those by the data word that points at the function, then by `--refs` on that word's table.

### The two builds and the source files: `tools/builds.py` (roadmap 0.9)

`tools/ghidra.ps1 export` runs `builds.py` between the index and the pseudocode. It writes `out/ghidra/builds.json`, which `n64dis.py` reads:

```bash
python tools/n64dis.py --other 0x800788cc            # the same function in the other build: 80081d2c in debug
python tools/n64dis.py --file turn.cpp               # the functions placed in a source file, with the lines they cite
```

- **The match.** Both builds come from one source, linked in one order. The debug build adds asserts and debug code. A function's shape is its opcodes without registers or immediates. Shapes unique to both builds anchor the match, and between anchors an alignment pairs the rest by similarity. Then matched callers vote on how their callees pair, which settles neighbours that look alike. It matches 3,715 of 1.0's 3,740 functions, and 9,206 of the 9,217 calls between matched functions land on the matched callee. The 25 left unmatched are mostly two-word stubs.
- **Names carry across.** A name in either build's map shows in both, in `n64dis.py` and in the pseudocode (`out/ghidra/<rom>.symbols.txt` is the merged map Ghidra applies). Where both maps name a matched pair differently, each build keeps its own, and `builds.py` reports the conflict.
- **Source files.** The debug build passes a file name and line to its allocator, its free and its asserts. 338 functions cite a file that way, or in a message such as `scene.cpp, SceneGetLocatorMtx()`, and 173 of them give their line. A run of functions between two in the same file is in that file too, since a file's functions are linked together. That places 889 of the debug build's functions in 115 files. 1.0 cites 32 files itself, and every one agrees with the file its matched function was placed in. Functions in a run between two different files aren't placed yet.
- **The disassembly's header** gives the function's source file, and its lines where it cites them.

1.0's functions sit in the same order as the debug build's, so find one in the other by its neighbours, or with `--other`. Check a float's bits, not just its look: `0x3E32B7FE` is 10° in radians and `0x3EB2B7FE` is 20°.

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

The C# side reads states with `AresCheckpoint` (`Aidyn.Rom.Rig`), which needs no ROM: it finds RDRAM by the flag table's header, which is in RAM exactly once, and whose pointers give its own address. The tests use it to check the [flag engine](systems/flags.md) against every checkpoint, and the editor loads a checkpoint's flags with it.

### Known issue: GDB after loading a state

When the debug ROM was booted with `--save-state`, gdb got `Ignoring packet error` on connect. After the failed handshake the GDB port stopped listening, and on one attempt ares stopped responding. A plain boot with no state attached fine earlier. The cause isn't isolated yet: it could be state loading, or the stub not recovering from a client that exits without detaching. Until it's fixed, use `bst.py` for checkpoint inspection and keep gdb for live sessions started from a cold boot.

## Finding the original's code

The project doesn't read the decomp ([ADR 0015](adr/0015-the-port-no-longer-reads-the-decomp.md)). A research run finds a system's code from what the project already knows and what the ROM and the checkpoints show, cheapest first:

1. **The docs.** Each system's doc cites the functions it ports with their 1.0 addresses, and the symbol map names them. Start from the nearest known function.
2. **Strings.** The function that loads a string is near the code that uses it. That includes the debug build's file names and assert messages, such as `GetAttackActor(), Attack type %i is not defined!`, and the ROM's own string tables, such as the combat strings.
3. **Data.** A table the importer reads has an address, and the code that indexes it is found by what references that address. The same goes for a global that a checkpoint shows changing.
4. **The call graph.** A known function's callers and callees, and the debug build's source files, whose functions sit together.
5. **The emulator.** Change a value in the game between two checkpoints and diff RAM. A watchpoint on that value then finds the code that writes it, and a trace finds what an action runs.

Steps 2–4 are `n64dis.py --strings`, `--refs`, `--callers` and `--callees`, over the code index, and `--c` reads a function's pseudocode once it's found. Roadmap tasks 0.9–0.12 add the source-file map, `bst.py --diff`, gdb on a checkpoint and traces. Until each lands, do its step by hand with `n64dis.py` and `bst.py`. Write what a run settles into the system's doc with its addresses, and its names into the symbol map.

### The symbol map (roadmap 0.8)

`tools/symbols/us.txt` (1.0) and `tools/symbols/debug.txt` hold our names for the original's functions and globals, one per line, as `name = 0xaddress; // where the name comes from`. `n64dis.py` prints them, and `tools/ghidra.ps1 export` applies them to the pseudocode. The map holds only addresses and names, so it's committed.

A name comes from the first of these that has one:

1. **The original's own name,** from a string in the ROM: an assert or log message such as `No spell list (Entity::GetSpell())` or `World::SetTerrain`. Written as the string has it, with `::`. Cite the string's address in the comment.
2. **The port's name:** the C# that ports the function or holds the value, dotted, as `Camping.AmbushRoll` or `Party.CampHeal`.
3. **A name we coin,** dotted in the same way, after the system it belongs to, for code not ported yet: `Combat.ApplyDamage`.

Library code takes the C library's or the N64 SDK's public names (`memcpy`, `sprintf`, `osRecvMesg`), recognised by what the function does.

A name that the docs or the code cite only as the decomp's, whether in a `<c>` tag or in prose written before [ADR 0015](adr/0015-the-port-no-longer-reads-the-decomp.md), never goes in the map, even where it's apt. Leave those citations where they are, as credit.
