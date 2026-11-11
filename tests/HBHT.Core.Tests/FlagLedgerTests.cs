using HBHT.Core.Flags;
using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>The flag ledger (<see cref="FlagLedger"/>): every flag seen to turn on, whatever it's been since.</summary>
public class FlagLedgerTests
{
    /// <summary>Plain flags 0–9 (1 set), flag 9 the new journal entry flag, and derived 10 = 2 and not 3.</summary>
    private static FlagNetwork Network() => new(
        [
            .. Enumerable.Range(0, 10).Select(i => new FlagRecord(Flag(i), Initial: i == 1)),
            new FlagRecord(Flag(10), Formula: new FlagFormula(FlagFormulaKind.Logic, FlagOperation.Mask, 1, [Flag(2), Flag(3)])),
        ],
        Flag(9));

    [Fact]
    public void AScriptThatSetsAndClearsAFlagInOneRunLeavesItSeen()
    {
        var flags = Network();
        var ledger = flags.Ledger = new FlagLedger(flags.Count);
        var script = new ScriptRecord(RecordId.Hex(RecordTypes.Script, 0x0400), 1, ScriptMode.Conversation,
        [
            ScriptNodes.Line(0, "", 1, null, new ScriptOp.FlagSet(Flag(4))),
            ScriptNodes.Line(1, "", null, null, new ScriptOp.FlagClear(Flag(4)), new ScriptOp.ScriptEnd()),
        ]);
        var runner = new ScriptRunner(script, flags, new NoWorld(), new NoHost(), new Lcg(), _ => null);
        runner.Start();
        while (runner.Status != ScriptStatus.Ended)
            runner.Continue();

        Assert.False(flags.Get(4));
        Assert.True(ledger.Seen(4));
        Assert.Equal([4], ledger.TakeFresh());
        Assert.Empty(ledger.TakeFresh());
    }

    [Fact]
    public void ADerivedFlagIsSeenAsItsEvaluationTurnsItOnThoughItTurnsOffAgain()
    {
        var flags = Network();
        var ledger = flags.Ledger = new FlagLedger(flags.Count);
        flags.Set(2, true);
        Assert.True(flags.Get(10));
        flags.Set(3, true);
        Assert.False(flags.Get(10));
        Assert.True(ledger.Seen(10));
        Assert.Equal([2, 10, 3], ledger.TakeFresh());
    }

    [Fact]
    public void ARunOfBitsMarksTheBitsThatTurnOn()
    {
        var flags = Network();
        var ledger = flags.Ledger = new FlagLedger(flags.Count);
        flags.SetBits([5, 6, 7], 0b101);
        flags.SetBits([5, 6, 7], 0b000);
        Assert.Equal([5, 7], ledger.All());
    }

    [Fact]
    public void ANewGameAndASaveLoadedMarkNothing()
    {
        var flags = Network();
        var ledger = flags.Ledger = new FlagLedger(flags.Count);
        flags.Restore(h => h is 1 or 2);
        Assert.True(flags.Get(10));
        flags.NewGame();
        Assert.Equal(0, ledger.Count);
        Assert.Same(ledger, flags.Ledger);
    }

    [Fact]
    public void ALedgerGrowsForHandlesPastWhatItWasMadeFor()
    {
        var ledger = new FlagLedger(1);
        ledger.Mark(200);
        ledger.Mark(200);
        Assert.True(ledger.Seen(200));
        Assert.False(ledger.Seen(5000));
        Assert.Equal(1, ledger.Count);
    }

    private sealed class NoHost : IScriptHost
    {
        public void Cue(ScriptCue cue)
        {
        }
    }

    private sealed class NoWorld : IScriptWorld
    {
        public bool InParty(RecordId entity) => false;
        public bool HasItem(RecordId item) => false;
        public int SkillCheck(int skill, IRandom random) => 0;
        public int BestStat(int stat) => 0;
        public int WorstStat(int stat) => 0;
        public int MemberStat(RecordId? member, int stat) => 0;
        public int PartySize => 1;
        public string? PlayerName => "Alaron";
        public uint Gold { get; set; }
        public void AddItem(RecordId item)
        {
        }
        public bool TakeItem(RecordId item) => false;
        public bool AddMember(RecordId entity) => false;
        public void RemoveMember(RecordId entity)
        {
        }
        public void CampHeal()
        {
        }
        public bool TryRecall(Memory memory, out int value)
        {
            value = 0;
            return false;
        }
        public void Remember(Memory memory, int value)
        {
        }
        public bool AddExp(RecordId member, int amount) => false;
    }
}
