namespace HBHT.Rom;

public enum RomVersionId
{
    Na10,
    Na11,
    Debug,
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

/// <summary>
/// The supported ROMs and where each has its tables. NA 1.0's offsets are the table below; the other versions are NA 1.0
/// moved (<see cref="Moves"/>), with a few tables resized, and the debug ROM's code segment, a different build, has a
/// column of its own (<c>docs/formats/rom.md</c>).
/// </summary>
public static class RomVersions
{
    // NA 1.0's regions. Table order and 1.0 offsets follow the decomp's us.yaml, which folds ShopDB into ChestDB.
    private const int BorgArchive = 0x00E7220, BorgList = 0x1F89C10, DbBlock = 0x1FC2BB0, MapChunkLists = 0x00DBD90;

    /// <summary>The code segment's tables, in NA 1.0's order: length, NA 1.0's offset and the debug ROM's.</summary>
    private static readonly CodeRow[] Code =
    [
        // NA 1.1 moves these by -0xC4: the spell visuals' code.
        new(CodeTable.SpellVisualRefusedResisted, 12,     0x008C4D8, 0x0095C98),
        // -0xB4.
        new(CodeTable.SpellVisualCasterActor,     0x168,  0x008D740, 0x0096F08),
        new(CodeTable.SpellVisualTargetHead,      0x38,   0x008D8A8, 0x0097070),
        new(CodeTable.SpellVisualTargetActor,     0x198,  0x008D8E0, 0x00970A8),
        new(CodeTable.SpellVisualAura,            0x1C0,  0x008DA78, 0x0097240),
        new(CodeTable.SpellVisualPotionActor,     0xC4,   0x008DC38, 0x0097400),
        new(CodeTable.SpellVisualPotionAura,      0xE0,   0x008DCFC, 0x00974C4),
        // -0x10: the code from there on, and the data up to the camp tables.
        new(CodeTable.ParticleTextures,           0x44,   0x00CE460, 0x00D94B8),
        new(CodeTable.ContainerSounds,            0x30,   0x00CE5D0, 0x00D9A40),
        new(CodeTable.AlwaysAnimated,             20,     0x00CE650, 0x00D9AC0),
        new(CodeTable.SkyGradients,               0x24,   0x00CEB50, 0x00DA6A0),
        new(CodeTable.GameOverScenes,             0xC,    0x00CEDE0, 0x00DAAA0),
        new(CodeTable.OverworldMusicScenes,       0x4C,   0x00CF31C, 0x00DB384),
        new(CodeTable.JundarMusicScenes,          0x10,   0x00CF368, 0x00DB3D0),
        new(CodeTable.BattleMusic,                0x14,   0x00CF378, 0x00DB3E0),
        new(CodeTable.CreditsCards,               0x21,   0x00CFC60, 0x00DC0D0),
        new(CodeTable.PotionDescriptions,         0x40,   0x00D0360, 0x00DC8E0),
        new(CodeTable.ItemDescriptions,           0x14C,  0x00D03A0, 0x00DC948),
        new(CodeTable.SpellDescriptions,          0xE0,   0x00D04EC, 0x00DCAA8),
        new(CodeTable.WorldMapTowns,              0x24,   0x00D1720, 0x00DDF50),
        new(CodeTable.RegionFlags,                0x7A4,  0x00D1E6C, 0x00DE7CC),
        new(CodeTable.ScriptBattleFlags,          0xC,    0x00D2A80, 0x00DF740),
        new(CodeTable.CastingFactors,             0x34,   0x00D35CC, 0x00E075C),
        new(CodeTable.WeaponModels,               0x86,   0x00D3B14, 0x00E0D14),
        new(CodeTable.ItemModels,                 0xE,    0x00D3B9C, 0x00E0D9C),
        new(CodeTable.SpellSymbols,               0x6E,   0x00D3C58, 0x00E0E54),
        new(CodeTable.PotionNotNow,               0x26,   0x00D3D50, 0x00E0F68),
        new(CodeTable.PotionNotInTrek,            0x29,   0x00D3D78, 0x00E0F90),
        new(CodeTable.AuraImmune,                 13,     0x00D3E14, 0x00E102C),
        new(CodeTable.AuraResistant,              16,     0x00D3E24, 0x00E103C),
        new(CodeTable.AuraVulnerable,             17,     0x00D3E34, 0x00E104C),
        new(CodeTable.AuraResistantBelow,         4,      0x00D3E48, 0x00E1060),
        new(CodeTable.AuraFollower,               9,      0x00D3E50, 0x00E1068),
        new(CodeTable.AuraLeader,                 7,      0x00D3E5C, 0x00E1074),
        new(CodeTable.AuraHitpoints,              19,     0x00D3E64, 0x00E107C),
        new(CodeTable.AuraLevel,                  10,     0x00D3E78, 0x00E1090),
        new(CodeTable.AuraProtection,             26,     0x00D3E84, 0x00E109C),
        new(CodeTable.PartyFlags,                 0x54,   0x00D42C4, 0x00E173C),
        new(CodeTable.PartyDeathFlags,            0x38,   0x00D4318, 0x00E1790),
        new(CodeTable.ScrollNoSpell,              0x57,   0x00D435C, 0x00E17D4),
        new(CodeTable.SkillTrainingCosts,         0x30,   0x00D44D0, 0x00E193C),
        new(CodeTable.WeaponTrainingCosts,        0x2C,   0x00D4500, 0x00E197C),
        new(CodeTable.StatTrainingCosts,          0x1C,   0x00D4620, 0x00E1AF0),
        new(CodeTable.WeaponSounds,               0x218,  0x00D50D0, 0x00E26C0),
        new(CodeTable.CombatNoItems,              36,     0x00D5434, 0x00E2A24),
        new(CodeTable.CombatNoSpells,             30,     0x00D5458, 0x00E2A48),
        new(CodeTable.CombatLayOnHands,           13,     0x00D5480, 0x00E2A70),
        new(CodeTable.CombatHealWithHerbs,        16,     0x00D5490, 0x00E2A80),
        new(CodeTable.CombatPerform,              8,      0x00D54A0, 0x00E2A90),
        new(CodeTable.SpellVisualJumpTables,      0x424,  0x00D5898, 0x00E3008),
        new(CodeTable.ArmorRecipes,               0x1E,   0x00D8540, 0x00E6D10),
        new(CodeTable.PotionRecipes,              0x69,   0x00D8560, 0x00E6D30),
        new(CodeTable.CampDialogueFlags,          0x12,   0x00D85F0, 0x00E6E00),
        new(CodeTable.CampfireScenes,             0x28,   0x00D8604, 0x00E6E14),
        // +0x40: from the fonts to the end of the ROM, but for the text that grew.
        new(CodeTable.FontFaces,                  0x18,   0x00DAF08, 0x00E9918),
        new(CodeTable.LensFlare,                  0x80,   0x00DE770, 0x00ED230),
        new(CodeTable.Clouds,                     0x620,  0x00DE808, 0x00ED2C8),
        new(CodeTable.SkyObjects,                 0x758,  0x00DEE48, 0x00ED908),
        new(CodeTable.InventoryOrder,             0x20,   0x00DF600, 0x00EE160),
        new(CodeTable.EquipmentSlotImages,        0x3C,   0x00DF670, 0x00EE1D0),
        new(CodeTable.MenuIcons,                  0x504,  0x00DF700, 0x00EE260),
        new(CodeTable.WorldMapPieces,             0xA0,   0x00DFC40, 0x00EE7A0),
        new(CodeTable.MinimapSections,            0x24,   0x00E07A4, 0x00EF304),
        new(CodeTable.MinimapFlagImages,          0xE0,   0x00E07C8, 0x00EF328),
        new(CodeTable.ScriptSounds,               0xA0,   0x00E0960, 0x00EF4C0),
        new(CodeTable.ScriptEndings,              0x30,   0x00E0A1C, 0x00EF57C),
        new(CodeTable.FootstepEntries,            0x188,  0x00E0A60, 0x00EF5D0),
        new(CodeTable.StepSets,                   0x140,  0x00E0BE8, 0x00EF758),
        new(CodeTable.StepSounds,                 0x180,  0x00E0D28, 0x00EF898),
        new(CodeTable.VoiceSounds,                0x1F8,  0x00E0EA8, 0x00EFA18),
        new(CodeTable.SpellSounds,                0xEC,   0x00E10A0, 0x00EFC10),
        new(CodeTable.Cinematics,                 0x40,   0x00E1380, 0x00EFEF0),
        new(CodeTable.CinematicFades,             0x10,   0x00E1598, 0x00F0108),
        new(CodeTable.FlycamSequences,            120,    0x00E15DC, 0x00F014C),
        new(CodeTable.BowKindWeapons,             9,      0x00E1808, 0x00F0378),
        new(CodeTable.ThrownKindWeapons,          9,      0x00E1814, 0x00F0384),
        new(CodeTable.ArmorList,                  68,     0x00E1850, 0x00F03C0),
        new(CodeTable.DialogEntityList,           0x25,   0x00E1900, 0x00F0470),
        new(CodeTable.EntityModels,               0x1848, 0x00E1930, 0x00F04A0),
        new(CodeTable.DialogEntityModels,         0x294,  0x00E3178, 0x00F1CE8),
        new(CodeTable.EntityList,                 0xDD,   0x00E3440, 0x00F1FB0),
        new(CodeTable.WeaponsWithModels,          0x43,   0x00E3520, 0x00F2090),
        new(CodeTable.ItemsWithModels,            0xE,    0x00E3564, 0x00F20D4),
        new(CodeTable.ItemList,                   394,    0x00E3590, 0x00F2100),
        new(CodeTable.SpellList,                  0x3B,   0x00E3740, 0x00F22B0),
        new(CodeTable.WeaponList,                 105,    0x00E3790, 0x00F2300),
        new(CodeTable.LevelExp,                   0xA8,   0x00E3804, 0x00F2378),
        new(CodeTable.PotionPrices,               0x22,   0x00E38B0, 0x00F2420),
        new(CodeTable.MustTakeItems,              0x2A,   0x00E38E0, 0x00F2450),
        new(CodeTable.ReservedItems,              0xA0,   0x00E3910, 0x00F2490),
        new(CodeTable.KeyItemFlags,               0x3E,   0x00E39B0, 0x00F2530),
        new(CodeTable.KeyItems,                   0x3E,   0x00E39F0, 0x00F2570),
        new(CodeTable.KeyItemRegionFlags,         0x3E,   0x00E3A30, 0x00F25B0),
        new(CodeTable.KeyEquipment,               0x1A,   0x00E3A70, 0x00F25F0),
        new(CodeTable.CampFactors,                0x78,   0x00E3B38, 0x00F26B8),
        new(CodeTable.StatCaps,                   0x38,   0x00E3BE4, 0x00F2764),
        new(CodeTable.SeasonPrecipitation,        0x8,    0x00E3C40, 0x00F27C0),
        new(CodeTable.TerrainPrecipitation,       0x1C,   0x00E3C48, 0x00F27C8),
        new(CodeTable.TerrainPrecipitationScale,  0x70,   0x00E3C64, 0x00F27E4),
        new(CodeTable.FontGlyphs,                 0x6C,   0x00E56C0, 0x00F4240),
        new(CodeTable.FontColors,                 0x24,   0x00E572C, 0x00F42AC),
    ];

    /// <summary>The DB block's tables, back to back in NA 1.0's order: the databases and the string tables, with their lengths and NA 1.0's offsets.</summary>
    private static readonly BlockRow[] Block =
    [
        // NA 1.1 moves these by +0x40.
        new(DbTable.Entities,               0x7588, 0x1FC2BB0),
        new(DbTable.Weapons,                0x13E0, 0x1FCA140),
        new(DbTable.Armor,                  0xCD0,  0x1FCB520),
        new(DbTable.Spells,                 0x870,  0x1FCC1F0),
        new(DbTable.Items,                  0x2210, 0x1FCCA60),
        new(RomStringTable.Items,           0xED0,  0x1FCEC70),
        new(RomStringTable.Stats,           0x2F0,  0x1FCFB40),
        new(RomStringTable.Spells,          0x13A0, 0x1FCFE30),
        new(RomStringTable.Skills,          0x790,  0x1FD11D0),
        new(RomStringTable.PotionDetails,   0xA80,  0x1FD1960),
        new(DbTable.Chests,                 0xDD0,  0x1FD23E0),
        new(DbTable.Shops,                  0x1FB0, 0x1FD31B0),
        new(DbTable.DialogEntities,         0x37C,  0x1FD5160),
        new(DbTable.GameStateMod,           0x57F4, 0x1FD54DC),
        new(DbTable.GameStateFunnel,        0xBAB0, 0x1FDACD0),
        new(DbTable.AudioKeys,              0x1400, 0x1FE6780),
        new(RomStringTable.Common,          0x2830, 0x1FE7B80),
        // +0x170, after String/Common grew by 0x130.
        new(RomStringTable.Copyright,       0x180,  0x1FEA3B0),
        new(DbTable.Journal,                0x2090, 0x1FEA530),
        new(DbTable.TheatreMenu,            0x60,   0x1FEC5C0),
        new(DbTable.CinematicText,          0xD10,  0x1FEC620),
        new(DbTable.CinematicTextIndex,     0x210,  0x1FED330),
        new(RomStringTable.Controller,      0x410,  0x1FED540),
        // +0x270, after String/Controller grew by 0x100.
        new(RomStringTable.Combat,          0x500,  0x1FED950),
        new(DbTable.CampEncounters,         0x870,  0x1FEDE50),
        new(RomStringTable.Cheats,          0xF0,   0x1FEE6C0),
        new(RomStringTable.Credits,         0x720,  0x1FEE7B0),
        // The debug ROM moves this 0x30 less than the rest, after its shorter credits.
        new(RomStringTable.PotionNames,     0x100,  0x1FEEED0),
    ];

    public static RomVersion Na10 { get; } = new(
        RomVersionId.Na10, "NA 1.0", "47ae795a2b12783a13e2b8d03439ce9a39fc826c", ResearchOnly: false,
        Place(new Moves((0, 0))));

    // 1.1 is 1.0 rebuilt with fixes and new Controller Pak messages: its code segment moves by the amounts in the table's
    // comments, from the first offset where the bytes show each, and its DB block by +0x40 and then by what the two string
    // tables grew. Its code before 0x8BAC4 moves by other amounts, which no table needs yet.
    public static RomVersion Na11 { get; } = new(
        RomVersionId.Na11, "NA 1.1", "f80e135e7e962308f51bb9f7600b25213549c688", ResearchOnly: false,
        Place(
            new Moves((0x008BAC4, -0xC4), (0x008D710, -0xB4), (0x008DDE4, -0x10), (0x00DAF08, +0x40), (0x1FEA3B0, +0x170), (0x1FED950, +0x270)),
            resized: new() { [RomStringTable.Common] = 0x2960, [RomStringTable.Controller] = 0x510 }));

    // The debug ROM's archive and DB block are 1.0's moved by +0xEB80, but for its credits, which are 0x30 bytes shorter.
    // Its code segment is its own build, whose tables are the code table's debug column.
    public static RomVersion Debug { get; } = new(
        RomVersionId.Debug, "Debug 02.01d-PRERELEASE", "8169959ae059869b46461712a829ca0fe15c902c", ResearchOnly: true,
        Place(
            new Moves((BorgArchive, +0xEB80), (0x1FEEED0, +0xEB50)),
            code: row => row.Debug,
            mapChunkLists: 0x00EA850,
            resized: new() { [RomStringTable.Credits] = 0x6F0 }));

    public static IReadOnlyList<RomVersion> All { get; } = [Na10, Na11, Debug];

    public static RomVersion Get(RomVersionId id) => All.Single(v => v.Id == id);

    public static RomVersion? FindBySha1(string sha1) =>
        All.FirstOrDefault(v => string.Equals(v.Sha1, sha1, StringComparison.OrdinalIgnoreCase));

    /// <param name="code">Where the version has a code table, if not where <paramref name="moves"/> puts NA 1.0's.</param>
    /// <param name="mapChunkLists">Where the version has the map sets' chunk lists, if not where <paramref name="moves"/> puts NA 1.0's.</param>
    /// <param name="resized">The string tables whose length isn't NA 1.0's. The moves carry what follows them.</param>
    private static RomLayout Place(
        Moves moves, Func<CodeRow, int>? code = null, int? mapChunkLists = null, Dictionary<RomStringTable, int>? resized = null)
    {
        code ??= row => moves.Of(row.Na10);
        int Length(BlockRow row) =>
            row.Table is RomStringTable table && resized is not null && resized.TryGetValue(table, out var length) ? length : row.Length;

        return new RomLayout
        {
            BorgArchive = moves.Of(BorgArchive),
            BorgList = moves.Of(BorgList),
            DbBlock = moves.Of(DbBlock),
            MapChunkLists = mapChunkLists ?? moves.Of(MapChunkLists),
            CodeTables = Code.ToDictionary(row => row.Table, row => new RomRange(code(row), row.Length)),
            DbTables = Block.Where(row => row.Table is DbTable)
                .ToDictionary(row => (DbTable)row.Table, row => new RomRange(moves.Of(row.Na10), row.Length)),
            StringTables = Block.Where(row => row.Table is RomStringTable)
                .ToDictionary(row => (RomStringTable)row.Table, row => new RomRange(moves.Of(row.Na10), Length(row))),
        };
    }

    private readonly record struct CodeRow(CodeTable Table, int Length, int Na10, int Debug);

    /// <param name="Table">A <see cref="DbTable"/> or a <see cref="RomStringTable"/>.</param>
    private readonly record struct BlockRow(Enum Table, int Length, int Na10);

    /// <summary>
    /// How a version moves NA 1.0's offsets: an offset from one step's <c>From</c> up to the next step's moves by its
    /// <c>By</c>. One before the first step has no known place in the version.
    /// </summary>
    private sealed class Moves(params (int From, int By)[] steps)
    {
        public int Of(int na10)
        {
            for (var i = steps.Length - 1; i >= 0; i--)
                if (steps[i].From <= na10)
                    return na10 + steps[i].By;
            throw new NotSupportedException($"0x{na10:X7} is before where this version's moves from NA 1.0 are known.");
        }
    }
}
