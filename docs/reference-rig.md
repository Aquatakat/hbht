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
- **DB block:** every record is identical. The only change is new text: five Controller Pak error messages appended to `String/Common` and four to `String/Controller`. The two tables grew, which moves everything after them (see below).

So 1.1 contains code fixes and a few new messages, and all assets and records are shared. The decomp's `us.yaml` targets 1.0, which stays the primary target.

The debug ROM's `Borg_List` is at `0x1F98790` and its DB block starts at `0x1FD1730`.

### What `Aidyn.Rom` found (0.2, 0.3)

- **All three ROMs share one Borg archive.** The debug ROM's archive and `Borg_List` are byte-identical to 1.0's, as 1.1's are. There are 14,585 entries.
- **The `Borg_List` header** is the entry count followed by the archive's size in bytes (`0x1EA29F0`), which equals `Borg_List − archive start` in every version.
- **Stored sizes are padded to 8 bytes** with zeros, and entries start on 16-byte boundaries. An LZB stream therefore ends 0–7 bytes before its stored size.
- **Compression:** 14,047 Borgs are LZB and 538 are stored raw. None use LZ01x, which is only used for text. LZB is UCL's NRV2B with an 8-bit bit buffer. LZ01x is LZO1X from LZO 1.06.
- **1.1's text changed.** `String/Common` sits at 1.0's offset plus `0x40`, but it was edited and now decompresses to `0x357E` bytes instead of `0x340C`. Everything after it moves by `+0x270`. `String/Combat` and `String/Cheats` are unchanged apart from the move. `String/Controller` has changed too. The debug ROM's four `RomString` tables match 1.0's.

### What the DB readers found (0.4)

- **Layout.** `RomVersions` has every table in the DB block for all three ROMs. 1.1 is 1.0 at `+0x40` up to `String/Common`, which is `0x2960` bytes (1.0: `0x2830`). Copyright through the cinematic text are at `+0x170`. `String/Controller` is at `0x1FED6B0` and is `0x510` bytes (1.0: `0x410`), so everything after it is at `+0x270`. The debug ROM is 1.0 at `+0xEB80` throughout, except that its credits are `0x6F0` bytes, not `0x720`: three names are missing and one date differs. us.yaml has no ShopDB segment. It starts at `0x1FD31B0`, inside what the yaml calls ChestDB.
- **All records are identical in all three ROMs.** Only the text tables noted above differ.
- **The decomp's string decryption is slightly wrong.** `decrypt_sub_a` shifts the second seed right by 8, not 7, and the key setup mixes all 16 bytes, not 14. This was found by solving for the key from the six stat descriptions, whose plaintext is in the decomp. Every string in every table then decrypts to printable ASCII. The only markup is `\n` and `\xHH`, where `\x83` and `\x84` are controller button glyphs. The readers keep both as written.
- **Grouped tables.** EntityDB, WeaponDB, ArmorDB, SpellDB and ItemDB split their records into groups. Each group matches a field: entity category, weapon class, armor slot, spell school, and the item ID's type. EntityDB has 7 categories, one more than the decomp's `ENTITY_CATEGORY`, and category 4 is empty.
- **AidynEditor agrees with the decomp** on the field offsets of entities, weapons, gear and loot tables.
- **Shops.** The game stocks a priced slot only when its third byte is nonzero, and it always stocks one item. The first byte is never read. The three trailing slots are always stocked at base price.
- **Camp encounters** are indexed as `terrain × 5 + level tier`, with 54 terrains. Mean enemy level rises with the tier, which confirms the order. Their item IDs are big-endian, unlike the DB tables.
- **Odd values the game tolerates.** A few entities have an "is heavy" byte other than 0 or 1 (Alaron's is 220). The game only tests it for nonzero.

### What the texture decoders found (1A)

`aidyn textures <rom>` writes every Borg1 and Borg8 as PNG, with an `index.csv` of formats, sizes and flags.

- **Borg1 (2,183 textures)** is a 0x18-byte header, then every mip level back to back, then an RGBA16 palette for CI8 (256 entries) and CI4 (16). There are `lods + 1` levels. Each level halves both sides, and each row is padded to at least 8 bytes. All 2,183 match this layout exactly. No texture has its own display list, and every bitmap starts at 0x18.
- **Borg1 bitmaps are stored in TMEM order.** The game uploads each one with a single `LoadBlock`, so odd rows have each pair of 32-bit words swapped (64-bit words for RGBA32). Undoing the swap is what makes the first mip level match a downsample of the base level: 1,718 of the 1,723 mipmapped textures match better, and the other 5 are a single flat colour.
- **Borg1 formats:** CI8 1,769, RGBA16 137, CI4 135, IA8 62, IA16 56, RGBA32 12, I4 6, I8 5, IA4 1. The largest is 64×64, apart from one 32×128. Most are 32×32.
- **Borg1 flags:** 0x20 (unknown) is on 349 textures. 103 scroll (`0x200`), and all of those also have 0x100, which gives them a second, writable bitmap.
- **Some textures are upside down,** the digit glyphs among them. The models presumably flip them with their texture coordinates. Check this when Borg2 lands.
- **Borg8 (560 UI images)** is a 0x10-byte header, then the palette (CI only) and a linear bitmap, at offsets from the start of the data. The game draws them with `LoadTile`, so there's no TMEM swap. Formats: IA8 275, CI8 267, RGBA32 16, RGBA16 1, I4 1. The largest are the 320×240 menu backdrops.

### What the geometry reader found (1A)

- **Borg2 (5,636 models)** has a 0x50-byte header, followed by these parts in order:
  1. 8 bounding-box vertices per display list.
  2. The vertices.
  3. The display lists, back to back.
  4. A table of their offsets.
  5. A 0x14-byte material per display list: flags plus an RGBA float tint.

  Every file follows this layout, and every file ends exactly after the materials. The `unk0x3c` block the decomp fixes up never occurs. The game points segment 0xE at the bounding boxes and segment 0xA at the vertices, then runs one display list per Borg5 instruction. The texture is bound by the Borg5 scene, not the Borg2.
- **The display lists are F3DEX2** and use only six commands:
  - `G_VTX`, the vertex loads, which never go past buffer slot 31.
  - `G_TRI1`. There are 313,655 of these, but also 3,027 `G_TRI2`, so they don't only use TRI1.
  - `G_MODIFYVTX`, which only ever rewrites texture coordinates (`0x14`), and only in lit lists.
  - `G_CULLDL` against the list's own bounding box, always first.
  - `G_ENDDL`.

  In total there are 14,996 display lists and 319,709 triangles. 1,013 of the lists are empty, and 101 models have no lists at all. Every mesh lies inside its bounding box.
- **Material flag 0x8 means *unlit*.** The decomp names it "Lighting", but its code turns lighting on when the bit is clear, and the data agrees. In lists with the bit clear, 99.9% of vertices hold unit normals. In lists with it set, they hold colours. 9,833 lists are unlit (pre-lit vertex colours) and 5,163 are lit. On a contact sheet the lit ones look mostly like character body parts.
- **Material flags 0x1000 and 0x2000 wrap S and T, and 0x4000 and 0x8000 mirror them.** This is inferred from the data: with both wrap bits set, texture coordinates span many texture widths, and without them the coordinates stay within one. The decomp's tile setup reads these bits so that T always clamps. That can't be right for the tiled terrain, so it's probably a misread. Confirm this against the emulator when textured export lands.
- **Header:** the scale is 1/16 on 5,267 of the 5,636 models. The position and rotation are set on only a few. The word at 0x28 is 1 on 162 models, and the decomp's draw code skips their display lists. Vertex `flag` (0–3) is ignored by the RSP and its meaning is unknown.

### What the scene reader found (1A)

- **Borg5 (1,402 scenes)** has a 0x44-byte header, then 0x40-byte transforms, then these lists:
  - the transforms' child lists;
  - the archive indices of the scene's lights, models and textures;
  - a u16 instruction stream;
  - particle emitters, each a pointer plus 0xBC bytes.

  Every scene names a Borg3 camera. No scene has animated textures, which the game would crash on. 75 scenes have particle emitters (218 in total), and those aren't parsed yet.
- **Transform order:** transform 0 is the camera, which uses op 4 (look-at). Next comes one direction (op 3) per light except the last, then the model nodes, which all use op 1 (scale, rotate about a pivot, translate). Ops 0 and 2 are never used. The last light has no direction and is presumably ambient. In multi-light scenes it's the strictly darkest in 112, the first light is in 15, and the rest tie.
- **Hierarchy:** flag 0x100 marks exactly the transforms with no parent, every other transform has exactly one parent, and children always come after their parent. Flag 0x200 is a billboard (321 transforms).
- **Matrix conventions:** the game uses row vectors, like `System.Numerics`, and a child's world matrix is `local × parentWorld`. Rotations are in degrees and apply as roll about X, then pitch about Y, then heading about Z (libultra's `guRotateRPYF` order). The pivot ends up at the position. The per-axis scale that the game keeps in the fourth column is always 1 for op 1, so the composition reduces to the ordinary one.
- **Units:** the game draws at 16 times scene units and scales vertices by the Borg2's scale, so a vertex lands at `v × scale / 16` in scene units. At the usual scale of 1/16, 256 vertex units make one scene unit.
- **Instructions:** these are `LoadTexture` (0xFF unbinds), `LoadModel`, `Matrix` and `Draw`. The stream flattens to 16,597 draws, of which 15,230 are textured. Five scenes (5246, 13851, 13922, 13924 and 13928) draw display lists that their models don't have. The operands are 8-bit, so in the 4 scenes with 256 or more transforms, the transforms from 256 on can't be drawn.
- **Texture coordinates are 64 units per texel,** not 32. The game sets a texture scale of ½ (`gSPTexture 0x8000`). As a check, 71 textured billboards span their texture exactly once at 64 units per texel, and none do at 32. So UV = S / (64 × width), with T = 0 at the texture's first row.
- **Borg3 (5 cameras):** each is 45° wide at 4:3. Near and far are 0.1 and 1000, 0.01 and 1000, or 0.25 and 25 scene units. **Borg4 (507 lights)** is 1.0 followed by an RGB colour, and most are greys.

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
