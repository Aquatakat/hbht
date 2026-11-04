using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>Where NA 1.0's chunks are, and what a map object is called there (ADR 0035).</summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class ChunkPlacementImportTests(Na10Import fixture)
{
    [Fact]
    public void EveryChunkIsInItsCellAndItsIdNamesTheCell()
    {
        var data = fixture.Data;
        var index = ChunkIndex.For(data.Set);

        Assert.Equal(680, data.ChunkRecords.Count);
        Assert.Empty(index.Clashes);
        Assert.All(data.ChunkRecords, chunk =>
        {
            Assert.Equal(chunk.Id, index.At(chunk.MapSet, chunk.Row, chunk.Column));
            Assert.Equal(new ChunkCell(chunk.MapSet, chunk.Row, chunk.Column), index.CellOf(chunk.Id));
            // The ID alone says it, which is how the plugin set finds a chunk without reading its file.
            Assert.Equal((chunk.MapSet, chunk.Row, chunk.Column), ChunkRecord.PlaceOf(chunk.Id));
            Assert.Equal(chunk.Id, ChunkRecord.IdOf(chunk.MapSet, chunk.Row, chunk.Column));
        });
        Assert.Equal(680, data.MapSets.Sum(s => index.In(s.Id).Count));
        Assert.Equal(543, data.MapSets.Sum(s => index.In(s.Id).Count(id => data.Chunk(id).Scene is not null || data.Chunk(id).AltScene is not null)));

        var i08 = data.Set.ChunkAt(RecordId.Decimal(RecordTypes.MapSet, 0), 9, 8)!;
        Assert.Equal(Ids.Chunk(0, "I08"), i08.Id);
        Assert.Equal("Overworld I08", ChunkPlace.Of(data.Set, i08.Id)!.ToString());
    }

    [Fact]
    public void TheSeaNorthOfTheOverworldIsNamedByNumbers()
    {
        // The importer's sea past row A has no letter: 0_13 to 0_17 and 0_19 to 0_25, and the same in row -1.
        var numbered = fixture.Data.ChunkRecords.Where(c => c.Id.Key.Split('/')[1].Contains('_', StringComparison.Ordinal)).ToList();
        Assert.Equal(24, numbered.Count);
        Assert.All(numbered, c => Assert.Equal("aidyn:mapset/0", c.MapSet.ToString()));
        Assert.Equal(Enumerable.Range(13, 5).Concat(Enumerable.Range(19, 7)).Select(c => $"0/-1_{c}").Concat(Enumerable.Range(13, 5).Concat(Enumerable.Range(19, 7)).Select(c => $"0/0_{c}")).Order(StringComparer.Ordinal),
            numbered.Select(c => c.Id.Key).Order(StringComparer.Ordinal));
        Assert.Equal((-1, 13), (numbered.Single(c => c.Id.Key == "0/-1_13").Row, numbered.Single(c => c.Id.Key == "0/-1_13").Column));
    }

    [Fact]
    public void TheNineSharedBorg9sAreCopiedIntoEachOfTheirCells()
    {
        var shared = fixture.Data.ChunkRecords.GroupBy(c => c.Borg).Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key!, g => string.Join(' ', g.Select(c => c.Id.Key).Order(StringComparer.Ordinal)));
        Assert.Equal(new Dictionary<string, string>
        {
            ["0x3559"] = string.Join(' ', fixture.Data.ChunkRecords.Where(c => c.Borg == "0x3559").Select(c => c.Id.Key).Order(StringComparer.Ordinal)),
            ["0x0E87"] = "16/A01 16/A03",
            ["0x0E88"] = "16/C01 16/C03 16/G01 16/G03 16/K11",
            ["0x0E89"] = "16/E01 16/E03",
            ["0x0E8A"] = "16/I01 16/I03 16/I05 16/I07",
            ["0x3629"] = "16/K01 16/K03",
            ["0x363B"] = "16/A11 16/A13",
            ["0x3675"] = "16/E11 16/M17",
            ["0x3672"] = "16/M15 16/M21",
        }, shared);
        var sea = fixture.Data.ChunkRecords.Where(c => c.Borg == "0x3559").ToList();
        Assert.Equal(141, sea.Count);
        Assert.Contains(sea, c => c.Id == Ids.Chunk(0, "L23"));
        Assert.Contains(sea, c => c.Id == Ids.Chunk(0, "S17"));
        Assert.Contains(sea, c => c.Id == Ids.Chunk(0, "S18"));
        Assert.Contains(sea, c => c.Id == Ids.Chunk(0, "T17"));
        // Each copy's collision is its Borg9's.
        Assert.All(fixture.Data.ChunkRecords, c => Assert.Equal(RecordId.Parse("aidyn:collision/" + c.Borg), c.Collision));
        Assert.Equal(8, fixture.Data.MapSets.Count(s => s.Minimap is not null));
    }

    [Fact]
    public void EveryMapObjectIsDescribedWhereItIs()
    {
        var data = fixture.Data;
        var catalog = PluginCatalog.Build(data.Set);

        Assert.All(data.Objects, o =>
        {
            Assert.Same(o.Value, ChunkRecord.Find(data.Set, o.Key));
            Assert.Equal(o.Value.Index, ChunkRecord.IndexOf(o.Key));
            var text = catalog.Describe(o.Key);
            Assert.DoesNotContain(", in chunk ", text, StringComparison.Ordinal);
            Assert.Equal($"{catalog.Describe(o.Value)}, in {ChunkPlace.Of(data.Set, ChunkRecord.ChunkOf(o.Key))}", text);
        });
    }
}
