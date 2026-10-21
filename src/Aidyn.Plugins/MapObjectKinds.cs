using Aidyn.Plugins.Records;

namespace Aidyn.Plugins;

/// <summary>
/// The kinds of map object, in the order <see cref="MapObject"/>'s <c>JsonDerivedType</c> attributes list them, which is the
/// order the editor's legend lists them, with the name every tool calls each (<see cref="PluginCatalog.Describe(MapObject?)"/>
/// and the map list's counts among them). The kinds and their <c>type</c> discriminators come from the attributes
/// (<see cref="RecordSchema.DerivedTypes"/>), so a kind added there is listed here; only its name is kept here.
/// </summary>
public static class MapObjectKinds
{
    /// <summary>A kind: its record type, its <c>type</c> discriminator in the JSON, and its name.</summary>
    public sealed record Kind(Type Type, string Discriminator, string Name);

    private static readonly IReadOnlyDictionary<Type, string> Names = new Dictionary<Type, string>
    {
        [typeof(SceneObject)] = "Scene",
        [typeof(ContainerObject)] = "Container",
        [typeof(LightObject)] = "Light",
        [typeof(SoundObject)] = "Sound",
        [typeof(WanderNodeObject)] = "Wander node",
        [typeof(MonsterPartyObject)] = "Monster party",
        [typeof(ReferencePointObject)] = "Reference point",
        [typeof(TeleporterObject)] = "Teleporter",
        [typeof(CameraObject)] = "Camera",
        [typeof(DialogueObject)] = "Dialogue",
        [typeof(TriggerObject)] = "Trigger",
        [typeof(SavePointObject)] = "Save point",
    };

    public static readonly IReadOnlyList<Kind> All =
        [.. RecordSchema.DerivedTypes(typeof(MapObject)).Select(d => new Kind(d.Type, d.Discriminator, Names.GetValueOrDefault(d.Type) ?? d.Type.Name))];

    /// <summary>The name of a map object's kind, such as "Monster party".</summary>
    public static string Name(MapObject o) => All.FirstOrDefault(k => k.Type == o.GetType())?.Name ?? o.GetType().Name;

    /// <summary>The name of the kind a JSON discriminator names, or the discriminator itself if it names none.</summary>
    public static string Name(string discriminator) => All.FirstOrDefault(k => k.Discriminator == discriminator)?.Name ?? discriminator;
}
