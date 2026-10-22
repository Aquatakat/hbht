using Aidyn.Core.Sky;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Shows the core's sky (<see cref="SkyDirector"/>): the colours it gives the scenery, its tint and fog, and the ambient
/// and sun that light bodies, which the <see cref="N64Material"/> reads as global shader parameters, pushed each frame
/// as the director last worked them out. <see cref="SkyBackdropView"/> and <see cref="LensFlareView"/> draw its gradient,
/// sun, moon, stars, clouds and lens flare from it.
/// </summary>
/// <remarks>
/// With no fog in the weather, as at a new game, the original fogs from 996 to 1000 on the depth buffer's scale
/// (<c>Zones.SetSceneColors</c>), which with trek's planes of 1 and 180 is from about 105 units out to the far plane. The
/// weather's fog moves the start in (<see cref="SkyDirector.SceneryFogStart"/>). A cut-scene draws with planes of 0.1 and
/// 125 (<see cref="SkyDirector.Planes"/>), so the same 996 is about 21 units out, on the stage's scenery and actors alike.
/// </remarks>
public partial class SkyView : Node
{
    /// <summary>Trek's clip planes, which battles keep (<see cref="SkyDirector.TrekNear"/>).</summary>
    public const float Near = SkyDirector.TrekNear, Far = SkyDirector.TrekFar;

    private readonly PluginSet _plugins;
    private readonly UiImages _images;

    public SkyView(PluginSet plugins, SkyDirector sky)
    {
        _plugins = plugins;
        _images = new UiImages(plugins);
        Sky = sky;
        Name = "Sky";
    }

    /// <summary>The sky the core steps, which this shows.</summary>
    public SkyDirector Sky { get; }

    public override void _Process(double delta) => Push();

    /// <summary>
    /// Sets the global shader parameters to the colours as the director last worked them out (<see cref="SkyDirector.Apply"/>):
    /// nothing until a sky has been chosen. The fog is measured against the planes the original draws the frame with, a
    /// cut-scene's while one plays (<see cref="SkyDirector.Planes"/>), whatever the Godot camera's own.
    /// </summary>
    public void Push()
    {
        var (near, far) = Sky.Planes;
        N64Material.SetSky(Sky, near, far);
    }

    /// <summary>A UI image, as a texture, or null if no plugin has it.</summary>
    public Texture2D? Image(RecordId image) => _images.Get(image);

    /// <summary>A UI image's size in pixels, from its PNG's header (<see cref="PluginSet.ImageSize"/>), or nothing if it's missing.</summary>
    public System.Numerics.Vector2 ImageSize(RecordId image)
    {
        var (width, height) = _plugins.ImageSize(image);
        return new(width, height);
    }
}
