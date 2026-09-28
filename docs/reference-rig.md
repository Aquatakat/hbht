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
- **Borg1 flags:** 0x20 is on exactly the 407 textures with transparent texels. The game's render modes blend IA16, IA8 and RGBA32 textures with it and alpha-test the rest. 103 scroll (`0x200`), and all of those also have 0x100, which gives them a second, writable bitmap.
- **Some textures are upside down,** the digit glyphs among them. T = 0 is the first row, and the models' texture coordinates are exported as they are, so any flip is the model's own.
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
  - `G_MODIFYVTX`, which only ever rewrites texture coordinates (`0x14`), and only in lit lists. The RSP applies the texture scale as it loads vertices, and `G_MODIFYVTX` writes over the already-scaled values, so the edited coordinates are in half the units of the loaded ones. The data confirms this: 17,296 edited coordinates equal half the original, shifted by whole textures, against 369 that equal the original shifted, which is about chance. Reading them unscaled put Brenna's cuff pattern up her bare arms and misplaced her hood trim.
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
  - particle emitters, 0xC0 bytes each.

  Every scene names a Borg3 camera. No scene has animated textures, which the game would crash on. 75 scenes have particle emitters (218 in total); see the 1B notes.
- **Transform order:** transform 0 is the camera, which uses op 4 (look-at). Next comes one direction (op 3) per light except the last, then the model nodes, which all use op 1 (scale, rotate about a pivot, translate). Ops 0 and 2 are never used. The last light has no direction and is presumably ambient. In multi-light scenes it's the strictly darkest in 112, the first light is in 15, and the rest tie.
- **Hierarchy:** flag 0x100 marks exactly the transforms with no parent, every other transform has exactly one parent, and children always come after their parent. Flag 0x200 is a billboard (321 transforms).
- **Matrix conventions:** the game uses row vectors, like `System.Numerics`, and a child's world matrix is `local × parentWorld`. Rotations are in degrees and apply as roll about X by `rot.y`, then pitch about Y by `rot.x`, then heading about Z by `rot.z` (libultra's `guRotateRPYF` order). The scenes are Y-up, so `rot.x` turns a model about the vertical. The decomp's `Borg5Transform_op1` has X and Y the other way round. Its machine code (`0x8009E4C4` on the debug ROM, the same instructions in 1.0) takes its roll from offset 0x14 and its pitch from 0x10, as the camera and lights (ops 3 and 4) do. Static scenes didn't show the mistake: the Gwernia house has no rotations, and Brenna was only checked for texture placement. Animated ones did, when the user reviewed them: the opening's doors spun into the floor and Gabrion walked upside down. The pivot ends up at the position. The per-axis scale that the game keeps in the fourth column is always 1 for op 1, so the composition reduces to the ordinary one.
- **Units:** the game draws at 16 times scene units and scales vertices by the Borg2's scale, so a vertex lands at `v × scale / 16` in scene units. At the usual scale of 1/16, 256 vertex units make one scene unit.
- **Instructions:** these are `LoadTexture` (0xFF unbinds), `LoadModel`, `Matrix` and `Draw`. The stream flattens to 16,597 draws, of which 15,230 are textured. Five scenes (5246, 13851, 13922, 13924 and 13928) draw display lists that their models don't have. The operands are 8-bit, so in the 4 scenes with 256 or more transforms, the transforms from 256 on can't be drawn.
- **Texture coordinates are 64 units per texel,** not 32. The game sets a texture scale of ½ (`gSPTexture 0x8000`). As a check, 71 textured billboards span their texture exactly once at 64 units per texel, and none do at 32. So UV = S / (64 × width), with T = 0 at the texture's first row.
- **Borg3 (5 cameras):** each is 45° wide at 4:3. Near and far are 0.1 and 1000, 0.01 and 1000, or 0.25 and 25 scene units. **Borg4 (507 lights)** is 1.0 followed by an RGB colour, and most are greys.

### glTF export (1A)

`aidyn scenes <rom> [<index>...]` writes each Borg5 as `out/<version>/scenes/<index>.glb`. All 1,402 pass the Khronos glTF validator with no errors or warnings. The only notes are degenerate triangles, which are in the game's data. Units are scene units, and these look like metres: the character in scene 10696 is 1.84 tall, and the house in scene 2558 is 9.4 × 8.5 × 19.8.

- **Nodes:** there's one node per model transform, using translation, rotation and scale. Billboards draw on a separate root node that holds only their scale and world position, facing the way they were modelled. The camera is a glTF perspective camera, and each directional light uses `KHR_lights_punctual`. The ambient light goes in the scene's `extras`.
- **Meshes:** each draw becomes a primitive. The draws the game skips are left out: hidden models, fully transparent ones, empty display lists and missing ones. So are draws through non-op-1 transforms, which the game squashes onto a line. Positions have the Borg2 scale baked in.
- **Materials** follow the game's combiners:
  - Textured draws are texture × shade (the vertex colour or the lighting), with alpha = texture alpha × (1 − tint alpha). The tint's colour isn't used.
  - Untextured draws are shade × tint colour.
  - Unlit display lists use `KHR_materials_unlit` with `COLOR_0`. Lit ones have normals.
  - The wrap and mirror flags map to sampler wrap modes, and flag 0x80 maps to nearest filtering.
  - Culling maps to `doubleSided`.
  - Environment-mapped draws lose their texture, because glTF can't generate texture coordinates.
- **Viewer:** `tools/scene-viewer.html` is a three.js page. Serve the repo root on localhost (the `scene-viewer` entry in `.claude/launch.json`, or `python -m http.server 8123 --bind 127.0.0.1`) and open `/tools/scene-viewer.html?scene=2558`. The **Scene camera** option views through the Borg3 at 4:3. The scenes are ROM-derived, so keep them local.
- **Checked against the emulator:** the user captured frames of the Gwernia house (scene 2558) and of Brenna (scene 10696, in combat). The house matched. Brenna's texture placement matched once `G_MODIFYVTX` was fixed. Neither tested rotations (see the matrix conventions above).
- **Left for the renderer.** These points are from the user's review:
  - **Lighting:** the viewer's lighting looks like sunny daytime in-game. Real scene lighting varies with time of day and weather. The decomp's draw code seems to ignore Borg4 lights in favour of a fixed white light, which is still unresolved.
  - **Billboards:** the user doesn't recall any that visibly turn to follow the camera, except maybe a bush in later areas. This can wait until one turns up.
  - **Texture filtering:** probably not worth emulating. Aidyn doesn't use the N64's three-point filtering for artistic effect, and its textures, especially the faces, have far more texels than the screen shows.

### What the animation reader found (1B)

- **Borg6 (1,988 animations)** animate the transforms of 259 Borg5 scenes. The file is a 0x10-byte header (the scene, the target count and offset, and the length in ticks), then 0x18-byte targets, 0x10-byte tracks, 8-byte keys and their floats, at offsets from the start of the data. Files are padded to 8 bytes.
- **A target is one transform,** with a 9-bit mask of the channels it drives: rotation XYZ, then position XYZ, then scale XYZ, one track per set bit. There are 29,473 targets and 84,958 tracks. Most drive a model node. 32 drive the camera and 35 turn a light, all in cinematics. No animation drives a transform twice.
- **Tracks are polynomials stepped by forward differences.** A track's state is a value and its first three differences. Each 60 Hz tick adds each difference to the one before, as far as the current key's mask reaches: mask 1 holds, 2 and 3 step linearly, 6 and 7 quadratically, 14 and 15 cubically. The first key is always at tick 1 and sets a value (mask 15, 3, 1 or 7). Later keys mostly load only the differences (mask 14 or 2: 638,243 of them), so the curve carries on from its value. Only 1,286 later keys (mask 15) jump. Each key list ends with a marker at the animation's length, which the game never loads.
- **Timing** (`Borg6Player`, from `anim.cpp`): a track's counter starts at 1. On a tick where it equals the next key's tick, that key loads, then the state steps and the counter goes up. When the counter reaches the length, the track reloads its first key instead, without stepping. So the scene clock `aniTime` starts at 1, and at time *t* each value is its curve at *t*. The animation loops every `Length` ticks, which the tests confirm bit for bit for all 1,988.
- **Driven channels are replaced, not offset.** Each tick the game zeroes a driven channel, then adds the value times the animation's weight, which is always 1. The differences are also added into four vectors per transform that nothing reads.
- **What the game supports and the data never uses:** target types 0, 2 and 3 (fields of other Borgs, such as a Borg2 material's tint), playback modes other than looping, tracks shorter than their animation, and blending several animations by weight.
- **102 animations have no targets.** They hold their scene still for their length.
- **glTF export:** `aidyn scenes` adds every Borg6 of a scene to its `.glb` as a glTF animation named `borg6 <index>`. Each is baked at one sample per tick, from time 1 to time `Length + 1`, which is time 1 again, so a clip lasts exactly one loop. Linear interpolation matches the game at every tick. Driven model nodes get translation, rotation and scale, because the pivot makes the translation depend on the rotation. Billboards below a driven node get their world position, the camera gets its look-at, and lights get their direction. The animations with no targets can't be glTF animations, so they're listed in the scene's `extras` as `stillAnimations`. All 1,402 files still pass the validator.
- **Viewer:** `tools/scene-viewer.html` plays a scene's animations: pick one, play or pause with the button or Space, scrub by tick, or step with `,` and `.`. `&anim=<borg6>` in the URL starts one. With **Scene camera**, cinematic shots play through their animated camera, for example `?scene=8728&anim=8729&camera=1`, the first shot of the opening. **Root in place** draws transform 2 with its position zeroed, which is how the game draws actors (`Borg7_Render`), moving them by the root's motion instead.

### What the actor reader found (1B)

- **Borg7 (203 actors)** is a 0xC-byte header (the root transform, the state count and the initial state), then 0xC-byte states (a Borg6, a u16 of flags, and the count and offset of the branches), then 4-byte branches (request, chance, flags, target state). Some files are padded to 8 bytes with leftover text. Every actor's root transform is 2 and every actor starts in state 0. All of an actor's animations animate one scene. There are 1,932 states and 9,227 branches, and actors use 1,923 of the 1,988 Borg6s.
- **Requests.** Each frame, the game asks each actor for an animation by number (`Borg7_SetAnimation`). The decomp's names include 0 idle, 2 walk, 3 run, 7 hit, 9 dying and 25 sneak. The branches use 0–22, 25 and 255, which means any request. A state's flag 0x1 marks the initial state, in all but two actors (7613 and 7620). The game doesn't read it, and uses bit 0x2 of the same field at run time.
- **Branching** (`Borg7_TickAnimation`). When the scene clock reaches the animation's length, the game rolls 0–99. It walks the branches in order, and for each whose request is the current one or any, it subtracts the chance. It takes the first branch at which the roll drops below 1, so a chance of 10 after the roll is 11 in 100. With no branch taken, the animation restarts. When the request changes, branches with flag 0x1 that match it are rolled for at once, and the animation doesn't advance that tick. 3,755 branches interrupt like this. Actor animations use their own random streams (`gAniRandSeed`, and `gBorg7Rand` in a variant that ticks once).
- **Brenna's actor (10723) reads as expected.** Idle (state 0) repeats 90% of the time and fidgets 10% of the time. Walk or sneak starts a walk (1), which then alternates two steps (2 and 3) and ends in a stop (4 or 5) when walking isn't requested. Run goes to state 6. Requests 5, 6, 12, 13 and 15 interrupt from 18 of her 25 states, and the rest, including hit (7) and dying (9), interrupt only from idle and a few others.
- **Root motion.** The game draws the actor with transform 2's position zeroed (`Borg7_Render`), and moves the actor by how far that position moved each tick. It skips the tick a branch is taken or an animation restarts, and the two ticks after. The walk steps are cut from one continuous walk: Brenna's root goes 0 → 0.32 through the start, 0.38 → 1.06 through one step and 1.12 → 1.80 through the other. Skipping those ticks hides the jump back between steps. That comes to about 1.9 scene units a second.
- **Entities' actors** (`EntityModels`, from `gEntityExtras` and `dailougEnt_info_array`). Each entry is keyed by the record's position in the code's entity or dialogue entity list, plus 1. Both lists are in the same order as the DB tables. Keying by the record's ID instead would give Alaron Brenna's actor. All 221 entities and 32 of the 37 dialogue entities have an actor, a portrait, a height and a scale. Entities also have a collision radius and an unknown float. 104 actors are used this way, and the other 99 belong to scenery, spell effects, the pause menu and the like. The tables and the dialogue entity list are identical in all three ROMs.
- **`aidyn actors <rom>`** writes every actor's scene, states, branches and users to `out/<version>/actors/actors.json`. The scene viewer then shows an actor panel for scenes that actors use, with each state's branches. Interrupting branches are highlighted, and clicking a state plays it. **Drive** runs the state machine for a chosen request, with root motion, so `?scene=10696` with Drive on and walk requested shows Brenna start, step and stop.

### What the particle reader found (1B)

- **Emitters** (`Borg5ParticleEmitter`) are 0xC0-byte records at the end of a Borg5, run by `particles.cpp`. An emitter waits its delay, spawns a burst, then spawns one particle every *interval* ticks for its lifetime, or several per tick if the interval is negative. Each particle is a textured square that lives a set number of ticks. It starts at the emitter's locator, jittered by a random vector, with a random velocity plus a speed along a direction or the locator's normal, and a random scale and colour from two ranges. An acceleration (XYZ times W, then × 0.006) is added to every particle's velocity each tick. Optional flags let the acceleration change over time, make particles head for a target locator, fade to a colour, scale to an end size, or bounce at height 0. The data never moves an emitter or gives it a second acceleration, though the code can. Flag 0x40 is set on two emitters, and nothing is known to read it.
- **Starting.** 216 of the 218 emitters start when an actor enters a state for their request (`Borg7_StartParticles`), mostly request 5 (181 emitters), which combat code sends to spell and attack effect actors to start them. Nothing starts the other two. The game's particle random stream is seeded from the clock, so no two runs match. The simulation is left for the renderer (M3). The notes above and the field docs in `Borg5ParticleEmitter` are enough to port it.
- **Locators.** A Borg2 whose header word at 0x24 is 0–7 is a locator; the other 5,279 have 0xFFFFFFFF. A scene's locator *i* is the first of its models marked *i*, at the model's position, drawn with the transform the game last sets while that model is loaded. 183 emitters sit on their own scene's locators. 34 sit on another actor's scene, attached at run time (locator bits 7–8): a spell effect attaches two actors, presumably the caster and the target (`spellvisuals.cpp`), and an attack effect attaches the actor it hits (`CombatAttackVisuals.cpp`). Scene 10015's one emitter (Bowden's actor) names a locator the scene doesn't have. The game would crash if it started, but it has no request flag, so nothing starts it.
- **Textures** come from the engine's particle list (`partTextInds`, 1.0 `0xCE460`), not from the scene. It has 17 slots, which hold Borg1s 0–12 or are empty.
- **Chests mostly face one way.** Of the 179 chests with a collision box within reach, 134 aren't turned at all and 45 are, such as the one beside the player at the start of the game (I08, turned 36°). The collision was baked from the same placements, and the boxes line up face for face with the models as the game rotates them. That includes all 11 chests that are knocked over, which settles the rotation's axis order without the machine code: Borg5's order gets 9 of them wrong. One box is 2° off its model, and 12 chests stand against other walls, so the check can't tell for those.
- **Reference points** mostly sit in clusters, which the user recognised as where cut-scenes put their cameras.
- **Export and viewer.** Every emitter's fields go in the scene's `extras` as `particleEmitters`. Those on the scene's own locators also become child nodes of the locator's transform, so they follow its animation, and the viewer marks them in their particles' brightest colour. Oriana's (scene 11211, request 5) is on the orb in her hand.

### 1B review and leftovers

The user reviewed the scene viewer against the game on Sep 27, 2026. All 13 shots of the opening cinematic play correctly, including the camera, and Brenna's animation set is exactly as in the game. Oriana's actor has animations that look like the ending's, or like ones that were cut. They're in her data, so they're kept. Left for the renderer:

- **Lighting**, as in 1A. The cinematics look flat without it.
- **Animated textures.** No Borg5 uses the scene-level kind, which the game would crash on. The effects the user remembers are probably the 103 Borg1s that scroll (flags `0x200` and `0x100`), which the export doesn't animate yet.
- **Some odd rendering** the user noticed in the cinematics. It isn't pinned down yet.
- **The particle simulation** (see the particle notes above), and each cinematic shot's near and far planes, which the cinematic table overrides and the viewer ignores.

### What the dialogue reader found (1E)

`aidyn dialogue <rom> [<index>...]` writes each Borg13 as text to `out/<version>/dialogue/`, with speakers named from the DB.

- **Borg13 (892 scripts)** is a 0x18-byte header, then 0x38-byte nodes, then 8-byte actor entries, then plain ASCII text padded to 8 bytes. The text isn't encrypted, unlike the DB strings. It keeps `\n` and `\xHH` escapes, and a run of 20 `#` stands for the player's name. 513 scripts carry leftover strings that no node uses. 153 scripts end with a blank node that nothing links to.
- **Header:** `ID` is the script's number in the original tools (1004–13147), not its archive index. `flag` is set when a cut-scene, backdrop or help script finishes, but not after a conversation or sign. `start_func` is the presentation: 596 conversations, 211 cut-scenes, 78 backdrops (text over a picture, such as a book), 6 signposts and the help menu. `libraryType` picks the backdrop's picture and is 14 everywhere else. Op 19 duplicates it in each backdrop's first node, and the game never reads op 19.
- **Nodes** have four ops (a u16 operand, a command byte, then six zero bytes), a text offset and length, a `[a, b]` range, a first link, seven more links, the node's own index and a flags byte. Links end at the first 0xFF. The flags byte gives the kind:
  - **0x1, line** (24,955): shows its text, then follows its one link. With no link it goes back to the last menu or case.
  - **0x2, if** (937): evaluates a condition (party member, flag or item) and follows the first link if it's nonzero, else the second.
  - **0x4, case** (118): evaluates a value (skill, party size or gold) and considers the links whose `[a, b]` holds it. 65000 means no upper bound. One match is followed, and several are offered as a menu.
  - **Otherwise, menu** (674): offers its links as choices. Choices already taken are hidden, except in backdrops and the help menu.
  - 0x40 is on most menu choices and is never read. 0x80 is the run-time "visited" bit.
- **Ops run in three passes:** every setup op (speaker, cameras, actors, enemies) when the node is reached, the first condition op when branching, and **only the first effect op** when a line is left, or at once for menus and cases. The data agrees: only 4 of 26,684 nodes have a second effect. One of them, node 88 of script 1019, is a `SetFlag` that never runs.
- **Quirks:** script 1294 has 0xCCCC, the tools' fill value, as an actor and a speaker. Ops 1 and 2 are never read by the game.

### What the map object reader found (1E)

1E reads only the map objects (voxels) of each Borg9, since they're what gate on flags and start dialogues. The rest of Borg9 is 1C's.

- **Map chunk lists** are 17 zero-terminated lists of 12-byte entries (row, column, scene, alternate scene, Borg9, terrain), back to back in the code segment. They're at `0xDBD90` in 1.0, `0xDBDD0` in 1.1 and `0xEA850` in the debug ROM, identical in all three: 543 chunks. 57 Borg9s aren't in any list.
- **Map objects** are 0x6C bytes: a 0x28-byte header (position, radius, a run-time time stamp, flags, type, two links, flags A, B and C, and a run-time pointer), then 0x44 bytes that depend on the type. The Borg9 header's count and pointer at 0x28 and 0x44 find them, and the collision sections start right after, 8-byte aligned. There are 26,091 across 584 Borg9s.
- **The three flag slots.** A flag check is the flag's value XOR an invert bit, where flag 0 is always false and flag 1 always true, so an inverted 0 means "always".
  - **C** (inverted by bit 0x80) says whether a scene, light, sound, teleporter or trigger is there at all.
  - **A** (inverted by 0x100) says whether a teleporter, trigger or dialogue works, and whether a monster party is there.
  - **B** is written when the object is used: set, or cleared if bit 0x200 is on. A monster party without a dialogue is gone while its B is set, and winning its fight sets it. With a dialogue, talking to it sets B. A dialogue object's B always equals its script's done flag (273 of them; 91 are 0) and the game doesn't read it.
- **Type-specific flags:** containers have an "opened" and an "exploded" flag; teleporters have lock, unlock, trap, disarm and secret-door flags.
- **Dead writes:** the game only writes plain flags from 2 up. Two SetFlag triggers name derived flag 5495 and flag 0, and six monster parties name derived flags as their B.

### What the map reader found (1C)

`Borg9Map` reads the rest of each Borg9: its collision and the grid that indexes it.

- **Layout.** A 0x50-byte header, then six parts, each 8-byte aligned: the cells' triangle indices, their object indices, the vertices, the triangles, the map objects and the cells. The header holds the counts of each and their offsets from the start of the data. Two of its offsets (0x30 and 0x40) are fixed up by the game and never used, and hold leftovers. All 584 Borg9s match this exactly. 24–48 bytes follow the cells, all zeros except in one file.
- **Triangles** (168,317) are 0x20 bytes: three vertices as byte offsets into the vertex list, a unit normal, a surface, flags and a ground type. They wind counter-clockwise about their normals, except for 27 with no area and 19 slivers.
- **Surfaces** are the 26 physics properties the game names in `String/Common`: Default, Collision Player And Camera, Collision Player, then footstep materials such as Grass, Stone and Water, and "Trans" versions of each. The data uses 19 of them. Most walls and floors are *Player And Camera* (101,394). *Player* (10,345) is for invisible walls: all of them, and only them, have flag 0x200, which line-of-sight tests skip, so the camera passes through. Whatever touches those two surfaces keeps its speed and slides along; every other surface stops it (`EnvPropBools`). Water has a speed property that halves speed and nearly cancels gravity, but no triangle has flag 0x2, which applies it.
- **Flags** say which edges and corners collide, besides the face. The decomp's edge bits are right: the data confirms it, since every edge no other triangle shares has its bit (0x4 is A→B, 0x10 is B→C and 0x8 is C→A). No triangle is passable (0x1) as stored.
- **Ground types.** The top four bits give a triangle's role, and the game rewrites the rest when it loads the chunk:
  - 160,474 are ordinary.
  - 1,849 are **triggers**: the low bits index a map object, which runs when the player walks on the triangle. 1,532 name cameras and 317 name dialogues.
  - 62 are **toggles**, with an id one more than the low bits. A `ChangeCollision` trigger that names the id makes them passable, so the way opens. Every toggle id has its trigger in the same Borg9, and every such trigger has its triangles.
  - 5,932 are **Expansion Pak only**. Without the pak, the game makes them passable.
- **The grid** starts at the collision's minimum corner, with cells about 5 units square. Each cell lists the triangles near it, and the objects the player could use from it or that it draws: scenes, containers, lights, teleporters, cameras, dialogues, triggers and the three save points. Sounds, wander nodes, monster parties and reference points are handled per chunk. 26 triangles, in 8 Borg9s, are in no cell, so nothing collides with them. Chaos Isle's A09 (Borg9 5019) has a 1 × 2 × 1 box of 8 triangles about 297,000 units away. It stretches that chunk's grid to cells 12,383 units wide, so the rest of the chunk sits in one cell. It still works, but every collision test there checks every triangle.
- **Map objects, the rest of the types.** Scene objects place 1 or 2 levels of detail of a Borg5 (7,913), a Borg7 actor (163), or a container's model followed by its opened and destroyed models (371). They scale, then rotate, then move, and rotate by `guRotateRPYF` with roll = `rot.x`, pitch = `rot.y` and heading = `rot.z`, in radians. That's not the Borg5 transforms' order (they swap X and Y); the machine code confirms it (`Scene::MatrixARotate` passes its first two angles to `guRotateRPYF` the other way round, and the call site in `RenderVoxelScenes` passes `rot.y, rot.x, rot.z`). Lights hold two colours and a way of moving between them (static, alternate, sine, random). Sounds play a Borg12 with a volume and a random wait. Wander nodes form chains, each naming the next two. Cameras move the camera or its aim to a reference point (or above the player) by cutting, panning or easing. The code objects and save points hold nothing.
- **Chunks in a set.** A chunk's own coordinates start at (row × cell size, 0, column × cell size): rows run along X. The cell size is a literal in `BorgMaps::LoadMap`, checked in 1.0's code: 100, except 500 for the misc interiors, 30 for Erromon, 200 for Terminor, 25 for the Barrows, 1000 for Ehud's tower and 150 for the battlefields. Neighbouring chunks' collision overlaps by 10–30 units, and a test confirms the placement: where it overlaps, 95% of the overworld's ground heights agree, against 3% with rows along Z.
- **A chunk's second scene** is drawn after everything else (`renderTransZones_`), so it holds see-through surfaces such as water. It isn't an alternative to the first.
- **Chests mostly face one way.** Of the 179 chests with a collision box within reach, 134 aren't turned at all and 45 are, such as the one beside the player at the start of the game (I08, turned 36°). The collision was baked from the same placements, and the boxes line up face for face with the models as the game rotates them. That includes all 11 chests that are knocked over, which settles the rotation's axis order without the machine code: Borg5's order gets 9 of them wrong. One box is 2° off its model, and 12 chests stand against other walls, so the check can't tell for those.
- **Reference points** mostly sit in clusters, which the user recognised as where cut-scenes put their cameras.
- **Export and viewer.** `aidyn maps <rom> [<set>...]` writes each map set to `out/<version>/maps/`: `<set>.json` holds its chunks and every map object with its decoded fields, and `<set>.glb` its collision, a node per chunk at its origin. Each collision vertex carries its triangle's surface, role and flags as the attributes `_SURFACE`, `_ROLE` and `_FLAGS`. All 17 files pass the glTF validator. To browse a set, run `aidyn scenes` as well, serve the repo root as for the scene viewer, and open `/tools/map-viewer.html?v=na10&set=Overworld` (add `&chunk=I08` to start at a chunk). The user checked it against the game on Sep 27, 2026: the layout, terrain, objects and enemy spawns are where they remember. It draws each chunk's scenes, the scenery that scene objects place (the nearest level of detail, instanced), the collision coloured by surface or role, and the other objects as dots by type. Clicking a dot shows the object's fields, its radius and its links, and hovering over collision names the chunk, surface and role. **Expansion Pak** switches between the objects and triangles for playing with and without it. The overworld takes about 15 seconds to load.

### The quest atlas (1E)

`aidyn atlas <rom>` writes `out/<version>/atlas/atlas.json`, and `aidyn flag <rom> <flag>...` prints what sets and reads a flag, with the nearest dialogue line and where each script is started. To browse it, serve the repo root as for the scene viewer and open `/tools/quest-atlas.html?v=na10`. It searches flags, scripts and text, shows each flag's setters and readers and each script's nodes, and draws what leads to an item (upstream) or what it leads to (downstream). For example, `#flag/285` shows that the lighthouse key comes from the keeper's conversation, and that the keeper only appears once derived flag 5349 holds. The atlas cross-references the dialogue ops, script done flags, map objects, the flag network and the flags engine code reads and writes (1H). Engine code also names many flags, such as "Brenna is in the party", and the viewer shows and searches these names.

- **The flag network:** derived flags are evaluated in index order until nothing changes. Logic flags are AND, OR, exactly-one (EOR), none (NOT) or "inputs as bits equal the operand" (MSK). Value flags compare the inputs as bits, and count flags compare how many are set, using ==, >, < or !=. Inputs of 0xFFFF are skipped. Evaluating the network from each checkpoint's plain flags reproduces every derived flag in its RAM, in all four checkpoints.
- **The journal:** entry *i* has flag 5556 + *i*, which is "entry *i*'s flag is set". When a flag from 5556 up turns on, the game raises flag 3617 and shows the new-entry notice.
- **Coverage, checked against the checkpoints:** 2,031 plain flags have a setter in the data, and 140 are read but never set by data. Of the roughly 560 flags set in the late checkpoints, only 49 have no known setter. They were 3, 154, 456, 458, 533, 566, 686, 689, 692, 697, 788–805, 955–958, 960–963, 3404, 3426, 3428, 3432, 3611, 3612 and 3733–3739. 1H found engine code that sets all but 955–958 and 960–963 (see [hardcoded-content.md](hardcoded-content.md)).
- **The Gwernia opening reads as a chain:** the Kendall cut-scene (Borg13 866) sets done flag 5. Derived flag 5423 is "5 set, 9 and 47 clear", and it enables the Shadow and the `stagenorth` scene (868). 5033 is "5 set, 55 and 47 clear", and it enables Gotzone and Zurene. The ambush cut-scene (869) sets 11, its scripted fight sets 978 (the party loses it by design), and Oriana's conversation (916) sets 50, journal entry 1.
- **Scripts:** 79 scripts aren't started by any map object or script. Code starts some of them, such as the help menu and the death scenes.
- **Reading flags from a checkpoint:** `gameStates` (1.0: `0x800DAF00`) points to the loaded `GameStateFunnel`. Its word at +8 points to the 8-byte flag entries, and each entry's byte at +6 is the flag's current value.

### The asset inspector (0.6)

`src/Aidyn.Inspector` is a Godot app for browsing every Borg by type and index. Run it with `tools/godot.ps1 --path src/Aidyn.Inspector` (build it first with `dotnet build`, or open it in the editor). It finds the ROMs in `roms/` (or `AIDYN_ROMS`) by hash and opens 1.0 first, or use **Open ROM…**.

- **The list** filters by type, and by index or summary text. Summaries fill in as it decodes the archive in the background, which takes a few seconds.
- **Preview:** textures with every mip level, and UI images. Models are shown alone and untextured, since scenes bind the textures. Scenes play all their animations. An animation plays in its scene, an actor plays its initial state, and a map chunk's collision shows where its first map set places it. Dialogue shows as text. Drag to turn, right-drag to pan, scroll to zoom. The 3D views are the CLI's glTF, loaded at run time with Godot's `GltfDocument`, so they match the scene viewer.
- **Godot loses texture wrap modes on import.** A `BaseMaterial3D` has one repeat switch for both axes and can't mirror, so glTF's mirrored repeat and one-axis clamp come out wrong. Faces are often half a texture mirrored across the middle (Donovan, scene 10363, lost half his face), and the knight's pauldrons (scene 9765) and the Gwernia house's door clamp or mirror too. `WrapMaterials` swaps those materials for a shader that wraps each axis itself. The runtime's N64 material will need the same.
- **Fields:** the decoded record, read by reflection, so a new decoder's fields show up with no inspector code. Lists expand in pages of 256. The Borg's `Borg_List` entry is below it.
- **Hex:** the decompressed bytes. Samples and sequences (1D) have only this so far.
- **For scripts and screenshots:** `tools/godot.ps1 --path src/Aidyn.Inspector -- --borg 10723 --tab preview --screenshot out/shot.png` opens a Borg, saves the window and quits. It gives up after a minute, so a hang can't block a script.

The decoding lives in `Aidyn.Rom.Inspection` (`BorgInspector`, `FieldTree`), which the tests check against every Borg; the Godot project is only UI. Like the CLI, it reads the ROM through `Aidyn.Rom` as a research tool. ADR 0001 keeps the runtime off the ROM, and when this grows into the mod editor (Phase 5) it will need to open plugins as well.

## The importer and the base plugin (M1)

`aidyn import <rom>` turns a ROM into `aidyn.base`, the plugin the runtime loads (ADR 0001). It goes to `plugins/aidyn.base` in the data folder: `AIDYN_DATA` if set, else `%LOCALAPPDATA%\Aidyn`. `--out` puts it elsewhere. It takes about 5 seconds and writes about 270 MB. It refuses the debug ROM (ADR 0007).

- **Layout.** `plugin.json` records the importer version (2 so far) (`BasePlugin.ImporterVersion`) and the ROM's SHA-1, so the runtime can tell when to re-import. `records/` holds `mapsets.json`, `maps.json` and `actors.json`. `assets/aidyn/<type>/<key>.glb` holds every scene (`aidyn:scene/0xHHHH`, the same glTF as `aidyn scenes`) and every chunk's collision (`aidyn:map/0xHHHH`, as `aidyn maps` writes it but in the chunk's own coordinates). A mod replaces an asset by putting a file at the same path in its own `assets/`.
- **Scenes have locator nodes** (`locator <i>`, under their transform), for what the game attaches at run time. Alaron's actor record names his head scene and its locator.
- **Records so far.** Map sets are `aidyn:mapset/<n>`, numbered as the original's `EnumMapDatA`, with the cell size and chunk list. A map (`aidyn:map/0xHHHH`, a Borg9) holds its scene objects, keyed by index. Their models name a scene or an actor, and they keep the original's rotation angles. Actors (`aidyn:actor/0xHHHH`) hold their scene and state machine. The other map object types, the DB tables and the flags come with the tracks that use them.
- **Deterministic.** Two imports are byte-identical, which a test checks. JSON is indented, with vectors and colours on one line, so two imports diff cleanly.
- **Merging** follows ADR 0005: patches merge field by field (RFC 7386), and `PluginSet.Conflicts` lists the top-level fields more than one plugin set. Lists are still replaced whole. Keyed lists need the record schema, which is Phase 5 work.

## The runtime (M1)

`src/Aidyn.Game` is the Godot game. It references `Aidyn.Plugins` and not `Aidyn.Rom`, so the build enforces ADR 0001. Import first (see above), then run it with `tools/godot.ps1 --path src/Aidyn.Game` (build with `dotnet build` first). It reads the plugins from the data folder, so set `AIDYN_DATA` for both if the import went elsewhere.

- **What it does so far.** It streams the 3×3 chunks around the player: each chunk's scene and see-through scene, the models its scene objects place (Expansion Pak set; each level of detail over its own distance range), actors running their Borg7 state machines, and the collision. Alaron starts where a new game does, snapped to the ground, and walks relative to the camera with his walk and run cycles.
- **Collision** is Godot physics from the collision assets. Every triangle that isn't passable blocks the player. See-through triangles, the invisible walls, don't block the camera. Toggles stay solid until the flag engine can open them.
- **Stand-ins** still to replace: the movement speeds and gravity (to port from `PlayerMove.cpp`), the orbit camera (the SM64-style one is in `camera.cpp`), the flat sky colour and fixed light, and materials that are Godot's glTF import plus `WrapMaterials`. Objects gated on real flags are all shown until the flag engine exists (M2). Alaron's head follows his neck, but the game keeps it upright and turns it toward nearby objects.
- **The user's first walk (Sep 27, 2026).** The user walked from the spawn to Gwernia (M07), and the barn, statue and castle line up with their emulator capture. Their notes, in order of how much they stood out:
  - **Vertex colour looks missing** on some models, trees especially. The unlit primitives do carry `COLOR_0`, so check whether Godot's import applies it to textured unlit materials, and whether the `WrapMaterials` swap keeps it. The N64 material should take over either way.
  - **Alaron needs his round shadow** on the ground under him (the player shadow in `ZoneEngine.cpp`; see the audit's combat visuals).
  - **The camera:** the generic orbit camera with mouse control feels good, but the SM64-style one is still the target. When a wall is behind the player, the spring arm pulls the camera into his face.
  - **Movement speed** is too fast now, but the original's is too slow and feels poor. Port the original speed exactly as the fidelity default and offer a faster profile (ADR 0006's fidelity settings; roadmap Phase 6). Gravity is probably Earth's, since 1 unit is about 1 m, and there's little falling in the game.
  - **Step height is too low.** He gets stuck on low edges. `CharacterBody3D` has no step-up, so either add one or port the original's collision response, which collides with edges and corners by the triangle flags.
- **Controls.** WASD or the left stick moves: the stick walks under 80% tilt and runs above, and on a keyboard Shift walks. Q and E, the right stick or right-drag turn the camera. F toggles a free camera (Space and C for up and down, Shift for faster), and F3 the overlay, which shows the chunk, the position in chunk coordinates and the collision below.
- **For scripts:** `-- --chunk I08 --at 16,62 --yaw 0 --pitch -17 --distance 5 --hold move_forward,4 --screenshot out/shot.png` starts somewhere, holds an input, saves the window and quits. `--fly x,y,z` starts with the free camera. It gives up after a minute.
- **The spawn point** is tight. A rock stands in a hole in the terrain in front of it, with collision walls and no top, and a birch's collision box is beside it. Walking while still falling from the spawn height once dropped the player into the rock, which is why the runtime now snaps to the ground.

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
