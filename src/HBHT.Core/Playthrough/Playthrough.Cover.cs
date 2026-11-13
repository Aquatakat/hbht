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
    private (int Step, string Action, int Index, HashSet<int> Live, bool Predicted)? _pending;

    /// <summary>The step the cover goal last chose, and its number: what stranded the run if nothing has a plan after it.</summary>
    private (int Step, string Action, int Index)? _lastChosen;

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
        foreach (var avoid in goal.Avoid)
            _bannedForGood.UnionWith(CoverGoal.Avoided(Model, avoid));
        // A party job keeps the party it makes: no step leaves one of its members out, or brings in one it keeps out but to set a target.
        if (goal.Party.Count > 0)
            _bannedForGood.UnionWith(PartyJobs.Leaving(Model, goal.Party));
        if (goal.Out.Count > 0)
            _bannedForGood.UnionWith(PartyJobs.Joining(Model, goal.Out, goal.Targets));
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
            // A derived flag over what the engine writes can hold in the plan's state while the game hasn't set it, and so can an engine flag
            // itself: it waits.
            if (facts[fact] && Model.Flags.Kind(target.Handle) is FlagKind.Derived or FlagKind.Engine)
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
            var (step, action, index, _, predicted) = _pending!.Value;
            var save = SaveBefore();
            // A loss the check didn't foresee may be a story trigger the step crossed on the way: the branch shuns those it crossed.
            var crossed = !predicted && Played.Count > 0 && Played[^1].Number == step ? Played[^1].Outcome?.Crossed ?? [] : [];
            _cover.Branches.Add(new CoverBranch(step, action, save, [.. lost.Select(t => t.Flag)], predicted) { ActionIndex = index, Crossed = crossed });
            // Lost by the places alone: the relaxation with every place reached still sets them, so where the step left the player walls
            // them off (a way down with none back up, or the world graph's reach from there).
            var stranded = StrandedOf(state, lost);
            foreach (var target in lost)
            {
                target.State = TargetState.Lost;
                target.Why = $"lost at step {step} ({action}){(stranded.Contains(target.Handle) ? " by the places alone" : "")}; " +
                    $"{(save is null ? "no save before it to branch from" : "a branch from the save before it")}";
            }
            Progress?.Invoke($"     step {step} lost {lost.Count} target(s){(predicted ? ", as the loss check said" : "")}" +
                $"{(stranded.Count > 0 ? $", {stranded.Count} by the places alone" : "")}: " +
                $"{string.Join(", ", lost.Take(12).Select(t => FlagIs.Key(t.Flag)))}{(lost.Count > 12 ? ", …" : "")}");
        }
        _pending = null;
        _cover.Looks++;
        _cover.LookSeconds += clock.Elapsed.TotalSeconds;
        return (relaxation, result, live);
    }

    /// <summary>The targets of those lost that the relaxation with every place taken as reached still sets.</summary>
    private HashSet<int> StrandedOf(PlanState state, List<CoverTarget> lost)
    {
        var reach = Model.Reach;
        try
        {
            Model.Reach = Everywhere.Instance;
            var free = new Relaxation(Model, Enumerable.Range(0, Model.Actions.Count)).Run(state);
            return [.. lost.Where(t => free.Reached(Model.FlagFact(t.Handle, true))).Select(t => t.Handle)];
        }
        finally
        {
            Model.Reach = reach;
        }
    }

    /// <summary>
    /// The cover goal's next step (see the class's summary). Once no step nearby sets a target safely, the member goal comes before the
    /// plans, while the party has a slot and its next step loses no target. Empty when no target is live, which ends the run, or when the
    /// targets planned to all found nothing and were struck.
    /// </summary>
    private (Goal Goal, List<PlayerAction> Steps, Plan? Plan) ChooseCover(PlanState state)
    {
        var (relaxation, result, live) = LookAtTargets(state);
        if (_cover!.Goal.HasParty && _cover.PartyAt is null && PartyWanted.Holds(Game.Party.Contains))
        {
            _cover.PartyAt = _step;
            Progress?.Invoke($"     the party is whole{(_step > 0 ? $" after step {_step}" : "")}: {PartyWanted.Describe()}");
        }
        if (live.Count == 0)
        {
            _coverDone = true;
            var waited = WaitForTheClock();
            foreach (var waiting in _cover!.Targets.Where(t => t.State == TargetState.Waits))
            {
                waiting.State = TargetState.GivenUp;
                waiting.Why = Model.Flags.Horizon(waiting.Handle) == 0
                    ? "holds in the planner's state, over the date, the terrain or the journal's flag, which a job doesn't wait for, but the game never set it"
                    : $"holds in the planner's state, over the clock or the weather, but the game didn't set it in {waited / (double)(Calendar.GameClock.Day / Calendar.GameClock.DaySpeed):0.#} game days of waiting";
            }
            return (Goal, [], null);
        }
        // Nothing done for the targets in many steps: whatever the run is doing, it's going round.
        var moved = _cover!.Targets.Count(t => t.State is not TargetState.Live and not TargetState.Waits);
        if (moved != _movedCount)
            (_movedCount, _movedAt) = (moved, _step);
        else if (_step - _movedAt >= MostIdleSteps)
        {
            foreach (var h in live)
            {
                _targets[h].State = TargetState.GivenUp;
                _targets[h].Why = $"no target covered, lost or given up in the {MostIdleSteps} steps up to step {_step}: the run was going round";
            }
            Progress?.Invoke($"     {MostIdleSteps} steps with nothing done for the targets: the {live.Count} live are given up");
            _coverDone = true;
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

        // A party job's party comes next, before anyone else joins or any target is planned to.
        if (ChooseParty(state, relaxation, live) is { } party)
            return party;

        // Nothing to do here, so the run must go further: someone joins first, while the party has a slot and the way loses nothing. Not
        // in a party job that keeps someone out: its targets want the party small, and the loss check can't see what a join closes behind
        // the places. From Oriana's hut with Abrecan and Godric kept out, Brenna and then Rheda joined in Gwernia castle, whose way out
        // only two of Abrecan, Godric and Rheda open, and the questioning outside it (63) that the castle's conversations wait on was
        // walled off.
        if (!_memberResting && _cover!.Goal.Out.Count == 0 && Game.Party.HasEmptySlot())
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
                // The planner's why is kept from the first, so a target given up where the run stood says it.
                var stuck = _reach is { } reach ? Stuck(state, goal, reach) : "";
                target.Why = $"no plan: {plan?.Why?.Split(Environment.NewLine)[0] ?? "the planner found none"}{(stuck.Length > 0 ? $"; {stuck}" : "")}";
                if (target.NoPlans >= _cover.Goal.MostNoPlans)
                    target.State = TargetState.GivenUp;
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
        // Every target planned to found nothing where the run stands.
        if (order.Count == 0)
        {
            // The relaxation still sets them, so it's where the last step left the player (a part of the world graph with no way on):
            // they go to a branch from the save before that step, which avoids it. Only where there's no such step, or no save before it,
            // are they given up.
            var save = _lastChosen is { } last && last.Step == _step && _step > 0 ? SaveBefore() : null;
            var targets = live.Select(h => _targets[h]).ToList();
            if (save is not null)
            {
                _cover!.Branches.Add(new CoverBranch(_step, _lastChosen!.Value.Action, save, [.. targets.Select(t => t.Flag)], false, Stranded: true)
                    { ActionIndex = _lastChosen.Value.Index });
                Progress?.Invoke($"     stranded after step {_step} ({_lastChosen.Value.Action}): no plan to any of {targets.Count} live target(s); " +
                    "a branch from the save before it avoids it");
            }
            foreach (var target in targets)
            {
                target.State = save is null ? TargetState.GivenUp : TargetState.Lost;
                target.Why = save is null
                    ? target.Why ?? "no plan from where the run stood"
                    : $"stranded after step {_step} ({_lastChosen!.Value.Action}): no plan to any target from there; a branch from the save before it avoids it";
            }
            // Nothing is left for this run where it stands: it ends, and the branch goes on.
            if (save is not null)
                _coverDone = true;
        }
        return (Goal, [], null);
    }

    /// <summary>
    /// Once nothing is live, the targets that wait on the clock or the weather (<see cref="FlagModel.Horizon"/>) are waited for where the
    /// player stands in trek, an hour at a time, up to the longest horizon of those still waiting and not seen by another job: a day for the
    /// time of day, four weeks for the moon and the rain. Trek brings the network up to date every tick nothing fades, so the ledger sees a
    /// derived flag as soon as it holds. Wanderers may come while he stands, and the battle pilot plays them. The ticks waited.
    /// </summary>
    private long WaitForTheClock()
    {
        var waiting = _cover!.Targets.Where(t => t.State == TargetState.Waits && Model.Flags.Horizon(t.Handle) > 0).ToList();
        if (waiting.Count == 0)
            return 0;
        var start = Host.Clock.Ticks;
        const long hour = Calendar.GameClock.Hour / Calendar.GameClock.DaySpeed;
        const long day = Calendar.GameClock.Day / Calendar.GameClock.DaySpeed;
        var refresh = start;
        Progress?.Invoke($"     waiting for the clock: {string.Join(", ", waiting.Take(12).Select(t => FlagIs.Key(t.Flag)))}{(waiting.Count > 12 ? ", …" : "")}");
        while (true)
        {
            if (_cover.Goal.CoveredElsewhere is { } elsewhere && Host.Clock.Ticks >= refresh)
            {
                _elsewhere = elsewhere();
                refresh += day;
            }
            TakeSeen();
            foreach (var target in waiting.Where(t => t.State == TargetState.Waits && _elsewhere.Contains(t.Flag)))
            {
                target.State = TargetState.Covered;
                target.Why = "seen by another job";
            }
            waiting.RemoveAll(t => t.State != TargetState.Waits);
            if (waiting.Count == 0 || Host.Clock.Ticks - start >= waiting.Max(t => Model.Flags.Horizon(t.Handle)) / Calendar.GameClock.DaySpeed
                || OutOfBudget() is not null || Host.End != HostEnd.None)
                break;
            var until = Host.Clock.Ticks + hour;
            if (Host.RunUntil(h => h.Clock.Ticks >= until && h.PlayerHasControl, 4 * hour) != HostRun.Done && Host.End != HostEnd.None)
                break;
        }
        var waited = Host.Clock.Ticks - start;
        Progress?.Invoke($"     waited {waited / (double)day:0.#} game days; {waiting.Count} still waiting");
        return waited;
    }

    private (Goal Goal, List<PlayerAction> Steps, Plan? Plan) Pending(Goal goal, List<PlayerAction> steps, Plan? plan, HashSet<int> live, bool predicted)
    {
        _pending = (_step + 1, steps[0].Describe(), steps[0].Index, live, predicted);
        _lastChosen = (_step + 1, steps[0].Describe(), steps[0].Index);
        return (goal, steps, plan);
    }

    /// <summary>
    /// A party job's next step towards its party (<see cref="CoverGoal.Party"/>), while it isn't whole and the run hasn't stopped trying;
    /// else null. A plan whose first step the loss check says loses targets is planned again without that step, up to
    /// <see cref="MostPartyPlans"/> plans, so the party isn't got by a way that loses what it's wanted for. With none that loses nothing, the one that loses fewest is taken all the same: the party is what the
    /// job is for, and what it loses goes to a branch as any step's does. A party the planner finds no plan to is given up on for the run.
    /// </summary>
    private (Goal Goal, List<PlayerAction> Steps, Plan? Plan)? ChooseParty(PlanState state, Relaxation relaxation, HashSet<int> live)
    {
        var cover = _cover!;
        if (!cover.Goal.HasParty || cover.PartyWhy is not null || PartyWanted.Holds(Game.Party.Contains))
            return null;
        var goal = PartyWanted;
        (List<PlayerAction> Steps, Plan? Plan, int Lost)? best = null;
        var without = new HashSet<int>();
        string? why = null;
        for (var tries = 0; tries < MostPartyPlans; tries++)
        {
            var clock = Stopwatch.StartNew();
            var (steps, plan) = tries == 0 ? PlanTo(goal, state) : Replan(goal, state, without);
            cover.Plans++;
            cover.PlanSeconds += clock.Elapsed.TotalSeconds;
            if (steps.Count == 0 || plan is { Found: false })
            {
                why = plan?.Why?.Split(Environment.NewLine)[0] ?? "the planner found none";
                break;
            }
            var lost = Losses(relaxation, state, steps[0], live).Count;
            if (best is null || lost < best.Value.Lost)
                best = (steps, plan, lost);
            if (lost == 0)
                break;
            without.Add(steps[0].Index);
        }
        if (best is not { } chosen)
        {
            StopParty($"no plan: {why}");
            return null;
        }
        if (chosen.Lost > 0)
            Progress?.Invoke($"     towards the party, {chosen.Steps[0].Describe()} loses {chosen.Lost} target(s), the fewest; it's taken all the same");
        return Pending(goal, chosen.Steps, chosen.Plan, live, chosen.Lost > 0);
    }

    /// <summary>
    /// The choices to take in the story triggers a step opens (<see cref="PilotStep.Consequences"/>, <see cref="StoryTriggers.Opened"/>):
    /// each is played by its way that loses the fewest of the targets live before the step and then sets the most, where its ways differ.
    /// The queen's conversation (0x03FB) by the way that wins her trust (231) opens her scene at Overworld G12/4 (0x03FC), which then
    /// plays at once; by the host's default it closes without giving Pandara's amulet (234), which its way by choices 26, 62 and 83
    /// gives, and which the riddle at the Jundar Gate (479) wants long after.
    /// </summary>
    private List<ChoiceWanted> Consequences(PlayerAction action, PlanState state)
    {
        if (_pending is not { } pending || pending.Index != action.Index || pending.Live.Count == 0)
            return [];
        var derived = DerivedInputs(pending.Live);
        var wanted = new List<ChoiceWanted>();
        var notes = new List<string>();
        var relaxation = new Relaxation(Model, Enumerable.Range(0, Model.Actions.Count));
        HashSet<int>? live = null;
        int Worth(PlanState next, PlayerAction play)
        {
            if (live is null)
            {
                var result = relaxation.Run(next);
                live = [.. pending.Live.Where(h => result.Reached(Model.FlagFact(h, true)))];
            }
            return pending.Live.Count(h => Sets(next, play, h, derived)) - 1000 * Losses(relaxation, next, play, live).Count;
        }
        foreach (var (trigger, play) in Triggers.Opened(state, action, Worth))
        {
            wanted.AddRange(StepTranslation.Choices(play, Model.Plugins, notes));
            Progress?.Invoke($"     the step opens {trigger.Id}; if it plays, it goes by {play.Describe()}");
        }
        return wanted;
    }

    /// <summary>Whether an action sets a target that isn't set in the state: a plain one by its effects, a derived one by its formula.</summary>
    private bool Sets(PlanState state, PlayerAction action, int target, HashSet<int> derivedInputs)
    {
        if (Model.Flags.Kind(target) == FlagKind.Plain)
            return !state.Plain(target) && Model.Adds(action.Index).Contains(Model.FlagFact(target, true));
        if (Model.Flags.Kind(target) != FlagKind.Derived || SetFor(state, target)
            || !Model.Adds(action.Index).Any(f => f < 2 * Model.Flags.Count && derivedInputs.Contains(f / 2)))
            return false;
        return SetFor(state.Apply(action), target);
    }

    /// <summary>A party job's party: its members in and those it keeps out.</summary>
    private PartyGoal PartyWanted => new(_cover!.Goal.Party, _cover.Goal.Out);

    /// <summary>The plans to the party tried in one look, each without the first steps of those before it that lost targets.</summary>
    private const int MostPartyPlans = 3;

    /// <summary>A new plan to a goal with some more actions banned, beside the run's own bans.</summary>
    private (List<PlayerAction> Steps, Plan? Plan) Replan(Goal goal, PlanState state, IEnumerable<int> without)
    {
        var banned = Banned(state);
        banned.UnionWith(without);
        var plan = _planner.Plan(state, goal, Options.PlanBudget, banned);
        return (plan.Steps.ToList(), plan);
    }

    /// <summary>The run stops trying to make its party, and sweeps its targets with the party it has.</summary>
    private void StopParty(string why)
    {
        _cover!.PartyWhy = why;
        Progress?.Invoke($"     the party is given up on: {why}");
        Entry(EntryKind.Unreachable, null, PartyWanted.Describe(), $"given up on for the run: {why}", fromStep: false);
    }

    /// <summary>
    /// A step towards the party played out. One meant to bring one of its members in, or take one it keeps out away, that did neither failed
    /// all the same: it's banned where it was played, and after <see cref="MostMemberMisses"/> such running the run stops trying to make
    /// its party.
    /// </summary>
    private void CoverPartyDone(RunStep step, PlanState state)
    {
        var wanted = _cover!.Goal.Party.Where(m => !state.InParty(m)).ToList();
        var away = _cover.Goal.Out.Where(state.InParty).ToList();
        var adds = Model.Adds(step.Action.Index);
        if (!wanted.Any(m => Model.PersonFact(m, true) is { } f && adds.Contains(f)) && !away.Any(m => Model.PersonFact(m, false) is { } f && adds.Contains(f)))
            return;
        if (wanted.Any(Game.Party.Contains) || away.Any(m => !Game.Party.Contains(m)))
        {
            _partyMisses = 0;
            return;
        }
        Ban(step.Action, state);
        if (++_partyMisses >= MostMemberMisses)
            StopParty($"{MostMemberMisses} steps towards it played out with none of it joining, the last {step.Action.Describe()}");
    }

    private int _partyMisses;

    /// <summary>The run stops trying to make its party after steps towards it failed in a row.</summary>
    private void CoverPartyFailed() => StopParty($"{Options.MostFailures} steps towards it failed in a row");

    /// <summary>The loss check (<see cref="LossCheck.Lost"/>), timed.</summary>
    private List<int> Losses(Relaxation relaxation, PlanState state, PlayerAction action, HashSet<int> live)
    {
        var clock = Stopwatch.StartNew();
        var lost = LossCheck.Lost(relaxation, state, action, live);
        // Opening a trapped container and its trap going off each rule the other out. The run opens it, as it would without the trap's
        // flag to see, and the look after the step sends the exploded flag to a branch from the save before it, which sets the trap off
        // once the opened flag is covered.
        if (action.Kind == ActionKind.Open && action.Place is { } place && Springs.TryGetValue(place, out var blown))
            lost.Remove(blown);
        _cover!.LossChecks++;
        _cover.LossSeconds += clock.Elapsed.TotalSeconds;
        return lost;
    }

    /// <summary>Each trapped container's exploded flag, by the container: the flag its <see cref="ActionKind.Spring"/> action sets.</summary>
    private Dictionary<RecordId, int> Springs => _springs ??= Model.Actions.Where(a => a.Kind == ActionKind.Spring && a.Place is not null)
        .SelectMany(a => a.Effects.OfType<SetFlag>().Where(s => s.Value && Model.Flags.TryHandle(s.Flag, out _)).Select(s => (Place: a.Place!.Value, Handle: Model.Flags.Handle(s.Flag))))
        .DistinctBy(s => s.Place).ToDictionary(s => s.Place, s => s.Handle);

    private Dictionary<RecordId, int>? _springs;

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

    /// <summary>How far a place is from the player (<see cref="Distance"/>); a step with no place is no distance.</summary>
    private float Far(RecordId? place, Place? here) => place is { } id ? Distance(PositionOf(Model.Plugins, id), here) : 0;

    /// <summary>
    /// How far a place is from the player, as the crow flies within his map set; another map set's is 10,000 on, since only a teleporter
    /// gets there. No distance where either is unknown.
    /// </summary>
    public static float Distance(Place? at, Place? here)
    {
        if (at is not { } a || here is not { } h)
            return 0;
        return a.MapSet == h.MapSet ? Vector3.Distance(a.Feet, h.Feet) : 10_000 + a.Feet.Length() / 100;
    }

    /// <summary>Where an action's place is: a map object's position, or a chunk's middle; null if it's nowhere.</summary>
    public static Place? PositionOf(PluginSet plugins, RecordId place)
    {
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

    /// <summary>
    /// A step towards a target played out. One whose own effects set it, played out with the target not seen set, failed all the same: it's
    /// banned where it was played and counted against the target, so the run doesn't walk into a tripwire that sets nothing again and
    /// again. Otherwise the target's failures start again.
    /// </summary>
    private void CoverDone(FlagGoal goal, RunStep step, PlanState state)
    {
        if (!_targets.TryGetValue(Model.Flags.Handle(goal.Flag), out var target))
            return;
        var meant = Model.Adds(step.Action.Index).Contains(Model.FlagFact(target.Handle, true));
        if (meant && Host.Ledger?.Seen(target.Handle) == false && !Game.Flags.Get(target.Handle))
        {
            Ban(step.Action, state);
            CoverFailed(goal, step with { Summary = $"{step.Summary}, but the flag wasn't set" });
            return;
        }
        target.Failures = 0;
    }

    /// <summary>
    /// A step towards someone joining played out. One that brought no one in failed all the same: it's banned where it was played, and
    /// after <see cref="MostMemberMisses"/> such running the member goal rests until the next milestone. The companions' scenes at
    /// Overworld H13 and I13 (0x0529 by choice 94, 0x0527 by 110) each played out with no one joining, and a cover run went from one to
    /// the other 3,543 times.
    /// </summary>
    private void CoverMemberDone(RunStep step, PlanState state, bool joined)
    {
        if (joined)
        {
            _memberMisses = 0;
            return;
        }
        Ban(step.Action, state);
        if (++_memberMisses >= MostMemberMisses)
        {
            _memberMisses = 0;
            Rest($"{MostMemberMisses} steps towards it played out with no one joining");
        }
    }

    private int _memberMisses;

    /// <summary>Steps towards someone joining that may play out with no one joining before the member goal rests.</summary>
    private const int MostMemberMisses = 2;

    /// <summary>
    /// Steps a cover run may play with no target covered, lost or given up before it gives the rest up: a guard against any loop that
    /// plays out step after step and does nothing for the targets.
    /// </summary>
    private const int MostIdleSteps = 60;

    /// <summary>The last step at which a target was covered, lost or given up.</summary>
    private int _movedAt;

    private int _movedCount = -1;

    /// <summary>
    /// As the run stops: what the last step saw and lost, looked at once more where the player has the controls in trek. The targets
    /// still live stay so, for whoever runs the job to carry on or give up.
    /// </summary>
    private void FinishCover()
    {
        if (_cover is null)
            return;
        if (!_coverDone && Host.End != HostEnd.None)
        {
            // The game ended in the last step (a battle lost again on its replay): its live targets go to a branch from the save before
            // it, which avoids it, as a stranded run's do.
            TakeSeen();
            var live = _cover.Targets.Where(t => t.State == TargetState.Live).ToList();
            var save = _lastChosen is { } last && last.Step == _step && _step > 0 && live.Count > 0 ? SaveBefore() : null;
            if (save is null)
                return;
            _cover.Branches.Add(new CoverBranch(_step, _lastChosen!.Value.Action, save, [.. live.Select(t => t.Flag)], false, Stranded: true, Ended: true)
                { ActionIndex = _lastChosen.Value.Index });
            foreach (var target in live)
            {
                target.State = TargetState.Lost;
                target.Why = $"the game ended ({Host.End}) in step {_step} ({_lastChosen.Value.Action}); a branch from the save before it avoids it";
            }
            Progress?.Invoke($"     the game ended in step {_step}: {live.Count} live target(s) go to a branch from the save before it, which avoids it");
            return;
        }
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
