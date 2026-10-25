using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Engine;
using HBHT.Rom.Import;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

public class WeatherImportTests
{
    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheWeathersTablesReadTheSameInEveryVersion(RomVersionId id)
    {
        var tables = WeatherTables.Read(TestRoms.Get(id));
        Assert.Equal([43, 48, 33, 25, 36, 40], tables.Seasons);
        Assert.Equal([0, 0, 0, -100, 5, 30, 0, 15, 0, 0, 20, 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 0, 0, 30, 15, 0, -100, -100], tables.Terrains);
        Assert.Equal(28, tables.Scales.Count);
        Assert.Equal([0.5f, 0.5f, 0.4f, 0f, 0.7f, 0.6f, 0.5f, 0.8f], tables.Scales.Take(8));
        Assert.Equal([0.8f, 0.2f, 0.2f, 0.2f, 0.2f, 0.8f, 0.6f, 0.2f, 0f, 0f], tables.Scales.Skip(18));
        Assert.All(tables.Scales.Skip(8).Take(10), s => Assert.Equal(0.2f, s));
    }

    [Fact]
    public void TheTerrainsCarryTheWeatherAndOnlyTypeFiveSnows()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var terrains = WorldImporter.BuildTerrains(Skies.ReadGradients(rom), CampFactors.Read(rom), WeatherTables.Read(rom));
        Assert.Equal(Skies.TerrainCount, terrains.Count);
        Assert.Equal([5], Enumerable.Range(0, terrains.Count).Where(t => terrains[t].Snow));
        Assert.Equal((30, 0.6f), (terrains[5].Precipitation, terrains[5].PrecipitationScale));
        Assert.Equal((100, 0.8f), (terrains[18].Precipitation, terrains[18].PrecipitationScale));
        var weather = WorldImporter.BuildWeather(WeatherTables.Read(rom), Skies.ReadGradients(rom));
        Assert.Equal(150f / 648, weather.WindMaxSpeed, 6);
        // Sky 2, Borg8 0x19E, the Battlefields' sky.
        Assert.Equal(RecordId.Hex(RecordTypes.Image, 0x19E), weather.RainSky);
        Assert.Equal(terrains[18].Sky, weather.RainSky);
        Assert.Equal(1.5f / 648, weather.WindGust, 6);
    }

    [Fact]
    public void RainAndSnowDrawWithTheParticleTexturesTheirSlotsName()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var weather = WorldImporter.BuildWeather(WeatherTables.Read(rom), Skies.ReadGradients(rom), ParticleTextures.Read(rom));
        // Rain's slots 4 and 16, a streak and a ripple; snow's 0 for both, a soft disc.
        Assert.Equal((RecordId.Hex(RecordTypes.Texture, 2), RecordId.Hex(RecordTypes.Texture, 12)), (weather.Rain!.Drop, weather.Rain.Splash));
        Assert.Equal((RecordId.Hex(RecordTypes.Texture, 3), RecordId.Hex(RecordTypes.Texture, 3)), (weather.Snow!.Drop, weather.Snow.Splash));
        Assert.Equal(new WeatherColor(0.546875f, 0.7421875f, 0.8203125f, 0.8f), weather.Rain.Color);
        Assert.True(weather.Rain.Splashes);
        Assert.False(weather.Snow.Splashes);
    }

    [Theory]
    [MemberData(nameof(TestRoms.Checkpoints), MemberType = typeof(TestRoms))]
    public void ACheckpointsWeatherAgreesWithItsRainFlags(string name)
    {
        if (name.Length == 0)
            Assert.Skip($"No checkpoints in {TestRoms.Folder}.");
        var checkpoint = AresCheckpoint.Read(Path.Combine(TestRoms.Folder, name));
        var weather = checkpoint.Weather;
        Assert.Equal(weather.Precipitation, (checkpoint.Flags[2319] ? 1 : 0) + (checkpoint.Flags[2320] ? 2 : 0));
        Assert.InRange(weather.Precipitation, 0, 2);
        Assert.InRange(weather.Falling, 0, 2);
        Assert.InRange(weather.Intensity, 0, 1);
        Assert.InRange(weather.Fog, 0, 0.75f);
        Assert.InRange(weather.Storm, 0, weather.Intensity);
        Assert.InRange(weather.Wind.Length(), 0, 150f / 648 + 1e-6f);
        Assert.InRange(weather.FallIntensity, 0, 1);
        Assert.InRange(weather.FogTime, 0, 0.75f);
    }
}
