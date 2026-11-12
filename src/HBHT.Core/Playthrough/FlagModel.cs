using System.Numerics;
using HBHT.Core.Flags;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>What the planner makes of a flag.</summary>
public enum FlagKind
{
    /// <summary>Flag 0 or 1, always clear or always set.</summary>
    Constant,
    /// <summary>Set and cleared by what happens: scripts, map objects, members, items, battles.</summary>
    Plain,
    /// <summary>
    /// Written by the engine as time passes or the player moves (<see cref="StateFlagsRecord"/>): the calendar, the rain, the terrain
    /// and the new journal entry flag. The planner takes either value as one it can wait or walk for.
    /// </summary>
    Engine,
    /// <summary>Computed from others by its formula (<see cref="FlagRecord.Formula"/>).</summary>
    Derived,
}

/// <summary>
/// The flags as the planner sees them: each one's handle (<see cref="FlagNetwork"/>'s), its kind, and the derived flags' formulas,
/// which it evaluates over what each input can be. A value set is two bits: <see cref="CanBeClear"/> and <see cref="CanBeSet"/>.
/// </summary>
public sealed class FlagModel
{
    public const byte CanBeClear = 1, CanBeSet = 2, Either = CanBeClear | CanBeSet;

    private readonly FlagNetwork _network;
    private readonly FlagKind[] _kinds;
    private readonly FlagFormula?[] _formulas;
    private readonly int[][] _inputs;
    private readonly int[] _derived;
    private readonly int[][] _readers;

    public FlagModel(PluginSet plugins)
    {
        _network = FlagNetwork.Load(plugins);
        var count = _network.Count;
        _kinds = new FlagKind[count];
        _formulas = new FlagFormula?[count];
        _inputs = new int[count][];
        foreach (var flag in plugins.All<FlagRecord>(RecordTypes.Flag))
        {
            var h = _network.Handle(flag.Id);
            _formulas[h] = flag.Formula;
            _inputs[h] = flag.Formula is { } formula ? [.. formula.Inputs.Select(_network.Handle)] : [];
            _kinds[h] = flag.Formula is not null ? FlagKind.Derived : FlagKind.Plain;
        }
        _kinds[FlagNetwork.False] = _kinds[FlagNetwork.True] = FlagKind.Constant;
        var state = plugins.Singleton<StateFlagsRecord>();
        IReadOnlyList<RecordId>[] runs = [state.MoonPhase, state.TimeOfDay, state.DayOfSeason, state.Season, state.DayOfWeek, state.Week,
            state.Rain, state.Terrain, [state.NewJournalEntry]];
        foreach (var flag in runs.SelectMany(r => r))
        {
            if (_network.TryHandle(flag, out var h) && _kinds[h] == FlagKind.Plain)
                _kinds[h] = FlagKind.Engine;
        }
        _derived = [.. Enumerable.Range(0, count).Where(h => _kinds[h] == FlagKind.Derived)];
        var readers = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        foreach (var d in _derived)
            foreach (var input in _inputs[d].Distinct())
                readers[input].Add(d);
        _readers = [.. readers.Select(r => r.ToArray())];
        _engineInputs = new int[count][];
        for (var h = 0; h < count; h++)
            _engineInputs[h] = _kinds[h] == FlagKind.Engine ? [h] : [];
        // Through the derived flags between, in as many passes as their forward references need.
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var d in _derived)
            {
                var engine = _inputs[d].SelectMany(i => _engineInputs[i]).Distinct().Order().ToArray();
                if (engine.Length != _engineInputs[d].Length)
                    (_engineInputs[d], changed) = (engine, true);
            }
        }
        _moonAndRain = [.. state.MoonPhase.Concat(state.Rain).Where(f => _network.TryHandle(f, out _)).Select(_network.Handle)];
        _calendar = [.. new[] { state.DayOfSeason, state.Season, state.DayOfWeek, state.Week }.SelectMany(r => r)
            .Where(f => _network.TryHandle(f, out _)).Select(_network.Handle)];
        _timeOfDay = [.. state.TimeOfDay.Where(f => _network.TryHandle(f, out _)).Select(_network.Handle)];
    }

    private readonly int[][] _engineInputs;
    private readonly HashSet<int> _moonAndRain;
    private readonly HashSet<int> _calendar;
    private readonly HashSet<int> _timeOfDay;

    /// <summary>
    /// The engine flags a flag hangs on: an engine flag itself, a derived flag's through its inputs and any derived flags between them,
    /// none for anything else. The clock, the weather and the terrain set these as play goes on, not any step.
    /// </summary>
    public IReadOnlyList<int> EngineInputs(int handle) => _engineInputs[handle];

    /// <summary>
    /// How long a flag over the engine's (<see cref="EngineInputs"/>) can take to come true while the player stands and waits, in game
    /// time: a day for the time of day; four weeks, the moon's cycle, for the moon's phase and the rain; nothing for the date and the season
    /// (a job doesn't wait months: the trunk's own game, some 70 days, sees the date move on), the terrain (walking) and the journal's flag.
    /// </summary>
    public uint Horizon(int handle)
    {
        var engine = _engineInputs[handle];
        if (engine.Length == 0 || engine.Any(_calendar.Contains) || engine.Any(e => _kinds[e] == FlagKind.Engine && !IsClock(e)))
            return 0;
        return engine.Any(_moonAndRain.Contains) ? 4 * Calendar.GameClock.Week : Calendar.GameClock.Day;
    }

    /// <summary>Whether an engine flag is the clock's or the weather's: the time of day, the moon, the date, the rain.</summary>
    private bool IsClock(int engine) => _moonAndRain.Contains(engine) || _calendar.Contains(engine) || _timeOfDay.Contains(engine);

    public int Count => _kinds.Length;

    public FlagKind Kind(int handle) => _kinds[handle];

    public int Handle(RecordId flag) => _network.Handle(flag);

    public bool TryHandle(RecordId flag, out int handle) => _network.TryHandle(flag, out handle);

    public RecordId Id(int handle) => _network.Id(handle);

    /// <summary>The derived flags, in the order the network evaluates them.</summary>
    public IReadOnlyList<int> Derived => _derived;

    /// <summary>A derived flag's inputs, by handle.</summary>
    public IReadOnlyList<int> Inputs(int handle) => _inputs[handle];

    public FlagFormula? Formula(int handle) => _formulas[handle];

    /// <summary>The derived flags that read a flag.</summary>
    public IReadOnlyList<int> Readers(int handle) => _readers[handle];

    /// <summary>
    /// What every flag can be, from what each plain flag can be: constants as they are, engine flags either way, derived flags by
    /// their formulas in the network's order, again until nothing changes (their forward references), each counting every
    /// combination its inputs can take.
    /// </summary>
    /// <param name="plain">What a plain flag can be, by handle: <see cref="CanBeClear"/>, <see cref="CanBeSet"/> or both.</param>
    public byte[] Evaluate(Func<int, byte> plain)
    {
        var values = new byte[_kinds.Length];
        for (var h = 0; h < values.Length; h++)
        {
            values[h] = _kinds[h] switch
            {
                FlagKind.Constant => h == FlagNetwork.True ? CanBeSet : CanBeClear,
                FlagKind.Plain => plain(h),
                FlagKind.Engine => Either,
                _ => CanBeClear,
            };
        }
        Settle(values);
        return values;
    }

    /// <summary>Brings the derived flags in <paramref name="values"/> up to date with the rest, as <see cref="Evaluate"/> does.</summary>
    public void Settle(byte[] values)
    {
        for (var pass = 0; pass < 16; pass++)
        {
            var changed = false;
            foreach (var d in _derived)
            {
                var value = Possible(d, values);
                // After a few passes, only grow, so a cycle settles.
                if (pass >= 4)
                    value |= values[d];
                if (value != values[d])
                {
                    values[d] = value;
                    changed = true;
                }
            }
            if (!changed)
                return;
        }
    }

    /// <summary>What a derived flag can be, given what its inputs can be.</summary>
    public byte Possible(int derived, byte[] values)
    {
        var inputs = _inputs[derived];
        var formula = _formulas[derived]!;
        var fixedBits = 0;
        var free = new List<int>();
        for (var i = 0; i < inputs.Length; i++)
        {
            var v = values[inputs[i]];
            if (v == CanBeSet)
                fixedBits |= 1 << i;
            else if (v == Either)
                free.Add(i);
            else if (v == 0)
                return 0;
        }
        byte result = 0;
        var combinations = 1 << Math.Min(free.Count, 16);
        for (var c = 0; c < combinations && result != Either; c++)
        {
            var bits = fixedBits;
            for (var j = 0; j < free.Count && j < 16; j++)
            {
                if ((c & (1 << j)) != 0)
                    bits |= 1 << free[j];
            }
            result |= Holds(formula, bits, inputs.Length) ? CanBeSet : CanBeClear;
        }
        return result;
    }

    /// <summary>
    /// A way to make a derived flag <paramref name="want"/>: the value each input takes, from what the inputs can be, or null if
    /// none does. The planner's relaxation takes the inputs it names as the flag's preconditions.
    /// </summary>
    public bool[]? Witness(int derived, byte[] values, bool want)
    {
        var inputs = _inputs[derived];
        var formula = _formulas[derived]!;
        var fixedBits = 0;
        var free = new List<int>();
        for (var i = 0; i < inputs.Length; i++)
        {
            var v = values[inputs[i]];
            if (v == CanBeSet)
                fixedBits |= 1 << i;
            else if (v == Either)
                free.Add(i);
            else if (v == 0)
                return null;
        }
        var combinations = 1 << Math.Min(free.Count, 16);
        for (var c = 0; c < combinations; c++)
        {
            var bits = fixedBits;
            for (var j = 0; j < free.Count && j < 16; j++)
            {
                if ((c & (1 << j)) != 0)
                    bits |= 1 << free[j];
            }
            if (Holds(formula, bits, inputs.Length) == want)
                return [.. Enumerable.Range(0, inputs.Length).Select(i => (bits & (1 << i)) != 0)];
        }
        return null;
    }

    /// <summary>The formula over its inputs as bits, the first the lowest: <see cref="FlagNetwork"/>'s evaluation.</summary>
    private static bool Holds(FlagFormula formula, int bits, int count)
    {
        var all = (1 << count) - 1;
        bits &= all;
        var value = formula.Kind == FlagFormulaKind.Count ? BitOperations.PopCount((uint)bits) : bits;
        return (formula.Kind, formula.Op) switch
        {
            (FlagFormulaKind.Logic, FlagOperation.And) => bits == all,
            (FlagFormulaKind.Logic, FlagOperation.Or) => bits != 0,
            (FlagFormulaKind.Logic, FlagOperation.Xor) => BitOperations.PopCount((uint)bits) == 1,
            (FlagFormulaKind.Logic, FlagOperation.Not) => bits == 0,
            (FlagFormulaKind.Logic, FlagOperation.Mask) => bits == formula.Operand,
            (not FlagFormulaKind.Logic, FlagOperation.Equal) => value == formula.Operand,
            (not FlagFormulaKind.Logic, FlagOperation.Greater) => value > formula.Operand,
            (not FlagFormulaKind.Logic, FlagOperation.Less) => value < formula.Operand,
            (not FlagFormulaKind.Logic, FlagOperation.NotEqual) => value != formula.Operand,
            _ => false,
        };
    }
}
