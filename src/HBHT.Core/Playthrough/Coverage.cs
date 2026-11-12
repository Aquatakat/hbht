using System.Text.Json.Serialization;
using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>Where a cover run's target stands.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TargetState>))]
public enum TargetState
{
    /// <summary>Not seen set, and the relaxation from where the run stands can still set it.</summary>
    Live,
    /// <summary>Seen set, in this run or by another job.</summary>
    Covered,
    /// <summary>Its fact holds in the planner's state, as a derived flag over the flags the engine writes can, but the game hasn't set it.</summary>
    Waits,
    /// <summary>A step lost it: a branch from the save before that step is recorded for it.</summary>
    Lost,
    /// <summary>Given up on: no plan, the pilot's failures, or never reachable from the run's start.</summary>
    GivenUp,
}

/// <summary>One of a cover run's targets: where it stands, and why.</summary>
public sealed class CoverTarget(RecordId flag, int handle)
{
    public RecordId Flag { get; } = flag;

    public int Handle { get; } = handle;

    public TargetState State { get; internal set; }

    /// <summary>Why it's lost, given up on or waiting; for a covered one, by what.</summary>
    public string? Why { get; internal set; }

    /// <summary>The step it was seen set in, 0 for one another job saw.</summary>
    public int Step { get; internal set; }

    /// <summary>The steps towards it that failed since one did what it should.</summary>
    internal int Failures { get; set; }

    /// <summary>The plans to it that found nothing, and the step the last one was at.</summary>
    internal int NoPlans { get; set; }

    internal int NoPlanAt { get; set; } = -1;

    public override string ToString() => $"{FlagIs.Key(Flag)} {State}{(Why is { } why ? $": {why}" : "")}";
}

/// <summary>A flag a cover run saw set first: the step, the tick, and the save from before the step, to play it again from.</summary>
public sealed record CoverSeen(RecordId Flag, int Step, long Tick, string? Save);

/// <summary>
/// A step that lost targets not yet covered: the save from before it, from which a branch job plays for them, the step and why.
/// </summary>
/// <param name="Predicted">The loss check said before the step that it would lose these, and no safe step was left.</param>
/// <param name="Stranded">
/// The step left the player where no target has a plan, though the relaxation still sets them: the branch plays from before it with
/// the step avoided (<see cref="CoverGoal.Avoid"/>).
/// </param>
/// <param name="Ended">The game ended in the step (a battle lost again on its replay): stranded too, the branch avoiding it.</param>
public sealed record CoverBranch(int Step, string Action, string? Save, IReadOnlyList<RecordId> Targets, bool Predicted, bool Stranded = false,
    bool Ended = false)
{
    /// <summary>The step's action (<see cref="PlayerAction.Index"/>), which the branch avoids by <see cref="CoverGoal.Avoid"/>.</summary>
    public int? ActionIndex { get; init; }

    /// <summary>The story triggers the step crossed, other than its own, which the branch shuns (<see cref="CoverGoal.Shun"/>).</summary>
    public IReadOnlyList<RecordId> Crossed { get; init; } = [];

    /// <summary>How the branch's job names the step it avoids (<see cref="CoverGoal.Avoid"/>).</summary>
    public string Avoid => ActionIndex is { } i ? $"#{i} {Action}" : Action;
}

/// <summary>What a cover run (<see cref="CoverGoal"/>) came to, and how it spent its time.</summary>
public sealed class Coverage
{
    internal Coverage(CoverGoal goal, IEnumerable<CoverTarget> targets)
    {
        Goal = goal;
        Targets = [.. targets];
    }

    public CoverGoal Goal { get; }

    public IReadOnlyList<CoverTarget> Targets { get; }

    /// <summary>Every flag first seen set in the run, targets or not, in order.</summary>
    public List<CoverSeen> Seen { get; } = [];

    public List<CoverBranch> Branches { get; } = [];

    /// <summary>How many steps the loss check weighed, and the seconds it took.</summary>
    public int LossChecks { get; internal set; }

    public double LossSeconds { get; internal set; }

    /// <summary>The relaxations of where the targets stand, one before each step, and their seconds.</summary>
    public int Looks { get; internal set; }

    public double LookSeconds { get; internal set; }

    /// <summary>How many plans to one target were made, and their seconds.</summary>
    public int Plans { get; internal set; }

    public double PlanSeconds { get; internal set; }

    /// <summary>Routes asked of the world graph to choose the nearest step, and their seconds.</summary>
    public int Routes { get; internal set; }

    public double RouteSeconds { get; internal set; }

    /// <summary>For a goal with a <see cref="CoverGoal.Party"/>, the step the party was whole after (0 as the run began); null while it isn't.</summary>
    public int? PartyAt { get; internal set; }

    /// <summary>Why the run stopped trying to make its party, if it did.</summary>
    public string? PartyWhy { get; internal set; }

    public int Count(TargetState state) => Targets.Count(t => t.State == state);

    public override string ToString() =>
        $"{Count(TargetState.Covered)} covered, {Count(TargetState.Live)} live, {Count(TargetState.Waits)} waiting, {Count(TargetState.Lost)} lost " +
        $"({Branches.Count} branches), {Count(TargetState.GivenUp)} given up, of {Targets.Count}";
}

/// <summary>
/// The cover run's loss check: which live targets a step would lose, by the relaxation from the state after it. What the relaxation
/// can't set after the step and can now (the live targets are those it can), less what the step sets itself, can't be set by any order
/// of steps after it.
/// </summary>
public static class LossCheck
{
    /// <param name="relaxation">A relaxation over the actions the targets may be set by: every action, for the run.</param>
    /// <param name="live">The targets' handles the relaxation from <paramref name="state"/> sets.</param>
    public static List<int> Lost(Relaxation relaxation, PlanState state, PlayerAction action, IEnumerable<int> live)
    {
        var model = state.Model;
        var next = state.Apply(action);
        var result = relaxation.Run(next);
        var lost = new List<int>();
        foreach (var h in live)
        {
            if (!result.Reached(model.FlagFact(h, true)) && !SetFor(next, h))
                lost.Add(h);
        }
        return lost;
    }

    /// <summary>Whether a flag is set in a plan's state for certain: a plain one set, a derived one whose inputs can make it nothing else.</summary>
    public static bool SetFor(PlanState state, int handle) => state.Model.Flags.Kind(handle) switch
    {
        FlagKind.Plain => state.Plain(handle),
        FlagKind.Derived => state.Value(handle) == FlagModel.CanBeSet,
        _ => false,
    };
}
