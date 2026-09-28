using Aidyn.Core.Sky;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the lens flare (<see cref="LensFlare"/>) over the scene, as the original does after the
/// scenery and before the HUD.
/// </summary>
public partial class LensFlareView : Control
{
    private readonly SkyView _sky;
    private readonly List<SkySprite> _sprites = [];

    public LensFlareView(SkyView sky)
    {
        _sky = sky;
        Name = "Lens flare";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>A canvas layer over the scene, under the HUD, holding the flare.</summary>
    public static CanvasLayer Create(SkyView sky)
    {
        var layer = new CanvasLayer { Name = "Lens flare", Layer = 0 };
        layer.AddChild(new LensFlareView(sky));
        return layer;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        // Only an outdoor sky has one.
        if (_sky.Flare is not { } flare || _sky.Objects is not { } objects || !_sky.State.Drawn)
            return;
        _sprites.Clear();
        flare.Draw(_sprites, objects.SunMiddle, _sky.Colors.Tint.X, _sky.ImageSize);
        SkyBackdropView.DrawSprites(this, _sky, _sprites);
    }
}
