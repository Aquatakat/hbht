using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

[Collection(ImportedPluginCollection.Name)]
public sealed class MapPlacementImportTests(Na10Import fixture)
{
    [Fact]
    public void EveryMapIsPlacedAndEveryPlacementIsAMap()
    {
        var data = fixture.Data;
        var placements = MapPlacements.For(data.Set);

        // A map's chunks are in its map sets (the battle arenas and one overworld map are in several), and each one a map.
        Assert.All(data.Maps, m => Assert.NotEmpty(placements.All(m.Id)));
        var chunkMaps = data.MapSets.SelectMany(s => s.Chunks.Select(c => c.Map)).ToHashSet();
        Assert.Equal(chunkMaps, data.Maps.Select(m => m.Id).ToHashSet());
        Assert.Equal(data.MapSets.Sum(s => s.Chunks.Count), data.Maps.Sum(m => placements.All(m.Id).Count));
        Assert.Equal(9, data.Maps.Count(m => placements.All(m.Id).Count > 1));

        var i08 = data.MapSets.Single(s => s.Label == "Overworld").Chunks.Single(c => c.Row == 9 && c.Column == 8);
        Assert.Equal("Overworld I08", placements.First(i08.Map)!.ToString());
    }

    [Fact]
    public void EveryMapObjectIsDescribedWhereItIs()
    {
        var data = fixture.Data;
        var catalog = PluginCatalog.Build(data.Set);

        Assert.All(data.Objects, o =>
        {
            Assert.Same(o.Value, MapRecord.Find(data.Set, o.Key));
            Assert.Equal(o.Value.Index, MapRecord.IndexOf(o.Key));
            var text = catalog.Describe(o.Key);
            Assert.DoesNotContain(", in map ", text, StringComparison.Ordinal);
            Assert.Equal($"{catalog.Describe(o.Value)}, in {MapPlacements.For(data.Set).First(MapRecord.MapOf(o.Key))}", text);
        });
    }
}
