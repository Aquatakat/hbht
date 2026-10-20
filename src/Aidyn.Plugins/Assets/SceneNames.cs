using System.Globalization;

namespace Aidyn.Plugins.Assets;

/// <summary>
/// What the importer calls the parts of a scene's glTF (docs/formats/scenes.md), which the exporter writes and the
/// readers look for: nodes, animations and images by the number of the Borg or transform they come from. The
/// importer's <c>SceneGltf</c> and every reader of its files (the actor clips, the runtime's and the editor's views)
/// take the names from here.
/// </summary>
public static class SceneNames
{
    /// <summary>The scene's camera node.</summary>
    public const string Camera = "camera";

    /// <summary>What follows a billboard's transform number in the name of its node that turns to the camera.</summary>
    public const string FacingSuffix = " facing";

    private const string TransformPrefix = "transform ", LightPrefix = "light ", EmitterPrefix = "emitter ",
        LocatorPrefix = "locator ", AnimationPrefix = "borg6 ", ImagePrefix = "borg1 ";

    /// <summary>A transform's node (Borg5 transform <paramref name="transform"/>).</summary>
    public static string Transform(int transform) => Name(TransformPrefix, transform);

    /// <summary>A billboard's root node, which holds its position and scale.</summary>
    public static string Billboard(int transform) => Transform(transform) + " billboard";

    /// <summary>The node under a billboard's root that turns to the camera.</summary>
    public static string Facing(int transform) => Transform(transform) + FacingSuffix;

    /// <summary>A billboard's card, which holds its meshes.</summary>
    public static string Card(int transform) => Transform(transform) + " card";

    /// <summary>A scene's directional light (the scene's <paramref name="light"/>th, the last being its ambient, which has none).</summary>
    public static string Light(int light) => Name(LightPrefix, light);

    /// <summary>A particle emitter's node, on the transform of the locator it sits at.</summary>
    public static string Emitter(int emitter) => Name(EmitterPrefix, emitter);

    /// <summary>A locator's node (0–7), where the game attaches what it draws on a scene at run time.</summary>
    public static string Locator(int locator) => Name(LocatorPrefix, locator);

    /// <summary>
    /// The node under a locator's that what a body holds there hangs from (<c>SceneGetLocatorMtx</c>, <c>0x8009EB10</c>): the locator
    /// turned by its model's rotation, which the locator's own node leaves out, as its emitters and heads don't take it.
    /// </summary>
    public static string Attachment(int locator) => Locator(locator) + " attachment";

    /// <summary>An animation (Borg6 <paramref name="borg6"/>, its archive index).</summary>
    public static string Animation(int borg6) => Name(AnimationPrefix, borg6);

    /// <summary>A texture's image (Borg1 <paramref name="borg1"/>, its archive index).</summary>
    public static string Image(int borg1) => Name(ImagePrefix, borg1);

    /// <summary>Whether a node name is a billboard's facing node.</summary>
    public static bool IsFacing(string name) => name.EndsWith(FacingSuffix, StringComparison.Ordinal);

    /// <summary>The number of a light node's name.</summary>
    public static bool TryLight(string name, out int light) => TryNumber(name, LightPrefix, out light);

    /// <summary>The number of an emitter node's name.</summary>
    public static bool TryEmitter(string name, out int emitter) => TryNumber(name, EmitterPrefix, out emitter);

    /// <summary>The number of a locator node's name.</summary>
    public static bool TryLocator(string name, out int locator) => TryNumber(name, LocatorPrefix, out locator);

    /// <summary>The archive index of an animation's name.</summary>
    public static bool TryAnimation(string name, out int borg6) => TryNumber(name, AnimationPrefix, out borg6);

    private static string Name(string prefix, int number) => prefix + number.ToString(CultureInfo.InvariantCulture);

    private static bool TryNumber(string name, string prefix, out int number)
    {
        number = 0;
        return name.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }
}
