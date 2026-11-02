using HBHT.Core;
using HBHT.Core.MapObjects;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Draws the popup trek shows over the world (<see cref="TrekPopup"/>) on a <see cref="UiScreen"/>'s
/// canvas, at its centre: a message, the trap's damage, or a question over choices.
/// </summary>
public partial class TrekPopupView : Control
{
    private readonly UiPainter _ui;

    /// <summary>The popup to draw, or none.</summary>
    public TrekPopup? Popup
    {
        get;
        set
        {
            field = value;
            QueueRedraw();
        }
    }

    /// <summary>A popup fading out after its press, drawn under the one up.</summary>
    public TrekPopup? Leaving
    {
        get;
        set
        {
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

    /// <summary>Draws a tick on: the choice's pulse and the fades, which trek steps (<see cref="MapObjectUse.Step"/>).</summary>
    public void Step()
    {
        if (Popup is { Kind: TrekPopupKind.Choice } or { IsOpen: false } || Leaving is not null)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (Leaving is { } leaving)
            DrawPopup(leaving);
        if (Popup is { } popup)
            DrawPopup(popup);
    }

    private void DrawPopup(TrekPopup popup)
    {
        var screen = (Vector2I)Size;
        // The fade is the whole popup's, its box and text alike.
        var opacity = _ui.Opacity;
        _ui.Opacity = opacity * popup.Alpha / 255f;
        switch (popup.Kind)
        {
            case TrekPopupKind.Message:
                _ui.Message(this, screen, popup.Body!);
                break;
            case TrekPopupKind.Damage:
                Damage(screen, popup);
                break;
            case TrekPopupKind.Choice:
                Choice(screen, popup);
                break;
        }
        _ui.Opacity = opacity;
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

    // WidgetChoiceDia::Update.
    private void Choice(Vector2I screen, TrekPopup popup) => _ui.Choice(this, screen, popup.Question!, popup.Highlight);
}
