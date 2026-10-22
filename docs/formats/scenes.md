# Scenes, cameras and lights: Borg5, Borg3 and Borg4 (roadmap 1A)

`Borg5Scene`, `Borg5ParticleEmitter`, `Borg3Camera` and `Borg4Light` (`Aidyn.Rom.Graphics`) read them, and `SceneGltf` writes each scene as glTF.

- **Borg5 (1,402 scenes)** has a 0x44-byte header, then 0x40-byte transforms, then these lists:
  - the transforms' child lists;
  - the archive indices of the scene's lights, models and textures;
  - a u16 instruction stream;
  - particle emitters, 0xC0 bytes each ([particles](particles.md)).

  Every scene names a Borg3 camera. No scene has animated textures, which the game would crash on.
- **Transform order:** transform 0 is the camera, which uses op 4 (look-at). Next comes one direction (op 3) per light except the last, then the model nodes, which all use op 1 (scale, rotate about a pivot, translate). Ops 0 and 2 are never used. The last light has no direction and is the ambient, as the draw has it ([rendering](../systems/rendering.md#a-scenes-own-lights) says which scenes are lit by their own lights: no world scene has any).
- **Hierarchy:** flag 0x100 marks exactly the transforms with no parent, every other transform has exactly one parent, and children always come after their parent. Flag 0x200 is a billboard.
- **Billboards** are cards the game turns to the camera's position (`BorgAnimDrawSceneRaw`, with libultra's `guAlignF`). It keeps only the transform's world position and its scale times the scene object's (`SceneData.scalar`), and rolls the card by the transform's `rot.z` (offset 0x18); [rendering](../systems/rendering.md#billboards) has how it's drawn, and `Aidyn.Core.Scenes.Billboard` ports the matrix. In the data, some billboards have a roll, some are scaled unevenly, and animations drive the roll of some (in spell and combat scenes, such as 0x1B71). Far levels of detail of trees (0x0B5E for 0x0B5C, 38 to 70 away) and Oriana's path lights (0x374A for 0x1910, 5 to 24 away) are billboards.
- **Matrix conventions:** the game uses row vectors, like `System.Numerics`, and a child's world matrix is `local × parentWorld`. Rotations are in degrees and apply as roll about X by `rot.y`, then pitch about Y by `rot.x`, then heading about Z by `rot.z` (libultra's `guRotateRPYF` order). The scenes are Y-up, so `rot.x` turns a model about the vertical. The machine code (`0x8009E4C4` on the debug ROM, the same instructions in 1.0) takes op 1's roll from offset 0x14 and its pitch from 0x10, as the camera and lights (ops 3 and 4) do, so `rot.x` is the pitch and not the roll. Getting this the other way round only shows in animated scenes. Map objects rotate the other way round ([maps](maps.md#map-objects)). Scale comes first, in the node's own axes, and the pivot ends up at the position (`Borg5Scene.PivotRotate`, `0x8009497c`). The per-axis scale that the game keeps in the fourth column is always 1 for op 1, so the composition reduces to the ordinary one.
- **Units:** the game draws at 16 times scene units and scales vertices by the Borg2's scale, so a vertex lands at `v × scale / 16` in scene units. At the usual scale of 1/16, 256 vertex units make one scene unit.
- **Instructions:** these are `LoadTexture` (0xFF unbinds), `LoadModel`, `Matrix` and `Draw`. Five scenes (5246, 13851, 13922, 13924 and 13928) draw display lists that their models don't have. The operands are 8-bit, so in the 4 scenes with 256 or more transforms, the transforms from 256 on can't be drawn.
- **Texture coordinates are 64 units per texel,** not 32. The game sets a texture scale of ½ (`gSPTexture 0x8000`). So UV = S / (64 × width), with T = 0 at the texture's first row.
- **Borg3 (5 cameras):** each is 45° wide at 4:3. Near and far are 0.1 and 1000, 0.01 and 1000, or 0.25 and 25 scene units. **Borg4 (507 lights)** is 1.0 followed by an RGB colour, and most are greys.

## glTF export (1A)

The importer writes each scene into the base plugin, and `aidyn scenes <rom> [<index>...]` also writes them as `out/<version>/scenes/<index>.glb` for other tools. Units are scene units, which look like metres: the character in scene 10696 is 1.84 tall.

- **Nodes:** there's one node per model transform, using translation, rotation and scale. Billboards draw on a separate root node, `transform <t> billboard`, that holds only their scale and world position, which animations move and scale. Under it are `transform <t> facing`, which a viewer turns to the camera (still, it faces the way the card was modelled), and `transform <t> card`, which holds the meshes and rolls by `rot.z` about Z, animated where an animation drives the transform. The camera is a glTF perspective camera, and each directional light uses `KHR_lights_punctual`. The ambient light goes in the scene's `extras`, and `mirrors` is set there when a model transform's scale has an odd number of negative axes, at rest or as one of its animations poses it ([rendering](../systems/rendering.md#mirrored-models)).
- **Locators** ([particles](particles.md)) become a node `locator <i>` on their transform, at the locator model's position, where emitters and heads go. Under each is `locator <i> attachment`, turned by the locator model's own rotation (the Borg2 header's at `+0x18`, `+0x1C`, `+0x20`, in radians), where what a body holds hangs. `SceneGetLocatorMtx` (`0x8009EB10`), which only `RenderPlayers` calls for the bodies' attachments, makes the locator's matrix as `guRotateRPYF` of that rotation (roll about X, pitch about Y, heading about Z, row vectors, with no swap, unlike a transform's), then the position, then the transform's matrix (`Mtx.Multiply`, `0x800A280C`, out = b × a). `SceneGetLocatorPos` (`0x8009ECD0`), which the emitters and the spells' visuals take, leaves the rotation out. Alaron's shield arm (scene `0x25CB`, locator 2) is turned by (2.36, 0.36, −3.11).
- **Meshes:** each draw becomes a primitive. The draws the game skips are left out: hidden models, fully transparent ones, empty display lists and missing ones. So are draws through non-op-1 transforms, which the game squashes onto a line. Positions have the Borg2 scale baked in.
- **Materials** follow the game's combiners:
  - Textured draws are texture × shade (the vertex colour or the lighting), with alpha = texture alpha × (1 − tint alpha). The tint's colour isn't used.
  - Untextured draws are shade × tint colour.
  - Unlit display lists use `KHR_materials_unlit` with `COLOR_0`. Lit ones have normals.
  - Vertex colours and untextured tints are the values the N64 puts on screen, so they're decoded as sRGB into glTF's linear `COLOR_0` and `baseColorFactor`; written raw they would come out too bright ([rendering](../systems/rendering.md)).
  - The wrap and mirror flags map to sampler wrap modes, and flag 0x80 maps to nearest filtering.
  - Culling maps to `doubleSided`.
  - Cut-outs are `MASK` with an `alphaCutoff` of 0.125, where the N64's coverage leaves nothing of a pixel ([rendering](../systems/rendering.md#the-n64-material)).
  - Environment-mapped draws lose their texture, because glTF can't generate texture coordinates.
  - A textured material's `extras` has its Borg1 as `borg1`. The scene's `extras` lists the textures that scroll as `scrollingTextures`, each with its Borg1, width, scroll byte and how many times the scene's instructions load it ([textures](textures.md)).
- **Names and keys.** `SceneNames` and `ExtrasKeys` (`Aidyn.Plugins.Assets`) hold the node, animation and image names above and the extras' keys; `SceneGltf` writes with them, and every reader (`GlbReader`, `SceneExtras`, `ActorClips`) looks by them.

The editor views the imported scenes ([inspector](../systems/inspector.md)), and [rendering](../systems/rendering.md) has how the runtime draws them.
