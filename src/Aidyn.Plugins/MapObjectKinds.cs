using Aidyn.Plugins.Records;

namespace Aidyn.Plugins;

/// <summary>
/// The kinds of map object, in the order the editor's legend lists them, with the name every tool calls
/// each (<see cref="PluginCatalog.Describe(MapObject?)"/> and the map list's counts among them).
/// </summary>
public static class MapObjectKinds
{
    /// <summary>A kind: its record type, its <c>type</c> discriminator in the JSON, and its name.</summary>
    public sealed record Kind(Type Type, string Discriminator, string Name);

    public static readonly IReadOnlyList<Kind> All =
    [
        new(typeof(SceneObject), "scene", "Scene"),
        new(typeof(ContainerObject), "container", "Container"),
        new(typeof(LightObject), "light", "Light"),
        new(typeof(SoundObject), "sound", "Sound"),
        new(typeof(WanderNodeObject), "wanderNode", "Wander node"),
        new(typeof(MonsterPartyObject), "monsterParty", "Monster party"),
        new(typeof(ReferencePointObject), "refPoint", "Reference point"),
        new(typeof(TeleporterObject), "teleporter", "Teleporter"),
        new(typeof(CameraObject), "camera", "Camera"),
        new(typeof(DialogueObject), "dialogue", "Dialogue"),
        new(typeof(TriggerObject), "trigger", "Trigger"),
        new(typeof(SavePointObject), "savePoint", "Save point"),
    ];

    /// <summary>The name of a map object's kind, such as "Monster party".</summary>
    public static string Name(MapObject o) => All.FirstOrDefault(k => k.Type == o.GetType())?.Name ?? o.GetType().Name;

    /// <summary>The name of the kind a JSON discriminator names, or the discriminator itself if it names none.</summary>
    public static string Name(string discriminator) => All.FirstOrDefault(k => k.Discriminator == discriminator)?.Name ?? discriminator;
}
