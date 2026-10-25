using Aidyn.Core.Scenes;
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
/// Under the Draw Distance setting's Extended, trek's frame is cut at a sphere about the eye, and its fog measured along the
/// distance from it (<see cref="Radial"/>), and over the Overworld's 5×5 its planes move out (<see cref="Further"/>,
/// <see cref="DrawDistance"/>).
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

    /// <summary>
    /// Whether the frame is trek's under Draw Distance's Extended: cut at a sphere about the eye at the planes' far, where the
    /// fog ends, with the fog measured along the distance from the eye, rather than at the original's flat far plane.
    /// Never in a cut-scene, a battle or a cinematic, which keep the original's.
    /// </summary>
    public bool Radial { get; set; }

    /// <summary>Whether, besides, the Overworld's 5×5 is drawn, so the planes move out with it (<see cref="DrawDistance.Planes"/>).</summary>
    public bool Further { get; set; }

    /// <summary>
    /// The planes the frame's fog is measured against, and the radius it's cut at about the eye, or 0 for the original's
    /// far plane: the original's (<see cref="SkyDirector.Planes"/>) unless trek's frame is drawn further.
    /// </summary>
    public (float Near, float Far, float Radial) Frame
    {
        get
        {
            var planes = Sky.Planes;
            if (!Radial || Sky.CutScene)
                return (planes.Near, planes.Far, 0);
            if (Further)
                planes = DrawDistance.Planes(planes, Sky.FogTime);
            return (planes.Near, planes.Far, planes.Far);
        }
    }

    /// <summary>
    /// How far the follow camera's own far plane is set: the original's 180, or under <see cref="Radial"/> far enough that
    /// the sphere about the eye the frame is cut at fits inside it at the corners of the widest window.
    /// </summary>
    public float CameraFar => Frame.Radial > 0 ? Frame.Radial * 2 : Far;

    public override void _Process(double delta) => Push();

    /// <summary>
    /// Sets the global shader parameters to the colours as the director last worked them out (<see cref="SkyDirector.Apply"/>):
    /// nothing until a sky has been chosen. The fog is measured against the planes the original draws the frame with, a
    /// cut-scene's while one plays (<see cref="SkyDirector.Planes"/>), whatever the Godot camera's own, or those Extended
    /// draws trek's frame with (<see cref="Frame"/>).
    /// </summary>
    public void Push()
    {
        var (near, far, radial) = Frame;
        N64Material.SetSky(Sky, near, far, radial);
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
