namespace Aidyn.Plugins.Records;

/// <summary>
/// One of the four pre-animated cinematics (<c>aidyn:cinematic/0</c> to <c>/3</c>: the opening, the Shadow's, Roog's and
/// the ending), from the cinematic table in the code segment (<c>Cinematics.cpp</c>). A cinematic plays its shots in
/// order, each a scene's animation through the scene's own camera.
/// </summary>
/// <param name="DoneFlag">Set when it ends or is skipped, including when the Theatre menu plays it again.</param>
public sealed record CinematicRecord(RecordId Id, IReadOnlyList<CinematicShot> Shots, RecordId DoneFlag);

/// <summary>A cinematic's shot.</summary>
/// <param name="Animation">The Borg6 that animates the shot, which names its scene.</param>
/// <param name="Music">The sequence that plays through it.</param>
/// <param name="Near">
/// The camera's near plane, which the table sets for the shot in place of its scene's Borg3 camera's (0.1, 0.01 or
/// 0.25).
/// </param>
/// <param name="Far">The camera's far plane, in place of the Borg3's (1000 or 25).</param>
public sealed record CinematicShot(RecordId Animation, RecordId Music, float Near, float Far);
