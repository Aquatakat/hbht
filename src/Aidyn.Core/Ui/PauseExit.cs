using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>The pause menu's tabs, left to right (<c>PauseMenuSection</c>).</summary>
public enum PauseTab
{
    System,
    Party,
    World,
}

/// <summary>
/// The pause menu's exit codes (<c>PauseMenu.Exit</c>, <c>0x80048e64</c>, its jump table at <c>0x800d1240</c>): the code the
/// trek pause menu leaves as it rolls up, which sets trek's next state (<c>PauseMenu.NextTrekState</c>, <c>0x800df031</c>,
/// copied to <c>TrekLoop.State</c> as the slide ends, <c>0x8004898c</c>).
/// </summary>
public enum PauseExitCode
{
    /// <summary>B or Start, or a window of the Party tab done: trek runs on (state 3; the jump table's default, <c>0x80048f10</c>).</summary>
    Close = 0,

    /// <summary>Camp on the World tab where the terrain allows it (<c>WorldMenu.Camp</c>, stored at <c>0x80041080</c>): the party camps (trek state 0x11).</summary>
    Camp = 1,

    /// <summary>A seen cinematic on the Theatre's page (<c>Theatre.OnA</c>, <c>0x80044a8c</c>): it plays (trek states 0x16, 0x17).</summary>
    Theatre = 2,

    /// <summary>Help (<c>pauseMenuSelectCallBack</c>, <c>0x80040500</c>): the help menu's script starts (trek state 0x12).</summary>
    Help = 3,

    /// <summary>A save has loaded (<c>PauseMenu.OnLoaded</c>, <c>0x80048e18</c>): trek is freed and built again from it (trek state 4).</summary>
    Load = 4,

    /// <summary>Credits (row 9999, <c>0x80040524</c>): the credits play over trek held still (trek state 0x19).</summary>
    Credits = 5,
}

/// <summary>
/// How the pause menu ended, which whoever runs the game carries on from (ADR 0019): the menu's step returns it once the
/// scroll has rolled up, as the original's menu leaves its exit code for trek (<see cref="PauseExitCode"/>).
/// </summary>
/// <param name="Cinematic">The cinematic the Theatre chose, for <see cref="PauseExitCode.Theatre"/>.</param>
public sealed record PauseExit(PauseExitCode Code, CinematicRecord? Cinematic = null)
{
    public static readonly PauseExit Close = new(PauseExitCode.Close);

    public static readonly PauseExit Camp = new(PauseExitCode.Camp);

    public static readonly PauseExit Help = new(PauseExitCode.Help);

    public static readonly PauseExit Load = new(PauseExitCode.Load);

    public static readonly PauseExit Credits = new(PauseExitCode.Credits);

    /// <summary>The Theatre chose <paramref name="cinematic"/>.</summary>
    public static PauseExit Theatre(CinematicRecord cinematic) => new(PauseExitCode.Theatre, cinematic);
}
