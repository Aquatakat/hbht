# Geometry: Borg2 (roadmap 1A)

`Borg2Geometry` (`Aidyn.Rom.Graphics`) reads a Borg2, and `F3dex2` runs its display lists.

- **Borg2 (5,636 models)** has a 0x50-byte header, followed by these parts in order:
  1. 8 bounding-box vertices per display list.
  2. The vertices.
  3. The display lists, back to back.
  4. A table of their offsets.
  5. A 0x14-byte material per display list: flags plus an RGBA float tint.

  Every file follows this layout, and every file ends exactly after the materials. The game points segment 0xE at the bounding boxes and segment 0xA at the vertices, then runs one display list per Borg5 instruction. The texture is bound by the Borg5 scene, not the Borg2.
- **Header:** alpha (0x00, a float, 0 in every Borg2), the display-list count (0x04), the scale (0x08), a position (0x0C) and a rotation in radians (0x18), 3 floats each, the locator index (0x24), a word whose bit 0 hides the model (0x28), then the offsets from the start of the data of the display-list table (0x2C), the bounding boxes (0x30) and the vertices (0x34), the vertex count (0x38) and the materials' offset (0x40). The words at 0x3C and 0x44 to 0x4C are always 0. The scale is 1/16 on most models, and the position and rotation are set on only a few; a locator model's place and turn are these ([particles](particles.md), [scenes](scenes.md#gltf-export-1a)). The locator index is 0xFFFFFFFF on most models, otherwise 0 to 7. The word at 0x28 is 1 on 162 models, whose display lists the game's draw code skips.
- **The display lists are F3DEX2** and use only six commands:
  - `G_VTX`, the vertex loads, which never go past buffer slot 31. The RSP lights a load's vertices in pairs from its first, and the game's engine lights reach only the first of each pair ([rendering](../systems/rendering.md#bodies-light)), so `F3dex2` marks the second (`N64Vertex.SecondOfPair`, by the vertex's place in the load, not its slot), and a lit list keeps the same vertex loaded at either place as two.
  - `G_TRI1` and `G_TRI2`.
  - `G_MODIFYVTX`, which only ever rewrites texture coordinates (`0x14`), and only in lit lists. The RSP applies the texture scale as it loads vertices, and `G_MODIFYVTX` writes over the already-scaled values, so the edited coordinates are in half the units of the loaded ones. Reading them unscaled misplaces textures on lit models.
  - `G_CULLDL` against the list's own bounding box, always first.
  - `G_ENDDL`.

  Some lists are empty, and some models have no lists at all. Every mesh lies inside its bounding box.
- **Material flag 0x8 means *unlit*.** The game turns lighting on when the bit is clear. In lists with the bit clear, vertices hold unit normals; with it set, they hold colours. Most lists are unlit (pre-lit vertex colours); the lit ones are mostly character body parts.
- **Material flags 0x1000 and 0x2000 wrap S and T, and 0x4000 and 0x8000 mirror them** (see [rendering](../systems/rendering.md)). With a wrap bit set, texture coordinates span many texture widths; without it they stay within one.
- **Vertex `flag`** (0–3) is ignored by the RSP and its meaning is unknown.
