using Aidyn.Core.Audio;
using Aidyn.Core.Ui;

namespace Aidyn.Game;

/// <summary>
/// The credits in the runtime (<see cref="CreditsLayer"/>): the System tab's Credits row rolls the menu up, the screen
/// cuts to black and they play over the world held still, its music and sounds going on silently under theirs; when
/// they end the session fades trek in and reopens the menu (<see cref="Core.GameSession.CreditsEnded"/>). See
/// docs/systems/ui.md#the-credits.
/// </summary>
public partial class Game
{
    private CreditsLayer? _credits;
    private SoundHandle _creditsMusic;

    /// <summary>
    /// Plays the credits (<c>Credits.Start</c>, <c>0x8002c0b0</c>): their music from its start at the music volume, and
    /// the game's own muted, not stopped, until they end.
    /// </summary>
    private void PlayCredits(bool ending)
    {
        if (!_credits!.Play(ending))
            return;
        if (_session!.Game.Sounds is { } sounds)
            _creditsMusic = _audio!.Start(sounds.Credits, GameAudio.Volume(_audio.MusicVolume), 0x80, loop: true);
        _audio!.Muted = true;
    }

    /// <summary>
    /// A tick of the credits, and their end (<c>Credits.Destroy</c>): their music stops at once, the game's comes back where
    /// it was, and the session fades trek in from black over half a second.
    /// </summary>
    private void StepCredits()
    {
        if (!_credits!.Step())
            return;
        _audio!.Stop(_creditsMusic);
        _creditsMusic = default;
        _audio.Muted = false;
        _session!.CreditsEnded();
        ShowFade(_session.Trek.Brightness);
    }

    /// <summary>A, B or Start ends them; they take every press.</summary>
    private void CreditsInput(MenuAction action)
    {
        if (action is MenuAction.Accept or MenuAction.Back or MenuAction.Start)
            _credits!.Roll?.Skip();
    }
}
