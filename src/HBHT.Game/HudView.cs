using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Draws trek's HUD (<see cref="TrekHud"/>) on a <see cref="UiScreen"/>'s canvas, under trek's popups: the sundial, the
/// party's portraits, and the compass or the minimap, each kept to its corner of the screen. It's drawn as it stands
/// under the pause menu, as the original's is in the frame its menu darkens, and under a backdrop's page.
/// </summary>
public partial class HudView : Control
{
    private readonly UiPainter _ui;

    public HudView(PluginSet plugins)
    {
        _ui = new UiPainter(plugins);
        Name = "HUD";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Trek, whose HUD this draws.</summary>
    public TrekLoop? Trek { get; set; }

    /// <summary>Whether the player's chunk is under an outdoor sky, where the compass shows.</summary>
    public bool Outdoors { get; set; }

    public override void _Draw()
    {
        if (Trek is not { } trek || !(trek.HudShown || trek.Conversation is { ShowsBackdrop: true }))
            return;
        var look = trek.Camera.Look;
        var flat = new System.Numerics.Vector2(look.X, look.Z);
        flat = flat.LengthSquared() < 1e-6f ? new System.Numerics.Vector2(0, -1) : System.Numerics.Vector2.Normalize(flat);
        var screen = (Vector2I)Size;
        // At full brightness: the runtime's fade darkens trek's whole UI screen, as the original dims the HUD with the scene.
        foreach (var sprite in trek.Hud.Draw(trek.Game, Outdoors, flat, 1, screen.X, screen.Y))
            _ui.Sprite(this, sprite);
    }
}
