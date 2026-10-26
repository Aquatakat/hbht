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

- The GDB server listens on port 9123. It binds `0.0.0.0`, so it is reachable from the LAN while running.
- `Input/Defocus` is overridden to `Allow`. The user setting is `Pause`, which freezes emulation as soon as gdb takes focus. If the PC is stuck in IPL3 (`0xA40014xx`), this setting is the cause.
- The Expansion Pak is on (the ares default), which is the set of map objects the port targets.
- **Pin ares at v148.** Save states are tied to the emulator version.

## Debugger: gdb-multiarch 18.1 (MSYS2 UCRT64)

Located at `C:\msys64\ucrt64\bin\gdb-multiarch.exe`.

```
gdb-multiarch -ex "set architecture mips:4300" -ex "set endian big" -ex "target remote 127.0.0.1:9123"
```

Resolve addresses against the project's symbol map, `tools/symbols/` (roadmap 0.8).

## Engine: Godot 4.7.2 (.NET)

ADR 0002 has the Godot version pinned in the repository. `tools/godot.ps1` runs the pinned editor. On first use it downloads `Godot_v4.7.2-stable_mono_win64.zip` from the official GitHub release, checks it against the release's SHA-512 and unpacks it into the gitignored `.tools/godot/`. The editor runs self-contained (the `._sc_` file beside it), so its settings and caches stay in `.tools/` and don't touch another Godot install. The script runs the console build, so Godot's output and exit code reach the terminal.

```powershell
tools/godot.ps1 --path src/HBHT.Editor --editor   # the editor
tools/godot.ps1 --path src/HBHT.Editor            # run the project
tools/godot.ps1 -ExePath                              # print the executable's path
```

- **The pin is in two places:** the version and the hashes of the editors and the export templates in `tools/godot-pin.ps1` (which `tools/godot.ps1` and the packaging script share, [releases](systems/releases.md)), and `Godot.NET.Sdk/4.7.2` in each Godot project's `.csproj`. Change them together.
- **Godot projects state `TargetFramework` themselves.** The editor doesn't see `Directory.Build.props`, and if the `.csproj` has no `TargetFramework` it writes in its own default, `net8.0`, which can't reference the net10 libraries. Godot 4.7.2 runs net10 assemblies without trouble.
- **No `.sln` is needed.** The editor builds the `.csproj`, and `dotnet build` builds Godot projects like any other, into `.godot/mono/temp/bin/`. The project's `.godot/` folder is a cache and is gitignored.

## Ghidra 12.1.4 (roadmap 0.7, 0.14)

Ghidra analyses the original's code headless, and the project's scripts export what the rest of the rig reads. It's unpacked into the gitignored `tools/ghidra_12.1.4_PUBLIC/` (or `GHIDRA_HOME`) and runs on the JDK 21 already installed (Eclipse Adoptium). Everything it writes goes to `out/ghidra/`, because it's derived from the ROMs.

```powershell
tools/ghidra.ps1 import          # both ROMs, about a minute: import, analyse, then export
tools/ghidra.ps1 export          # after changing tools/symbols: re-apply names, re-write the index and pseudocode
tools/ghidra.ps1 run us Decompile.java 0x80078d14   # a script from tools/ghidra, on us or debug
```

- **The import.** The code segment is extracted to `out/ghidra/<rom>.bin` and loaded raw at `0x80000400` as `MIPS:BE:64:64-32addr` with the o32 compiler spec. 1.0 loads `0x80000400`–`0x800E6620` (ROM `0x1000`–`0xE7220`) with BSS to `0x800F0EA0`; the debug build loads to `0x800F51A0` with BSS to `0x800FFA50`. `SetupN64.java` maps the BSS, low memory, the rest of RDRAM and the hardware registers so references to them resolve.
- **The text** ends at `0x800CCEC0` in 1.0 and `0x800D6F80` in the debug build, with data after it. Besides what calls reach, `FindPointedFunctions.java` makes a function wherever data or a `lui` pair points into undisassembled text (vtables and callbacks), and from each remaining run that disassembles cleanly and returns, bookmarked as unreferenced (dead code, or code only the debug build calls). What stays undisassembled in 1.0 is chiefly the RSP microcode at `0x80000470`–`0x80002700`.
- **The export** writes `<rom>.index.json`, the code index, and `<rom>.c`, every function's pseudocode, after `ApplySymbols.java` names functions and globals from the symbol map. It takes about half a minute a ROM.
- **The pseudocode** is cheaper to research from than the disassembly. Since the CPU is modelled as 64-bit, ints often show as `longlong`, and an address above `0x80000000` that's built by arithmetic can show as negative (`-0x7ff1d0c8` is `0x800E2F38`). The index's references and `n64dis.py` have the real address. Check a constant or a branch that matters against the disassembly.
- **Ghidra misses many `lui` pairs,** a jump table's own address among them, so `ExportIndex.java` follows each `lui` through its function to the `addiu`, `ori`, load or store that completes it.

## Existing saves

Mupen64Plus saves are in `%APPDATA%\Mupen64Plus\save`. Only `AIDYN_CHRONICLES (unknown rom).mpk` holds Aidyn data. Its four slots, all named "Alaron", are at the four story checkpoints' positions (below), and load on the debug ROM too. The playthrough used a hacked ROM with low-level enemies, so the party's stats, equipment and spells are below par for the story: good for flag, map and quest state, not for combat parity. The pak's layout is in [saves](systems/saves.md#the-originals-save), and `hbht save to-pak` writes a save of the port's into the pak beside a ROM, so the original can be loaded at any place, with any flags and party: that's how a check in [user-checks](user-checks.md) is set up without playing to it.

A Mupen `.mpk` is four 32 KB paks back to back. ares keeps one 32 KB Controller Pak beside the ROM as `<rom basename>.pak`, in the same raw layout, so converting means taking the first 32 KB:

```bash
head -c 32768 "AIDYN_CHRONICLES (unknown rom).mpk" > "roms/Aidyn Chronicles - The First Mage (U) (V1.0) [!].pak"
```

## Debug ROM controls

These are gated on `Debug.Enabled` (`0x800E68A0` in the debug build, <c>gDebugFlag</c>), which is on in that build.

| Input (overworld) | Effect |
| --- | --- |
| C-up | Cycle the performance overlay (6 states): memory bars, object count, ground type, chunk plus position, fps |
| **L + Start** | **Open the big debug menu** |
| R + A (minimap open) | Minimap shows everything |
| L + D-left | Load only the centre map chunk |
| R + D-right | Replace the next loaded scene objects with boulders (reference objects) |

The big debug menu's entries, labelled from the common strings:

| # | Entry |
| --- | --- |
| 0 | The event-flag viewer and editor |
| 2 | Cheats |
| 3 | Add a party member |
| 14 | Clear inventory |
| 15 | Play the credits |
| 16 | Unused debug toggle widget |
| 17 | Toggle enemies hostile |
| 18 | Toggle "only teleporters active" |
| 19 | Item DB browser: armor, shield, item, weapon, potion; A adds the item to the inventory |
| 20 | Bad-reference object hunt (voxel index display) |

Entries 1 and 4–13 are null in the function table.

## Checkpoint library

Save states are captured by the user playing, not scripted. They live in `roms/` and are named `<rom basename> - <label>.bsN`, made with ares v148. Each of the four story labels exists for 1.0 and for the debug build, with the [existing save](#existing-saves)'s slot at the same position:

| Label | Pak slot | Chunk |
| --- | --- | --- |
| Before Shamsuk's Tower | B | P17 |
| Before Rooughah | C | B01 |
| Before final encounter | A | M06 |
| Before Rooughah, in Terminor | D | G01 |

Load a checkpoint by label:

```powershell
tools/ares.ps1 debug -Checkpoint "Before final encounter"
```

**One battle, for combat** (1.0 only, labels `Combat 01, …` to `Combat 14, …`), against ten rats with Alaron, Brenna, Rheda and Sholeh: 01 on the field before it; 02–04 the first turns (Rheda's, Sholeh's, Alaron's); 05 Alaron choosing a melee target; 06 the attack, a rat's death; 07 Rheda choosing a spell's target; 08 her Endurance cast on herself; 09 reserves joining and moving; 10 a rat's attack on Brenna; 11 a rat fleeing; 12 victory; 13 the post-battle training; 14 the loot. [Combat](systems/combat.md) reads them.

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

Check a float's bits, not just its look: `0x3E32B7FE` is 10° in radians and `0x3EB2B7FE` is 20°.

### The two builds and the source files: `tools/builds.py` (roadmap 0.9)

`tools/ghidra.ps1 export` runs `builds.py` between the index and the pseudocode. It writes `out/ghidra/builds.json`, which `n64dis.py` reads:

```bash
python tools/n64dis.py --other 0x800788cc            # the same function in the other build: 80081d2c in debug
python tools/n64dis.py --file turn.cpp               # the functions placed in a source file, how, and the lines they cite
```

- **The match.** Both builds come from one source, linked in one order, and 1.0's functions sit in the same order as the debug build's. The debug build adds asserts and debug code. A function's shape is its opcodes without registers or immediates. Shapes unique to both builds anchor the match, between anchors an alignment pairs the rest by similarity, and matched callers vote on how their callees pair. The unmatched few are mostly two-word stubs.
- **Names carry across.** A name in either build's map shows in both, in `n64dis.py` and in the pseudocode (`out/ghidra/<rom>.symbols.txt` is the merged map Ghidra applies). Where both maps name a matched pair differently, each build keeps its own, and `builds.py` reports the conflict.
- **Source files.** The debug build passes a file name and line to its allocator, its free and its asserts, and some messages name a file, such as `scene.cpp, SceneGetLocatorMtx()` or `GetIdIndex (ids.cpp)`. Those functions anchor the placement of the rest.
- **Where each file's code starts.** Every file's code starts on a 16-byte boundary, and the functions inside a file abut. So a file whose code ends off a boundary leaves one to three nops of padding, and those mark a file's start for certain. `builds.py` finds them in the words, not only between the code index's functions, since a file can start with a function no call reaches (aiscores.cpp at debug `0x80064F60`). Taking both builds' padding together, the debug build splits into **units**: some hold the named files, many are the library's (libultra and the rest, which name no files), and some are game files the ROM never names. `builds.json` lists them.
- **How sure a placement is.** `n64dis.py --file` gives how each function was placed:

  | `how` | Means |
  | --- | --- |
  | `cites` | It names the file. |
  | `between` | It lies between two functions citing one file. |
  | `unit` | It's in the unit of one named file, with no 16-byte boundary in both builds between it and that file's citing functions, where another file could start. |
  | `probable` | The same, but such a boundary lies between, so an unnamed file could hide there. |
  | `boundary`, `rodata` | Where a unit holds several files: placed by the only start possible, or by its rodata, which each file links in the same order as its code. |

- **Two files are named `./src/audio.cpp`:** the game's sound, beside the dialogue code, and the audio driver (`AudioProcInit`, `dmaProc`), beside `dcm.cpp`. The second is `./src/audio.cpp (2)`.
- **`scenes.cpp` and `scene.cpp` are probably one file,** as nothing between them starts a file. `builds.py` reports this and leaves the four functions between them unplaced.
- **The disassembly's header** gives the function's source file, and its lines where it cites them. A function in a file the ROM doesn't name shows that file's extent, as `an unnamed file (80084ce0-80085c1c in debug)`.

### Reading state RAM offline: `tools/bst.py`

ares states store RDRAM uncompressed as 32-bit little-endian words. `bst.py` locates RDRAM by fingerprinting the code segment against the matching ROM, found by filename prefix. It then reads it as big-endian KSEG0 addresses:

```bash
python tools/bst.py "<state>" 0x800f1bbc 16     # hexdump
python tools/bst.py "<state>" --word 0x800f1bbc # one u32
python tools/bst.py "<state>" --dump out.bin    # 8 MB RDRAM image
python tools/bst.py "<state>" --diff "<other state>" --range 0x800e0000..0x80100000   # changed runs of words
python tools/bst.py "<state>" --diff "<other state>" --width 2 --was 25 --now 18      # values that went 25 -> 18
```

`--diff` (roadmap 0.10) compares two states of one ROM. Without a filter it lists the runs of changed words, the first 200 (`--max`). With `--was`, `--now` or both, it lists the values of `--width` bytes that changed that way. That's how to find where a value lives: note it in the game, change it (take a hit, spend gold), and diff the two states. Static addresses are labelled by the nearest symbol in the map. Write a structure's fields, once settled, into its system's doc, as [saves](systems/saves.md) does.

This is the preferred way to inspect checkpoints: it's deterministic, repeatable, and doesn't need a live emulator.

| Symbol | Debug | 1.0 |
| --- | --- | --- |
| `GameClock.WorldPointer` (<c>TerrainPointer</c>) | `0x800F1BBC` | `0x800E303C` |
| `GameClock.PlayTime`, the time played | `+0x34` | `+0x34` |
| `GameClock.Time`, the in-game clock (<c>InGameTime</c>) | `+0x20` | `+0x20` |

The C# side reads states with `AresCheckpoint` (`HBHT.Rom.Rig`), which needs no ROM: it finds RDRAM by the flag table's header, which is in RAM exactly once, and whose pointers give its own address. The tests use it to check the [flag engine](systems/flags.md) against every checkpoint, and the editor loads a checkpoint's flags with it.

### gdb on a checkpoint: `tools/gdb.ps1` (roadmap 0.11)

`tools/gdb.ps1` starts ares at a checkpoint (or a cold boot), lets the game run for `-Boot` seconds, attaches gdb in batch mode, runs the commands and detaches. gdb is killed after `-Timeout` seconds, and ares is closed afterwards unless `-Keep` is passed. Hardware watchpoints and breakpoints work, so "what writes this value?" takes one run:

```powershell
tools/gdb.ps1 -Checkpoint "Before Rooughah" -Commands 'set $t = *(unsigned int*)0x800e303c',
    'watch *(unsigned int*)($t+0x20)', 'continue', 'p/x $pc'
```

That one watches 1.0's in-game clock (`GameClock.Time`) and stops at `0x8007C000`, in the function at `0x8007BFF0` that clamps the clock at `0x67D21FFF` and that the lapse functions call. gdb stops just after the write, so the writing store is the instruction before the stop or, after a branch, its delay slot.

- **1.0's checkpoints attach, and so does a cold boot of either build. The debug build's checkpoints don't.** After loading one of them, ares runs the game and keeps its GDB port open, but its stub never answers gdb's first packet, not even with an ack, however long it waits. Research targets 1.0, so this rarely matters. For the debug build, use `bst.py`, or boot it cold.
- ares reports its architecture as `mips:4000`, which the script sets.
- The launcher resolves the ROM's path literally, since `[!]` in 1.0's filename is a wildcard to PowerShell.

### Traces: `tools/trace.ps1` (roadmap 0.12)

What code does an action run? `tools/trace.ps1` starts ares at a checkpoint, and gdb puts a temporary breakpoint on every function entry in the code index. Each one is removed by its first hit, so a function that runs every frame costs one stop and then nothing. A baseline window runs first (one beep), where you stand still or do whatever the action is compared against. Then the action window runs (two beeps), where you do the thing in ares, until three beeps. The report lists the functions first run in the action window, which the baseline never ran, in order, with their names and source files. The log, one line per first hit, is in `out/`. A trace is a quick way to find, and then name, the code behind a feature.

```powershell
tools/trace.ps1 -Checkpoint "Before Rooughah" -Baseline 20 -Action 30            # every function
tools/trace.ps1 -Checkpoint "<a battle>" -Files combat -Action 60                 # only those placed in combat's files
python tools/gdb/trace.py --report out/trace-<time>.log                           # the report again
```

- **Cost:** setting a breakpoint on every function takes about 20 s, and the game then runs at about half speed until the hot functions have used theirs up, within about 20 s of standing still in trek. `-Files` or `-Range` sets fewer breakpoints and keeps the game near full speed.
- **Input is the user's.** Scripting key presses into ares isn't worth it: taking the foreground needs a key first, and an Alt tap opens ares's menu bar. Do the action by hand at the two beeps.
- **How it's built:** `tools/gdb/trace.py` runs inside gdb, with gdb's own Python, and is driven through `tools/gdb.ps1 -Source`. A timer interrupts the target to close each window. gdb is given an empty stdin, because without a console (as in `trace.ps1`'s background job) it blocks before starting.

### Replaying the original's AI: `tools/aivectors/`

`tools/aivectors/aivectors.py` writes `tests/HBHT.Rom.Tests/CasterVectors.cs`, the table `CasterCheckpointTests` checks the port's casters and bosses against. It runs 1.0's own `ChooseSpellOrAttack`, `RunStyle` and `EnemyAI.Step` in a MIPS interpreter (`cpu.py`) over the RAM of the Combat 02 and 08–11 checkpoints, with each enemy caster built by the original's entity builder in a rat's place (`ovl.py`, `vec.py`, `vec2.py`). The cases are in `cases.py`, the one-off vectors (`ScoreSpellCells` by action slot, `SpellValue` by kind, `AICanCast`, the area danger, the morale spells) in `extras.py`, and `emit.py` writes the C# and a `vectors.json` with every case's full state.

```bash
pip install capstone numpy
python tools/aivectors/aivectors.py                  # every case, some minutes
python tools/aivectors/aivectors.py --only Lugash    # a few cases, to out/aivectors/vectors.json only
```

- **Inputs:** the 1.0 ROM in `roms/` (or `HBHT_ROMS`, or `--roms`), whose code is disassembled with capstone and cached in the gitignored `out/aivectors/insns.pkl`, and the checkpoints' RDRAM through `bst.py`'s `load_rdram`.
- **The output is checked in,** the inputs aren't. It is deterministic: running it again gives the same file. Run it only when the cases change or a port-side finding needs a new vector, and commit the new table with the change.
- **The interpreter** covers the integer and single/double float instructions these functions use, runs a function with `cpu.run(address, args)` and can replace one with a Python stub (`cpu.hooks`, in `sim.py`) for drawing, message queues and sounds. A case that hits an instruction it lacks stops with `unimpl`.

## Finding the original's code

The project doesn't read the decomp ([ADR 0015](adr/0015-the-port-no-longer-reads-the-decomp.md)). A research run finds a system's code from what the project already knows and what the ROM and the checkpoints show, cheapest first:

1. **The docs.** Each system's doc cites the functions it ports with their 1.0 addresses, and the symbol map names them. Start from the nearest known function.
2. **Strings.** The function that loads a string is near the code that uses it. That includes the debug build's file names and assert messages, such as `GetAttackActor(), Attack type %i is not defined!`, and the ROM's own string tables, such as the combat strings.
3. **Data.** A table the importer reads has an address, and the code that indexes it is found by what references that address. The same goes for a global that a checkpoint shows changing.
4. **The call graph.** A known function's callers and callees, and the debug build's source files, whose functions sit together.
5. **The emulator.** Change a value in the game between two checkpoints and diff RAM. A watchpoint on that value then finds the code that writes it, and a trace finds what an action runs.

Steps 2–4 are `n64dis.py --strings`, `--refs`, `--callers` and `--callees`, over the code index, and `--c` reads a function's pseudocode once it's found. Step 5 is `bst.py --diff`, `tools/gdb.ps1`'s watchpoints and `tools/trace.ps1`'s traces. Write what a run settles into the system's doc with its addresses, and its names into the symbol map.

### The symbol map (roadmap 0.8)

`tools/symbols/us.txt` (1.0) and `tools/symbols/debug.txt` hold our names for the original's functions and globals, one per line, as `name = 0xaddress; // where the name comes from`. `n64dis.py` prints them, and `tools/ghidra.ps1 export` applies them to the pseudocode. The map holds only addresses and names, so it's committed.

A name comes from the first of these that has one:

1. **The original's own name,** from a string in the ROM: an assert or log message such as `No spell list (Entity::GetSpell())` or `World::SetTerrain`. Written as the string has it, with `::`. Cite the string's address in the comment.
2. **The port's name:** the C# that ports the function or holds the value, dotted, as `Camping.AmbushRoll` or `Party.CampHeal`.
3. **A name we coin,** dotted in the same way, after the system it belongs to, for code not ported yet: `Combat.ApplyHit`.

Library code takes the C library's or the N64 SDK's public names (`memcpy`, `sprintf`, `osRecvMesg`), recognised by what the function does. In 1.0 the library is one run, `0x800B3B64`–`0x800CAE00`: libultra, libgu, libm, the printf family, the 64-bit divide helpers, the audio synthesizer and the Controller Pak code. The debug build has the same code at `+0x9F30`. The game's own string and memory functions (`memset`, `strcpy`, `strcmp`) sit apart, at `0x8008EF30`–`0x8008F644`.

A name that the docs or the code cite only as the decomp's, whether in a `<c>` tag or in prose written before [ADR 0015](adr/0015-the-port-no-longer-reads-the-decomp.md), never goes in the map, even where it's apt. Leave those citations where they are, as credit.
