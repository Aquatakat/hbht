using Aidyn.Core.Audio;

namespace Aidyn.Core.Ui;

/// <summary>
/// What the pause menu asks of whatever presents it (ADR 0019): its one-off cues, sounds and the scroll's animation steps,
/// through <see cref="Cue"/>, and writing a save. The runtime plays and draws them, and tests record them.
/// </summary>
public interface IPauseMenuHost
{
    /// <summary>A one-off: a sound to play, or a step of the scroll's actor.</summary>
    void Cue(PauseMenuCue cue);

    /// <summary>
    /// The System tab's Save: writes the game to <paramref name="path"/>, with whatever the host keeps beside it, such as the
    /// screenshot of the world as the menu opened. Returns why it couldn't, or null.
    /// </summary>
    string? Save(string path);
}

/// <summary>A one-off the pause menu asks its host for.</summary>
public abstract record PauseMenuCue;

/// <summary>A menu sound (<c>Audio.PlaySound</c> at gain 1): the cursor, A, B, a tab, the member, the coins or a level gained.</summary>
public sealed record MenuSoundCue(OneShot Sound) : PauseMenuCue;

/// <summary>
/// A step of the scroll's actor (<see cref="PauseScrollView"/> in the runtime): toward the cue's request, or into its state
/// partway through, as the original's actors reset their request after each step.
/// </summary>
public sealed record ScrollPlayCue(ScrollCue Cue) : PauseMenuCue;
