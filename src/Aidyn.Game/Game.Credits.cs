using Aidyn.Core.Audio;
using Aidyn.Core.Ui;

namespace Aidyn.Game;

/// <summary>
/// The credits in the runtime (<see cref="CreditsLayer"/>): the System tab's Credits row rolls the menu up, the screen
/// cuts to black and they play over the world held still, its music and sounds going on silently under theirs. The
/// session steps them and ends them (<see cref="Core.GameSession.Credits"/>), fading trek in and reopening the menu; this
/// draws them and plays their music. See docs/systems/ui.md#the-credits.
/// </summary>
public partial class Game
{
    private CreditsLayer? _credits;
    private SoundHandle _creditsMusic;

    /// <summary>
    /// Shows the credits the session has started (<c>Credits.Start</c>, <c>0x8002c0b0</c>): their music from its start at
    /// the music volume, and the game's own muted, not stopped, until they end.
    /// </summary>
    private void PlayCredits()
    {
        _credits!.Play();
        if (_session!.Game.Sounds is { } sounds)
            _creditsMusic = _audio!.Start(sounds.Credits, GameAudio.Volume(_audio.MusicVolume), 0x80, loop: true);
        _audio!.Muted = true;
    }

    /// <summary>A tick of the credits, which the session stepped: their lines as they stand.</summary>
    private void DrawCredits() => _credits!.Draw(_session!.Credits?.Roll);

    /// <summary>
    /// The credits have ended (<c>Credits.Destroy</c>): their music stops at once, the game's comes back where it was, and
    /// the screen is as trek's fade leaves it, black as it fades in from the credits.
    /// </summary>
    private void EndCredits()
    {
        _credits!.End();
        _audio!.Stop(_creditsMusic);
        _creditsMusic = default;
        _audio.Muted = false;
        ShowFade(_session!.Trek.Brightness);
    }

    /// <summary>A, B or Start ends them; they take every press.</summary>
    private void CreditsInput(MenuAction action)
    {
        if (action is MenuAction.Accept or MenuAction.Back or MenuAction.Start)
            _session!.Credits?.Roll.Skip();
    }
}
