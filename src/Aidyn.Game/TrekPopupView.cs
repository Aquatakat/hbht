using Aidyn.Core;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the popup trek shows over the world (<see cref="TrekPopup"/>) on a <see cref="UiScreen"/>'s
/// canvas, at its centre: a message, the trap's damage, or a question over choices.
/// </summary>
public partial class TrekPopupView : Control
{
    /// <summary>The cursor's option pulses between these over 20 frames each way (<c>WidgetScrollMenu</c>'s defaults).</summary>
    private static readonly Rgba PulseFrom = new(100, 100, 100, 100), PulseTo = new(255, 255, 255, 255);

    private const int PulseFrames = 20;

    private readonly UiPainter _ui;
    private int _pulse, _pulseSign = 1, _ticks;

    /// <summary>The popup to draw, or none.</summary>
    public TrekPopup? Popup
    {
        get;
        set
        {
            if (field != value)
                (_pulse, _pulseSign) = (0, 1);
            field = value;
            QueueRedraw();
        }
    }

    public TrekPopupView(PluginSet plugins)
    {
        _ui = new UiPainter(plugins);
        Name = "Trek popup";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    /// <summary>Steps the cursor's pulse by a 60 Hz tick.</summary>
    public void Step()
    {
        if (Popup is not { Kind: TrekPopupKind.Choice } || ++_ticks < Cadence.FrameTicks)
            return;
        _ticks = 0;
        _pulse += _pulseSign;
        if (_pulse == 0 || _pulse >= PulseFrames - 1)
            _pulseSign = -_pulseSign;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Popup is not { } popup)
            return;
        var screen = (Vector2I)Size;
        switch (popup.Kind)
        {
            case TrekPopupKind.Message:
                _ui.Message(this, screen, popup.Text, popup.Wrap, popup.Color);
                break;
            case TrekPopupKind.Damage:
                Damage(screen, popup);
                break;
            case TrekPopupKind.Choice:
                Choice(screen, popup);
                break;
        }
    }

    // TrapMenu::TakeDamage: 16 wider than the widest line, and each line as tall as the first's height plus 2.
    private void Damage(Vector2I screen, TrekPopup popup)
    {
        var lines = popup.Text.Split('\n');
        var font = _ui.Main;
        var width = lines.Max(font.Width) + 16;
        var height = (font.Layout.Height(lines[0], 0, UiScreen.Width) + 2) * lines.Length;
        _ui.MessageBox(this, screen, popup.Text, width, height, popup.Color);
    }

    // WidgetChoiceDia::Update: the question and its options centred on the screen, a gap between them,
    // on a feathered panel as wide as the popup.
    private void Choice(Vector2I screen, TrekPopup popup)
    {
        var font = _ui.Main;
        var half = popup.Wrap / 2;
        var centre = new Vector2I(screen.X / 2, screen.Y / 2);
        var (left, right) = (centre.X - half, centre.X + half);
        var textHeight = font.Layout.Height(popup.Text, left, right);
        var rows = popup.Choices.Rows;
        var rowHeight = font.LineHeight;
        var top = centre.Y - (rows.Count * rowHeight + textHeight + popup.Gap) / 2;
        var choicesTop = top + textHeight + popup.Gap;
        var bottom = choicesTop + rows.Count * rowHeight;
        _ui.Images.DrawPanel(this, _ui.Images.Around(new ScreenRect(left, top, right, bottom)));
        _ui.Text(this, font, popup.Text, left, top, new ScreenRect(left, top, right, choicesTop), popup.Color);
        for (var i = 0; i < rows.Count; i++)
        {
            var color = i == popup.Choices.Cursor ? Pulse() : popup.Color;
            _ui.Text(this, font, rows[i], left - 1, choicesTop + i * rowHeight, new ScreenRect(left - 1, choicesTop, right + 1, bottom), color);
        }
    }

    private Rgba Pulse() => new(Blend(PulseFrom.R, PulseTo.R), Blend(PulseFrom.G, PulseTo.G), Blend(PulseFrom.B, PulseTo.B), Blend(PulseFrom.A, PulseTo.A));

    private byte Blend(byte from, byte to) => (byte)(from + (to - from) / PulseFrames * _pulse);
}
