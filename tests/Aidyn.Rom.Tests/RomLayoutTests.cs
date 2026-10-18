namespace Aidyn.Rom.Tests;

public class RomLayoutTests
{
    /// <summary>Every table is placed, and they tile the DB block in order without overlapping.</summary>
    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TablesTileTheDbBlock(RomVersionId id)
    {
        var layout = RomVersions.Get(id).Layout;
        Assert.Equal(Enum.GetValues<DbTable>().Length, layout.DbTables.Count);
        Assert.Equal(Enum.GetValues<RomStringTable>().Length, layout.StringTables.Count);

        var ranges = layout.DbTables.Values.Concat(layout.StringTables.Values).OrderBy(r => r.Offset).ToList();
        Assert.Equal(layout.DbBlock, ranges[0].Offset);
        for (var i = 1; i < ranges.Count; i++)
            Assert.True(ranges[i - 1].End <= ranges[i].Offset, $"0x{ranges[i - 1].Offset:X} overlaps 0x{ranges[i].Offset:X}");
    }
}
