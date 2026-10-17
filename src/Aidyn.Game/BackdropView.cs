using Aidyn.Core.Scripts;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a backdrop's or the help menu's page (<see cref="BackdropPage"/>) on a <see cref="UiScreen"/>'s canvas: the
/// picture in the original's 320 × 240 frame at the screen's centre, the node's text and the choices, all at the
/// page's alpha, as the journal is drawn (<see cref="JournalView"/>).
/// </summary>
public partial class BackdropView : Control
{
    private const int FrameWidth = 320, FrameHeight = 240;

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
        _ui.Image(this, style.Picture, 0, 0, color: new Rgba(255, 255, 255, (byte)page.Alpha));
        Print(layout.Text, style.Text, style.Shadow, alpha);
        for (var i = 0; i < layout.Choices.Count; i++)
            Print(layout.Choices[i], layout.First + i == layout.Highlight ? page.HighlightNow : style.Choice, style.Shadow, alpha);
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
/// A backdrop's or the help menu's page over the world: the world as it was when the page opened, darkened to half as
/// it fades in, as the pause menu darkens it (<see cref="PauseScroll.Dark"/>), and the page on a UI screen of its own
/// over it. The game is paused while it shows. The original draws the world on behind its opaque page, which covers
/// its whole 320 × 240 frame; the port centres the frame on the screen, so the world shows either side on a wider one.
/// </summary>
public partial class BackdropLayer : CanvasLayer
{
    private readonly TextureRect _world = new()
    {
        Name = "World",
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    private readonly UiScreen _screen = new();
    private readonly BackdropView _view;
    private readonly TextLayout _font;

    public BackdropLayer(PluginSet plugins)
    {
        Name = "Backdrop";
        Layer = PauseMenu.MenuLayer;
        Visible = false;
        _font = new TextLayout(plugins.Get<FontRecord>(RecordId.Decimal(RecordTypes.Font, 0)));
        _world.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_world);
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_screen);
        _view = new BackdropView(plugins);
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.Canvas.AddChild(_view);
    }

    /// <summary>The UI screen, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    /// <summary>The page, from its opening until it has faded out.</summary>
    public BackdropPage? Page { get; private set; }

    /// <summary>Whether a page shows, or is still fading out.</summary>
    public bool Showing => Page is not null;

    /// <summary>Whether the page takes presses: once open, and until it closes.</summary>
    public bool Taking => Page is { Closing: false };

    /// <summary>The name the game writes where text says <c>{player}</c>: the first party member's.</summary>
    public string PlayerName { get; set; } = "Alaron";

    /// <summary>The page has faded out, and the game goes on.</summary>
    public event Action? Closed;

    /// <summary>
    /// Opens a page in a picture's style over the world as the next frame draws it, once the game has hidden what a
    /// page hides, such as the HUD, which the original draws only in trek.
    /// </summary>
    public void Open(BackdropStyle style)
    {
        Page = new BackdropPage(_font, style);
        _view.Page = Page;
        _world.Texture = null;
        RenderingServer.FramePostDraw += Snap;
    }

    private void Snap()
    {
        RenderingServer.FramePostDraw -= Snap;
        if (Page is null)
            return;
        _world.Texture = ImageTexture.CreateFromImage(GetViewport().GetTexture().GetImage());
        Visible = true;
        Tint();
    }

    /// <summary>Shows a node's text and choices, with the name placeholders filled in.</summary>
    public void Show(ScriptPage page)
    {
        Page?.Show(new ScriptPage(Named(page.Line), [.. page.Choices.Select(Named)]));
        _view.QueueRedraw();
    }

    /// <summary>Moves the cursor.</summary>
    public void MoveCursor(int by)
    {
        Page?.MoveCursor(by);
        _view.QueueRedraw();
    }

    /// <summary>The page fades out, and the game goes on once it has.</summary>
    public void Close() => Page?.Close();

    public override void _PhysicsProcess(double delta)
    {
        if (Page is not { } page || _world.Texture is null)
            return;
        page.Step();
        if (page.Closed)
        {
            Page = null;
            _view.Page = null;
            _world.Texture = null;
            Visible = false;
            Closed?.Invoke();
            return;
        }
        Tint();
    }

    /// <summary>The world's tint, from bright to the pause menu's half as the page fades in.</summary>
    private void Tint()
    {
        var alpha = Page!.Alpha / 255f;
        var tint = Mathf.Lerp(1f, PauseScroll.Dark / 255f, alpha);
        _world.SelfModulate = new Color(tint, tint, tint);
        _view.QueueRedraw();
    }

    private ScriptLine Named(ScriptLine line) => line with { Text = line.Text.Replace("{player}", PlayerName, StringComparison.Ordinal) };
}
