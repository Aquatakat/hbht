using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Db;
using Aidyn.Rom.Quests;

namespace Aidyn.Rom.Import;

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
        db.Journal.Select((e, i) => new JournalRecord(RecordId.Decimal(RecordTypes.Journal, i), DbRecordIds.Flag(e.Flag), e.Text)).ToList();

    /// <summary>The flags the engine writes its state into (<see cref="EngineFlags"/>).</summary>
    internal static StateFlagsRecord BuildStateFlags() => new(
        RecordId.Decimal(RecordTypes.StateFlags, 0),
        Flags(EngineFlags.MoonPhase), Flags(EngineFlags.TimeOfDay), Flags(EngineFlags.DayOfSeason), Flags(EngineFlags.Season),
        Flags(EngineFlags.DayOfWeek), Flags(EngineFlags.Week), Flags(EngineFlags.Rain), Flags(EngineFlags.Terrain),
        DbRecordIds.Flag(EngineFlags.NewJournalEntry),
        DbRecordIds.Flag(EngineFlags.RandomSkillChecks));

    private static RecordId[] Flags(EngineFlagRange range) => [.. Enumerable.Range(range.First, range.Bits).Select(DbRecordIds.Flag)];

    /// <summary>
    /// Every flag of the <c>GameStateFunnel</c>, with the labels engine code gives them (<see cref="FlagLabels"/>). Derived
    /// flags from <see cref="EngineFlags.JournalTriggers"/> on each add the journal entry of their
    /// position after it (<c>set_journalentry_flag</c>).
    /// </summary>
    internal static List<FlagRecord> BuildFlags(GameDb db, IReadOnlyDictionary<ushort, string> labels)
    {
        var funnel = db.GameStateFunnel;
        var records = new List<FlagRecord>(funnel.Flags.Count);
        for (var i = 0; i < funnel.Flags.Count; i++)
        {
            var flag = funnel.Flags[i];
            var label = labels.GetValueOrDefault((ushort)i) ?? i switch { 0 => "Always clear", 1 => "Always set", _ => null };
            if (i < funnel.BitFlagCount)
            {
                if (flag.Kind != Db.FlagKind.Bit)
                    throw new InvalidDataException($"Flag {i} is below the derived flags, but it's {flag.Kind}.");
                records.Add(new FlagRecord(DbRecordIds.Flag(i), label, flag.InitialValue != 0));
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
            var entry = i - EngineFlags.JournalTriggers;
            RecordId? journal = entry < 0 ? null
                : entry < db.Journal.Count ? RecordId.Decimal(RecordTypes.Journal, entry)
                : throw new InvalidDataException($"Journal trigger {i} has no journal entry.");
            records.Add(new FlagRecord(DbRecordIds.Flag(i), label, false, formula, journal));
        }
        return records;
    }
}
