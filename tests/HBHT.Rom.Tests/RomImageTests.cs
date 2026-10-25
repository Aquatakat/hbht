using System.Text;

namespace HBHT.Rom.Tests;

public class RomImageTests
{
    /// <summary>A minimal image that passes byte-order detection but matches no known version.</summary>
    private static byte[] FakeRom()
    {
        var rom = new byte[0x1000];
        rom[0] = 0x80; rom[1] = 0x37; rom[2] = 0x12; rom[3] = 0x40;
        rom[0x08] = 0x80; rom[0x09] = 0x00; rom[0x0A] = 0x04; rom[0x0B] = 0x00;
        Encoding.ASCII.GetBytes("AIDYN CHRONICLES    ").CopyTo(rom, 0x20);
        Encoding.ASCII.GetBytes("NAYE").CopyTo(rom, 0x3B);
        rom[0x3F] = 1;
        return rom;
    }

    [Theory]
    [InlineData(new byte[] { 0x80, 0x37, 0x12, 0x40 }, RomByteOrder.BigEndian)]
    [InlineData(new byte[] { 0x37, 0x80, 0x40, 0x12 }, RomByteOrder.ByteSwapped)]
    [InlineData(new byte[] { 0x40, 0x12, 0x37, 0x80 }, RomByteOrder.LittleEndian)]
    public void DetectsByteOrderFromFirstWord(byte[] firstWord, RomByteOrder expected) =>
        Assert.Equal(expected, RomByteOrders.Detect(firstWord));

    [Fact]
    public void RejectsNonRom() =>
        Assert.Throws<InvalidDataException>(() => RomByteOrders.Detect([0x50, 0x4B, 0x03, 0x04]));

    [Theory]
    [InlineData(RomByteOrder.ByteSwapped, new byte[] { 2, 1, 4, 3, 6, 5, 8, 7 })]
    [InlineData(RomByteOrder.LittleEndian, new byte[] { 4, 3, 2, 1, 8, 7, 6, 5 })]
    public void SwapsAndSwapsBack(RomByteOrder order, byte[] swapped)
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        RomByteOrders.Swap(data, order);
        Assert.Equal(swapped, data);
        RomByteOrders.Swap(data, order);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], data);
    }

    [Fact]
    public void ParsesHeader()
    {
        var header = RomHeader.Parse(FakeRom());
        Assert.Equal("AIDYN CHRONICLES", header.Name);
        Assert.Equal("NAYE", header.GameCode);
        Assert.Equal(1, header.Revision);
        Assert.Equal(0x80000400u, header.EntryPoint);
    }

    [Theory]
    [InlineData(RomByteOrder.BigEndian)]
    [InlineData(RomByteOrder.ByteSwapped)]
    [InlineData(RomByteOrder.LittleEndian)]
    public void RefusesUnknownRomWithItsHash(RomByteOrder order)
    {
        var rom = FakeRom();
        RomByteOrders.Swap(rom, order);
        var e = Assert.Throws<UnsupportedRomException>(() => RomImage.FromBytes(rom));
        Assert.Matches("^[0-9a-f]{40}$", e.Sha1);
        Assert.Contains(e.Sha1, e.Message);
        Assert.Equal("NAYE", e.Header.GameCode);
    }

    [Theory]
    [InlineData(RomVersionId.Na10, "NAYE", 0)]
    [InlineData(RomVersionId.Na11, "NAYE", 1)]
    [InlineData(RomVersionId.Debug, "", 0)]
    public void IdentifiesKnownRoms(RomVersionId id, string gameCode, byte revision)
    {
        var rom = TestRoms.Get(id);
        Assert.Equal(id, rom.Version.Id);
        Assert.Equal(gameCode, rom.Header.GameCode);
        Assert.Equal(revision, rom.Header.Revision);
        Assert.Equal(id == RomVersionId.Debug, rom.Version.ResearchOnly);
    }

    [Theory]
    [InlineData(RomByteOrder.ByteSwapped)]
    [InlineData(RomByteOrder.LittleEndian)]
    public void IdentifiesSwappedDumps(RomByteOrder order)
    {
        var bytes = TestRoms.Get(RomVersionId.Na10).Data.ToArray();
        RomByteOrders.Swap(bytes, order);

        var rom = RomImage.FromBytes(bytes);
        Assert.Equal(RomVersionId.Na10, rom.Version.Id);
        Assert.Equal(order, rom.OriginalByteOrder);
    }

    [Fact]
    public void RefusesModifiedRom()
    {
        var bytes = TestRoms.Get(RomVersionId.Na10).Data.ToArray();
        bytes[0x1000000] ^= 0xFF;

        var e = Assert.Throws<UnsupportedRomException>(() => RomImage.FromBytes(bytes));
        Assert.NotEqual(RomVersions.Na10.Sha1, e.Sha1);
    }
}
