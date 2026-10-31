using System.IO.Compression;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

/// <summary>
/// Combat parity's logs (<c>tools/parity.ps1</c>): RAM as gdb dumps it reads as a checkpoint does, and the log's records
/// read as <c>tools/gdb/parity.py</c> writes them.
/// </summary>
public sealed class ParityLogTests
{
    private const string Checkpoint = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - Combat 09, rat newcomer joins and moves.bs1";

    [Fact]
    public void RamDumpedBigEndianReadsAsTheStateItCameFrom()
    {
        var state = TestRoms.Checkpoint(Checkpoint);
        var rdram = new byte[0x800000];
        for (var i = 0; i < rdram.Length; i += 4)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(rdram.AsSpan(i), state.ReadWord(0x80000000 + (uint)i));

        var folder = Directory.CreateTempSubdirectory("hbht-parity-").FullName;
        try
        {
            var path = Path.Combine(folder, "0001.rdram.gz");
            using (var gz = new GZipStream(File.Create(path), CompressionLevel.Fastest))
                gz.Write(rdram);
            var dumped = AresCheckpoint.FromRdram(path);

            Assert.Equal(state.Flags, dumped.Flags);
            Assert.Equal(state.Party.Select(p => (p.Id, p.Current.Sum())), dumped.Party.Select(p => (p.Id, p.Current.Sum())));
            Assert.Equal(state.Time, dumped.Time);
            Assert.Equal(state.ReadWord(0x800D8088), dumped.ReadWord(0x800D8088));
            Assert.Equal(state.ReadByte(0x800D9454), dumped.ReadByte(0x800D9454));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void TheLogsRecordsReadInOrder()
    {
        var folder = Directory.CreateTempSubdirectory("hbht-parity-").FullName;
        try
        {
            File.WriteAllLines(Path.Combine(folder, "log.jsonl"),
            [
                """{"t": "enter", "name": "BattleSetup.FromMonsterParty", "pc": 2147521716, "ra": 2147614720, "args": [1, 2, 3, 4], "seed": 305419896}""",
                """{"t": "draw", "old": 305419896, "new": 2882400001, "pc": 2147514028, "chain": [2147514028, 2147521800], "state": 0}""",
                """{"t": "leave", "name": "BattleSetup.FromMonsterParty", "pc": 2147614720, "seed": 2882400001}""",
                """{"t": "enter", "name": "TurnOrder.BeginTurn", "pc": 2147920328, "ra": 2147650000, "args": [0, 0, 0, 0], "seed": 7, "state": "0101", "delta": "00000002", "frame": 12, "current": 5, "combatant": "00ff", "dump": "0002.rdram.gz", "dumpSeconds": 1.9}""",
            ]);
            var log = ParityLog.Read(folder);

            Assert.Equal(4, log.Records.Count);
            var draw = Assert.Single(log.Draws);
            Assert.Equal((305419896u, 2882400001u, 0), (draw.Old, draw.New, draw.State));
            Assert.Equal([2147514028u, 2147521800u], draw.Chain);
            Assert.IsType<ParityLeave>(log.Records[2]);
            var turn = log.Entries.Last();
            Assert.Equal(("TurnOrder.BeginTurn", 5, 12u, "0002.rdram.gz"), (turn.Name, turn.Current, turn.Frame, turn.Dump));
            Assert.Equal([0x00, 0xFF], turn.Combatant);
            Assert.Equal([1, 1], turn.State);
            Assert.Null(log.Entries.First().Dump);
            Assert.False(log.Watches("TurnOrder.BeginTurn"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void CombatSetupListsWhatTheLoggerWatched()
    {
        var folder = Directory.CreateTempSubdirectory("hbht-parity-").FullName;
        try
        {
            File.WriteAllLines(Path.Combine(folder, "log.jsonl"),
            [
                """{"t": "enter", "name": "Combat.Setup", "pc": 2147864924, "ra": 0, "args": [0, 0, 0, 0], "seed": 7, "state": "0000", "delta": "40000000", "frame": null, "current": null, "combatant": null, "dump": "0001.rdram.gz", "watched": ["Combat.Setup", "Combat.InputMoving"]}""",
            ]);
            var log = ParityLog.Read(folder);

            Assert.True(log.Watches("Combat.InputMoving"));
            Assert.False(log.Watches("Combat.EndCast"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
