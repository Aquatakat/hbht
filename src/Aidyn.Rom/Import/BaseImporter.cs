using System.Text.Json;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Graphics;
using Aidyn.Rom.Maps;
using FlagGate = Aidyn.Plugins.Records.FlagGate;
using PluginSceneModel = Aidyn.Plugins.Records.SceneModel;
using PluginSceneObjectFlags = Aidyn.Plugins.Records.SceneObjectFlags;
using RomSceneObjectFlags = Aidyn.Rom.Maps.SceneObjectFlags;

namespace Aidyn.Rom.Import;

/// <summary>
/// Turns a ROM into <c>aidyn.base</c> (ADR 0001): records under <c>records/</c> and converted assets
/// under <c>assets/aidyn/</c>, keyed by record ID. The output is deterministic: the same ROM and
/// <see cref="BasePlugin.ImporterVersion"/> always give the same bytes.
/// </summary>
/// <remarks>
/// This is the slice M1 needs: map sets and their chunks, scene objects, actors, every scene as
/// glTF and every chunk's collision. Records for the rest of the content come with the tracks that
/// use them.
/// </remarks>
public static class BaseImporter
{
    /// <summary>
    /// Imports <paramref name="rom"/> into <paramref name="directory"/>, replacing what's there. It
    /// writes to a sibling folder first, so a failed import leaves the old one in place.
    /// </summary>
    public static void Import(RomImage rom, string directory, IProgress<string>? progress = null)
    {
        if (rom.Version.ResearchOnly)
            throw new UnsupportedRomException(rom.Sha1, rom.Header);

        directory = Path.GetFullPath(directory);
        var staging = directory + ".importing";
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        var archive = BorgArchive.Open(rom);
        var chunks = MapChunks.Read(rom);

        WriteJson(Path.Combine(staging, PluginManifest.FileName), new PluginManifest(
            RecordId.BaseNamespace, "Aidyn Chronicles: The First Mage", "1.0.0",
            Import: new ImportInfo(BasePlugin.ImporterVersion, rom.Sha1, rom.Version.Name)));

        progress?.Report("Map sets");
        WriteJson(Path.Combine(staging, "records", "mapsets.json"), BuildMapSets(chunks));

        progress?.Report("Maps and collision");
        var maps = chunks.Select(c => c.Collision).Distinct().Order().ToList();
        var actors = archive.Listings.Where(l => l.Type == BorgType.Actor).Select(l => l.Index).ToHashSet();
        var mapRecords = new List<MapRecord>();
        foreach (var index in maps)
        {
            var map = Borg9Map.Parse(archive.Read(index));
            mapRecords.Add(BuildMap(index, map, actors));
            var chunk = chunks.First(c => c.Collision == index);
            WriteAsset(staging, RecordId.Hex(RecordTypes.Map, index), "glb", CollisionGltf.Export([(chunk, map)], placeInSet: false));
        }
        WriteJson(Path.Combine(staging, "records", "maps.json"), mapRecords);

        progress?.Report("Actors");
        var actorRecords = new List<ActorRecord>();
        foreach (var index in actors.Order())
        {
            var actor = Borg7Actor.Parse(archive.Read(index));
            var scene = Borg6Animation.Parse(archive.Read(actor.States[0].Animation)).Scene;
            actorRecords.Add(new ActorRecord(RecordId.Hex(RecordTypes.Actor, index), RecordId.Hex(RecordTypes.Scene, scene),
                actor.RootTransform, actor.InitialState,
                [.. actor.States.Select(s => new ActorState(RecordId.Hex(RecordTypes.Animation, s.Animation),
                    [.. s.Branches.Select(b => new ActorBranch(b.Request, b.Chance, b.Interrupts, b.State))]))]));
        }
        WriteJson(Path.Combine(staging, "records", "actors.json"), actorRecords);

        progress?.Report("Scenes");
        var animations = archive.Listings.Where(l => l.Type == BorgType.Animation)
            .Select(l => (l.Index, Animation: Borg6Animation.Parse(archive.Read(l.Index))))
            .ToLookup(a => a.Animation.Scene);
        var scenes = archive.Listings.Where(l => l.Type == BorgType.Scene).Select(l => l.Index).ToList();
        var done = 0;
        Parallel.ForEach(scenes, index =>
        {
            var scene = Borg5Scene.Parse(archive.Read(index));
            WriteAsset(staging, RecordId.Hex(RecordTypes.Scene, index), "glb",
                SceneGltf.Export(scene, archive.Read, index, animations[index].ToList()));
            if (Interlocked.Increment(ref done) % 200 == 0)
                progress?.Report($"Scenes {done}/{scenes.Count}");
        });

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        Directory.Move(staging, directory);
    }

    private static List<MapSetRecord> BuildMapSets(IReadOnlyList<MapChunk> chunks) =>
    [
        .. Enum.GetValues<MapSet>().Select(set => new MapSetRecord(
            RecordId.Decimal(RecordTypes.MapSet, (int)set), set.ToString(), Maps.MapSets.CellSize(set),
            [
                .. chunks.Where(c => c.Set == set).Select(c => new ChunkEntry(c.Row, c.Column,
                    c.Scene == 0 ? null : RecordId.Hex(RecordTypes.Scene, c.Scene),
                    c.AltScene == 0 ? null : RecordId.Hex(RecordTypes.Scene, c.AltScene),
                    RecordId.Hex(RecordTypes.Map, c.Collision), c.Terrain)),
            ])),
    ];

    private static MapRecord BuildMap(int index, Borg9Map map, IReadOnlySet<int> actors)
    {
        var objects = new List<MapObject>();
        foreach (var o in map.Objects)
        {
            if (o is not { Type: VoxelType.Scene, Data: SceneData scene })
                continue;
            var pak = o.Flags.HasFlag(VoxelFlags.ExpansionPak) ? PakVariant.With
                : o.Flags.HasFlag(VoxelFlags.NoExpansionPak) ? PakVariant.Without
                : PakVariant.Any;
            var isActor = scene.SceneFlags.HasFlag(RomSceneObjectFlags.Actor);
            objects.Add(new SceneObject(o.Index, o.Position, o.Radius, pak, Gate(o.Visible),
                [
                    .. scene.Models.Select(m => new PluginSceneModel(
                        RecordId.Hex(isActor && actors.Contains(m.Borg) ? RecordTypes.Actor : RecordTypes.Scene, m.Borg),
                        m.DrawDistance)),
                ],
                scene.Lods, scene.Rotation, scene.Scale, new Plugins.Records.Rgba(scene.Tint.R, scene.Tint.G, scene.Tint.B, scene.Tint.A),
                (PluginSceneObjectFlags)((int)scene.SceneFlags & ~(int)RomSceneObjectFlags.Actor)));
        }
        return new MapRecord(RecordId.Hex(RecordTypes.Map, index), objects);
    }

    private static FlagGate Gate(Maps.FlagGate gate) => new(RecordId.Decimal(RecordTypes.Flag, gate.Flag), gate.Inverted);

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, PluginJson.Options));
    }

    private static void WriteAsset(string plugin, RecordId id, string extension, byte[] bytes)
    {
        var path = Path.Combine([plugin, "assets", id.Namespace, id.Type, .. id.Key.Split('/')]) + "." + extension;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
