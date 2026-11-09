using System.Diagnostics;
using HBHT.Core.Combat;
using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>
/// How the host plays: the session's settings, and what it hurries since nobody's watching. The original's settings by default, with
/// cinematics skipped as B skips them, the credits the menu rolls ended as A ends them, and the ending's, which can't be, rolled
/// <see cref="CreditsPace"/> times as fast.
/// </summary>
/// <param name="Settings">What every tick plays by (<see cref="GameSession.Tick"/>).</param>
/// <param name="SkipCinematics">Whether a cinematic is skipped with B as it starts; its done flag and what follows it are the same.</param>
/// <param name="SkipCredits">Whether credits that can be skipped are, with A.</param>
/// <param name="CreditsPace">How many ticks of the ending's credits, which can't be skipped, play in each of the session's ticks.</param>
/// <param name="SavesFolder">Where the pause menu's save and load screens list saves: the data folder's if none.</param>
public sealed record PlaythroughOptions(
    SessionSettings? Settings = null,
    bool SkipCinematics = true,
    bool SkipCredits = true,
    int CreditsPace = 8,
    string? SavesFolder = null)
{
    public static readonly PlaythroughOptions Default = new();

    public SessionSettings Playing => Settings ?? SessionSettings.Original;
}

/// <summary>A transition the session handed the host, and the tick it came on.</summary>
public sealed record TransitionAt(long Tick, Transition Transition)
{
    public override string ToString() => $"{Tick}: {Transition.GetType().Name}";
}

/// <summary>What a note is.</summary>
public enum NoteKind
{
    /// <summary><see cref="Trek.ITrekHost.Log"/>, or a host's own note, such as a camp's outcome or the hours passed.</summary>
    Log,

    /// <summary><see cref="Trek.ITrekHost.Warn"/>: a problem, such as where a script stopped that the original would crash.</summary>
    Warn,

    /// <summary>Something said to the player (<see cref="Transition.Say"/>), such as that a keeper keeps no shop.</summary>
    Say,
}

/// <summary>A line of text the game noted, and the tick it came on.</summary>
public sealed record Note(long Tick, NoteKind Kind, string Text)
{
    public override string ToString() => $"{Tick} {Kind}: {Text}";
}

/// <summary>A page the host pressed on: the script, the page's node, and the choice taken if it had choices to take (else null).</summary>
/// <param name="Choice">The choice taken, from 0, of <paramref name="Of"/>.</param>
/// <param name="ChoiceNode">The node of the choice taken.</param>
public sealed record PageTurned(long Tick, PageSource Source, RecordId Script, int Node, int? Choice = null, int Of = 0, int? ChoiceNode = null,
    string? ChoiceText = null)
{
    public override string ToString() => Choice is { } c ? $"{Tick} {Script} node {Node}: choice {c} of {Of} (node {ChoiceNode})" : $"{Tick} {Script} node {Node}";
}

/// <summary>
/// A battle the host played: when it began and ended, what started it, how it ended, whether the battle policy won it outright
/// (<see cref="PlaythroughHost.WinBattleOutright"/>), and the party's members who died in it.
/// </summary>
public sealed class BattleFought(long begun, BattleSetup setup)
{
    public long Begun { get; } = begun;

    public long? Ended { get; internal set; }

    public BattleSetup Setup { get; } = setup;

    public BattleOutcome Outcome { get; internal set; }

    public bool WonOutright { get; internal set; }

    /// <summary>Whether this is the battle played again from its start after the party lost it (<see cref="BattlePilot"/>).</summary>
    public bool LostFirst { get; internal set; }

    /// <summary>Whether it was won outright because neither side could end it (<see cref="BattlePilot.QuietTicks"/>).</summary>
    public bool Stalemate { get; internal set; }

    public List<RecordId> Deaths { get; } = [];

    public override string ToString() =>
        $"{Begun}–{Ended}: {Setup.Party?.ToString() ?? Setup.Source.ToString()} {Outcome}{(WonOutright ? " (won outright)" : "")}{(LostFirst ? " (lost first)" : "")}{(Stalemate ? " (stalemate)" : "")}"
        + (Deaths.Count > 0 ? $", {Deaths.Count} died" : "");
}

/// <summary>Why the host stopped playing.</summary>
public enum HostEnd
{
    /// <summary>It hasn't.</summary>
    None,

    /// <summary>The game is over (<see cref="Transition.GameOver"/>): the death scene or the ending's credits have ended.</summary>
    GameOver,

    /// <summary>Quit Game (<see cref="Transition.Quit"/>).</summary>
    Quit,

    /// <summary>Quit Game's Return to Title (<see cref="Transition.ToTitle"/>).</summary>
    ToTitle,
}

/// <summary>Ticks stepped, and the wall time they took.</summary>
public sealed class HostClock
{
    private readonly Stopwatch _wall = new();

    /// <summary>The session's ticks the host has stepped, its 60 Hz ones.</summary>
    public long Ticks { get; private set; }

    /// <summary>The wall time spent stepping them, the host's own carrying out with them.</summary>
    public TimeSpan Wall => _wall.Elapsed;

    /// <summary>Ticks stepped a second of wall time; 0 before any.</summary>
    public double TicksPerSecond => Wall.TotalSeconds > 0 ? Ticks / Wall.TotalSeconds : 0;

    /// <summary>How many times faster than the game plays: 60 ticks a second is 1.</summary>
    public double Speed => TicksPerSecond / 60;

    internal void Start() => _wall.Start();

    internal void Stop()
    {
        _wall.Stop();
        Ticks++;
    }

    public override string ToString() => $"{Ticks} ticks in {Wall.TotalSeconds:0.0} s, {TicksPerSecond:0} a second ({Speed:0.0}×)";
}
