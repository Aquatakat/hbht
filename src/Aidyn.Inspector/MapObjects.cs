using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Inspector;

/// <summary>Where maps are placed, and map objects' names and colours, for the editor's views.</summary>
public static class MapObjects
{
    /// <summary>A chunk of a map set, where a map is placed.</summary>
    public sealed record Place(MapSetRecord Set, ChunkEntry Chunk)
    {
        public override string ToString() => $"{Set.Label} {Chunk.Name}";
    }

    /// <summary>A kind of map object: its name, for the legend, and its marker's colour.</summary>
    public sealed record Kind(string Name, Color Colour);

    private static readonly ConditionalWeakTable<PluginSet, Dictionary<RecordId, Place>> PlacesBySet = [];

    /// <summary>The kinds, in the legend's order, with the map viewer's colours.</summary>
    public static readonly IReadOnlyList<(Type Type, Kind Kind)> Kinds =
    [
        (typeof(SceneObject), new("Scene", Color.FromHtml("#8d99ae"))),
        (typeof(ContainerObject), new("Container", Color.FromHtml("#f4c542"))),
        (typeof(LightObject), new("Light", Color.FromHtml("#fff4a8"))),
        (typeof(SoundObject), new("Sound", Color.FromHtml("#6ec6ff"))),
        (typeof(WanderNodeObject), new("Wander node", Color.FromHtml("#9be38b"))),
        (typeof(MonsterPartyObject), new("Monster party", Color.FromHtml("#ff5c5c"))),
        (typeof(ReferencePointObject), new("Reference point", Color.FromHtml("#c792ea"))),
        (typeof(TeleporterObject), new("Teleporter", Color.FromHtml("#00e5c0"))),
        (typeof(CameraObject), new("Camera", Color.FromHtml("#ff9f43"))),
        (typeof(DialogueObject), new("Dialogue", Color.FromHtml("#ff7ad9"))),
        (typeof(TriggerObject), new("Trigger", Color.FromHtml("#7f8cff"))),
        (typeof(SavePointObject), new("Save point", Color.FromHtml("#ffffff"))),
    ];

    public static Kind KindOf(MapObject o) =>
        Kinds.FirstOrDefault(k => k.Type == o.GetType()).Kind ?? new Kind(o.GetType().Name, Colors.Gray);

    /// <summary>Where each map is placed, by the first map set to place it.</summary>
    public static IReadOnlyDictionary<RecordId, Place> Places(PluginSet plugins) =>
        PlacesBySet.GetValue(plugins, p =>
        {
            var places = new Dictionary<RecordId, Place>();
            foreach (var set in p.All<MapSetRecord>(RecordTypes.MapSet))
            {
                foreach (var chunk in set.Chunks)
                    places.TryAdd(chunk.Map, new Place(set, chunk));
            }
            return places;
        });

    /// <summary>The index a voxel ID names within its map.</summary>
    public static int Index(RecordId voxel) =>
        int.Parse(voxel.Key[(voxel.Key.IndexOf('/', StringComparison.Ordinal) + 1)..], CultureInfo.InvariantCulture);

    /// <summary>The object a voxel ID names, if its map has one at that index.</summary>
    public static MapObject? Find(PluginSet plugins, RecordId voxel)
    {
        var index = Index(voxel);
        return plugins.TryGet<MapRecord>(MapRecord.MapOf(voxel))?.Objects.FirstOrDefault(o => o.Index == index);
    }

    /// <summary>An object's fields as its map record holds them.</summary>
    public static JsonObject? Json(PluginSet plugins, RecordId voxel)
    {
        var index = Index(voxel);
        return (plugins.GetJson(MapRecord.MapOf(voxel))?["objects"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(o => (int?)o["index"] == index);
    }

    /// <summary>What an object is, such as "Monster party: Male Commoner" or "Teleporter patioL2".</summary>
    public static string Describe(PluginCatalog catalog, MapObject? o) => o switch
    {
        DialogueObject d => $"Dialogue object {d.Name}",
        MonsterPartyObject m => $"Monster party: {catalog.Find(m.Entity)?.Summary}",
        TeleporterObject t => $"Teleporter {t.Name}",
        ContainerObject c => c.Kind.ToString(),
        TriggerObject t => $"{t.Kind} trigger",
        ReferencePointObject r => $"Reference point {r.Name}",
        SceneObject s => s.Models.Count > 0 ? $"Scene object: {s.Models[0].Model}" : "Scene object",
        { } other => KindOf(other).Name,
        null => "Object",
    };

    /// <summary>What a voxel ID names and where, such as "Teleporter patioL2, in JundarInteriors K07".</summary>
    public static string Describe(PluginCatalog catalog, RecordId voxel)
    {
        var map = MapRecord.MapOf(voxel);
        var where = Places(catalog.Plugins).GetValueOrDefault(map)?.ToString() ?? $"map {map.Key}";
        return $"{Describe(catalog, Find(catalog.Plugins, voxel))}, in {where}";
    }
}
