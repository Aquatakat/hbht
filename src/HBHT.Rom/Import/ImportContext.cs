using System.Text.Json;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;
using HBHT.Rom.Maps;
using HBHT.Rom.Quests;

namespace HBHT.Rom.Import;

/// <summary>
/// What one import shares: the ROM, its archive and DB, each code-segment table read once on first use, and the
/// staging folder the records and assets are written to. The importers of each family (<see cref="BaseImporter"/>
/// runs them) build from this and write through it. The tables are read on the importing thread, before any
/// parallel work that uses them.
/// </summary>
internal sealed class ImportContext(RomImage rom, string staging, IProgress<string>? progress = null)
{
    public RomImage Rom { get; } = rom;

    /// <summary>The folder being written, which replaces the plugin's once the import is done.</summary>
    public string Staging { get; } = staging;

    public BorgArchive Archive => field ??= BorgArchive.Open(Rom);

    /// <summary>The DB. Its records' places are the code's: every position list is checked to be the DB's own order (<see cref="PositionLists"/>).</summary>
    public GameDb Db => field ??= ReadDb(Rom);

    public IReadOnlyList<MapChunk> Chunks => field ??= MapChunks.Read(Rom);

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
