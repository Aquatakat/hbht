# Text, fonts and portraits (roadmap 1F)

The string tables, the fonts and the portraits beside what characters say. Dialogue's own text is in [dialogue](dialogue.md), the journal's in [quests](quests.md), and the string tables' layout in the DB block in [db](db.md). How the runtime lays text out and draws the conversation box is in [ui](../systems/ui.md).

## String tables

`aidyn strings <rom>` writes every table to `out/<version>/strings/`, and `aidyn strings <rom> <table>` prints one. The base plugin has each as a record, `aidyn:strings/0` to `/11`, numbered in the order they sit in the ROM, with the escapes decoded as the game does when it loads a table (`some_string_func`): `\n` is a newline and `\xHH` that character. Nothing else is escaped; the only button glyphs are `\x83` (C-down) and `\x84` (C-up), in Common. `%s` and `%d` are for the engine to fill in.

| # | Table | Lines (1.0) | What reads it |
| --- | --- | --- | --- |
| 0 | Items | 82 | The item details screen |
| 1 | Stats | 6 | The attribute descriptions |
| 2 | Spells | 55 | The spell descriptions |
| 3 | Skills | 18 | The skill descriptions |
| 4 | PotionDetails | 15 | The potion descriptions |
| 5 | Common | 555 | Menus and the rest of the UI, by position (`commonstrings.cpp` names its sections: stats, skills, weapons, terrains, weekdays and so on) |
| 6 | Copyright | 12 | The title screen |
| 7 | Controller | 21 | Controller Pak messages |
| 8 | Combat | 64 | The combat log |
| 9 | Cheats | 17 | The cheat codes |
| 10 | Credits | 133 | The credits, which a table in the code cuts into cards, as the credits record (`aidyn:credits/0`, [ui](../systems/ui.md#the-credits)) |
| 11 | PotionNames | 17 | Potions' names, which the item records already carry |

1.1 has 560 lines of Common and 25 of Controller, the Controller Pak errors it added. The debug ROM's credits have 130 lines. The descriptions are on the item, spell, stat and skill records too, which know which line is whose.

## Fonts

Engine code holds the fonts in three tables in the code segment, identical in all three ROMs (`Fonts`):

- **`font_face`** (1.0 `0xDAF08`): each font's Borg8 and its grid of glyph cells, the main font `0x1C` and the slim one `0x1D`, both 46 rows by 3 columns. Both sheets are IA8, 36 × 414, so a cell is 12 × 9. The sheet fills a column at a time: glyph *g* is in column *g* / 46 and row *g* % 46.
- **`struct_unk_.textIndexies`** (`0xE56C0`): the glyph each character from `0x20` to `0x8B` draws, the same for every font. 43 means the character is skipped: it isn't drawn and takes no room. That's `#`, `[` and `]`, which is why the game can write `\n` as a newline and a `#` it ignores. Some characters draw symbols: `<` a skull, `>` a heart, `|` infinity, `^` a triangle, `_` a diamond, `\` "ok", `` ` `` a cross and `~` a double arrow. `0x7F`–`0x89` are the controller buttons: A, B, the four C buttons, Start, the D-pad, Z, L and R.
- **`extraFontColors`** (`0xE572C`): the colour each button draws in, in place of the text's: A blue, B green, the C buttons yellow, Start red and the rest grey. The text's alpha still applies. `0x8A` and `0x8B` read past the table's end in the original; nothing uses them.

**Glyph widths are measured from the sheet** as the game loads it (`Font::SetupBorg8`). Each cell's top row marks the glyph's width: the glyph ends after the last texel that differs from a reference texel. For 8-bit sheets the reference is the cell's own rightmost texel; for 16- and 32-bit sheets it's the next texel in memory, and a cell whose rightmost texel already differs is full width. In the fonts, the glyph area of each cell is intensity 15, with its alpha as the glyph, and the rest of the cell is zero, so a glyph is as wide as its intensity-15 texels on the top row. A 4-bit sheet would be measured wrongly, by the high nibble of whole bytes, but none is.

- **L and R never show.** Their cells mark no width, so they measure 0 wide. The sheet is at fault, not `SetupBorg8`.
- **The slim font has no buttons.** Its button cells are blank, apart from a 2-texel mark.
- A glyph is drawn a whole cell tall, from its cell's left edge, and the next character starts where it ends. There's no other kerning.

The base plugin has each font as a record, `aidyn:font/0` (main) and `aidyn:font/1` (slim), with its sheet, grid, cell size, the widths the importer measured, the character map and the buttons' colours. The runtime reads the widths from the record, so a mod that redraws a sheet sets them too; the sheets are the images `aidyn:image/0x001C` and `0x001D`.

## Portraits

Each entity and dialogue entity has a portrait, a 40 × 40 CI8 Borg8, in the same code tables as its model (see [actors](actors.md)); `EntityDB::GetPortrait` reads them. The base plugin puts it on the entity and dialogue entity records as `portrait`. All 221 entities have one, and 32 of the 37 dialogue entities. The other five, and a speaker that isn't an entity at all (a script with no speaker, or the tools' `0xCCCC`), get `BORG8_PortraitNPCMale1` (`0x0125`), a fallback in code.
