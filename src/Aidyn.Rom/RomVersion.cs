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

/// <summary>LZ01x-compressed string tables loaded through the game's <c>RomString::Load</c>.</summary>
public enum RomStringTable
{
    Common,
    Controller,
    Combat,
    Cheats,
}

/// <summary>
/// Where things are in one ROM version (ADR 0007). Every ROM offset the code uses comes from here.
/// Entries are added as a reader is written and tested against them, so a missing string table
/// means "not verified for this version yet", not "absent".
/// </summary>
public sealed record RomLayout
{
    /// <summary>Start of the Borg archive. Listing offsets are relative to it.</summary>
    public required int BorgArchive { get; init; }

    /// <summary>The <c>Borg_List</c> index, which directly follows the archive.</summary>
    public required int BorgList { get; init; }

    /// <summary>Start of the fixed databases (EntityDB onwards).</summary>
    public required int DbBlock { get; init; }

    /// <summary>Each string table's location. The length is what the game copies from ROM.</summary>
    public IReadOnlyDictionary<RomStringTable, RomRange> StringTables { get; init; } =
        new Dictionary<RomStringTable, RomRange>();
}

/// <param name="Sha1">Lowercase hex SHA-1 of the big-endian image.</param>
/// <param name="ResearchOnly">Readable, but the importer must never build <c>aidyn.base</c> from it.</param>
public sealed record RomVersion(RomVersionId Id, string Name, string Sha1, bool ResearchOnly, RomLayout Layout);

public static class RomVersions
{
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
            StringTables = new Dictionary<RomStringTable, RomRange>
            {
                [RomStringTable.Common] = new(0x1FE7B80, 0x2830),
                [RomStringTable.Controller] = new(0x1FED540, 0x410),
                [RomStringTable.Combat] = new(0x1FED950, 0x500),
                [RomStringTable.Cheats] = new(0x1FEE6C0, 0xF0),
            },
        });

    // 1.1 shares the archive with 1.0, shifted by +0x40. String/Common was edited and grew, which
    // pushes everything after it to +0x270, and String/Controller changed too. Common's new length
    // and Controller's location still need identifying (roadmap 0.4).
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
            StringTables = new Dictionary<RomStringTable, RomRange>
            {
                [RomStringTable.Combat] = new(0x1FEDBC0, 0x500),
                [RomStringTable.Cheats] = new(0x1FEE930, 0xF0),
            },
        });

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
            StringTables = new Dictionary<RomStringTable, RomRange>
            {
                [RomStringTable.Common] = new(0x1FF6700, 0x2830),
                [RomStringTable.Controller] = new(0x1FFC0C0, 0x410),
                [RomStringTable.Combat] = new(0x1FFC4D0, 0x500),
                [RomStringTable.Cheats] = new(0x1FFD240, 0xF0),
            },
        });

    public static IReadOnlyList<RomVersion> All { get; } = [Na10, Na11, Debug];

    public static RomVersion Get(RomVersionId id) => All.Single(v => v.Id == id);

    public static RomVersion? FindBySha1(string sha1) =>
        All.FirstOrDefault(v => string.Equals(v.Sha1, sha1, StringComparison.OrdinalIgnoreCase));
}
