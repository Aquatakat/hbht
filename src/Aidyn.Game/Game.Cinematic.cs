using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Cinematics in the runtime (<see cref="CinematicLayer"/>): trek's music and sounds stop and the cinematic plays over
/// everything with the game held still; when it's over its done flag is set and trek comes back where the player stood.
/// See docs/systems/cinematics.md.
/// </summary>
public partial class Game
{
    private CinematicLayer? _cinematic;

    /// <summary>Whether the cinematic playing came from the Theatre, so the pause menu reopens on its page after.</summary>
    private bool _fromTheatre;

    /// <summary>Trek has come back from the Theatre's cinematic, and the menu reopens once it has faded in.</summary>
    private bool _reopenTheatre;

    private bool InCinematic => _cinematic is { Playing: true };

    /// <summary>
    /// Plays a cinematic (<c>Cinematic.Load</c>, <c>Cinematic.Begin</c>): every sound stops, and trek's music with them,
    /// so it starts again from its beginning after.
    /// </summary>
    private void PlayCinematic(CinematicRecord cinematic, bool fromTheatre)
    {
        if (_cinematic is null || InCinematic)
            return;
        _fromTheatre = fromTheatre;
        _audio!.Music = null;
        _audio.StopAll();
        _cinematic.Play(cinematic, _game!.Party.Members.FirstOrDefault()?.Name ?? "Alaron");
    }

    /// <summary>
    /// Plays a cinematic from the command line (<c>--cinematic 0</c>, or <c>--cinematic 0,5,300</c> to start at shot 5's
    /// time 300, and <c>--cinematic 0,5,300,hold</c> to stand still there), as from the Theatre but without the menu after.
    /// </summary>
    private void CinematicFromCommandLine(string arg)
    {
        var parts = arg.Split(',');
        if (_plugins!.TryGet<CinematicRecord>(RecordId.Decimal(RecordTypes.Cinematic, int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture))) is not { } cinematic)
        {
            Say($"There's no cinematic {parts[0]}.");
            return;
        }
        PlayCinematic(cinematic, fromTheatre: false);
        if (parts.Length > 1)
            _cinematic!.SkipTo(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                parts.Length > 2 ? int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 0);
        _cinematic!.Held = parts.Length > 3 && parts[3] == "hold";
    }

    /// <summary>A tick of the cinematic, and its end (<c>Cinematic.EndCase</c>, <c>0x80053c20</c>).</summary>
    private void StepCinematic()
    {
        if (_cinematic!.Step() is not { } ended)
            return;
        // The done flag is set however it ended, from the Theatre too.
        _game!.Flags.Set(ended.DoneFlag, true);
        // Trek comes back where the player stood, its music asked for again and the screen fading in from black.
        _trek!.Resume();
        _reopenTheatre = _fromTheatre;
        ResetInterpolation();
    }

    /// <summary>Once trek has faded in after the Theatre's cinematic, the menu reopens on the Theatre's page (trek's state 8).</summary>
    private void ReopenTheatre()
    {
        if (!_reopenTheatre || _trek!.CutSceneFade.Fading)
            return;
        _reopenTheatre = false;
        _menu!.OpenTheatre();
    }

    /// <summary>B or Start skips the rest; the cinematic takes every press.</summary>
    private void CinematicInput(InputEvent e)
    {
        if (e.IsActionPressed("menu_back") || e.IsActionPressed("pause"))
            _cinematic!.Skip();
    }
}
