namespace Aidyn.Rom.Engine;

/// <summary>A fade's colour. <see cref="None"/> fades from the current colour back to the picture.</summary>
public enum CinematicFadeColor : byte
{
    None,
    Black,
    White,
    Red,
}

/// <summary>One shot of a cinematic.</summary>
/// <param name="Animation">The Borg6 that animates the shot. It names its own Borg5 scene.</param>
/// <param name="Audio">The Borg12 that plays through the shot.</param>
/// <param name="Captions">Which caption list (<see cref="Db.GameDb.Captions"/>) shows over the shot, or null.</param>
public sealed record CinematicScene(int Animation, int Audio, int? Captions, float Near, float Far);

/// <summary>A fade to or from a colour during one shot. Times are halved, like caption times.</summary>
/// <param name="Scene">The shot, counting from 0.</param>
/// <param name="Start">When the fade starts, from the start of the shot.</param>
/// <param name="Hold">Extra time the fade's colour stays.</param>
public sealed record CinematicFade(int Scene, CinematicFadeColor Color, ushort Start, ushort Length, short Hold);

/// <param name="DoneFlag">Set when the cinematic ends or is skipped, including when the Theatre menu replays it.</param>
public sealed record Cinematic(IReadOnlyList<CinematicScene> Scenes, IReadOnlyList<CinematicFade> Fades, ushort DoneFlag);

/// <summary>
/// The four cinematics (<c>Cinematics.cpp</c>, <c>CinematicFade.cpp</c>), read from the code
/// segment. In the game's order they're the opening, the Shadow, the Roog and the ending.
/// </summary>
public static class Cinematics
{
    public static IReadOnlyList<Cinematic> Read(RomImage rom)
    {
        var pointers = CodeSegment.Table(rom, CodeTable.Cinematics);
        var fadePointers = CodeSegment.Table(rom, CodeTable.CinematicFades);
        var cinematics = new List<Cinematic>();
        for (var i = 0; i < pointers.Length / 16; i++)
        {
            var animations = CodeSegment.At(rom, CodeSegment.U32(pointers, i * 16));
            var flags = CodeSegment.At(rom, CodeSegment.U32(pointers, i * 16 + 4));
            var audio = CodeSegment.At(rom, CodeSegment.U32(pointers, i * 16 + 8));
            var planes = CodeSegment.At(rom, CodeSegment.U32(pointers, i * 16 + 12));

            // The animation list ends with 0. Each shot has an entry in the flag list, which
            // holds its captions, and the entry after the last shot is the done flag.
            var scenes = new List<CinematicScene>();
            for (var s = 0; CodeSegment.U32(animations, s * 4) is var animation and not 0; s++)
            {
                var captions = CodeSegment.U16(flags, s * 2);
                scenes.Add(new CinematicScene((int)animation, (int)CodeSegment.U32(audio, s * 4),
                    captions == 0xFFFF ? null : captions,
                    CodeSegment.F32(planes, s * 8), CodeSegment.F32(planes, s * 8 + 4)));
            }

            // Fades end with an entry for shot 0, which the game never reaches.
            var fadeData = CodeSegment.At(rom, CodeSegment.U32(fadePointers, i * 4));
            var fades = new List<CinematicFade>();
            for (var at = 0; fadeData[at] != 0; at += 8)
            {
                fades.Add(new CinematicFade(fadeData[at] - 1, (CinematicFadeColor)fadeData[at + 1],
                    CodeSegment.U16(fadeData, at + 2), CodeSegment.U16(fadeData, at + 4), CodeSegment.S16(fadeData, at + 6)));
            }

            cinematics.Add(new Cinematic(scenes, fades, CodeSegment.U16(flags, scenes.Count * 2)));
        }
        return cinematics;
    }
}
