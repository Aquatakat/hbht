using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Engine;

namespace HBHT.Rom.Import;

/// <summary>
/// The minimaps (<c>minimap.cpp</c>): their pieces and the flag lists from the code's tables, and which map set each is
/// for and how a chunk chooses its piece, which are code, carried as facts.
/// </summary>
internal static class MinimapImporter
{
    /// <summary>Each list's map set, by <c>BorgMaps::LoadMap</c>'s calls to <c>MiniMap::LoadData</c> in 1.0, in the order of the code's table, which a save numbers the pieces in.</summary>
    internal static readonly IReadOnlyList<int> MapSets = [4, 14, 7, 12, 11, 6, 9, 10];

    private const int Gwernia = 0, Ehud = 3, MageSchool = 4, RoogEntrance = 5, Barrows = 6;

    public static List<MinimapRecord> Build(Minimaps minimaps) =>
    [
        .. minimaps.Sections.Select((list, i) => new MinimapRecord(
            RecordId.Decimal(RecordTypes.Minimap, MapSets[i]),
            RecordId.Decimal(RecordTypes.MapSet, MapSets[i]),
            [.. list.Select(s => new MinimapSection(s.Row, s.Column, s.Group, RecordId.Hex(RecordTypes.Image, s.Image), s.X, s.Y, s.Seen))],
            Rules(i, list, minimaps))),
    ];

    /// <summary>
    /// The chunks whose piece isn't simply their own (<c>ShowingSpecialSection</c>, <c>0x8004b96c</c> in 1.0, and the
    /// functions it calls, <c>0x8004badc</c>–<c>0x8004c34c</c>). The original chooses again only when the player crosses
    /// one of these bounds or a flag changes (<c>SpecialSections</c>), which comes to the same.
    /// </summary>
    private static List<MinimapRule> Rules(int list, IReadOnlyList<RomMinimapSection> sections, Minimaps minimaps)
    {
        var rules = new List<MinimapRule>();
        void Rule(string chunk, int? image, int? flag = null, params MinimapBound[] bounds)
        {
            var (row, column) = (chunk[0] - 'A' + 1, int.Parse(chunk[1..]));
            int? section = image is { } i ? Index(sections, i) : null;
            rules.Add(new MinimapRule(row, column, section, flag is { } f ? DbRecordIds.Flag(f) : null,
                bounds.Length == 0 ? null : bounds));
        }
        static MinimapBound Below(MinimapAxis axis, float value) => new(axis, Below: value);

        switch (list)
        {
            case Gwernia:
                // The courtyard by floor, and its ground by which side: z above 26, not from 26.
                Rule("I07", 0x2A, null, Below(MinimapAxis.Y, 4), new MinimapBound(MinimapAxis.Z, Above: 26));
                Rule("I07", 0x2B, null, Below(MinimapAxis.Y, 4));
                Rule("I07", 0x37, null, Below(MinimapAxis.Y, 8));
                Rule("I07", 0x38);
                // The walls by quarter.
                Rule("A01", 0x3827, null, Below(MinimapAxis.X, 35), Below(MinimapAxis.Z, 25));
                Rule("A01", 0x3829, null, Below(MinimapAxis.X, 35));
                Rule("A01", 0x3828, null, Below(MinimapAxis.Z, 25));
                Rule("A01", 0x382A);
                // The tower by floor.
                Rule("O09", 0x98, null, Below(MinimapAxis.Y, 4));
                Rule("O09", 0x9D);
                break;
            case MageSchool:
                Rule("A01", 0xA2, null, Below(MinimapAxis.Y, 4));
                Rule("A01", 0xA7, null, Below(MinimapAxis.Y, 8));
                Rule("A01", 0xAA);
                break;
            case Ehud:
                Rule("C11", 0x80, null, Below(MinimapAxis.X, 48), Below(MinimapAxis.Z, 48));
                Rule("C11", 0x82, null, Below(MinimapAxis.X, 48));
                Rule("C11", 0x81, null, Below(MinimapAxis.Z, 48));
                Rule("C11", 0x83);
                Rule("I09", 0x5A, null, Below(MinimapAxis.X, 16));
                Rule("I09", 0x5B);
                // The maze room the player's in, by the first of its flags that's set, or none.
                foreach (var room in minimaps.Ehud)
                    Rule("O05", room.Image, room.Flag);
                Rule("O05", null);
                break;
            case RoogEntrance:
                foreach (var room in minimaps.RoogEntrance)
                    Rule("K03", room.Image, room.Flag);
                Rule("K03", null);
                break;
            case Barrows:
                // MiniMap::BarrowsFlags (0x8004bfac): the rooms' states, some showing another chunk's piece.
                Rule("A02", 0xC1, 0x10C5);
                Rule("A03", 0xC4, 0x10C5);
                Rule("B02", 0xC2, 0x10C4);
                Rule("B02", 0xC3, 0x10C6);
                Rule("B03", 0xC5, 0x10C6);
                Rule("E01", 0xC9, 0x10C3);
                Rule("E01", 0xC6, null, Below(MinimapAxis.X, 1.3f));
                Rule("E01", 0xC7);
                Rule("E02", 0xC9, 0x10C3);
                Rule("E02", 0xC8);
                Rule("G01", 0xCC, 0x10BE);
                Rule("G01", 0xCD, 0x10BF);
                Rule("G02", 0xCD, 0x10BF);
                Rule("G03", 0xD0, 0x10C0);
                Rule("G03", 0xD4, 0x10C1);
                Rule("H03", 0xD3, 0x10C2);
                break;
        }
        return rules;
    }

    private static int Index(IReadOnlyList<RomMinimapSection> sections, int image)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            if (sections[i].Image == image)
                return i;
        }
        throw new InvalidDataException($"No minimap piece has image 0x{image:X}.");
    }
}
