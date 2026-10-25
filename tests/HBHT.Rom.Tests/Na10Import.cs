using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Import;

namespace HBHT.Rom.Tests;

/// <summary>The tests that share one import of NA 1.0 (<see cref="Na10Import"/>), and so run one after another.</summary>
[CollectionDefinition(Name)]
public sealed class ImportedPluginCollection : ICollectionFixture<Na10Import>
{
    public const string Name = "NA 1.0 imported";
}

/// <summary>Imports NA 1.0 once for the tests in <see cref="ImportedPluginCollection"/>, when the first test needs it.</summary>
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
        var maps = set.All<MapRecord>(RecordTypes.Map).ToList();
        var chunks = Maps.MapChunks.Read(rom);
        var archive = Borg.BorgArchive.Open(rom);
        return new Loaded
        {
            Dir = dir,
            Set = set,
            Maps = maps,
            MapSets = [.. set.All<MapSetRecord>(RecordTypes.MapSet)],
            Objects = maps.SelectMany(m => m.Objects.Select(o => (Id: MapRecord.VoxelId(m.Id, o.Index), Object: o)))
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
        public required IReadOnlyList<MapRecord> Maps { get; init; }
        public required IReadOnlyList<MapSetRecord> MapSets { get; init; }

        /// <summary>Every map object by its voxel ID.</summary>
        public required IReadOnlyDictionary<RecordId, MapObject> Objects { get; init; }

        public required QuestIndex Index { get; init; }
        public required IReadOnlyList<Maps.MapChunk> Chunks { get; init; }

        /// <summary>The ROM's map objects of each Borg9 the chunk list names.</summary>
        public required IReadOnlyDictionary<int, IReadOnlyList<Maps.VoxelObject>> RomObjects { get; init; }

        public MapRecord Map(string id) => Maps.Single(m => m.Id == RecordId.Parse(id));

        /// <summary>Every object of a type, with its voxel ID and map.</summary>
        public IEnumerable<(RecordId Id, MapRecord Map, T Object)> Of<T>() where T : MapObject =>
            Maps.SelectMany(m => m.Objects.OfType<T>().Select(o => (MapRecord.VoxelId(m.Id, o.Index), m, o)));

        /// <summary>The map set's chunk the reference names, or null.</summary>
        public ChunkEntry? Chunk(ChunkRef chunk) => MapSets.Single(s => s.Id == chunk.MapSet).Chunks
            .SingleOrDefault(c => c.Row == chunk.Row && c.Column == chunk.Column);
    }
}
