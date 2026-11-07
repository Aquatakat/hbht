namespace HBHT.Core;

/// <summary>
/// Godot's renderer, the player's choice (<see cref="Settings.Renderer"/>), which takes effect as the game next starts: Godot
/// can't change it while it runs. All three draw the original's look alike; what sets them apart is their cost and what else
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
