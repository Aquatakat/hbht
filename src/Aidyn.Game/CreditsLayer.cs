using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The credits over everything (game mode 0x13, <c>Credits.Frame</c>, <c>0x8001f648</c>): a black screen, and on it the
/// lines of <see cref="CreditsRoll"/> in the main font, each centred across. The game steps it a tick at a time and
/// carries out its end.
/// </summary>
public partial class CreditsLayer : CanvasLayer
{
    private readonly UiScreen _screen = new();
    private readonly CreditsView _view;
    private readonly CreditsRecord? _credits;

    public CreditsLayer(PluginSet plugins)
    {
        Name = "Credits";
        Layer = PauseMenu.MenuLayer + 1;
        Visible = false;
        _credits = plugins.TrySingleton<CreditsRecord>();
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

    /// <summary>The credits rolling, until their end has been carried out.</summary>
    public CreditsRoll? Roll => _view.Roll;

    /// <summary>Whether they're rolling.</summary>
    public bool Playing => _view.Roll is not null;

    /// <summary>Starts the credits from their beginning, on a screen as tall as the UI's. False if the plugins have none.</summary>
    public bool Play(bool ending)
    {
        if (_credits is null)
            return false;
        _view.Roll = new CreditsRoll(_credits, _view.Ui.Main.LineHeight, _screen.CanvasSize.Y, ending);
        Visible = true;
        return true;
    }

    /// <summary>One tick. True once they've ended, and they hide.</summary>
    public bool Step()
    {
        if (_view.Roll is not { } roll)
            return false;
        roll.Step();
        _view.QueueRedraw();
        if (!roll.Ended)
            return false;
        _view.Roll = null;
        Visible = false;
        return true;
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
