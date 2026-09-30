using Aidyn.Plugins.Records;

namespace Aidyn.Plugins;

/// <summary>What a record does with a flag.</summary>
public enum FlagAccess
{
    Read,
    Set,
    Clear,
}

/// <summary>Where a use of a flag comes from, which says what the use means.</summary>
public enum FlagRole
{
    /// <summary>A script node's SetFlag, ClearFlag or CheckFlag op.</summary>
    ScriptOp,
    /// <summary>A script's done flag, set when it ends.</summary>
    ScriptDone,
    /// <summary>A script's battle flag, set by winning its battle.</summary>
    ScriptBattle,
    /// <summary>An input of a derived flag's formula. <see cref="FlagUse.By"/> is the derived flag, which reads it.</summary>
    DerivedInput,
    /// <summary>
    /// A flag's journal entry. <see cref="FlagUse.By"/> is the entry, which appears when the flag turns
    /// on, so the entry reads the flag.
    /// </summary>
    JournalAdds,
    /// <summary>A journal entry's flag, which the journal lists it by. <see cref="FlagUse.By"/> is the entry, which reads it.</summary>
    JournalListed,
    /// <summary>A map object's visibility gate. <see cref="FlagUse.By"/> is the object (<c>aidyn:voxel/…</c>), which reads the flag.</summary>
    ObjectVisible,
    /// <summary>The state flags record: the engine writes the flags it keeps its state in, and reads the random skill check option.</summary>
    Engine,
}

/// <summary>One use of a flag.</summary>
/// <param name="Flag">The flag used.</param>
/// <param name="By">The record that uses it, which the role explains.</param>
/// <param name="Access">Whether the record reads the flag, sets it or clears it.</param>
/// <param name="Role">Where the use comes from.</param>
/// <param name="At">The script node, for <see cref="FlagRole.ScriptOp"/>. Null for other roles.</param>
/// <param name="Inverted">For an object gate: whether it holds while the flag is clear.</param>
public sealed record FlagUse(RecordId Flag, RecordId By, FlagAccess Access, FlagRole Role, int? At = null, bool Inverted = false);

/// <summary>
/// An edge of the atlas graph, pointing the way things happen: from a setter to the flag it sets,
/// and from a flag to what reads it.
/// </summary>
public sealed record QuestEdge(RecordId From, RecordId To, FlagRole Role, FlagAccess Access);

/// <summary>
/// The quest atlas over plugin records: how each record uses each event flag, the atlas's
/// upstream and downstream graph, and a search over script text. Built once over a
/// <see cref="PluginSet"/>, like <see cref="PluginCatalog"/>.
/// </summary>
/// <remarks>
/// A new kind of record that uses flags is one more loop in <see cref="Build"/>.
/// </remarks>
public sealed class QuestIndex
{
    private readonly ILookup<RecordId, FlagUse> _byFlag;
    private readonly ILookup<RecordId, FlagUse> _byUser;
    private readonly ILookup<RecordId, QuestEdge> _into;
    private readonly ILookup<RecordId, QuestEdge> _outOf;
    private readonly List<(RecordId Script, int Node, string Text)> _text;

    private QuestIndex(List<FlagUse> uses, List<(RecordId, int, string)> text)
    {
        _text = text;
        _byFlag = uses.OrderBy(u => u, Comparer<FlagUse>.Create(CompareByUser)).ToLookup(u => u.Flag);
        _byUser = uses.OrderBy(u => u, Comparer<FlagUse>.Create(CompareByFlag)).ToLookup(u => u.By);
        var edges = uses.Select(Edge).Distinct().ToList();
        _into = edges.OrderBy(e => e, Comparer<QuestEdge>.Create((a, b) => CompareEdges(a, b, a.From, b.From))).ToLookup(e => e.To);
        _outOf = edges.OrderBy(e => e, Comparer<QuestEdge>.Create((a, b) => CompareEdges(a, b, a.To, b.To))).ToLookup(e => e.From);
    }

    /// <summary>Reads every script, flag, journal entry, map and the state flags. The base plugin takes a second or two.</summary>
    public static QuestIndex Build(PluginSet plugins)
    {
        var uses = new List<FlagUse>();
        var text = new List<(RecordId, int, string)>();

        foreach (var script in plugins.All<DialogueRecord>(RecordTypes.Dialogue))
        {
            foreach (var node in script.Nodes)
            {
                if (node.Text.Length > 0)
                    text.Add((script.Id, node.Index, node.Text));
                foreach (var op in node.Ops ?? [])
                {
                    if (op.Target is { Type: RecordTypes.Flag } flag && ScriptAccess(op.Command) is { } access)
                        uses.Add(new FlagUse(flag, script.Id, access, FlagRole.ScriptOp, node.Index));
                }
            }
            if (script.DoneFlag is { } done)
                uses.Add(new FlagUse(done, script.Id, FlagAccess.Set, FlagRole.ScriptDone));
            if (script.BattleFlag is { } battle)
                uses.Add(new FlagUse(battle, script.Id, FlagAccess.Set, FlagRole.ScriptBattle));
        }

        foreach (var flag in plugins.All<FlagRecord>(RecordTypes.Flag))
        {
            foreach (var input in flag.Formula?.Inputs ?? [])
                uses.Add(new FlagUse(input, flag.Id, FlagAccess.Read, FlagRole.DerivedInput));
            if (flag.Journal is { } entry)
                uses.Add(new FlagUse(flag.Id, entry, FlagAccess.Read, FlagRole.JournalAdds));
        }

        foreach (var entry in plugins.All<JournalRecord>(RecordTypes.Journal))
            uses.Add(new FlagUse(entry.Flag, entry.Id, FlagAccess.Read, FlagRole.JournalListed));

        foreach (var map in plugins.All<MapRecord>(RecordTypes.Map))
        {
            foreach (var mapObject in map.Objects)
            {
                if (IsConstant(mapObject.Visible.Flag))
                    continue;
                var voxel = RecordId.Parse($"{map.Id.Namespace}:{RecordTypes.Voxel}/{map.Id.Key}/{mapObject.Index}");
                uses.Add(new FlagUse(mapObject.Visible.Flag, voxel, FlagAccess.Read, FlagRole.ObjectVisible, Inverted: mapObject.Visible.Invert));
            }
        }

        foreach (var state in plugins.All<StateFlagsRecord>(RecordTypes.StateFlags))
        {
            IReadOnlyList<RecordId>[] runs =
                [state.MoonPhase, state.TimeOfDay, state.DayOfSeason, state.Season, state.DayOfWeek, state.Week, state.Rain, state.Terrain];
            foreach (var flag in runs.SelectMany(run => run).Append(state.NewJournalEntry))
                uses.Add(new FlagUse(flag, state.Id, FlagAccess.Set, FlagRole.Engine));
            if (state.RandomSkillChecks is { } option)
                uses.Add(new FlagUse(option, state.Id, FlagAccess.Read, FlagRole.Engine));
        }

        return new QuestIndex(uses, text);
    }

    /// <summary>Every use of a flag, by role, then by the record that uses it, then by script node.</summary>
    public IReadOnlyList<FlagUse> UsesOf(RecordId flag) => [.. _byFlag[flag]];

    /// <summary>
    /// Every flag a script, flag, journal entry, map object or the state flags record uses, by role,
    /// then by script node, then by flag.
    /// </summary>
    public IReadOnlyList<FlagUse> UsesBy(RecordId record) => [.. _byUser[record]];

    /// <summary>
    /// The edges into <paramref name="key"/>, by where they come from: for a flag, its setters and,
    /// if it's derived, its inputs; for a script, the flags it checks; for a map object, the flag
    /// that gates it.
    /// </summary>
    public IReadOnlyList<QuestEdge> Upstream(RecordId key) => [.. _into[key]];

    /// <summary>
    /// The edges out of <paramref name="key"/>, by where they lead: for a flag, what reads it; for a
    /// script, the flags it sets or clears, its done and battle flags included.
    /// </summary>
    public IReadOnlyList<QuestEdge> Downstream(RecordId key) => [.. _outOf[key]];

    /// <summary>The nodes whose text holds <paramref name="text"/>, ignoring case, in script order and then node order.</summary>
    public IReadOnlyList<(RecordId Script, int Node)> FindText(string text) =>
        text.Length == 0 ? [] : [.. _text.Where(t => t.Text.Contains(text, StringComparison.OrdinalIgnoreCase)).Select(t => (t.Script, t.Node))];

    /// <summary>Flag 0 always reads as clear and flag 1 as set, so a gate on either is a constant, not quest state.</summary>
    private static bool IsConstant(RecordId flag) =>
        flag.Namespace == RecordId.BaseNamespace && flag.Type == RecordTypes.Flag && flag.Key is "0" or "1";

    private static FlagAccess? ScriptAccess(DialogueCommand command) => command switch
    {
        DialogueCommand.CheckFlag => FlagAccess.Read,
        DialogueCommand.SetFlag => FlagAccess.Set,
        DialogueCommand.ClearFlag => FlagAccess.Clear,
        _ => null,
    };

    private static QuestEdge Edge(FlagUse use) => use.Access == FlagAccess.Read
        ? new QuestEdge(use.Flag, use.By, use.Role, use.Access)
        : new QuestEdge(use.By, use.Flag, use.Role, use.Access);

    private static int CompareByUser(FlagUse a, FlagUse b)
    {
        var c = a.Role.CompareTo(b.Role);
        if (c == 0)
            c = Compare(a.By, b.By);
        if (c == 0)
            c = Nullable.Compare(a.At, b.At);
        return c != 0 ? c : a.Access.CompareTo(b.Access);
    }

    private static int CompareByFlag(FlagUse a, FlagUse b)
    {
        var c = a.Role.CompareTo(b.Role);
        if (c == 0)
            c = Nullable.Compare(a.At, b.At);
        if (c == 0)
            c = Compare(a.Flag, b.Flag);
        return c != 0 ? c : a.Access.CompareTo(b.Access);
    }

    private static int CompareEdges(QuestEdge a, QuestEdge b, RecordId otherA, RecordId otherB)
    {
        var c = Compare(otherA, otherB);
        if (c == 0)
            c = a.Role.CompareTo(b.Role);
        return c != 0 ? c : a.Access.CompareTo(b.Access);
    }

    /// <summary>By type, then namespace, then key, with shorter keys first so that 47 comes before 5423.</summary>
    private static int Compare(RecordId a, RecordId b)
    {
        var c = string.CompareOrdinal(a.Type, b.Type);
        if (c == 0)
            c = string.CompareOrdinal(a.Namespace, b.Namespace);
        if (c == 0)
            c = a.Key.Length.CompareTo(b.Key.Length);
        return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
    }
}
