using Aidyn.Core.Audio;
using Aidyn.Core.Cinematics;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// A cinematic over everything (game mode 0xE): the shot (<see cref="CinematicView"/>), the caption box on a UI screen
/// of its own, and the fade over both, as the original draws them (<c>Cinematic.DrawShot</c>, <c>0x80053d94</c>, then
/// <c>Cinematic.StepFades</c>). Each shot's music plays through it. The session steps it a tick at a time and carries out
/// its end (<see cref="Core.GameSession.Cinematic"/>); this draws what each tick left (<see cref="Draw"/>). All three are
/// framed to the cinematic's aspect ratio, 4:3 for the original's, with black beside or above and below, since the sets
/// end beyond the console's screen.
/// </summary>
public partial class CinematicLayer : CanvasLayer
{
    private readonly CinematicView _view;
    private readonly UiScreen _screen = new();
    private readonly CaptionView _captions;
    private readonly ColorRect _fade = new() { Name = "Fade", Color = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly AspectRatioContainer _frame = new()
    {
        Name = "Frame",
        StretchMode = AspectRatioContainer.StretchModeEnum.Fit,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    private readonly GameAudio? _audio;
    private SoundHandle _music;

    public CinematicLayer(PluginSet plugins, Models models, GameAudio? audio)
    {
        Name = "Cinematic";
        Layer = PauseMenu.MenuLayer + 1;
        Visible = false;
        _audio = audio;
        var backdrop = new ColorRect { Name = "Backdrop", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);
        _frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_frame);
        // The container sizes only its own children, so the shot, captions and fade fill one that it sizes.
        var picture = new Control { Name = "Picture", MouseFilter = Control.MouseFilterEnum.Ignore };
        _frame.AddChild(picture);
        _view = new CinematicView(models);
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        picture.AddChild(_view);
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.MouseFilter = Control.MouseFilterEnum.Ignore;
        picture.AddChild(_screen);
        _captions = new CaptionView(plugins);
        _captions.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.Canvas.AddChild(_captions);
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        picture.AddChild(_fade);
    }

    /// <summary>The UI screen the captions are on, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    /// <summary>The 3D view, whose antialiasing the game sets.</summary>
    public SubViewport Viewport => _view.Viewport;

    /// <summary>Shows a cinematic the session has started, framed to its aspect ratio, with nothing drawn yet.</summary>
    public void Play(CinematicRecord cinematic)
    {
        _frame.Ratio = cinematic.Aspect;
        _captions.Caption = null;
        _fade.Color = Colors.Transparent;
        Visible = true;
    }

    /// <summary>Steps the player on silently to a shot's tick, for the command line's screenshots, and shows it.</summary>
    public void SkipTo(CinematicPlayer player, int shot, int time)
    {
        while (!player.Ended && !(player.ShotIndex == shot && player.Shot is not null && player.Time >= time))
            player.Step();
        if (player.Shot is not { } current)
            return;
        _view.Show(current);
        StartMusic(current);
        _view.Pose(player.Time);
        _captions.Caption = player.Caption;
        _fade.Color = Color.Color8(player.Fade.R, player.Fade.G, player.Fade.B, player.Fade.A);
    }

    /// <summary>
    /// Draws the tick the session stepped: a shot that started shows with its music, a shot freed stops its music, the
    /// picture holding until the next, and a tick that drew poses the shot at its time with its caption and fade.
    /// </summary>
    public void Draw(CinematicPlayer player)
    {
        if (player.ShotStarted && player.Shot is { } shot)
        {
            _view.Show(shot);
            StartMusic(shot);
        }
        else if (player.Shot is null)
        {
            // The shot is freed with its music (Cinematic.FreeShot, 0x80053ad8); the picture holds until the next.
            StopMusic();
        }
        if (player.Drew)
        {
            _view.Pose(player.Time);
            _captions.Caption = player.Caption;
            var f = player.Fade;
            _fade.Color = Color.Color8(f.R, f.G, f.B, f.A);
        }
    }

    /// <summary>The cinematic has ended: its music stops, its picture goes and the layer hides.</summary>
    public void End()
    {
        StopMusic();
        _view.Clear();
        Visible = false;
    }

    /// <summary>
    /// The shot's music, looped and centred (the pan 0x80, <c>0x80053ed4</c>) at the louder of the two volumes as they are now (<c>Cinematic.StartShot</c>).
    /// </summary>
    private void StartMusic(CinematicShot shot)
    {
        StopMusic();
        if (_audio is null)
            return;
        var volume = Math.Max(GameAudio.Volume(_audio.SoundVolume), GameAudio.Volume(_audio.MusicVolume));
        _music = _audio.Start(shot.Music, volume, 0x80, loop: true);
    }

    private void StopMusic()
    {
        if (_audio is not null && !_music.Equals(default(SoundHandle)))
            _audio.Stop(_music);
        _music = default;
    }

    /// <summary>The caption box (<see cref="CaptionBox"/>) and its caption, in the main font.</summary>
    private sealed partial class CaptionView : Control
    {
        private readonly UiPainter _ui;
        private string? _caption;

        public CaptionView(PluginSet plugins)
        {
            _ui = new UiPainter(plugins);
            Name = "Captions";
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public string? Caption
        {
            get => _caption;
            set
            {
                if (_caption == value)
                    return;
                _caption = value;
                QueueRedraw();
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationResized)
                QueueRedraw();
        }

        public override void _Draw()
        {
            if (_caption is null)
                return;
            var layout = CaptionBox.Layout(_ui.Main.Layout, _caption, (int)Size.X, (int)Size.Y);
            _ui.Images.DrawPanel(this, layout.Panel);
            var text = layout.Text;
            _ui.Text(this, _ui.Main, text.Text, text.X, text.Y, text.Bounds, CaptionBox.TextColor);
        }
    }
}
