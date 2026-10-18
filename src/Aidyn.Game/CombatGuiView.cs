using Aidyn.Core.Combat;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the battle screen (<see cref="CombatGui"/>) on trek's <see cref="UiScreen"/>, as trek's HUD is drawn: the log box
/// across the foot with the current combatant's portrait and line, the radar, the portraits' frames, the portraits with
/// their skulls, and the HP bars, in the original's order. It's drawn as it stands under the pause menu, as the original's
/// is in the frame its menu darkens.
/// </summary>
public partial class CombatGuiView : Control
{
    private readonly PluginSet _plugins;
    private readonly UiPainter _ui;

    public CombatGuiView(PluginSet plugins)
    {
        _plugins = plugins;
        _ui = new UiPainter(plugins);
        Name = "Battle screen";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>The battle whose screen this draws.</summary>
    public Battle? Battle { get; set; }

    /// <summary>
    /// Where the radar sits: in the screen's bottom right corner, the user's choice, which on a wide screen keeps it clear of
    /// the log box; or in the log box, as the original has it. On a 4:3 screen they're the same.
    /// </summary>
    public RadarAnchor Radar { get; set; } = RadarAnchor.Corner;

    public override void _Draw()
    {
        if (Battle is not { } battle)
            return;
        var screen = (Vector2I)Size;
        var drawn = battle.Gui.Draw(battle, Portrait, Radar, screen.X, screen.Y);

        var log = drawn.Log;
        _ui.Images.DrawPanel(this, log.Panel);
        _ui.Image(this, CombatLogBox.Border, log.BorderX, log.BorderY);
        if (log.Portrait is { } portrait)
            _ui.Image(this, portrait, log.PortraitX, log.PortraitY);
        _ui.Text(this, _ui.Main, log.Text.Text, log.Text.X, log.Text.Y, log.Text.Bounds, CombatLogBox.TextColor);

        _ui.Sprite(this, drawn.RadarRing);
        Fill(drawn.RadarDots);
        foreach (var frame in drawn.Frames)
            _ui.Sprite(this, frame);
        foreach (var image in drawn.Portraits)
            _ui.Sprite(this, image);
        Fill(drawn.HpBars);
    }

    private void Fill(IEnumerable<HudFill> fills)
    {
        foreach (var fill in fills)
            DrawRect(new Rect2(fill.X, fill.Y, fill.Width, fill.Height), UiPainter.Color8(fill.Color));
    }

    /// <summary>An entity's portrait (<c>Entity.PortraitImage</c>), or none, for the screen's own fallback.</summary>
    private RecordId? Portrait(RecordId entity) => _plugins.TryGet<EntityRecord>(entity)?.Portrait;
}
