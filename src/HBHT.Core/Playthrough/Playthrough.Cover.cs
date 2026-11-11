using System.Diagnostics;
using System.Numerics;
using HBHT.Core.Flags;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Core.Playthrough;

/// <summary>
/// The run's flag coverage (docs/systems/playthrough.md, "Flag coverage"): with a <see cref="CoverGoal"/>, each step it looks at which
/// targets are still live (a relaxation over every action from the real state, with the world graph's places), takes the step nearest
/// the player that sets one and loses none, else plans to the most promising one, and records a branch where a step loses targets.
/// </summary>
public sealed partial class Playthrough
{
    private Coverage? _cover;
    private Dictionary<int, CoverTarget> _targets = [];
    private bool _coverDone;
    private IReadOnlySet<RecordId> _elsewhere = new HashSet<RecordId>();
    private int _refreshedAt;

    /// <summary>The live targets before the step being played, and whether the loss check said it would lose some.</summary>
    private (int Step, string Action, HashSet<int> Live, bool Predicted)? _pending;

    /// <summary>What the cover goal came to: null unless the run's goal is one.</summary>
    public Coverage? Coverage => _cover;

    /// <summary>The most direct steps the loss check weighs before taking the best it found.</summary>
    private const int MostChecks = 60;

    /// <summary>The safe steps whose routes are asked of the world graph, to choose the nearest.</summary>
    private const int MostRouted = 4;

    /// <summary>The plans found in one look, to the most promising targets first, before the step that loses fewest is taken.</summary>
    private const int MostPlanned = 3;

    /// <summary>The plans tried in one look, found or not.</summary>
    private const int MostPlanTries = 12;

    private void StartCover(CoverGoal goal)
    {
        Host.Ledger ??= new FlagLedger(Model.Flags.Count);
        var targets = new List<CoverTarget>();
        foreach (var flag in goal.Targets.Distinct())
        {
            if (Model.Flags.TryHandle(flag, out var h))
                targets.Add(new CoverTarget(flag, h));
        }
        _cover = new Coverage(goal, targets);
        _targets = targets.ToDictionary(t => t.Handle);
        // Set already as the run begins, as a new game's start sets a few before the ledger can watch: covered, seen at step 0.
        foreach (var target in targets.Where(t => Game.Flags.Get(t.Handle)))
        {
            target.State = TargetState.Covered;
            target.Why = "set as the run began";
            _cover.Seen.Add(new CoverSeen(target.Flag, 0, Host.Clock.Ticks, null));
        }
        _elsewhere = goal.CoveredElsewhere?.Invoke() ?? new HashSet<RecordId>();
    }

    /// <summary>The flags first seen since the last look, put down to the step just played (or, before the first, to none).</summary>
    private void TakeSeen()
    {
        if (_cover is null || Host.Ledger is not { } ledger)
            return;
        var fresh = ledger.TakeFresh();
        if (fresh.Count == 0)
            return;
        var save = _step > 0 ? SaveBefore() : null;
        foreach (var h in fresh)
        {
            _cover.Seen.Add(new CoverSeen(Model.Flags.Id(h), _step, Host.Clock.Ticks, save));
            if (_targets.TryGetValue(h, out var target) && target.State != TargetState.Covered)
            {
                target.State = TargetState.Covered;
                target.Step = _step;
                target.Why = _step > 0 ? $"step {_step}" : "before the first step";
            }
        }
    }

    /// <summary>
    /// Where each target stands from the real state, with the places' reach the step will be planned with: covered as the ledger or
    /// another job saw it, live while the relaxation can still set it, waiting while it holds in the plan's state without being set, and
    /// lost when it was live before the last step and isn't now, with a branch from the save before that step.
    /// </summary>
    private (Relaxation Relaxation, Relaxation.Result Result, HashSet<int> Live) LookAtTargets(PlanState state)
    {
        var clock = Stopwatch.StartNew();
        TakeSeen();
        if (_cover!.Goal.CoveredElsewhere is { } elsewhere && _step - _refreshedAt >= _cover.Goal.RefreshSteps)
        {
            _elsewhere = elsewhere();
            _refreshedAt = _step;
        }
        // Over every action, the banned ones too: a ban is the bot's, and a target only a banned step sets is given up for no plan, not lost.
        var relaxation = new Relaxation(Model, Enumerable.Range(0, Model.Actions.Count));
        var result = relaxation.Run(state);
        var facts = Model.Facts(state);
        var live = new HashSet<int>();
        var lost = new List<CoverTarget>();
        foreach (var target in _cover.Targets)
        {
            if (target.State is TargetState.Covered or TargetState.GivenUp)
                continue;
            if (_elsewhere.Contains(target.Flag))
            {
                target.State = TargetState.Covered;
                target.Why = "seen by another job";
                continue;
            }
            var fact = Model.FlagFact(target.Handle, true);
            if (!result.Reached(fact))
            {
                if (target.State == TargetState.Lost)
                    continue;
                if (_pending is { } before && before.Live.Contains(target.Handle))
                {
                    lost.Add(target);
                }
                else if (target.State is TargetState.Live or TargetState.Waits && _step == 0)
                {
                    target.State = TargetState.GivenUp;
                    target.Why = "not reachable from the job's start with the world graph's places";
                }
                else
                {
                    target.State = TargetState.GivenUp;
                    target.Why = $"no longer reachable after step {_step}";
                }
                continue;
            }
            if (target.State == TargetState.Lost)
                Progress?.Invoke($"     target {FlagIs.Key(target.Flag)} is reachable again");
            // A derived flag over what the engine writes can hold in the plan's state while the game hasn't set it: it waits.
            if (facts[fact] && Model.Flags.Kind(target.Handle) == FlagKind.Derived)
            {
                target.State = TargetState.Waits;
                target.Why = "holds in the planner's state, over flags the engine writes, but the game hasn't set it";
                continue;
            }
            target.State = TargetState.Live;
            live.Add(target.Handle);
        }
        if (lost.Count > 0)
        {
            var (step, action, _, predicted) = _pending!.Value;
            var save = SaveBefore();
            _cover.Branches.Add(new CoverBranch(step, action, save, [.. lost.Select(t => t.Flag)], predicted));
            foreach (var target in lost)
            {
                target.State = TargetState.Lost;
                target.Why = $"lost at step {step} ({action}); {(save is null ? "no save before it to branch from" : "a branch from the save before it")}";
            }
            Progress?.Invoke($"     step {step} lost {lost.Count} target(s){(predicted ? ", as the loss check said" : "")}: " +
                $"{string.Join(", ", lost.Take(12).Select(t => FlagIs.Key(t.Flag)))}{(lost.Count > 12 ? ", …" : "")}");
        }
        _pending = null;
        _cover.Looks++;
        _cover.LookSeconds += clock.Elapsed.TotalSeconds;
        return (relaxation, result, live);
    }

    /// <summary>
    /// The cover goal's next step (see the class's summary). Once no step nearby sets a target safely, the member goal comes before the
    /// plans, while the party has a slot and its next step loses no target. Empty when no target is live, which ends the run, or when the
    /// targets planned to all found nothing and were struck.
    /// </summary>
    private (Goal Goal, List<PlayerAction> Steps, Plan? Plan) ChooseCover(PlanState state)
    {
        var (relaxation, result, live) = LookAtTargets(state);
        if (live.Count == 0)
        {
            _coverDone = true;
            foreach (var waiting in _cover!.Targets.Where(t => t.State == TargetState.Waits))
            {
                waiting.State = TargetState.GivenUp;
                waiting.Why = "holds in the planner's state, over flags the engine writes, but the game never set it; the run doesn't wait for them";
            }
            return (Goal, [], null);
        }
        var banned = Banned(state);
        var here = Here();

        // Getting past a story trigger that turned the player back comes first, as in any run.
        if (ChoosePass(state) is { } passing)
            return Pending(passing.Goal, passing.Steps, passing.Plan, live, false);

        // Steps that can be done now and set a live target, nearest first.
        var candidates = new List<(PlayerAction Action, int Target, float Far)>();
        var derived = DerivedInputs(live);
        foreach (var action in Model.Actions)
        {
            if (banned.Contains(action.Index) || !state.CanDo(action) || Gains(state, action, live, derived) is not { } target
                || Unmet(action) is not null)
                continue;
            candidates.Add((action, target, Far(action.Place, here)));
        }
        candidates.Sort((a, b) => a.Far.CompareTo(b.Far));
        var safe = new List<(PlayerAction Action, int Target, float Far)>();
        (Goal Goal, List<PlayerAction> Steps, Plan? Plan, int Lost)? fewest = null;
        var checks = 0;
        foreach (var c in candidates)
        {
            if (checks++ >= MostChecks)
                break;
            var lost = Losses(relaxation, state, c.Action, live);
            if (lost.Count == 0)
            {
                safe.Add(c);
                if (safe.Count >= MostRouted)
                    break;
            }
            else if (fewest is null || lost.Count < fewest.Value.Lost)
            {
                fewest = (new FlagGoal(Model.Flags.Id(c.Target)), [c.Action], null, lost.Count);
            }
        }
        if (safe.Count > 0)
        {
            var best = safe.Count == 1 ? safe[0] : safe.MinBy(c => Routed(c.Action, here, c.Far));
            return Pending(new FlagGoal(Model.Flags.Id(best.Target)), [best.Action], null, live, false);
        }

        // Nothing to do here, so the run must go further: someone joins first, while the party has a slot and the way loses nothing.
        if (!_memberResting && Game.Party.HasEmptySlot())
        {
            var member = new MemberGoal();
            var (steps, plan) = PlanTo(member, state);
            if (steps.Count == 0)
            {
                Rest($"no step towards it: {plan?.Why ?? "the planner found none"}");
            }
            else if (Losses(relaxation, state, steps[0], live) is { Count: > 0 } lost)
            {
                Rest($"its next step, {steps[0].Describe()}, would lose {lost.Count} target(s)");
            }
            else
            {
                return Pending(member, steps, plan, live, false);
            }
        }

        // Nothing to do here: plan to the most promising targets, the nearest in the relaxation first.
        var (found, tries) = (0, 0);
        var order = live.Select(h => _targets[h]).Where(t => t.NoPlanAt != _step)
            .OrderBy(t => result.FactLayer[Model.FlagFact(t.Handle, true)])
            .ThenBy(t => result.Achiever[Model.FlagFact(t.Handle, true)] is var a && a >= 0 ? Far(Model.Actions[a].Place, here) : float.MaxValue)
            .ToList();
        foreach (var target in order)
        {
            if (found >= MostPlanned || tries++ >= MostPlanTries)
                break;
            var goal = new FlagGoal(target.Flag);
            var clock = Stopwatch.StartNew();
            var (steps, plan) = PlanTo(goal, state);
            _cover!.Plans++;
            _cover.PlanSeconds += clock.Elapsed.TotalSeconds;
            if (steps.Count == 0)
            {
                target.NoPlans++;
                target.NoPlanAt = _step;
                if (target.NoPlans >= _cover.Goal.MostNoPlans)
                {
                    target.State = TargetState.GivenUp;
                    target.Why = $"no plan: {plan?.Why?.Split(Environment.NewLine)[0] ?? "the planner found none"}";
                }
                continue;
            }
            if (Unmet(steps[0]) is { } gate)
            {
                // Its next step's code gate isn't met now (a lock the party can't pick): left until the run has moved on.
                target.NoPlanAt = _step;
                target.Why = $"its next step, {steps[0].Describe()}, needs {gate.What}, which the party doesn't meet";
                continue;
            }
            found++;
            var lost = Losses(relaxation, state, steps[0], live);
            if (lost.Count == 0)
                return Pending(goal, steps, plan, live, false);
            if (fewest is null || lost.Count < fewest.Value.Lost)
                fewest = (goal, steps, plan, lost.Count);
        }
        if (fewest is { } least)
        {
            Progress?.Invoke($"     no step loses nothing; the one that loses fewest ({least.Lost}) is taken: {least.Steps[0].Describe()}");
            return Pending(least.Goal, least.Steps, least.Plan, live, true);
        }
        // Every target planned to found nothing where the run stands: those not planned to again since are given up.
        if (order.Count == 0)
        {
            foreach (var h in live)
            {
                var target = _targets[h];
                target.State = TargetState.GivenUp;
                target.Why ??= "no plan from where the run stood";
            }
        }
        return (Goal, [], null);
    }

    private (Goal Goal, List<PlayerAction> Steps, Plan? Plan) Pending(Goal goal, List<PlayerAction> steps, Plan? plan, HashSet<int> live, bool predicted)
    {
        _pending = (_step + 1, steps[0].Describe(), live, predicted);
        return (goal, steps, plan);
    }

    /// <summary>The loss check (<see cref="LossCheck.Lost"/>), timed.</summary>
    private List<int> Losses(Relaxation relaxation, PlanState state, PlayerAction action, HashSet<int> live)
    {
        var clock = Stopwatch.StartNew();
        var lost = LossCheck.Lost(relaxation, state, action, live);
        _cover!.LossChecks++;
        _cover.LossSeconds += clock.Elapsed.TotalSeconds;
        return lost;
    }

    /// <summary>The first of a step's code gates the party doesn't meet now (<see cref="CodeGates.Meets"/>), which keeps it from being played.</summary>
    private CodeGate? Unmet(PlayerAction action) => action.Gates.Distinct().FirstOrDefault(g => CodeGates.Meets(g, Game) == false);

    private bool SetFor(PlanState state, int handle) => LossCheck.SetFor(state, handle);

    /// <summary>The plain flags the live derived targets are made from, through any derived flags between.</summary>
    private HashSet<int> DerivedInputs(HashSet<int> live)
    {
        var inputs = new HashSet<int>();
        var queue = new Queue<int>(live.Where(h => Model.Flags.Kind(h) == FlagKind.Derived));
        var seen = new HashSet<int>(queue);
        while (queue.TryDequeue(out var d))
        {
            foreach (var i in Model.Flags.Inputs(d))
            {
                if (Model.Flags.Kind(i) == FlagKind.Derived)
                {
                    if (seen.Add(i))
                        queue.Enqueue(i);
                }
                else
                {
                    inputs.Add(i);
                }
            }
        }
        return inputs;
    }

    /// <summary>A live target the action sets for certain that isn't set now, the first such; null if none.</summary>
    private int? Gains(PlanState state, PlayerAction action, HashSet<int> live, HashSet<int> derivedInputs)
    {
        var touches = false;
        foreach (var f in Model.Adds(action.Index))
        {
            if (f >= 2 * Model.Flags.Count)
                continue;
            var h = f / 2;
            if (f % 2 == 0 && live.Contains(h) && !state.Plain(h))
                return h;
            touches |= derivedInputs.Contains(h);
        }
        if (!touches)
            return null;
        var next = state.Apply(action);
        foreach (var h in live)
        {
            if (Model.Flags.Kind(h) == FlagKind.Derived && SetFor(next, h) && !SetFor(state, h))
                return h;
        }
        return null;
    }

    /// <summary>
    /// How far a place is from the player, as the crow flies within his map set; another map set's is 10,000 on, since only a teleporter
    /// gets there. A step with no place is no distance.
    /// </summary>
    private float Far(RecordId? place, Place? here)
    {
        if (place is not { } id || here is not { } h || Position(id) is not { } at)
            return 0;
        return at.MapSet == h.MapSet ? Vector3.Distance(at.Feet, h.Feet) : 10_000 + at.Feet.Length() / 100;
    }

    private Place? Position(RecordId place)
    {
        var plugins = Model.Plugins;
        if (place.Type == RecordTypes.Chunk)
        {
            if (plugins.TryGet<ChunkRecord>(place) is not { } chunk || plugins.TryGet<MapSetRecord>(chunk.MapSet) is not { } set)
                return null;
            return new Place(chunk.MapSet, set.Origin(chunk) + new Vector3(set.CellSize / 2, 0, set.CellSize / 2));
        }
        if (plugins.TryGet<ChunkRecord>(ChunkRecord.ChunkOf(place)) is not { } c || c.Object(ChunkRecord.IndexOf(place)) is not { } o
            || plugins.TryGet<MapSetRecord>(c.MapSet) is not { } s)
            return null;
        return new Place(c.MapSet, s.Origin(c) + o.Position);
    }

    /// <summary>The walk to a step's map object by the world graph's route, or its distance as the crow flies and more where there's none.</summary>
    private float Routed(PlayerAction action, Place? here, float far)
    {
        if (here is not { } from || action.Place is not { } place || place.Type != RecordTypes.MapObject)
            return far;
        var clock = Stopwatch.StartNew();
        var route = Graph.Route(Game, from, place);
        _cover!.Routes++;
        _cover.RouteSeconds += clock.Elapsed.TotalSeconds;
        return route.Route is { } r ? r.Length : far + 5_000;
    }

    /// <summary>A step towards a target failed: it's given up on once that's happened <see cref="CoverGoal.MostFailures"/> times running.</summary>
    private void CoverFailed(FlagGoal goal, RunStep step)
    {
        if (!_targets.TryGetValue(Model.Flags.Handle(goal.Flag), out var target) || target.State != TargetState.Live)
            return;
        if (++target.Failures < _cover!.Goal.MostFailures)
            return;
        target.State = TargetState.GivenUp;
        target.Why = $"{target.Failures} steps towards it failed, the last {step.Action.Describe()}: {step.Summary}";
        Progress?.Invoke($"     target {FlagIs.Key(goal.Flag)} given up: {target.Why}");
    }

    private void CoverDone(FlagGoal goal)
    {
        if (_targets.TryGetValue(Model.Flags.Handle(goal.Flag), out var target))
            target.Failures = 0;
    }

    /// <summary>
    /// As the run stops: what the last step saw and lost, looked at once more where the player has the controls in trek. The targets
    /// still live stay so, for whoever runs the job to carry on or give up.
    /// </summary>
    private void FinishCover()
    {
        if (_cover is null)
            return;
        if (_coverDone || Host.End != HostEnd.None || !Host.PlayerHasControl)
        {
            TakeSeen();
            return;
        }
        var state = PlanState.From(Model, Game);
        MakeReach(state);
        LookAtTargets(state);
    }

    /// <summary>
    /// The game as it stands written to <paramref name="path"/>, as the menu's save is, for a job to go on from: only with the player in
    /// trek with the controls. Whether it was.
    /// </summary>
    public bool SaveNow(string path)
    {
        if (Host.End != HostEnd.None || !Host.PlayerHasControl || !Host.Session.NotePlace())
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        SaveFile.Write(path, Save());
        return true;
    }
}
