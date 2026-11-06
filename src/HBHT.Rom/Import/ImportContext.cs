using System.Numerics;
using System.Text.Json;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Assets;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;
using HBHT.Rom.Graphics;
using HBHT.Rom.Maps;
using HBHT.Rom.Quests;

namespace HBHT.Rom.Import;

/// <summary>
/// What one import shares: the ROM, its archive and DB, each code-segment table read once on first use, and the
/// staging folder the records and assets are written to. The importers of each family (<see cref="BaseImporter"/>
/// runs them) build from this and write through it. The tables are read on the importing thread, before any
/// parallel work that uses them.
/// </summary>
internal sealed class ImportContext(RomImage rom, string staging, IProgress<string>? progress = null, ImportOptions? options = null)
{
    public RomImage Rom { get; } = rom;

    /// <summary>The player's choices for the import, such as whether to move the Roog Cave's mushroom (<see cref="MapCorrections"/>).</summary>
    public ImportOptions Options { get; } = options ?? ImportOptions.Default;

    /// <summary>The folder being written, which replaces the plugin's once the import is done.</summary>
    public string Staging { get; } = staging;

    public BorgArchive Archive => field ??= BorgArchive.Open(Rom);

    /// <summary>The DB. Its records' places are the code's: every position list is checked to be the DB's own order (<see cref="PositionLists"/>).</summary>
    public GameDb Db => field ??= ReadDb(Rom);

    public IReadOnlyList<MapChunk> Chunks => field ??= MapChunks.Read(Rom);

    /// <summary>The Battle arenas' chunks by their numbers (<see cref="Arenas.Chunks"/>).</summary>
    public IReadOnlyList<RecordId> ArenaChunks => field ??= Arenas.Chunks(Chunks);

    /// <summary>The scripts, once <see cref="ScriptImporter"/> has built them, which the chunks' npcs are read against.</summary>
    public IReadOnlyList<ScriptRecord>? Scripts { get; set; }

    public EngineTables Engine => field ??= EngineTables.Read(Rom);

    public AudioTables Audio => field ??= AudioTables.Read(Rom);

    public BodySoundTables BodySounds => field ??= BodySoundTables.Read(Rom);

    /// <summary>The Borg1 each particle emitter texture slot names (<see cref="ParticleTextures"/>).</summary>
    public IReadOnlyList<int?> ParticleSlots => field ??= ParticleTextures.Read(Rom);

    public IReadOnlyList<int> Gradients => field ??= Skies.ReadGradients(Rom);

    public SpellVisualTables SpellVisuals => field ??= SpellVisualTables.Read(Rom);

    public WeatherTables Weather => field ??= WeatherTables.Read(Rom);

    public CharacterTables Characters => field ??= CharacterTables.Read(Rom);

    public ItemTables Items => field ??= ItemTables.Read(Rom);

    public MenuTables Menus => field ??= MenuTables.Read(Rom);

    public EntityModels Models => field ??= EntityModels.Read(Rom);

    /// <summary>Every Borg6 with its index, by the scene it animates.</summary>
    public ILookup<int, (int Index, Borg6Animation Animation)> Animations => field ??= Archive.Listings
        .Where(l => l.Type == BorgType.Animation)
        .Select(l => (l.Index, Animation: Borg6Animation.Parse(Archive.Read(l.Index))))
        .ToLookup(a => a.Animation.Scene);

    /// <summary>
    /// The scenes chunks swap in while a flag holds, with the chunks that do (<see cref="ToggledScenes"/>).
    /// </summary>
    public IReadOnlyList<(MapChunk Chunk, int Scene)> Swaps => field ??=
        [.. ToggledScenes.Read(Rom).SelectMany(t => Chunks.Where(c => c.Scene == t.Scene).Select(c => (c, t.Lit)))];

    /// <summary>
    /// Where <see cref="SeamWelds"/> moves the chunk scenes' vertices, by scene: welded a map set at a time, and a lit scene a chunk
    /// swaps in welded as its chunk's, to the neighbours round it. None without <see cref="ImportOptions.WeldSeams"/>.
    /// </summary>
    public Dictionary<int, Dictionary<DrawVertex, Vector3>> ChunkWelds => field ??= WeldChunks();

    /// <summary>What <see cref="ChunkCuts"/> takes out of the chunk scenes of the map sets Draw Distance's Extended draws further in.</summary>
    public ChunkCuts OverworldCuts => field ??= ChunkCuts.For([.. Chunks.Where(c => c.Set == MapSet.Overworld)],
        Swaps.Where(s => s.Chunk.Set == MapSet.Overworld), Archive.Read, ChunkWelds);

    private Dictionary<int, Dictionary<DrawVertex, Vector3>> WeldChunks()
    {
        if (!Options.WeldSeams)
            return [];
        bool Animated(int scene) => Animations[scene].Any();
        var welds = SeamWelds.ForChunks(Chunks, Archive.Read, Animated, MapSets.Islands(Chunks));
        foreach (var (chunk, lit) in Swaps)
        {
            var around = Chunks.Where(c => c.Set == chunk.Set && Math.Abs(c.Row - chunk.Row) <= 1 && Math.Abs(c.Column - chunk.Column) <= 1)
                .Select(c => c == chunk ? c with { Scene = lit } : c).ToList();
            if (SeamWelds.ForChunks(around, Archive.Read, Animated).TryGetValue(lit, out var moved))
                welds[lit] = moved;
        }
        return welds;
    }

    private static GameDb ReadDb(RomImage rom)
    {
        var db = GameDb.Read(rom);
        PositionLists.Verify(rom, db);
        return db;
    }

    /// <summary>Tells the progress callback the importer has started on <paramref name="step"/>.</summary>
    public void Report(string step) => progress?.Report(step);

    /// <summary>Writes <paramref name="value"/> as JSON at <paramref name="path"/> under the staging folder.</summary>
    public void WriteJson<T>(string path, T value) => Write(Path.Combine(Staging, path), JsonSerializer.SerializeToUtf8Bytes(value, PluginJson.Options));

    /// <summary>Writes the record file <paramref name="file"/>, under <c>records/</c>.</summary>
    public void WriteRecords<T>(string file, T value) => WriteJson(Path.Combine("records", file), value);

    /// <summary>Writes an asset of record <paramref name="id"/>, at the path the record's ID names.</summary>
    public void WriteAsset(RecordId id, string extension, byte[] bytes) =>
        Write(Path.Combine([Staging, "assets", id.Namespace, id.Type, .. id.Key.Split('/')]) + "." + extension, bytes);

    private static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
