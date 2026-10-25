namespace HBHT.Rom.Engine;

/// <summary>One of the title screen's fly-throughs, as the code's table has it (<c>gFlycamSequences</c>).</summary>
/// <param name="Animation">The Borg6 that moves the camera (<c>FUN_80096098</c>, which loads it).</param>
/// <param name="Row">Stored in <c>Zones.Row</c> (<c>0x800D83FA</c>).</param>
/// <param name="Column">Stored in <c>Zones.Column</c> (<c>0x800D83FC</c>).</param>
/// <param name="OriginRow">
/// Stored at <c>0x800D90DC</c>: the row of the chunk whose corner the animation's coordinates are from. No code names the address
/// directly but the store; the camera's paths run a few units over the ground from that corner and from no other.
/// </param>
/// <param name="OriginColumn">Stored at <c>0x800D90DE</c>: that chunk's column.</param>
public sealed record Flycam(int Animation, int Row, int Column, int OriginRow, int OriginColumn);

/// <summary>The title screen's tables in the code segment.</summary>
public static class TitleTables
{
    /// <summary>The table's entry size: a u32 Borg6, then four u32s whose low halves the code stores.</summary>
    private const int EntrySize = 0x14;

    /// <summary>
    /// The fly-through that needs the Expansion Pak: <c>Title.StartScenery</c> (<c>0x80054D70</c> in 1.0) steps its place in the table by
    /// one, modulo 6, and on reaching 4 without the Pak (<c>Memory.HasExpansionPak</c>, <c>0x800D7EA4</c>) takes 5. A fact checked against
    /// that code.
    /// </summary>
    public const int PakSequence = 4;

    /// <summary>
    /// The six fly-throughs (<c>gFlycamSequences</c>, <c>0x800E09DC</c> in 1.0, six entries of 0x14 bytes). The code indexes the table
    /// by place times 0x14 and reads, as u16s, the low half of the second u32 into <c>0x800D90DC</c>, of the third into <c>0x800D90DE</c>,
    /// of the fourth into <c>Zones.Row</c> and of the fifth into <c>Zones.Column</c>, and loads the first as a Borg6.
    /// </summary>
    public static IReadOnlyList<Flycam> ReadFlycams(RomImage rom)
    {
        var data = rom.Table(CodeTable.FlycamSequences);
        if (data.Length % EntrySize != 0)
            throw new InvalidDataException($"The fly-through table is {data.Length} bytes, not a whole number of entries.");
        var flycams = new List<Flycam>();
        for (var at = 0; at < data.Length; at += EntrySize)
        {
            // Each u32 after the first holds a u16: its top half is 0.
            for (var field = 1; field < 5; field++)
                if (Be.U16(data, at + field * 4) != 0)
                    throw new InvalidDataException($"A fly-through's field {field} is over 16 bits.");
            flycams.Add(new Flycam((int)Be.U32(data, at), Row: Be.U16(data, at + 0xE), Column: Be.U16(data, at + 0x12),
                OriginRow: Be.U16(data, at + 6), OriginColumn: Be.U16(data, at + 0xA)));
        }
        return flycams;
    }
}
