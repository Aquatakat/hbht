using Godot;

namespace HBHT.Views;

/// <summary>The free camera's movement, which the runtime's camera and the editor's map view both fly by.</summary>
public static class FreeFly
{
    /// <summary>
    /// Which way a camera facing by <paramref name="basis"/> goes for the controls held: <paramref name="right"/> along
    /// its X, <paramref name="up"/> straight up whichever way it looks, and <paramref name="back"/> along its Z, each
    /// from −1 to 1.
    /// </summary>
    public static Vector3 Direction(Basis basis, float right, float up, float back) =>
        basis.X * right + Vector3.Up * up + basis.Z * back;
}
