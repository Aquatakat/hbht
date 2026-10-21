using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;

namespace Aidyn.Game;

/// <summary>
/// Cinematics in the runtime (<see cref="CinematicLayer"/>): trek's music and sounds stop and the cinematic plays over
/// everything with the game held still; when it's over the session sets its done flag and brings trek back where the
/// player stood (<see cref="Core.GameSession.CinematicEnded"/>). See docs/systems/cinematics.md.
/// </summary>
public partial class Game
{
    private CinematicLayer? _cinematic;

    /// <summary>
    /// Plays the cinematic the session asked for (<c>Cinematic.Load</c>, <c>Cinematic.Begin</c>): every sound stops, and
    /// trek's music with them, so it starts again from its beginning after.
    /// </summary>
    private void PlayCinematic(CinematicRecord cinematic)
    {
        _audio!.Music = null;
        _audio.StopAll();
        _cinematic!.Play(cinematic, _session!.Game.Party.Members.FirstOrDefault()?.Name ?? "Alaron");
    }

    /// <summary>A tick of the cinematic, and its end (<c>Cinematic.EndCase</c>, <c>0x80053c20</c>), which the session carries out.</summary>
    private void StepCinematic()
    {
        if (_cinematic!.Step() is null)
            return;
        _session!.CinematicEnded();
        ResetInterpolation();
    }

    /// <summary>B or Start skips the rest; the cinematic takes every press.</summary>
    private void CinematicInput(MenuAction action)
    {
        if (action is MenuAction.Back or MenuAction.Start)
            _cinematic!.Skip();
    }
}
