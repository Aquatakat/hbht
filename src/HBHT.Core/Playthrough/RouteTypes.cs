using System.Numerics;
using HBHT.Core.Flags;
using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>Where the player stands: a map set and his feet, in the map set's world coordinates (<see cref="Movement.Reach.Cell"/>'s).</summary>
public readonly record struct Place(RecordId MapSet, Vector3 Feet);

/// <summary>What a gate on a route or on an object is made of.</summary>
public enum GateKind
{
    /// <summary>The map object's visibility flag: it isn't there unless the flag holds (<see cref="HBHT.Core.MapObjects.MapObjectPresence"/>).</summary>
    Visible,

    /// <summary>The map object's enabled flag: a teleporter, tripwire, script object, herb source or party works only while it holds.</summary>
    Enabled,

    /// <summary>A lock the teleporter's <c>LockFlag</c> unlocks: picking it (or the key) sets the flag. A tangible teleporter has none in the way.</summary>
    Locked,

    /// <summary>A trap on a teleporter: using it opens the trap menu, which the flag says is disarmed. It never stops the way.</summary>
    Trapped,

    /// <summary>A secret door that using finds the first time (its flag set). It only stops the way where it needs a skill (a level above 0).</summary>
    Secret,

    /// <summary>A use that can happen once: the flag says it's done (a container opened or burst, an encounter beaten), so the object is there while it's clear.</summary>
    Spent,
}

/// <summary>
/// One condition on using a map object or taking a teleporter, kept as data to be read against the <see cref="GameState"/> when a
/// route is asked for, not decided when the graph is made.
/// </summary>
/// <param name="Object">The map object that carries it.</param>
/// <param name="Flag">The flag it reads, or null for a lock with none, which only picking opens.</param>
/// <param name="Invert">Whether the gate holds while the flag is clear.</param>
/// <param name="Level">A lock's, trap's or secret's level, or 0.</param>
/// <param name="Blocking">Whether it stops the way while it doesn't hold. A trap, and a secret door needing no skill, never do.</param>
public sealed record RouteGate(RecordId Object, GateKind Kind, RecordId? Flag, bool Invert, int Level, bool Blocking)
{
    /// <summary>Whether the gate holds: the flag differs from <see cref="Invert"/>. A gate with no flag doesn't.</summary>
    public bool Holds(FlagNetwork flags) => Flag is { } flag && flags.IsSet(flag) != Invert;

    /// <summary>Whether it stops the way now: it blocks, and doesn't hold (a lock holding no matter if <paramref name="pickLocks"/>).</summary>
    public bool Closed(FlagNetwork flags, bool pickLocks = false) => Blocking && !Holds(flags) && !(pickLocks && Kind == GateKind.Locked);

    public override string ToString() => Flag is { } flag
        ? $"{Kind} {Object} ({"needs "}{flag}{(Invert ? " clear" : " set")}{(Level > 0 ? $", level {Level}" : "")})"
        : $"{Kind} {Object} (level {Level}, picked only)";
}

/// <summary>What a route is asked to assume of the player's skills.</summary>
/// <param name="PickLocks">Whether a locked door is taken to be one the party can open, so only its flag-gates stop the way.</param>
/// <param name="Avoid">Places a pilot found itself stuck at: the walk graph's ground within <paramref name="AvoidRadius"/> of each is left out, so a new route goes round.</param>
/// <param name="KeepClear">Zones the route keeps out of, each with its own radius: the ground within it is left out, as for <paramref name="Avoid"/>.</param>
public sealed record RouteOptions(bool PickLocks = false, IReadOnlyList<Place>? Avoid = null, float AvoidRadius = 1.5f,
    IReadOnlyList<RouteZone>? KeepClear = null);

/// <summary>Ground a route keeps out of: the walk graph's cells whose middle is within <paramref name="Radius"/> of <paramref name="Centre"/>, in 3D.</summary>
public readonly record struct RouteZone(Place Centre, float Radius);

/// <summary>Why <see cref="WorldGraph.Route"/> found none.</summary>
public enum RefusalKind
{
    /// <summary>No chunk has the map object.</summary>
    UnknownObject,

    /// <summary>Its type is one the player doesn't use by walking up to it.</summary>
    NotUsable,

    /// <summary>The object's own gates don't hold: it isn't there, or isn't enabled, or is spent.</summary>
    ObjectGated,

    /// <summary>The place to start from isn't on the walk graph of its map set.</summary>
    StartOffGraph,

    /// <summary>A route exists but through gates that don't hold; <see cref="RouteRefusal.Blockers"/> are the ones it crosses.</summary>
    Gated,

    /// <summary>No route even with every gate open: the object is in a part of the world not connected on foot to the start.</summary>
    NotOnFoot,

    /// <summary>The object's map set has no reached ground within its use range.</summary>
    NoStandingPlace,
}

/// <summary>Why there is no route.</summary>
/// <param name="Blockers">The gates that don't hold: the object's own, or on the best route's teleporters (the fewest).</param>
public sealed record RouteRefusal(RefusalKind Kind, string Message, IReadOnlyList<RouteGate> Blockers);

/// <summary>One map set's part of a route.</summary>
/// <param name="Waypoints">
/// Where the pilot steers in turn, in the map set's world coordinates (the middle of a cell, at the height of the feet): the player
/// walks to each in a straight line with the stick, and has passed it within 0.8 of it.
/// </param>
/// <param name="Teleporter">The teleporter map object used at the end of the leg, or null on the last.</param>
/// <param name="Arrives">Where the teleporter puts him in the next leg's map set (its arrival point, in world coordinates).</param>
/// <param name="Toggles">The tripwires whose opened collision the leg walks through (<see cref="Toggle"/>), in order.</param>
public sealed record RouteLeg(RecordId MapSet, IReadOnlyList<Vector3> Waypoints, RecordId? Teleporter, Vector3? Arrives,
    IReadOnlyList<RecordId>? Toggles = null);

/// <summary>A way to the place to stand to use a map object.</summary>
/// <param name="StandAt">Where to stand to use the object (feet, in the last leg's world coordinates).</param>
/// <param name="UseRange">How near the player's sphere (centre half a unit above his feet) must be to the object, by trek's own rule.</param>
/// <param name="Distance">How far the sphere is from the object standing at <see cref="StandAt"/>.</param>
/// <param name="Length">The walking distance, in units, plus a fixed cost for each teleporter.</param>
/// <param name="Gates">Every gate the route and the object pass, with their state against the game it was asked for: why it may not hold later.</param>
public sealed record WorldRoute(RecordId Target, IReadOnlyList<RouteLeg> Legs, Vector3 StandAt, float UseRange, float Distance, float Length,
    IReadOnlyList<RouteGate> Gates)
{
    /// <summary>The teleporters taken, in order.</summary>
    public IEnumerable<RecordId> Teleporters => Legs.Select(l => l.Teleporter).OfType<RecordId>();
}

/// <summary>A route, or why there is none.</summary>
public sealed record RouteResult(WorldRoute? Route, RouteRefusal? Refusal)
{
    public bool Found => Route is not null;
}
