using System.Numerics;
using Aidyn.Core.Physics;

namespace Aidyn.Core.Movement;

/// <summary>
/// The round shadow under a body (<c>Actor::CalculateShadow</c>, <c>renderPlayerShadows</c>,
/// <c>PlaneObj_Render</c>): a square of the shadow texture laid on the ground below the body's
/// collision sphere, black, with the texture's intensity as its alpha.
/// </summary>
/// <remarks>
/// <para>
/// Each frame it sweeps a sphere of radius 0.5 from 0.05 above the body's centre to
/// <see cref="Reach"/> below that, skipping see-through triangles. On a hit, it lies on the
/// ground there, directly below the body, turned to the triangle's normal. It's full size, its
/// corners twice the body's radius from its middle, while the ground is within a radius of the
/// body's centre, and it shrinks and fades as the body rises. With no ground in reach, it keeps
/// its last height, normal, size and alpha, and follows the body in X and Z.
/// </para>
/// <para>
/// Its alpha is at most 160 × 128 / 255, about 80 in 255 (<see cref="DrawAlpha"/>). The
/// original also scales it by the screen brightness, which isn't ported.
/// </para>
/// <para>
/// One deliberate difference: the original lies on the first triangle its collision grid lists
/// that the sweep touches, which beside a leaning wall is often the wall, so the shadow stands up
/// on it. This lies on the one the sweep touches soonest of those no steeper than
/// <see cref="Steepest"/>, and treats walls as no ground at all. Over I08, with the body settled
/// by the physics at each point of a half-unit grid, the original's rule tilts the shadow past 45°
/// at 263 of 23,039 points, nearly all against walls, and this at 12.
/// </para>
/// Checked against the 1.0 ROM's machine code.
/// </remarks>
public sealed class BodyShadow
{
    /// <summary>How far below the body's centre it looks for ground.</summary>
    public const float Reach = 3;

    /// <summary>How far from the camera's aim bodies' shadows are drawn in trek and combat (<c>shadowDist</c>).</summary>
    public const float TrekDistance = 20, CombatDistance = 128;

    /// <summary>Other bodies' shadows are drawn only this close to the camera's aim, fading out toward it.</summary>
    public const float OthersDistance = 10;

    /// <summary>The steepest ground it lies on, as the Y of the normal: 60°.</summary>
    public const float Steepest = 0.5f;

    /// <summary>The alpha the draw loop starts from, before fading (<c>renderPlayerShadows</c>).</summary>
    private const byte BaseAlpha = 0x80;

    /// <summary>The alpha at full size, before <see cref="BaseAlpha"/> scales it.</summary>
    private const float FullAlpha = 160;

    /// <summary>The middle of the square, on the ground below the body.</summary>
    public Vector3 Position;

    /// <summary>The normal of the ground it lies on. Zero until it's first placed.</summary>
    public Vector3 Normal;

    /// <summary>How far its corners are from its middle (<c>PlaneObj.scale</c>). Zero until it's first placed.</summary>
    public float Size;

    /// <summary>Its vertices' alpha, which scales the texture's.</summary>
    public byte Alpha;

    /// <summary>Half the width of the square.</summary>
    public float HalfWidth => Size * MathF.Sqrt(0.5f);

    /// <summary>
    /// Places it under a body whose sphere is at <paramref name="centre"/>, for a body of
    /// <paramref name="radius"/> (<c>scaleRad</c>, its radius times its scale), with the alpha
    /// <see cref="DrawAlpha"/> gave. Returns whether it found ground; if not, it only follows in X and Z.
    /// </summary>
    public bool Place(CollisionWorld world, Vector3 centre, float radius, byte alpha)
    {
        var from = centre + new Vector3(0, 0.05f, 0);
        if (!SpherePhysics.SweepNearest(world, from, from - new Vector3(0, Reach, 0), 0.5f, skipSeeThrough: true, Steepest, out var point, out var normal))
        {
            Position.X = centre.X;
            Position.Z = centre.Z;
            return false;
        }
        Position = new Vector3(centre.X, point.Y, centre.Z);
        Normal = normal;
        var share = MathF.Min(1, (Reach - (Vector3.Distance(centre, Position) - radius)) / Reach);
        Size = radius * 2 * share;
        Alpha = ColorBytes.Single(share * FullAlpha * (float)(alpha / 255.0));
        return true;
    }

    /// <summary>
    /// The alpha a body's shadow is drawn with, or null when it isn't drawn
    /// (<c>renderPlayerShadows</c>, <c>PlayerShadowAlpha</c>). <paramref name="fromAim"/> is the
    /// body's distance from the camera's aim, and <paramref name="bodyAlpha"/> the body's own
    /// (<c>shadowAlpha</c>), which a dying body fades out.
    /// </summary>
    /// <remarks>
    /// Shadows fade out over the last 2 units of <paramref name="drawDistance"/>. Only the
    /// player's is drawn farther than <see cref="OthersDistance"/>, and others fade toward it.
    /// </remarks>
    /// <param name="ownAim">
    /// The distance the original measures for that second test: from the body's position in its own
    /// chunk's coordinates to the aim in the player's chunk's, which for a body in another chunk is
    /// off by the chunks between them. Without it, <paramref name="fromAim"/>.
    /// </param>
    public static byte? DrawAlpha(float drawDistance, float fromAim, bool player, byte bodyAlpha = 0xFF, float? ownAim = null)
    {
        if (fromAim >= drawDistance)
            return null;
        var alpha = Math.Min(Fade(drawDistance, fromAim, BaseAlpha), bodyAlpha);
        if (player)
            return alpha;
        var own = ownAim ?? fromAim;
        if (own >= OthersDistance)
            return null;
        return ColorBytes.Single(alpha * (1 - own / OthersDistance));
    }

    /// <summary>
    /// The alpha a body is drawn with, or null when it isn't drawn (<c>renderPlayers</c>,
    /// <c>PlayerShadowAlpha</c>): bodies show within <paramref name="drawDistance"/> of the camera's
    /// aim, fading out over its last 2 units.
    /// </summary>
    public static byte? BodyAlpha(float drawDistance, float fromAim, byte bodyAlpha = 0xFF) =>
        fromAim >= drawDistance ? null : Math.Min(Fade(drawDistance, fromAim, 0xFF), bodyAlpha);

    /// <summary>
    /// The alpha at <paramref name="fromAim"/> within <paramref name="drawDistance"/>: <paramref name="full"/>
    /// until the last 2 units, then falling to 0 over them, from 255 whatever <paramref name="full"/> is
    /// (<c>PlayerShadowAlpha</c>).
    /// </summary>
    private static byte Fade(float drawDistance, float fromAim, byte full) =>
        drawDistance - 2 < fromAim ? ColorBytes.Single((drawDistance - fromAim) * 0.5f * 255f) : full;

    /// <summary>
    /// The camera's aim as <see cref="DrawAlpha"/>'s <c>ownAim</c> takes it: in the coordinates of the chunk the
    /// player stands in, whose origin is <paramref name="playerChunkOrigin"/>, or as it is where he stands in none.
    /// </summary>
    public static Vector3 ChunkAim(Vector3 aim, Vector3? playerChunkOrigin) => playerChunkOrigin is { } origin ? aim - origin : aim;

    /// <summary>
    /// A body's distance from <paramref name="ownAim"/> (<see cref="ChunkAim"/>) as the original measures it for
    /// <see cref="DrawAlpha"/>'s second test: from its position in its own chunk's coordinates, the chunk whose
    /// origin is <paramref name="chunkOrigin"/>.
    /// </summary>
    public static float OwnDistance(Vector3 position, Vector3 chunkOrigin, Vector3 ownAim) =>
        Vector3.Distance(position - chunkOrigin, ownAim);

    /// <summary>
    /// The directions the texture's S and T run across the ground: the plane's X and Y axes after
    /// <c>guAlign</c> turns its Z axis to face into the ground, with no spin. The original adds
    /// 10⁻⁶ to the normal's X so level ground has a direction to turn from, which sends S along +Z.
    /// </summary>
    public (Vector3 S, Vector3 T) Axes()
    {
        var n = Normal with { X = Normal.X + SpherePhysics.Epsilon };
        n = Vector3.Normalize(n);
        var h = MathF.Sqrt(n.X * n.X + n.Z * n.Z);
        return (new Vector3(-n.Z / h, 0, n.X / h), new Vector3(-n.Y * n.X / h, h, -n.Y * n.Z / h));
    }
}
