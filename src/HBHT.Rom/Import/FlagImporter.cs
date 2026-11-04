using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Db;
using HBHT.Rom.Quests;

namespace HBHT.Rom.Import;

/// <summary>
/// The flags, the journal and the flags the engine writes its state into.
/// </summary>
internal static class FlagImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Flags");
        ctx.WriteRecords("flags.json", BuildFlags(ctx.Db, FlagLabels.Read(ctx.Rom, ctx.Db)));
        ctx.WriteRecords("journal.json", BuildJournal(ctx.Db));
        ctx.WriteRecords("stateflags.json", BuildStateFlags());
    }

    internal static List<JournalRecord> BuildJournal(GameDb db) =>
        db.Journal.Select((e, i) => new JournalRecord(RecordId.Decimal(RecordTypes.Journal, i), e.Text)).ToList();

    /// <summary>The flags the engine writes its state into (<see cref="EngineFlags"/>).</summary>
    internal static StateFlagsRecord BuildStateFlags() => new(
        RecordId.Decimal(RecordTypes.StateFlags, 0),
        Flags(EngineFlags.MoonPhase), Flags(EngineFlags.TimeOfDay), Flags(EngineFlags.DayOfSeason), Flags(EngineFlags.Season),
        Flags(EngineFlags.DayOfWeek), Flags(EngineFlags.Week), Flags(EngineFlags.Rain), Flags(EngineFlags.Terrain),
        DbRecordIds.Flag(EngineFlags.NewJournalEntry),
        DbRecordIds.Flag(EngineFlags.RandomSkillChecks));

    private static RecordId[] Flags(EngineFlagRange range) => [.. Enumerable.Range(range.First, range.Bits).Select(DbRecordIds.Flag)];

    /// <summary>
    /// Every flag of the <c>GameStateFunnel</c>, with the labels engine code gives them (<see cref="FlagLabels"/>), but the
    /// journal's. Derived flag <see cref="EngineFlags.JournalTriggers"/> + i is <c>count(entry i's flag) &gt; 0</c>, whose
    /// write adds entry i (<c>GameStateFunnel.Write</c>, <c>0x800cb33c</c>); nothing else reads those 71, so they're left out
    /// and each entry's own flag names it (<see cref="FlagRecord.Journal"/>), the network doing what their writes did.
    /// </summary>
    internal static List<FlagRecord> BuildFlags(GameDb db, IReadOnlyDictionary<ushort, string> labels)
    {
        var funnel = db.GameStateFunnel;
        var listing = new Dictionary<int, RecordId>();
        for (var e = 0; e < db.Journal.Count; e++)
        {
            if (!listing.TryAdd(db.Journal[e].Flag, RecordId.Decimal(RecordTypes.Journal, e)))
                throw new InvalidDataException($"Journal entries {listing[db.Journal[e].Flag]} and {e} have the same flag, {db.Journal[e].Flag}.");
        }
        var records = new List<FlagRecord>(funnel.Flags.Count);
        for (var i = 0; i < funnel.Flags.Count; i++)
        {
            if (i >= EngineFlags.JournalTriggers)
            {
                CheckJournalTrigger(db, i);
                continue;
            }
            var flag = funnel.Flags[i];
            var label = labels.GetValueOrDefault((ushort)i) ?? i switch { 0 => "Always clear", 1 => "Always set", _ => null };
            if (i < funnel.BitFlagCount)
            {
                if (flag.Kind != Db.FlagKind.Bit)
                    throw new InvalidDataException($"Flag {i} is below the derived flags, but it's {flag.Kind}.");
                records.Add(new FlagRecord(DbRecordIds.Flag(i), label, flag.InitialValue != 0,
                    Journal: listing.TryGetValue(i, out var listed) ? listed : null));
                continue;
            }

            var kind = flag.Kind switch
            {
                Db.FlagKind.Logic => FlagFormulaKind.Logic,
                Db.FlagKind.Value => FlagFormulaKind.Value,
                Db.FlagKind.Count => FlagFormulaKind.Count,
                _ => throw new InvalidDataException($"Derived flag {i} is {flag.Kind}."),
            };
            var inputs = Enumerable.Range(flag.InputStart, flag.InputCount).Select(n => funnel.Inputs[n]).ToList();
            // An unused input still takes its bit, so leaving it out would move the others.
            if (flag.Op == Db.FlagOp.Invalid || inputs.Contains((ushort)0xFFFF) || inputs.Count > 16 || flag.InitialValue != 0)
                throw new InvalidDataException($"Derived flag {i} ({flag}) can't be written as a formula.");
            var formula = new FlagFormula(kind, (FlagOperation)flag.Op, flag.Operand, [.. inputs.Select(n => DbRecordIds.Flag(n))]);
            records.Add(new FlagRecord(DbRecordIds.Flag(i), label, false, formula, listing.TryGetValue(i, out var lists) ? lists : null));
        }
        if (listing.Keys.Any(f => f >= EngineFlags.JournalTriggers))
            throw new InvalidDataException("A journal entry's flag is a journal trigger.");
        return records;
    }

    /// <summary>
    /// Checks that journal trigger <paramref name="i"/> is what the network does in its place: <c>count(entry's flag) &gt; 0</c>
    /// over its entry's own flag.
    /// </summary>
    private static void CheckJournalTrigger(GameDb db, int i)
    {
        var funnel = db.GameStateFunnel;
        var flag = funnel.Flags[i];
        var entry = i - EngineFlags.JournalTriggers;
        if (entry >= db.Journal.Count || flag.Kind != Db.FlagKind.Count || flag.Op != Db.FlagOp.Greater || flag.Operand != 0
            || flag.InputCount != 1 || funnel.Inputs[flag.InputStart] != db.Journal[entry].Flag)
            throw new InvalidDataException($"Journal trigger {i} ({flag}) isn't its entry's flag counted above 0.");
    }
}
