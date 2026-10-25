using System.Globalization;
using System.Numerics;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Flags;

/// <summary>
/// The event flags and the network of derived flags over them: the original's <c>GameStateFunnel</c>,
/// with <c>ProcessGameStates</c> and the getters and setters of <c>gamestatelib.cpp</c> and
/// <c>gameStateFunnel.cpp</c>, checked against 1.0's machine code.
/// </summary>
/// <remarks>
/// Flags are addressed by handle (ADR 0004): a base-game flag's handle is its number, and other
/// plugins' flags follow in load order. Setting a flag only marks the network out of date; derived
/// flags are brought up to date when one is read, as the original does.
/// </remarks>
public sealed class FlagNetwork
{
    /// <summary><c>aidyn:flag/0</c> always reads as clear, and <c>aidyn:flag/1</c> as set.</summary>
    public const int False = 0, True = 1;

    private readonly RecordId[] _ids;
    private readonly Dictionary<RecordId, int> _handles;
    private readonly bool[] _initial;
    private readonly Formula?[] _formulas;
    /// <summary>The derived flags, in the order they're evaluated.</summary>
    private readonly int[] _derived;
    private readonly RecordId?[] _journal;
    private readonly HashSet<RecordId> _entries;
    private readonly List<RecordId> _journalOrder = [];
    private readonly int _newJournalEntry;
    private readonly byte[] _values;

    private readonly record struct Formula(FlagFormulaKind Kind, FlagOperation Op, int Operand, int[] Inputs);

    /// <param name="flags">Every flag. Base-game flags must be numbered from 0 with no gaps.</param>
    /// <param name="newJournalEntry">The plain flag set when a flag with a journal entry turns on.</param>
    /// <param name="loadOrder">Where each non-base flag's plugin loads, which orders their handles.</param>
    public FlagNetwork(IEnumerable<FlagRecord> flags, RecordId newJournalEntry, Func<RecordId, int>? loadOrder = null)
    {
        var all = flags.ToList();
        var numbered = all.Where(f => f.Id.Namespace == RecordId.BaseNamespace).OrderBy(f => int.Parse(f.Id.Key, CultureInfo.InvariantCulture)).ToList();
        for (var i = 0; i < numbered.Count; i++)
        {
            if (numbered[i].Id != RecordId.Decimal(RecordTypes.Flag, i))
                throw new InvalidDataException($"The base game's flags skip from {(i == 0 ? "the start" : numbered[i - 1].Id)} to {numbered[i].Id}.");
        }
        var others = all.Where(f => f.Id.Namespace != RecordId.BaseNamespace)
            .OrderBy(f => loadOrder?.Invoke(f.Id) ?? 0).ThenBy(f => f.Id.ToString(), StringComparer.Ordinal);
        var ordered = numbered.Concat(others).ToList();

        _ids = [.. ordered.Select(f => f.Id)];
        _handles = [];
        for (var h = 0; h < _ids.Length; h++)
        {
            if (!_handles.TryAdd(_ids[h], h))
                throw new InvalidDataException($"Flag {_ids[h]} is defined twice.");
        }
        _initial = [.. ordered.Select(f => f.Formula is null && f.Initial)];
        _journal = [.. ordered.Select(f => f.Journal)];
        _entries = [.. _journal.OfType<RecordId>()];
        _formulas = [.. ordered.Select(f => f.Formula is { } formula ? Compile(f.Id, formula) : (Formula?)null)];
        _derived = [.. Enumerable.Range(0, _ids.Length).Where(h => _formulas[h] is not null)];
        _values = new byte[_ids.Length];
        _newJournalEntry = Handle(newJournalEntry);
        if (IsDerived(_newJournalEntry))
            throw new InvalidDataException($"The new journal entry flag, {newJournalEntry}, is derived.");
        NewGame();
    }

    /// <summary>The flags of the loaded plugins, as a new game starts.</summary>
    public static FlagNetwork Load(PluginSet plugins)
    {
        var state = plugins.Singleton<StateFlagsRecord>();
        var order = plugins.Plugins.Select((p, i) => (p.Manifest.Id, i)).ToDictionary(p => p.Id, p => p.i);
        return new FlagNetwork(plugins.All<FlagRecord>(RecordTypes.Flag), state.NewJournalEntry,
            id => plugins.DefinedBy(id) is { } plugin ? order[plugin] : int.MaxValue);
    }

    public int Count => _ids.Length;

    /// <summary>True while a flag has changed since the derived flags were last brought up to date.</summary>
    public bool Dirty { get; private set; }

    /// <summary>
    /// The journal entry most recently added, which the journal opens at (<c>newestJournal</c>). The
    /// journal clears it.
    /// </summary>
    public RecordId? NewestJournalEntry { get; set; }

    /// <summary>
    /// The journal entries in the order they were added, which the journal lists them in. The original lists them in
    /// its table's order, and its saves keep only the flags; the port keeps the order, which the user chose
    /// (Oct 3, 2026). An entry whose flag clears stays in its place, and the journal shows it if it's set again.
    /// </summary>
    public IReadOnlyList<RecordId> JournalOrder => _journalOrder;

    /// <summary>Whether a flag adds this journal entry.</summary>
    public bool HasJournalEntry(RecordId entry) => _entries.Contains(entry);

    /// <summary>
    /// Every journal entry a flag adds, in the order of the flags that add them, the network's: for the base game, the journal
    /// table's.
    /// </summary>
    public IEnumerable<RecordId> JournalEntries => _journal.OfType<RecordId>().Distinct();

    public RecordId Id(int handle) => _ids[handle];

    public int Handle(RecordId id) =>
        _handles.TryGetValue(id, out var handle) ? handle : throw new KeyNotFoundException($"No flag {id}.");

    public bool TryHandle(RecordId id, out int handle) => _handles.TryGetValue(id, out handle);

    public bool IsDerived(int handle) => _formulas[handle] is not null;

    /// <summary>
    /// A flag's value (<c>getEventFlag</c>). Reading a derived flag brings the network up to date first.
    /// Reading a plain one doesn't, so the new journal entry flag can lag until something else does.
    /// </summary>
    public bool Get(int handle)
    {
        switch (handle)
        {
            case False:
                return false;
            case True:
                return true;
        }
        if (Dirty && IsDerived(handle))
            Process();
        return _values[handle] != 0;
    }

    /// <summary>
    /// A flag's value by record, throwing if no loaded plugin has it. For where an unknown flag is a mistake, as in
    /// tests; the rules use <see cref="IsSet(RecordId)"/>.
    /// </summary>
    public bool Get(RecordId id) => Get(Handle(id));

    /// <summary>
    /// Whether a flag is set, an unknown one reading as clear. A mod can remove a flag that another plugin's records
    /// still name, which the base game's never do (all of the original's are here), and the rules read such a flag as
    /// clear rather than fail.
    /// </summary>
    public bool IsSet(RecordId id) => TryHandle(id, out var handle) && Get(handle);

    /// <summary>
    /// Whether a flag is set as it's stored, an unknown one reading as clear, without bringing the network up to date: reading
    /// it changes nothing, so what only draws can ask, such as the scenery beyond the 3×3 (<see cref="Chunks.SceneryRing"/>).
    /// A derived flag can lag a plain one written since the network was last brought up to date, which trek does every tick.
    /// </summary>
    public bool IsSetAsStored(RecordId id) => TryHandle(id, out var handle) && handle != False && (handle == True || Peek(handle));

    /// <summary>
    /// Whether a gate holds: whether its flag differs from its <see cref="FlagGate.Invert"/>, an unknown flag reading
    /// as clear (see <see cref="IsSet(RecordId)"/>).
    /// </summary>
    public bool Holds(FlagGate gate) => IsSet(gate.Flag) != gate.Invert;

    /// <summary>A flag as it's stored, without bringing the network up to date (<c>get_EventFlag</c>).</summary>
    public bool Peek(int handle) => _values[handle] != 0;

    /// <summary>
    /// Sets or clears a plain flag (<c>setEventFlag</c>). The game refuses to write flags 0 and 1 and
    /// derived flags, so those writes do nothing.
    /// </summary>
    public void Set(int handle, bool value)
    {
        if (handle > True && !IsDerived(handle))
            Write(handle, value);
    }

    /// <summary>A flag set or cleared by record, throwing if no loaded plugin has it; see <see cref="TrySet"/>.</summary>
    public void Set(RecordId id, bool value) => Set(Handle(id), value);

    /// <summary>
    /// Sets or clears a plain flag, ignoring an unknown one, as a mod can remove a flag that another plugin's records
    /// still write (see <see cref="IsSet(RecordId)"/>). Returns whether the flag was there; the game's refusal to write
    /// derived flags is <see cref="Set(int, bool)"/>'s, as ever.
    /// </summary>
    public bool TrySet(RecordId id, bool value)
    {
        if (!TryHandle(id, out var handle))
            return false;
        Set(handle, value);
        return true;
    }

    /// <summary>
    /// Sets or clears a plain flag as a map object does, and brings the network up to date at once (see
    /// <see cref="TrySet"/>). The original follows every map object's flag write with a read of derived flag 5626, whose
    /// value it never uses (<c>getEventFlag(0x15FA)</c>: a trigger's flag at <c>0x80010bc4</c>, a used flag at
    /// <c>0x80011760</c>, a container's opened flag at <c>0x800105ec</c>, an explosion's at <c>0x8000ffe4</c>, a secret
    /// door's at <c>0x800117c0</c>, a talker's at <c>0x800109fc</c>), and reading a derived flag while anything has changed
    /// processes the network (<c>0x800cb3ac</c>). So what the write turns on is there before the next tick, and a journal
    /// entry's notice with it.
    /// </summary>
    public bool TrySetAndProcess(RecordId id, bool value)
    {
        var known = TrySet(id, value);
        Process();
        return known;
    }

    /// <summary>
    /// <see cref="TrySetAndProcess(RecordId, bool)"/> for an object's slot that may be empty: the original writes the slot,
    /// which <c>setEventFlag</c> refuses when it's 0, and reads 5626 either way (a trigger's or harvest's Used flag through
    /// <c>MapObjects.WriteUsed</c>, <c>0x80011760</c>, a container's opened flag at <c>0x800105ec</c>, an exploded flag at
    /// <c>0x8000ffe4</c>, a talker's B at <c>0x800109fc</c>), so the network comes up to date whether or not there's a flag.
    /// </summary>
    public bool TrySetAndProcess(RecordId? id, bool value)
    {
        var known = id is { } flag && TrySet(flag, value);
        Process();
        return known;
    }

    /// <summary>
    /// Writes a number into a run of plain flags, lowest bit first (<c>set_flag_array</c>). Bits past
    /// the last flag are dropped. It marks the network out of date even when nothing changes.
    /// </summary>
    public void SetBits(IReadOnlyList<int> handles, int value)
    {
        if (handles.Any(IsDerived))
            return;
        foreach (var handle in handles)
        {
            _values[handle] = (byte)(value & 1);
            value >>= 1;
        }
        Dirty = true;
    }

    /// <summary>
    /// Every flag as a new game starts: plain flags at their initial values and derived flags
    /// evaluated from them (<c>load_gamestateFunnel</c>).
    /// </summary>
    public void NewGame()
    {
        for (var h = 0; h < _values.Length; h++)
            _values[h] = (byte)(_initial[h] ? 1 : 0);
        NewestJournalEntry = null;
        _journalOrder.Clear();
        Dirty = true;
        Process();
    }

    /// <summary>
    /// Sets every plain flag and brings the derived flags up to date, as loading a save does
    /// (<c>LoadGameState</c>). Derived flags that turn on add their journal entries, but none is left the newest:
    /// the user found the original's journal highlights nothing just after loading (Oct 17, 2026).
    /// </summary>
    /// <param name="journal">
    /// The journal's order as saved. Entries that turn on and aren't in it, as from a save made before the port kept
    /// the order or from a checkpoint, come first, in their flags' order, the table's.
    /// </param>
    public void Restore(Func<int, bool> plain, IReadOnlyList<RecordId>? journal = null)
    {
        for (var h = 0; h < _values.Length; h++)
        {
            if (!IsDerived(h))
                _values[h] = (byte)(plain(h) ? 1 : 0);
        }
        Dirty = true;
        Process();
        var on = Enumerable.Range(0, _ids.Length).Where(h => _values[h] != 0 && _journal[h] is not null)
            .Select(h => _journal[h]!.Value).Distinct().ToList();
        var saved = (journal ?? []).Where(_entries.Contains).Distinct().ToList();
        _journalOrder.Clear();
        _journalOrder.AddRange(on.Where(e => !saved.Contains(e)));
        _journalOrder.AddRange(saved);
        NewestJournalEntry = null;
    }

    /// <summary>
    /// Brings the derived flags up to date (<c>ProcessGameStates</c>): evaluates every derived flag in
    /// order, each seeing the ones before it as already updated, and starts again from the first
    /// while anything changed.
    /// </summary>
    public void Process()
    {
        while (Dirty)
        {
            Dirty = false;
            foreach (var handle in _derived)
                Write(handle, Evaluate(_formulas[handle]!.Value));
        }
    }

    /// <summary><c>set_journalentry_flag</c>: a change marks the network out of date, and may add a journal entry.</summary>
    private void Write(int handle, bool value)
    {
        var bit = (byte)(value ? 1 : 0);
        if (_values[handle] == bit)
            return;
        _values[handle] = bit;
        Dirty = true;
        if (value && _journal[handle] is { } entry)
        {
            _values[_newJournalEntry] = 1;
            NewestJournalEntry = entry;
            if (!_journalOrder.Contains(entry))
                _journalOrder.Add(entry);
        }
    }

    private bool Evaluate(Formula formula)
    {
        // The inputs as bits, the first the lowest, or how many are set (gameStateBitmask).
        var bits = 0;
        var count = 0;
        for (var i = 0; i < formula.Inputs.Length; i++)
        {
            if (_values[formula.Inputs[i]] != 0)
            {
                bits |= 1 << i;
                count++;
            }
        }
        var all = (1 << formula.Inputs.Length) - 1;
        var value = formula.Kind == FlagFormulaKind.Count ? count : bits;

        // A kind with the other kind's operation is an error the original reports and reads as clear.
        return (formula.Kind, formula.Op) switch
        {
            (FlagFormulaKind.Logic, FlagOperation.And) => (bits & all) == all,
            (FlagFormulaKind.Logic, FlagOperation.Or) => (bits & all) != 0,
            (FlagFormulaKind.Logic, FlagOperation.Xor) => BitOperations.PopCount((uint)(bits & all)) == 1,
            (FlagFormulaKind.Logic, FlagOperation.Not) => (bits & all) == 0,
            (FlagFormulaKind.Logic, FlagOperation.Mask) => (bits & all) == formula.Operand,
            (not FlagFormulaKind.Logic, FlagOperation.Equal) => value == formula.Operand,
            (not FlagFormulaKind.Logic, FlagOperation.Greater) => value > formula.Operand,
            (not FlagFormulaKind.Logic, FlagOperation.Less) => value < formula.Operand,
            (not FlagFormulaKind.Logic, FlagOperation.NotEqual) => value != formula.Operand,
            _ => false,
        };
    }

    private Formula Compile(RecordId id, FlagFormula formula)
    {
        // The original counts inputs in 16 bits and compares with a u16.
        if (formula.Inputs.Count > 16 || formula.Operand is < 0 or > ushort.MaxValue)
            throw new InvalidDataException($"Flag {id} has {formula.Inputs.Count} inputs and operand {formula.Operand}; the most is 16 and 65535.");
        return new Formula(formula.Kind, formula.Op, formula.Operand, [.. formula.Inputs.Select(Handle)]);
    }
}
