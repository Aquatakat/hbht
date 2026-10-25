namespace HBHT.Plugins.Records;

/// <summary>
/// One of the four pre-animated cinematics (<c>aidyn:cinematic/0</c> to <c>/3</c>: the opening, the Shadow's, Roog's and
/// the ending), from the cinematic table in the code segment (<c>Cinematics.cpp</c>), with its captions and its title
/// from the DB. A cinematic plays its shots in order, each a scene's animation through the scene's own camera.
/// </summary>
/// <param name="Title">What the Theatre menu lists it as once <paramref name="DoneFlag"/> is set, or null if it isn't listed.</param>
/// <param name="DoneFlag">Set when it ends or is skipped, including when the Theatre menu plays it again.</param>
/// <param name="Aspect">
/// The shape its shots are framed for, width over height: the original's are all 4:3, the console's screen, and their
/// sets end beyond it, with actors waiting there for the camera. The runtime pillarboxes or letterboxes it to fit.
/// </param>
public sealed record CinematicRecord(RecordId Id, string? Title, IReadOnlyList<CinematicShot> Shots, RecordId DoneFlag,
    float Aspect = CinematicRecord.ConsoleAspect)
{
    /// <summary>The console's screen, 4:3, which the original's cinematics are framed for.</summary>
    public const float ConsoleAspect = 4f / 3;
}

/// <summary>A cinematic's shot. Its times are in ticks from the shot's start.</summary>
/// <param name="Scene">The scene the animation drives, whose camera the shot is seen through.</param>
/// <param name="Animation">The Borg6 that animates the shot.</param>
/// <param name="Length">How long the shot lasts.</param>
/// <param name="Music">The sequence that plays through it.</param>
/// <param name="Near">
/// The camera's near plane, which the table sets for the shot in place of its scene's Borg3 camera's (0.1, 0.01 or
/// 0.25).
/// </param>
/// <param name="Far">The camera's far plane, in place of the Borg3's (1000 or 25).</param>
/// <param name="Captions">What's said, shown in the caption box at the foot of the screen.</param>
/// <param name="Fades">The screen fading to a colour and back.</param>
public sealed record CinematicShot(
    RecordId Scene,
    RecordId Animation,
    int Length,
    RecordId Music,
    float Near,
    float Far,
    IReadOnlyList<CinematicCaption> Captions,
    IReadOnlyList<CinematicFade> Fades);

/// <summary>A caption: a line shown from <paramref name="Start"/> until <paramref name="End"/>.</summary>
public sealed record CinematicCaption(int Start, int End, string Text);

/// <summary>The colour a cinematic's fade goes to. <see cref="Clear"/> fades from the colour on screen back to the picture.</summary>
public enum CinematicFadeColor
{
    Clear,
    Black,
    White,
    Red,
}

/// <summary>A fade during a shot, from <paramref name="Start"/> over <paramref name="Length"/>, its colour held <paramref name="Hold"/> more.</summary>
public sealed record CinematicFade(CinematicFadeColor Color, int Start, int Length, int Hold);
