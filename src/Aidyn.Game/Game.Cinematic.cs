using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;

namespace Aidyn.Game;

/// <summary>
/// Cinematics in the runtime (<see cref="CinematicLayer"/>): trek's music and sounds stop and the cinematic plays over
/// everything with the game held still. The session steps it and ends it (<see cref="Core.GameSession.Cinematic"/>); this
/// draws each tick's shot, caption and fade, and plays each shot's music. See docs/systems/cinematics.md.
/// </summary>
public partial class Game
{
    private CinematicLayer? _cinematic;

    /// <summary>
    /// Shows the cinematic the session has started (<c>Cinematic.Load</c>, <c>Cinematic.Begin</c>): every sound stops, and
    /// trek's music with them, so it starts again from its beginning after.
    /// </summary>
    private void PlayCinematic(CinematicRecord cinematic)
    {
        _audio!.Music = null;
        _audio.StopAll();
        _cinematic!.Play(cinematic);
    }

    /// <summary>A tick of the cinematic, which the session stepped: its shot, caption and fade as they stand, unless it's held for a screenshot.</summary>
    private void DrawCinematic()
    {
        if (_session!.Cinematic is { } showing && !_session.CinematicHeld)
            _cinematic!.Draw(showing.Player);
    }

    /// <summary>The cinematic has ended (<c>Cinematic.EndCase</c>, <c>0x80053c20</c>): its picture and music go, before what follows it.</summary>
    private void EndCinematic()
    {
        _cinematic!.End();
        ResetInterpolation();
    }
}
