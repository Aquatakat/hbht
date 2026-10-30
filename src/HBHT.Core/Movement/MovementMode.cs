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
public sealed record ModernRules(float FloorUp = 0.45f, float PlainLevelUp = 0.97f, float StepHeight = 0.5f, float SnapDown = 0.5f,
    bool PlainIsWall = true)
{
    public static readonly ModernRules Default = new();

    /// <summary>Whether a triangle of this surface, facing up by <paramref name="up"/>, is ground to stand and walk on.</summary>
    public bool Ground(int surface, float up) => up >= (PlainIsWall && surface is 1 or 2 ? PlainLevelUp : FloorUp);
}
