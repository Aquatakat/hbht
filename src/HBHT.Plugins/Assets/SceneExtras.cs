using System.Numerics;
using System.Text.Json;

namespace HBHT.Plugins.Assets;

/// <summary>A texture of a scene that scrolls (Borg1 flag 0x8), and how it moves.</summary>
/// <param name="Borg1">The texture's archive index.</param>
/// <param name="Width">Its width in texels.</param>
/// <param name="Move">The Borg1's move byte, which sets the direction and speed.</param>
/// <param name="Loads">How many times each draw of the scene loads it, which is how many times it scrolls.</param>
public readonly record struct ScrollingTexture(int Borg1, int Width, byte Move, int Loads);

/// <summary>An animation that only holds its scene still for its length, which glTF can't have an empty animation of.</summary>
public readonly record struct StillAnimation(int Borg6, int Length);

/// <summary>
/// What the importer keeps in a scene's glTF extras (docs/formats/scenes.md), read once: the scene's scrolling textures,
/// whether it mirrors a model, its lights' colours, ambient and emitters, its still animations and its transforms'
/// pivots. The keys are <see cref="ExtrasKeys"/>'s.
/// </summary>
public sealed class SceneExtras
{
    private SceneExtras() { }

    /// <summary>The scene's textures that scroll.</summary>
    public IReadOnlyList<ScrollingTexture> Scrolling { get; private init; } = [];

    /// <summary>Whether a model's transform mirrors it, at rest or as an animation poses it.</summary>
    public bool Mirrored { get; private init; }

    /// <summary>The ambient light's colour, or null if the scene has none.</summary>
    public Vector3? Ambient { get; private init; }

    /// <summary>The colour of each of the scene's directional lights (the glTF's <c>KHR_lights_punctual</c>), by light number.</summary>
    public IReadOnlyList<Vector3> LightColors { get; private init; } = [];

    /// <summary>The brighter of each particle emitter's two colours (its maximum), by emitter number, without the alpha.</summary>
    public IReadOnlyList<Vector3> EmitterColors { get; private init; } = [];

    /// <summary>The animations that only hold the scene still.</summary>
    public IReadOnlyList<StillAnimation> StillAnimations { get; private init; } = [];

    /// <summary>The pivot of each transform that has one and isn't a billboard, by transform number.</summary>
    public IReadOnlyDictionary<int, Vector3> Pivots { get; private init; } = new Dictionary<int, Vector3>();

    public static SceneExtras Read(GlbReader glb)
    {
        var root = glb.Root;
        var lights = root.TryGetProperty("extensions", out var extensions) && extensions.TryGetProperty("KHR_lights_punctual", out var punctual)
            ? punctual.GetProperty("lights").EnumerateArray().Select(l => Vec3(l.GetProperty("color"))).ToArray() : [];

        var pivots = new Dictionary<int, Vector3>();
        if (root.TryGetProperty("nodes", out var nodes))
        {
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object
                    && extras.TryGetProperty(ExtrasKeys.Transform, out var t) && extras.TryGetProperty(ExtrasKeys.Pivot, out var pivot)
                    && !(extras.TryGetProperty(ExtrasKeys.Billboard, out var billboard) && billboard.GetBoolean()))
                    pivots.TryAdd(t.GetInt32(), Vec3(pivot));
            }
        }

        if (glb.Scene is not { } scene || !scene.TryGetProperty("extras", out var sceneExtras) || sceneExtras.ValueKind != JsonValueKind.Object)
            return new SceneExtras { LightColors = lights, Pivots = pivots };

        return new SceneExtras
        {
            Scrolling = sceneExtras.TryGetProperty(ExtrasKeys.ScrollingTextures, out var scrolling) && scrolling.ValueKind == JsonValueKind.Array
                ? [.. scrolling.EnumerateArray().Where(HasBorg1).Select(e => new ScrollingTexture(e.GetProperty(ExtrasKeys.Borg1).GetInt32(),
                    e.GetProperty(ExtrasKeys.Width).GetInt32(), e.GetProperty(ExtrasKeys.Move).GetByte(), e.GetProperty(ExtrasKeys.Loads).GetInt32()))] : [],
            Mirrored = sceneExtras.TryGetProperty(ExtrasKeys.Mirrors, out var mirrors) && mirrors.GetBoolean(),
            Ambient = sceneExtras.TryGetProperty(ExtrasKeys.AmbientLight, out var ambient) ? Vec3(ambient) : null,
            LightColors = lights,
            EmitterColors = sceneExtras.TryGetProperty(ExtrasKeys.ParticleEmitters, out var emitters)
                ? [.. emitters.EnumerateArray().Select(e => Vec3(e.GetProperty(ExtrasKeys.Color)[1]))] : [],
            StillAnimations = sceneExtras.TryGetProperty(ExtrasKeys.StillAnimations, out var still)
                ? [.. still.EnumerateArray().Select(a => new StillAnimation(a.GetProperty(ExtrasKeys.Borg6).GetInt32(), a.GetProperty(ExtrasKeys.Length).GetInt32()))] : [],
            Pivots = pivots,
        };

        static bool HasBorg1(JsonElement e) => e.TryGetProperty(ExtrasKeys.Borg1, out var b) && b.ValueKind == JsonValueKind.Number;
    }

    /// <summary>The first three numbers of a JSON array as a vector.</summary>
    private static Vector3 Vec3(JsonElement c) => new(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle());
}
