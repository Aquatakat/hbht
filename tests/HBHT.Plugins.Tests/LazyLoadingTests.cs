using HBHT.Plugins.Records;

namespace HBHT.Plugins.Tests;

/// <summary>
/// The base plugin's chunks and scripts are indexed by their file's path and read when first asked for (ADR 0036), and a chunk's
/// cell is found from its ID without reading it (ADR 0035, <see cref="ChunkIndex"/>).
/// </summary>
public sealed class LazyLoadingTests : IDisposable
{
    private readonly TempPlugins _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Plugin(string id, string[]? dependencies, params (string File, string Json)[] files) => _temp.Add(id, dependencies, files);

    private static RecordId Id(string text) => RecordId.Parse(text);

    private static string Chunk(string id, int set, int row, int column, string objects = "[]", string extra = "") =>
        $$"""{ "id": "{{id}}", "mapSet": "aidyn:mapset/{{set}}", "row": {{row}}, "column": {{column}}, "terrain": "aidyn:terrain/1"{{extra}}, "objects": {{objects}} }""";

    /// <summary>A teleporter with every field the type requires, and these (<paramref name="extra"/>) besides.</summary>
    private static string Door(int index, string name, string extra = "") =>
        $$"""{ "type": "teleporter", "index": {{index}}, "position": [0, 0, 0], "radius": 1, "lockLevel": 0, "trapLevel": 0, "secretLevel": 0, "name": "{{name}}"{{extra}} }""";

    private static readonly string Teleporters = $$"""[{{Door(0, "door", """, "arrival": "aidyn:mapobject/8/C05/1" """)}}]""";

    private string Base(params (string File, string Json)[] more) => Plugin("aidyn", null,
        [
            ("mapsets.json", """[{ "id": "aidyn:mapset/8", "label": "Gwernia", "cellSize": 100 }, { "id": "aidyn:mapset/0", "label": "Overworld", "cellSize": 100 }]"""),
            ("chunks/8/C05.json", Chunk("aidyn:chunk/8/C05", 8, 3, 5, Teleporters)),
            ("chunks/8/D06.json", Chunk("aidyn:chunk/8/D06", 8, 4, 6)),
            ("chunks/0/0_13.json", Chunk("aidyn:chunk/0/0_13", 0, 0, 13)),
            ("scripts/0x0362.json", """{ "id": "aidyn:script/0x0362", "number": 1, "mode": "Conversation", "nodes": [] }"""),
            .. more,
        ]);

    [Fact]
    public void ABaseChunkAndScriptAreIndexedUnread()
    {
        var set = PluginSet.Load([Base()]);
        var chunk = Id("aidyn:chunk/8/C05");
        var script = Id("aidyn:script/0x0362");

        Assert.True(set.Contains(chunk));
        Assert.True(set.Contains(script));
        Assert.False(set.IsRead(chunk));
        Assert.False(set.IsRead(script));
        Assert.Contains(chunk, set.Ids);
        Assert.Equal(["aidyn:chunk/0/0_13", "aidyn:chunk/8/C05", "aidyn:chunk/8/D06"], set.IdsOf(RecordTypes.Chunk).Select(i => i.ToString()).Order(StringComparer.Ordinal));
        Assert.Equal([script], set.IdsOf(RecordTypes.Script));
        Assert.Equal("aidyn", set.DefinedBy(chunk));
        // Asking for the index reads nothing.
        _ = ChunkIndex.For(set).In(Id("aidyn:mapset/8"));
        Assert.All(set.IdsOf(RecordTypes.Chunk), id => Assert.False(set.IsRead(id)));
        Assert.False(set.IsRead(script));
    }

    [Fact]
    public void AskingForARecordReadsItOnce()
    {
        var set = PluginSet.Load([Base()]);
        var chunk = Id("aidyn:chunk/8/C05");

        var read = set.Get<ChunkRecord>(chunk);
        Assert.True(set.IsRead(chunk));
        Assert.Equal(5, read.Column);
        Assert.Same(read, set.Get<ChunkRecord>(chunk));
        Assert.False(set.IsRead(Id("aidyn:chunk/8/D06")));

        Assert.NotNull(set.TryGet<ChunkRecord>(Id("aidyn:chunk/8/D06")));
        Assert.True(set.IsRead(Id("aidyn:chunk/8/D06")));

        var script = Id("aidyn:script/0x0362");
        Assert.NotNull(set.GetJson(script));
        Assert.True(set.IsRead(script));

        var other = PluginSet.Load([Base()]);
        Assert.Equal(3, other.All<ChunkRecord>().Count);
        Assert.All(other.IdsOf(RecordTypes.Chunk), id => Assert.True(other.IsRead(id)));

        var validated = PluginSet.Load([Base()]);
        Assert.True(validated.Validate().IsClean);
        Assert.All(validated.IdsOf(RecordTypes.Chunk), id => Assert.True(validated.IsRead(id)));
        Assert.True(validated.IsRead(script));
    }

    [Fact]
    public void AFileWhosePathIsNoIdIsReadLikeAnyOther()
    {
        var set = PluginSet.Load([Base(("chunks/notes.json", Chunk("aidyn:chunk/8/E07", 8, 5, 7)))]);
        var id = Id("aidyn:chunk/8/E07");
        Assert.True(set.IsRead(id));
        Assert.Equal(7, set.Get<ChunkRecord>(id).Column);
        // Nor does a plugin that isn't the base plugin get the lazy folders.
        var mod = PluginSet.Load([Base(), Plugin("mymod", ["aidyn"], ("chunks/isle.json", Chunk("mymod:chunk/isle", 8, 9, 9)))]);
        Assert.True(mod.IsRead(Id("mymod:chunk/isle")));
        Assert.False(mod.IsRead(Id("aidyn:chunk/8/C05")));
    }

    [Fact]
    public void ALazyFileHoldsItsPathsRecordOrReadingItFails()
    {
        var set = PluginSet.Load([Base(("chunks/8/E07.json", Chunk("aidyn:chunk/8/F08", 8, 6, 8)))]);
        var wrong = Id("aidyn:chunk/8/E07");
        Assert.True(set.Contains(wrong));
        Assert.Throws<InvalidDataException>(() => set.GetJson(wrong));
        Assert.Throws<InvalidDataException>(() => set.Get<ChunkRecord>(wrong));
    }

    [Fact]
    public void ValidateReportsALazyFileThatHoldsAnotherRecord()
    {
        var set = PluginSet.Load([Base(("chunks/8/E07.json", Chunk("aidyn:chunk/8/F08", 8, 6, 8)))]);
        var wrong = Id("aidyn:chunk/8/E07");
        var problem = Assert.Single(set.Validate().Problems);
        Assert.Equal(wrong, problem.Record);
        Assert.Equal("aidyn", problem.Plugin);
        Assert.Contains("its path names aidyn:chunk/8/E07", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModsPatchReadsALazyRecordAsItLoads()
    {
        var chunk = Id("aidyn:chunk/8/C05");
        var set = PluginSet.Load([
            Base(),
            Plugin("one", ["aidyn"], ("c.json", """{ "patch": "aidyn:chunk/8/C05", "terrain": "aidyn:terrain/2", "objects": [{ "type": "savePoint", "index": 5, "position": [1, 0, 1], "radius": 1 }] }""")),
            Plugin("two", ["aidyn"], ("c.json", """{ "patch": "aidyn:chunk/8/C05", "terrain": "aidyn:terrain/3", "objects": [{ "index": 0, "name": "gate" }] }""")),
        ]);

        // The patched record is read; the ones nobody patched are not.
        Assert.True(set.IsRead(chunk));
        Assert.False(set.IsRead(Id("aidyn:chunk/8/D06")));
        var read = set.Get<ChunkRecord>(chunk);
        Assert.Equal("aidyn:terrain/3", read.Terrain.ToString());
        Assert.Equal([0, 5], read.Objects.Select(o => o.Index));
        Assert.Equal("gate", Assert.IsType<TeleporterObject>(read.Objects[0]).Name);

        // The merge and the report work as for any record: the definer set the fields, and both mods set the terrain.
        var conflict = Assert.Single(set.Conflicts);
        Assert.Equal(chunk, conflict.Record);
        Assert.Equal("terrain", conflict.Path);
        Assert.Equal(["one", "two"], conflict.Plugins);
        Assert.Equal(["aidyn", "one", "two"], set.Setters(chunk, "terrain"));
        Assert.Equal(["aidyn"], set.Setters(chunk, "row"));
        Assert.Equal(["one", "two"], set.Patchers(chunk));
        // And a lazy record nobody patched has its definer for every field it holds.
        Assert.Equal(["aidyn"], set.Setters(Id("aidyn:chunk/8/D06"), "terrain"));
        Assert.Empty(set.Setters(Id("aidyn:chunk/8/D06"), "shows"));
    }

    [Fact]
    public void APatchToARecordNothingDefinesIsStillRefused()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            PluginSet.Load([Base(), Plugin("mymod", ["aidyn"], ("c.json", """{ "patch": "aidyn:chunk/8/Z26", "terrain": "aidyn:terrain/2" }"""))]));
        Assert.Contains("aidyn:chunk/8/Z26", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIndexFindsABaseChunksCellFromItsIdWithoutReadingIt()
    {
        var set = PluginSet.Load([Base()]);
        var index = ChunkIndex.For(set);
        var gwernia = Id("aidyn:mapset/8");

        Assert.Equal(Id("aidyn:chunk/8/C05"), index.At(gwernia, 3, 5));
        Assert.Equal(new ChunkCell(Id("aidyn:mapset/0"), 0, 13), index.CellOf(Id("aidyn:chunk/0/0_13")));
        Assert.Equal([Id("aidyn:chunk/8/C05"), Id("aidyn:chunk/8/D06")], index.In(gwernia));
        Assert.Null(index.At(gwernia, 9, 9));
        Assert.All(set.IdsOf(RecordTypes.Chunk), id => Assert.False(set.IsRead(id)));

        // Reading the chunk at a cell is what ChunkAt does.
        Assert.Equal(4, set.ChunkAt(gwernia, 4, 6)!.Row);
        Assert.True(set.IsRead(Id("aidyn:chunk/8/D06")));
        Assert.False(set.IsRead(Id("aidyn:chunk/8/C05")));
        Assert.Equal([3, 4], set.ChunksIn(gwernia).Select(c => c.Row));
    }

    [Fact]
    public void AModsChunkIsFoundByItsFields()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("isle.json", Chunk("mymod:chunk/hermit_isle", 8, 9, 9, "[{ \"type\": \"savePoint\", \"index\": 3, \"position\": [1, 0, 1], \"radius\": 1 }]"))),
        ]);
        var index = ChunkIndex.For(set);
        var isle = Id("mymod:chunk/hermit_isle");

        Assert.Equal(isle, index.At(Id("aidyn:mapset/8"), 9, 9));
        Assert.Equal(new ChunkCell(Id("aidyn:mapset/8"), 9, 9), index.CellOf(isle));
        Assert.Equal([Id("aidyn:chunk/8/C05"), Id("aidyn:chunk/8/D06"), isle], index.In(Id("aidyn:mapset/8")));
        Assert.Equal("mymod:mapobject/hermit_isle/3", ChunkRecord.ObjectId(isle, 3).ToString());
        Assert.IsType<SavePointObject>(ChunkRecord.Find(set, ChunkRecord.ObjectId(isle, 3)));
        Assert.True(set.Validate().IsClean);
    }

    [Fact]
    public void TwoChunksClaimingACellTheEarlierPluginHoldsIt()
    {
        var set = PluginSet.Load([
            Base(),
            Plugin("mymod", ["aidyn"], ("isle.json", Chunk("mymod:chunk/usurper", 8, 3, 5))),
        ]);
        var index = ChunkIndex.For(set);
        Assert.Equal(Id("aidyn:chunk/8/C05"), index.At(Id("aidyn:mapset/8"), 3, 5));
        Assert.Null(index.CellOf(Id("mymod:chunk/usurper")));
        var (loser, holder) = Assert.Single(index.Clashes);
        Assert.Equal(Id("mymod:chunk/usurper"), loser);
        Assert.Equal(Id("aidyn:chunk/8/C05"), holder);

        var problem = Assert.Single(set.Validate().Problems);
        Assert.Equal(Id("mymod:chunk/usurper"), problem.Record);
        Assert.Equal("mymod", problem.Plugin);
        Assert.Equal("It claims the cell aidyn:chunk/8/C05 already has.", problem.Message);
    }

    [Fact]
    public void ABaseChunkWhoseFieldsDisagreeWithItsIdIsAProblem()
    {
        var set = PluginSet.Load([Base(("chunks/8/E07.json", Chunk("aidyn:chunk/8/E07", 8, 5, 8)))]);
        var problem = Assert.Single(set.Validate().Problems);
        Assert.Equal(Id("aidyn:chunk/8/E07"), problem.Record);
        Assert.Contains("A base chunk's ID names its cell", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATeleporterNeedsADestinationOrAnArrivalInTheSameChunk()
    {
        var objects = "[" + string.Join(", ",
            Door(0, "neither"),
            Door(1, "agree", """, "destination": "aidyn:chunk/8/C05", "arrival": "aidyn:mapobject/8/C05/1" """),
            Door(2, "disagree", """, "destination": "aidyn:chunk/8/D06", "arrival": "aidyn:mapobject/8/C05/1" """),
            Door(3, "destination", """, "destination": "aidyn:chunk/8/D06" """)) + "]";
        var set = PluginSet.Load([Base(("chunks/8/E07.json", Chunk("aidyn:chunk/8/E07", 8, 5, 7, objects)))]);
        var problems = set.Validate().Problems;
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Path == "objects[0]" && p.Message == "A teleporter needs a destination or an arrival.");
        Assert.Contains(problems, p => p.Path == "objects[2]"
            && p.Message == "The teleporter's arrival is in aidyn:chunk/8/C05, not its destination aidyn:chunk/8/D06.");
    }

    [Fact]
    public void ATeleporterLeadsToItsArrivalsChunkOrItsDestination()
    {
        static TeleporterObject Door(RecordId? destination, RecordId? arrival) =>
            new(0, default, 1, null, destination, arrival, null, 0, null, null, 0, null, null, null, 0, "door");
        Assert.Equal(Id("aidyn:chunk/8/D06"), Door(null, ChunkRecord.ObjectId(Id("aidyn:chunk/8/D06"), 2)).Leads);
        Assert.Equal(Id("aidyn:chunk/8/C05"), Door(Id("aidyn:chunk/8/C05"), null).Leads);
        Assert.Null(Door(null, null).Leads);
    }
}
