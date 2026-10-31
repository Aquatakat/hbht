using HBHT.Plugins.Records;

namespace HBHT.Core.Movement;

/// <summary>How the player moves in trek: the Movement setting (docs/systems/movement.md#modern-movement).</summary>
public enum MovementMode
{
    /// <summary>1.0's: moved and collided once a frame, bouncing and sliding as its sphere physics does.</summary>
    Original,

    /// <summary>The port's: a sphere that slides along walls and keeps to the ground, stepped every tick.</summary>
    Modern,
}

/// <summary>
/// What Modern movement takes for ground and wall, and how far it steps up and down (docs/systems/movement.md#modern-movement).
/// </summary>
/// <param name="FloorUp">
/// The least a surface other than plain collision faces up, the Y of its normal, to be ground: 0.45, about 63°. Q25's sand ramp,
/// meant to be climbed, is 0.48, and the Wizard's School's steeper stairs 0.71; at 0.4 the reachability check found him going
/// up F08's grass mound of 0.43, which 1.0 can't.
/// </param>
/// <param name="PlainLevelUp">
/// The least plain collision, surfaces 1 and 2, faces up to be ground: 0.97, about 14°. Steeper, it's a wall, which he slides
/// along and never climbs; at 0.9 the check found him walking along the tops of N27's plain faces.
/// </param>
/// <param name="StepHeight">How high a ledge he steps up onto when a wall stops him: his radius, as 1.0's sphere lifts him over lower.</param>
/// <param name="SnapDown">How far below him he keeps to the ground going down slopes and stairs, rather than leaving it.</param>
/// <param name="PlainIsWall">Whether plain collision is a wall where it's steep. Only a check of the reachability tool turns it off.</param>
/// <param name="WallEdge">
/// The share of his radius walls' edges and corners keep him from as he slips between walls that hold him, where faces and the
/// ground keep him a whole radius away: 1.0's sphere slips past them, through gaps between a wall's end and a post, or two posts,
/// that are narrower than it.
/// </param>
/// <param name="Stride">
/// The ticks his speed is the mean root motion of, or 1 for none: his footfalls jerk him a little, which the mean glides over.
/// </param>
/// <param name="SetOffTurn">
/// How far he turns a tick, in radians, toward the stick as he sets off from standing, before his animation's root first moves
/// him: at 60° he faces any way in the three ticks his run or walk stands still as it starts, and sets off straight.
/// </param>
/// <param name="StopPace">
/// How many times as fast the rest of his step plays once the stick is let go, carrying him as far a tick as it did: the
/// original finishes the step, up to 19 ticks and 1.2 units of his run, before his stop plays in place.
/// </param>
/// <param name="StopReach">The furthest the rest of his step carries him once the stick is let go.</param>
/// <remarks>
/// The last four are the body's, which the party's leader's entity gives him (<see cref="For"/>, <c>EntityBody</c>); the rest are
/// the world's.
/// </remarks>
public sealed record ModernRules(float FloorUp = 0.45f, float PlainLevelUp = 0.97f, float StepHeight = 0.5f, float SnapDown = 0.5f,
    bool PlainIsWall = true, float WallEdge = 0.75f, int Stride = EntityBody.DefaultStride, float SetOffTurn = EntityBody.DefaultSetOffTurn * MathF.PI / 180,
    float StopPace = EntityBody.DefaultStopPace, float StopReach = EntityBody.DefaultStopReach)
{
    public static readonly ModernRules Default = new();

    /// <summary>These rules with a body's own for its steps, or as they are for none.</summary>
    public ModernRules For(EntityBody? body) => body is null ? this : this with
    {
        Stride = body.Stride,
        SetOffTurn = body.SetOffTurn * MathF.PI / 180,
        StopPace = body.StopPace,
        StopReach = body.StopReach,
    };

    /// <summary>Whether a triangle of this surface, facing up by <paramref name="up"/>, is ground to stand and walk on.</summary>
    public bool Ground(int surface, float up) => up >= (PlainIsWall && surface is 1 or 2 ? PlainLevelUp : FloorUp);
}
