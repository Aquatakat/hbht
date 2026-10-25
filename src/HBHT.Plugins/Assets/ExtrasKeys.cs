namespace HBHT.Plugins.Assets;

/// <summary>
/// The keys the importer's <c>SceneGltf</c> writes in its glTFs' extras (docs/formats/scenes.md), which the readers
/// look for: <see cref="SceneExtras"/> reads the scene's, <see cref="SceneMesh"/> and <see cref="ActorClips"/> and
/// the runtime's materials read the nodes', animations' and materials'.
/// </summary>
public static class ExtrasKeys
{
    /// <summary>The scene's extras: its scrolling textures.</summary>
    public const string ScrollingTextures = "scrollingTextures";
    /// <summary>The scene's extras: its ambient light's colour.</summary>
    public const string AmbientLight = "ambientLight";
    /// <summary>The scene's extras: its particle emitters.</summary>
    public const string ParticleEmitters = "particleEmitters";
    /// <summary>The scene's extras: its animations with nothing to move.</summary>
    public const string StillAnimations = "stillAnimations";
    /// <summary>The scene's extras: set when a model's transform mirrors it.</summary>
    public const string Mirrors = "mirrors";

    /// <summary>A node's extras: the transform it is, or belongs to.</summary>
    public const string Transform = "transform";
    /// <summary>A node's extras: a transform's pivot, when it has one.</summary>
    public const string Pivot = "pivot";
    /// <summary>A node's extras: whether the transform is a billboard.</summary>
    public const string Billboard = "billboard";
    /// <summary>A node's extras: set on a billboard's facing node.</summary>
    public const string Facing = "facing";

    /// <summary>An animation's, or a still animation's, extras: the Borg6's archive index.</summary>
    public const string Borg6 = "borg6";
    /// <summary>An animation's, or a still animation's, extras: its length in ticks.</summary>
    public const string Length = "length";

    /// <summary>A material's extras: the texture's Borg1 archive index.</summary>
    public const string Borg1 = "borg1";
    /// <summary>A material's extras: its Borg2 material flags.</summary>
    public const string Borg2Flags = "borg2Flags";

    /// <summary>A scrolling texture's keys, in the scene's <see cref="ScrollingTextures"/>.</summary>
    public const string Width = "width", Move = "move", Loads = "loads";
    /// <summary>A particle emitter's colours (minimum, maximum), in the scene's <see cref="ParticleEmitters"/>.</summary>
    public const string Color = "color";
}
