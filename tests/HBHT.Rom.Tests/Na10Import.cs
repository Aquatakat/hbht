using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Import;

[assembly: AssemblyFixture(typeof(HBHT.Rom.Tests.Na10Import))]

namespace HBHT.Rom.Tests;

/// <summary>
/// Imports NA 1.0 once for the whole assembly, when the first test needs it. Any test class takes it in its constructor; the classes still
/// run side by side, so what they read of it must only be read (the <see cref="PluginSet"/> reads its lazy records under a lock).
/// </summary>
public sealed class Na10Import : IDisposable
{
    private readonly TempPlugins _temp = new();
    private readonly Lazy<Loaded> _loaded;

    public Na10Import() => _loaded = new(Load);

    public Loaded Data => _loaded.Value;

    public void Dispose() => _temp.Dispose();

    private Loaded Load()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var dir = Path.Combine(_temp.Root, BasePlugin.Folder);
        BaseImporter.Import(rom, dir);
        var set = PluginSet.Load([dir]);
        var maps = set.All<ChunkRecord>(RecordTypes.Chunk).ToList();
        var chunks = Maps.MapChunks.Read(rom);
        var archive = Borg.BorgArchive.Open(rom);
        return new Loaded
        {
            Dir = dir,
            Set = set,
            ChunkRecords = maps,
            MapSets = [.. set.All<MapSetRecord>(RecordTypes.MapSet)],
            Objects = maps.SelectMany(m => m.Objects.Select(o => (Id: m.ObjectId(o.Index), Object: o)))
                .ToDictionary(o => o.Id, o => o.Object),
            Index = QuestIndex.Build(set),
            Chunks = chunks,
            RomObjects = chunks.Select(c => c.Collision).Distinct().Order()
                .ToDictionary(b => b, b => Maps.Borg9Voxels.Read(archive.Read(b))),
        };
    }

    public sealed class Loaded
    {
        public required string Dir { get; init; }
        public required PluginSet Set { get; init; }
        /// <summary>Every chunk record (ADR 0035), read.</summary>
        public required IReadOnlyList<ChunkRecord> ChunkRecords { get; init; }
        public required IReadOnlyList<MapSetRecord> MapSets { get; init; }

        /// <summary>Every map object by its ID.</summary>
        public required IReadOnlyDictionary<RecordId, MapObject> Objects { get; init; }

        public required QuestIndex Index { get; init; }
        public required IReadOnlyList<Maps.MapChunk> Chunks { get; init; }

        /// <summary>The ROM's map objects of each Borg9 the chunk list names.</summary>
        public required IReadOnlyDictionary<int, IReadOnlyList<Maps.VoxelObject>> RomObjects { get; init; }

        /// <summary>The chunk record with an ID, such as <c>aidyn:chunk/0/I08</c>.</summary>
        public ChunkRecord Chunk(string id) => ChunkRecords.Single(m => m.Id == RecordId.Parse(id));

        /// <summary>The chunk record with an ID.</summary>
        public ChunkRecord Chunk(RecordId id) => ChunkRecords.Single(m => m.Id == id);

        /// <summary>Every object of a type, with its ID and chunk.</summary>
        public IEnumerable<(RecordId Id, ChunkRecord Map, T Object)> Of<T>() where T : MapObject =>
            ChunkRecords.SelectMany(m => m.Objects.OfType<T>().Select(o => (m.ObjectId(o.Index), m, o)));
    }
}

/// <summary>
/// The Battle arenas by the original's numbers, which records name by their chunks (ADR 0035): arena n is the nth Battlefields chunk in the
/// ROM's table order (<c>Zones.ArenaChunks</c>), 0 A01, 1 A03, 2 C01 and so on.
/// </summary>
public static class Battlefield
{
    private static readonly Lazy<IReadOnlyList<RecordId>> All = new(() =>
        HBHT.Rom.Engine.Arenas.Chunks(HBHT.Rom.Maps.MapChunks.Read(TestRoms.Get(RomVersionId.Na10))));

    /// <summary>Arena <paramref name="number"/>'s chunk.</summary>
    public static RecordId Chunk(int number) => All.Value[number];

    /// <summary>The number of the arena in <paramref name="chunk"/>.</summary>
    public static int Number(RecordId chunk) => All.Value.ToList().IndexOf(chunk);
}
