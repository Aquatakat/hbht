using Aidyn.Core.Audio;
using Aidyn.Core.Trek;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The credits in the runtime (<see cref="CreditsLayer"/>): the System tab's Credits row rolls the menu up, the screen
/// cuts to black and they play over the world held still, its music and sounds going on silently under theirs; when
/// they end trek fades in and the menu reopens. See docs/systems/ui.md#the-credits.
/// </summary>
public partial class Game
{
    private CreditsLayer? _credits;
    private SoundHandle _creditsMusic;

    /// <summary>Trek has come back from the credits, and the menu reopens once it has faded in (trek's state 8).</summary>
    private bool _reopenAfterCredits;

    private bool InCredits => _credits is { Playing: true };

    /// <summary>
    /// Plays the credits (<c>Credits.Start</c>, <c>0x8002c0b0</c>): their music from its start at the music volume, and
    /// the game's own muted, not stopped, until they end.
    /// </summary>
    private void PlayCredits()
    {
        if (_credits is null || InCredits || !_credits.Play(ending: false))
            return;
        if (_game!.Sounds is { } sounds)
            _creditsMusic = _audio!.Start(sounds.Credits, GameAudio.Volume(_audio.MusicVolume), 0x80, loop: true);
        _audio!.Muted = true;
    }

    /// <summary>
    /// A tick of the credits, with the map's sounds kept up silently, and their end (<c>Credits.Destroy</c>): their
    /// music stops at once, the game's comes back where it was, and trek fades in from black over half a second.
    /// </summary>
    private void StepCredits()
    {
        if (_scene is null)
            _trek!.StepSounds();
        if (!_credits!.Step())
            return;
        _audio!.Stop(_creditsMusic);
        _creditsMusic = default;
        _audio.Muted = false;
        _trek!.CutSceneFade.In(TrekLoop.CutSceneFadePerTick);
        ShowFade(_trek.Brightness);
        _reopenAfterCredits = true;
    }

    /// <summary>Once trek has faded in after the credits, the menu reopens on the System tab's list.</summary>
    private void ReopenAfterCredits()
    {
        if (!_reopenAfterCredits || _trek!.CutSceneFade.Fading)
            return;
        _reopenAfterCredits = false;
        _menu!.Open();
    }

    /// <summary>A, B or Start ends them; they take every press.</summary>
    private void CreditsInput(InputEvent e)
    {
        if (e.IsActionPressed("menu_accept") || e.IsActionPressed("menu_back") || e.IsActionPressed("pause"))
            _credits!.Roll?.Skip();
    }
}
