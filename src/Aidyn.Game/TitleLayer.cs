using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The title screen over the world (<see cref="TitleScreen"/>): the intro cards on black, then over the fly-through's scenery the logo
/// and PRESS START, and the menu, the System tab's pages on their feathered boxes (<see cref="SystemTabView"/> in the title's style).
/// All of it is drawn in the original's 320 × 240 frame at the centre of a UI screen of its own, so it shrinks with the Menu Size
/// setting as the menus do. The session steps the title; this draws what each tick left (<see cref="Draw"/>).
/// </summary>
public partial class TitleLayer : CanvasLayer
{
    private readonly UiScreen _screen = new() { ForwardsMouse = true };
    private readonly ColorRect _black = new() { Name = "Cards' black", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly ColorRect _fade = new() { Name = "Fade", Color = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly TitleView _view;
    private readonly NintendoLogoView _logo;
    private SystemTabView? _menu;
    private readonly PluginSet _plugins;

    public TitleLayer(PluginSet plugins, Models models)
    {
        _plugins = plugins;
        Name = "Title";
        // Over the cinematic's and the credits' layers: nothing else shows while the title does.
        Layer = PauseMenuView.MenuLayer + 2;
        Visible = false;
        _black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_black);
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);
        // The N64 logo's scene under its caption.
        _logo = new NintendoLogoView(models) { Visible = false };
        _screen.Canvas.AddChild(_logo);
        _view = new TitleView(new UiPainter(plugins), plugins.All<StringsRecord>(RecordTypes.Strings).FirstOrDefault(s => s.Name == "Copyright")?.Lines ?? []);
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.Canvas.AddChild(_view);
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_fade);
    }

    /// <summary>The UI screen the title is on, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    /// <summary>Shows the title the session has started: its cards on black, or the title over the scenery.</summary>
    public void Show(TitleScreen title)
    {
        _view.Title = title;
        Visible = true;
        Draw(title);
    }

    /// <summary>Draws the tick the session stepped: the card at its alpha, or the logo, PRESS START and the menu, and the fade out as it ends.</summary>
    public void Draw(TitleScreen title)
    {
        _view.Title = title;
        _black.Visible = title.Phase == TitlePhase.Cards;
        var cards = title.Cards;
        _logo.Visible = cards is { Ended: false, Card: IntroCard.Nintendo };
        if (_logo.Visible)
            _logo.Pose(cards!.LogoAngle, cards.Alpha, _view.Origin);
        if (title.Menu is { } menu)
        {
            if (_menu is null)
            {
                _menu = new SystemTabView(_plugins, menu);
                _screen.Canvas.AddChild(_menu);
            }
            _menu.Menu = menu;
            _menu.QueueRedraw();
        }
        _fade.Color = new Color(0, 0, 0, 1 - title.Brightness);
        _view.QueueRedraw();
    }

    /// <summary>The title has ended: the layer hides, and the menu's view goes with this title's menu.</summary>
    public void End()
    {
        Visible = false;
        _view.Title = null;
        _logo.Visible = false;
        _menu?.QueueFree();
        _menu = null;
    }

    /// <summary>Draws again at once what a press changed.</summary>
    public void Redraw()
    {
        _view.QueueRedraw();
        _menu?.QueueRedraw();
    }

    /// <summary>The cards and the logo, in the 320 × 240 frame at the screen's centre.</summary>
    private sealed partial class TitleView(UiPainter ui, IReadOnlyList<string> legal) : Control
    {
        private const int FrameWidth = 320, FrameHeight = 240;

        public TitleScreen? Title { get; set; }

        /// <summary>Where the frame's corner is on the screen.</summary>
        public Vector2I Origin => new(((int)Size.X - FrameWidth) / 2, ((int)Size.Y - FrameHeight) / 2);

        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

        public override void _Notification(int what)
        {
            if (what == NotificationResized)
                QueueRedraw();
        }

        public override void _Draw()
        {
            if (Title is not { } title)
                return;
            DrawSetTransform(Origin);
            if (title.Cards is { Ended: false } cards)
                Card(cards);
            else if (title.LogoAlpha > 0)
                Logo(title);
            DrawSetTransform(Vector2.Zero);
        }

        /// <summary>
        /// A card at its fade: an image centred, white at the fade's alpha (<c>Title.DrawCardImage</c>, <c>0x8001b778</c>); the legal
        /// text's lines, every byte of their colour the alpha; or the N64 logo's caption under its scene, drawn by its own view.
        /// </summary>
        private void Card(IntroCards cards)
        {
            var a = (byte)cards.Alpha;
            if (a == 0)
                return;
            switch (cards.Card)
            {
                case IntroCard.ExpansionPak:
                    Centred(IntroCards.ExpansionPakImage, new Rgba(255, 255, 255, a));
                    break;
                case IntroCard.Thq:
                    Centred(IntroCards.ThqImage, new Rgba(255, 255, 255, a));
                    break;
                case IntroCard.H2o:
                    Centred(IntroCards.H2oImage, new Rgba(255, 255, 255, a));
                    break;
                case IntroCard.Nintendo:
                    var caption = ui.Size(IntroCards.LicensedImage);
                    ui.Image(this, IntroCards.LicensedImage, FrameWidth / 2 - caption.X / 2, IntroCards.LicensedTop, color: new Rgba(a, a, a, a));
                    break;
                case IntroCard.Legal:
                    Legal(new Rgba(a, a, a, a));
                    break;
            }
        }

        /// <summary>The Copyright table's lines (<c>Title.CardLegal</c>, <c>0x8001b9bc</c>), each centred, in paragraphs.</summary>
        private void Legal(Rgba color)
        {
            var y = IntroCards.LegalTop;
            for (var i = 0; i < legal.Count; i++)
            {
                ui.Text(this, ui.Slim, legal[i], (FrameWidth - ui.Slim.Width(legal[i])) / 2, y, color);
                y += IntroCards.LegalPitch + (IntroCards.LegalBreaks.Contains(i) ? IntroCards.LegalGap : 0);
            }
        }

        /// <summary>The logo's shadow, the logo over it, and PRESS START while it blinks on, all at the logo's alpha.</summary>
        private void Logo(TitleScreen title)
        {
            var color = new Rgba(255, 255, 255, (byte)title.LogoAlpha);
            ui.Image(this, TitleScreen.LogoShadow, TitleScreen.LogoShadowAt.X, TitleScreen.LogoShadowAt.Y, color: color);
            ui.Image(this, TitleScreen.Logo, TitleScreen.LogoAt.X, TitleScreen.LogoAt.Y, color: color);
            if (title.PressStartShown && title.Menu is null)
                ui.Image(this, TitleScreen.PressStart, TitleScreen.PressStartAt.X, TitleScreen.PressStartAt.Y, color: color);
        }

        private void Centred(RecordId image, Rgba color)
        {
            var size = ui.Size(image);
            ui.Image(this, image, (FrameWidth - size.X) / 2, (FrameHeight - size.Y) / 2, color: color);
        }
    }

    /// <summary>
    /// The N64 logo's card (<c>Title.CardN64</c>, <c>0x8001bba8</c>): scene <c>0x25A8</c> in a world of its own, seen from a camera going
    /// round it 40 out and 15 up, looking at its origin, with the scene's own 45° and planes of 1 and 256, tinted by the fade. It's drawn in
    /// the frame on the title's UI screen, so it shrinks with the menus.
    /// </summary>
    private sealed partial class NintendoLogoView : SubViewportContainer
    {
        private readonly SubViewport _viewport = new() { OwnWorld3D = true, HandleInputLocally = false, Size = new Vector2I(320, 240) };
        private readonly Camera3D _camera = new() { Fov = 45, Near = 1, Far = 256 };
        private readonly Models _models;
        private Node3D? _scene;
        private OwnLights? _lights;

        public NintendoLogoView(Models models)
        {
            _models = models;
            Name = "N64 logo";
            Stretch = true;
            MouseFilter = MouseFilterEnum.Ignore;
            Size = new Vector2(320, 240);
            var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black };
            OnScreenOutput.Apply(environment);
            _viewport.AddChild(new WorldEnvironment { Environment = environment });
            _viewport.AddChild(_camera);
            AddChild(_viewport);
        }

        /// <summary>The camera at its angle round the logo, the logo tinted by the card's alpha, and the view at the frame's corner.</summary>
        public void Pose(float degrees, int alpha, Vector2I origin)
        {
            if (_scene is null && _models.Instantiate(IntroCards.NintendoLogo) is { } scene)
            {
                _scene = scene;
                _viewport.AddChild(scene);
                if (_models.Glb(IntroCards.NintendoLogo) is { } glb)
                    _lights = new OwnLights(scene, glb.Extras);
                _camera.MakeCurrent();
            }
            var angle = Mathf.DegToRad(degrees);
            _camera.LookAtFromPosition(new Vector3(Mathf.Sin(angle) * IntroCards.LogoDistance, IntroCards.LogoHeight, Mathf.Cos(angle) * IntroCards.LogoDistance),
                Vector3.Zero, Vector3.Up);
            if (_scene is not null)
                N64Material.SetEye(_scene, _camera.GlobalPosition);
            _lights?.Apply();
            var shade = alpha / 255f;
            Modulate = new Color(shade, shade, shade);
            Position = origin;
            Size = new Vector2(320, 240);
        }
    }
}
