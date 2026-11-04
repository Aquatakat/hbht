using HBHT.Plugins.Records;

namespace HBHT.Plugins.Tests;

public class RecordIdTests
{
    [Theory]
    [InlineData("aidyn:entity/0x023F")]
    [InlineData("aidyn:flag/47")]
    [InlineData("aidyn:flag/0")]
    [InlineData("aidyn:mapobject/8/C05/17")]
    [InlineData("aidyn:chunk/8/C05")]
    [InlineData("aidyn:chunk/0/0_13")]
    [InlineData("aidyn:chunk/0/-1_13")]
    [InlineData("aidyn:chunk/0/H10")]
    [InlineData("aidyn:chunk/16/Z100")]
    [InlineData("aidyn:mapobject/0/-1_13/0")]
    [InlineData("aidyn:mapset/16")]
    [InlineData("mymod:flag/met_the_hermit")]
    [InlineData("mymod:chunk/isle")]
    [InlineData("mymod:chunk/hermit_isle")]
    [InlineData("mymod:mapobject/isle/3")]
    [InlineData("my_mod2:mapobject/camp/3")]
    public void CanonicalIdsRoundTrip(string text)
    {
        Assert.Equal(text, RecordId.Parse(text).ToString());
    }

    [Theory]
    [InlineData("aidyn:entity/0x023f")] // lowercase hex
    [InlineData("aidyn:entity/023F")] // no 0x
    [InlineData("aidyn:entity/0x23F")] // too few digits
    [InlineData("aidyn:flag/047")] // leading zero
    [InlineData("aidyn:flag/")]
    [InlineData("aidyn:widget/1")] // unknown type
    [InlineData("MyMod:flag/x")] // namespace case
    [InlineData("mymod:flag/Met")] // mod key case
    [InlineData("aidyn:mapobject/8/C05/0x0001")] // an index is a decimal
    [InlineData("aidyn/flag/1")]
    [InlineData("aidyn:entity/47")] // a DB record is keyed by its ItemID
    [InlineData("aidyn:flag/0x002F")] // a flag by a decimal
    [InlineData("aidyn:mapobject/8/C05")] // a map object needs its index
    [InlineData("aidyn:chunk/8/C05/3")] // a chunk has none
    [InlineData("aidyn:chunk/8/c05")] // the row's letter is uppercase
    [InlineData("aidyn:chunk/08/C05")] // the map set's number has no leading zero
    [InlineData("aidyn:chunk/8/C5")] // the column has two digits
    [InlineData("aidyn:chunk/8/C005")] // and no more than it needs
    [InlineData("aidyn:chunk/C05")] // a base chunk's key starts with its map set
    [InlineData("aidyn:chunk/8/0_013")] // a numbered cell has no leading zeros
    [InlineData("aidyn:mapobject/8/C05/017")] // an index has none either
    [InlineData("aidyn:map/0x1A20")] // maps are chunks now
    [InlineData("aidyn:voxel/0x1A20/12")] // and map objects are named by their chunk
    [InlineData("mymod:chunk/Isle")] // a mod's key is a lowercase name
    [InlineData("mymod:chunk/hermit/isle")] // and one segment
    [InlineData("mymod:mapobject/isle")] // a mod's map object needs its index too
    [InlineData("mymod:flag/a/b")] // one segment unless nested
    [InlineData("aidyn:geometry/0x0001")] // a Borg type nothing produces
    public void NonCanonicalIdsAreRejected(string text)
    {
        Assert.False(RecordId.TryParse(text, out _));
    }

    [Fact]
    public void BaseIdsAreBuiltCanonically()
    {
        Assert.Equal("aidyn:scene/0x0A0E", RecordId.Hex(RecordTypes.Scene, 0xA0E).ToString());
        Assert.Equal("aidyn:collision/0x1436", RecordId.Hex(RecordTypes.Collision, 0x1436).ToString());
        Assert.Equal("aidyn:flag/5627", RecordId.Decimal(RecordTypes.Flag, 5627).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordId.Hex(RecordTypes.Scene, 0x10000));
        // The registry's key shape holds for built IDs too.
        Assert.Throws<ArgumentException>(() => RecordId.Decimal(RecordTypes.Entity, 1));
        Assert.Throws<ArgumentException>(() => RecordId.Hex(RecordTypes.Flag, 1));
        Assert.Throws<ArgumentException>(() => RecordId.Hex(RecordTypes.Chunk, 0x1A20));
        Assert.Throws<ArgumentException>(() => RecordId.Decimal(RecordTypes.MapObject, 3));
    }

    [Fact]
    public void ChunkNamesFollowTheRowLetterAndColumnRule()
    {
        // The row's letter (row - 1 + 'A') and the column in two digits.
        Assert.Equal("C05", ChunkRecord.NameOf(3, 5));
        Assert.Equal("A01", ChunkRecord.NameOf(1, 1));
        Assert.Equal("Z100", ChunkRecord.NameOf(26, 100));
        // A row outside 1-26, or a negative column, is its row and column.
        Assert.Equal("0_13", ChunkRecord.NameOf(0, 13));
        Assert.Equal("-1_13", ChunkRecord.NameOf(-1, 13));
        Assert.Equal("27_4", ChunkRecord.NameOf(27, 4));
        Assert.Equal("3_-2", ChunkRecord.NameOf(3, -2));

        foreach (var (row, column) in new[] { (3, 5), (1, 1), (26, 100), (0, 13), (-1, 25), (27, 4), (3, -2) })
            Assert.Equal((row, column), ChunkRecord.CellOf(ChunkRecord.NameOf(row, column)));
        Assert.Null(ChunkRecord.CellOf("hermit_isle"));
        Assert.Null(ChunkRecord.CellOf("C"));

        Assert.Equal("aidyn:chunk/8/C05", ChunkRecord.IdOf(8, 3, 5).ToString());
        Assert.Equal("aidyn:chunk/0/0_13", ChunkRecord.IdOf(0, 0, 13).ToString());
        Assert.Equal("aidyn:chunk/0/-1_13", ChunkRecord.IdOf(RecordId.Parse("aidyn:mapset/0"), -1, 13).ToString());
        Assert.Equal((RecordId.Parse("aidyn:mapset/8"), 3, 5), ChunkRecord.PlaceOf(RecordId.Parse("aidyn:chunk/8/C05")));
        Assert.Equal((RecordId.Parse("aidyn:mapset/0"), -1, 13), ChunkRecord.PlaceOf(RecordId.Parse("aidyn:chunk/0/-1_13")));
        // A mod's chunk key is a name, which says no cell.
        Assert.Null(ChunkRecord.PlaceOf(RecordId.Parse("mymod:chunk/isle")));
    }

    [Fact]
    public void MapObjectIdsNameTheirChunkAndIndex()
    {
        var chunk = RecordId.Parse("aidyn:chunk/8/C05");
        var id = ChunkRecord.ObjectId(chunk, 17);
        Assert.Equal("aidyn:mapobject/8/C05/17", id.ToString());
        Assert.Equal(chunk, ChunkRecord.ChunkOf(id));
        Assert.Equal(17, ChunkRecord.IndexOf(id));

        var mod = RecordId.Parse("mymod:chunk/isle");
        var modObject = ChunkRecord.ObjectId(mod, 3);
        Assert.Equal("mymod:mapobject/isle/3", modObject.ToString());
        Assert.Equal(mod, ChunkRecord.ChunkOf(modObject));
        Assert.Equal(3, ChunkRecord.IndexOf(modObject));

        var negative = ChunkRecord.ObjectId(RecordId.Parse("aidyn:chunk/0/-1_13"), 0);
        Assert.Equal("aidyn:mapobject/0/-1_13/0", negative.ToString());
        Assert.Equal("aidyn:chunk/0/-1_13", ChunkRecord.ChunkOf(negative).ToString());
    }
}
