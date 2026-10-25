using HBHT.Core.Calendar;
using HBHT.Core.Flags;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

public class FlagNetworkTests
{
    /// <summary>
    /// Plain flags 0–9 (1 set, as in the base game), then the derived flags given, numbered from 10.
    /// Flag 9 is the new journal entry flag.
    /// </summary>
    private static FlagNetwork Network(params FlagFormula[] derived) => JournalNetwork([.. derived.Select(f => (f, (RecordId?)null))]);

    private static FlagNetwork JournalNetwork(params (FlagFormula Formula, RecordId? Journal)[] derived) => new(
        [
            .. Enumerable.Range(0, 10).Select(i => new FlagRecord(Flag(i), Initial: i == 1)),
            .. derived.Select((d, i) => new FlagRecord(Flag(10 + i), Formula: d.Formula, Journal: d.Journal)),
        ],
        Flag(9));

    private static FlagFormula Logic(FlagOperation op, int operand, params int[] inputs) =>
        new(FlagFormulaKind.Logic, op, operand, [.. inputs.Select(Flag)]);

    [Theory]
    [InlineData(FlagOperation.And, 0, new[] { false, false, false, true })]
    [InlineData(FlagOperation.Or, 0, new[] { false, true, true, true })]
    [InlineData(FlagOperation.Xor, 0, new[] { false, true, true, false })]
    [InlineData(FlagOperation.Not, 0, new[] { true, false, false, false })]
    [InlineData(FlagOperation.Mask, 2, new[] { false, false, true, false })]
    public void LogicFlagsGateTheirInputs(FlagOperation op, int operand, bool[] expected)
    {
        var flags = Network(Logic(op, operand, 2, 3));
        for (var bits = 0; bits < 4; bits++)
        {
            flags.Set(2, (bits & 1) != 0);
            flags.Set(3, (bits & 2) != 0);
            Assert.Equal(expected[bits], flags.Get(10));
        }
    }

    [Fact]
    public void ValueFlagsReadTheirInputsAsBitsAndCountFlagsCountThem()
    {
        // Inputs 2 and 4 set: 0b101 = 5 as bits, 2 as a count.
        var flags = Network(
            new FlagFormula(FlagFormulaKind.Value, FlagOperation.Equal, 5, [Flag(2), Flag(3), Flag(4)]),
            new FlagFormula(FlagFormulaKind.Value, FlagOperation.Greater, 4, [Flag(2), Flag(3), Flag(4)]),
            new FlagFormula(FlagFormulaKind.Value, FlagOperation.Less, 5, [Flag(2), Flag(3), Flag(4)]),
            new FlagFormula(FlagFormulaKind.Count, FlagOperation.Equal, 2, [Flag(2), Flag(3), Flag(4)]),
            new FlagFormula(FlagFormulaKind.Count, FlagOperation.NotEqual, 2, [Flag(2), Flag(3), Flag(4)]),
            // A value flag with a logic operation is an error the original reads as clear.
            new FlagFormula(FlagFormulaKind.Value, FlagOperation.Or, 0, [Flag(2)]));
        flags.Set(2, true);
        flags.Set(4, true);
        Assert.Equal([true, true, false, true, false, false], Enumerable.Range(10, 6).Select(flags.Get));
    }

    [Fact]
    public void DerivedFlagsUpdateWhenOneIsRead()
    {
        var flags = Network(Logic(FlagOperation.Or, 0, 2));
        flags.Set(2, true);
        Assert.True(flags.Dirty);
        Assert.False(flags.Peek(10));
        // Reading a plain flag doesn't bring the network up to date.
        Assert.True(flags.Get(2));
        Assert.True(flags.Dirty);
        Assert.True(flags.Get(10));
        Assert.False(flags.Dirty);
    }

    [Fact]
    public void AFlagReadingALaterOneIsUpdatedOnTheNextPass()
    {
        // 10 reads 11, which is evaluated after it, so the first pass leaves 10 behind.
        var flags = Network(Logic(FlagOperation.Or, 0, 11), Logic(FlagOperation.Or, 0, 2));
        flags.Set(2, true);
        Assert.True(flags.Get(10));
    }

    [Fact]
    public void FlagsZeroAndOneAreConstantsAndDerivedFlagsCantBeSet()
    {
        var flags = Network(Logic(FlagOperation.Or, 0, 2));
        flags.Set(0, true);
        flags.Set(1, false);
        flags.Set(10, true);
        Assert.Equal((false, true, false), (flags.Get(0), flags.Get(1), flags.Get(10)));
        Assert.False(flags.Dirty);
    }

    [Fact]
    public void AJournalFlagTurningOnRaisesTheNewEntryFlag()
    {
        var entry = RecordId.Decimal(RecordTypes.Journal, 4);
        var flags = JournalNetwork((Logic(FlagOperation.Or, 0, 2), entry));
        Assert.Null(flags.NewestJournalEntry);

        flags.Set(2, true);
        Assert.True(flags.Get(10));
        Assert.Equal(entry, flags.NewestJournalEntry);
        Assert.True(flags.Get(9));

        // Turning off doesn't.
        flags.Set(9, false);
        flags.NewestJournalEntry = null;
        flags.Set(2, false);
        Assert.False(flags.Get(10));
        Assert.Equal((false, null), (flags.Get(9), flags.NewestJournalEntry));
    }

    [Fact]
    public void AMapObjectsWriteBringsTheNetworkUpToDateWithAFlagInItsSlotOrNot()
    {
        // A script's write waits (plain flags don't process); a map object's empty slot still reads 5626, as 1.0's does.
        var entry = RecordId.Decimal(RecordTypes.Journal, 4);
        var flags = JournalNetwork((Logic(FlagOperation.Or, 0, 2), entry));
        flags.Set(2, true);
        Assert.False(flags.Peek(9));
        Assert.False(flags.TrySetAndProcess((RecordId?)null, true));
        Assert.True(flags.Peek(9));
    }

    [Fact]
    public void TheJournalListsEntriesAsTheyTurnOnAndARestorePutsUnsavedOnesFirst()
    {
        RecordId Entry(int n) => RecordId.Decimal(RecordTypes.Journal, n);
        var flags = JournalNetwork((Logic(FlagOperation.Or, 0, 2), Entry(0)), (Logic(FlagOperation.Or, 0, 3), Entry(1)),
            (Logic(FlagOperation.Or, 0, 4), Entry(2)));

        // Entry 2, then 0: the order they turned on, not the table's. Clearing one keeps its place.
        flags.Set(4, true);
        flags.Process();
        flags.Set(2, true);
        flags.Process();
        flags.Set(2, false);
        flags.Process();
        flags.Set(2, true);
        flags.Process();
        Assert.Equal([Entry(2), Entry(0)], flags.JournalOrder);

        // A save from before the port kept the order lists nothing, so it's the flags' order.
        flags.Restore(h => h is 1 or 2 or 3 or 4);
        Assert.Equal([Entry(0), Entry(1), Entry(2)], flags.JournalOrder);

        flags.NewGame();
        Assert.Empty(flags.JournalOrder);

        // Loading: entry 1 isn't in the saved order, so it comes first, and no entry is the newest.
        flags.Restore(h => h is 1 or 2 or 3 or 4, [Entry(2), Entry(0)]);
        Assert.Equal([Entry(1), Entry(2), Entry(0)], flags.JournalOrder);
        Assert.Null(flags.NewestJournalEntry);
    }

    [Fact]
    public void SetBitsWritesTheLowestBitFirstAndDropsTheRest()
    {
        var flags = Network();
        flags.Process();
        flags.SetBits([2, 3, 4], 0b1101);
        Assert.Equal([true, false, true], new[] { 2, 3, 4 }.Select(flags.Get));
        // Even an unchanged value marks the network out of date, as set_flag_array does.
        flags.Process();
        flags.SetBits([2, 3, 4], 0b101);
        Assert.True(flags.Dirty);
    }

    [Fact]
    public void RestoringPlainFlagsEvaluatesTheDerivedOnes()
    {
        var flags = Network(Logic(FlagOperation.And, 0, 2, 3));
        flags.Restore(h => h is 1 or 2 or 3);
        Assert.True(flags.Peek(10));
        flags.NewGame();
        Assert.Equal((false, false, true), (flags.Peek(2), flags.Peek(10), flags.Peek(1)));
    }

    [Fact]
    public void OtherPluginsFlagsComeAfterTheBaseGames()
    {
        var flags = new FlagNetwork(
            [
                new FlagRecord(RecordId.Parse("mymod:flag/met_the_hermit")),
                .. Enumerable.Range(0, 3).Select(i => new FlagRecord(Flag(i))),
                new FlagRecord(RecordId.Parse("mymod:flag/hermit_friendly"), Formula: new(FlagFormulaKind.Logic, FlagOperation.Or, 0,
                    [RecordId.Parse("mymod:flag/met_the_hermit")])),
            ],
            Flag(2));
        Assert.Equal((2, 3, 4), (flags.Handle(Flag(2)), flags.Handle(RecordId.Parse("mymod:flag/hermit_friendly")),
            flags.Handle(RecordId.Parse("mymod:flag/met_the_hermit"))));
        flags.Set(RecordId.Parse("mymod:flag/met_the_hermit"), true);
        Assert.True(flags.Get(RecordId.Parse("mymod:flag/hermit_friendly")));
    }

    [Fact]
    public void AnUnknownFlagReadsClearAndItsSetIsIgnored()
    {
        var flags = Network();
        var removed = RecordId.Parse("mymod:flag/removed");

        Assert.False(flags.IsSet(removed));
        Assert.False(flags.TrySet(removed, true));
        Assert.False(flags.IsSet(removed));
        Assert.Throws<KeyNotFoundException>(() => flags.Get(removed));
        Assert.Throws<KeyNotFoundException>(() => flags.Set(removed, true));

        Assert.True(flags.TrySet(Flag(2), true));
        Assert.True(flags.IsSet(Flag(2)));
        Assert.True(flags.IsSet(Flag(1)));
    }

    [Fact]
    public void AGateHoldsWhileItsFlagDiffersFromItsInversionAndAnUnknownFlagIsClear()
    {
        var flags = Network();
        var removed = RecordId.Parse("mymod:flag/removed");

        Assert.False(flags.Holds(new FlagGate(Flag(2))));
        Assert.True(flags.Holds(new FlagGate(Flag(2), Invert: true)));
        flags.Set(2, true);
        Assert.True(flags.Holds(new FlagGate(Flag(2))));
        Assert.False(flags.Holds(new FlagGate(Flag(2), Invert: true)));
        Assert.False(flags.Holds(new FlagGate(removed)));
        Assert.True(flags.Holds(new FlagGate(removed, Invert: true)));
    }

    [Fact]
    public void TheCalendarIsWrittenIntoItsFlags()
    {
        var flags = new FlagNetwork(Enumerable.Range(0, 32).Select(i => new FlagRecord(Flag(i))), Flag(31));
        RecordId[] Run(int first, int bits) => [.. Enumerable.Range(first, bits).Select(Flag)];
        var state = new StateFlags(flags, new StateFlagsRecord(RecordId.Parse("aidyn:stateflags/0"),
            Run(2, 2), Run(4, 3), Run(7, 6), Run(13, 3), Run(16, 3), Run(19, 3), Run(22, 2), Run(24, 4), Flag(31)));
        int Read(int first, int bits) => Enumerable.Range(0, bits).Sum(i => flags.Get(first + i) ? 1 << i : 0);

        // A new game starts at noon on day 2 of week 6: the afternoon, with the moon new.
        state.SetTime(GameClock.NewGame());
        state.SetTerrain(18);
        Assert.Equal((0, (int)PartOfDay.Afternoon, 44, 0, 2, 6), (Read(2, 2), Read(4, 3), Read(7, 6), Read(13, 3), Read(16, 3), Read(19, 3)));
        // Four flags keep terrain 18's low bits.
        Assert.Equal(2, Read(24, 4));
    }
}
