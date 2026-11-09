namespace HBHT.Core.Playthrough;

/// <summary>
/// The quest graph with nothing ever undone (the planner's relaxation, as FF's): from a state's facts, each action whose preconditions
/// have all come true adds its effects' facts, layer by layer, and derived flags come true as soon as their inputs can make them. A
/// fact never reached can't come true from that state by any order of actions; the layer a fact is reached at, and the actions
/// behind it, are the planner's estimate of how far off it is.
/// </summary>
public sealed class Relaxation
{
    private readonly QuestModel _model;
    private readonly List<int>[] _requiredBy;
    private readonly int[] _counts;
    private readonly IReadOnlyList<int> _actions;

    /// <summary>A relaxation over some of the model's actions (all, or the planner's relevant ones).</summary>
    public Relaxation(QuestModel model, IEnumerable<int> actions)
    {
        _model = model;
        _actions = [.. actions];
        _requiredBy = new List<int>[model.FactCount];
        _counts = new int[model.Actions.Count];
        foreach (var a in _actions)
        {
            // Its own preconditions, and what the way to its place needs (the closed gates the plan can open).
            var pre = model.Needs(a);
            _counts[a] = pre.Count;
            foreach (var f in pre)
                (_requiredBy[f] ??= []).Add(a);
        }
    }

    /// <summary>The actions it relaxes over.</summary>
    public IReadOnlyList<int> Actions => _actions;

    /// <summary>What came true, when and by what.</summary>
    public sealed class Result
    {
        internal Result(int facts, int actions)
        {
            FactLayer = new int[facts];
            Achiever = new int[facts];
            ActionLayer = new int[actions];
            Array.Fill(FactLayer, -1);
            Array.Fill(Achiever, int.MinValue);
            Array.Fill(ActionLayer, -1);
        }

        /// <summary>The layer each fact came true at, or −1.</summary>
        public int[] FactLayer { get; }

        /// <summary>The first action that made each fact true, or −2 − handle for a derived flag, or <see cref="int.MinValue"/> for one true from the start.</summary>
        public int[] Achiever { get; }

        /// <summary>The layer each action could first be done at, or −1.</summary>
        public int[] ActionLayer { get; }

        internal Dictionary<int, bool[]> Witnesses { get; } = [];

        public bool Reached(int fact) => FactLayer[fact] >= 0;
    }

    /// <summary>
    /// Runs the relaxation from a state until <paramref name="stop"/> says enough, or nothing more comes true. Actions whose places
    /// can't be reached from the state, and actions that end the game, are left out; the opening's only while it's still to play.
    /// </summary>
    public Result Run(PlanState state, Func<Result, bool>? stop = null)
    {
        var model = _model;
        var flags = model.Flags;
        var result = new Result(model.FactCount, model.Actions.Count);
        var counts = (int[])_counts.Clone();
        var initial = model.Facts(state);
        var values = new byte[flags.Count];
        for (var h = 0; h < flags.Count; h++)
            values[h] = state.Value(h);

        var excluded = new bool[model.Actions.Count];
        foreach (var a in _actions)
        {
            var action = model.Actions[a];
            excluded[a] = action.Effects.Any(e => e is EndGame)
                || action.Kind == ActionKind.Event && !state.Opening
                || model.Unplaced(a);
        }

        var current = new List<int>();
        for (var f = 0; f < initial.Length; f++)
        {
            if (initial[f])
            {
                result.FactLayer[f] = 0;
                current.Add(f);
            }
        }
        var ready = new List<int>();
        foreach (var a in _actions)
        {
            if (counts[a] == 0 && !excluded[a])
                ready.Add(a);
        }
        for (var layer = 0; ; layer++)
        {
            foreach (var f in current)
            {
                if (_requiredBy[f] is not { } readers)
                    continue;
                foreach (var a in readers)
                {
                    if (--counts[a] == 0 && !excluded[a])
                        ready.Add(a);
                }
            }
            if (stop?.Invoke(result) == true)
                return result;
            var next = new List<int>();
            foreach (var a in ready)
            {
                result.ActionLayer[a] = layer;
                foreach (var f in model.Adds(a))
                {
                    if (result.FactLayer[f] < 0)
                    {
                        result.FactLayer[f] = layer + 1;
                        result.Achiever[f] = a;
                        next.Add(f);
                    }
                }
            }
            ready.Clear();

            // Derived flags their inputs can now make set or clear.
            var changed = false;
            foreach (var f in next)
            {
                if (f < 2 * flags.Count)
                {
                    var bit = f % 2 == 0 ? FlagModel.CanBeSet : FlagModel.CanBeClear;
                    if ((values[f / 2] & bit) == 0)
                    {
                        values[f / 2] |= bit;
                        changed = true;
                    }
                }
            }
            for (var pass = 0; changed && pass < 16; pass++)
            {
                changed = false;
                foreach (var d in flags.Derived)
                {
                    var value = (byte)(flags.Possible(d, values) | values[d]);
                    if (value == values[d])
                        continue;
                    foreach (var want in new[] { true, false })
                    {
                        var bit = want ? FlagModel.CanBeSet : FlagModel.CanBeClear;
                        if ((value & bit) == 0 || (values[d] & bit) != 0)
                            continue;
                        var fact = model.FlagFact(d, want);
                        if (result.FactLayer[fact] < 0)
                        {
                            result.FactLayer[fact] = layer + 1;
                            result.Achiever[fact] = -2 - d;
                            result.Witnesses[fact] = flags.Witness(d, values, want) ?? [];
                            next.Add(fact);
                        }
                    }
                    values[d] = value;
                    changed = true;
                }
            }

            if (next.Count == 0)
                return result;
            current = next;
        }
    }

    /// <summary>
    /// FF's relaxed plan to the goal facts (all of them, or with <paramref name="any"/> the nearest one): the actions it takes, and those
    /// of them that can be done now, the helpful ones. Null if a goal fact is never reached.
    /// </summary>
    public (HashSet<int> Plan, List<int> Helpful)? Extract(Result result, IReadOnlyList<int> goals, bool any = false)
    {
        var model = _model;
        var chosen = goals.Where(result.Reached).ToList();
        if (any)
            chosen = chosen.OrderBy(g => result.FactLayer[g]).Take(1).ToList();
        else if (chosen.Count < goals.Count)
            return null;
        if (chosen.Count == 0)
            return null;
        var plan = new HashSet<int>();
        var marked = new HashSet<int>();
        var buckets = new SortedDictionary<int, List<int>>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
        void Want(int fact)
        {
            var layer = result.FactLayer[fact];
            if (layer <= 0 || marked.Contains(fact))
                return;
            if (!buckets.TryGetValue(layer, out var list))
                buckets[layer] = list = [];
            list.Add(fact);
        }
        foreach (var g in chosen)
            Want(g);
        while (buckets.Count > 0)
        {
            var (layer, list) = buckets.First();
            buckets.Remove(layer);
            foreach (var fact in list)
            {
                if (!marked.Add(fact))
                    continue;
                var by = result.Achiever[fact];
                if (by >= 0)
                {
                    if (plan.Add(by))
                    {
                        foreach (var p in model.Needs(by))
                            Want(p);
                    }
                }
                else if (by != int.MinValue)
                {
                    var d = -2 - by;
                    var witness = result.Witnesses.GetValueOrDefault(fact) ?? [];
                    var inputs = model.Flags.Inputs(d);
                    for (var i = 0; i < witness.Length && i < inputs.Count; i++)
                        Want(model.FlagFact(inputs[i], witness[i]));
                }
            }
        }
        return (plan, [.. plan.Where(a => result.ActionLayer[a] == 0)]);
    }
}
