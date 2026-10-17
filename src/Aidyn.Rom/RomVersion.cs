namespace Aidyn.Rom;

public enum RomVersionId
{
    Na10,
    Na11,
    Debug,
}

/// <summary>A byte range in the ROM.</summary>
public readonly record struct RomRange(int Offset, int Length)
{
    public int End => Offset + Length;
}

/// <summary>LZ01x-compressed, encrypted string tables loaded through the game's <c>RomString::Load</c>.</summary>
public enum RomStringTable
{
    /// <summary>Item descriptions for the item details screen.</summary>
    Items,
    /// <summary>Attribute descriptions.</summary>
    Stats,
    /// <summary>Spell descriptions.</summary>
    Spells,
    /// <summary>Skill descriptions.</summary>
    Skills,
    /// <summary>Potion descriptions.</summary>
    PotionDetails,
    /// <summary>Menu and UI text, including the debug menus.</summary>
    Common,
    /// <summary>The title screen's copyright notice.</summary>
    Copyright,
    /// <summary>Controller Pak messages.</summary>
    Controller,
    /// <summary>Combat log text.</summary>
    Combat,
    Cheats,
    Credits,
    PotionNames,
}

/// <summary>The fixed databases and other tables in the DB block, apart from the <see cref="RomStringTable"/>s.</summary>
public enum DbTable
{
    Entities,
    Weapons,
    Armor,
    Spells,
    Items,
    Chests,
    Shops,
    DialogEntities,
    /// <summary>The debug flag viewer's sort order. Not read yet.</summary>
    GameStateMod,
    GameStateFunnel,
    /// <summary>Audio key data (track 1D). Not read yet.</summary>
    AudioKeys,
    Journal,
    TheatreMenu,
    /// <summary>Compressed, encrypted cinematic captions.</summary>
    CinematicText,
    /// <summary>Where each cinematic's captions are in <see cref="CinematicText"/>, and when they show.</summary>
    CinematicTextIndex,
    CampEncounters,
}

/// <summary>
/// Content tables in the code segment that engine code reads (roadmap 1H). Each range is the
/// table's data, including its terminator where it has one.
/// </summary>
public enum CodeTable
{
    /// <summary>One byte per entity: the entity index the code's own tables use (the decomp's <c>entityList</c>).</summary>
    EntityList,
    /// <summary>Entity list index, joined flag, left flag, per recruitable member (<c>Party::CheckFlags</c>).</summary>
    PartyFlags,
    /// <summary>Entity list index and died flag, per recruitable member.</summary>
    PartyDeathFlags,
    /// <summary>The key items' "has" flags, in <see cref="KeyItems"/> order.</summary>
    KeyItemFlags,
    /// <summary>The 31 key items that get their own inventory slots and flags (<c>PartyInventory</c>).</summary>
    KeyItems,
    /// <summary>The world map region each key item reveals, or 0.</summary>
    KeyItemRegionFlags,
    /// <summary>Overworld row, column and region flag, as u32s (the decomp's <c>set_map_event_flag</c>).</summary>
    RegionFlags,
    /// <summary>The flag A values that mark a dialogue object as a camp scene (<c>Camp.cpp</c>).</summary>
    CampDialogueFlags,
    /// <summary>Script and flag pairs: winning the battle the script starts sets the flag (<c>dialoug_func</c>).</summary>
    ScriptBattleFlags,
    /// <summary>Scripts after which eight hours pass, as s32s ending with -1 (<c>isCampfireScene</c>).</summary>
    CampfireScenes,
    /// <summary>Scripts whose end leads somewhere: script, padding and ending kind (the decomp's <c>borg_short_list</c>).</summary>
    ScriptEndings,
    /// <summary>The death scene scripts. Only the first is used (<c>Gameover_func</c>).</summary>
    GameOverScenes,
    /// <summary>Flag, Borg8 image, x and y of each world map piece (<c>WorldMapPieces</c>).</summary>
    WorldMapPieces,
    /// <summary>Borg8 image, x and y of each town's name on the world map (<c>0x800d0b20</c> in 1.0).</summary>
    WorldMapTowns,
    /// <summary>The camp ambush's factor for each terrain type, 30 floats; 0 refuses camping (<c>camp_float_array</c>).</summary>
    CampFactors,
    /// <summary>
    /// The chance of precipitation in percent for each season, six u8s and two of padding (<c>Weather.Roll</c>,
    /// <c>0x8007bdb0</c> in 1.0, reads them as <c>0x800e3040</c>).
    /// </summary>
    SeasonPrecipitation,
    /// <summary>The chance of precipitation in percent added for each terrain type, 28 s8s.</summary>
    TerrainPrecipitation,
    /// <summary>How hard precipitation falls on each terrain type, 28 floats.</summary>
    TerrainPrecipitationScale,
    /// <summary>Pointers to each cinematic's Borg6 scenes, flags, Borg12 audio and clip planes (<c>gCinematicPointers</c>).</summary>
    Cinematics,
    /// <summary>Pointers to each cinematic's fades (<c>sCFadePs</c>).</summary>
    CinematicFades,
    /// <summary>Pointers to the minimap section lists, ending with null (<c>minimap_sec_pointers</c>).</summary>
    MinimapSections,
    /// <summary>
    /// Flag and Borg8 pairs, as u32s, in three lists that each end with zeros: the Ehud
    /// underground's, the Roog entrance's and the Barrows' (<c>minimap_dats_2</c>).
    /// </summary>
    MinimapFlagImages,
    /// <summary>
    /// Each entity's Borg7, Borg8 portrait and sizes, 0x1C bytes each, ending with zeros
    /// (<c>gEntityExtras</c>). Keyed by position in <see cref="EntityList"/> + 1.
    /// </summary>
    EntityModels,
    /// <summary>One byte per dialogue entity, like <see cref="EntityList"/> (the decomp's <c>DialougEntList</c>).</summary>
    DialogEntityList,
    /// <summary>
    /// Each dialogue entity's Borg7, Borg8 portrait and sizes, 0x14 bytes each, ending with zeros
    /// (<c>dailougEnt_info_array</c>). Keyed by position in <see cref="DialogEntityList"/> + 1.
    /// </summary>
    DialogEntityModels,
    /// <summary>17 s32 Borg1 indices, one per particle texture slot, −1 for empty (<c>partTextInds</c>).</summary>
    ParticleTextures,
    /// <summary>The Borg8 sky gradients by sky number, as u32s; number 0 is unused (<c>sky_borg8s</c>, <c>Sky::loadDay</c>).</summary>
    SkyGradients,
    /// <summary>
    /// The sun, moon and stars, 20 bytes each, ending with a type 3 entry (<c>skyobjectStructs</c>).
    /// The stars come first, then the sun and the moon.
    /// </summary>
    SkyObjects,
    /// <summary>The clouds, 28 bytes each, ending with an image 3 entry (<c>gCloudData</c>).</summary>
    Clouds,
    /// <summary>The lens flare's eight pieces, 16 bytes each (<c>gLensFlareData</c>).</summary>
    LensFlare,
    /// <summary>
    /// The fonts, 8 bytes each, ending with zeros (<c>font_face</c>): a u32 Borg8, then its rows
    /// and columns of glyph cells as bytes. The main font, then the slim one.
    /// </summary>
    FontFaces,
    /// <summary>
    /// Each character's glyph cell, one byte per character from <c>0x20</c> to <c>0x8B</c>, where
    /// 43 means the character isn't drawn (<c>struct_unk_.textIndexies</c>).
    /// </summary>
    FontGlyphs,
    /// <summary>
    /// The colours characters from <c>0x7F</c> draw in, in place of the text's: RGB bytes for the
    /// eleven controller buttons, then zeros (<c>extraFontColors</c>).
    /// </summary>
    FontColors,
    /// <summary>
    /// The experience each level starts at, as s32s from level 0 to 40, then a 0 (<c>EXP_TNL</c>).
    /// </summary>
    LevelExp,
    /// <summary>
    /// The most each stat's base value reads as (<c>base_stat_cap</c>) and then the most its modified
    /// value reads as (<c>modded_stat_cap</c>), 7 s32s each, by stat: Intelligence, Willpower,
    /// Dexterity, Endurance, Strength, Stamina, Level.
    /// </summary>
    StatCaps,
    /// <summary>
    /// What training each stat costs, per square of the level raised to, as 7 s32s (<c>stat_multiplier</c>
    /// in <c>CharStats::GetTraningPrice</c>). The level's is never used.
    /// </summary>
    StatTrainingCosts,
    /// <summary>The same for the 12 skills, as s32s (the multipliers <c>CharSkills::GetSkillXpMod</c> copies).</summary>
    SkillTrainingCosts,
    /// <summary>The same for the 11 weapon classes, as s32s (<c>CharSkills::GetWeaponXpMod</c>).</summary>
    WeaponTrainingCosts,
    /// <summary>The 17 potions' prices, as u16s by potion (<c>potion_prices</c>).</summary>
    PotionPrices,
    /// <summary>
    /// The Alchemist's 15 recipes, 7 bytes each: potion, spice, herb, gemstone, Alchemist level, an
    /// unread byte and the least step (<c>potionRecipies</c>).
    /// </summary>
    PotionRecipes,
    /// <summary>
    /// The Mechanic's 5 recipes, 6 bytes each: the hide's item DB position as a u16, the armor's armor
    /// DB position, the least step, the Mechanic level and padding (<c>ArmorRecipies</c>).
    /// </summary>
    ArmorRecipes,
    /// <summary>
    /// The menus' icons (<c>menuimages.cpp</c>): lists of u16 key and Borg8 pairs, each followed by its
    /// count as a u32. Items by ID, skills, aspects, schools and spell symbols.
    /// </summary>
    MenuIcons,
    /// <summary>
    /// The 80 items with their own inventory slots after the general ones, as u16 IDs: reagents,
    /// hides, scrolls and potions (<c>PartyInventory::AddKeyItem</c>).
    /// </summary>
    ReservedItems,
    /// <summary>The 13 unique weapons, armor and gear with their own inventory slots, as u16 IDs (<c>AddKeyEquipment</c>).</summary>
    KeyEquipment,
    /// <summary>The 21 items that loot can't be left behind with, as u16 IDs (<c>IInventory::HasNoKeyItem</c>).</summary>
    MustTakeItems,
    /// <summary>The spells' positions that code tables key spells by: the low byte of each position's spell ID (<c>SpellList</c>).</summary>
    SpellList,
    /// <summary>
    /// Each spell's icon symbol, as (position in <see cref="SpellList"/>, symbol) byte pairs ending with
    /// 0xFF (<c>SpellDB::GetIcon</c>). A spell not listed has symbol 0.
    /// </summary>
    SpellSymbols,
    /// <summary>Each potion's line in the PotionDetails string table, as (potion ID, line) u16 pairs ending with a 0 ID (<c>WidgetItemDetail</c>).</summary>
    PotionDescriptions,
    /// <summary>Each item's line in the Items string table, as (item ID, line) u16 pairs ending with a 0 ID.</summary>
    ItemDescriptions,
    /// <summary>Each spell's line in the Spells string table, as (spell ID, line) u16 pairs ending with a 0 ID.</summary>
    SpellDescriptions,
    /// <summary>The order the inventory list shows item kinds in, as u16 DB types ending with 0 (<c>D_800ed560</c>).</summary>
    InventoryOrder,
    /// <summary>Each equipment slot's empty image, 15 u32 Borg8 indices by slot (<c>gItemSlotBorg8Indecies</c>).</summary>
    EquipmentSlotImages,
    /// <summary>
    /// The positions in the weapon DB that have a model, as bytes ending with 0xFF (<c>weapons_with_borg5</c>).
    /// <see cref="WeaponModels"/> has each one's Borg5 at the same index.
    /// </summary>
    WeaponsWithModels,
    /// <summary>The Borg5 of each weapon in <see cref="WeaponsWithModels"/>, as u16s, 66 of them (<c>weapon_borg5_ID</c>).</summary>
    WeaponModels,
    /// <summary>
    /// The positions in the item DB that have a model, as u16s ending with 0xFFFF (<c>ItemBorg5Search</c>).
    /// <see cref="ItemModels"/> has each one's Borg5 at the same index.
    /// </summary>
    ItemsWithModels,
    /// <summary>The Borg5 of each item in <see cref="ItemsWithModels"/>, as u16s.</summary>
    ItemModels,
    /// <summary>What drinking a potion says when it would do nothing now, a C string (<c>Entity::CanUsePotion</c>).</summary>
    PotionNotNow,
    /// <summary>What drinking a flask, or a potion with no effect, says outside combat, a C string.</summary>
    PotionNotInTrek,
    /// <summary>What reading a scroll without a spell says, a C string (<c>Party::UseScroll</c>): the Lighthouse scroll's.</summary>
    ScrollNoSpell,
    /// <summary>The 39 Borg12s a script's <c>PlaySound</c> plays by its operand, as u32s, then a 0 (<c>dialoug_SFX</c>).</summary>
    ScriptSounds,
    /// <summary>
    /// The containers' sounds, as u32 Borg12s: eight by container kind, then the four of a trapped one exploding
    /// (<c>loot_func</c>, <c>open_explosive_chest</c>).
    /// </summary>
    ContainerSounds,
    /// <summary>
    /// The Overworld's chunks with music, by their Borg5 scenes as u16s, in two lists that each end with a 0
    /// (<c>Zones.SetMapSet</c>).
    /// </summary>
    OverworldMusicScenes,
    /// <summary>The Jundar interiors' chunks with music, by their Borg5 scenes as u16s, ending with a 0 (<c>Zones.JundarBgmList</c>).</summary>
    JundarMusicScenes,
    /// <summary>
    /// The battle music as u16 Borg12s: five with the Expansion Pak, ending with a 0, then four without
    /// (<c>Zones.SetMapSet</c>).
    /// </summary>
    BattleMusic,
    /// <summary>
    /// The 49 footstep keys, 8 bytes each as the audio keys' (<see cref="DbTable.AudioKeys"/>): a u16 frac, the request,
    /// a count of step ids and four step ids. Entry 0 is empty (<c>AudioKeys.PlaySurfaceSteps</c>).
    /// </summary>
    FootstepEntries,
    /// <summary>
    /// The four footstep sets by body kind, 0x50 bytes each: 26 surfaces × the sneak, walk and run footstep keys' bytes,
    /// 0 for none, then 2 bytes of padding.
    /// </summary>
    StepSets,
    /// <summary>The 96 Borg12s step ids name, as u32s (<c>Audio.StepSounds</c>).</summary>
    StepSounds,
    /// <summary>The 126 Borg12s the audio keys' voice ids and the weapons' sounds name, as u32s (<c>Audio.VoiceSounds</c>).</summary>
    VoiceSounds,
    /// <summary>The 59 spells' casting sounds as u32 Borg12s, by spell position (<c>Audio.SpellSounds</c>).</summary>
    SpellSounds,
    /// <summary>
    /// Each weapon's sound by its position in <see cref="WeaponsWithModels"/>, 8 bytes each: two u16 voice ids, one
    /// picked at random, and padding, ending with 0xFFFF (<c>AudioKeys.WeaponSound</c>).
    /// </summary>
    WeaponSounds,
}

/// <summary>
/// Where things are in one ROM version (ADR 0007). Every ROM offset the code uses comes from here.
/// Entries are added as a reader is written and tested against them, so a missing table means
/// "not verified for this version yet", not "absent".
/// </summary>
/// <remarks>
/// A table's range can include a few bytes of padding after its data.
/// </remarks>
public sealed record RomLayout
{
    /// <summary>Start of the Borg archive. Listing offsets are relative to it.</summary>
    public required int BorgArchive { get; init; }

    /// <summary>The <c>Borg_List</c> index, which directly follows the archive.</summary>
    public required int BorgList { get; init; }

    /// <summary>Start of the fixed databases (EntityDB onwards).</summary>
    public required int DbBlock { get; init; }

    /// <summary>
    /// The map sets' chunk lists, in the code segment (the decomp's <c>BorgMapLists.cpp</c>): one
    /// list per <see cref="Maps.MapSet"/>, back to back, each ending with an all-zero entry.
    /// </summary>
    public required int MapChunkLists { get; init; }

    public IReadOnlyDictionary<RomStringTable, RomRange> StringTables { get; init; } =
        new Dictionary<RomStringTable, RomRange>();

    public IReadOnlyDictionary<DbTable, RomRange> DbTables { get; init; } =
        new Dictionary<DbTable, RomRange>();

    public IReadOnlyDictionary<CodeTable, RomRange> CodeTables { get; init; } =
        new Dictionary<CodeTable, RomRange>();
}

/// <param name="Sha1">Lowercase hex SHA-1 of the big-endian image.</param>
/// <param name="ResearchOnly">Readable, but the importer must never build <c>aidyn.base</c> from it.</param>
public sealed record RomVersion(RomVersionId Id, string Name, string Sha1, bool ResearchOnly, RomLayout Layout);

public static class RomVersions
{
    // Table order and 1.0 offsets follow the decomp's us.yaml, which folds ShopDB into ChestDB.
    public static RomVersion Na10 { get; } = new(
        RomVersionId.Na10,
        "NA 1.0",
        "47ae795a2b12783a13e2b8d03439ce9a39fc826c",
        ResearchOnly: false,
        new RomLayout
        {
            BorgArchive = 0x00E7220,
            BorgList = 0x1F89C10,
            DbBlock = 0x1FC2BB0,
            MapChunkLists = 0x00DBD90,
            CodeTables = new Dictionary<CodeTable, RomRange>
            {
                [CodeTable.EntityList] = new(0x00E3440, 0xDD),
                [CodeTable.PartyFlags] = new(0x00D42C4, 0x54),
                [CodeTable.PartyDeathFlags] = new(0x00D4318, 0x38),
                [CodeTable.KeyItemFlags] = new(0x00E39B0, 0x3E),
                [CodeTable.KeyItems] = new(0x00E39F0, 0x3E),
                [CodeTable.KeyItemRegionFlags] = new(0x00E3A30, 0x3E),
                [CodeTable.RegionFlags] = new(0x00D1E6C, 0x7A4),
                [CodeTable.CampDialogueFlags] = new(0x00D85F0, 0x12),
                [CodeTable.ScriptBattleFlags] = new(0x00D2A80, 0xC),
                [CodeTable.CampfireScenes] = new(0x00D8604, 0x28),
                [CodeTable.ScriptEndings] = new(0x00E0A1C, 0x30),
                [CodeTable.GameOverScenes] = new(0x00CEDE0, 0xC),
                [CodeTable.WorldMapPieces] = new(0x00DFC40, 0xA0),
                [CodeTable.WorldMapTowns] = new(0x00D1720, 0x24),
                [CodeTable.CampFactors] = new(0x00E3B38, 0x78),
                [CodeTable.SeasonPrecipitation] = new(0x00E3C40, 0x8),
                [CodeTable.TerrainPrecipitation] = new(0x00E3C48, 0x1C),
                [CodeTable.TerrainPrecipitationScale] = new(0x00E3C64, 0x70),
                [CodeTable.Cinematics] = new(0x00E1380, 0x40),
                [CodeTable.CinematicFades] = new(0x00E1598, 0x10),
                [CodeTable.MinimapSections] = new(0x00E07A4, 0x24),
                [CodeTable.MinimapFlagImages] = new(0x00E07C8, 0xE0),
                [CodeTable.EntityModels] = new(0x00E1930, 0x1848),
                [CodeTable.DialogEntityList] = new(0x00E1900, 0x25),
                [CodeTable.DialogEntityModels] = new(0x00E3178, 0x294),
                [CodeTable.ParticleTextures] = new(0x00CE460, 0x44),
                [CodeTable.SkyGradients] = new(0x00CEB50, 0x24),
                [CodeTable.SkyObjects] = new(0x00DEE48, 0x758),
                [CodeTable.Clouds] = new(0x00DE808, 0x620),
                [CodeTable.LensFlare] = new(0x00DE770, 0x80),
                [CodeTable.FontFaces] = new(0x00DAF08, 0x18),
                [CodeTable.FontGlyphs] = new(0x00E56C0, 0x6C),
                [CodeTable.FontColors] = new(0x00E572C, 0x24),
                [CodeTable.LevelExp] = new(0x00E3804, 0xA8),
                [CodeTable.StatCaps] = new(0x00E3BE4, 0x38),
                [CodeTable.StatTrainingCosts] = new(0x00D4620, 0x1C),
                [CodeTable.SkillTrainingCosts] = new(0x00D44D0, 0x30),
                [CodeTable.WeaponTrainingCosts] = new(0x00D4500, 0x2C),
                [CodeTable.PotionPrices] = new(0x00E38B0, 0x22),
                [CodeTable.PotionRecipes] = new(0x00D8560, 0x69),
                [CodeTable.ArmorRecipes] = new(0x00D8540, 0x1E),
                [CodeTable.MenuIcons] = new(0x00DF700, 0x504),
                [CodeTable.ReservedItems] = new(0x00E3910, 0xA0),
                [CodeTable.KeyEquipment] = new(0x00E3A70, 0x1A),
                [CodeTable.MustTakeItems] = new(0x00E38E0, 0x2A),
                [CodeTable.SpellList] = new(0x00E3740, 0x3B),
                [CodeTable.SpellSymbols] = new(0x00D3C58, 0x6E),
                [CodeTable.PotionDescriptions] = new(0x00D0360, 0x40),
                [CodeTable.ItemDescriptions] = new(0x00D03A0, 0x14C),
                [CodeTable.SpellDescriptions] = new(0x00D04EC, 0xE0),
                [CodeTable.InventoryOrder] = new(0x00DF600, 0x20),
                [CodeTable.EquipmentSlotImages] = new(0x00DF670, 0x3C),
                [CodeTable.PotionNotNow] = new(0x00D3D50, 0x26),
                [CodeTable.PotionNotInTrek] = new(0x00D3D78, 0x29),
                [CodeTable.ScrollNoSpell] = new(0x00D435C, 0x57),
                [CodeTable.ScriptSounds] = new(0x00E0960, 0xA0),
                [CodeTable.ContainerSounds] = new(0x00CE5D0, 0x30),
                [CodeTable.OverworldMusicScenes] = new(0x00CF31C, 0x4C),
                [CodeTable.JundarMusicScenes] = new(0x00CF368, 0x10),
                [CodeTable.BattleMusic] = new(0x00CF378, 0x14),
                [CodeTable.FootstepEntries] = new(0x00E0A60, 0x188),
                [CodeTable.StepSets] = new(0x00E0BE8, 0x140),
                [CodeTable.StepSounds] = new(0x00E0D28, 0x180),
                [CodeTable.VoiceSounds] = new(0x00E0EA8, 0x1F8),
                [CodeTable.SpellSounds] = new(0x00E10A0, 0xEC),
                [CodeTable.WeaponSounds] = new(0x00D50D0, 0x218),
                [CodeTable.WeaponsWithModels] = new(0x00E3520, 0x43),
                [CodeTable.WeaponModels] = new(0x00D3B14, 0x86),
                [CodeTable.ItemsWithModels] = new(0x00E3564, 0xE),
                [CodeTable.ItemModels] = new(0x00D3B9C, 0xE),
            },
            DbTables = new Dictionary<DbTable, RomRange>
            {
                [DbTable.Entities] = new(0x1FC2BB0, 0x7588),
                [DbTable.Weapons] = new(0x1FCA140, 0x13E0),
                [DbTable.Armor] = new(0x1FCB520, 0xCD0),
                [DbTable.Spells] = new(0x1FCC1F0, 0x870),
                [DbTable.Items] = new(0x1FCCA60, 0x2210),
                [DbTable.Chests] = new(0x1FD23E0, 0xDD0),
                [DbTable.Shops] = new(0x1FD31B0, 0x1FB0),
                [DbTable.DialogEntities] = new(0x1FD5160, 0x37C),
                [DbTable.GameStateMod] = new(0x1FD54DC, 0x57F4),
                [DbTable.GameStateFunnel] = new(0x1FDACD0, 0xBAB0),
                [DbTable.AudioKeys] = new(0x1FE6780, 0x1400),
                [DbTable.Journal] = new(0x1FEA530, 0x2090),
                [DbTable.TheatreMenu] = new(0x1FEC5C0, 0x60),
                [DbTable.CinematicText] = new(0x1FEC620, 0xD10),
                [DbTable.CinematicTextIndex] = new(0x1FED330, 0x210),
                [DbTable.CampEncounters] = new(0x1FEDE50, 0x870),
            },
            StringTables = new Dictionary<RomStringTable, RomRange>
            {
                [RomStringTable.Items] = new(0x1FCEC70, 0xED0),
                [RomStringTable.Stats] = new(0x1FCFB40, 0x2F0),
                [RomStringTable.Spells] = new(0x1FCFE30, 0x13A0),
                [RomStringTable.Skills] = new(0x1FD11D0, 0x790),
                [RomStringTable.PotionDetails] = new(0x1FD1960, 0xA80),
                [RomStringTable.Common] = new(0x1FE7B80, 0x2830),
                [RomStringTable.Copyright] = new(0x1FEA3B0, 0x180),
                [RomStringTable.Controller] = new(0x1FED540, 0x410),
                [RomStringTable.Combat] = new(0x1FED950, 0x500),
                [RomStringTable.Cheats] = new(0x1FEE6C0, 0xF0),
                [RomStringTable.Credits] = new(0x1FEE7B0, 0x720),
                [RomStringTable.PotionNames] = new(0x1FEEED0, 0x100),
            },
        });

    // 1.1 is 1.0 shifted by +0x40 up to String/Common, which grew by 0x130. Copyright through
    // the cinematic text are then at +0x170. String/Controller grew by 0x100, so everything
    // after it is at +0x270. Apart from those two string tables, the data is unchanged.
    public static RomVersion Na11 { get; } = new(
        RomVersionId.Na11,
        "NA 1.1",
        "f80e135e7e962308f51bb9f7600b25213549c688",
        ResearchOnly: false,
        new RomLayout
        {
            BorgArchive = 0x00E7260,
            BorgList = 0x1F89C50,
            DbBlock = 0x1FC2BF0,
            MapChunkLists = 0x00DBDD0,
            CodeTables = new Dictionary<CodeTable, RomRange>
            {
                [CodeTable.EntityList] = new(0x00E3480, 0xDD),
                [CodeTable.PartyFlags] = new(0x00D42B4, 0x54),
                [CodeTable.PartyDeathFlags] = new(0x00D4308, 0x38),
                [CodeTable.KeyItemFlags] = new(0x00E39F0, 0x3E),
                [CodeTable.KeyItems] = new(0x00E3A30, 0x3E),
                [CodeTable.KeyItemRegionFlags] = new(0x00E3A70, 0x3E),
                [CodeTable.RegionFlags] = new(0x00D1E5C, 0x7A4),
                [CodeTable.CampDialogueFlags] = new(0x00D85E0, 0x12),
                [CodeTable.ScriptBattleFlags] = new(0x00D2A70, 0xC),
                [CodeTable.CampfireScenes] = new(0x00D85F4, 0x28),
                [CodeTable.ScriptEndings] = new(0x00E0A5C, 0x30),
                [CodeTable.GameOverScenes] = new(0x00CEDD0, 0xC),
                [CodeTable.WorldMapPieces] = new(0x00DFC80, 0xA0),
                [CodeTable.WorldMapTowns] = new(0x00D1710, 0x24),
                [CodeTable.CampFactors] = new(0x00E3B78, 0x78),
                [CodeTable.SeasonPrecipitation] = new(0x00E3C80, 0x8),
                [CodeTable.TerrainPrecipitation] = new(0x00E3C88, 0x1C),
                [CodeTable.TerrainPrecipitationScale] = new(0x00E3CA4, 0x70),
                [CodeTable.Cinematics] = new(0x00E13C0, 0x40),
                [CodeTable.CinematicFades] = new(0x00E15D8, 0x10),
                [CodeTable.MinimapSections] = new(0x00E07E4, 0x24),
                [CodeTable.MinimapFlagImages] = new(0x00E0808, 0xE0),
                [CodeTable.EntityModels] = new(0x00E1970, 0x1848),
                [CodeTable.DialogEntityList] = new(0x00E1940, 0x25),
                [CodeTable.DialogEntityModels] = new(0x00E31B8, 0x294),
                [CodeTable.ParticleTextures] = new(0x00CE450, 0x44),
                [CodeTable.SkyGradients] = new(0x00CEB40, 0x24),
                [CodeTable.SkyObjects] = new(0x00DEE88, 0x758),
                [CodeTable.Clouds] = new(0x00DE848, 0x620),
                [CodeTable.LensFlare] = new(0x00DE7B0, 0x80),
                [CodeTable.FontFaces] = new(0x00DAF48, 0x18),
                [CodeTable.FontGlyphs] = new(0x00E5700, 0x6C),
                [CodeTable.FontColors] = new(0x00E576C, 0x24),
                [CodeTable.LevelExp] = new(0x00E3844, 0xA8),
                [CodeTable.StatCaps] = new(0x00E3C24, 0x38),
                [CodeTable.StatTrainingCosts] = new(0x00D4610, 0x1C),
                [CodeTable.SkillTrainingCosts] = new(0x00D44C0, 0x30),
                [CodeTable.WeaponTrainingCosts] = new(0x00D44F0, 0x2C),
                [CodeTable.PotionPrices] = new(0x00E38F0, 0x22),
                [CodeTable.PotionRecipes] = new(0x00D8550, 0x69),
                [CodeTable.ArmorRecipes] = new(0x00D8530, 0x1E),
                [CodeTable.MenuIcons] = new(0x00DF740, 0x504),
                [CodeTable.ReservedItems] = new(0x00E3950, 0xA0),
                [CodeTable.KeyEquipment] = new(0x00E3AB0, 0x1A),
                [CodeTable.MustTakeItems] = new(0x00E3920, 0x2A),
                [CodeTable.SpellList] = new(0x00E3780, 0x3B),
                [CodeTable.SpellSymbols] = new(0x00D3C48, 0x6E),
                [CodeTable.PotionDescriptions] = new(0x00D0350, 0x40),
                [CodeTable.ItemDescriptions] = new(0x00D0390, 0x14C),
                [CodeTable.SpellDescriptions] = new(0x00D04DC, 0xE0),
                [CodeTable.InventoryOrder] = new(0x00DF640, 0x20),
                [CodeTable.EquipmentSlotImages] = new(0x00DF6B0, 0x3C),
                [CodeTable.PotionNotNow] = new(0x00D3D40, 0x26),
                [CodeTable.PotionNotInTrek] = new(0x00D3D68, 0x29),
                [CodeTable.ScrollNoSpell] = new(0x00D434C, 0x57),
                [CodeTable.ScriptSounds] = new(0x00E09A0, 0xA0),
                [CodeTable.ContainerSounds] = new(0x00CE5C0, 0x30),
                [CodeTable.OverworldMusicScenes] = new(0x00CF30C, 0x4C),
                [CodeTable.JundarMusicScenes] = new(0x00CF358, 0x10),
                [CodeTable.BattleMusic] = new(0x00CF368, 0x14),
                [CodeTable.FootstepEntries] = new(0x00E0AA0, 0x188),
                [CodeTable.StepSets] = new(0x00E0C28, 0x140),
                [CodeTable.StepSounds] = new(0x00E0D68, 0x180),
                [CodeTable.VoiceSounds] = new(0x00E0EE8, 0x1F8),
                [CodeTable.SpellSounds] = new(0x00E10E0, 0xEC),
                [CodeTable.WeaponSounds] = new(0x00D50C0, 0x218),
                [CodeTable.WeaponsWithModels] = new(0x00E3560, 0x43),
                [CodeTable.WeaponModels] = new(0x00D3B04, 0x86),
                [CodeTable.ItemsWithModels] = new(0x00E35A4, 0xE),
                [CodeTable.ItemModels] = new(0x00D3B8C, 0xE),
            },
            DbTables = new Dictionary<DbTable, RomRange>
            {
                [DbTable.Entities] = new(0x1FC2BF0, 0x7588),
                [DbTable.Weapons] = new(0x1FCA180, 0x13E0),
                [DbTable.Armor] = new(0x1FCB560, 0xCD0),
                [DbTable.Spells] = new(0x1FCC230, 0x870),
                [DbTable.Items] = new(0x1FCCAA0, 0x2210),
                [DbTable.Chests] = new(0x1FD2420, 0xDD0),
                [DbTable.Shops] = new(0x1FD31F0, 0x1FB0),
                [DbTable.DialogEntities] = new(0x1FD51A0, 0x37C),
                [DbTable.GameStateMod] = new(0x1FD551C, 0x57F4),
                [DbTable.GameStateFunnel] = new(0x1FDAD10, 0xBAB0),
                [DbTable.AudioKeys] = new(0x1FE67C0, 0x1400),
                [DbTable.Journal] = new(0x1FEA6A0, 0x2090),
                [DbTable.TheatreMenu] = new(0x1FEC730, 0x60),
                [DbTable.CinematicText] = new(0x1FEC790, 0xD10),
                [DbTable.CinematicTextIndex] = new(0x1FED4A0, 0x210),
                [DbTable.CampEncounters] = new(0x1FEE0C0, 0x870),
            },
            StringTables = new Dictionary<RomStringTable, RomRange>
            {
                [RomStringTable.Items] = new(0x1FCECB0, 0xED0),
                [RomStringTable.Stats] = new(0x1FCFB80, 0x2F0),
                [RomStringTable.Spells] = new(0x1FCFE70, 0x13A0),
                [RomStringTable.Skills] = new(0x1FD1210, 0x790),
                [RomStringTable.PotionDetails] = new(0x1FD19A0, 0xA80),
                [RomStringTable.Common] = new(0x1FE7BC0, 0x2960),
                [RomStringTable.Copyright] = new(0x1FEA520, 0x180),
                [RomStringTable.Controller] = new(0x1FED6B0, 0x510),
                [RomStringTable.Combat] = new(0x1FEDBC0, 0x500),
                [RomStringTable.Cheats] = new(0x1FEE930, 0xF0),
                [RomStringTable.Credits] = new(0x1FEEA20, 0x720),
                [RomStringTable.PotionNames] = new(0x1FEF140, 0x100),
            },
        });

    // The debug ROM is 1.0 shifted by +0xEB80, except that its credits are 0x30 bytes shorter.
    public static RomVersion Debug { get; } = new(
        RomVersionId.Debug,
        "Debug 02.01d-PRERELEASE",
        "8169959ae059869b46461712a829ca0fe15c902c",
        ResearchOnly: true,
        new RomLayout
        {
            BorgArchive = 0x00F5DA0,
            BorgList = 0x1F98790,
            DbBlock = 0x1FD1730,
            MapChunkLists = 0x00EA850,
            CodeTables = new Dictionary<CodeTable, RomRange>
            {
                [CodeTable.EntityList] = new(0x00F1FB0, 0xDD),
                [CodeTable.PartyFlags] = new(0x00E173C, 0x54),
                [CodeTable.PartyDeathFlags] = new(0x00E1790, 0x38),
                [CodeTable.KeyItemFlags] = new(0x00F2530, 0x3E),
                [CodeTable.KeyItems] = new(0x00F2570, 0x3E),
                [CodeTable.KeyItemRegionFlags] = new(0x00F25B0, 0x3E),
                [CodeTable.RegionFlags] = new(0x00DE7CC, 0x7A4),
                [CodeTable.CampDialogueFlags] = new(0x00E6E00, 0x12),
                [CodeTable.ScriptBattleFlags] = new(0x00DF740, 0xC),
                [CodeTable.CampfireScenes] = new(0x00E6E14, 0x28),
                [CodeTable.ScriptEndings] = new(0x00EF57C, 0x30),
                [CodeTable.GameOverScenes] = new(0x00DAAA0, 0xC),
                [CodeTable.WorldMapPieces] = new(0x00EE7A0, 0xA0),
                [CodeTable.WorldMapTowns] = new(0x00DDF50, 0x24),
                [CodeTable.CampFactors] = new(0x00F26B8, 0x78),
                [CodeTable.SeasonPrecipitation] = new(0x00F27C0, 0x8),
                [CodeTable.TerrainPrecipitation] = new(0x00F27C8, 0x1C),
                [CodeTable.TerrainPrecipitationScale] = new(0x00F27E4, 0x70),
                [CodeTable.Cinematics] = new(0x00EFEF0, 0x40),
                [CodeTable.CinematicFades] = new(0x00F0108, 0x10),
                [CodeTable.MinimapSections] = new(0x00EF304, 0x24),
                [CodeTable.MinimapFlagImages] = new(0x00EF328, 0xE0),
                [CodeTable.EntityModels] = new(0x00F04A0, 0x1848),
                [CodeTable.DialogEntityList] = new(0x00F0470, 0x25),
                [CodeTable.DialogEntityModels] = new(0x00F1CE8, 0x294),
                [CodeTable.ParticleTextures] = new(0x00D94B8, 0x44),
                [CodeTable.SkyGradients] = new(0x00DA6A0, 0x24),
                [CodeTable.SkyObjects] = new(0x00ED908, 0x758),
                [CodeTable.Clouds] = new(0x00ED2C8, 0x620),
                [CodeTable.LensFlare] = new(0x00ED230, 0x80),
                [CodeTable.FontFaces] = new(0x00E9918, 0x18),
                [CodeTable.FontGlyphs] = new(0x00F4240, 0x6C),
                [CodeTable.FontColors] = new(0x00F42AC, 0x24),
                [CodeTable.LevelExp] = new(0x00F2378, 0xA8),
                [CodeTable.StatCaps] = new(0x00F2764, 0x38),
                [CodeTable.StatTrainingCosts] = new(0x00E1AF0, 0x1C),
                [CodeTable.SkillTrainingCosts] = new(0x00E193C, 0x30),
                [CodeTable.WeaponTrainingCosts] = new(0x00E197C, 0x2C),
                [CodeTable.PotionPrices] = new(0x00F2420, 0x22),
                [CodeTable.PotionRecipes] = new(0x00E6D30, 0x69),
                [CodeTable.ArmorRecipes] = new(0x00E6D10, 0x1E),
                [CodeTable.MenuIcons] = new(0x00EE260, 0x504),
                [CodeTable.ReservedItems] = new(0x00F2490, 0xA0),
                [CodeTable.KeyEquipment] = new(0x00F25F0, 0x1A),
                [CodeTable.MustTakeItems] = new(0x00F2450, 0x2A),
                [CodeTable.SpellList] = new(0x00F22B0, 0x3B),
                [CodeTable.SpellSymbols] = new(0x00E0E54, 0x6E),
                [CodeTable.PotionDescriptions] = new(0x00DC8E0, 0x40),
                [CodeTable.ItemDescriptions] = new(0x00DC948, 0x14C),
                [CodeTable.SpellDescriptions] = new(0x00DCAA8, 0xE0),
                [CodeTable.InventoryOrder] = new(0x00EE160, 0x20),
                [CodeTable.EquipmentSlotImages] = new(0x00EE1D0, 0x3C),
                [CodeTable.PotionNotNow] = new(0x00E0F68, 0x26),
                [CodeTable.PotionNotInTrek] = new(0x00E0F90, 0x29),
                [CodeTable.ScrollNoSpell] = new(0x00E17D4, 0x57),
                [CodeTable.ScriptSounds] = new(0x00EF4C0, 0xA0),
                [CodeTable.ContainerSounds] = new(0x00D9A40, 0x30),
                [CodeTable.OverworldMusicScenes] = new(0x00DB384, 0x4C),
                [CodeTable.JundarMusicScenes] = new(0x00DB3D0, 0x10),
                [CodeTable.BattleMusic] = new(0x00DB3E0, 0x14),
                [CodeTable.FootstepEntries] = new(0x00EF5D0, 0x188),
                [CodeTable.StepSets] = new(0x00EF758, 0x140),
                [CodeTable.StepSounds] = new(0x00EF898, 0x180),
                [CodeTable.VoiceSounds] = new(0x00EFA18, 0x1F8),
                [CodeTable.SpellSounds] = new(0x00EFC10, 0xEC),
                [CodeTable.WeaponSounds] = new(0x00E26C0, 0x218),
                [CodeTable.WeaponsWithModels] = new(0x00F2090, 0x43),
                [CodeTable.WeaponModels] = new(0x00E0D14, 0x86),
                [CodeTable.ItemsWithModels] = new(0x00F20D4, 0xE),
                [CodeTable.ItemModels] = new(0x00E0D9C, 0xE),
            },
            DbTables = new Dictionary<DbTable, RomRange>
            {
                [DbTable.Entities] = new(0x1FD1730, 0x7588),
                [DbTable.Weapons] = new(0x1FD8CC0, 0x13E0),
                [DbTable.Armor] = new(0x1FDA0A0, 0xCD0),
                [DbTable.Spells] = new(0x1FDAD70, 0x870),
                [DbTable.Items] = new(0x1FDB5E0, 0x2210),
                [DbTable.Chests] = new(0x1FE0F60, 0xDD0),
                [DbTable.Shops] = new(0x1FE1D30, 0x1FB0),
                [DbTable.DialogEntities] = new(0x1FE3CE0, 0x37C),
                [DbTable.GameStateMod] = new(0x1FE405C, 0x57F4),
                [DbTable.GameStateFunnel] = new(0x1FE9850, 0xBAB0),
                [DbTable.AudioKeys] = new(0x1FF5300, 0x1400),
                [DbTable.Journal] = new(0x1FF90B0, 0x2090),
                [DbTable.TheatreMenu] = new(0x1FFB140, 0x60),
                [DbTable.CinematicText] = new(0x1FFB1A0, 0xD10),
                [DbTable.CinematicTextIndex] = new(0x1FFBEB0, 0x210),
                [DbTable.CampEncounters] = new(0x1FFC9D0, 0x870),
            },
            StringTables = new Dictionary<RomStringTable, RomRange>
            {
                [RomStringTable.Items] = new(0x1FDD7F0, 0xED0),
                [RomStringTable.Stats] = new(0x1FDE6C0, 0x2F0),
                [RomStringTable.Spells] = new(0x1FDE9B0, 0x13A0),
                [RomStringTable.Skills] = new(0x1FDFD50, 0x790),
                [RomStringTable.PotionDetails] = new(0x1FE04E0, 0xA80),
                [RomStringTable.Common] = new(0x1FF6700, 0x2830),
                [RomStringTable.Copyright] = new(0x1FF8F30, 0x180),
                [RomStringTable.Controller] = new(0x1FFC0C0, 0x410),
                [RomStringTable.Combat] = new(0x1FFC4D0, 0x500),
                [RomStringTable.Cheats] = new(0x1FFD240, 0xF0),
                [RomStringTable.Credits] = new(0x1FFD330, 0x6F0),
                [RomStringTable.PotionNames] = new(0x1FFDA20, 0x100),
            },
        });

    public static IReadOnlyList<RomVersion> All { get; } = [Na10, Na11, Debug];

    public static RomVersion Get(RomVersionId id) => All.Single(v => v.Id == id);

    public static RomVersion? FindBySha1(string sha1) =>
        All.FirstOrDefault(v => string.Equals(v.Sha1, sha1, StringComparison.OrdinalIgnoreCase));
}
