namespace Aidyn.Rom.Engine;

/// <summary>A piece of an interior's minimap, placed at <see cref="X"/>, <see cref="Y"/>.</summary>
/// <param name="Row">The map chunk's row, counting from 1 for A.</param>
/// <param name="Group">
/// Pieces that show together. The game reveals a group as the party explores it, and saves
/// which pieces have been seen.
/// </param>
/// <param name="Image">The piece's Borg8.</param>
public sealed record MinimapSection(int Row, int Column, int Group, int Image, int X, int Y);

/// <summary>A minimap image that shows while its flag is set.</summary>
public sealed record MinimapFlagImage(ushort Flag, int Image);

/// <summary>
/// The interior minimaps (<c>minimap.cpp</c>), read from the code segment.
/// </summary>
/// <param name="Sections">
/// Pieces for each map that has a minimap: Gwernia, Ugarit, Erromon, the Ehud underground, the
/// mage school, the Roog entrance, the Roog cave and the Barrows. Code chooses the list when it
/// loads a map set.
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
        var pointers = CodeSegment.Table(rom, CodeTable.MinimapSections);
        var sections = new List<IReadOnlyList<MinimapSection>>();
        for (var i = 0; CodeSegment.U32(pointers, i * 4) is var pointer and not 0; i++)
        {
            // Each list ends with a group of 0.
            var data = CodeSegment.At(rom, pointer);
            var list = new List<MinimapSection>();
            for (var at = 0; CodeSegment.U16(data, at + 4) != 0; at += SectionSize)
            {
                list.Add(new MinimapSection(CodeSegment.U16(data, at), CodeSegment.U16(data, at + 2),
                    CodeSegment.U16(data, at + 4), CodeSegment.U16(data, at + 8),
                    CodeSegment.U16(data, at + 10), CodeSegment.U16(data, at + 12)));
            }
            sections.Add(list);
        }

        var images = CodeSegment.Table(rom, CodeTable.MinimapFlagImages);
        var lists = new List<List<MinimapFlagImage>> { new() };
        for (var at = 0; lists.Count <= 3; at += 8)
        {
            var flag = CodeSegment.U32(images, at);
            var image = CodeSegment.U32(images, at + 4);
            if (image == 0)
                lists.Add([]);
            else
                lists[^1].Add(new MinimapFlagImage((ushort)flag, (int)image));
        }

        return new Minimaps(sections, lists[0], lists[1], lists[2]);
    }
}
