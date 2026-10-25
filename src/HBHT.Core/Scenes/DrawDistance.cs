namespace HBHT.Core.Scenes;

/// <summary>
/// The Draw Distance setting's Extended, a port addition, as the runtime draws trek's frame by it
/// (docs/systems/rendering.md#draw-distance). Under it the Overworld's 5×5 is drawn (<see cref="Chunks.SceneryRing"/>),
/// and the planes the fog is measured against move out by <see cref="FarScale"/>, so the fog's start and end, which sit on
/// the depth buffer's scale, move out with them. Thick weather fog pulls them back toward the original's. The frame is cut
/// at a distance from the eye rather than at a far plane, the planes' far, which is where the fog ends.
/// </summary>
/// <remarks>
/// It's only what's drawn: nothing the core plays by reads it (<see cref="SessionSettings"/>), as with
/// <see cref="ObjectDistance"/>.
/// </remarks>
public static class DrawDistance
{
    /// <summary>
    /// How far the planes move out over the 5×5: 300 for the original's 180. The original's far plane is 1.2 times the 3×3's
    /// half-width from a chunk's middle (150), and 300 is the same for the 5×5's (250), so what lies past the loaded chunks
    /// is as deep in the fog as the original's is. The fog clear of the weather starts about 175 units out, where the
    /// original's starts about 105, within the 200 the 5×5 always reaches.
    /// </summary>
    public const float FarScale = 300f / 180;

    /// <summary>The weather's fog at its thickest (docs/systems/weather.md#the-fog), which gives the original's planes back.</summary>
    public const float ThickestFog = 0.75f;

    /// <summary>
    /// How far the planes move out with the weather's fog this thick (<see cref="Sky.SkyDirector.FogTime"/>): all the way
    /// clear of it, and less as it thickens, to none at its thickest. So weather fog still pulls the fog in, to the original's
    /// distances at its thickest, as the 5×5 can't be seen through it.
    /// </summary>
    public static float Scale(float fogTime) => FarScale + (1 - FarScale) * Math.Clamp(fogTime / ThickestFog, 0, 1);

    /// <summary>Trek's planes as Extended draws a frame of the 5×5 with the weather's fog this thick.</summary>
    public static (float Near, float Far) Planes((float Near, float Far) original, float fogTime)
    {
        var scale = Scale(fogTime);
        return (original.Near * scale, original.Far * scale);
    }

    /// <summary>
    /// How far from the eye a point on the depth buffer's 0–1000 scale is for these planes, as the fog measures it: the inverse
    /// of <c>z / w</c>. Trek's 996 is about 105 for 1 and 180.
    /// </summary>
    public static float Distance((float Near, float Far) planes, float onScale)
    {
        var (n, f) = planes;
        var z = (onScale - 500) / 500;
        return 2 * f * n / (f + n - z * (f - n));
    }
}
