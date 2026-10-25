using System.Numerics;
using HBHT.Core.Chunks;

namespace HBHT.Core.Physics;

/// <summary>
/// A sphere that moves and collides with the world: an actor's collision (<c>collisionSphere</c>,
/// with the parts of its <c>EnvProp</c> that the game sets). Velocities are per tick.
/// </summary>
public sealed class CollisionSphere
{
    public float Radius { get; init; } = 0.5f;
    public Vector3 Position;
    public Vector3 Velocity;

    /// <summary>Added to the velocity every tick (<c>SpeedProperty.gravity</c>).</summary>
    public Vector3 Gravity;

    /// <summary>
    /// How much velocity it keeps after touching something, times the surface's own share
    /// (<c>collisionTypeA.unk4</c>). Actors keep all of theirs.
    /// </summary>
    public float Keep = 1;

    /// <summary>Whether it passes through see-through triangles (collision sphere flag <c>0x200</c>).</summary>
    public bool IgnoresSeeThrough { get; init; }

    /// <summary>Whether it touched anything on the last step (<c>hits</c>).</summary>
    public bool Touching;

    /// <summary>The normal of the last thing it bounced off (<c>polyNormal</c>).</summary>
    public Vector3 ContactNormal;

    /// <summary>
    /// The surface of the last triangle it touched. The game's trigger callback keeps this for
    /// actors as their ground type, which picks footsteps.
    /// </summary>
    public int Surface;

    /// <summary>
    /// The trigger triangles it touched on the last step, each once: the key of the part each is in,
    /// and the object it runs (<c>check_trigger</c>, which the original's default callback calls).
    /// </summary>
    public HashSet<(ChunkKey Part, int Target)> Trod { get; } = [];
}
