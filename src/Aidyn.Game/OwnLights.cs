using Aidyn.Plugins.Assets;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// A scene's own Borg4 lights (docs/systems/rendering.md's "A scene's own lights"), as the importer writes them: each light but
/// the last a directional light node, which its animation can turn and scale, and the last the ambient, in the scene's extras. A
/// scene without the engine-lit bit is lit by them (<c>BorgAnimDrawSceneRaw</c>, <c>0x80097a90</c>): a cinematic's shot, the
/// pause menu's scroll, and the spells' visuals. A scene with none gets the white ambient and no light.
/// </summary>
public sealed class OwnLights
{
    private readonly Node3D _scene;
    private readonly Color _ambient;
    /// <summary>The node whose scale tints the ambient: the transform after the last light's, the first model node.</summary>
    private readonly Node3D? _ambientTint;
    private readonly List<(Light3D Node, Color Color)> _lights = [];

    /// <summary>Reads <paramref name="scene"/>'s lights from its extras, hides their nodes, and lights it by them from now on.</summary>
    public OwnLights(Node3D scene, SceneExtras extras)
    {
        _scene = scene;
        var colors = extras.LightColors;
        _ambient = extras.Ambient is { } ambient ? Rgb(ambient) : Colors.White;
        // The ambient is tinted by transform 1 + its index, as each light is, which is the first model node; a scene
        // without lights gets the plain white ambient.
        var (first, billboard) = (SceneNames.Transform(1 + colors.Count), SceneNames.Billboard(1 + colors.Count));
        _ambientTint = extras.Ambient is null ? null : Nodes.FindAll<Node3D>(scene).FirstOrDefault(n => n.Name == first || n.Name == billboard);
        foreach (var light in Nodes.FindAll<Light3D>(scene))
        {
            light.Visible = false;
            if (SceneNames.TryLight(light.Name.ToString(), out var i) && i < colors.Count)
                _lights.Add((light, Rgb(colors[i])));
        }
        _lights.Sort((a, b) => string.CompareOrdinal(a.Node.Name, b.Node.Name));
        if (_lights.Count > N64Material.MaxLights)
            _lights.RemoveRange(N64Material.MaxLights, _lights.Count - N64Material.MaxLights);
        N64Material.SetLighting(scene, N64Material.Lighting.Own);
    }

    /// <summary>Lights the scene as it's posed now: each light along its transform's way, tinted by its scale.</summary>
    public void Apply() =>
        N64Material.SetOwnLights(_scene, Tinted(_ambient, _ambientTint), [.. _lights.Select(l => (Tinted(l.Color, l.Node), l.Node.GlobalBasis.Z))]);

    /// <summary>
    /// A light's colour tinted by its transform's scale, each channel clamped to 0–1 and the product to 1, as the
    /// original reads <c>+0x28</c>–<c>+0x30</c> into it (<c>BorgAnimDrawSceneRaw</c>, <c>0x80097d54</c>).
    /// </summary>
    private static Color Tinted(Color color, Node3D? transform)
    {
        if (transform is null)
            return color;
        var scale = transform.Scale;
        return new Color(Math.Min(1, color.R * Math.Clamp(scale.X, 0, 1)), Math.Min(1, color.G * Math.Clamp(scale.Y, 0, 1)),
            Math.Min(1, color.B * Math.Clamp(scale.Z, 0, 1)));
    }

    private static Color Rgb(System.Numerics.Vector3 c) => new(c.X, c.Y, c.Z);
}
