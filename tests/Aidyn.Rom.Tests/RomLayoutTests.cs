using System.Buffers.Binary;
using Aidyn.Rom.Compression;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Text;

namespace Aidyn.Rom.Tests;

public class RomLayoutTests
{
    /// <summary>Every table is placed, and they tile the DB block in order without overlapping.</summary>
    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TablesTileTheDbBlock(RomVersionId id)
    {
        var layout = RomVersions.Get(id).Layout;
        Assert.Equal(Enum.GetValues<CodeTable>().Length, layout.CodeTables.Count);
        Assert.Equal(Enum.GetValues<DbTable>().Length, layout.DbTables.Count);
        Assert.Equal(Enum.GetValues<RomStringTable>().Length, layout.StringTables.Count);

        var ranges = layout.DbTables.Values.Concat(layout.StringTables.Values).OrderBy(r => r.Offset).ToList();
        Assert.Equal(layout.DbBlock, ranges[0].Offset);
        for (var i = 1; i < ranges.Count; i++)
            Assert.True(ranges[i - 1].End <= ranges[i].Offset, $"0x{ranges[i - 1].Offset:X} overlaps 0x{ranges[i].Offset:X}");
    }

    /// <summary>
    /// Every table is where the version's ROM has it: the same bytes as NA 1.0's, whose readers' tests pin its tables'
    /// contents, or where a version's own build changed them, the same strings, the same instructions or pointers to the
    /// same data. The string tables' streams also end in their range's padding, which pins their lengths.
    /// </summary>
    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void EveryTableIsWhereTheRomHasIt(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var na10 = TestRoms.Get(RomVersionId.Na10);

        var list = rom.Slice(rom.Layout.BorgList, 8);
        Assert.Equal(rom.Layout.BorgList - rom.Layout.BorgArchive, BinaryPrimitives.ReadInt32BigEndian(list[4..]));
        Assert.True(rom.Slice(rom.Layout.BorgArchive, 0x1000).SequenceEqual(na10.Slice(na10.Layout.BorgArchive, 0x1000)));
        Assert.True(rom.Slice(rom.Layout.BorgList, 0x1000).SequenceEqual(na10.Slice(na10.Layout.BorgList, 0x1000)));
        Assert.True(rom.Slice(rom.Layout.MapChunkLists, 0x200).SequenceEqual(na10.Slice(na10.Layout.MapChunkLists, 0x200)));

        foreach (var table in Enum.GetValues<DbTable>())
            Assert.True(rom.Table(table).SequenceEqual(na10.Table(table)), $"{table} isn't NA 1.0's.");

        foreach (var table in Enum.GetValues<RomStringTable>())
        {
            var block = rom.Table(table);
            var payload = new byte[BinaryPrimitives.ReadUInt16BigEndian(block)];
            var read = Lzo1x.Decompress(block[2..], payload).BytesRead + 2;
            Assert.InRange(block.Length - read, 0, 15);
            if (!Changed(id, table))
                Assert.Equal(RomStringBlock.Read(na10, table), RomStringBlock.Read(rom, table));
        }

        foreach (var table in Enum.GetValues<CodeTable>())
        {
            var bytes = rom.Table(table);
            var original = na10.Table(table);
            if (SpellVisualCode.Contains(table))
                Assert.Equal(Shapes(original), Shapes(bytes));
            else if (table == CodeTable.SpellVisualJumpTables)
                Assert.Equal(CasesAt(na10, original), CasesAt(rom, bytes));
            else if (PointerTables.Contains(table))
                Assert.Equal(Pointees(na10, original), Pointees(rom, bytes));
            else
                Assert.True(bytes.SequenceEqual(original), $"{table} isn't NA 1.0's.");
        }
    }

    /// <summary>The string tables a version changed: 1.1's new Controller Pak messages, and the debug ROM's credits.</summary>
    private static bool Changed(RomVersionId id, RomStringTable table) => (id, table) switch
    {
        (RomVersionId.Na11, RomStringTable.Common or RomStringTable.Controller) => true,
        (RomVersionId.Debug, RomStringTable.Credits) => true,
        _ => false,
    };

    /// <summary>The spell visuals' code, whose calls and addresses each build has its own of.</summary>
    private static readonly CodeTable[] SpellVisualCode =
    [
        CodeTable.SpellVisualCasterActor, CodeTable.SpellVisualTargetHead, CodeTable.SpellVisualTargetActor, CodeTable.SpellVisualAura,
        CodeTable.SpellVisualPotionActor, CodeTable.SpellVisualPotionAura, CodeTable.SpellVisualRefusedResisted,
    ];

    /// <summary>The tables of pointers into the code segment's data, which moves between builds.</summary>
    private static readonly CodeTable[] PointerTables = [CodeTable.Cinematics, CodeTable.CinematicFades, CodeTable.MinimapSections];

    private static IEnumerable<uint> Words(ReadOnlySpan<byte> bytes)
    {
        var words = new uint[bytes.Length / 4];
        for (var i = 0; i < words.Length; i++)
            words[i] = BinaryPrimitives.ReadUInt32BigEndian(bytes[(i * 4)..]);
        return words;
    }

    /// <summary>Each instruction's opcode and registers, without the immediates and targets that addresses change.</summary>
    private static List<uint> Shapes(ReadOnlySpan<byte> code) =>
        [.. Words(code).Select(ins => ins >> 26 is 0 ? ins : ins & 0xFFFF0000u)];

    /// <summary>Each jump table entry as the spell visual function it lands in and how far into it, and the padding between tables as nulls.</summary>
    private static List<(CodeTable, int)?> CasesAt(RomImage rom, ReadOnlySpan<byte> jumpTables) =>
    [
        .. Words(jumpTables).Select((CodeTable, int)? (address) =>
        {
            if (address == 0)
                return null;
            var offset = CodeSegment.RomOffset(address);
            var function = SpellVisualCode.Single(f => rom.Layout.CodeTables[f] is var r && r.Offset <= offset && offset < r.End);
            return (function, offset - rom.Layout.CodeTables[function].Offset);
        }),
    ];

    /// <summary>Each word, or for a pointer into the code segment, the 16 bytes it points to.</summary>
    private static List<string> Pointees(RomImage rom, ReadOnlySpan<byte> pointers) =>
    [
        .. Words(pointers).Select(word => word >= CodeSegment.RamBase && word < CodeSegment.RamBase + 0x100000
            ? Convert.ToHexString(CodeSegment.At(rom, word)[..16])
            : $"0x{word:X8}"),
    ];
}
