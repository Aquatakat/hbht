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
        if (Popup is not { Kind: TrekPopupKind.Choice } popup)
            return;
        popup.Step();
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
                _ui.Message(this, screen, popup.Body!);
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

    // WidgetChoiceDia::Update.
    private void Choice(Vector2I screen, TrekPopup popup) => _ui.Choice(this, screen, popup.Question!, popup.Highlight);
}
