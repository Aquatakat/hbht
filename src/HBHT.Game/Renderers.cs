using HBHT.Core;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Godot's renderer: the one the game runs with, and the player's choice of it (<see cref="Settings.Renderer"/>), which Godot
/// takes up as it next starts.
/// </summary>
/// <remarks>
/// Godot picks its renderer before any of the game's code runs, and can't change it after. The project reads
/// <see cref="OverridePath"/> over its own settings as it starts (<c>application/config/project_settings_override</c>), so the
/// choice is written there, and a run's <c>--rendering-method</c> still goes over it. Godot falls back to Compatibility on its
/// own on a machine without Vulkan or Direct3D 12, whatever was chosen (<c>rendering/rendering_device/fallback_to_opengl3</c>).
/// </remarks>
internal static class Renderers
{
    /// <summary>The file the project reads over its settings as it starts, which holds the player's renderer.</summary>
    public const string OverridePath = "user://renderer.cfg";

    private const string Section = "rendering", Key = "renderer/rendering_method";

    private static readonly Lazy<string> Method = new(() => RenderingServer.GetCurrentRenderingMethod());

    /// <summary>
    /// Whether the game runs under Compatibility, Godot's OpenGL renderer: it hands the 3D image to the screen as it is, and
    /// draws no decals (<see cref="OnScreenOutput"/>, <see cref="ShadowView"/>).
    /// </summary>
    public static bool Compatibility => Method.Value == "gl_compatibility";

    /// <summary>The renderer the game runs with.</summary>
    public static Renderer Running => Of(Method.Value) ?? Renderer.ForwardPlus;

    /// <summary>Godot's name for a renderer, as <c>--rendering-method</c> takes it.</summary>
    public static string Name(Renderer renderer) => renderer switch
    {
        Renderer.Mobile => "mobile",
        Renderer.Compatibility => "gl_compatibility",
        _ => "forward_plus",
    };

    private static Renderer? Of(string name) => name switch
    {
        "forward_plus" => Renderer.ForwardPlus,
        "mobile" => Renderer.Mobile,
        "gl_compatibility" => Renderer.Compatibility,
        _ => null,
    };

    /// <summary>Writes <paramref name="renderer"/> for Godot to start with next time, unless it's there already.</summary>
    public static void Choose(Renderer renderer)
    {
        var name = Name(renderer);
        var file = new ConfigFile();
        if (file.Load(OverridePath) == Error.Ok && file.GetValue(Section, Key, "").AsString() == name)
            return;
        file.SetValue(Section, Key, name);
        if (file.Save(OverridePath) is not Error.Ok and var error)
            GD.PushWarning($"Couldn't write the renderer to {OverridePath}: {error}.");
    }
}
