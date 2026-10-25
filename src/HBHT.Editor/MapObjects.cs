using System.Text.Json.Nodes;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Editor;

/// <summary>Map objects' colours and fields, for the editor's views. Their names and places are <see cref="MapObjectKinds"/> and <see cref="MapPlacements"/>.</summary>
public static class MapObjects
{
    /// <summary>A kind of map object: its name, for the legend, and its marker's colour.</summary>
    public sealed record Kind(string Name, Color Colour);

    private static readonly Dictionary<Type, Color> Colours = new()
    {
        [typeof(SceneObject)] = Color.FromHtml("#8d99ae"),
        [typeof(ContainerObject)] = Color.FromHtml("#f4c542"),
        [typeof(LightObject)] = Color.FromHtml("#fff4a8"),
        [typeof(SoundObject)] = Color.FromHtml("#6ec6ff"),
        [typeof(WanderNodeObject)] = Color.FromHtml("#9be38b"),
        [typeof(MonsterPartyObject)] = Color.FromHtml("#ff5c5c"),
        [typeof(ReferencePointObject)] = Color.FromHtml("#c792ea"),
        [typeof(TeleporterObject)] = Color.FromHtml("#00e5c0"),
        [typeof(CameraObject)] = Color.FromHtml("#ff9f43"),
        [typeof(DialogueObject)] = Color.FromHtml("#ff7ad9"),
        [typeof(TriggerObject)] = Color.FromHtml("#7f8cff"),
        [typeof(SavePointObject)] = Color.FromHtml("#ffffff"),
    };

    /// <summary>The kinds, in the legend's order, with the map viewer's colours.</summary>
    public static readonly IReadOnlyList<(Type Type, Kind Kind)> Kinds =
        [.. MapObjectKinds.All.Select(k => (k.Type, new Kind(k.Name, Colours.GetValueOrDefault(k.Type, Colors.Gray))))];

    public static Kind KindOf(MapObject o) =>
        Kinds.FirstOrDefault(k => k.Type == o.GetType()).Kind ?? new Kind(o.GetType().Name, Colors.Gray);

    /// <summary>An object's fields as its map record holds them.</summary>
    public static JsonObject? Json(PluginSet plugins, RecordId voxel)
    {
        var index = MapRecord.IndexOf(voxel);
        return (plugins.GetJson(MapRecord.MapOf(voxel))?["objects"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(o => (int?)o["index"] == index);
    }
}
