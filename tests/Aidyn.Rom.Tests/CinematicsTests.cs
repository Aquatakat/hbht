using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;

namespace Aidyn.Rom.Tests;

public class CinematicsTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void CinematicsMatchAcrossVersions(RomVersionId id)
    {
        var a = Cinematics.Read(TestRoms.Get(RomVersionId.Na10));
        var b = Cinematics.Read(TestRoms.Get(id));

        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Scenes, b[i].Scenes);
            Assert.Equal(a[i].Fades, b[i].Fades);
            Assert.Equal(a[i].DoneFlag, b[i].DoneFlag);
        }
    }

    /// <summary>
    /// The done flags are the Theatre menu's, in order. The decomp lists five shots for the ending;
    /// the ROM has six.
    /// </summary>
    [Fact]
    public void CinematicsAreTheTheatres()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var cinematics = Cinematics.Read(rom);

        Assert.Equal(GameDb.Read(rom).CinematicTitles.Select(t => t.Flag), cinematics.Select(c => c.DoneFlag));
        Assert.Equal([13, 4, 3, 6], cinematics.Select(c => c.Scenes.Count));
        Assert.Equal([23, 12, 6, 12], cinematics.Select(c => c.Fades.Count));
    }

    /// <summary>Each caption list shows over exactly one shot.</summary>
    [Fact]
    public void ScenesUseEachCaptionListOnce()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var captions = Cinematics.Read(rom).SelectMany(c => c.Scenes).Select(s => s.Captions).OfType<int>();

        Assert.Equal(Enumerable.Range(0, GameDb.Read(rom).Captions.Count), captions.Order());
    }

    [Fact]
    public void ScenesNameTheirAssets()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var archive = BorgArchive.Open(rom);
        var cinematics = Cinematics.Read(rom);

        Assert.All(cinematics.SelectMany(c => c.Scenes), s =>
        {
            Assert.Equal(BorgType.Animation, archive.Listings[s.Animation].Type);
            Assert.Equal(BorgType.Sequence, archive.Listings[s.Audio].Type);
            Assert.InRange(s.Near, 0.025f, 1f);
            Assert.True(s.Far > s.Near);
        });
        Assert.All(cinematics, c => Assert.All(c.Fades, f => Assert.InRange(f.Scene, 0, c.Scenes.Count - 1)));
    }
}
