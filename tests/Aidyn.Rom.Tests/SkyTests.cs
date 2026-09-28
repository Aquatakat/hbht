using Aidyn.Rom.Borg;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Tests;

public class SkyTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void TheSkyGradientsAreTwelveByThirtySevenImages(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var gradients = Skies.ReadGradients(rom);

        // BORG8_SkyGradient0 to 7, in the order Sky::loadDay numbers them.
        Assert.Equal([0, 0x19F, 0x19E, 0x19A, 0x199, 0x1A0, 0x19D, 0x19B, 0x19C], gradients);
        var archive = BorgArchive.Open(rom);
        foreach (var index in gradients.Skip(1))
        {
            var image = Borg8Image.Parse(archive.Read(index));
            Assert.Equal((Borg8Format.Rgba32, 12, 37), (image.Format, image.Width, image.Height));
        }
    }

    [Fact]
    public void EveryTerrainHasASkyAndTheCaveLikeOnesAreNotDrawn()
    {
        var skies = Enumerable.Range(0, Skies.TerrainCount).Select(Skies.ForTerrain).ToList();
        Assert.All(skies, s => Assert.InRange(s.Sky, 1, 8));
        // I08, where a new game starts, is terrain 2.
        Assert.Equal((1, true), skies[2]);
        Assert.Equal([6, 8, 9, 11, 13, 14, 15, 16], Enumerable.Range(0, Skies.TerrainCount).Where(t => !skies[t].Drawn));
    }
}
