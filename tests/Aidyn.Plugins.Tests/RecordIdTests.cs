namespace Aidyn.Plugins.Tests;

public class RecordIdTests
{
    [Theory]
    [InlineData("aidyn:entity/0x023F")]
    [InlineData("aidyn:flag/47")]
    [InlineData("aidyn:flag/0")]
    [InlineData("aidyn:voxel/0x1A20/12")]
    [InlineData("aidyn:mapset/16")]
    [InlineData("mymod:flag/met_the_hermit")]
    [InlineData("my_mod2:voxel/camp/3")]
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
    [InlineData("aidyn:voxel/0x1A20/0x0001")] // hex only in the first segment
    [InlineData("aidyn/flag/1")]
    [InlineData("aidyn:entity/47")] // a DB record is keyed by its ItemID
    [InlineData("aidyn:flag/0x002F")] // a flag by a decimal
    [InlineData("aidyn:voxel/0x1A20")] // a map object needs its index
    [InlineData("aidyn:map/0x1A20/3")] // a map has none
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
        Assert.Equal("aidyn:voxel/0x1A20/12", RecordId.Hex(RecordTypes.Voxel, 0x1A20, 12).ToString());
        Assert.Equal("aidyn:flag/5627", RecordId.Decimal(RecordTypes.Flag, 5627).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordId.Hex(RecordTypes.Scene, 0x10000));
        // The registry's key shape holds for built IDs too.
        Assert.Throws<ArgumentException>(() => RecordId.Decimal(RecordTypes.Entity, 1));
        Assert.Throws<ArgumentException>(() => RecordId.Hex(RecordTypes.Flag, 1));
        Assert.Throws<ArgumentException>(() => RecordId.Hex(RecordTypes.Voxel, 0x1A20));
        Assert.Throws<ArgumentException>(() => RecordId.Hex(RecordTypes.Map, 0x1A20, 3));
    }
}
