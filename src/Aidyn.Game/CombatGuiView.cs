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

        if (battle.Menu is { } menu)
            DrawMenu(menu, screen);
        if (battle.Aura is { } aura)
            DrawAura(aura, screen);
    }

    /// <summary>
    /// A held menu (<c>CombatMenu.Draw</c>, <c>0x80087E30</c>) at the original frame's centre: the feathered black panel round its list,
    /// each row's icon and its text in the main font, the cursor's row pulsing toward its colour, the scroll arrows; or with nothing to
    /// list, its message.
    /// </summary>
    private void DrawMenu(CombatMenu menu, Vector2I screen)
    {
        if (menu.Empty is { } empty)
        {
            _ui.Message(this, screen, new MenuMessage(empty));
            return;
        }
        var (ox, oy) = ((screen.X - 320) / 2, (screen.Y - 240) / 2);
        var textHeight = _ui.Main.Layout.LineHeight();
        var heights = menu.Rows.Select(r => menu.RowHeight(IconSize(r.Icon).Y, textHeight)).ToList();
        var layout = menu.Layout(heights);
        var list = layout.List;
        var band = new ScreenRect(list.Left + ox, list.Top + oy, list.Right + ox, list.Bottom + oy);
        _ui.Images.DrawPanel(this, _ui.Images.Around(band));
        var clip = new Rect2I(band.Left, band.Top, band.Right - band.Left, band.Bottom - band.Top);
        for (var i = 0; i < menu.Rows.Count; i++)
        {
            var row = menu.Rows[i];
            var top = band.Top + layout.RowTops[i] - layout.Scroll;
            if (top + heights[i] <= band.Top || top >= band.Bottom)
                continue;
            var color = i == menu.Cursor ? menu.CursorColor(row.Color) : row.Color;
            if (row.Icon is { } icon)
                _ui.Image(this, icon, band.Left, top, clip: clip);
            var x = band.Left + menu.IconWidth + 5;
            _ui.Text(this, _ui.Main, row.Text, x, top + menu.TextTop(textHeight), new ScreenRect(x, band.Top, band.Right + 40, band.Bottom), color, clip);
        }
        if (layout.UpArrow)
            _ui.Image(this, CombatMenu.UpArrow, band.Left - 9, band.Top - 2 - IconSize(CombatMenu.UpArrow).Y);
        if (layout.DownArrow)
            _ui.Image(this, CombatMenu.DownArrow, band.Left - 9, band.Bottom + 2);
    }

    /// <summary>
    /// Sense Aura's popup (<c>Entity.SenseAura</c>'s message box, the Party tab's): its lines, white, centred, as wide as its longest up
    /// to seven pixels a character.
    /// </summary>
    private void DrawAura(string aura, Vector2I screen)
    {
        var lines = aura.Split('\n');
        var wrap = Math.Min(7 * lines.Max(l => l.Length), lines.Max(_ui.Main.Width));
        _ui.Message(this, screen, aura, wrap, MenuMessage.Color);
    }

    private Vector2I IconSize(RecordId? image) => image is { } id && _ui.Images.Get(id) is { } texture ? (Vector2I)texture.GetSize() : Vector2I.Zero;

    private void Fill(IEnumerable<HudFill> fills)
    {
        foreach (var fill in fills)
            DrawRect(new Rect2(fill.X, fill.Y, fill.Width, fill.Height), UiPainter.Color8(fill.Color));
    }

    /// <summary>An entity's portrait (<c>Entity.PortraitImage</c>), or none, for the screen's own fallback.</summary>
    private RecordId? Portrait(RecordId entity) => _plugins.TryGet<EntityRecord>(entity)?.Portrait;
}
