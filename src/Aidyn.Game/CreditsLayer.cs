using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The credits over everything (game mode 0x13, <c>Credits.Frame</c>, <c>0x8001f648</c>): a black screen, and on it the
/// lines of <see cref="CreditsRoll"/> in the main font, each centred across. The session steps the roll a tick at a time
/// and carries out its end (<see cref="Core.GameSession.Credits"/>); this draws it.
/// </summary>
public partial class CreditsLayer : CanvasLayer
{
    private readonly UiScreen _screen = new();
    private readonly CreditsView _view;

    public CreditsLayer(PluginSet plugins)
    {
        Name = "Credits";
        Layer = PauseMenuView.MenuLayer + 1;
        Visible = false;
        var backdrop = new ColorRect { Name = "Backdrop", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);
        _view = new CreditsView(new UiPainter(plugins));
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.Canvas.AddChild(_view);
    }

    /// <summary>The UI screen the credits are on, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    /// <summary>Shows the black screen the credits roll on.</summary>
    public void Play() => Visible = true;

    /// <summary>Draws the roll as the session's tick left it.</summary>
    public void Draw(CreditsRoll? roll)
    {
        _view.Roll = roll;
        _view.QueueRedraw();
    }

    /// <summary>The credits have ended: the layer hides.</summary>
    public void End()
    {
        _view.Roll = null;
        Visible = false;
    }

    private sealed partial class CreditsView(UiPainter ui) : Control
    {
        public UiPainter Ui { get; } = ui;

        public CreditsRoll? Roll { get; set; }

        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

        public override void _Draw()
        {
            if (Roll is null)
                return;
            var width = (int)Size.X;
            foreach (var line in Roll.Lines)
                Ui.Text(this, Ui.Main, line.Text, (width - Ui.Main.Width(line.Text)) / 2, line.Y, line.Color);
        }
    }
}
