# DB tables (roadmap 0.4)

The fixed databases at the end of the ROM: entities, items, spells, shops, camp encounters and the string tables (`Aidyn.Rom.Db`, `Aidyn.Rom.Text`). What each string table holds, and how the base plugin carries them, is in [text](text.md).

- **Layout.** `RomVersions` has every table in the DB block for all three ROMs. 1.1 is 1.0 at `+0x40` up to `String/Common`, which is `0x2960` bytes (1.0: `0x2830`). Copyright through the cinematic text are at `+0x170`. `String/Controller` is at `0x1FED6B0` and is `0x510` bytes (1.0: `0x410`), so everything after it is at `+0x270`. The debug ROM is 1.0 at `+0xEB80` throughout, except that its credits are `0x6F0` bytes, not `0x720`: three names are missing and one date differs. us.yaml has no ShopDB segment. It starts at `0x1FD31B0`, inside what the yaml calls ChestDB.
- **All records are identical in all three ROMs.** Only the text tables noted in [rom.md](rom.md) differ.
- **The decomp's string decryption is slightly wrong.** `decrypt_sub_a` shifts the second seed right by 8, not 7, and the key setup mixes all 16 bytes, not 14. This was found by solving for the key from the six stat descriptions, whose plaintext is in the decomp. Every string in every table then decrypts to printable ASCII. The only markup is `\n` and `\xHH`, where `\x83` and `\x84` are controller button glyphs. The readers keep both as written.
- **Grouped tables.** EntityDB, WeaponDB, ArmorDB, SpellDB and ItemDB split their records into groups. Each group matches a field: entity category, weapon class, armor slot, spell school, and the item ID's type. EntityDB has 7 categories, one more than the decomp's `ENTITY_CATEGORY`, and category 4 is empty.
- **AidynEditor agrees with the decomp** on the field offsets of entities, weapons, gear and loot tables.
- **Shops.** The game stocks a priced slot only when its third byte is nonzero, and it always stocks one item. The first byte is never read. The three trailing slots are always stocked at base price.
- **Camp encounters** are indexed as `terrain × 5 + level tier`, with 54 terrains. Mean enemy level rises with the tier, which confirms the order. Their item IDs are big-endian, unlike the DB tables.
- **Odd values the game tolerates.** A few entities have an "is heavy" byte other than 0 or 1 (Alaron's is 220). The game only tests it for nonzero.
- **Entities' fields** (see [characters](../systems/characters.md) for what the base plugin makes of them):
  - **Categories** are 0 plain NPCs and trainers, 1 humanoids, 2 named characters, 3 recruits, 5 Chaos troops and bosses, and 6 animals; 4 is empty. The decomp's enum puts Chaos at 4, but the code tests 5 (Charming fails on it).
  - **A skill of 255** can never be learnt. Values above 10 occur (13 and 15), which read as 10.
  - **Attributes** can be above their caps: some enemies have 120 Stamina, which reads as 90 but counts in full once modified.
  - **Resistances** are an element and a damage multiplier in quarters: 0 is immune, 4 normal and 8 double. 99 entities have (None, 4), which combat ignores.
  - **The reward byte** is × 50 on load, and × 1.5 again when awarded.
  - **Spell slots** with no spell still hold a level, usually 10.
- **Spells' training cost** is their last byte × 50 (`TempSpell::Init`).
