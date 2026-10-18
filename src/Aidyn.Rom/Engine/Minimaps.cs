namespace Aidyn.Rom.Engine;

/// <summary>A piece of an interior's minimap, placed at <see cref="X"/>, <see cref="Y"/>.</summary>
/// <param name="Row">The map chunk's row, counting from 1 for A.</param>
/// <param name="Group">
/// Pieces that show together. The game reveals a group as the party explores it, and saves
/// which pieces have been seen.
/// </param>
/// <param name="Seen">
/// Whether the piece is seen from the start (<c>checked</c>). The game sets it when the piece is first shown, and saves it.
/// </param>
/// <param name="Image">The piece's Borg8.</param>
public sealed record MinimapSection(int Row, int Column, int Group, bool Seen, int Image, int X, int Y);

/// <summary>A minimap image that shows while its flag is set.</summary>
public sealed record MinimapFlagImage(ushort Flag, int Image);

/// <summary>
/// The interior minimaps (<c>minimap.cpp</c>), read from the code segment.
/// </summary>
/// <param name="Sections">
/// Pieces for each map set that has a minimap, in the order the game saves them: Gwernia castle (map set 4),
/// Ugarit 2 (14), Erromon (7), the Ehud underground (12), the Jundar interiors' mage school (11), the Roog
/// entrance in the misc interiors (6), the Barrows (9) and the Roog cave (10). Code chooses the list when it loads a
/// map set (<c>BorgMaps::LoadMap</c>); the decomp's names for the last two are swapped.
/// </param>
/// <param name="Ehud">Images for the Ehud underground's O05 chunk, the first set flag winning.</param>
/// <param name="RoogEntrance">Images for the Roog entrance, the first set flag winning.</param>
/// <param name="Barrows">
/// Flags that change the Barrows' images. Code decides which piece each flag affects, and how.
/// </param>
public sealed record Minimaps(
    IReadOnlyList<IReadOnlyList<MinimapSection>> Sections,
    IReadOnlyList<MinimapFlagImage> Ehud,
    IReadOnlyList<MinimapFlagImage> RoogEntrance,
    IReadOnlyList<MinimapFlagImage> Barrows)
{
    private const int SectionSize = 14;

    public static Minimaps Read(RomImage rom)
    {
        var pointers = rom.Table(CodeTable.MinimapSections);
        var sections = new List<IReadOnlyList<MinimapSection>>();
        for (var i = 0; Be.U32(pointers, i * 4) is var pointer and not 0; i++)
        {
            // Each list ends with a group of 0.
            var data = CodeSegment.At(rom, pointer);
            var list = new List<MinimapSection>();
            for (var at = 0; Be.U16(data, at + 4) != 0; at += SectionSize)
            {
                list.Add(new MinimapSection(Be.U16(data, at), Be.U16(data, at + 2),
                    Be.U16(data, at + 4), Be.U16(data, at + 6) != 0, Be.U16(data, at + 8),
                    Be.U16(data, at + 10), Be.U16(data, at + 12)));
            }
            sections.Add(list);
        }

        var images = rom.Table(CodeTable.MinimapFlagImages);
        var lists = new List<List<MinimapFlagImage>> { new() };
        for (var at = 0; lists.Count <= 3; at += 8)
        {
            var flag = Be.U32(images, at);
            var image = Be.U32(images, at + 4);
            if (image == 0)
                lists.Add([]);
            else
                lists[^1].Add(new MinimapFlagImage((ushort)flag, (int)image));
        }

        return new Minimaps(sections, lists[0], lists[1], lists[2]);
    }
}
