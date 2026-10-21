namespace Aidyn.Core.Ui;

/// <summary>
/// What a press came to (<see cref="GameSession.Press"/>), for whoever reads the controls to act on: the runtime shows what
/// changed, and a test asserts it.
/// </summary>
public enum PressOutcome
{
    /// <summary>Nothing took it: the mode that runs has no use for the button, or its input is dead, as through a fade.</summary>
    Ignored,

    /// <summary>The mode that runs took it: a cursor moved, a page turned, a popup closed, the portraits rose.</summary>
    Taken,

    /// <summary>Start in trek: the pause menu opened.</summary>
    MenuOpened,

    /// <summary>A in trek: the map object the player reaches was used.</summary>
    Used,

    /// <summary>A cinematic or the credits skip to their end, which the view playing them does (<c>Cinematic.EndCase</c>, <c>Credits.Destroy</c>).</summary>
    Skip,
}
