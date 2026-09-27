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

    public IReadOnlyDictionary<RomStringTable, RomRange> StringTables { get; init; } =
        new Dictionary<RomStringTable, RomRange>();

    public IReadOnlyDictionary<DbTable, RomRange> DbTables { get; init; } =
        new Dictionary<DbTable, RomRange>();
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
