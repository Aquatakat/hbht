namespace Aidyn.Rom.Db;

/// <summary>Between Min and Max of one enemy.</summary>
public readonly record struct EncounterGroup(ItemId Enemy, byte Min, byte Max);

/// <summary>
/// The ambush that interrupts camping, for one terrain type and party level tier. Unlike the DB
/// tables, the ROM stores these IDs big-endian.
/// </summary>
/// <param name="LevelTier">0 for an average party level of 5 or less, then 6–10, 11–15, 16–20 and over 20.</param>
public sealed record CampEncounter(int Terrain, int LevelTier, IReadOnlyList<EncounterGroup> Groups)
{
    public const int LevelTiers = 5;
    public const int Size = 8;

    internal static IReadOnlyList<CampEncounter> ReadAll(ReadOnlySpan<byte> data)
    {
        var r = new RecordReader(data);
        var encounters = new CampEncounter[data.Length / Size];
        for (var i = 0; i < encounters.Length; i++)
        {
            encounters[i] = new CampEncounter(i / LevelTiers, i % LevelTiers,
            [
                new(new ItemId(r.U16Be()), r.U8(), r.U8()),
                new(new ItemId(r.U16Be()), r.U8(), r.U8()),
            ]);
        }
        return encounters;
    }
}
