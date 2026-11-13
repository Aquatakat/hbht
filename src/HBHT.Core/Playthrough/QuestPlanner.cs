namespace HBHT.Core.Playthrough;

/// <summary>A plan: the steps to a goal from a state, or why there are none.</summary>
/// <param name="Steps">The actions in order. Each one's preconditions hold after the ones before it.</param>
/// <param name="Why">Why the goal can't be reached, or why the search gave up, when it wasn't.</param>
/// <param name="Missing">What the goal's nearest setters need that never comes true, when it can't be reached.</param>
/// <param name="Relevant">How many actions the backward chain from the goal found to matter.</param>
/// <param name="Expanded">How many states the search looked at.</param>
public sealed record Plan(Goal Goal, bool Found, IReadOnlyList<PlayerAction> Steps, string? Why, IReadOnlyList<string> Missing,
    int Relevant, int Expanded);

/// <summary>
/// The playthrough's planner (layer 1): the next steps from a state to a goal. It chains back from the goal through what sets it to
/// the actions that matter (their preconditions' setters, and theirs, to the start), then searches forward over only those: FF's
/// enforced hill-climbing, each state judged by the relaxed plan's length from it (<see cref="Relaxation"/>), its helpful actions
/// tried first, falling back to a best-first search where hill-climbing stalls. The plan is then cut down to the steps it needs.
/// </summary>
public sealed class QuestPlanner
{
    private readonly QuestModel _model;
    private readonly List<int>[] _adders;

    public QuestPlanner(QuestModel model)
    {
        _model = model;
        _adders = new List<int>[model.FactCount];
        foreach (var action in model.Actions)
        {
            foreach (var f in model.Adds(action.Index))
                (_adders[f] ??= []).Add(action.Index);
        }
    }

    public QuestModel Model => _model;

    /// <summary>The story triggers, for <see cref="PassGoal"/>s; none if null.</summary>
    public StoryTriggers? Triggers { get; set; }

    /// <summary>The actions whose effects make a fact true.</summary>
    public IReadOnlyList<int> Adders(int fact) => _adders[fact] ?? (IReadOnlyList<int>)[];

    /// <summary>Whether a goal is met by any one of its facts rather than all of them.</summary>
    public static bool AnyFact(Goal goal) => goal is MemberGoal;

    /// <summary>Whether a state meets the goal, against who could join as the plan began.</summary>
    public bool Achieved(PlanState state, Goal goal, IReadOnlyCollection<int> goalFacts) => goal switch
    {
        FlagGoal f => state.IsSet(f.Flag),
        MemberGoal => goalFacts.Any(f => _model.Facts(state)[f]),
        EndingGoal => state.Credits,
        PassGoal p => Triggers?.Get(p.Trigger) is not { } t || !Triggers.Blocks(t, state),
        FactsGoal g => _model.Facts(state) is var facts && g.Facts.All(f => facts[f]),
        PartyGoal p => p.Holds(state.InParty),
        _ => false,
    };

    /// <summary>
    /// The facts that meet the goal: all of them, or for a member any one. A <see cref="PassGoal"/> is planned as each of its ways in turn
    /// (<see cref="StoryTriggers.PassOptions"/>), and has none of its own.
    /// </summary>
    public IReadOnlyList<int> GoalFacts(PlanState state, Goal goal) => goal switch
    {
        FlagGoal f when _model.Flags.TryHandle(f.Flag, out var h) => [_model.FlagFact(h, true)],
        MemberGoal => [.. _model.Members
            .Where(m => !state.InParty(m.Key) && !state.IsSet(m.Value.Died))
            .Select(m => _model.PersonFact(m.Key, true)).OfType<int>()],
        EndingGoal => [_model.CreditsFact],
        FactsGoal g => g.Facts,
        PartyGoal p => [.. p.Members.Select(m => _model.PersonFact(m, true)).Concat(p.Without.Select(m => _model.PersonFact(m, false))).OfType<int>()],
        _ => [],
    };

    /// <summary>
    /// The steps that get the player past a story trigger: the shortest plan found to any of its ways (<see cref="StoryTriggers.PassOptions"/>),
    /// never walking into it the way that turns him back. A partial plan isn't one: no way found, no steps.
    /// </summary>
    private Plan PlanPass(PlanState state, PassGoal pass, int budget, IReadOnlySet<int>? banned)
    {
        if (Triggers?.Get(pass.Trigger) is not { } trigger)
            return new Plan(pass, false, [], $"{pass.Trigger} isn't a story trigger.", [], 0, 0);
        if (Achieved(state, pass, []))
            return new Plan(pass, true, [], null, [], 0, 0);
        var ban = new HashSet<int>([.. banned ?? new HashSet<int>(), .. Triggers.BlockingPlays(trigger)]);
        Plan? best = null;
        var whys = new List<string>();
        var (relevant, expanded) = (0, 0);
        foreach (var facts in Triggers.PassOptions(trigger, state))
        {
            var what = string.Join(" and ", facts.Select(_model.DescribeFact));
            var plan = Plan(state, new FactsGoal(facts, what), budget, ban);
            (relevant, expanded) = (relevant + plan.Relevant, expanded + plan.Expanded);
            if (plan.Found && (best is null || plan.Steps.Count < best.Steps.Count))
                best = plan;
            else if (!plan.Found)
                whys.Add($"{what}: {plan.Why?.Split(Environment.NewLine)[0]}");
        }
        return best is not null
            ? best with { Goal = pass, Relevant = relevant, Expanded = expanded }
            : new Plan(pass, false, [], whys.Count > 0 ? string.Join("; ", whys) : "Nothing the model has lets the player past it.", [], relevant, expanded);
    }

    /// <summary>The actions that matter to the goal: what makes its facts true, what makes theirs true, and so on back.</summary>
    public HashSet<int> Relevant(IEnumerable<int> goalFacts)
    {
        var flags = _model.Flags;
        var facts = new HashSet<int>();
        var actions = new HashSet<int>();
        var queue = new Queue<int>();
        void Want(int fact)
        {
            if (facts.Add(fact))
                queue.Enqueue(fact);
        }
        void Take(int a)
        {
            if (!actions.Add(a))
                return;
            foreach (var p in _model.Needs(a))
                Want(p);
            // A trigger that starts a script object's cut-scene makes its ways the next step.
            foreach (var start in _model.Actions[a].Effects.OfType<StartScript>())
            {
                foreach (var forced in _model.Actions.Where(x => x.Kind == ActionKind.Play && x.Place == start.MapObject))
                    Take(forced.Index);
            }
        }
        foreach (var g in goalFacts)
            Want(g);
        foreach (var opening in _model.Opening)
            Take(opening.Index);
        while (queue.TryDequeue(out var fact))
        {
            if (fact < 2 * flags.Count && flags.Kind(fact / 2) == FlagKind.Derived)
            {
                foreach (var input in flags.Inputs(fact / 2))
                {
                    Want(_model.FlagFact(input, true));
                    Want(_model.FlagFact(input, false));
                }
            }
            foreach (var a in Adders(fact))
                Take(a);
        }
        return actions;
    }

    /// <summary>The actions that can be done in a state, of those given.</summary>
    public IEnumerable<PlayerAction> Applicable(PlanState state, IEnumerable<int> actions) =>
        actions.Select(a => _model.Actions[a]).Where(state.CanDo);

    /// <summary>The next steps from a state to a goal.</summary>
    /// <param name="budget">How many states the search may look at before it gives up.</param>
    /// <param name="banned">Actions the plan mustn't take: the run's, for steps that failed where it stands.</param>
    public Plan Plan(PlanState state, Goal goal, int budget = 20_000, IReadOnlySet<int>? banned = null)
    {
        if (goal is PassGoal pass)
            return PlanPass(state, pass, budget, banned);
        var goalFacts = GoalFacts(state, goal);
        if (Achieved(state, goal, goalFacts))
            return new Plan(goal, true, [], null, [], 0, 0);
        if (goalFacts.Count == 0)
            return new Plan(goal, false, [], goal switch
            {
                MemberGoal => "No one living is left to join.",
                PassGoal => "Nothing the model has lets the player past it.",
                PartyGoal => "None of them is someone the model follows into the party.",
                _ => "No such flag.",
            }, [], 0, 0);
        var any = AnyFact(goal);

        // Can it be reached at all, even with nothing undone?
        var everything = new Relaxation(_model, Enumerable.Range(0, _model.Actions.Count).Where(a => banned?.Contains(a) != true));
        var full = everything.Run(state);
        if (!(any ? goalFacts.Any(full.Reached) : goalFacts.All(full.Reached)))
        {
            var missing = new List<string>();
            var why = Explain(full, goalFacts.Where(f => !full.Reached(f)).ToList(), missing, state);
            return new Plan(goal, false, [], why, missing, 0, 0);
        }

        var relevant = Relevant(goalFacts);
        if (banned is not null)
            relevant.ExceptWith(banned);
        var relaxation = new Relaxation(_model, relevant);
        var search = new Search(this, relaxation, relevant, goal, goalFacts, any, budget);
        var steps = search.Run(state);
        if (steps is null)
        {
            return new Plan(goal, false, search.Partial, $"The search gave up after {search.Expanded} states" +
                (search.Stuck is { } stuck ? $": {stuck}" : "."), [], relevant.Count, search.Expanded);
        }
        return new Plan(goal, true, Shorten(state, steps, goal, goalFacts), null, [], relevant.Count, search.Expanded);
    }

    /// <summary>Whether a plan's steps can each be done in turn from a state, and reach the goal.</summary>
    public bool Valid(PlanState state, IEnumerable<PlayerAction> steps, Goal goal, IReadOnlyCollection<int> goalFacts)
    {
        foreach (var step in steps)
        {
            if (!state.CanDo(step))
                return false;
            state = state.Apply(step);
        }
        return Achieved(state, goal, goalFacts);
    }

    /// <summary>Leaves out every step the plan still works without, from the last back.</summary>
    private List<PlayerAction> Shorten(PlanState state, List<PlayerAction> steps, Goal goal, IReadOnlyCollection<int> goalFacts)
    {
        for (var i = steps.Count - 2; i >= 0; i--)
        {
            var without = steps.Take(i).Concat(steps.Skip(i + 1)).ToList();
            if (Valid(state, without, goal, goalFacts))
                steps = without;
        }
        return steps;
    }

    /// <summary>
    /// Why facts never come true: for each, what sets it and the first of that setter's preconditions that never does, followed back a
    /// few steps.
    /// </summary>
    private string Explain(Relaxation.Result result, List<int> facts, List<string> missing, PlanState state)
    {
        var lines = new List<string>();
        var explained = new HashSet<int>();
        void Walk(int fact, int depth)
        {
            if (!explained.Add(fact) || depth > 4)
                return;
            var indent = new string(' ', 2 * depth);
            var adders = Adders(fact);
            var flags = _model.Flags;
            if (fact < 2 * flags.Count && flags.Kind(fact / 2) == FlagKind.Derived)
            {
                lines.Add($"{indent}{_model.DescribeFact(fact)}: derived, and its inputs never make it so");
                return;
            }
            if (adders.Count == 0)
            {
                lines.Add($"{indent}{_model.DescribeFact(fact)}: nothing the model has does it");
                if (depth == 0)
                    missing.Add(_model.DescribeFact(fact));
                return;
            }
            lines.Add($"{indent}{_model.DescribeFact(fact)}: {adders.Count} action(s) do it, none can be done");
            foreach (var a in adders.Take(3))
            {
                var action = _model.Actions[a];
                var blocker = _model.Needs(a).FirstOrDefault(p => !result.Reached(p), -1);
                if (blocker >= 0)
                {
                    lines.Add($"{indent}  {action.Describe()} needs {_model.DescribeFact(blocker)}");
                    if (depth == 0)
                        missing.Add(_model.DescribeFact(blocker));
                    Walk(blocker, depth + 2);
                }
                else
                {
                    var why = action.Effects.Any(e => e is EndGame) ? "ends the game"
                        : action.Presence.OfType<Reaches>().FirstOrDefault(r => !_model.Reach.CanReach(r.Place, state)) is { } far
                            ? $"can't reach {far.Place}" : "can't be done";
                    lines.Add($"{indent}  {action.Describe()} {why}");
                    if (depth == 0)
                        missing.Add(why);
                }
            }
        }
        foreach (var f in facts)
            Walk(f, 0);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>One search: enforced hill-climbing on the relaxed plan's length, with a best-first search where it stalls.</summary>
    private sealed class Search(QuestPlanner planner, Relaxation relaxation, HashSet<int> relevant, Goal goal, IReadOnlyList<int> goalFacts,
        bool any, int budget)
    {
        private const int Infinite = int.MaxValue;
        private readonly Dictionary<PlanState, (int H, List<int> Helpful)> _heuristic = [];

        public int Expanded { get; private set; }

        public List<PlayerAction> Partial { get; private set; } = [];

        public string? Stuck { get; private set; }

        public List<PlayerAction>? Run(PlanState start)
        {
            var current = start;
            var plan = new List<PlayerAction>();
            while (!planner.Achieved(current, goal, goalFacts))
            {
                var (h, _) = Evaluate(current);
                if (h == Infinite)
                {
                    Stuck = "the goal can't be reached from where the plan had got to";
                    Partial = plan;
                    return null;
                }
                var path = Climb(current, h, helpfulOnly: true) ?? Climb(current, h, helpfulOnly: false) ?? BestFirst(current);
                if (path is null)
                {
                    Partial = plan;
                    Stuck ??= "no state nearer the goal could be found";
                    return null;
                }
                foreach (var step in path)
                    current = current.Apply(step);
                plan.AddRange(path);
                if (Expanded > budget)
                {
                    Partial = plan;
                    return null;
                }
            }
            return plan;
        }

        /// <summary>The relaxed plan's length from a state (∞ where the goal can't be reached), and its helpful actions.</summary>
        private (int H, List<int> Helpful) Evaluate(PlanState state)
        {
            if (_heuristic.TryGetValue(state, out var known))
                return known;
            if (planner.Achieved(state, goal, goalFacts))
                return _heuristic[state] = (0, []);
            var result = relaxation.Run(state, r => any ? goalFacts.Any(r.Reached) : goalFacts.All(r.Reached));
            var extracted = relaxation.Extract(result, goalFacts, any);
            var value = extracted is { } e ? (Math.Max(1, e.Plan.Count), e.Helpful) : (Infinite, []);
            _heuristic[state] = value;
            return value;
        }

        private IEnumerable<PlayerAction> Successors(PlanState state, bool helpfulOnly)
        {
            if (state.Opening || state.Forced is not null)
                return planner.Applicable(state, relevant);
            var actions = helpfulOnly ? Evaluate(state).Helpful : (IEnumerable<int>)relevant;
            return planner.Applicable(state, actions);
        }

        /// <summary>A breadth-first search from a state for one strictly nearer the goal.</summary>
        private List<PlayerAction>? Climb(PlanState start, int h, bool helpfulOnly)
        {
            var seen = new HashSet<PlanState> { start };
            var queue = new Queue<(PlanState State, List<PlayerAction> Path)>();
            queue.Enqueue((start, []));
            var limit = helpfulOnly ? 400 : 1500;
            var looked = 0;
            while (queue.TryDequeue(out var node))
            {
                if (++looked > limit)
                    return null;
                Expanded++;
                foreach (var action in Successors(node.State, helpfulOnly))
                {
                    var next = node.State.Apply(action);
                    if (!seen.Add(next))
                        continue;
                    var path = new List<PlayerAction>(node.Path) { action };
                    var (hn, _) = Evaluate(next);
                    if (hn < h)
                        return path;
                    if (hn != Infinite)
                        queue.Enqueue((next, path));
                }
            }
            return null;
        }

        /// <summary>A greedy best-first search to the goal, for where hill-climbing finds no way down.</summary>
        private List<PlayerAction>? BestFirst(PlanState start)
        {
            var seen = new HashSet<PlanState> { start };
            var open = new PriorityQueue<(PlanState State, List<PlayerAction> Path), (int, int)>();
            open.Enqueue((start, []), (Evaluate(start).H, 0));
            var looked = 0;
            while (open.TryDequeue(out var node, out _))
            {
                if (planner.Achieved(node.State, goal, goalFacts))
                    return node.Path;
                if (++looked > 5000 || Expanded > budget)
                    return null;
                Expanded++;
                foreach (var action in Successors(node.State, helpfulOnly: false))
                {
                    var next = node.State.Apply(action);
                    if (!seen.Add(next))
                        continue;
                    var (hn, _) = Evaluate(next);
                    if (hn != Infinite)
                        open.Enqueue((next, [.. node.Path, action]), (hn, node.Path.Count + 1));
                }
            }
            return null;
        }
    }
}
