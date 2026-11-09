using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>
/// Whether the player can get to a place from where the plan stands: a map object (<c>aidyn:mapobject/…</c>) or a chunk
/// (<c>aidyn:chunk/…</c>). The planner asks it of every step's place (<see cref="Reaches"/>); the world graph answers it from the
/// walkable routes and the teleporters' gates (<see cref="WorldReach"/>).
/// </summary>
public interface IPlaceReach
{
    /// <summary>Whether the place can be reached in a state of the plan.</summary>
    bool CanReach(RecordId place, PlanState state);

    /// <summary>
    /// What the way to a place needs that the plan can make true: the closed gates' literals on the way with the fewest of them, such
    /// as a locked door's flag set. The relaxation and the planner's backward chain take them as preconditions of the actions there, so
    /// a plan chains back through what opens them (<see cref="QuestModel.Needs"/>), while the search asks <see cref="CanReach"/>. Null
    /// if the place can never be reached, which leaves its actions out of the relaxation too. By default none.
    /// </summary>
    IReadOnlyList<Literal>? Gates(RecordId place) => [];
}

/// <summary>Says every place can be reached: the planner's default until the world graph answers.</summary>
public sealed class Everywhere : IPlaceReach
{
    public static Everywhere Instance { get; } = new();

    public bool CanReach(RecordId place, PlanState state) => true;
}
