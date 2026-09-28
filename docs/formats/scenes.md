# Scenes, cameras and lights: Borg5, Borg3 and Borg4 (roadmap 1A)

- **Borg5 (1,402 scenes)** has a 0x44-byte header, then 0x40-byte transforms, then these lists:
  - the transforms' child lists;
  - the archive indices of the scene's lights, models and textures;
  - a u16 instruction stream;
  - particle emitters, 0xC0 bytes each.

  Every scene names a Borg3 camera. No scene has animated textures, which the game would crash on. 75 scenes have particle emitters (218 in total); see [particles](particles.md).
- **Transform order:** transform 0 is the camera, which uses op 4 (look-at). Next comes one direction (op 3) per light except the last, then the model nodes, which all use op 1 (scale, rotate about a pivot, translate). Ops 0 and 2 are never used. The last light has no direction and is presumably ambient. In multi-light scenes it's the strictly darkest in 112, the first light is in 15, and the rest tie.
- **Hierarchy:** flag 0x100 marks exactly the transforms with no parent, every other transform has exactly one parent, and children always come after their parent. Flag 0x200 is a billboard (321 transforms).
- **Matrix conventions:** the game uses row vectors, like `System.Numerics`, and a child's world matrix is `local × parentWorld`. Rotations are in degrees and apply as roll about X by `rot.y`, then pitch about Y by `rot.x`, then heading about Z by `rot.z` (libultra's `guRotateRPYF` order). The scenes are Y-up, so `rot.x` turns a model about the vertical. The decomp's `Borg5Transform_op1` has X and Y the other way round. Its machine code (`0x8009E4C4` on the debug ROM, the same instructions in 1.0) takes its roll from offset 0x14 and its pitch from 0x10, as the camera and lights (ops 3 and 4) do. Static scenes didn't show the mistake: the Gwernia house has no rotations, and Brenna was only checked for texture placement. Animated ones did, when the user reviewed them: the opening's doors spun into the floor and Gabrion walked upside down. The pivot ends up at the position. The per-axis scale that the game keeps in the fourth column is always 1 for op 1, so the composition reduces to the ordinary one.
- **Units:** the game draws at 16 times scene units and scales vertices by the Borg2's scale, so a vertex lands at `v × scale / 16` in scene units. At the usual scale of 1/16, 256 vertex units make one scene unit.
- **Instructions:** these are `LoadTexture` (0xFF unbinds), `LoadModel`, `Matrix` and `Draw`. The stream flattens to 16,597 draws, of which 15,230 are textured. Five scenes (5246, 13851, 13922, 13924 and 13928) draw display lists that their models don't have. The operands are 8-bit, so in the 4 scenes with 256 or more transforms, the transforms from 256 on can't be drawn.
- **Texture coordinates are 64 units per texel,** not 32. The game sets a texture scale of ½ (`gSPTexture 0x8000`). As a check, 71 textured billboards span their texture exactly once at 64 units per texel, and none do at 32. So UV = S / (64 × width), with T = 0 at the texture's first row.
- **Borg3 (5 cameras):** each is 45° wide at 4:3. Near and far are 0.1 and 1000, 0.01 and 1000, or 0.25 and 25 scene units. **Borg4 (507 lights)** is 1.0 followed by an RGB colour, and most are greys.

## glTF export (1A)

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

What the renderer still has to do for scenes is in [rendering](../systems/rendering.md).
