namespace HBHT.Core;

/// <summary>
/// Godot's renderer, the player's choice (<see cref="Settings.Renderer"/>). Godot can't change it while it runs, so changing it
/// restarts the game, which comes back where it was (<see cref="Ui.PauseExit.Restart"/>). All three draw the original's look alike; what sets them apart is their cost and what else
/// they can draw (docs/systems/rendering.md#the-renderers).
/// </summary>
public enum Renderer
{
    /// <summary>Forward+, Godot's clustered renderer over Vulkan or Direct3D 12, with everything Godot can draw, for what mods may light.</summary>
    ForwardPlus,

    /// <summary>Godot's Mobile renderer over Vulkan or Direct3D 12: fewer lights an object and fewer effects, and cheaper.</summary>
    Mobile,

    /// <summary>Compatibility, Godot's OpenGL 3.3 renderer, for old or weak graphics, and what Godot falls back to without Vulkan.</summary>
    Compatibility,
}

/// <summary>The renderers' names, as the options show them.</summary>
public static class RendererNames
{
    /// <summary>The renderers, in the order the options list them.</summary>
    public static readonly IReadOnlyList<Renderer> All = [Renderer.ForwardPlus, Renderer.Mobile, Renderer.Compatibility];

    public static string Name(this Renderer renderer) => renderer switch
    {
        Renderer.Mobile => "Mobile",
        Renderer.Compatibility => "Compatibility",
        _ => "Forward+",
    };
}
