using Aidyn.Core.Scripts;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a backdrop's or the help menu's page (<see cref="BackdropPage"/>) on a <see cref="UiScreen"/>'s canvas: the
/// picture in the original's 320 × 240 frame at the screen's centre, its edges feathered, the node's text and the
/// choices, all at the page's alpha, as the journal is drawn (<see cref="JournalView"/>).
/// </summary>
public partial class BackdropView : Control
{
    private const int FrameWidth = 320, FrameHeight = 240;

    /// <summary>
    /// How far in from each edge the picture fades out, left, top, right and bottom: the original's overscan bars, 16
    /// pixels at the sides and 12 at the top and bottom (<c>Graphics::EndList</c>), which hide the picture's raw edges on
    /// the console. The port draws no bars and shows the frame over the darkened world, so the picture fades to nothing
    /// over what they'd cover, and all of it the original shows stays whole. The port's own choice; the text and choices,
    /// from 30 down and 64 across at their nearest, are well inside it and aren't feathered.
    /// </summary>
    private static readonly (int Left, int Top, int Right, int Bottom) Feather =
        (DialogueBox.OverscanSide, DialogueBox.OverscanBottom, DialogueBox.OverscanSide, DialogueBox.OverscanBottom);

    private readonly UiPainter _ui;

    public BackdropView(PluginSet plugins)
    {
        _ui = new UiPainter(plugins);
        Name = "Backdrop page";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The page showing, or null.</summary>
    public BackdropPage? Page { get; set; }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (Page is not { Layout: { } layout } page)
            return;
        var size = new Vector2I((int)Size.X, (int)Size.Y);
        DrawSetTransform(new Vector2I((size.X - FrameWidth) / 2, (size.Y - FrameHeight) / 2));
        var alpha = page.Alpha / 255f;
        var style = layout.Style;
        Picture(style.Picture, alpha);
        Print(layout.Text, style.Text, style.Shadow, alpha);
        // The choices off the cursor are in the text's colour and shadow; the cursor's pulses, with no shadow
        // (WidgetScrollMenu.Step, 0x800b0248).
        for (var i = 0; i < layout.Choices.Count; i++)
        {
            if (i == layout.Highlight)
                Print(layout.Choices[i], page.HighlightNow, null, alpha);
            else
                Print(layout.Choices[i], style.Text, style.Shadow, alpha);
        }
    }

    /// <summary>
    /// The picture at (0, 0), at its own size, faded out over <see cref="Feather"/> at its edges, with round corners, as
    /// the original's feathered message box is (<see cref="EdgeFeather"/>), and at the page's alpha.
    /// </summary>
    private void Picture(RecordId image, float alpha)
    {
        if (_ui.Images.Feathered(image, Feather.Left, Feather.Top, Feather.Right, Feather.Bottom) is { } texture)
            DrawTexture(texture, Vector2.Zero, new Color(1, 1, 1, alpha));
    }

    /// <summary>Text at the page's alpha, over its shadow if the picture has one (<c>WidgetShadowText</c>).</summary>
    private void Print(TextBlock block, Rgba color, TextShadow? shadow, float alpha)
    {
        if (shadow is { } s)
        {
            var bounds = new ScreenRect(block.Bounds.Left + s.X, block.Bounds.Top + s.Y, block.Bounds.Right + s.X, block.Bounds.Bottom + s.Y);
            _ui.Text(this, _ui.Main, block.Text, block.X + s.X, block.Y + s.Y, bounds, s.Color with { A = (byte)(s.Color.A * alpha) });
        }
        _ui.Text(this, _ui.Main, block.Text, block.X, block.Y, block.Bounds, color with { A = (byte)(color.A * alpha) });
    }
}

/// <summary>
/// A backdrop's or the help menu's page over the world: the world and the HUD, still as the game stops them, darkened
/// to half as it fades in, as the pause menu darkens them (<see cref="PauseScroll.Dark"/>), and the page on a UI screen
/// of its own over them. The session makes and steps the page (<see cref="Core.GameSession.Backdrop"/>); this draws it.
/// The game is paused while it shows, and the world goes on being drawn under a black layer, so the window's size
/// redraws it. The original draws the world on behind its opaque page, which covers its whole
/// 320 × 240 frame; the port centres the frame on the screen, so the world shows either side on a wider one.
/// </summary>
public partial class BackdropLayer : CanvasLayer
{
    /// <summary>What darkens the world: black at one less the tint.</summary>
    private readonly ColorRect _darkness = new()
    {
        Name = "Darkness",
        Color = Colors.Transparent,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    private readonly UiScreen _screen = new();
    private readonly BackdropView _view;

    public BackdropLayer(PluginSet plugins)
    {
        Name = "Backdrop";
        Layer = PauseMenu.MenuLayer;
        Visible = false;
        _darkness.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_darkness);
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_screen);
        _view = new BackdropView(plugins);
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.Canvas.AddChild(_view);
    }

    /// <summary>The UI screen, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    /// <summary>The page drawn, from its opening until it has faded out: the session's (<see cref="Core.GameSession.Backdrop"/>).</summary>
    public BackdropPage? Page => _view.Page;

    /// <summary>
    /// Draws the session's page as it stands, each tick and after a press: shown as one opens, hidden once it has faded out,
    /// and the world's tint from bright to the pause menu's half as it fades in.
    /// </summary>
    public void Draw(BackdropPage? page)
    {
        _view.Page = page;
        Visible = page is not null;
        if (page is null)
            return;
        var alpha = page.Alpha / 255f;
        var tint = Mathf.Lerp(1f, PauseScroll.Dark / 255f, alpha);
        _darkness.Color = new Color(0, 0, 0, 1 - tint);
        _view.QueueRedraw();
    }
}
