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
/// <c>Cinematic.StepFades</c>). Each shot's music plays through it. The game steps it a tick at a time and carries out
/// its end.
/// </summary>
public partial class CinematicLayer : CanvasLayer
{
    private readonly CinematicView _view;
    private readonly UiScreen _screen = new();
    private readonly CaptionView _captions;
    private readonly ColorRect _fade = new() { Name = "Fade", Color = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly GameAudio? _audio;
    private SoundHandle _music;

    public CinematicLayer(PluginSet plugins, Models models, GameAudio? audio)
    {
        Name = "Cinematic";
        Layer = PauseMenu.MenuLayer + 1;
        Visible = false;
        _audio = audio;
        _view = new CinematicView(models);
        _view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_view);
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_screen);
        _captions = new CaptionView(plugins);
        _captions.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.Canvas.AddChild(_captions);
        _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_fade);
    }

    /// <summary>The UI screen the captions are on, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    /// <summary>The 3D view, whose antialiasing the game sets.</summary>
    public SubViewport Viewport => _view.Viewport;

    /// <summary>The cinematic playing, until its end has been carried out.</summary>
    public CinematicPlayer? Player { get; private set; }

    /// <summary>Whether it stands still where it is, for screenshots.</summary>
    public bool Held { get; set; }

    /// <summary>Whether one is playing.</summary>
    public bool Playing => Player is not null;

    /// <summary>Starts a cinematic, the party leader's name in its captions in place of Alaron's.</summary>
    public void Play(CinematicRecord cinematic, string leader)
    {
        Player = new CinematicPlayer(cinematic, leader);
        _captions.Caption = null;
        _fade.Color = Colors.Transparent;
        Visible = true;
    }

    /// <summary>Steps on silently to a shot's tick, for the command line's screenshots, and shows it.</summary>
    public void SkipTo(int shot, int time)
    {
        if (Player is not { } player)
            return;
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

    /// <summary>B or Start: the rest is skipped.</summary>
    public void Skip() => Player?.Skip();

    /// <summary>One tick. Returns the cinematic once it has ended, and hides.</summary>
    public CinematicRecord? Step()
    {
        if (Player is not { } player || Held)
            return null;
        player.Step();
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
        if (!player.Ended)
            return null;
        StopMusic();
        _view.Clear();
        Visible = false;
        Player = null;
        return player.Cinematic;
    }

    /// <summary>
    /// The shot's music, looped and centred at the louder of the two volumes as they are now (<c>Cinematic.StartShot</c>).
    /// </summary>
    private void StartMusic(CinematicShot shot)
    {
        StopMusic();
        if (_audio is null)
            return;
        var volume = Math.Max(GameAudio.Volume(_audio.SoundVolume), GameAudio.Volume(_audio.MusicVolume));
        _music = _audio.Start(shot.Music, volume, GameAudio.Pan(0), loop: true);
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
