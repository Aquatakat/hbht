# Geometry: Borg2 (roadmap 1A)

- **Borg2 (5,636 models)** has a 0x50-byte header, followed by these parts in order:
  1. 8 bounding-box vertices per display list.
  2. The vertices.
  3. The display lists, back to back.
  4. A table of their offsets.
  5. A 0x14-byte material per display list: flags plus an RGBA float tint.

  Every file follows this layout, and every file ends exactly after the materials. No file has an `unk0x3c` block. The game points segment 0xE at the bounding boxes and segment 0xA at the vertices, then runs one display list per Borg5 instruction. The texture is bound by the Borg5 scene, not the Borg2.
- **The display lists are F3DEX2** and use only six commands:
  - `G_VTX`, the vertex loads, which never go past buffer slot 31.
  - `G_TRI1` (313,655) and `G_TRI2` (3,027).
  - `G_MODIFYVTX`, which only ever rewrites texture coordinates (`0x14`), and only in lit lists. The RSP applies the texture scale as it loads vertices, and `G_MODIFYVTX` writes over the already-scaled values, so the edited coordinates are in half the units of the loaded ones. Reading them unscaled misplaces textures on lit models.
  - `G_CULLDL` against the list's own bounding box, always first.
  - `G_ENDDL`.

  In total there are 14,996 display lists and 319,709 triangles. 1,013 of the lists are empty, and 101 models have no lists at all. Every mesh lies inside its bounding box.
- **Material flag 0x8 means *unlit*.** The game turns lighting on when the bit is clear. In lists with the bit clear, vertices hold unit normals; with it set, they hold colours. 9,833 lists are unlit (pre-lit vertex colours) and 5,163 are lit, mostly character body parts.
- **Material flags 0x1000 and 0x2000 wrap S and T, and 0x4000 and 0x8000 mirror them** (see [rendering](../systems/rendering.md)). With a wrap bit set, texture coordinates span many texture widths; without it they stay within one.
- **Header:** the scale is 1/16 on 5,267 of the 5,636 models. The position and rotation are set on only a few. The word at 0x28 is 1 on 162 models, whose display lists the game's draw code skips. Vertex `flag` (0–3) is ignored by the RSP and its meaning is unknown.
