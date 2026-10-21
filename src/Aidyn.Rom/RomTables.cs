namespace Aidyn.Rom;

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
    /// <summary>
    /// The first line of each card of the credits' string table, 33 bytes (<c>Credits.CardStarts</c>): card k is its lines
    /// from entry k to entry k + 1, and the last runs to the line before "The End" (<c>Credits.Start</c>).
    /// </summary>
    CreditsCards,
    /// <summary>
    /// The floats casting reads, 13 of them (<c>0x800D29CC</c> in 1.0): a Lunar caster's chance by the moon's phase (4), anyone else's by the
    /// part of the day (5), the spell-improvement factor, an int-to-float constant the code needs there, and the resist roll's aspect
    /// factors, same then different (<c>Combatant.CastChance</c>, <c>ResolveCastRoll</c>, <c>ResistRoll</c>).
    /// </summary>
    CastingFactors,
    /// <summary>The weapon DB positions that are bows, as bytes ending with 0xFF (<c>Combat.BowKindWeapons</c>, <c>0x800E0C08</c> in 1.0): they fly as an arrow.</summary>
    BowKindWeapons,
    /// <summary>The weapon DB positions that are thrown, as bytes ending with 0xFF (<c>Combat.ThrownKindWeapons</c>, <c>0x800E0C14</c> in 1.0): they fly as themselves.</summary>
    ThrownKindWeapons,
    /// <summary>"Lay On Hands", the combat menu's row for the skill (<c>0x800D4880</c> in 1.0, <c>CombatMenu.PickLayOnHands</c>), a C string.</summary>
    CombatLayOnHands,
    /// <summary>"Heal With Herbs", the row for the skill (<c>0x800D4890</c> in 1.0, <c>CombatMenu.PickHealWithHerbs</c>), a C string.</summary>
    CombatHealWithHerbs,
    /// <summary>"Perform", the Troubadour's row (<c>0x800D48A0</c> in 1.0, <c>CombatMenu.PickTroubadour</c>), a C string.</summary>
    CombatPerform,
    /// <summary>What the combat menu says when there are no spells or skills to choose (<c>0x800D4858</c> in 1.0, <c>CombatMenu.OpenSpells</c>), a C string.</summary>
    CombatNoSpells,
    /// <summary>What the combat menu says when there are no potions or item spells to choose (<c>0x800D4834</c> in 1.0, <c>CombatMenu.OpenItems</c>), a C string.</summary>
    CombatNoItems,
    /// <summary>Sense Aura's line for a party member who follows (<c>0x800D3250</c> in 1.0), a C string.</summary>
    AuraFollower,
    /// <summary>Sense Aura's line for the party's leader (<c>0x800D325C</c> in 1.0), a C string.</summary>
    AuraLeader,
    /// <summary>Sense Aura's hitpoints line, a printf format (<c>0x800D3264</c> in 1.0).</summary>
    AuraHitpoints,
    /// <summary>Sense Aura's level line, a printf format (<c>0x800D3278</c> in 1.0).</summary>
    AuraLevel,
    /// <summary>Sense Aura's armor line, a printf format (<c>0x800D3284</c> in 1.0).</summary>
    AuraProtection,
    /// <summary>Sense Aura's line for a resistance at 0 or less, a printf format (<c>0x800D3214</c> in 1.0).</summary>
    AuraImmune,
    /// <summary>Sense Aura's line for a resistance above 0 and below <see cref="AuraResistantBelow"/>, a printf format (<c>0x800D3224</c> in 1.0).</summary>
    AuraResistant,
    /// <summary>Sense Aura's line for a resistance at or above <see cref="AuraResistantBelow"/>, a printf format (<c>0x800D3234</c> in 1.0).</summary>
    AuraVulnerable,
    /// <summary>The float a resistance is compared with (<c>0x800D3248</c> in 1.0): 2.0.</summary>
    AuraResistantBelow,
    /// <summary>The caster-actor switch of the spell visuals (role 0, <c>0x8008CB40</c> in 1.0, <c>CombatSpellFx.CasterActor</c>): the function and its case blocks. The five functions run back to back.</summary>
    SpellVisualCasterActor,
    /// <summary>The function that says whether a spell's target actor stands at the head (<c>0x8008CCA8</c> in 1.0, <c>CombatSpellFx.TargetAtHead</c>).</summary>
    SpellVisualTargetHead,
    /// <summary>The target-actor switch (role 1, <c>0x8008CCE0</c> in 1.0, <c>CombatSpellFx.TargetActor</c>).</summary>
    SpellVisualTargetActor,
    /// <summary>The target's aura-scene switch (role 5, <c>0x8008CE78</c> in 1.0, <c>CombatSpellFx.AuraScene</c>).</summary>
    SpellVisualAura,
    /// <summary>The potion-actor switch (role 2, <c>0x8008D038</c> in 1.0, <c>CombatSpellFx.PotionActor</c>).</summary>
    SpellVisualPotionActor,
    /// <summary>The potion's aura-scene switch (role 6, <c>0x8008D0FC</c> in 1.0, <c>CombatSpellFx.PotionAura</c>), to the end of the spell visuals' code.</summary>
    SpellVisualPotionAura,
    /// <summary>The jump tables of the six spell-visual switches, back to back (<c>0x800D4C98</c> in 1.0).</summary>
    SpellVisualJumpTables,
    /// <summary>
    /// The instructions that load the refused cast's actor (<c>addiu s3, zero, 0x1CAA</c>, <c>0x8008B8D8</c> in 1.0) and, 8 bytes on, the resisted
    /// cast's (<c>0x37F1</c>), in <c>CombatSpellFx.AddVisual</c>.
    /// </summary>
    SpellVisualRefusedResisted,
    /// <summary>
    /// The armor DB's positions, as the low byte of each position's armor or shield ID, 68 bytes (<c>ArmorList</c>, <c>0xE1850</c> in 1.0).
    /// It is the armor DB's own order: <see cref="Engine.PositionLists"/> checks that.
    /// </summary>
    ArmorList,
    /// <summary>The weapon DB's positions, as the low byte of each weapon ID, 105 bytes (<c>weaponList</c>, <c>0xE3790</c> in 1.0).</summary>
    WeaponList,
    /// <summary>The item DB's positions, as each item's u16 ID, 197 of them (<c>itemID_array</c>, <c>0xE3590</c> in 1.0).</summary>
    ItemList,
    /// <summary>
    /// The Borg7 actors that <c>ProcessPlayers</c> animates in combat even idle and far from the camera's aim, as u16s ending with a 0
    /// (<c>Body.AlwaysAnimated</c>, <c>0x800CDA50</c> in 1.0, read by <c>Body.InAlwaysAnimated</c>).
    /// </summary>
    AlwaysAnimated,
    /// <summary>
    /// The title screen's six fly-throughs, 20 bytes each (<c>0x800E09DC</c> in 1.0, <c>Title.StartScenery</c>): a Borg6 as a u32, then four
    /// u32s whose low halves the code stores.
    /// </summary>
    FlycamSequences,
}
