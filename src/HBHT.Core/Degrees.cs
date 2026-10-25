namespace HBHT.Core;

/// <summary>Angles in degrees, where the original's data and the port's maths differ in their units.</summary>
public static class Degrees
{
    /// <summary>A number of degrees as radians: the sky's heights and azimuths, the cameras' pitch and orbit, billboards' turns.</summary>
    public static float ToRadians(float degrees) => degrees * (MathF.PI / 180);
}
