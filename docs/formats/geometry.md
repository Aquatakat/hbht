# Geometry: Borg2 (roadmap 1A)

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
