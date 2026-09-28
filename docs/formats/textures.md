# Textures and UI images: Borg1 and Borg8 (roadmap 1A)

`aidyn textures <rom>` writes every Borg1 and Borg8 as PNG, with an `index.csv` of formats, sizes and flags.

- **Borg1 (2,183 textures)** is a 0x18-byte header, then every mip level back to back, then an RGBA16 palette for CI8 (256 entries) and CI4 (16). There are `lods + 1` levels. Each level halves both sides, and each row is padded to at least 8 bytes. All 2,183 match this layout exactly. No texture has its own display list, and every bitmap starts at 0x18.
- **Borg1 bitmaps are stored in TMEM order.** The game uploads each one with a single `LoadBlock`, so odd rows have each pair of 32-bit words swapped (64-bit words for RGBA32). Undoing the swap is what makes the first mip level match a downsample of the base level: 1,718 of the 1,723 mipmapped textures match better, and the other 5 are a single flat colour.
- **Borg1 formats:** CI8 1,769, RGBA16 137, CI4 135, IA8 62, IA16 56, RGBA32 12, I4 6, I8 5, IA4 1. The largest is 64×64, apart from one 32×128. Most are 32×32.
- **Borg1 flags:** 0x20 is on exactly the 407 textures with transparent texels. The game's render modes blend IA16, IA8 and RGBA32 textures with it and alpha-test the rest. 103 scroll (`0x200`), and all of those also have 0x100, which gives them a second, writable bitmap.
- **Some textures are upside down,** the digit glyphs among them. T = 0 is the first row, and the models' texture coordinates are exported as they are, so any flip is the model's own.
- **Borg8 (560 UI images)** is a 0x10-byte header, then the palette (CI only) and a linear bitmap, at offsets from the start of the data. The game draws them with `LoadTile`, so there's no TMEM swap. Formats: IA8 275, CI8 267, RGBA32 16, RGBA16 1, I4 1. The largest are the 320×240 menu backdrops.
