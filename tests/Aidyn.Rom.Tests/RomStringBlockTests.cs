using System.Buffers.Binary;
using Aidyn.Rom.Text;

namespace Aidyn.Rom.Tests;

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

    [Theory]
    [MemberData(nameof(Tables))]
    public void DecompressesToWholeStrings(RomVersionId id, RomStringTable table)
    {
        var payload = RomStringBlock.Decompress(TestRoms.Get(id), table);

        // A u16 count, then length-prefixed strings that should fill the payload exactly.
        var count = BinaryPrimitives.ReadUInt16BigEndian(payload);
        var position = 2;
        for (var i = 0; i < count; i++)
        {
            Assert.True(position < payload.Length, $"string {i} of {count} starts past the end");
            position += 1 + payload[position];
        }
        Assert.Equal(payload.Length, position);
    }

    [Theory]
    [InlineData(RomStringTable.Common)]
    [InlineData(RomStringTable.Controller)]
    [InlineData(RomStringTable.Combat)]
    [InlineData(RomStringTable.Cheats)]
    public void DebugTablesMatchNa10(RomStringTable table) =>
        Assert.Equal(
            RomStringBlock.Decompress(TestRoms.Get(RomVersionId.Na10), table),
            RomStringBlock.Decompress(TestRoms.Get(RomVersionId.Debug), table));
}
