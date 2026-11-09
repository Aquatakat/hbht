using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>
/// Whether the player can get to a place from where the plan stands: a map object (<c>aidyn:mapobject/…</c>) or a chunk
/// (<c>aidyn:chunk/…</c>). The planner asks it of every step's place (<see cref="Reaches"/>); the world graph answers it from the
/// walkable routes and the teleporters' gates, read off the state's flags.
/// </summary>
public interface IPlaceReach
{
    bool CanReach(RecordId place, PlanState state);
}

/// <summary>Says every place can be reached: the planner's default until the world graph answers.</summary>
public sealed class Everywhere : IPlaceReach
{
    public static Everywhere Instance { get; } = new();

    public bool CanReach(RecordId place, PlanState state) => true;
}
