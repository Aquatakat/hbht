using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The pause menu's World tab, not ported yet: only its image, <c>0x00DA</c>, with the world map, the compass and
/// the calendar's days painted in, so the classic layout's scroll runs on past the Party tab as the original's does.
/// The tab can't be slid to until its widgets are ported.
/// </summary>
public partial class WorldTabView : Control
{
    private readonly UiPainter _ui;

    public WorldTabView(PluginSet plugins)
    {
        _ui = new UiPainter(plugins);
        Name = "World tab";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    public override void _Draw()
    {
        var origin = new Vector2I(((int)Size.X - UiScreen.Width) / 2, ((int)Size.Y - UiScreen.Height) / 2);
        _ui.Image(this, MenuImages.WorldTab, origin.X, origin.Y);
    }
}
