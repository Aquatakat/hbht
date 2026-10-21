using System.Numerics;

namespace Aidyn.Core.Scenes;

/// <summary>
/// A scene transform the game turns to face the camera (Borg5 transform flag 0x200): bushes,
/// grass, glowing orbs and the like, modelled as a flat card facing +Z.
/// </summary>
/// <remarks>
/// When it draws a billboard, <c>BorgAnimDrawSceneRaw</c> throws away the transform's rotation and
/// its parents', along with the scene object's. It keeps only the world position, and the
/// transform's scale times the scene object's. It turns the card with libultra's <c>guAlignF</c>
/// toward the way from the camera to that position. Then it scales it along the world's axes, and
/// moves it there. So a billboard faces the camera's position, not the way it looks, and tilts up
/// and down with it as well as turning. Checked against the debug ROM's machine code.
/// </remarks>
public static class Billboard
{
    /// <summary>
    /// A billboard's world matrix, for row vectors (<c>v' = v × M</c>).
    /// </summary>
    /// <param name="position">The transform's world position.</param>
    /// <param name="scale">The transform's scale times the scene object's (<c>SceneData.scalar</c>).</param>
    /// <param name="roll">The transform's <c>rot.z</c>, in degrees.</param>
    /// <param name="camera">The camera's position.</param>
    public static Matrix4x4 World(Vector3 position, Vector3 scale, float roll, Vector3 camera)
    {
        var m = Align(roll, position - camera) * Matrix4x4.CreateScale(scale);
        m.Translation = position;
        return m;
    }

    /// <summary>
    /// libultra's <c>guAlignF</c>, for row vectors: a rotation that turns +Z to point back along
    /// <paramref name="direction"/> (so a card facing +Z faces where the direction comes from),
    /// keeps +Y as near straight up as it can, and then rolls the card by
    /// <paramref name="degrees"/> about the direction.
    /// </summary>
    /// <remarks>
    /// Along the vertical there's no heading to take, and it gives the identity, as libultra does.
    /// </remarks>
    public static Matrix4x4 Align(float degrees, Vector3 direction)
    {
        var length = direction.Length();
        if (!(length > 0))
            return Matrix4x4.Identity;
        var (x, y, z) = (direction.X / length, direction.Y / length, direction.Z / length);
        var (s, c) = MathF.SinCos(Degrees.ToRadians(degrees));
        var h = MathF.Sqrt(x * x + z * z);
        if (h == 0)
            return Matrix4x4.Identity;
        return new Matrix4x4(
            (-z * c - s * y * x) / h, s * h, (c * x - s * y * z) / h, 0,
            (z * s - c * y * x) / h, c * h, (-s * x - c * y * z) / h, 0,
            -x, -y, -z, 0,
            0, 0, 0, 1);
    }
}
