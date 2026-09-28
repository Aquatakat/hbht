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

    [Theory]
    [MemberData(nameof(Versions))]
    public void TheSunMoonAndStarsReadTheSameInEveryVersion(RomVersionId id)
    {
        var objects = Skies.ReadSkyObjects(TestRoms.Get(id));
        Assert.Equal(Skies.ReadSkyObjects(TestRoms.Get(RomVersionId.Na10)), objects);

        // The table's range is exactly its entries, 91 stars, the sun and the moon, and the end entry.
        Assert.Equal(93, objects.Count);
        Assert.Equal(91, objects.Count(o => o.Kind == SkyObjectKind.Star));
        Assert.Equal(new SkyObjectEntry(SkyObjectKind.Sun, 1f, 0f, 90f, new Maps.Rgba(250, 250, 250, 250)), objects.Single(o => o.Kind == SkyObjectKind.Sun));
        var moon = objects.Single(o => o.Kind == SkyObjectKind.Moon);
        Assert.Equal((1f, 0f, 0f), (moon.Scale, moon.Height, moon.Azimuth));
        Assert.All(objects.Where(o => o.Kind == SkyObjectKind.Star), o => Assert.Equal(0.2f, o.Scale));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void TheCloudsReadTheSameInEveryVersion(RomVersionId id)
    {
        var clouds = Skies.ReadClouds(TestRoms.Get(id));
        Assert.Equal(Skies.ReadClouds(TestRoms.Get(RomVersionId.Na10)), clouds);

        Assert.Equal(55, clouds.Count);
        Assert.Equal(new CloudEntry(0, -530f, -530f, 255, 0.2f), clouds[0]);
        Assert.All(clouds, c => Assert.InRange(c.Image, 0, 2));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void TheLensFlareReadsTheSameInEveryVersion(RomVersionId id)
    {
        var pieces = Skies.ReadLensFlare(TestRoms.Get(id));
        Assert.Equal(Skies.ReadLensFlare(TestRoms.Get(RomVersionId.Na10)), pieces);

        Assert.Equal(8, pieces.Count);
        Assert.Equal(new FlareEntry(0, 1f, 5.02f, new Maps.Rgba(250, 250, 250, 50)), pieces[0]);
        Assert.All(pieces, p => Assert.InRange(p.Image, 0, 2));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void EveryImageTheSkyDrawsWithIsABorg8(RomVersionId id)
    {
        var archive = BorgArchive.Open(TestRoms.Get(id));
        var moon = Skies.MoonImages;
        int[] images =
        [
            Skies.SunImage, Skies.StarImage, .. Skies.CloudImages, .. Skies.FlareImages,
            moon.New, moon.WaxingCrescent, moon.WaningCrescent, moon.FirstQuarter, moon.ThirdQuarter, moon.Full,
        ];
        Assert.All(images, i => Assert.Equal(BorgType.UiImage, archive.Listings[i].Type));
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
