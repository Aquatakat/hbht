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
        if (!SpherePhysics.Sweep(world, from, from - new Vector3(0, Reach, 0), 0.5f, skipSeeThrough: true, out var point, out var normal))
        {
            Position.X = centre.X;
            Position.Z = centre.Z;
            return false;
        }
        Position = new Vector3(centre.X, point.Y, centre.Z);
        Normal = normal;
        var share = MathF.Min(1, (Reach - (Vector3.Distance(centre, Position) - radius)) / Reach);
        Size = radius * 2 * share;
        Alpha = (byte)(int)(share * FullAlpha * (float)(alpha / 255.0));
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
    /// player's is drawn farther than <see cref="OthersDistance"/>. The original measures that
    /// second distance from the body's position in its own chunk, not in the loaded world, so for
    /// a body in another chunk it's off by the chunks between them. That isn't ported: bodies
    /// other than the player don't have shadows yet.
    /// </remarks>
    public static byte? DrawAlpha(float drawDistance, float fromAim, bool player, byte bodyAlpha = 0xFF)
    {
        if (fromAim >= drawDistance)
            return null;
        var alpha = drawDistance - 2 < fromAim ? (byte)(int)((drawDistance - fromAim) * 0.5f * 255) : BaseAlpha;
        alpha = Math.Min(alpha, bodyAlpha);
        if (player)
            return alpha;
        if (fromAim >= OthersDistance)
            return null;
        return (byte)(int)(alpha * (1 - fromAim / OthersDistance));
    }

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
