using HBHT.Rom.Text;

namespace HBHT.Rom.Tests;

/// <summary>The real-data check for LZ01x, which the Borg archive doesn't use.</summary>
public class RomStringBlockTests
{
    public static TheoryData<RomVersionId, RomStringTable> Tables()
    {
        var data = new TheoryData<RomVersionId, RomStringTable>();
        foreach (var version in RomVersions.All)
            foreach (var table in version.Layout.StringTables.Keys)
                data.Add(version.Id, table);
        return data;
    }

    /// <summary><see cref="RomStringBlock.Split"/> throws unless the strings exactly fill the payload.</summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void DecompressesToWholeStrings(RomVersionId id, RomStringTable table) =>
        Assert.NotEmpty(RomStringBlock.Read(TestRoms.Get(id), table));

    [Fact]
    public void SplitReadsLengthPrefixedStrings() =>
        Assert.Equal(["", ""], RomStringBlock.Split([0, 2, 0, 0]));

    [Theory]
    [InlineData(new byte[] { 0, 2, 0 })]        // the second string is missing
    [InlineData(new byte[] { 0, 1, 5, 1, 2 })]  // the string runs past the end
    [InlineData(new byte[] { 0, 1, 0, 9 })]     // a byte follows the last string
    public void SplitRejectsPayloadsTheStringsDontFill(byte[] payload) =>
        Assert.Throws<InvalidDataException>(() => RomStringBlock.Split(payload));
}
