using Aidyn.Rom.Borg;

namespace Aidyn.Rom.Tests;

public class BorgArchiveTests
{
    private static readonly Dictionary<BorgType, int> ExpectedTypeCounts = new()
    {
        [BorgType.Texture] = 2183,
        [BorgType.Geometry] = 5636,
        [BorgType.Camera] = 5,
        [BorgType.Light] = 507,
        [BorgType.Scene] = 1402,
        [BorgType.Animation] = 1988,
        [BorgType.Actor] = 203,
        [BorgType.UiImage] = 560,
        [BorgType.MapChunk] = 584,
        [BorgType.Sample] = 149,
        [BorgType.Sequence] = 476,
        [BorgType.Dialogue] = 892,
    };

    [Theory]
    [InlineData(RomVersionId.Na10)]
    [InlineData(RomVersionId.Na11)]
    [InlineData(RomVersionId.Debug)]
    public void IndexHasExpectedShape(RomVersionId id)
    {
        var archive = BorgArchive.Open(TestRoms.Get(id));

        Assert.Equal(14585, archive.Count);
        Assert.Equal(ExpectedTypeCounts, archive.Listings.GroupBy(l => l.Type).ToDictionary(g => g.Key, g => g.Count()));
        Assert.DoesNotContain(archive.Listings, l => l.Compression == BorgCompression.Lz01x);
        Assert.All(archive.Listings, l => Assert.Equal(0, l.Offset % 16));
    }

    /// <summary>
    /// The main correctness check for LZB: every compressed asset must consume its stream
    /// exactly and produce exactly its listed size.
    /// </summary>
    [Fact]
    public void EveryBorgDecompressesToItsListedSize()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        long total = 0;
        for (var i = 0; i < archive.Count; i++)
        {
            var data = archive.Read(i);
            Assert.Equal(archive.Listings[i].UncompressedSize, data.Length);
            total += data.Length;
        }
        Assert.Equal(archive.Listings.Sum(l => (long)l.UncompressedSize), total);
    }

    /// <summary>The rig found the archive shared byte for byte between versions (ADR 0007).</summary>
    [Theory]
    [InlineData(RomVersionId.Na11)]
    [InlineData(RomVersionId.Debug)]
    public void ArchiveMatchesNa10(RomVersionId id)
    {
        var na10 = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var other = BorgArchive.Open(TestRoms.Get(id));

        Assert.Equal(na10.Listings, other.Listings);
        for (var i = 0; i < na10.Count; i++)
            Assert.True(na10.ReadStored(i).SequenceEqual(other.ReadStored(i)), $"Borg {i} differs");
    }
}
